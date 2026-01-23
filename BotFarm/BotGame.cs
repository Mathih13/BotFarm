using BotFarm.Properties;
using Client;
using Client.UI;
using Client.World;
using Client.World.Network;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Client.World.Definitions;
using Client.World.Entities;
using DetourCLI;
using MapCLI;
using DBCStoresCLI;
using BotFarm.AI;
using Client.AI.Tasks;
using BotFarm.AI.Tasks;
using Client.World.Items;

namespace BotFarm
{
    public class BotGame : AutomatedGame
    {
        // Static counter for round-robin class distribution
        private static int classDistributionCounter = 0;
        private static readonly object classDistributionLock = new object();
        private static readonly Class[] availableClasses = { Class.Warrior, Class.Priest, Class.Paladin };

        public BotBehaviorSettings Behavior
        {
            get;
            private set;
        }

        private TaskExecutorAI currentRouteExecutor = null;

        // Track if we've already created a fresh character this session
        private bool hasCreatedFreshCharacter = false;

        // Harness settings for test framework
        private HarnessSettings harnessSettings = null;
        private string assignedClass = null;
        private Race assignedRace = Race.Human;
        private int harnessIndex = 0;

        /// <summary>
        /// The harness settings assigned to this bot, if any
        /// </summary>
        public HarnessSettings HarnessSettings => harnessSettings;

        /// <summary>
        /// The index of this bot within the harness (0-based)
        /// </summary>
        public int HarnessIndex => harnessIndex;

        /// <summary>
        /// Indicates whether the last MoveTo call succeeded in finding a path.
        /// Use this to detect unreachable targets.
        /// </summary>
        public bool LastMoveSucceeded { get; private set; } = true;

        #region Player members
        DateTime CorpseReclaim;
        public ulong TraderGUID
        {
            get;
            private set;
        }
        public HashSet<ulong> TradedGUIDs
        {
            get;
            private set;
        } = new HashSet<ulong>();
        #endregion

        public BotGame(string hostname, int port, string username, string password, int realmId, int character, BotBehaviorSettings behavior)
            : base(hostname, port, username, password, realmId, character)
        {
            this.Behavior = behavior;

            #region AutoResurrect
            if (Behavior.AutoResurrect)
            {
                // Resurrect if bot reaches 0 hp
                AddTrigger(new Trigger(new[] 
                { 
                    new UpdateFieldTriggerAction((int)UnitField.UNIT_FIELD_HEALTH, 0)
                }, () =>
                   {
                       CancelActionsByFlag(ActionFlag.Movement);
                       Resurrect();
                   }));

                // Resurrect sequence
                AddTrigger(new Trigger(new TriggerAction[] 
                { 
                    new UpdateFieldTriggerAction((int)PlayerField.PLAYER_FLAGS, (uint)PlayerFlags.PLAYER_FLAGS_GHOST, () =>
                        {
                            OutPacket corpseQuery = new OutPacket(WorldCommand.MSG_CORPSE_QUERY);
                            SendPacket(corpseQuery);
                        }),
                    new OpcodeTriggerAction(WorldCommand.MSG_CORPSE_QUERY, packet =>
                    {
                        var inPacket = packet as InPacket;
                        if (inPacket == null)
                            return false;

                        bool found = inPacket.ReadByte() != 0;
                        if (found)
                        {
                            var mapId = inPacket.ReadInt32();

                            var corpsePosition = new Position(inPacket.ReadSingle(),
                                                              inPacket.ReadSingle(),
                                                              inPacket.ReadSingle(),
                                                              0.0f,
                                                              inPacket.ReadInt32());
                            Player.CorpsePosition = corpsePosition.GetPosition();

                            if (mapId == corpsePosition.MapID)
                            {
                                MoveTo(corpsePosition);
                                return true;
                            }
                        }

                        return false;
                    }),
                    new CustomTriggerAction(TriggerActionType.DestinationReached, (inputs) =>
                    {
                        if (Player.IsGhost && (Player.CorpsePosition - Player).Length <= 39f)
                        {
                            if (DateTime.Now > CorpseReclaim)
                                return true;
                            else
                                ScheduleAction(() => HandleTriggerInput(TriggerActionType.DestinationReached, inputs), CorpseReclaim.AddSeconds(1));
                        }

                        return false;
                    },() => 
                      {
                          OutPacket reclaimCorpse = new OutPacket(WorldCommand.CMSG_RECLAIM_CORPSE);
                          reclaimCorpse.Write(Player.GUID);
                          SendPacket(reclaimCorpse);
                      })
                }, null));
            }
            #endregion
        }

        public override void Start()
        {
            base.Start();

            // Anti-kick for being afk
            ScheduleAction(() => DoTextEmote(TextEmote.Yawn), DateTime.Now.AddMinutes(5), new TimeSpan(0, 5, 0));
            ScheduleAction(() =>
            {
                if (LoggedIn)
                    SendPacket(new OutPacket(WorldCommand.CMSG_KEEP_ALIVE));
            }, DateTime.Now.AddSeconds(15), new TimeSpan(0, 0, 30));

            #region Begger
            if (Behavior.Begger)
            {
                PushStrategicAI(new BeggerAI());
            }
            #endregion

            #region TestMove
#if DEBUG
            // TestMoveAI is only available in debug builds (file is gitignored)
            // if (Behavior.TestMove)
            // {
            //     PushStrategicAI(new TestMoveAI());
            // }
#endif
            #endregion

            #region FollowGroupLeader
            if (Behavior.FollowGroupLeader)
            {
                PushStrategicAI(new FollowGroupLeaderAI());
            }
            #endregion

            #region Explorer
            if (Behavior.Explorer)
            {
                AchievementExploreLocation targetLocation = null;
                List<AchievementExploreLocation> missingLocations = null;
                Position currentPosition = new Position();

                ScheduleAction(() =>
                {
                    if (!Player.IsAlive)
                        return;

                    if (targetLocation != null)
                    {
                        if (!HasExploreCriteria(targetLocation.CriteriaID) && (currentPosition - Player).Length > MovementEpsilon)
                        {
                            currentPosition = Player.GetPosition();
                            return;
                        }

                        targetLocation = null;
                    }

                    currentPosition = Player.GetPosition();

                    if (missingLocations == null)
                        missingLocations = DBCStores.GetAchievementExploreLocations(Player.X, Player.Y, Player.Z, Player.MapID);

                    missingLocations = missingLocations.Where(loc => !HasExploreCriteria(loc.CriteriaID)).ToList();
                    if (missingLocations.Count == 0)
                    {
                        CancelActionsByFlag(ActionFlag.Movement);
                        return;
                    }

                    float closestDistance = float.MaxValue;
                    var playerPosition = new Point(Player.X, Player.Y, Player.Z);
                    foreach (var missingLoc in missingLocations)
                    {
                        float distance = (missingLoc.Location - playerPosition).Length;
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            targetLocation = missingLoc;
                        }
                    }

                    MoveTo(new Position(targetLocation.Location.X, targetLocation.Location.Y, targetLocation.Location.Z, 0f, Player.MapID));
                }, DateTime.Now.AddSeconds(30), new TimeSpan(0, 0, 5));
            }
            #endregion
        }

        public override void NoCharactersFound()
        {
            CreateRandomCharacter();
        }

        public override void PresentCharacterList(Character[] characterList)
        {
            // If we've already created a fresh character this session, log in normally
            if (hasCreatedFreshCharacter)
            {
                Log($"Logging in with freshly created character: {characterList[0].Name}");
                base.PresentCharacterList(characterList);
                return;
            }

            // Delete all existing characters before creating a new one
            Log($"Found {characterList.Length} existing character(s), deleting all to create fresh");

            // Queue all characters for deletion
            foreach (var character in characterList)
            {
                PendingCharacterDeletions.Enqueue(character.GUID);
            }

            // Start the deletion process
            IsDeletingCharacters = true;
            if (PendingCharacterDeletions.Count > 0)
            {
                var firstGuid = PendingCharacterDeletions.Dequeue();
                DeleteCharacter(firstGuid);
            }
        }

        protected override void OnAllCharactersDeleted()
        {
            Log("All characters deleted, creating new character");
            CreateRandomCharacter();
        }

        /// <summary>
        /// Set harness settings for this bot (used by test framework)
        /// </summary>
        /// <param name="settings">The harness settings from the route</param>
        /// <param name="botIndex">The index of this bot within the harness (0-based)</param>
        public void SetHarnessSettings(HarnessSettings settings, int botIndex)
        {
            this.harnessSettings = settings;
            this.harnessIndex = botIndex;

            if (settings.Classes != null && settings.Classes.Count > 0)
            {
                this.assignedClass = settings.Classes[botIndex % settings.Classes.Count];
            }
            else
            {
                this.assignedClass = "Warrior";
            }

            if (!string.IsNullOrEmpty(settings.Race))
            {
                if (Enum.TryParse<Race>(settings.Race, true, out var race))
                {
                    this.assignedRace = race;
                }
            }

            Log($"Harness settings applied: Class={assignedClass}, Race={assignedRace}, Index={botIndex}");
        }

        /// <summary>
        /// Apply harness setup using GM commands (requires GM level 2).
        /// Called after bot is logged in to set level, add items, complete quests, and teleport.
        /// </summary>
        public void ApplyHarnessSetup()
        {
            if (harnessSettings == null)
            {
                Log("No harness settings to apply", LogLevel.Warning);
                return;
            }

            Log($"Applying harness setup via GM commands...");

            // Level up (relative, so level 1->10 needs .levelup 9)
            if (harnessSettings.Level > 1)
            {
                int levelsToAdd = harnessSettings.Level - 1;
                Log($"Leveling up by {levelsToAdd} levels (to level {harnessSettings.Level})");
                LevelUp(levelsToAdd);
            }

            // Add items
            if (harnessSettings.Items != null && harnessSettings.Items.Count > 0)
            {
                foreach (var item in harnessSettings.Items)
                {
                    Log($"Adding item {item.Entry} x{item.Count}");
                    AddItem(item.Entry, item.Count);
                }
            }

            // Complete prerequisite quests (add, complete objectives, then reward to fully finish)
            if (harnessSettings.CompletedQuests != null && harnessSettings.CompletedQuests.Count > 0)
            {
                foreach (var questId in harnessSettings.CompletedQuests)
                {
                    Log($"Adding quest {questId} to log...");
                    AddQuest(questId);
                    // Wait for server to process the add before completing
                    System.Threading.Thread.Sleep(500);
                    Log($"Completing quest {questId} objectives...");
                    CompleteQuest(questId);
                    System.Threading.Thread.Sleep(500);
                    Log($"Rewarding quest {questId}...");
                    RewardQuest(questId);
                    // Wait for completion to process before next quest
                    System.Threading.Thread.Sleep(500);
                }
            }

            // Apply equipment sets
            ApplyEquipmentSets();

            // Teleport to start position
            if (harnessSettings.StartPosition != null)
            {
                Log($"Teleporting to start position: ({harnessSettings.StartPosition.X}, {harnessSettings.StartPosition.Y}, {harnessSettings.StartPosition.Z}) on map {harnessSettings.StartPosition.MapId}");
                TeleportToPosition(
                    harnessSettings.StartPosition.X,
                    harnessSettings.StartPosition.Y,
                    harnessSettings.StartPosition.Z,
                    harnessSettings.StartPosition.MapId);
            }

            Log("Harness setup complete");
        }

        /// <summary>
        /// Apply equipment sets from harness settings.
        /// First checks for class-specific override, then falls back to generic equipment sets.
        /// </summary>
        private void ApplyEquipmentSets()
        {
            if (harnessSettings == null)
                return;

            var appliedSets = new List<string>();

            // 1. Check for class-specific equipment set override
            if (harnessSettings.ClassEquipmentSets != null &&
                harnessSettings.ClassEquipmentSets.Count > 0 &&
                !string.IsNullOrEmpty(assignedClass))
            {
                if (harnessSettings.ClassEquipmentSets.TryGetValue(assignedClass, out var classSetName))
                {
                    var classSet = EquipmentSetLoader.LoadByName(classSetName);
                    if (classSet != null)
                    {
                        ApplyEquipmentSetItems(classSet);
                        appliedSets.Add(classSetName);
                    }
                    else
                    {
                        Log($"Class equipment set '{classSetName}' not found", LogLevel.Warning);
                    }
                }
            }

            // 2. Apply generic equipment sets (filtered by class restriction)
            if (harnessSettings.EquipmentSets != null && harnessSettings.EquipmentSets.Count > 0)
            {
                foreach (var setName in harnessSettings.EquipmentSets)
                {
                    // Skip if we already applied a class-specific set with this name
                    if (appliedSets.Contains(setName))
                        continue;

                    var set = EquipmentSetLoader.LoadByName(setName);
                    if (set == null)
                    {
                        Log($"Equipment set '{setName}' not found", LogLevel.Warning);
                        continue;
                    }

                    // Check class restriction
                    if (!string.IsNullOrEmpty(set.ClassRestriction) &&
                        !string.IsNullOrEmpty(assignedClass) &&
                        !string.Equals(set.ClassRestriction, assignedClass, StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"Skipping equipment set '{setName}' - class restriction '{set.ClassRestriction}' doesn't match bot class '{assignedClass}'");
                        continue;
                    }

                    ApplyEquipmentSetItems(set);
                    appliedSets.Add(setName);
                }
            }

            if (appliedSets.Count > 0)
            {
                Log($"Applied equipment sets: {string.Join(", ", appliedSets)}");
            }
        }

        /// <summary>
        /// Add all items from an equipment set to the bot's inventory and equip them
        /// </summary>
        private void ApplyEquipmentSetItems(EquipmentSet set)
        {
            if (set?.Items == null || set.Items.Count == 0)
                return;

            Log($"Applying equipment set '{set.Name}' ({set.Items.Count} items)");

            // Track which item entries we need to equip
            var entriesToEquip = new HashSet<uint>();

            foreach (var item in set.Items)
            {
                Log($"  Adding item {item.Entry} x{item.Count}");
                AddItem(item.Entry, item.Count);

                if (item.Equip)
                    entriesToEquip.Add(item.Entry);

                System.Threading.Thread.Sleep(100);
            }

            if (entriesToEquip.Count == 0)
                return;

            // Wait for items to appear in inventory
            System.Threading.Thread.Sleep(1500);

            // Build list of items to equip with their bag slots
            var itemsToEquip = new List<(byte slot, uint entry, string name)>();

            Log($"  Scanning backpack for {entriesToEquip.Count} items to equip");

            int packSlotBase = (int)PlayerField.PLAYER_FIELD_PACK_SLOT_1;

            for (int slot = 0; slot < 16; slot++)
            {
                uint guidLow = Player[packSlotBase + slot * 2];
                uint guidHigh = Player[packSlotBase + slot * 2 + 1];
                ulong itemGuid = ((ulong)guidHigh << 32) | guidLow;

                if (itemGuid == 0)
                    continue;

                if (!Objects.TryGetValue(itemGuid, out var itemObject))
                    continue;

                uint entry = itemObject.Entry;
                if (!entriesToEquip.Contains(entry))
                    continue;

                // Query template if needed
                var template = ItemCache.Get(entry);
                if (template == null)
                {
                    QueryItem(entry);
                    for (int i = 0; i < 20 && ItemCache.Get(entry) == null; i++)
                        System.Threading.Thread.Sleep(100);
                    template = ItemCache.Get(entry);
                }

                if (template == null || !template.IsEquippableGear || template.GetEquipmentSlot() < 0)
                    continue;

                itemsToEquip.Add(((byte)slot, entry, template.Name));
                entriesToEquip.Remove(entry);
            }

            if (itemsToEquip.Count == 0)
            {
                Log($"  No equippable items found in backpack");
                return;
            }

            Log($"  Found {itemsToEquip.Count} items to equip");

            foreach (var (bagSlot, entry, name) in itemsToEquip)
            {
                // Get the target equipment slot from the item template
                var template = ItemCache.Get(entry);
                if (template == null)
                    continue;

                int equipSlot = template.GetEquipmentSlot();
                if (equipSlot < 0)
                    continue;

                // Check if equipment slot is currently empty
                int visibleField = (int)PlayerField.PLAYER_VISIBLE_ITEM_1_ENTRYID + equipSlot * 2;
                uint currentEquipped = Player[visibleField];
                bool slotIsEmpty = (currentEquipped == 0);

                byte dstSlot = (byte)equipSlot;
                byte srcSlot = (byte)(23 + bagSlot);  // Backpack starts at inventory slot 23
                string status = slotIsEmpty ? "empty" : $"replacing {currentEquipped}";
                Log($"    Equipping {name}: SwapItem dst=(255,{dstSlot}) src=(255,{srcSlot}) ({status})");

                // CMSG_SWAP_ITEM uses container:slot pairs
                // Format: dstBag, dstSlot, srcBag, srcSlot
                // With container 255 (player inventory):
                //   Slots 0-18 = equipment slots
                //   Slots 23-38 = backpack slots
                var packet = new OutPacket(WorldCommand.CMSG_SWAP_ITEM);
                packet.Write((byte)255);     // dstBag - player inventory
                packet.Write(dstSlot);       // dstSlot - equipment slot (0-18)
                packet.Write((byte)255);     // srcBag - player inventory
                packet.Write(srcSlot);       // srcSlot - backpack slot (23-38)
                SendPacket(packet);

                System.Threading.Thread.Sleep(500);
            }

            // Wait for server to finalize all swaps
            System.Threading.Thread.Sleep(1000);

            // Verification
            Log($"  DEBUG: === VERIFICATION - Equipment slots after equip ===");
            for (int eqSlot = 0; eqSlot < 19; eqSlot++)
            {
                int visibleField = (int)PlayerField.PLAYER_VISIBLE_ITEM_1_ENTRYID + eqSlot * 2;
                uint equippedEntry = Player[visibleField];
                if (equippedEntry > 0)
                    Log($"  DEBUG: Equipment slot {eqSlot} now has entry {equippedEntry}");
            }
        }

        private void CreateRandomCharacter()
        {
            Class classChoice;
            Race raceChoice;

            // Use harness settings if available
            if (harnessSettings != null && !string.IsNullOrEmpty(assignedClass))
            {
                if (Enum.TryParse<Class>(assignedClass, true, out classChoice))
                {
                    raceChoice = assignedRace;
                    Log($"Creating new {classChoice} character (race: {raceChoice}) from harness settings");
                }
                else
                {
                    Log($"Invalid class in harness settings: {assignedClass}, falling back to default", LogLevel.Warning);
                    classChoice = Class.Warrior;
                    raceChoice = Race.Human;
                }
            }
            else
            {
                // Round-robin distribution among available classes (default behavior)
                lock (classDistributionLock)
                {
                    classChoice = availableClasses[classDistributionCounter % availableClasses.Length];
                    classDistributionCounter++;
                }
                raceChoice = Race.Human;
                Log($"Creating new {classChoice} character");
            }

            hasCreatedFreshCharacter = true;
            CreateCharacter(raceChoice, classChoice);
        }

        public override void CharacterCreationFailed(CommandDetail result)
        {
            // Note: Name collisions are now mitigated by position-based digit encoding in CreateCharacter()
            Log($"Bot {Username} failed creating a character with error {result.ToString()}", LogLevel.Error);
        }

        public override void InvalidCredentials()
        {
            BotFactory.Instance.RemoveBot(this);
        }

        public WorldObject FindClosestNonBotPlayer(Func<WorldObject, bool> additionalCheck = null)
        {
            return FindClosestObject(HighGuid.Player, obj =>
            {
                if (BotFactory.Instance.IsBot(obj))
                    return false;
                if (additionalCheck != null && !additionalCheck(obj))
                    return false;
                return true;
            });
        }

        #region Handlers
        [PacketHandler(WorldCommand.SMSG_GROUP_INVITE)]
        protected void HandlePartyInvite(InPacket packet)
        {
            if(Behavior.AutoAcceptGroupInvites)
                SendPacket(new OutPacket(WorldCommand.CMSG_GROUP_ACCEPT, 4));
        }

        [PacketHandler(WorldCommand.SMSG_RESURRECT_REQUEST)]
        protected void HandleResurrectRequest(InPacket packet)
        {
            var resurrectorGuid = packet.ReadUInt64();
            OutPacket response = new OutPacket(WorldCommand.CMSG_RESURRECT_RESPONSE);
            response.Write(resurrectorGuid);
            if (Behavior.AutoAcceptResurrectRequests)
            {
                response.Write((byte)1);
                SendPacket(response);

                OutPacket result = new OutPacket(WorldCommand.MSG_MOVE_TELEPORT_ACK);
                result.WritePacketGuid(Player.GUID);
                result.Write((UInt32)0);
                result.Write(DateTime.Now.Millisecond);
                SendPacket(result);
            }
            else
            {
                response.Write((byte)0);
                SendPacket(response);
            }
        }

        [PacketHandler(WorldCommand.SMSG_CORPSE_RECLAIM_DELAY)]
        protected void HandleCorpseReclaimDelay(InPacket packet)
        {
            CorpseReclaim = DateTime.Now.AddMilliseconds(packet.ReadUInt32());
        }

        [PacketHandler(WorldCommand.SMSG_TRADE_STATUS)]
        protected void HandleTradeStatus(InPacket packet)
        {
            if (Behavior.Begger)
            {
                TradeStatus status = (TradeStatus)packet.ReadUInt32();
                switch (status)
                {
                    case TradeStatus.BeginTrade:
                        TraderGUID = packet.ReadUInt64();
                        // Stop moving
                        CancelActionsByFlag(ActionFlag.Movement);
                        // Accept trade
                        OutPacket beginTrade = new OutPacket(WorldCommand.CMSG_BEGIN_TRADE);
                        SendPacket(beginTrade);
                        break;
                    case TradeStatus.Canceled:
                        EnableActionsByFlag(ActionFlag.Movement);
                        TraderGUID = 0;
                        break;
                    case TradeStatus.Accept:
                        OutPacket acceptTrade = new OutPacket(WorldCommand.CMSG_ACCEPT_TRADE);
                        SendPacket(acceptTrade);
                        break;
                    case TradeStatus.Tradecomplete:
                        DoSayChat("Thank you!");
                        EnableActionsByFlag(ActionFlag.Movement);
                        HandleTriggerInput(TriggerActionType.TradeCompleted, TraderGUID);
                        TraderGUID = 0;
                        break;
                }
            }
        }
        #endregion

        #region Actions
        public void MoveTo(Position destination)
        {
            CancelActionsByFlag(ActionFlag.Movement, false);

            if (destination.MapID != Player.MapID)
            {
                Log("Trying to move to another map", Client.UI.LogLevel.Warning);
                HandleTriggerInput(TriggerActionType.DestinationReached, false);
                return;
            }

            Path path = null;
            using(var detour = new DetourCLI.Detour())
            {
                List<MapCLI.Point> resultPath;

                // Correct Z-coordinates using terrain height - Detour requires accurate heights
                float startZ = MapCLI.Map.GetHeight(Player.X, Player.Y, Player.Z, Player.MapID);
                float endZ = MapCLI.Map.GetHeight(destination.X, destination.Y, destination.Z, Player.MapID);

                // Check if GetHeight returned valid values
                if (float.IsNaN(startZ) || float.IsInfinity(startZ) || startZ < -10000)
                {
                    startZ = Player.Z;
                }
                if (float.IsNaN(endZ) || float.IsInfinity(endZ) || endZ < -10000)
                {
                    endZ = destination.Z;
                }

                var pathResult = detour.FindPath(Player.X, Player.Y, startZ,
                                        destination.X, destination.Y, endZ,
                                        Player.MapID, out resultPath);
                if (pathResult != PathType.Complete)
                {
                    Log($"Cannot reach destination, FindPath() returned {pathResult} : {destination.ToString()}", Client.UI.LogLevel.Warning);
                    HandleTriggerInput(TriggerActionType.DestinationReached, false);
                    LastMoveSucceeded = false;
                    return;
                }

                path = new Path(resultPath, Player.Speed, Player.MapID);
                LastMoveSucceeded = true;
                var destinationPoint = path.Destination;
                destination.SetPosition(destinationPoint.X, destinationPoint.Y, destinationPoint.Z);
            }

            var remaining = destination - Player.GetPosition();
            // check if we even need to move
            if (remaining.Length < MovementEpsilon)
            {
                HandleTriggerInput(TriggerActionType.DestinationReached, true);
                return;
            }

            var direction = remaining.Direction;

            var facing = new MovementPacket(WorldCommand.MSG_MOVE_SET_FACING)
            {
                GUID = Player.GUID,
                flags = MovementFlags.MOVEMENTFLAG_FORWARD,
                X = Player.X,
                Y = Player.Y,
                Z = Player.Z,
                O = path.CurrentOrientation
            };

            SendPacket(facing);
            Player.SetPosition(facing.GetPosition());

            var startMoving = new MovementPacket(WorldCommand.MSG_MOVE_START_FORWARD)
            {
                GUID = Player.GUID,
                flags = MovementFlags.MOVEMENTFLAG_FORWARD,
                X = Player.X,
                Y = Player.Y,
                Z = Player.Z,
                O = path.CurrentOrientation
            };
            SendPacket(startMoving);

            var previousMovingTime = DateTime.Now;

            var oldRemaining = remaining;
            ScheduleAction(() =>
            {
                Point progressPosition = path.MoveAlongPath((float)(DateTime.Now - previousMovingTime).TotalSeconds);
                Player.SetPosition(progressPosition.X, progressPosition.Y, progressPosition.Z);
                previousMovingTime = DateTime.Now;

                remaining = destination - Player.GetPosition();

                if (remaining.Length > MovementEpsilon)
                {
                    oldRemaining = remaining;

                    var heartbeat = new MovementPacket(WorldCommand.MSG_MOVE_HEARTBEAT)
                    {
                        GUID = Player.GUID,
                        flags = MovementFlags.MOVEMENTFLAG_FORWARD,
                        X = Player.X,
                        Y = Player.Y,
                        Z = Player.Z,
                        O = path.CurrentOrientation
                    };
                    SendPacket(heartbeat);
                }
                else
                {
                    var stopMoving = new MovementPacket(WorldCommand.MSG_MOVE_STOP)
                    {
                        GUID = Player.GUID,
                        flags = MovementFlags.MOVEMENTFLAG_NONE,
                        X = Player.X,
                        Y = Player.Y,
                        Z = Player.Z,
                        O = path.CurrentOrientation
                    };
                    SendPacket(stopMoving);
                    
                    Player.SetPosition(stopMoving.GetPosition());

                    CancelActionsByFlag(ActionFlag.Movement, false);

                    HandleTriggerInput(TriggerActionType.DestinationReached, true);
                }
            }, GetMovementInterval(), ActionFlag.Movement,
            () =>
            {
                var stopMoving = new MovementPacket(WorldCommand.MSG_MOVE_STOP)
                {
                    GUID = Player.GUID,
                    flags = MovementFlags.MOVEMENTFLAG_NONE,
                    X = Player.X,
                    Y = Player.Y,
                    Z = Player.Z,
                    O = path.CurrentOrientation
                };
                SendPacket(stopMoving);
            });
        }

        public void Resurrect()
        {
            OutPacket repop = new OutPacket(WorldCommand.CMSG_REPOP_REQUEST);
            repop.Write((byte)0);
            SendPacket(repop);
        }
        #endregion

        #region Logging
        public override void Log(string message, LogLevel level = LogLevel.Info)
        {
            BotFactory.Instance.Log(Username + " - " + message, level);
        }

        public override void LogLine(string message, LogLevel level = LogLevel.Info)
        {
            BotFactory.Instance.Log(Username + " - " + message, level);
        }

        public override void LogException(string message)
        {
            BotFactory.Instance.Log(Username + " - " + message, LogLevel.Error);
        }

        public override void LogException(Exception ex)
        {
            BotFactory.Instance.Log(string.Format(Username + " - {0} {1}", ex.Message, ex.StackTrace), LogLevel.Error);
        }
        #endregion

        #region Task Route System
        public bool LoadAndStartRoute(string routePath)
        {
            try
            {
                Log($"Loading task route from: {routePath}", LogLevel.Info);
                var route = TaskRouteLoader.LoadFromJson(routePath);
                return StartRoute(route);
            }
            catch (Exception ex)
            {
                LogException($"Failed to load route from {routePath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Load a route and create the executor, but don't start it yet.
        /// Use this when you need to subscribe to events before the route begins.
        /// Call StartLoadedRoute() after subscribing to events.
        /// </summary>
        /// <returns>The executor to subscribe to, or null if load failed</returns>
        public TaskExecutorAI LoadRoute(string routePath)
        {
            try
            {
                Log($"Loading task route from: {routePath}", LogLevel.Info);
                var route = TaskRouteLoader.LoadFromJson(routePath);
                if (route == null)
                {
                    Log("Failed to load route: null result", LogLevel.Error);
                    return null;
                }

                // Stop current route if any
                StopRoute();

                // Create the executor but don't push it yet
                currentRouteExecutor = new TaskExecutorAI(route);
                return currentRouteExecutor;
            }
            catch (Exception ex)
            {
                LogException($"Failed to load route from {routePath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Start a previously loaded route. Call this after subscribing to events on the executor.
        /// </summary>
        public bool StartLoadedRoute()
        {
            if (currentRouteExecutor == null)
            {
                Log("No route loaded to start", LogLevel.Error);
                return false;
            }

            if (PushStrategicAI(currentRouteExecutor))
            {
                Log($"Started route: {currentRouteExecutor.Route.Name}", LogLevel.Info);
                return true;
            }
            else
            {
                Log($"Failed to start route: {currentRouteExecutor.Route.Name}", LogLevel.Error);
                currentRouteExecutor = null;
                return false;
            }
        }

        public bool StartRoute(TaskRoute route)
        {
            if (route == null)
            {
                Log("Cannot start null route", LogLevel.Error);
                return false;
            }

            // Stop current route if any
            StopRoute();

            // Create and push the TaskExecutorAI
            currentRouteExecutor = new TaskExecutorAI(route);
            if (PushStrategicAI(currentRouteExecutor))
            {
                Log($"Started route: {route.Name}", LogLevel.Info);
                return true;
            }
            else
            {
                Log($"Failed to start route: {route.Name}", LogLevel.Error);
                currentRouteExecutor = null;
                return false;
            }
        }

        public void StopRoute()
        {
            if (currentRouteExecutor != null)
            {
                Log($"Stopping route: {currentRouteExecutor.Route.Name}", LogLevel.Info);
                PopStrategicAI(currentRouteExecutor);
                currentRouteExecutor = null;
            }
        }

        public void PauseRoute()
        {
            currentRouteExecutor?.Pause();
        }

        public void ResumeRoute()
        {
            currentRouteExecutor?.Resume();
        }

        /// <summary>
        /// Get the current route executor (for subscribing to events)
        /// </summary>
        public TaskExecutorAI GetRouteExecutor()
        {
            return currentRouteExecutor;
        }

        public string GetRouteStatus()
        {
            if (currentRouteExecutor == null)
                return "No route active";

            var route = currentRouteExecutor.Route;
            var currentTask = currentRouteExecutor.CurrentTask;
            int taskIndex = currentRouteExecutor.CurrentTaskIndex;

            if (currentTask == null)
            {
                return $"Route '{route.Name}' - Completed or not started";
            }

            return $"Route '{route.Name}' - Task {taskIndex + 1}/{route.Tasks.Count}: {currentTask.Name}";
        }
        #endregion
    }
}
