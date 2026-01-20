using Client;
using Client.AI.Tasks;
using Client.UI;
using Client.World;
using Client.World.Entities;
using Client.World.Network;
using Client.World.Definitions;
using System;
using System.Collections.Generic;

namespace BotFarm.AI.Tasks
{
    /// <summary>
    /// Task that turns in a completed quest to an NPC.
    /// Automatically handles movement and talking to the NPC if needed.
    /// </summary>
    public class TurnInQuestTask : BaseTask
    {
        private enum TurnInQuestState { FindingNPC, MovingToNPC, TalkingToNPC, RequestingCompletion, TurningIn }

        private readonly uint npcEntry;
        private readonly uint questId;
        private readonly uint rewardChoice;
        private readonly Dictionary<Class, uint> classQuests;
        private readonly Dictionary<Class, uint> classNPCs;
        private uint resolvedQuestId;
        private uint resolvedNpcEntry;
        private WorldObject targetNpc = null;
        private TurnInQuestState state = TurnInQuestState.FindingNPC;
        private bool moveStarted = false;
        private bool talkSent = false;
        private DateTime talkTime;
        private bool completionRequested = false;
        private DateTime completionTime;
        private readonly float interactionRange = 5.0f;
        private readonly double timeoutSeconds = 5.0;

        public override string Name => classQuests != null || classNPCs != null
            ? $"TurnInClassQuest({resolvedQuestId})"
            : (questId != 0 ? $"TurnInQuest({questId})" : $"TurnInQuestAtNPC({npcEntry})");

        /// <summary>
        /// Turn in a specific quest to a specific NPC
        /// </summary>
        /// <param name="npcEntry">NPC entry ID (creature template ID)</param>
        /// <param name="questId">Quest ID to turn in</param>
        /// <param name="rewardChoice">Index of reward to choose (0 for first/only reward)</param>
        public TurnInQuestTask(uint npcEntry, uint questId, uint rewardChoice = 0)
        {
            this.npcEntry = npcEntry;
            this.questId = questId;
            this.rewardChoice = rewardChoice;
            this.classQuests = null;
            this.classNPCs = null;
            SetDelayPadding(RandomDelay(0.3f, 0.6f), RandomDelay(0.5f, 1.0f));
        }

        /// <summary>
        /// Turn in a class-specific quest to an NPC, with optional class-specific NPCs
        /// </summary>
        /// <param name="npcEntry">Fallback NPC entry ID</param>
        /// <param name="fallbackQuestId">Quest ID to use if player's class isn't in the map</param>
        /// <param name="rewardChoice">Index of reward to choose (0 for first/only reward)</param>
        /// <param name="classQuests">Map of class to quest ID (can be null)</param>
        /// <param name="classNPCs">Map of class to NPC entry (can be null)</param>
        public TurnInQuestTask(uint npcEntry, uint fallbackQuestId, uint rewardChoice, Dictionary<Class, uint> classQuests, Dictionary<Class, uint> classNPCs = null)
        {
            this.npcEntry = npcEntry;
            this.questId = fallbackQuestId;
            this.rewardChoice = rewardChoice;
            this.classQuests = classQuests;
            this.classNPCs = classNPCs;
            SetDelayPadding(RandomDelay(0.3f, 0.6f), RandomDelay(0.5f, 1.0f));
        }

        /// <summary>
        /// Turn in whatever quest is currently pending (after TalkToNPCTask)
        /// </summary>
        /// <param name="rewardChoice">Index of reward to choose (0 for first/only reward)</param>
        public TurnInQuestTask(uint rewardChoice = 0)
        {
            this.npcEntry = 0;
            this.questId = 0;
            this.rewardChoice = rewardChoice;
            this.classQuests = null;
            this.classNPCs = null;
            SetDelayPadding(RandomDelay(0.3f, 0.6f), RandomDelay(0.5f, 1.0f));
        }

        public override bool Start(AutomatedGame game)
        {
            if (!base.Start(game))
                return false;

            var playerClass = game.World.SelectedCharacter?.Class ?? Class.Warrior;

            // Resolve quest ID based on player class if class quests are specified
            if (classQuests != null)
            {
                if (classQuests.TryGetValue(playerClass, out var classQuestId))
                {
                    resolvedQuestId = classQuestId;
                    game.Log($"TurnInQuestTask: Using class-specific quest {resolvedQuestId} for {playerClass}", LogLevel.Debug);
                }
                else if (questId > 0)
                {
                    resolvedQuestId = questId;
                    game.Log($"TurnInQuestTask: No quest for {playerClass}, using fallback {questId}", LogLevel.Debug);
                }
                else
                {
                    game.Log($"TurnInQuestTask: No quest available for {playerClass}, skipping", LogLevel.Debug);
                    return false;
                }
            }
            else
            {
                resolvedQuestId = questId;
            }

            // Resolve NPC entry based on player class if class NPCs are specified
            if (classNPCs != null)
            {
                if (classNPCs.TryGetValue(playerClass, out var classNpcEntry))
                {
                    resolvedNpcEntry = classNpcEntry;
                    game.Log($"TurnInQuestTask: Using class-specific NPC {resolvedNpcEntry} for {playerClass}", LogLevel.Debug);
                }
                else if (npcEntry > 0)
                {
                    resolvedNpcEntry = npcEntry;
                    game.Log($"TurnInQuestTask: No NPC for {playerClass}, using fallback {npcEntry}", LogLevel.Debug);
                }
                else
                {
                    game.Log($"TurnInQuestTask: No NPC available for {playerClass}, skipping", LogLevel.Debug);
                    return false;
                }
            }
            else
            {
                resolvedNpcEntry = npcEntry;
            }

            state = TurnInQuestState.FindingNPC;
            return true;
        }

        protected override TaskResult UpdateTask(AutomatedGame game)
        {
            var botGame = game as BotGame;
            if (botGame == null)
            {
                ErrorMessage = "Game is not a BotGame instance";
                return TaskResult.Failed;
            }

            if (!game.Player.IsAlive)
            {
                ErrorMessage = "Player is dead";
                game.Log($"TurnInQuestTask: {ErrorMessage}", LogLevel.Warning);
                return TaskResult.Failed;
            }

            // If turning in the pending quest (no specific quest ID)
            if (resolvedQuestId == 0)
            {
                if (game.HasPendingQuestTurnIn)
                {
                    game.TurnInQuest(rewardChoice);
                    game.Log($"TurnInQuestTask: Turned in pending quest", LogLevel.Debug);
                    return TaskResult.Success;
                }
                else
                {
                    ErrorMessage = "No pending quest turn-in available";
                    game.Log($"TurnInQuestTask: {ErrorMessage}", LogLevel.Warning);
                    return TaskResult.Failed;
                }
            }

            switch (state)
            {
                case TurnInQuestState.FindingNPC:
                    return HandleFindingNPC(game);

                case TurnInQuestState.MovingToNPC:
                    return HandleMovingToNPC(game, botGame);

                case TurnInQuestState.TalkingToNPC:
                    return HandleTalkingToNPC(game);

                case TurnInQuestState.RequestingCompletion:
                    return HandleRequestingCompletion(game);

                case TurnInQuestState.TurningIn:
                    return HandleTurningIn(game);

                default:
                    return TaskResult.Failed;
            }
        }

        private TaskResult HandleFindingNPC(AutomatedGame game)
        {
            // Find the NPC
            targetNpc = MoveToNPCTask.FindNPCByEntry(game, resolvedNpcEntry);
            if (targetNpc == null)
            {
                ErrorMessage = $"NPC with entry {resolvedNpcEntry} not found nearby";
                game.Log($"TurnInQuestTask: {ErrorMessage}", LogLevel.Warning);
                return TaskResult.Failed;
            }

            float distance = (targetNpc - game.Player).Length;
            game.Log($"TurnInQuestTask: Found NPC entry {resolvedNpcEntry} at distance {distance:F2}m", LogLevel.Debug);

            if (distance <= interactionRange)
            {
                // Already in range, go straight to talking
                state = TurnInQuestState.TalkingToNPC;
            }
            else
            {
                // Need to move first
                state = TurnInQuestState.MovingToNPC;
            }

            return TaskResult.Running;
        }

        private TaskResult HandleMovingToNPC(AutomatedGame game, BotGame botGame)
        {
            if (targetNpc == null || !game.Objects.ContainsKey(targetNpc.GUID))
            {
                ErrorMessage = "NPC is no longer visible";
                game.Log($"TurnInQuestTask: {ErrorMessage}", LogLevel.Error);
                return TaskResult.Failed;
            }

            float distance = (targetNpc - game.Player).Length;

            if (distance <= interactionRange)
            {
                game.Log($"TurnInQuestTask: Arrived at NPC (distance: {distance:F2}m)", LogLevel.Debug);
                state = TurnInQuestState.TalkingToNPC;
                return TaskResult.Running;
            }

            if (!moveStarted)
            {
                game.Log($"TurnInQuestTask: Moving to NPC entry {resolvedNpcEntry} (distance: {distance:F2}m)", LogLevel.Debug);
                game.CancelActionsByFlag(ActionFlag.Movement);
                botGame.MoveTo(targetNpc.GetPosition());
                moveStarted = true;
            }

            return TaskResult.Running;
        }

        private TaskResult HandleTalkingToNPC(AutomatedGame game)
        {
            if (targetNpc == null || !game.Objects.ContainsKey(targetNpc.GUID))
            {
                ErrorMessage = "NPC is no longer visible";
                game.Log($"TurnInQuestTask: {ErrorMessage}", LogLevel.Error);
                return TaskResult.Failed;
            }

            if (!talkSent)
            {
                game.Log($"TurnInQuestTask: Talking to NPC entry {resolvedNpcEntry}", LogLevel.Debug);

                OutPacket gossipHello = new OutPacket(WorldCommand.CMSG_GOSSIP_HELLO);
                gossipHello.Write(targetNpc.GUID);
                game.SendPacket(gossipHello);

                talkSent = true;
                talkTime = DateTime.Now;
                return TaskResult.Running;
            }

            // Wait 0.5s for gossip response
            if ((DateTime.Now - talkTime).TotalSeconds > 0.5)
            {
                state = TurnInQuestState.RequestingCompletion;
            }

            return TaskResult.Running;
        }

        private TaskResult HandleRequestingCompletion(AutomatedGame game)
        {
            if (targetNpc == null || !game.Objects.ContainsKey(targetNpc.GUID))
            {
                ErrorMessage = "NPC is no longer visible";
                game.Log($"TurnInQuestTask: {ErrorMessage}", LogLevel.Error);
                return TaskResult.Failed;
            }

            if (!completionRequested)
            {
                game.Log($"TurnInQuestTask: Requesting quest {resolvedQuestId} completion", LogLevel.Debug);
                game.CompleteQuest(targetNpc.GUID, resolvedQuestId);
                completionRequested = true;
                completionTime = DateTime.Now;
                return TaskResult.Running;
            }

            // Check if we got the reward offer
            if (game.HasPendingQuestTurnIn && game.PendingQuestTurnInId == resolvedQuestId)
            {
                state = TurnInQuestState.TurningIn;
                return TaskResult.Running;
            }

            // Also try direct turn-in after a delay (some quests skip the items request)
            if ((DateTime.Now - completionTime).TotalSeconds > 1.0)
            {
                game.TurnInQuest(targetNpc.GUID, resolvedQuestId, rewardChoice);
                game.Log($"TurnInQuestTask: Sent direct turn-in for quest {resolvedQuestId}", LogLevel.Debug);
                return TaskResult.Success;
            }

            // Check timeout
            if ((DateTime.Now - completionTime).TotalSeconds > timeoutSeconds)
            {
                ErrorMessage = "Timeout waiting for quest turn-in";
                game.Log($"TurnInQuestTask: {ErrorMessage}", LogLevel.Warning);
                return TaskResult.Failed;
            }

            return TaskResult.Running;
        }

        private TaskResult HandleTurningIn(AutomatedGame game)
        {
            game.TurnInQuest(rewardChoice);
            game.Log($"TurnInQuestTask: Quest {resolvedQuestId} turned in successfully", LogLevel.Debug);
            return TaskResult.Success;
        }

        public override void Cleanup(AutomatedGame game)
        {
            targetNpc = null;
            state = TurnInQuestState.FindingNPC;
            moveStarted = false;
            talkSent = false;
            completionRequested = false;
        }
    }
}
