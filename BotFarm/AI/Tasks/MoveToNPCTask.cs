using Client;
using Client.AI.Tasks;
using Client.UI;
using Client.World.Entities;
using Client.World.Definitions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BotFarm.AI.Tasks
{
    public class MoveToNPCTask : BaseTask
    {
        private readonly uint npcEntry;
        private readonly Dictionary<Class, uint> classNPCs;
        private readonly float arrivalThreshold;
        private uint resolvedNpcEntry;
        private bool moveStarted = false;
        private WorldObject targetNpc = null;
        
        public override string Name => classNPCs != null
            ? $"MoveToClassNPC({resolvedNpcEntry})"
            : $"MoveToNPC({npcEntry})";
        
        public MoveToNPCTask(uint npcEntry, float arrivalThreshold = 5.0f)
        {
            this.npcEntry = npcEntry;
            this.arrivalThreshold = arrivalThreshold;
            this.classNPCs = null;
        }

        /// <summary>
        /// Move to a class-specific NPC
        /// </summary>
        /// <param name="fallbackNpcEntry">NPC entry to use if player's class isn't in the map</param>
        /// <param name="arrivalThreshold">Distance threshold to consider arrived</param>
        /// <param name="classNPCs">Map of class to NPC entry</param>
        public MoveToNPCTask(uint fallbackNpcEntry, float arrivalThreshold, Dictionary<Class, uint> classNPCs)
        {
            this.npcEntry = fallbackNpcEntry;
            this.arrivalThreshold = arrivalThreshold;
            this.classNPCs = classNPCs;
        }

        /// <summary>
        /// Finds the NPC with the specified entry ID closest to the player.
        /// Returns null if not found.
        /// </summary>
        public static WorldObject FindNPCByEntry(AutomatedGame game, uint entry)
        {
            return game.Objects.Values
                .Where(obj => obj.IsType(HighGuid.Unit) && 
                              obj.GUID != game.Player.GUID && 
                              obj.MapID == game.Player.MapID &&
                              obj.Entry == entry)
                .OrderBy(obj => (obj - game.Player).Length)
                .FirstOrDefault();
        }

        /// <summary>
        /// Finds the closest NPC (any entry) to the player.
        /// Returns null if not found.
        /// </summary>
        public static WorldObject FindClosestNPC(AutomatedGame game)
        {
            return game.Objects.Values
                .Where(obj => obj.IsType(HighGuid.Unit) &&
                              obj.GUID != game.Player.GUID &&
                              obj.MapID == game.Player.MapID)
                .OrderBy(obj => (obj - game.Player).Length)
                .FirstOrDefault();
        }

        /// <summary>
        /// Finds a game object with the specified entry ID closest to the player.
        /// Returns null if not found.
        /// </summary>
        public static WorldObject FindGameObjectByEntry(AutomatedGame game, uint entry)
        {
            return game.Objects.Values
                .Where(obj => obj.IsType(HighGuid.GameObject) &&
                              obj.MapID == game.Player.MapID &&
                              obj.Entry == entry &&
                              IsGameObjectUsable(obj))
                .OrderBy(obj => (obj - game.Player).Length)
                .FirstOrDefault();
        }

        /// <summary>
        /// Finds any game object from a set of entry IDs closest to the player.
        /// Returns null if not found.
        /// </summary>
        public static WorldObject FindGameObjectByEntries(AutomatedGame game, HashSet<uint> entries)
        {
            return game.Objects.Values
                .Where(obj => obj.IsType(HighGuid.GameObject) &&
                              obj.MapID == game.Player.MapID &&
                              entries.Contains(obj.Entry) &&
                              IsGameObjectUsable(obj))
                .OrderBy(obj => (obj - game.Player).Length)
                .FirstOrDefault();
        }

        /// <summary>
        /// Check if a game object is in a usable state (not already looted/used).
        /// GAMEOBJECT_BYTES_1 contains state info: State 0 = Ready, State 1 = Active/In-use
        /// </summary>
        public static bool IsGameObjectUsable(WorldObject obj)
        {
            // GAMEOBJECT_BYTES_1 contains state in low byte
            // GO_STATE_READY = 0, GO_STATE_ACTIVE = 1, GO_STATE_ACTIVE_ALTERNATIVE = 2
            uint bytes1 = obj[(int)GameObjectField.GAMEOBJECT_BYTES_1];
            byte state = (byte)(bytes1 & 0xFF);
            return state == 0; // GO_STATE_READY
        }
        
        public override bool Start(AutomatedGame game)
        {
            if (!base.Start(game))
                return false;
            
            var botGame = game as BotGame;
            if (botGame == null)
            {
                game.Log($"MoveToNPCTask: Game is not a BotGame instance", LogLevel.Error);
                return false;
            }

            // Resolve NPC entry based on player class if class NPCs are specified
            if (classNPCs != null)
            {
                var playerClass = game.World.SelectedCharacter?.Class ?? Class.Warrior;
                if (classNPCs.TryGetValue(playerClass, out var classNpcEntry))
                {
                    resolvedNpcEntry = classNpcEntry;
                    game.Log($"MoveToNPCTask: Using class-specific NPC {resolvedNpcEntry} for {playerClass}", LogLevel.Debug);
                }
                else if (npcEntry > 0)
                {
                    resolvedNpcEntry = npcEntry;
                    game.Log($"MoveToNPCTask: No NPC for {playerClass}, using fallback {npcEntry}", LogLevel.Debug);
                }
                else
                {
                    game.Log($"MoveToNPCTask: No NPC available for {playerClass}, skipping", LogLevel.Debug);
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
                targetNpc = FindNPCByEntry(game, resolvedNpcEntry);
                if (targetNpc == null)
                {
                    game.Log($"MoveToNPCTask: NPC with entry {resolvedNpcEntry} not found nearby", LogLevel.Warning);
                    return false;
                }
            }
            else
            {
                targetNpc = FindClosestNPC(game);
                if (targetNpc == null)
                {
                    game.Log($"MoveToNPCTask: No NPCs found nearby", LogLevel.Warning);
                    return false;
                }
            }
            
            game.Log($"MoveToNPCTask: Found NPC entry {targetNpc.Entry} at ({targetNpc.X:F1}, {targetNpc.Y:F1}, {targetNpc.Z:F1})", LogLevel.Debug);
            return true;
        }
        
        protected override TaskResult UpdateTask(AutomatedGame game)
        {
            var botGame = game as BotGame;
            if (botGame == null)
                return TaskResult.Failed;
            
            if (!game.Player.IsAlive)
            {
                ErrorMessage = "Player is dead";
                game.Log($"MoveToNPCTask: {ErrorMessage}", LogLevel.Warning);
                return TaskResult.Failed;
            }
            
            if (targetNpc == null || !game.Objects.ContainsKey(targetNpc.GUID))
            {
                ErrorMessage = $"NPC {resolvedNpcEntry} is no longer visible";
                game.Log($"MoveToNPCTask: {ErrorMessage}", LogLevel.Error);
                return TaskResult.Failed;
            }
            
            float distance = (targetNpc - game.Player).Length;
            
            if (distance <= arrivalThreshold)
            {
                game.Log($"MoveToNPCTask: Arrived at NPC (distance: {distance:F2}m)", LogLevel.Debug);
                return TaskResult.Success;
            }
            
            if (!moveStarted)
            {
                game.Log($"MoveToNPCTask: Starting move to NPC entry {targetNpc.Entry} (distance: {distance:F2}m)", LogLevel.Debug);
                game.CancelActionsByFlag(ActionFlag.Movement);
                botGame.MoveTo(targetNpc.GetPosition());
                moveStarted = true;
            }
            
            return TaskResult.Running;
        }
        
        public override void Cleanup(AutomatedGame game)
        {
            targetNpc = null;
        }
    }
}
