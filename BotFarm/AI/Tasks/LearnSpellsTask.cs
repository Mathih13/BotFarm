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
    /// Task that learns spells from a trainer NPC.
    /// Prerequisites: Must be within interaction range of the trainer NPC (use MoveToNPC first),
    /// and have talked to the trainer (use TalkToNPC first).
    /// </summary>
    public class LearnSpellsTask : BaseTask
    {
        private readonly uint npcEntry;
        private readonly uint[] spellIds;
        private readonly Dictionary<Class, uint[]> classSpells;
        private readonly Dictionary<Class, uint> classNPCs;

        private uint resolvedNpcEntry;
        private uint[] resolvedSpellIds;
        private WorldObject targetNpc;
        private int currentSpellIndex;
        private DateTime lastLearnTime;
        private const float LearnDelaySeconds = 0.5f;
        private const float InteractionRange = 5.0f;

        public override string Name => classSpells != null || classNPCs != null
            ? $"LearnClassSpells({resolvedSpellIds?.Length ?? 0} spells)"
            : $"LearnSpells({spellIds?.Length ?? 0} spells from NPC {npcEntry})";

        /// <summary>
        /// Learn specific spells from a specific trainer NPC
        /// </summary>
        /// <param name="npcEntry">Trainer NPC entry ID</param>
        /// <param name="spellIds">Array of spell IDs to learn</param>
        public LearnSpellsTask(uint npcEntry, uint[] spellIds)
        {
            this.npcEntry = npcEntry;
            this.spellIds = spellIds;
            this.classSpells = null;
            this.classNPCs = null;
            SetDelayPadding(RandomDelay(0.3f, 0.6f), RandomDelay(0.5f, 1.0f));
        }

        /// <summary>
        /// Learn class-specific spells from class-specific trainers
        /// </summary>
        /// <param name="npcEntry">Fallback NPC entry ID</param>
        /// <param name="spellIds">Fallback spell IDs to learn</param>
        /// <param name="classSpells">Map of class to spell ID array</param>
        /// <param name="classNPCs">Map of class to trainer NPC entry</param>
        public LearnSpellsTask(uint npcEntry, uint[] spellIds,
            Dictionary<Class, uint[]> classSpells,
            Dictionary<Class, uint> classNPCs)
        {
            this.npcEntry = npcEntry;
            this.spellIds = spellIds;
            this.classSpells = classSpells;
            this.classNPCs = classNPCs;
            SetDelayPadding(RandomDelay(0.3f, 0.6f), RandomDelay(0.5f, 1.0f));
        }

        public override bool Start(AutomatedGame game)
        {
            if (!base.Start(game))
                return false;

            var playerClass = game.World.SelectedCharacter?.Class ?? Class.Warrior;

            // Resolve spell IDs based on player class if class spells are specified
            if (classSpells != null)
            {
                if (classSpells.TryGetValue(playerClass, out var classSpellIds))
                {
                    resolvedSpellIds = classSpellIds;
                    game.Log($"LearnSpellsTask: Using {resolvedSpellIds.Length} class-specific spells for {playerClass}", LogLevel.Debug);
                }
                else if (spellIds != null && spellIds.Length > 0)
                {
                    resolvedSpellIds = spellIds;
                    game.Log($"LearnSpellsTask: No spells for {playerClass}, using {spellIds.Length} fallback spells", LogLevel.Debug);
                }
                else
                {
                    game.Log($"LearnSpellsTask: No spells available for {playerClass}, skipping", LogLevel.Debug);
                    return false;
                }
            }
            else
            {
                resolvedSpellIds = spellIds;
            }

            // Validate we have spells to learn
            if (resolvedSpellIds == null || resolvedSpellIds.Length == 0)
            {
                game.Log("LearnSpellsTask: No spells specified", LogLevel.Warning);
                return false;
            }

            // Resolve NPC entry based on player class if class NPCs are specified
            if (classNPCs != null)
            {
                if (classNPCs.TryGetValue(playerClass, out var classNpcEntry))
                {
                    resolvedNpcEntry = classNpcEntry;
                    game.Log($"LearnSpellsTask: Using class-specific trainer NPC {resolvedNpcEntry} for {playerClass}", LogLevel.Debug);
                }
                else if (npcEntry > 0)
                {
                    resolvedNpcEntry = npcEntry;
                    game.Log($"LearnSpellsTask: No trainer for {playerClass}, using fallback NPC {npcEntry}", LogLevel.Debug);
                }
                else
                {
                    game.Log($"LearnSpellsTask: No trainer NPC available for {playerClass}, skipping", LogLevel.Debug);
                    return false;
                }
            }
            else
            {
                resolvedNpcEntry = npcEntry;
            }

            // Validate we have an NPC entry
            if (resolvedNpcEntry == 0)
            {
                game.Log("LearnSpellsTask: No trainer NPC entry specified", LogLevel.Warning);
                return false;
            }

            // Find the trainer NPC
            targetNpc = MoveToNPCTask.FindNPCByEntry(game, resolvedNpcEntry);
            if (targetNpc == null)
            {
                game.Log($"LearnSpellsTask: Trainer NPC with entry {resolvedNpcEntry} not found nearby", LogLevel.Warning);
                return false;
            }

            // Check distance
            float distance = (targetNpc - game.Player).Length;
            if (distance > InteractionRange)
            {
                game.Log($"LearnSpellsTask: Too far from trainer (distance: {distance:F2}m), need to be within {InteractionRange}m", LogLevel.Warning);
                return false;
            }

            // Initialize state
            currentSpellIndex = 0;
            lastLearnTime = DateTime.MinValue;

            game.Log($"LearnSpellsTask: Starting to learn {resolvedSpellIds.Length} spells from trainer {resolvedNpcEntry}", LogLevel.Info);
            return true;
        }

        protected override TaskResult UpdateTask(AutomatedGame game)
        {
            if (!game.Player.IsAlive)
            {
                game.Log("LearnSpellsTask: Player is dead", LogLevel.Warning);
                return TaskResult.Failed;
            }

            // Check if trainer is still valid
            if (targetNpc == null || !game.Objects.ContainsKey(targetNpc.GUID))
            {
                game.Log("LearnSpellsTask: Trainer NPC is no longer visible", LogLevel.Error);
                return TaskResult.Failed;
            }

            // Check if all spells learned
            if (currentSpellIndex >= resolvedSpellIds.Length)
            {
                game.Log($"LearnSpellsTask: Successfully learned all {resolvedSpellIds.Length} spells", LogLevel.Info);
                return TaskResult.Success;
            }

            // Wait for delay between spell learns
            if ((DateTime.Now - lastLearnTime).TotalSeconds < LearnDelaySeconds)
            {
                return TaskResult.Running;
            }

            // Learn the current spell
            uint spellId = resolvedSpellIds[currentSpellIndex];
            LearnSpell(game, targetNpc.GUID, spellId);
            game.Log($"LearnSpellsTask: Learning spell {spellId} ({currentSpellIndex + 1}/{resolvedSpellIds.Length})", LogLevel.Debug);

            lastLearnTime = DateTime.Now;
            currentSpellIndex++;

            return TaskResult.Running;
        }

        /// <summary>
        /// Send packet to learn a spell from a trainer
        /// </summary>
        private void LearnSpell(AutomatedGame game, ulong trainerGuid, uint spellId)
        {
            OutPacket packet = new OutPacket(WorldCommand.CMSG_TRAINER_BUY_SPELL);
            packet.Write(trainerGuid);
            packet.Write(spellId);
            game.SendPacket(packet);
        }

        public override void Cleanup(AutomatedGame game)
        {
            targetNpc = null;
            resolvedSpellIds = null;
        }
    }
}
