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
    /// Task that accepts a quest from an NPC.
    /// Automatically handles movement and talking to the NPC if needed.
    /// </summary>
    public class AcceptQuestTask : BaseTask
    {
        private enum AcceptQuestState { FindingNPC, MovingToNPC, TalkingToNPC, QueryingQuest, AcceptingQuest }

        private readonly uint npcEntry;
        private readonly uint questId;
        private readonly Dictionary<Class, uint> classQuests;
        private readonly Dictionary<Class, uint> classNPCs;
        private uint resolvedQuestId;
        private uint resolvedNpcEntry;
        private WorldObject targetNpc = null;
        private AcceptQuestState state = AcceptQuestState.FindingNPC;
        private bool moveStarted = false;
        private bool talkSent = false;
        private DateTime talkTime;
        private bool questQuerySent = false;
        private DateTime queryTime;
        private readonly float interactionRange = 5.0f;
        private readonly double timeoutSeconds = 5.0;

        public override string Name => classQuests != null || classNPCs != null
            ? $"AcceptClassQuest({resolvedQuestId})"
            : (questId != 0 ? $"AcceptQuest({questId})" : $"AcceptQuestFromNPC({npcEntry})");

        /// <summary>
        /// Accept a specific quest from a specific NPC
        /// </summary>
        /// <param name="npcEntry">NPC entry ID (creature template ID)</param>
        /// <param name="questId">Quest ID to accept</param>
        public AcceptQuestTask(uint npcEntry, uint questId)
        {
            this.npcEntry = npcEntry;
            this.questId = questId;
            this.classQuests = null;
            this.classNPCs = null;
            SetDelayPadding(RandomDelay(0.3f, 0.6f), RandomDelay(0.5f, 1.0f));
        }

        /// <summary>
        /// Accept a class-specific quest from an NPC, with optional class-specific NPCs
        /// </summary>
        /// <param name="npcEntry">Fallback NPC entry ID</param>
        /// <param name="fallbackQuestId">Quest ID to use if player's class isn't in the map</param>
        /// <param name="classQuests">Map of class to quest ID (can be null)</param>
        /// <param name="classNPCs">Map of class to NPC entry (can be null)</param>
        public AcceptQuestTask(uint npcEntry, uint fallbackQuestId, Dictionary<Class, uint> classQuests, Dictionary<Class, uint> classNPCs = null)
        {
            this.npcEntry = npcEntry;
            this.questId = fallbackQuestId;
            this.classQuests = classQuests;
            this.classNPCs = classNPCs;
            SetDelayPadding(RandomDelay(0.3f, 0.6f), RandomDelay(0.5f, 1.0f));
        }

        /// <summary>
        /// Accept whatever quest is currently being offered (after TalkToNPCTask)
        /// </summary>
        public AcceptQuestTask()
        {
            this.npcEntry = 0;
            this.questId = 0;
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
                    game.Log($"AcceptQuestTask: Using class-specific quest {resolvedQuestId} for {playerClass}", LogLevel.Debug);
                }
                else if (questId > 0)
                {
                    resolvedQuestId = questId;
                    game.Log($"AcceptQuestTask: No quest for {playerClass}, using fallback {questId}", LogLevel.Debug);
                }
                else
                {
                    game.Log($"AcceptQuestTask: No quest available for {playerClass}, skipping", LogLevel.Debug);
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
                    game.Log($"AcceptQuestTask: Using class-specific NPC {resolvedNpcEntry} for {playerClass}", LogLevel.Debug);
                }
                else if (npcEntry > 0)
                {
                    resolvedNpcEntry = npcEntry;
                    game.Log($"AcceptQuestTask: No NPC for {playerClass}, using fallback {npcEntry}", LogLevel.Debug);
                }
                else
                {
                    game.Log($"AcceptQuestTask: No NPC available for {playerClass}, skipping", LogLevel.Debug);
                    return false;
                }
            }
            else
            {
                resolvedNpcEntry = npcEntry;
            }

            state = AcceptQuestState.FindingNPC;
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
                game.Log($"AcceptQuestTask: {ErrorMessage}", LogLevel.Warning);
                return TaskResult.Failed;
            }

            // If accepting the pending quest offer (no specific quest ID)
            if (resolvedQuestId == 0)
            {
                if (game.HasPendingQuestOffer)
                {
                    game.AcceptQuest();
                    game.Log($"AcceptQuestTask: Accepted pending quest offer", LogLevel.Debug);
                    return TaskResult.Success;
                }
                else
                {
                    ErrorMessage = "No pending quest offer available";
                    game.Log($"AcceptQuestTask: {ErrorMessage}", LogLevel.Warning);
                    return TaskResult.Failed;
                }
            }

            switch (state)
            {
                case AcceptQuestState.FindingNPC:
                    return HandleFindingNPC(game);

                case AcceptQuestState.MovingToNPC:
                    return HandleMovingToNPC(game, botGame);

                case AcceptQuestState.TalkingToNPC:
                    return HandleTalkingToNPC(game);

                case AcceptQuestState.QueryingQuest:
                    return HandleQueryingQuest(game);

                case AcceptQuestState.AcceptingQuest:
                    return HandleAcceptingQuest(game);

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
                game.Log($"AcceptQuestTask: {ErrorMessage}", LogLevel.Warning);
                return TaskResult.Failed;
            }

            float distance = (targetNpc - game.Player).Length;
            game.Log($"AcceptQuestTask: Found NPC entry {resolvedNpcEntry} at distance {distance:F2}m", LogLevel.Debug);

            if (distance <= interactionRange)
            {
                // Already in range, go straight to talking
                state = AcceptQuestState.TalkingToNPC;
            }
            else
            {
                // Need to move first
                state = AcceptQuestState.MovingToNPC;
            }

            return TaskResult.Running;
        }

        private TaskResult HandleMovingToNPC(AutomatedGame game, BotGame botGame)
        {
            if (targetNpc == null || !game.Objects.ContainsKey(targetNpc.GUID))
            {
                ErrorMessage = "NPC is no longer visible";
                game.Log($"AcceptQuestTask: {ErrorMessage}", LogLevel.Error);
                return TaskResult.Failed;
            }

            float distance = (targetNpc - game.Player).Length;

            if (distance <= interactionRange)
            {
                game.Log($"AcceptQuestTask: Arrived at NPC (distance: {distance:F2}m)", LogLevel.Debug);
                state = AcceptQuestState.TalkingToNPC;
                return TaskResult.Running;
            }

            if (!moveStarted)
            {
                game.Log($"AcceptQuestTask: Moving to NPC entry {resolvedNpcEntry} (distance: {distance:F2}m)", LogLevel.Debug);
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
                game.Log($"AcceptQuestTask: {ErrorMessage}", LogLevel.Error);
                return TaskResult.Failed;
            }

            if (!talkSent)
            {
                game.Log($"AcceptQuestTask: Talking to NPC entry {resolvedNpcEntry}", LogLevel.Debug);

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
                state = AcceptQuestState.QueryingQuest;
            }

            return TaskResult.Running;
        }

        private TaskResult HandleQueryingQuest(AutomatedGame game)
        {
            if (targetNpc == null || !game.Objects.ContainsKey(targetNpc.GUID))
            {
                ErrorMessage = "NPC is no longer visible";
                game.Log($"AcceptQuestTask: {ErrorMessage}", LogLevel.Error);
                return TaskResult.Failed;
            }

            if (!questQuerySent)
            {
                game.Log($"AcceptQuestTask: Querying quest {resolvedQuestId} from NPC entry {resolvedNpcEntry}", LogLevel.Debug);
                game.QueryQuest(targetNpc.GUID, resolvedQuestId);
                questQuerySent = true;
                queryTime = DateTime.Now;
                return TaskResult.Running;
            }

            // Check if quest details arrived
            if (game.HasPendingQuestOffer && game.PendingQuestId == resolvedQuestId)
            {
                state = AcceptQuestState.AcceptingQuest;
                return TaskResult.Running;
            }

            // Check timeout
            if ((DateTime.Now - queryTime).TotalSeconds > timeoutSeconds)
            {
                ErrorMessage = "Timeout waiting for quest details";
                game.Log($"AcceptQuestTask: {ErrorMessage}", LogLevel.Warning);
                return TaskResult.Failed;
            }

            return TaskResult.Running;
        }

        private TaskResult HandleAcceptingQuest(AutomatedGame game)
        {
            game.AcceptQuest();
            game.Log($"AcceptQuestTask: Accepted quest {resolvedQuestId}", LogLevel.Debug);
            return TaskResult.Success;
        }

        public override void Cleanup(AutomatedGame game)
        {
            targetNpc = null;
            state = AcceptQuestState.FindingNPC;
            moveStarted = false;
            talkSent = false;
            questQuerySent = false;
        }
    }
}
