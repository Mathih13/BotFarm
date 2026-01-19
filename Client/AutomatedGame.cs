using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Client.UI;
using Client.Authentication;
using Client.World;
using Client.Chat;
using Client;
using Client.World.Network;
using Client.Authentication.Network;
using System.Threading;
using Client.Chat.Definitions;
using Client.World.Definitions;
using System.Diagnostics;
using Client.World.Entities;
using Client.World.Items;
using System.Collections;
using DetourCLI;
using MapCLI;
using Client.AI;

namespace Client
{
    public class AutomatedGame : IGameUI, IGame, IAsyncDisposable
    {
        #region Constants
        protected const float MovementEpsilon = 0.5f;
        protected const float FollowMovementEpsilon = 5f;
        protected const float FollowTargetRecalculatePathEpsilon = 5f;

        // Movement update interval with jitter to prevent synchronized packet bursts
        private static readonly Random MovementJitterRandom = new Random();
        protected static TimeSpan GetMovementInterval()
        {
            // 200ms base + 0-50ms jitter = 200-250ms (4-5 updates/sec, more realistic than 50ms)
            int jitter = MovementJitterRandom.Next(50);
            return TimeSpan.FromMilliseconds(200 + jitter);
        }
        #endregion

        #region Properties
        public bool Running { get; set; }
        GameSocket socket;
        public System.Numerics.BigInteger Key { get; private set; }
        public string Hostname { get; private set; }
        public int Port { get; private set; }
        public string Username { get; private set; }
        public string Password { get; private set; }
        public bool LoggedIn { get; private set; }
        public int RealmID { get; private set; }
        public int Character { get; private set; }
        public bool Connected { get; private set; }
        public string LastSentPacket
        {
            get
            {
                return socket.LastOutOpcodeName;
            }
        }
        public DateTime LastSentPacketTime
        {
            get
            {
                return socket.LastOutOpcodeTime;
            }
        }
        public string LastReceivedPacket
        {
            get
            {
                return socket.LastInOpcodeName;
            }
        }
        public DateTime LastReceivedPacketTime
        {
            get
            {
                return socket.LastInOpcodeTime;
            }
        }
        public DateTime LastUpdate
        {
            get;
            private set;
        }
        TaskCompletionSource<bool> loggedOutEvent = new TaskCompletionSource<bool>();
        public int ScheduledActionsCount => scheduledActions.Count;
        ScheduledActions scheduledActions;
        ActionFlag disabledActions;
        int scheduledActionCounter;
        public GameWorld World
        {
            get;
            private set;
        }
        public Player Player
        {
            get;
            protected set;
        }

        bool activeMoverSet = false;

        void SendSetActiveMover()
        {
            if (!activeMoverSet && Player.GUID != 0)
            {
                activeMoverSet = true;
                var setActiveMover = new OutPacket(WorldCommand.CMSG_SET_ACTIVE_MOVER);
                setActiveMover.Write(Player.GUID);
                Log($"Enabling movement for Player 0x{Player.GUID:X}", LogLevel.Debug);
                SendPacket(setActiveMover);
            }
        }

        public override LogLevel LogLevel
        {
            get
            {
                return _logLevel;
            }
            set
            {
                _logLevel = value;
            }
        }
        LogLevel _logLevel = Client.UI.LogLevel.Error;

        public override IGame Game
        {
            get
            {
                return this;
            }
        }
        UpdateObjectHandler updateObjectHandler;

        Stack<IStrategicAI> StrategicAIs;
        Stack<ITacticalAI> TacticalAIs;
        Stack<IOperationalAI> OperationalAIs;

        public Dictionary<ulong, WorldObject> Objects
        {
            get;
            private set;
        }

        protected HashSet<uint> CompletedAchievements
        {
            get;
            private set;
        }
        protected Dictionary<uint, ulong> AchievementCriterias
        {
            get;
            private set;
        }
        protected bool HasExploreCriteria(uint criteriaId)
        {
            ulong counter;
            if (AchievementCriterias.TryGetValue(criteriaId, out counter))
                return counter > 0;
            return false;
        }

        public UInt64 GroupLeaderGuid { get; private set; }
        public List<UInt64> GroupMembersGuids = new List<UInt64>();

        // Quest interaction state
        /// <summary>
        /// GUID of the NPC currently offering a quest (set by SMSG_QUESTGIVER_QUEST_DETAILS)
        /// </summary>
        public ulong PendingQuestGiverGuid { get; protected set; }
        
        /// <summary>
        /// Quest ID currently being offered (set by SMSG_QUESTGIVER_QUEST_DETAILS)
        /// </summary>
        public uint PendingQuestId { get; protected set; }
        
        /// <summary>
        /// True if quest details are available to accept
        /// </summary>
        public bool HasPendingQuestOffer => PendingQuestId != 0;
        
        /// <summary>
        /// GUID of the NPC ready to receive quest turn-in (set by SMSG_QUESTGIVER_OFFER_REWARD)
        /// </summary>
        public ulong PendingQuestTurnInGuid { get; protected set; }
        
        /// <summary>
        /// Quest ID ready to be turned in (set by SMSG_QUESTGIVER_OFFER_REWARD)
        /// </summary>
        public uint PendingQuestTurnInId { get; protected set; }
        
        /// <summary>
        /// True if quest can be turned in (reward selection available)
        /// </summary>
        public bool HasPendingQuestTurnIn => PendingQuestTurnInId != 0;
        
        /// <summary>
        /// Number of reward choices available when turning in quest
        /// </summary>
        public uint PendingQuestRewardChoiceCount { get; protected set; }

        /// <summary>
        /// Checks if a quest is currently in the player's quest log.
        /// </summary>
        /// <param name="questId">The quest ID to check for</param>
        /// <returns>True if the quest is in the log, false otherwise</returns>
        public bool IsQuestInLog(uint questId)
        {
            if (Player == null || questId == 0)
                return false;

            // Quest log has 25 slots, each slot is 5 fields apart
            // PLAYER_QUEST_LOG_X_1 contains the quest ID
            int baseField = (int)PlayerField.PLAYER_QUEST_LOG_1_1;
            for (int slot = 0; slot < 25; slot++)
            {
                int fieldIndex = baseField + (slot * 5);
                uint slotQuestId = Player[fieldIndex];
                if (slotQuestId == questId)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Current loot window state - tracks what's in the loot window when open
        /// </summary>
        public LootWindowState CurrentLoot { get; protected set; } = new LootWindowState();

        // Character deletion state
        /// <summary>
        /// Queue of character GUIDs pending deletion
        /// </summary>
        protected Queue<ulong> PendingCharacterDeletions { get; set; } = new Queue<ulong>();

        /// <summary>
        /// True if we're currently deleting characters before creating a new one
        /// </summary>
        protected bool IsDeletingCharacters { get; set; }

        /// <summary>
        /// Random number generator for character creation
        /// </summary>
        protected static Random CharacterRandom { get; } = new Random();
        #endregion

        public AutomatedGame(string hostname, int port, string username, string password, int realmId, int character)
        {
            this.RealmID = realmId;
            this.Character = character;
            scheduledActions = new ScheduledActions();
            updateObjectHandler = new UpdateObjectHandler(this);
            Triggers = new IteratedList<Trigger>();
            World = new GameWorld();
            Player = new Player();
            Player.OnFieldUpdated += OnFieldUpdate;
            Objects = new Dictionary<ulong, WorldObject>();
            CompletedAchievements = new HashSet<uint>();
            AchievementCriterias = new Dictionary<uint, ulong>();
            StrategicAIs = new Stack<IStrategicAI>();
            TacticalAIs = new Stack<ITacticalAI>();
            OperationalAIs = new Stack<IOperationalAI>();
            PushStrategicAI(new EmptyStrategicAI());
            PushTacticalAI(new EmptyTacticalAI());
            PushOperationalAI(new EmptyOperationalAI());

            this.Hostname = hostname;
            this.Port = port;
            this.Username = username;
            this.Password = password;

            socket = new AuthSocket(this, Hostname, Port, Username, Password);
            socket.InitHandlers();
        }

        #region Basic Methods
        public void ConnectTo(WorldServerInfo server)
        {
            if (socket is AuthSocket)
                Key = ((AuthSocket)socket).Key;

            socket.Dispose();

            socket = new WorldSocket(this, server);
            socket.InitHandlers();

            if (socket.Connect())
            {
                socket.Start();
                Connected = true;
            }
            else
                Reconnect();
        }

        public virtual void Start()
        {
            // the initial socket is an AuthSocket - it will initiate its own asynch read
            Running = true;
            socket.Connect();

            Task.Run(async () =>
                {
                    while (Running)
                    {
                        // main loop here
                        Update();
                        await Task.Delay(100).ConfigureAwait(false);
                    }
                });
        }

        public override void Update()
        {
            LastUpdate = DateTime.Now;

            (socket as WorldSocket)?.HandlePackets();

            // Reconnect if it passed some time since last try of logging in
            if (!Connected || !LoggedIn)
            {
                if (LastSentPacketTime < DateTime.Now.AddSeconds(-60) && LastSentPacketTime != default(DateTime))
                {
                    Reconnect();
                    return;
                }
            }

            if (World.SelectedCharacter == null)
                return;

            StrategicAIs.Peek().Update();
            TacticalAIs.Peek().Update();
            OperationalAIs.Peek().Update();

            while (scheduledActions.Count != 0)
            {
                var scheduledAction = scheduledActions.First();
                if (scheduledAction.ScheduledTime <= DateTime.Now)
                {
                    scheduledActions.RemoveAt(0, false);
                    if (scheduledAction.Interval > TimeSpan.Zero)
                        RescheduleAction(scheduledAction);
                    try
                    {
                        scheduledAction.Action();
                    }
                    catch(Exception ex)
                    {
                        LogException(ex);
                    }
                }
                else
                    break;
            }
        }

        public void Reconnect()
        {
            Connected = false;
            LoggedIn = false;
            while (Running)
            {
                socket.Disconnect();
                scheduledActions.Clear();
                ResetTriggers();
                socket = new AuthSocket(this, Hostname, Port, Username, Password);
                socket.InitHandlers();
                // exit from loop if the socket connected successfully
                if (socket.Connect())
                    break;

                // try again later
                Thread.Sleep(10000);
            }
        }

        public override async Task Exit()
        {
            ClearTriggers();
            ClearAIs();
            if (LoggedIn)
            {
                OutPacket logout = new OutPacket(WorldCommand.CMSG_LOGOUT_REQUEST);
                SendPacket(logout);
                await loggedOutEvent.Task.ConfigureAwait(false);
            }
            else
            {
                Connected = false;
                LoggedIn = false;
                Running = false;
            }
        }

        /// <summary>
        /// Request logout from the server. The bot will reconnect automatically via BotFactory.
        /// </summary>
        public void Logout()
        {
            Log("Requesting logout", LogLevel.Info);
            OutPacket logout = new OutPacket(WorldCommand.CMSG_LOGOUT_REQUEST);
            SendPacket(logout);
        }

        public void SendPacket(OutPacket packet)
        {
            if (socket is WorldSocket)
            {
                ((WorldSocket)socket).Send(packet);
                HandleTriggerInput(TriggerActionType.Opcode, packet);
            }
        }

        public override void PresentRealmList(WorldServerList realmList)
        {
            if (RealmID >= realmList.Count)
            {
                LogException("Invalid RealmID '" + RealmID + "' specified in the configs");
                Environment.Exit(1);
            }

            LogLine("Connecting to realm " + realmList[RealmID].Name);
            ConnectTo(realmList[RealmID]);
        }

        public override void PresentCharacterList(Character[] characterList)
        {
            World.SelectedCharacter = characterList[Character];
            OutPacket packet = new OutPacket(WorldCommand.CMSG_PLAYER_LOGIN);
            packet.Write(World.SelectedCharacter.GUID);
            SendPacket(packet);
            LoggedIn = true;
            Player.GUID = World.SelectedCharacter.GUID;
        }

        public override string ReadLine()
        {
            throw new NotImplementedException();
        }

        public override int Read()
        {
            throw new NotImplementedException();
        }

        public override ConsoleKeyInfo ReadKey()
        {
            throw new NotImplementedException();
        }

        public int ScheduleAction(Action action, TimeSpan interval = default(TimeSpan), ActionFlag flags = ActionFlag.None, Action cancel = null)
        {
            return ScheduleAction(action, DateTime.Now, interval, flags, cancel);
        }

        public int ScheduleAction(Action action, DateTime time, TimeSpan interval = default(TimeSpan), ActionFlag flags = ActionFlag.None, Action cancel = null)
        {
            if (Running && (flags == ActionFlag.None || !disabledActions.HasFlag(flags)))
            {
                scheduledActionCounter++;
                scheduledActions.Add(new RepeatingAction(action, cancel, time, interval, flags, scheduledActionCounter));
                return scheduledActionCounter;
            }
            else
                return 0;
        }

        public async Task ScheduleActionAndWait(Action action, int waitMilliseconds = 0)
        {
            await ScheduleActionAndWait(action, DateTime.Now, waitMilliseconds).ConfigureAwait(false);
        }

        public async Task ScheduleActionAndWait(Action action, DateTime time, int waitMilliseconds = 0)
        {
            var completion = new TaskCompletionSource<object>();
            ScheduleAction(() =>
            {
                action();
                completion.SetResult(null);
            }, time);

            await completion.Task.ConfigureAwait(false);

            if (waitMilliseconds > 0)
                await Task.Delay(waitMilliseconds).ConfigureAwait(false);
        }

        private void RescheduleAction(RepeatingAction action)
        {
            if (Running && (action.Flags == ActionFlag.None || !disabledActions.HasFlag(action.Flags)))
                scheduledActions.Add(new RepeatingAction(action.Action, action.Cancel, DateTime.Now + action.Interval, action.Interval, action.Flags, action.Id));
            else
                return;
        }

        public void CancelActionsByFlag(ActionFlag flag, bool cancel = true)
        {
            scheduledActions.RemoveByFlag(flag, cancel);
        }

        public bool CancelAction(int actionId)
        {
            return scheduledActions.Remove(actionId);
        }

        public void DisableActionsByFlag(ActionFlag flag)
        {
            disabledActions |= flag;
            CancelActionsByFlag(flag);
        }

        public void EnableActionsByFlag(ActionFlag flag)
        {
            disabledActions &= ~flag;
        }

        public void CreateCharacter(Race race, Class classWow)
        {
            Log("Creating new character");
            OutPacket createCharacterPacket = new OutPacket(WorldCommand.CMSG_CHAR_CREATE);
            StringBuilder charName = new StringBuilder();

            // Generate character name from username - convert digits to unique letter sequences
            // Use different offsets for each digit position to avoid collisions
            int digitPosition = 0;
            foreach (char c in Username)
            {
                if (char.IsLetter(c))
                    charName.Append(c);
                else if (char.IsDigit(c))
                {
                    // Use position-based offset to make digit sequences unique
                    // e.g., "11" becomes "bm" not "bb", "12" becomes "bn" not "bc"
                    int baseOffset = (c - '0');
                    int positionOffset = (digitPosition * 11) % 26; // Prime multiplier for variety
                    charName.Append((char)('a' + ((baseOffset + positionOffset) % 26)));
                    digitPosition++;
                }
                if (charName.Length >= 12)
                    break;
            }

            // If too short, generate a fallback name
            if (charName.Length < 2)
            {
                charName.Clear();
                charName.Append("Botchar");
            }

            // Ensure name starts with uppercase and rest lowercase (WoW naming rules)
            charName[0] = char.ToUpper(charName[0]);
            for (int i = 1; i < charName.Length; i++)
                charName[i] = char.ToLower(charName[i]);

            // Ensure no consecutive duplicate characters (WoW naming rules)
            char previousChar = '\0';
            for (int i = 0; i < charName.Length; i++ )
            {
                if (charName[i] == previousChar)
                    charName[i]++;
                previousChar = charName[i];
            }

            createCharacterPacket.Write(charName.ToString().ToCString());
            createCharacterPacket.Write((byte)race);
            createCharacterPacket.Write((byte)classWow);
            createCharacterPacket.Write((byte)Gender.Male);
            byte skin = 6; createCharacterPacket.Write(skin);
            byte face = 5; createCharacterPacket.Write(face);
            byte hairStyle = 0; createCharacterPacket.Write(hairStyle);
            byte hairColor = 1; createCharacterPacket.Write(hairColor);
            byte facialHair = 5; createCharacterPacket.Write(facialHair);
            byte outfitId = 0; createCharacterPacket.Write(outfitId);

            SendPacket(createCharacterPacket);
        }

        /// <summary>
        /// Delete a character by GUID
        /// </summary>
        /// <param name="guid">Character GUID to delete</param>
        public void DeleteCharacter(ulong guid)
        {
            Log($"Deleting character 0x{guid:X}");
            OutPacket packet = new OutPacket(WorldCommand.CMSG_CHAR_DELETE);
            packet.Write(guid);
            SendPacket(packet);
        }

        /// <summary>
        /// Called when all pending character deletions are complete
        /// Override this in subclasses to handle post-deletion logic
        /// </summary>
        protected virtual void OnAllCharactersDeleted()
        {
            // Default: do nothing, subclasses can override
        }

        public async ValueTask DisposeAsync()
        {
            scheduledActions.Clear();

            await Exit().ConfigureAwait(false);

            if (socket != null)
                socket.Dispose();
        }

        public virtual void NoCharactersFound()
        { }

        public virtual void CharacterCreationFailed(CommandDetail result)
        {
            NoCharactersFound();
        }

        public virtual void InvalidCredentials()
        { }

        protected WorldObject FindClosestObject(HighGuid highGuidType, Func<WorldObject, bool> additionalCheck = null)
        {
            float closestDistance = float.MaxValue;
            WorldObject closestObject = null;

            foreach (var obj in Objects.Values)
            {
                if (!obj.IsType(highGuidType))
                    continue;

                if (additionalCheck != null && !additionalCheck(obj))
                    continue;

                if (obj.MapID != Player.MapID)
                    continue;

                float distance = (obj - Player).Length;
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestObject = obj;
                }
            }

            return closestObject;
        }

        public string GetPlayerName(WorldObject obj)
        {
            return GetPlayerName(obj.GUID);
        }

        protected string GetPlayerName(ulong guid)
        {
            string name;
            if (Game.World.PlayerNameLookup.TryGetValue(guid, out name))
                return name;
            else
                return "";
        }

        public bool PushStrategicAI(IStrategicAI ai) => PushAI(ai, StrategicAIs);
        public bool PushTacticalAI(ITacticalAI ai) => PushAI(ai, TacticalAIs);
        public bool PushOperationalAI(IOperationalAI ai) => PushAI(ai, OperationalAIs);
        bool PushAI<T>(T ai, Stack<T> AIs) where T : IGameAI
        {
            if (AIs.Count == 0)
            {
                AIs.Push(ai);
                if (ai.Activate(this))
                    return true;
                else
                {
                    AIs.Pop();
                    return false;
                }
            }

            var currentAI = AIs.Peek();
            if (currentAI.AllowPause())
            {
                if (ai.GetType() == currentAI.GetType())
                    return false;
                else
                {
                    currentAI.Pause();
                    AIs.Push(ai);
                    if (ai.Activate(this))
                        return true;
                    else
                    {
                        AIs.Pop();
                        currentAI.Resume();
                        return false;
                    }
                }
            }
            else
                return false;
        }

        public bool PopStrategicAI(IStrategicAI ai) => PopAI(ai, StrategicAIs);
        public bool PopTacticalAI(ITacticalAI ai) => PopAI(ai, TacticalAIs);
        public bool PopOperationalAI(IOperationalAI ai) => PopAI(ai, OperationalAIs);
        public bool PopAI<T>(T ai, Stack<T> AIs) where T : class, IGameAI
        {
            if (AIs.Count <= 1)
                return false;

            var currentAI = AIs.Peek();
            if (currentAI != ai)
                return false;

            currentAI.Deactivate();
            AIs.Pop();

            AIs.Peek().Resume();
            return true;
        }

        public void ClearAIs()
        {
            while (StrategicAIs.Count > 1)
            {
                var currentAI = StrategicAIs.Pop();
                currentAI.Deactivate();
            }

            while (TacticalAIs.Count > 1)
            {
                var currentAI = TacticalAIs.Pop();
                currentAI.Deactivate();
            }

            while (OperationalAIs.Count > 1)
            {
                var currentAI = OperationalAIs.Pop();
                currentAI.Deactivate();
            }
        }
        #endregion

        #region Commands
        public void DoSayChat(string message)
        {
            var response = new OutPacket(WorldCommand.CMSG_MESSAGECHAT);

            response.Write((uint)ChatMessageType.Say);
            var race = World.SelectedCharacter.Race;
            var language = race.IsHorde() ? Language.Orcish : Language.Common;
            response.Write((uint)language);
            response.Write(message.ToCString());
            SendPacket(response);
        }

        public void DoPartyChat(string message)
        {
            var response = new OutPacket(WorldCommand.CMSG_MESSAGECHAT);

            response.Write((uint)ChatMessageType.Party);
            var race = World.SelectedCharacter.Race;
            var language = race.IsHorde() ? Language.Orcish : Language.Common;
            response.Write((uint)language);
            response.Write(message.ToCString());
            SendPacket(response);
        }

        public void DoGuildChat(string message)
        {
            var response = new OutPacket(WorldCommand.CMSG_MESSAGECHAT);

            response.Write((uint)ChatMessageType.Guild);
            var race = World.SelectedCharacter.Race;
            var language = race.IsHorde() ? Language.Orcish : Language.Common;
            response.Write((uint)language);
            response.Write(message.ToCString());
            SendPacket(response);
        }

        public void DoWhisperChat(string message, string player)
        {
            var response = new OutPacket(WorldCommand.CMSG_MESSAGECHAT);

            response.Write((uint)ChatMessageType.Whisper);
            var race = World.SelectedCharacter.Race;
            var language = race.IsHorde() ? Language.Orcish : Language.Common;
            response.Write((uint)language);
            response.Write(player.ToCString());
            response.Write(message.ToCString());
            SendPacket(response);
        }

        public void Tele(string teleport)
        {
            DoSayChat(".tele " + teleport);
        }

        public void CastSpell(int spellid, bool chatLog = true)
        {
            DoSayChat(".cast " + spellid);
            if (chatLog)
                DoSayChat("Cast spellid " + spellid);
        }

        #region GM Commands (require GM level 2+)

        /// <summary>
        /// Teleport self to coordinates using GM command (requires GM level 2+)
        /// Uses .go xyz which takes coordinates directly, unlike .tele which requires named locations
        /// </summary>
        public void TeleportToPosition(float x, float y, float z, uint mapId)
        {
            // Use invariant culture formatting to ensure decimal point (not comma)
            DoSayChat(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                ".go xyz {0} {1} {2} {3}", x, y, z, mapId));
        }

        /// <summary>
        /// Add item to inventory using GM command (requires GM level 2+)
        /// </summary>
        public void AddItem(uint itemEntry, int count = 1)
        {
            DoSayChat($".additem {itemEntry} {count}");
        }

        /// <summary>
        /// Level up by specified number of levels using GM command (requires GM level 2+)
        /// Note: .levelup is RELATIVE - it adds levels, doesn't set absolute level
        /// </summary>
        public void LevelUp(int levels)
        {
            if (levels > 0)
                DoSayChat($".levelup {levels}");
        }

        /// <summary>
        /// Add quest to quest log using GM command (requires GM level 2+)
        /// </summary>
        public void AddQuest(uint questId)
        {
            DoSayChat($".quest add {questId}");
        }

        /// <summary>
        /// Complete quest objectives using GM command (requires GM level 3+)
        /// Note: This only completes objectives, use RewardQuest to fully finish
        /// </summary>
        public void CompleteQuest(uint questId)
        {
            DoSayChat($".quest complete {questId}");
        }

        /// <summary>
        /// Reward/finish quest using GM command (requires GM level 3+)
        /// This gives rewards and marks quest as completed
        /// </summary>
        public void RewardQuest(uint questId)
        {
            DoSayChat($".quest reward {questId}");
        }

        #endregion

        #endregion

        #region Actions
        public void DoTextEmote(TextEmote emote)
        {
            var packet = new OutPacket(WorldCommand.CMSG_TEXT_EMOTE);
            packet.Write((uint)emote);
            packet.Write((uint)0);
            packet.Write((ulong)0);
            SendPacket(packet);
        }

        /// <summary>
        /// Send a stationary heartbeat to confirm position to server.
        /// Useful during combat when standing still to prevent position desync.
        /// </summary>
        public void SendPositionHeartbeat()
        {
            if (!Player.GetPosition().IsValid)
                return;
            
            var heartbeat = new MovementPacket(WorldCommand.MSG_MOVE_HEARTBEAT)
            {
                GUID = Player.GUID,
                flags = MovementFlags.MOVEMENTFLAG_NONE,
                X = Player.X,
                Y = Player.Y,
                Z = Player.Z,
                O = Player.O
            };
            SendPacket(heartbeat);
        }

        public void SetFacing(float orientation)
        {
            if (!Player.GetPosition().IsValid)
                return;
            var packet = new OutPacket(WorldCommand.MSG_MOVE_SET_FACING);
            packet.WritePacketGuid(Player.GUID);
            packet.Write((UInt32)0); //flags
            packet.Write((UInt16)0); //flags2
            packet.Write((UInt32)0); //time
            Player.O = orientation;
            packet.Write(Player.X);
            packet.Write(Player.Y);
            packet.Write(Player.Z);
            packet.Write(Player.O);
            packet.Write((UInt32)0); //fall time
            SendPacket(packet);
        }

        /// <summary>
        /// Face towards a target position
        /// </summary>
        public void FacePosition(Position target)
        {
            if (target == null || !Player.GetPosition().IsValid)
                return;
            
            float dx = target.X - Player.X;
            float dy = target.Y - Player.Y;
            float orientation = (float)Math.Atan2(dy, dx);
            SetFacing(orientation);
        }

        /// <summary>
        /// Face towards a target object
        /// </summary>
        public void FaceTarget(WorldObject target)
        {
            if (target == null)
                return;
            FacePosition(target.GetPosition());
        }

        public void Follow(WorldObject target)
        {
            if (target == null)
            {
                Log("Follow: target is null", LogLevel.Warning);
                return;
            }

            Path path = null;
            bool moving = false;
            Position pathEndPosition = target.GetPosition();
            DateTime previousMovingTime = DateTime.MinValue;

            ScheduleAction(() =>
            {
                if (!target.IsValid)
                {
                    Log($"Follow: target position invalid - X:{target.X} Y:{target.Y} Z:{target.Z} Map:{target.MapID}", LogLevel.Warning);
                    return;
                }

                if (target.MapID != Player.MapID)
                {
                    Log("Trying to follow a target on another map", Client.UI.LogLevel.Warning);
                    CancelActionsByFlag(ActionFlag.Movement, false);
                    return;
                }

                var distance = target - Player.GetPosition();
                // check if we even need to move
                if (distance.Length < FollowMovementEpsilon)
                {
                    if (path != null)
                    {
                        var stopMoving = new MovementPacket(WorldCommand.MSG_MOVE_STOP)
                        {
                            GUID = Player.GUID,
                            flags = MovementFlags.MOVEMENTFLAG_NONE,
                            X = Player.X,
                            Y = Player.Y,
                            Z = Player.Z,
                            O = Player.O
                        };
                        SendPacket(stopMoving);
                        Player.SetPosition(stopMoving.GetPosition());
                        moving = false;
                        path = null;
                        HandleTriggerInput(TriggerActionType.DestinationReached, true);
                    }

                    return;
                }

                float targetMovement = (target - pathEndPosition).Length;
                if (targetMovement > FollowTargetRecalculatePathEpsilon)
                    path = null;
                else if (distance.Length >= FollowMovementEpsilon && distance.Length <= FollowTargetRecalculatePathEpsilon)
                    path = null;

                if (path == null)
                {
                    using (var detour = new DetourCLI.Detour())
                    {
                        List<MapCLI.Point> resultPath;

                        // Correct Z-coordinates using terrain height - Detour requires accurate heights
                        float startZ = MapCLI.Map.GetHeight(Player.X, Player.Y, Player.Z, Player.MapID);
                        float endZ = MapCLI.Map.GetHeight(target.X, target.Y, target.Z, Player.MapID);

                        // Check if GetHeight returned valid values
                        if (float.IsNaN(startZ) || float.IsInfinity(startZ) || startZ < -10000)
                        {
                            Log($"Follow: Invalid start height from GetHeight: {startZ}, using Player.Z: {Player.Z:F2}", LogLevel.Warning);
                            startZ = Player.Z;
                        }
                        if (float.IsNaN(endZ) || float.IsInfinity(endZ) || endZ < -10000)
                        {
                            Log($"Follow: Invalid end height from GetHeight: {endZ}, using target.Z: {target.Z:F2}", LogLevel.Warning);
                            endZ = target.Z;
                        }

                        var findPathResult = detour.FindPath(Player.X, Player.Y, startZ,
                                                target.X, target.Y, endZ,
                                                Player.MapID, out resultPath);
                        if (findPathResult != PathType.Complete)
                        {
                            Log($"Follow: Pathfinding failed with result {findPathResult}", LogLevel.Warning);
                            HandleTriggerInput(TriggerActionType.DestinationReached, false);
                            CancelActionsByFlag(ActionFlag.Movement);
                            return;
                        }

                        path = new Path(resultPath, Player.Speed, Player.MapID);
                        pathEndPosition = target.GetPosition();
                    }
                }

                if (!moving)
                {
                    moving = true;

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

                    previousMovingTime = DateTime.Now;
                    return;
                }

                float deltaTime = (float)(DateTime.Now - previousMovingTime).TotalSeconds;
                Point progressPosition = path.MoveAlongPath(deltaTime);

                Player.SetPosition(progressPosition.X, progressPosition.Y, progressPosition.Z);
                previousMovingTime = DateTime.Now;

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
            }, GetMovementInterval(), flags: ActionFlag.Movement);
        }

        public void JoinLFG(LfgRoleFlag role, IEnumerable<uint> dungeonIDs, string comment = "")
        {
            var packet = new OutPacket(WorldCommand.CMSG_LFG_JOIN);
            packet.Write((UInt32)role);
            packet.Write((UInt16)0);
            packet.Write((byte)dungeonIDs.Count());
            foreach (var dungeonID in dungeonIDs)
                packet.Write((UInt32)dungeonID);
            packet.Write((UInt32)0);
            packet.Write((comment ?? "").ToCString());
            SendPacket(packet);
        }

        public void LeaveLFG()
        {
            var packet = new OutPacket(WorldCommand.CMSG_LFG_LEAVE);
            SendPacket(packet);
        }
        #endregion

        #region Packet Handlers
        [PacketHandler(WorldCommand.SMSG_LOGIN_VERIFY_WORLD)]
        protected void HandleLoginVerifyWorld(InPacket packet)
        {
            Player.MapID = (int)packet.ReadUInt32();
            Player.X = packet.ReadSingle();
            Player.Y = packet.ReadSingle();
            Player.Z = packet.ReadSingle();
            Player.O = packet.ReadSingle();

            // CRITICAL: Send CMSG_SET_ACTIVE_MOVER to enable movement
            SendSetActiveMover();
        }

        [PacketHandler(WorldCommand.SMSG_NEW_WORLD)]
        protected void HandleNewWorld(InPacket packet)
        {
            Player.MapID = (int)packet.ReadUInt32();
            Player.X = packet.ReadSingle();
            Player.Y = packet.ReadSingle();
            Player.Z = packet.ReadSingle();
            Player.O = packet.ReadSingle();

            OutPacket result = new OutPacket(WorldCommand.MSG_MOVE_WORLDPORT_ACK);
            SendPacket(result);
        }

        [PacketHandler(WorldCommand.SMSG_TRANSFER_PENDING)]
        protected void HandleTransferPending(InPacket packet)
        {
            Player.ResetPosition();
            var newMap = packet.ReadUInt32();
        }

        [PacketHandler(WorldCommand.MSG_MOVE_TELEPORT_ACK)]
        protected void HandleMoveTeleportAck(InPacket packet)
        {
            var packGuid = packet.ReadPackedGuid();
            packet.ReadUInt32();
            var movementFlags = packet.ReadUInt32();
            var extraMovementFlags = packet.ReadUInt16();
            var time = packet.ReadUInt32();
            Player.X = packet.ReadSingle();
            Player.Y = packet.ReadSingle();
            Player.Z = packet.ReadSingle();
            Player.O = packet.ReadSingle();

            CancelActionsByFlag(ActionFlag.Movement, false);

            OutPacket result = new OutPacket(WorldCommand.MSG_MOVE_TELEPORT_ACK);
            result.WritePacketGuid(Player.GUID);
            result.Write((UInt32)0);
            result.Write(time);
            SendPacket(result);
        }

        [PacketHandler(WorldCommand.SMSG_CHAR_CREATE)]
        protected void HandleCharCreate(InPacket packet)
        {
            var response = (CommandDetail)packet.ReadByte();
            if (response == CommandDetail.CHAR_CREATE_SUCCESS)
                SendPacket(new OutPacket(WorldCommand.CMSG_CHAR_ENUM));
            else
                CharacterCreationFailed(response);
        }

        [PacketHandler(WorldCommand.SMSG_CHAR_DELETE)]
        protected void HandleCharDelete(InPacket packet)
        {
            var response = (CommandDetail)packet.ReadByte();
            if (response == CommandDetail.CHAR_DELETE_SUCCESS)
            {
                Log("Character deleted successfully");

                // Check if there are more characters to delete
                if (PendingCharacterDeletions.Count > 0)
                {
                    var nextGuid = PendingCharacterDeletions.Dequeue();
                    DeleteCharacter(nextGuid);
                }
                else if (IsDeletingCharacters)
                {
                    // All characters deleted, trigger callback
                    IsDeletingCharacters = false;
                    OnAllCharactersDeleted();
                }
            }
            else
            {
                Log($"Character deletion failed: {response}", LogLevel.Error);
                // Still try to continue with next deletion if any
                if (PendingCharacterDeletions.Count > 0)
                {
                    var nextGuid = PendingCharacterDeletions.Dequeue();
                    DeleteCharacter(nextGuid);
                }
                else if (IsDeletingCharacters)
                {
                    IsDeletingCharacters = false;
                    OnAllCharactersDeleted();
                }
            }
        }

        [PacketHandler(WorldCommand.SMSG_LOGOUT_RESPONSE)]
        protected void HandleLogoutResponse(InPacket packet)
        {
            bool logoutOk = packet.ReadUInt32() == 0;
            bool instant = packet.ReadByte() != 0;

            if(instant || !logoutOk)
            {
                Connected = false;
                LoggedIn = false;
                Running = false;
            }
        }

        [PacketHandler(WorldCommand.SMSG_LOGOUT_COMPLETE)]
        protected void HandleLogoutComplete(InPacket packet)
        {
            Connected = false;
            LoggedIn = false;
            Running = false;
            loggedOutEvent.SetResult(true);
        }

        [PacketHandler(WorldCommand.SMSG_UPDATE_OBJECT)]
        protected void HandleUpdateObject(InPacket packet)
        {
            updateObjectHandler.HandleUpdatePacket(packet);
        }

        [PacketHandler(WorldCommand.SMSG_COMPRESSED_UPDATE_OBJECT)]
        protected void HandleCompressedUpdateObject(InPacket packet)
        {
            updateObjectHandler.HandleUpdatePacket(packet.Inflate());
        }

        [PacketHandler(WorldCommand.SMSG_MONSTER_MOVE)]
        protected void HandleMonsterMove(InPacket packet)
        {
            updateObjectHandler.HandleMonsterMovementPacket(packet);
        }

        [PacketHandler(WorldCommand.MSG_MOVE_START_FORWARD)]
        [PacketHandler(WorldCommand.MSG_MOVE_START_BACKWARD)]
        [PacketHandler(WorldCommand.MSG_MOVE_STOP)]
        [PacketHandler(WorldCommand.MSG_MOVE_START_STRAFE_LEFT)]
        [PacketHandler(WorldCommand.MSG_MOVE_START_STRAFE_RIGHT)]
        [PacketHandler(WorldCommand.MSG_MOVE_STOP_STRAFE)]
        [PacketHandler(WorldCommand.MSG_MOVE_JUMP)]
        [PacketHandler(WorldCommand.MSG_MOVE_START_TURN_LEFT)]
        [PacketHandler(WorldCommand.MSG_MOVE_START_TURN_RIGHT)]
        [PacketHandler(WorldCommand.MSG_MOVE_STOP_TURN)]
        [PacketHandler(WorldCommand.MSG_MOVE_START_PITCH_UP)]
        [PacketHandler(WorldCommand.MSG_MOVE_START_PITCH_DOWN)]
        [PacketHandler(WorldCommand.MSG_MOVE_STOP_PITCH)]
        [PacketHandler(WorldCommand.MSG_MOVE_SET_RUN_MODE)]
        [PacketHandler(WorldCommand.MSG_MOVE_SET_WALK_MODE)]
        [PacketHandler(WorldCommand.MSG_MOVE_FALL_LAND)]
        [PacketHandler(WorldCommand.MSG_MOVE_START_SWIM)]
        [PacketHandler(WorldCommand.MSG_MOVE_STOP_SWIM)]
        [PacketHandler(WorldCommand.MSG_MOVE_SET_FACING)]
        [PacketHandler(WorldCommand.MSG_MOVE_SET_PITCH)]
        [PacketHandler(WorldCommand.MSG_MOVE_HEARTBEAT)]
        [PacketHandler(WorldCommand.MSG_MOVE_START_ASCEND)]
        [PacketHandler(WorldCommand.MSG_MOVE_STOP_ASCEND)]
        [PacketHandler(WorldCommand.MSG_MOVE_START_DESCEND)]
        protected void HandleMove(InPacket packet)
        {
            updateObjectHandler.HandleMovementPacket(packet);
        }

        [PacketHandler(WorldCommand.SMSG_FORCE_MOVE_ROOT)]
        protected void HandleForceMoveRoot(InPacket packet)
        {
            Log("SERVER SENT: SMSG_FORCE_MOVE_ROOT - Player is being ROOTED by server", LogLevel.Warning);
        }

        [PacketHandler(WorldCommand.SMSG_FORCE_MOVE_UNROOT)]
        protected void HandleForceMoveUnroot(InPacket packet)
        {
            Log("SERVER SENT: SMSG_FORCE_MOVE_UNROOT - Player is being UNROOTED by server", LogLevel.Info);
        }

        [PacketHandler(WorldCommand.SMSG_FORCE_RUN_SPEED_CHANGE)]
        protected void HandleForceRunSpeedChange(InPacket packet)
        {
            var guid = packet.ReadPackedGuid();
            var counter = packet.ReadUInt32();
            var newSpeed = packet.ReadSingle();

            // Send ACK
            var ack = new MovementPacket(WorldCommand.CMSG_FORCE_RUN_SPEED_CHANGE_ACK)
            {
                GUID = Player.GUID,
                flags = MovementFlags.MOVEMENTFLAG_NONE,
                X = Player.X,
                Y = Player.Y,
                Z = Player.Z,
                O = Player.O
            };
            ack.Write(counter);
            ack.Write(newSpeed);
            SendPacket(ack);
        }

        [PacketHandler(WorldCommand.SMSG_FORCE_WALK_SPEED_CHANGE)]
        protected void HandleForceWalkSpeedChange(InPacket packet)
        {
            var guid = packet.ReadPackedGuid();
            var counter = packet.ReadUInt32();
            var newSpeed = packet.ReadSingle();

            var ack = new MovementPacket(WorldCommand.CMSG_FORCE_WALK_SPEED_CHANGE_ACK)
            {
                GUID = Player.GUID,
                flags = MovementFlags.MOVEMENTFLAG_NONE,
                X = Player.X,
                Y = Player.Y,
                Z = Player.Z,
                O = Player.O
            };
            ack.Write(counter);
            ack.Write(newSpeed);
            SendPacket(ack);
        }

        [PacketHandler(WorldCommand.SMSG_FORCE_SWIM_SPEED_CHANGE)]
        protected void HandleForceSwimSpeedChange(InPacket packet)
        {
            var guid = packet.ReadPackedGuid();
            var counter = packet.ReadUInt32();
            var newSpeed = packet.ReadSingle();

            var ack = new MovementPacket(WorldCommand.CMSG_FORCE_SWIM_SPEED_CHANGE_ACK)
            {
                GUID = Player.GUID,
                flags = MovementFlags.MOVEMENTFLAG_NONE,
                X = Player.X,
                Y = Player.Y,
                Z = Player.Z,
                O = Player.O
            };
            ack.Write(counter);
            ack.Write(newSpeed);
            SendPacket(ack);
        }

        [PacketHandler(WorldCommand.SMSG_FORCE_RUN_BACK_SPEED_CHANGE)]
        protected void HandleForceRunBackSpeedChange(InPacket packet)
        {
            var guid = packet.ReadPackedGuid();
            var counter = packet.ReadUInt32();
            var newSpeed = packet.ReadSingle();

            var ack = new MovementPacket(WorldCommand.CMSG_FORCE_RUN_BACK_SPEED_CHANGE_ACK)
            {
                GUID = Player.GUID,
                flags = MovementFlags.MOVEMENTFLAG_NONE,
                X = Player.X,
                Y = Player.Y,
                Z = Player.Z,
                O = Player.O
            };
            ack.Write(counter);
            ack.Write(newSpeed);
            SendPacket(ack);
        }

        [PacketHandler(WorldCommand.SMSG_FORCE_SWIM_BACK_SPEED_CHANGE)]
        protected void HandleForceSwimBackSpeedChange(InPacket packet)
        {
            var guid = packet.ReadPackedGuid();
            var counter = packet.ReadUInt32();
            var newSpeed = packet.ReadSingle();

            var ack = new MovementPacket(WorldCommand.CMSG_FORCE_SWIM_BACK_SPEED_CHANGE_ACK)
            {
                GUID = Player.GUID,
                flags = MovementFlags.MOVEMENTFLAG_NONE,
                X = Player.X,
                Y = Player.Y,
                Z = Player.Z,
                O = Player.O
            };
            ack.Write(counter);
            ack.Write(newSpeed);
            SendPacket(ack);
        }

        [PacketHandler(WorldCommand.SMSG_FORCE_FLIGHT_SPEED_CHANGE)]
        protected void HandleForceFlightSpeedChange(InPacket packet)
        {
            var guid = packet.ReadPackedGuid();
            var counter = packet.ReadUInt32();
            var newSpeed = packet.ReadSingle();

            var ack = new MovementPacket(WorldCommand.CMSG_FORCE_FLIGHT_SPEED_CHANGE_ACK)
            {
                GUID = Player.GUID,
                flags = MovementFlags.MOVEMENTFLAG_NONE,
                X = Player.X,
                Y = Player.Y,
                Z = Player.Z,
                O = Player.O
            };
            ack.Write(counter);
            ack.Write(newSpeed);
            SendPacket(ack);
        }

        [PacketHandler(WorldCommand.SMSG_FORCE_FLIGHT_BACK_SPEED_CHANGE)]
        protected void HandleForceFlightBackSpeedChange(InPacket packet)
        {
            var guid = packet.ReadPackedGuid();
            var counter = packet.ReadUInt32();
            var newSpeed = packet.ReadSingle();

            var ack = new MovementPacket(WorldCommand.CMSG_FORCE_FLIGHT_BACK_SPEED_CHANGE_ACK)
            {
                GUID = Player.GUID,
                flags = MovementFlags.MOVEMENTFLAG_NONE,
                X = Player.X,
                Y = Player.Y,
                Z = Player.Z,
                O = Player.O
            };
            ack.Write(counter);
            ack.Write(newSpeed);
            SendPacket(ack);
        }

        [PacketHandler(WorldCommand.SMSG_TIME_SYNC_REQ)]
        protected void HandleTimeSyncRequest(InPacket packet)
        {
            // SMSG_TIME_SYNC_REQ only contains the counter
            var counter = packet.ReadUInt32();

            // Calculate client time (milliseconds since process start)
            var clientTime = (uint)(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds;

            var response = new OutPacket(WorldCommand.CMSG_TIME_SYNC_RESP);
            response.Write(counter);
            response.Write(clientTime);
            SendPacket(response);
        }

        [PacketHandler(WorldCommand.SMSG_LOGIN_SET_TIME_SPEED)]
        protected void HandleLoginSetTimeSpeed(InPacket packet)
        {
            var serverTime = packet.ReadUInt32();
            var timeScale = packet.ReadSingle();
            // Silently accept - not needed for bot functionality
        }

        #region Quest Handlers
        [PacketHandler(WorldCommand.SMSG_QUESTGIVER_QUEST_DETAILS)]
        protected void HandleQuestGiverQuestDetails(InPacket packet)
        {
            // Quest details are offered - can be accepted
            PendingQuestGiverGuid = packet.ReadUInt64();
            packet.ReadUInt64(); // Quest sharer GUID (for shared quests)
            PendingQuestId = packet.ReadUInt32();
            
            Log($"Quest offer received: Quest ID {PendingQuestId} from NPC 0x{PendingQuestGiverGuid:X}", LogLevel.Debug);
        }

        [PacketHandler(WorldCommand.SMSG_QUESTGIVER_OFFER_REWARD)]
        protected void HandleQuestGiverOfferReward(InPacket packet)
        {
            // Quest can be turned in - reward selection available
            PendingQuestTurnInGuid = packet.ReadUInt64();
            PendingQuestTurnInId = packet.ReadUInt32();
            
            // Skip to reward choice count (complex packet structure)
            // Read past title, text, etc.
            packet.ReadCString(); // Title
            packet.ReadCString(); // Offer reward text
            packet.ReadCString(); // Portrait turn-in text
            packet.ReadCString(); // Portrait turn-in name
            packet.ReadUInt32(); // Portrait turn-in ID
            packet.ReadCString(); // Portrait giver text
            packet.ReadCString(); // Portrait giver name
            packet.ReadUInt32(); // Portrait giver ID
            packet.ReadUInt32(); // Auto launch (bool)
            packet.ReadUInt32(); // Flags
            packet.ReadUInt32(); // Suggested group num
            
            // Emote count
            var emoteCount = packet.ReadUInt32();
            for (int i = 0; i < emoteCount; i++)
            {
                packet.ReadUInt32(); // Emote delay
                packet.ReadUInt32(); // Emote ID
            }
            
            PendingQuestRewardChoiceCount = packet.ReadUInt32();
            
            Log($"Quest turn-in available: Quest ID {PendingQuestTurnInId} at NPC 0x{PendingQuestTurnInGuid:X}, {PendingQuestRewardChoiceCount} reward choices", LogLevel.Debug);
        }

        [PacketHandler(WorldCommand.SMSG_QUESTGIVER_REQUEST_ITEMS)]
        protected void HandleQuestGiverRequestItems(InPacket packet)
        {
            // NPC is requesting items for quest completion (not enough items to turn in yet)
            var npcGuid = packet.ReadUInt64();
            var questId = packet.ReadUInt32();
            
            Log($"Quest items requested: Quest ID {questId} by NPC 0x{npcGuid:X}", LogLevel.Debug);
        }

        [PacketHandler(WorldCommand.SMSG_QUESTGIVER_QUEST_COMPLETE)]
        protected void HandleQuestGiverQuestComplete(InPacket packet)
        {
            // Quest completed and turned in successfully
            var questId = packet.ReadUInt32();
            
            Log($"Quest completed: Quest ID {questId}", LogLevel.Info);
            
            // Clear pending turn-in state
            if (PendingQuestTurnInId == questId)
            {
                PendingQuestTurnInGuid = 0;
                PendingQuestTurnInId = 0;
                PendingQuestRewardChoiceCount = 0;
            }
        }

        [PacketHandler(WorldCommand.SMSG_QUESTGIVER_QUEST_FAILED)]
        protected void HandleQuestGiverQuestFailed(InPacket packet)
        {
            var questId = packet.ReadUInt32();
            var reason = packet.ReadUInt32();
            
            Log($"Quest failed: Quest ID {questId}, reason {reason}", LogLevel.Warning);
        }

        /// <summary>
        /// Accept the currently offered quest (from SMSG_QUESTGIVER_QUEST_DETAILS)
        /// </summary>
        public void AcceptQuest()
        {
            if (!HasPendingQuestOffer)
            {
                Log("AcceptQuest: No pending quest offer", LogLevel.Warning);
                return;
            }
            
            var packet = new OutPacket(WorldCommand.CMSG_QUESTGIVER_ACCEPT_QUEST);
            packet.Write(PendingQuestGiverGuid);
            packet.Write(PendingQuestId);
            packet.Write((uint)0); // Unknown field
            SendPacket(packet);
            
            Log($"Accepting quest {PendingQuestId} from NPC 0x{PendingQuestGiverGuid:X}", LogLevel.Debug);
            
            // Clear pending offer state
            PendingQuestGiverGuid = 0;
            PendingQuestId = 0;
        }
        
        /// <summary>
        /// Accept a specific quest from a specific NPC
        /// </summary>
        public void AcceptQuest(ulong npcGuid, uint questId)
        {
            var packet = new OutPacket(WorldCommand.CMSG_QUESTGIVER_ACCEPT_QUEST);
            packet.Write(npcGuid);
            packet.Write(questId);
            packet.Write((uint)0); // Unknown field
            SendPacket(packet);
            
            Log($"Accepting quest {questId} from NPC 0x{npcGuid:X}", LogLevel.Debug);
        }

        /// <summary>
        /// Turn in the currently pending quest (from SMSG_QUESTGIVER_OFFER_REWARD)
        /// </summary>
        /// <param name="rewardChoice">Index of reward to choose (0 if no choice)</param>
        public void TurnInQuest(uint rewardChoice = 0)
        {
            if (!HasPendingQuestTurnIn)
            {
                Log("TurnInQuest: No pending quest turn-in", LogLevel.Warning);
                return;
            }
            
            var packet = new OutPacket(WorldCommand.CMSG_QUESTGIVER_CHOOSE_REWARD);
            packet.Write(PendingQuestTurnInGuid);
            packet.Write(PendingQuestTurnInId);
            packet.Write(rewardChoice);
            SendPacket(packet);
            
            Log($"Turning in quest {PendingQuestTurnInId} to NPC 0x{PendingQuestTurnInGuid:X}, reward choice {rewardChoice}", LogLevel.Debug);
        }
        
        /// <summary>
        /// Turn in a specific quest to a specific NPC
        /// </summary>
        public void TurnInQuest(ulong npcGuid, uint questId, uint rewardChoice = 0)
        {
            var packet = new OutPacket(WorldCommand.CMSG_QUESTGIVER_CHOOSE_REWARD);
            packet.Write(npcGuid);
            packet.Write(questId);
            packet.Write(rewardChoice);
            SendPacket(packet);
            
            Log($"Turning in quest {questId} to NPC 0x{npcGuid:X}, reward choice {rewardChoice}", LogLevel.Debug);
        }
        
        /// <summary>
        /// Request to complete a quest (used when NPC requests items)
        /// </summary>
        public void CompleteQuest(ulong npcGuid, uint questId)
        {
            var packet = new OutPacket(WorldCommand.CMSG_QUESTGIVER_COMPLETE_QUEST);
            packet.Write(npcGuid);
            packet.Write(questId);
            SendPacket(packet);
            
            Log($"Requesting quest completion: Quest {questId} at NPC 0x{npcGuid:X}", LogLevel.Debug);
        }

        /// <summary>
        /// Request quest details from an NPC for a specific quest
        /// </summary>
        public void QueryQuest(ulong npcGuid, uint questId)
        {
            var packet = new OutPacket(WorldCommand.CMSG_QUESTGIVER_QUERY_QUEST);
            packet.Write(npcGuid);
            packet.Write(questId);
            packet.Write((byte)0); // autoAccept
            SendPacket(packet);
            
            Log($"Querying quest {questId} from NPC 0x{npcGuid:X}", LogLevel.Debug);
        }
        #endregion

        #region Combat Methods
        /// <summary>
        /// Current combat target GUID
        /// </summary>
        public ulong CombatTargetGuid { get; protected set; }

        /// <summary>
        /// Returns true if currently in combat (attacking something)
        /// </summary>
        public bool IsInCombat => CombatTargetGuid != 0;

        /// <summary>
        /// Set the current target
        /// </summary>
        public void SetTarget(ulong targetGuid)
        {
            var packet = new OutPacket(WorldCommand.CMSG_SET_SELECTION);
            packet.Write(targetGuid);
            SendPacket(packet);
        }

        /// <summary>
        /// Start auto-attacking a target
        /// </summary>
        public void StartAttack(ulong targetGuid)
        {
            SetTarget(targetGuid);
            
            var packet = new OutPacket(WorldCommand.CMSG_ATTACK_SWING);
            packet.Write(targetGuid);
            SendPacket(packet);
            
            CombatTargetGuid = targetGuid;
        }

        /// <summary>
        /// Stop auto-attacking
        /// </summary>
        public void StopAttack()
        {
            var packet = new OutPacket(WorldCommand.CMSG_ATTACK_STOP);
            SendPacket(packet);
            
            CombatTargetGuid = 0;
        }

        /// <summary>
        /// Cast a spell on the current target
        /// </summary>
        /// <param name="spellId">The spell ID to cast</param>
        public void CastSpell(uint spellId)
        {
            CastSpell(spellId, CombatTargetGuid);
        }

        /// <summary>
        /// Cast a spell on a specific target
        /// </summary>
        /// <param name="spellId">The spell ID to cast</param>
        /// <param name="targetGuid">The target GUID (0 for self)</param>
        public void CastSpell(uint spellId, ulong targetGuid)
        {
            var packet = new OutPacket(WorldCommand.CMSG_CAST_SPELL);
            packet.Write((byte)0); // cast count
            packet.Write(spellId);
            packet.Write((byte)0); // cast flags
            
            // Target flags - simplified for unit target
            if (targetGuid != 0)
            {
                packet.Write((uint)0x0002); // TARGET_FLAG_UNIT
                packet.WritePacketGuid(targetGuid);
            }
            else
            {
                packet.Write((uint)0x0000); // TARGET_FLAG_SELF
            }
            
            SendPacket(packet);
        }

        /// <summary>
        /// Cast a spell on self
        /// </summary>
        public void CastSpellOnSelf(uint spellId)
        {
            CastSpell(spellId, 0);
        }

        #region Loot Actions
        /// <summary>
        /// Request to open loot on a corpse/object
        /// </summary>
        /// <param name="targetGuid">GUID of the lootable target</param>
        public void RequestLoot(ulong targetGuid)
        {
            var packet = new OutPacket(WorldCommand.CMSG_LOOT);
            packet.Write(targetGuid);
            SendPacket(packet);

            Log($"Requesting loot from 0x{targetGuid:X}", LogLevel.Debug);
        }

        /// <summary>
        /// Loot all money from the current loot window
        /// </summary>
        public void LootMoney()
        {
            if (!CurrentLoot.IsOpen)
            {
                Log("LootMoney: No loot window open", LogLevel.Warning);
                return;
            }

            if (CurrentLoot.Gold == 0)
            {
                return;
            }

            var packet = new OutPacket(WorldCommand.CMSG_LOOT_MONEY);
            SendPacket(packet);

            Log($"Looting {CurrentLoot.Gold / 100f:F2}g", LogLevel.Debug);
        }

        /// <summary>
        /// Loot a specific item by slot index
        /// </summary>
        /// <param name="slot">Slot index in loot window</param>
        public void LootItem(byte slot)
        {
            if (!CurrentLoot.IsOpen)
            {
                Log("LootItem: No loot window open", LogLevel.Warning);
                return;
            }

            var packet = new OutPacket(WorldCommand.CMSG_AUTOSTORE_LOOT_ITEM);
            packet.Write(slot);
            SendPacket(packet);

            Log($"Looting item from slot {slot}", LogLevel.Debug);
        }

        /// <summary>
        /// Loot all items from the current loot window
        /// </summary>
        public void LootAllItems()
        {
            if (!CurrentLoot.IsOpen)
            {
                Log("LootAllItems: No loot window open", LogLevel.Warning);
                return;
            }

            // Loot money first
            if (CurrentLoot.Gold > 0)
            {
                LootMoney();
            }

            // Loot each item
            foreach (var item in CurrentLoot.Items.ToList())
            {
                if (item.SlotType == LootSlotType.AllowLoot ||
                    item.SlotType == LootSlotType.Owner)
                {
                    LootItem(item.Slot);
                }
            }
        }

        /// <summary>
        /// Close the loot window / release loot
        /// </summary>
        public void ReleaseLoot()
        {
            if (!CurrentLoot.IsOpen)
            {
                return;
            }

            var packet = new OutPacket(WorldCommand.CMSG_LOOT_RELEASE);
            packet.Write(CurrentLoot.LootGuid);
            SendPacket(packet);

            Log($"Releasing loot from 0x{CurrentLoot.LootGuid:X}", LogLevel.Debug);
        }
        #endregion

        #region Item Query and Equip
        /// <summary>
        /// Query item template data from the server
        /// </summary>
        /// <param name="entry">Item entry ID</param>
        public void QueryItem(uint entry)
        {
            // Don't query if already cached or pending
            if (ItemCache.Get(entry) != null || ItemCache.IsPending(entry))
                return;

            ItemCache.MarkPending(entry);
            var packet = new OutPacket(WorldCommand.CMSG_ITEM_QUERY_SINGLE);
            packet.Write(entry);
            SendPacket(packet);

            Log($"Querying item template {entry}", LogLevel.Debug);
        }

        /// <summary>
        /// Auto-equip an item from a bag slot
        /// </summary>
        /// <param name="containerSlot">Container slot (255 for main backpack)</param>
        /// <param name="slot">Slot within the container (0-15 for backpack)</param>
        public void AutoEquipItem(byte containerSlot, byte slot)
        {
            var packet = new OutPacket(WorldCommand.CMSG_AUTOEQUIP_ITEM);
            packet.Write(containerSlot);
            packet.Write(slot);
            SendPacket(packet);

            Log($"Auto-equipping item from container {containerSlot} slot {slot}", LogLevel.Debug);
        }

        /// <summary>
        /// Sell an item to a vendor
        /// </summary>
        /// <param name="vendorGuid">GUID of the vendor NPC</param>
        /// <param name="itemGuid">GUID of the item to sell</param>
        /// <param name="count">Amount to sell (1 for non-stackable items)</param>
        public void SellItem(ulong vendorGuid, ulong itemGuid, uint count = 1)
        {
            var packet = new OutPacket(WorldCommand.CMSG_SELL_ITEM);
            packet.Write(vendorGuid);
            packet.Write(itemGuid);
            packet.Write(count);
            SendPacket(packet);

            Log($"Selling item 0x{itemGuid:X} to vendor 0x{vendorGuid:X} (count: {count})", LogLevel.Debug);
        }

        [PacketHandler(WorldCommand.SMSG_SELL_ITEM)]
        protected void HandleSellItemResponse(InPacket packet)
        {
            ulong vendorGuid = packet.ReadUInt64();
            ulong itemGuid = packet.ReadUInt64();
            byte error = packet.ReadByte();

            if (error != 0)
            {
                // Error codes: 1=CantFind, 2=CantSell (quest/soulbound), 3=CantFindVendor, 4=YouDontOwnThat, 5=Unknown, 6=OnlyEmptyBag
                string errorMsg = error switch
                {
                    1 => "CantFindItem",
                    2 => "CantSellItem",
                    3 => "CantFindVendor",
                    4 => "YouDontOwnThat",
                    5 => "Unknown",
                    6 => "OnlyEmptyBag",
                    _ => $"Error{error}"
                };
                Log($"SellItem failed: {errorMsg} (item: 0x{itemGuid:X})", LogLevel.Warning);
            }
        }

        [PacketHandler(WorldCommand.SMSG_ITEM_QUERY_SINGLE_RESPONSE)]
        protected void HandleItemQueryResponse(InPacket packet)
        {
            uint entry = packet.ReadUInt32();

            // High bit set means item not found
            if ((entry & 0x80000000) != 0)
            {
                uint actualEntry = entry & 0x7FFFFFFF;
                ItemCache.ClearPending(actualEntry);
                Log($"Item {actualEntry} not found in database", LogLevel.Warning);
                return;
            }

            var template = new ItemTemplate { Entry = entry };

            template.ItemClass = (ItemClass)packet.ReadUInt32();
            template.Subclass = packet.ReadUInt32();
            packet.ReadInt32();  // SoundOverrideSubclass

            // Read 4 localized names, use the first non-empty one
            template.Name = packet.ReadCString();
            packet.ReadCString();  // Name2
            packet.ReadCString();  // Name3
            packet.ReadCString();  // Name4

            template.DisplayId = packet.ReadUInt32();
            template.Quality = (ItemQuality)packet.ReadUInt32();
            template.Flags = packet.ReadUInt32();
            template.Flags2 = packet.ReadUInt32();
            template.BuyPrice = packet.ReadUInt32();
            template.SellPrice = packet.ReadUInt32();
            template.InventoryType = (InventoryType)packet.ReadUInt32();
            template.AllowableClass = packet.ReadUInt32();
            template.AllowableRace = packet.ReadUInt32();
            template.ItemLevel = packet.ReadUInt32();
            template.RequiredLevel = packet.ReadUInt32();
            template.RequiredSkill = packet.ReadUInt32();
            template.RequiredSkillRank = packet.ReadUInt32();

            // Skip fields we don't need
            packet.ReadUInt32();  // RequiredSpell
            packet.ReadUInt32();  // RequiredHonorRank
            packet.ReadUInt32();  // RequiredCityRank
            packet.ReadUInt32();  // RequiredReputationFaction
            packet.ReadUInt32();  // RequiredReputationRank
            packet.ReadInt32();   // MaxCount
            packet.ReadInt32();   // Stackable
            packet.ReadUInt32();  // ContainerSlots

            // Stats
            uint statsCount = packet.ReadUInt32();
            for (int i = 0; i < statsCount && i < 10; i++)
            {
                var statType = (ItemStatType)packet.ReadUInt32();
                int statValue = packet.ReadInt32();
                template.Stats.Add(new ItemStat(statType, statValue));
            }

            // Skip scaling stat fields
            packet.ReadUInt32();  // ScalingStatDistribution
            packet.ReadUInt32();  // ScalingStatValue

            // Damage - read first damage entry (primary weapon damage)
            template.MinDamage = packet.ReadSingle();
            template.MaxDamage = packet.ReadSingle();
            packet.ReadUInt32();  // DamageType

            // Skip remaining 4 damage types
            for (int i = 0; i < 4; i++)
            {
                packet.ReadSingle();  // Min
                packet.ReadSingle();  // Max
                packet.ReadUInt32();  // Type
            }

            template.Armor = packet.ReadUInt32();

            // We skip the rest of the packet (holy/fire/nature resist, delay, ammo type, etc.)
            // The attack speed is in the delay field but we'd need to read more to get there
            // For now, estimate from item level for weapons
            if (template.ItemClass == ItemClass.Weapon && template.MinDamage > 0)
            {
                // Rough estimate: base 2.0 speed for one-hand, 3.0 for two-hand
                if (template.InventoryType == InventoryType.TwoHandWeapon)
                    template.AttackSpeed = 3000;  // 3.0 sec
                else
                    template.AttackSpeed = 2000;  // 2.0 sec
            }

            ItemCache.Add(template);

            Log($"Cached item template: {template.Name} (Entry: {entry}, iLvl: {template.ItemLevel}, Quality: {template.Quality})", LogLevel.Debug);
        }
        #endregion

        [PacketHandler(WorldCommand.SMSG_ATTACK_START)]
        protected void HandleAttackStart(InPacket packet)
        {
            var attackerGuid = packet.ReadUInt64();
            var victimGuid = packet.ReadUInt64();
            
            if (attackerGuid == Player.GUID)
            {
                CombatTargetGuid = victimGuid;
            }
        }

        [PacketHandler(WorldCommand.SMSG_ATTACK_STOP)]
        protected void HandleAttackStop(InPacket packet)
        {
            var attackerGuidPacked = packet.ReadPackedGuid();
            var victimGuidPacked = packet.ReadPackedGuid();
            
            if (attackerGuidPacked == Player.GUID)
            {
                CombatTargetGuid = 0;
            }
        }

        [PacketHandler(WorldCommand.SMSG_ATTACKERSTATEUPDATE)]
        protected void HandleAttackerStateUpdate(InPacket packet)
        {
            // Combat log update - silently accept for now
            // Could be used to track damage dealt/received
        }

        #region Loot Handlers
        [PacketHandler(WorldCommand.SMSG_LOOT_RESPONSE)]
        protected void HandleLootResponse(InPacket packet)
        {
            CurrentLoot.Clear();

            CurrentLoot.LootGuid = packet.ReadUInt64();
            CurrentLoot.Type = (LootType)packet.ReadByte();

            if (CurrentLoot.Type == LootType.None)
            {
                // Loot failed - corpse already looted or no permission
                Log("Loot failed: no loot available", LogLevel.Debug);
                return;
            }

            CurrentLoot.Gold = packet.ReadUInt32();
            byte itemCount = packet.ReadByte();

            for (int i = 0; i < itemCount; i++)
            {
                var item = new LootItem
                {
                    Slot = packet.ReadByte(),
                    ItemId = packet.ReadUInt32(),
                    Count = packet.ReadUInt32(),
                    DisplayId = packet.ReadUInt32(),
                    RandomPropertySeed = packet.ReadInt32(),
                    RandomPropertyId = packet.ReadInt32(),
                    SlotType = (LootSlotType)packet.ReadByte()
                };
                CurrentLoot.Items.Add(item);
            }

            CurrentLoot.IsOpen = true;

            Log($"Loot window opened: {CurrentLoot.Gold / 100f:F2}g, {CurrentLoot.Items.Count} items", LogLevel.Debug);
        }

        [PacketHandler(WorldCommand.SMSG_LOOT_RELEASE_RESPONSE)]
        protected void HandleLootReleaseResponse(InPacket packet)
        {
            var guid = packet.ReadUInt64();
            var unknown = packet.ReadByte(); // Always 1

            if (guid == CurrentLoot.LootGuid)
            {
                Log("Loot window closed", LogLevel.Debug);
                CurrentLoot.Clear();
            }
        }

        [PacketHandler(WorldCommand.SMSG_LOOT_REMOVED)]
        protected void HandleLootRemoved(InPacket packet)
        {
            byte slot = packet.ReadByte();

            // Remove item from our tracked loot
            var item = CurrentLoot.Items.FirstOrDefault(i => i.Slot == slot);
            if (item != null)
            {
                CurrentLoot.Items.Remove(item);
                Log($"Looted item from slot {slot}", LogLevel.Debug);
            }
        }

        [PacketHandler(WorldCommand.SMSG_LOOT_MONEY_NOTIFY)]
        protected void HandleLootMoneyNotify(InPacket packet)
        {
            uint gold = packet.ReadUInt32();
            // byte isGroup = packet.ReadByte(); // 0 = solo, 1 = shared

            Log($"Looted {gold / 100f:F2}g", LogLevel.Debug);
            CurrentLoot.Gold = 0;
        }
        #endregion
        #endregion

        class UpdateObjectHandler
        {
            AutomatedGame game;

            uint blockCount;
            ObjectUpdateType updateType;
            ulong guid;
            TypeID objectType;
            ObjectUpdateFlags flags;
            MovementInfo movementInfo;
            Dictionary<UnitMoveType, float> movementSpeeds;
            SplineFlags splineFlags;
            float splineFacingAngle;
            ulong splineFacingTargetGuid;
            Vector3 splineFacingPointX;
            int splineTimePassed;
            int splineDuration;
            uint splineId;
            float splineVerticalAcceleration;
            int splineEffectStartTime;
            List<Vector3> splinePoints;
            SplineEvaluationMode splineEvaluationMode;
            Vector3 splineEndPoint;

            ulong transportGuid;
            Vector3 position;
            Vector3 transportOffset;
            float o;
            float corpseOrientation;

            uint lowGuid;
            ulong targetGuid;
            uint transportTimer;
            uint vehicledID;
            float vehicleOrientation;
            long goRotation;

            Dictionary<int, uint> updateFields;

            List<ulong> outOfRangeGuids;

            public UpdateObjectHandler(AutomatedGame game)
            {
                this.game = game;
                movementSpeeds = new Dictionary<UnitMoveType, float>();
                splinePoints = new List<Vector3>();
                updateFields = new Dictionary<int, uint>();
                outOfRangeGuids = new List<ulong>();
            }

            public void HandleUpdatePacket(InPacket packet)
            {
                blockCount = packet.ReadUInt32();
                for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
                {
                    ResetData();

                    updateType = (ObjectUpdateType)packet.ReadByte();

                    switch (updateType)
                    {
                        case ObjectUpdateType.UPDATETYPE_VALUES:
                            guid = packet.ReadPackedGuid();
                            ReadValuesUpdateData(packet);
                            break;
                        case ObjectUpdateType.UPDATETYPE_MOVEMENT:
                            guid = packet.ReadPackedGuid();
                            ReadMovementUpdateData(packet);
                            break;
                        case ObjectUpdateType.UPDATETYPE_CREATE_OBJECT:
                        case ObjectUpdateType.UPDATETYPE_CREATE_OBJECT2:
                            guid = packet.ReadPackedGuid();
                            objectType = (TypeID)packet.ReadByte();
                            ReadMovementUpdateData(packet);
                            ReadValuesUpdateData(packet);
                            break;
                        case ObjectUpdateType.UPDATETYPE_OUT_OF_RANGE_OBJECTS:
                            var guidCount = packet.ReadUInt32();
                            for (var guidIndex = 0; guidIndex < guidCount; guidIndex++)
                                outOfRangeGuids.Add(packet.ReadPackedGuid());
                            break;
                        case ObjectUpdateType.UPDATETYPE_NEAR_OBJECTS:
                            break;
                    }

                    HandleUpdateData();
                }
            }

            public void HandleMovementPacket(InPacket packet)
            {
                ResetData();
                updateType = ObjectUpdateType.UPDATETYPE_MOVEMENT;
                guid = packet.ReadPackedGuid();
                ReadMovementInfo(packet);

                // Don't apply server position corrections for our own player while actively moving
                // The server echoes back our movement packets, but applying them causes stuttering
                // because it fights with our pathfinding interpolation. We only need corrections
                // for other players/units, or for teleports (which use different opcodes).
                // Our own position is tracked via the Path class during movement.

                HandleUpdateData();
            }

            public void HandleMonsterMovementPacket(InPacket packet)
            {
                ResetData();
                updateType = ObjectUpdateType.UPDATETYPE_MOVEMENT;
                guid = packet.ReadPackedGuid();
                byte unk = packet.ReadByte();
                WorldObject worldObject = game.Objects[guid];
                worldObject.Set(packet.ReadVector3());
            }

            void ResetData()
            {
                updateType = ObjectUpdateType.UPDATETYPE_VALUES;
                guid = 0;
                lowGuid = 0;
                movementSpeeds.Clear();
                splinePoints.Clear();
                updateFields.Clear();
                outOfRangeGuids.Clear();
                movementInfo = null;
            }

            void ReadMovementUpdateData(InPacket packet)
            {
                flags = (ObjectUpdateFlags)packet.ReadUInt16();
                if (flags.HasFlag(ObjectUpdateFlags.UPDATEFLAG_LIVING))
                {
                    ReadMovementInfo(packet);

                    movementSpeeds = new Dictionary<UnitMoveType,float>();
                    movementSpeeds[UnitMoveType.MOVE_WALK] = packet.ReadSingle();
                    movementSpeeds[UnitMoveType.MOVE_RUN] = packet.ReadSingle();
                    movementSpeeds[UnitMoveType.MOVE_RUN_BACK] = packet.ReadSingle();
                    movementSpeeds[UnitMoveType.MOVE_SWIM] = packet.ReadSingle();
                    movementSpeeds[UnitMoveType.MOVE_SWIM_BACK] = packet.ReadSingle();
                    movementSpeeds[UnitMoveType.MOVE_FLIGHT] = packet.ReadSingle();
                    movementSpeeds[UnitMoveType.MOVE_FLIGHT_BACK] = packet.ReadSingle();
                    movementSpeeds[UnitMoveType.MOVE_TURN_RATE] = packet.ReadSingle();
                    movementSpeeds[UnitMoveType.MOVE_PITCH_RATE] = packet.ReadSingle();

                    if (movementInfo.Flags.HasFlag(MovementFlags.MOVEMENTFLAG_SPLINE_ENABLED))
                    {
                        splineFlags = (SplineFlags)packet.ReadUInt32();
                        if (splineFlags.HasFlag(SplineFlags.Final_Angle))
                            splineFacingAngle = packet.ReadSingle();
                        else if (splineFlags.HasFlag(SplineFlags.Final_Target))
                            splineFacingTargetGuid = packet.ReadUInt64();
                        else if (splineFlags.HasFlag(SplineFlags.Final_Point))
                            splineFacingPointX = packet.ReadVector3();

                        splineTimePassed = packet.ReadInt32();
                        splineDuration = packet.ReadInt32();
                        splineId = packet.ReadUInt32();
                        packet.ReadSingle();
                        packet.ReadSingle();
                        splineVerticalAcceleration = packet.ReadSingle();
                        splineEffectStartTime = packet.ReadInt32();
                        uint splineCount = packet.ReadUInt32();
                        for (uint index = 0; index < splineCount; index++)
                            splinePoints.Add(packet.ReadVector3());
                        splineEvaluationMode = (SplineEvaluationMode)packet.ReadByte();
                        splineEndPoint = packet.ReadVector3();
                    }
                }
                else if (flags.HasFlag(ObjectUpdateFlags.UPDATEFLAG_POSITION))
                {
                    transportGuid = packet.ReadPackedGuid();
                    position = packet.ReadVector3();
                    transportOffset = packet.ReadVector3();
                    o = packet.ReadSingle();
                    corpseOrientation = packet.ReadSingle();
                }
                else if (flags.HasFlag(ObjectUpdateFlags.UPDATEFLAG_STATIONARY_POSITION))
                {
                    position = packet.ReadVector3();
                    o = packet.ReadSingle();
                }

                if (flags.HasFlag(ObjectUpdateFlags.UPDATEFLAG_UNKNOWN))
                    packet.ReadUInt32();

                if (flags.HasFlag(ObjectUpdateFlags.UPDATEFLAG_LOWGUID))
                    lowGuid = packet.ReadUInt32();

                if (flags.HasFlag(ObjectUpdateFlags.UPDATEFLAG_HAS_TARGET))
                    targetGuid = packet.ReadPackedGuid();

                if (flags.HasFlag(ObjectUpdateFlags.UPDATEFLAG_TRANSPORT))
                    transportTimer = packet.ReadUInt32();

                if (flags.HasFlag(ObjectUpdateFlags.UPDATEFLAG_VEHICLE))
                {
                    vehicledID = packet.ReadUInt32();
                    vehicleOrientation = packet.ReadSingle();
                }

                if (flags.HasFlag(ObjectUpdateFlags.UPDATEFLAG_ROTATION))
                    goRotation = packet.ReadInt64();
            }

            void ReadMovementInfo(InPacket packet)
            {
                movementInfo = new MovementInfo(packet);
            }

            private void ReadValuesUpdateData(InPacket packet)
            {
                byte blockCount = packet.ReadByte();
                int[] updateMask = new int[blockCount];
                for (var i = 0; i < blockCount; i++)
                    updateMask[i] = packet.ReadInt32();
                var mask = new BitArray(updateMask);

                for (var i = 0; i < mask.Count; ++i)
                {
                    if (!mask[i])
                        continue;

                    updateFields[i] = packet.ReadUInt32();
                }
            }

            private void HandleUpdateData()
            {
                if (guid == game.Player.GUID)
                {
                    // Apply movement speeds if we received them
                    if (movementSpeeds.ContainsKey(UnitMoveType.MOVE_RUN))
                    {
                        float runSpeed = movementSpeeds[UnitMoveType.MOVE_RUN];
                        if (Math.Abs(game.Player.Speed - runSpeed) > 0.01f)
                        {
                            game.Log($"Setting Player run speed to {runSpeed:F2}", LogLevel.Debug);
                            game.Player.Speed = runSpeed;
                        }
                    }

                    foreach (var pair in updateFields)
                        game.Player[pair.Key] = pair.Value;

                    // CRITICAL: Ensure CMSG_SET_ACTIVE_MOVER is sent after player object creation
                    game.SendSetActiveMover();
                }
                else
                {
                    switch (updateType)
                    {
                        case ObjectUpdateType.UPDATETYPE_VALUES:
                            {
                                WorldObject worldObject = game.Objects[guid];
                                foreach (var pair in updateFields)
                                    worldObject[pair.Key] = pair.Value;
                                break;
                            }
                        case ObjectUpdateType.UPDATETYPE_MOVEMENT:
                            {
                                if (movementInfo != null)
                                {
                                    WorldObject worldObject = game.Objects[guid];
                                    worldObject.Set(movementInfo.Position);
                                    worldObject.O = movementInfo.O;
                                }
                                break;
                            }
                        case ObjectUpdateType.UPDATETYPE_CREATE_OBJECT:
                        case ObjectUpdateType.UPDATETYPE_CREATE_OBJECT2:
                            {
                                WorldObject worldObject = new WorldObject();
                                worldObject.GUID = guid;
                                if (movementInfo != null)
                                {
                                    worldObject.Set(movementInfo.Position);
                                    worldObject.O = movementInfo.O;
                                }
                                worldObject.MapID = game.Player.MapID;
                                foreach (var pair in updateFields)
                                    worldObject[pair.Key] = pair.Value;

#if DEBUG
                                if (game.Objects.ContainsKey(guid))
                                    game.Log($"{updateType} called with guid 0x{guid:X} already added", LogLevel.Debug);
#endif
                                game.Objects[guid] = worldObject;

                                if (worldObject.IsType(HighGuid.Player))
                                {
                                    OutPacket nameQuery = new OutPacket(WorldCommand.CMSG_NAME_QUERY);
                                    nameQuery.Write(guid);
                                    game.SendPacket(nameQuery);
                                }
                                break;
                            }
                        default:
                            break;
                    }
                }

                foreach (var outOfRangeGuid in outOfRangeGuids)
                {
                    WorldObject worldObject;
                    if (game.Objects.TryGetValue(outOfRangeGuid, out worldObject))
                    {
                        worldObject.ResetPosition();
                        game.Objects.Remove(outOfRangeGuid);
                    }
                }
            }
        }

        [PacketHandler(WorldCommand.SMSG_DESTROY_OBJECT)]
        protected void HandleDestroyObject(InPacket packet)
        {
            ulong guid = packet.ReadUInt64();
            WorldObject worldObject;
            if (Objects.TryGetValue(guid, out worldObject))
            {
                worldObject.ResetPosition();
                Objects.Remove(guid);
            }
        }

        [PacketHandler(WorldCommand.SMSG_ALL_ACHIEVEMENT_DATA)]
        protected void HandleAllAchievementData(InPacket packet)
        {
            CompletedAchievements.Clear();
            AchievementCriterias.Clear();

            for (;;)
            {
                uint achievementId = packet.ReadUInt32();
                if (achievementId == 0xFFFFFFFF)
                    break;

                packet.ReadPackedTime();

                CompletedAchievements.Add(achievementId);
            }

            for (;;)
            {
                uint criteriaId = packet.ReadUInt32();
                if (criteriaId == 0xFFFFFFFF)
                    break;
                ulong criteriaCounter = packet.ReadPackedGuid();
                packet.ReadPackedGuid();
                packet.ReadInt32();
                packet.ReadPackedTime();
                packet.ReadInt32();
                packet.ReadInt32();

                AchievementCriterias[criteriaId] = criteriaCounter;
            }
        }

        [PacketHandler(WorldCommand.SMSG_CRITERIA_UPDATE)]
        protected void HandleCriteriaUpdate(InPacket packet)
        {
            uint criteriaId = packet.ReadUInt32();
            ulong criteriaCounter = packet.ReadPackedGuid();

            AchievementCriterias[criteriaId] = criteriaCounter;
        }

        [PacketHandler(WorldCommand.SMSG_GROUP_LIST)]
        protected void HandlePartyList(InPacket packet)
        {
            GroupType groupType = (GroupType)packet.ReadByte();
            packet.ReadByte();
            packet.ReadByte();
            packet.ReadByte();
            if (groupType.HasFlag(GroupType.GROUPTYPE_LFG))
            {
                packet.ReadByte();
                packet.ReadUInt32();
            }
            packet.ReadUInt64();
            packet.ReadUInt32();
            uint membersCount = packet.ReadUInt32();
            GroupMembersGuids.Clear();
            for (uint index = 0; index < membersCount; index++)
            {
                packet.ReadCString();
                UInt64 memberGuid = packet.ReadUInt64();
                GroupMembersGuids.Add(memberGuid);
                packet.ReadByte();
                packet.ReadByte();
                packet.ReadByte();
                packet.ReadByte();
            }
            GroupLeaderGuid = packet.ReadUInt64();
        }

        [PacketHandler(WorldCommand.SMSG_GROUP_DESTROYED)]
        protected void HandlePartyDisband(InPacket packet)
        {
            GroupLeaderGuid = 0;
            GroupMembersGuids.Clear();
        }
        #endregion

        #region Unused Methods
        public override void Log(string message, LogLevel level = LogLevel.Info)
        {
#if DEBUG_LOG
            Console.WriteLine(message);
#endif
        }

        public override void LogLine(string message, LogLevel level = LogLevel.Info)
        {
#if !DEBUG_LOG
            if (level > LogLevel.Debug)
#endif
            Console.WriteLine(Username + " - " + message);
        }

        public override void LogDebug(string message)
        {
            LogLine(message, LogLevel.Debug);
        }

        public override void LogException(string message)
        {
            Console.WriteLine(Username + " - " + message);
        }

        public override void LogException(Exception ex)
        {
            Console.WriteLine(string.Format(Username + " - {0} {1}", ex.Message, ex.StackTrace));
        }

        public IGameUI UI
        {
            get
            {
                return this;
            }
        }

        public override void PresentChatMessage(ChatMessage message)
        {
        }
        #endregion

        #region Triggers Handling
        IteratedList<Trigger> Triggers;
        int triggerCounter;

        public int AddTrigger(Trigger trigger)
        {
            triggerCounter++;
            trigger.Id = triggerCounter;
            Triggers.Add(trigger);
            return triggerCounter;
        }

        public IEnumerable<int> AddTriggers(IEnumerable<Trigger> triggers)
        {
            var triggerIds = new List<int>();
            foreach (var trigger in triggers)
                triggerIds.Add(AddTrigger(trigger));
            return triggerIds;
        }

        public bool RemoveTrigger(int triggerId)
        {
            return Triggers.RemoveAll(trigger => trigger.Id == triggerId) > 0;
        }

        public void ClearTriggers()
        {
            Triggers.Clear();
        }

        public void ResetTriggers()
        {
            Triggers.ForEach(trigger => trigger.Reset());
        }

        public void HandleTriggerInput(TriggerActionType type, params object[] inputs)
        {
            Triggers.ForEach(trigger => trigger.HandleInput(type, inputs));
        }

        void OnFieldUpdate(object s, UpdateFieldEventArg e)
        {
            HandleTriggerInput(TriggerActionType.UpdateField, e);
        }

        public async Task<InPacket> WaitForPacket(WorldCommand opcode, int waitMilliseconds = -1)
        {
            var completion = new TaskCompletionSource<InPacket>();
            InPacket result = null;

            int triggerId = 0;
            triggerId = AddTrigger(new Trigger(new[]
            {
                new OpcodeTriggerAction(opcode, packet =>
                {
                    result = packet as InPacket;
                    return true;
                }),
            }, () =>
            {
                completion.SetResult(result);
                RemoveTrigger(triggerId);
            }));

            return await Task.WhenAny(completion.Task, Task.Run(async () =>
            {
                await Task.Delay(waitMilliseconds).ConfigureAwait(false);
                return (InPacket)null;
            })
            ).Result.ConfigureAwait(false);
        }
        #endregion
    }

    class MovementInfo
    {
        public MovementFlags Flags;
        public MovementFlags2 Flags2;
        public uint Time;
        public Vector3 Position;
        public float O;

        public ulong TransportGuid;
        public Vector3 TransportPosition;
        public float TransportO;
        public ulong TransportTime;
        public byte TransportSeat;
        public ulong TransportTime2;

        public float Pitch;

        public ulong FallTime;

        public float JumpZSpeed;
        public float JumpSinAngle;
        public float JumpCosAngle;
        public float JumpXYSpeed;

        public float SplineElevation;

        public MovementInfo(InPacket packet)
        {
            Flags = (MovementFlags)packet.ReadUInt32();
            Flags2 = (MovementFlags2)packet.ReadUInt16();
            Time = packet.ReadUInt32();
            Position = packet.ReadVector3();
            O = packet.ReadSingle();

            if (Flags.HasFlag(MovementFlags.MOVEMENTFLAG_ONTRANSPORT))
            {
                TransportGuid = packet.ReadPackedGuid();
                TransportPosition = packet.ReadVector3();
                TransportO = packet.ReadSingle();
                TransportTime = packet.ReadUInt32();
                TransportSeat = packet.ReadByte();
                if (Flags2.HasFlag(MovementFlags2.MOVEMENTFLAG2_INTERPOLATED_MOVEMENT))
                    TransportTime2 = packet.ReadUInt32();
            }

            if (Flags.HasFlag(MovementFlags.MOVEMENTFLAG_SWIMMING) || Flags.HasFlag(MovementFlags.MOVEMENTFLAG_FLYING)
                || Flags2.HasFlag(MovementFlags2.MOVEMENTFLAG2_ALWAYS_ALLOW_PITCHING))
                Pitch = packet.ReadSingle();

            FallTime = packet.ReadUInt32();

            if (Flags.HasFlag(MovementFlags.MOVEMENTFLAG_FALLING))
            {
                JumpZSpeed = packet.ReadSingle();
                JumpSinAngle = packet.ReadSingle();
                JumpCosAngle = packet.ReadSingle();
                JumpXYSpeed = packet.ReadSingle();
            }

            if (Flags.HasFlag(MovementFlags.MOVEMENTFLAG_SPLINE_ELEVATION))
                SplineElevation = packet.ReadSingle();
        }
    }
}
