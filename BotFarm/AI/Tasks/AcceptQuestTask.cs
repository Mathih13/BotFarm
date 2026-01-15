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
    /// Prerequisites: Must be within interaction range of the NPC (use MoveToNPCTask first),
    /// and the quest dialog must be open (use TalkToNPCTask or call this after gossip).
    /// </summary>
    public class AcceptQuestTask : BaseTask
    {
        private readonly uint npcEntry;
        private readonly uint questId;
        private readonly Dictionary<Class, uint> classQuests;
        private readonly Dictionary<Class, uint> classNPCs;
        private uint resolvedQuestId;
        private uint resolvedNpcEntry;
        private WorldObject targetNpc = null;
        private bool questQuerySent = false;
        private bool questAccepted = false;
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

            // If we have an NPC entry, find it
            if (resolvedNpcEntry != 0)
            {
                targetNpc = MoveToNPCTask.FindNPCByEntry(game, resolvedNpcEntry);
                if (targetNpc == null)
                {
                    game.Log($"AcceptQuestTask: NPC with entry {resolvedNpcEntry} not found nearby", LogLevel.Warning);
                    return false;
                }

                float distance = (targetNpc - game.Player).Length;
                if (distance > interactionRange)
                {
                    game.Log($"AcceptQuestTask: Too far from NPC (distance: {distance:F2}m), need to be within {interactionRange}m", LogLevel.Warning);
                    return false;
                }
            }

            return true;
        }
        
        protected override TaskResult UpdateTask(AutomatedGame game)
        {
            if (!game.Player.IsAlive)
            {
                game.Log($"AcceptQuestTask: Player is dead", LogLevel.Warning);
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
                    game.Log($"AcceptQuestTask: No pending quest offer available", LogLevel.Warning);
                    return TaskResult.Failed;
                }
            }
            
            // Accepting a specific quest from a specific NPC
            if (targetNpc == null || !game.Objects.ContainsKey(targetNpc.GUID))
            {
                game.Log($"AcceptQuestTask: NPC is no longer visible", LogLevel.Error);
                return TaskResult.Failed;
            }
            
            // Check if we need to query the quest first
            if (!questQuerySent)
            {
                game.Log($"AcceptQuestTask: Querying quest {resolvedQuestId} from NPC entry {resolvedNpcEntry}", LogLevel.Debug);
                game.QueryQuest(targetNpc.GUID, resolvedQuestId);
                questQuerySent = true;
                queryTime = DateTime.Now;
                return TaskResult.Running;
            }

            // Wait for quest details to arrive
            if (!questAccepted)
            {
                if (game.HasPendingQuestOffer && game.PendingQuestId == resolvedQuestId)
                {
                    game.AcceptQuest();
                    questAccepted = true;
                    game.Log($"AcceptQuestTask: Accepted quest {resolvedQuestId}", LogLevel.Debug);
                    return TaskResult.Success;
                }
                
                // Check timeout
                if ((DateTime.Now - queryTime).TotalSeconds > timeoutSeconds)
                {
                    game.Log($"AcceptQuestTask: Timeout waiting for quest details", LogLevel.Warning);
                    return TaskResult.Failed;
                }
            }
            
            return TaskResult.Running;
        }
        
        public override void Cleanup(AutomatedGame game)
        {
            targetNpc = null;
        }
    }
}
