using Client;
using Client.AI.Tasks;
using Client.UI;
using Client.World;
using Client.World.Entities;
using Client.World.Network;
using Client.World.Definitions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BotFarm.AI.Tasks
{
    public class TalkToNPCTask : BaseTask
    {
        private readonly uint npcEntry;
        private readonly Dictionary<Class, uint> classNPCs;
        private uint resolvedNpcEntry;
        private WorldObject targetNpc = null;
        private bool interactionSent = false;
        private DateTime interactionTime;
        private readonly float interactionRange = 5.0f;
        
        public override string Name => classNPCs != null
            ? $"TalkToClassNPC({resolvedNpcEntry})"
            : $"TalkToNPC({npcEntry})";
        
        public TalkToNPCTask(uint npcEntry)
        {
            this.npcEntry = npcEntry;
            this.classNPCs = null;
            SetDelayPadding(RandomDelay(0.3f, 0.6f), RandomDelay(0.3f, 0.6f));
        }

        /// <summary>
        /// Talk to a class-specific NPC
        /// </summary>
        /// <param name="fallbackNpcEntry">NPC entry to use if player's class isn't in the map</param>
        /// <param name="classNPCs">Map of class to NPC entry</param>
        public TalkToNPCTask(uint fallbackNpcEntry, Dictionary<Class, uint> classNPCs)
        {
            this.npcEntry = fallbackNpcEntry;
            this.classNPCs = classNPCs;
            SetDelayPadding(RandomDelay(0.3f, 0.6f), RandomDelay(0.3f, 0.6f));
        }
        
        public override bool Start(AutomatedGame game)
        {
            if (!base.Start(game))
                return false;
            
            var botGame = game as BotGame;
            if (botGame == null)
            {
                game.Log($"TalkToNPCTask: Game is not a BotGame instance", LogLevel.Error);
                return false;
            }

            // Resolve NPC entry based on player class if class NPCs are specified
            if (classNPCs != null)
            {
                var playerClass = game.World.SelectedCharacter?.Class ?? Class.Warrior;
                if (classNPCs.TryGetValue(playerClass, out var classNpcEntry))
                {
                    resolvedNpcEntry = classNpcEntry;
                    game.Log($"TalkToNPCTask: Using class-specific NPC {resolvedNpcEntry} for {playerClass}", LogLevel.Debug);
                }
                else if (npcEntry > 0)
                {
                    resolvedNpcEntry = npcEntry;
                    game.Log($"TalkToNPCTask: No NPC for {playerClass}, using fallback {npcEntry}", LogLevel.Debug);
                }
                else
                {
                    game.Log($"TalkToNPCTask: No NPC available for {playerClass}, skipping", LogLevel.Debug);
                    return false;
                }
            }
            else
            {
                resolvedNpcEntry = npcEntry;
            }
            
            // Find NPC by entry ID, or closest NPC if entry is 0
            if (resolvedNpcEntry != 0)
            {
                targetNpc = MoveToNPCTask.FindNPCByEntry(game, resolvedNpcEntry);
                if (targetNpc == null)
                {
                    game.Log($"TalkToNPCTask: NPC with entry {resolvedNpcEntry} not found nearby", LogLevel.Warning);
                    return false;
                }
            }
            else
            {
                targetNpc = MoveToNPCTask.FindClosestNPC(game);
                if (targetNpc == null)
                {
                    game.Log($"TalkToNPCTask: No NPCs found nearby", LogLevel.Warning);
                    return false;
                }
            }
            
            game.Log($"TalkToNPCTask: Found NPC entry {targetNpc.Entry} at ({targetNpc.X:F1}, {targetNpc.Y:F1}, {targetNpc.Z:F1})", LogLevel.Debug);
            return true;
        }
        
        protected override TaskResult UpdateTask(AutomatedGame game)
        {
            if (!game.Player.IsAlive)
            {
                game.Log($"TalkToNPCTask: Player is dead", LogLevel.Warning);
                return TaskResult.Failed;
            }
            
            if (targetNpc == null || !game.Objects.ContainsKey(targetNpc.GUID))
            {
                game.Log($"TalkToNPCTask: NPC {npcEntry} is no longer visible", LogLevel.Error);
                return TaskResult.Failed;
            }
            
            float distance = (targetNpc - game.Player).Length;
            
            if (distance > interactionRange)
            {
                game.Log($"TalkToNPCTask: Too far from NPC (distance: {distance:F2}m), need to be within {interactionRange}m", LogLevel.Warning);
                return TaskResult.Failed;
            }
            
            if (!interactionSent)
            {
                game.Log($"TalkToNPCTask: Interacting with NPC entry {targetNpc.Entry}", LogLevel.Debug);
                
                // Send gossip hello packet
                OutPacket gossipHello = new OutPacket(WorldCommand.CMSG_GOSSIP_HELLO);
                gossipHello.Write(targetNpc.GUID);
                game.SendPacket(gossipHello);
                
                interactionSent = true;
                interactionTime = DateTime.Now;
            }
            
            // Wait a moment for server response, then consider complete
            if ((DateTime.Now - interactionTime).TotalSeconds > 0.5)
            {
                game.Log($"TalkToNPCTask: Interaction sent successfully", LogLevel.Debug);
                return TaskResult.Success;
            }
            
            return TaskResult.Running;
        }
        
        public override void Cleanup(AutomatedGame game)
        {
            targetNpc = null;
        }
    }
}
