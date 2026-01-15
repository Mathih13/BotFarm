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

namespace BotFarm
{
    class BotGame : AutomatedGame
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
            if (Behavior.TestMove)
            {
                PushStrategicAI(new TestMoveAI());
            }
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
#warning ToDo: create a character with a different name
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
