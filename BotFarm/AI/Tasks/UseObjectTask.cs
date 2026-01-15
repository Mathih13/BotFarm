using Client;
using Client.AI.Tasks;
using Client.UI;
using Client.World;
using Client.World.Entities;
using Client.World.Definitions;
using Client.World.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BotFarm.AI.Tasks
{
    /// <summary>
    /// Task that interacts with game objects (chests, quest objects, levers, etc.)
    /// Supports finding objects by entry, moving to them, and using them.
    /// </summary>
    public class UseObjectTask : BaseTask
    {
        private readonly uint objectEntry;
        private readonly int useCount;
        private readonly float searchRadius;
        private readonly bool waitForLoot;
        private readonly float maxWaitSeconds;

        private UseObjectState state;
        private WorldObject targetObject;
        private int usesCompleted;
        private DateTime useStartTime;
        private DateTime lastMoveUpdate;
        private bool movingToObject;

        // Target claiming for multi-bot coordination
        private ClaimedTargetRegistry claimRegistry;
        private string routeFilePath;

        // Track objects that can't be pathed to
        private HashSet<ulong> unpathableObjects = new HashSet<ulong>();
        private DateTime lastUnpathableClear = DateTime.Now;
        private int consecutivePathFailures;
        private const int MaxPathFailuresBeforeSkip = 3;
        private const float UnpathableClearIntervalSeconds = 30f;

        private const float InteractionRange = 5.0f;
        private const float DefaultWaitTime = 3.0f;

        private enum UseObjectState
        {
            Searching,           // Looking for an object
            MovingToObject,      // Moving to interaction range
            UsingObject,         // Sending use packet
            WaitingForCompletion // Waiting for despawn/state change/loot
        }

        public override string Name => $"UseObject(entry {objectEntry}, {usesCompleted}/{useCount})";

        /// <summary>
        /// Create a use object task
        /// </summary>
        /// <param name="objectEntry">Game object entry ID to interact with</param>
        /// <param name="useCount">Number of objects to use (default 1)</param>
        /// <param name="searchRadius">Radius to search for objects (default 50)</param>
        /// <param name="waitForLoot">Whether object opens loot window (default false)</param>
        /// <param name="maxWaitSeconds">Max time to wait for completion (default 5)</param>
        public UseObjectTask(
            uint objectEntry,
            int useCount = 1,
            float searchRadius = 50f,
            bool waitForLoot = false,
            float maxWaitSeconds = 5f)
        {
            this.objectEntry = objectEntry;
            this.useCount = useCount > 0 ? useCount : 1;
            this.searchRadius = searchRadius;
            this.waitForLoot = waitForLoot;
            this.maxWaitSeconds = maxWaitSeconds > 0 ? maxWaitSeconds : DefaultWaitTime;

            // Add small delay padding for realistic behavior
            SetDelayPadding(0.2f, 0.3f);
        }

        /// <summary>
        /// Set the route context for target claiming coordination
        /// </summary>
        public void SetRouteContext(string routeFilePath)
        {
            this.routeFilePath = routeFilePath;
            if (!string.IsNullOrEmpty(routeFilePath))
            {
                this.claimRegistry = ClaimedTargetRegistry.GetForRoute(routeFilePath);
            }
        }

        public override bool Start(AutomatedGame game)
        {
            if (!base.Start(game))
                return false;

            var botGame = game as BotGame;
            if (botGame == null)
            {
                game.Log("UseObjectTask: Game is not a BotGame instance", LogLevel.Error);
                return false;
            }

            if (objectEntry == 0)
            {
                game.Log("UseObjectTask: No object entry specified", LogLevel.Error);
                return false;
            }

            usesCompleted = 0;
            unpathableObjects.Clear();
            consecutivePathFailures = 0;
            state = UseObjectState.Searching;

            game.Log($"UseObjectTask: Starting, looking for object entry {objectEntry} (need {useCount} uses)", LogLevel.Info);
            return true;
        }

        protected override TaskResult UpdateTask(AutomatedGame game)
        {
            var botGame = game as BotGame;
            if (botGame == null)
                return TaskResult.Failed;

            if (!game.Player.IsAlive)
            {
                game.Log("UseObjectTask: Player died", LogLevel.Warning);
                return TaskResult.Failed;
            }

            // Check completion
            if (usesCompleted >= useCount)
            {
                game.Log($"UseObjectTask: Completed all {useCount} object uses", LogLevel.Info);
                return TaskResult.Success;
            }

            // Periodically clear unpathable objects
            if ((DateTime.Now - lastUnpathableClear).TotalSeconds > UnpathableClearIntervalSeconds)
            {
                if (unpathableObjects.Count > 0)
                {
                    game.Log($"UseObjectTask: Clearing {unpathableObjects.Count} unpathable objects for retry", LogLevel.Debug);
                    unpathableObjects.Clear();
                }
                lastUnpathableClear = DateTime.Now;
            }

            switch (state)
            {
                case UseObjectState.Searching:
                    return HandleSearching(botGame);

                case UseObjectState.MovingToObject:
                    return HandleMovingToObject(botGame);

                case UseObjectState.UsingObject:
                    return HandleUsingObject(botGame);

                case UseObjectState.WaitingForCompletion:
                    return HandleWaitingForCompletion(botGame);
            }

            return TaskResult.Running;
        }

        private TaskResult HandleSearching(BotGame game)
        {
            targetObject = FindTarget(game);

            if (targetObject == null)
            {
                game.Log($"UseObjectTask: No valid objects with entry {objectEntry} in range", LogLevel.Debug);
                return TaskResult.Running;
            }

            game.Log($"UseObjectTask: Found object entry {targetObject.Entry} at distance {(targetObject - game.Player).Length:F1}m", LogLevel.Info);
            state = UseObjectState.MovingToObject;
            movingToObject = false;
            consecutivePathFailures = 0;
            return TaskResult.Running;
        }

        private TaskResult HandleMovingToObject(BotGame game)
        {
            // Check if object is still valid
            if (targetObject == null || !game.Objects.ContainsKey(targetObject.GUID))
            {
                game.Log("UseObjectTask: Target object disappeared, searching for new target", LogLevel.Warning);
                ReleaseTarget(game);
                state = UseObjectState.Searching;
                return TaskResult.Running;
            }

            // Check if object state changed (already used)
            if (!MoveToNPCTask.IsGameObjectUsable(targetObject))
            {
                game.Log("UseObjectTask: Object is no longer usable, searching for new target", LogLevel.Debug);
                ReleaseTarget(game);
                state = UseObjectState.Searching;
                return TaskResult.Running;
            }

            // Check for too many path failures
            if (consecutivePathFailures >= MaxPathFailuresBeforeSkip)
            {
                game.Log($"UseObjectTask: Object unreachable after {consecutivePathFailures} path failures, skipping", LogLevel.Warning);
                unpathableObjects.Add(targetObject.GUID);
                ReleaseTarget(game);
                state = UseObjectState.Searching;
                return TaskResult.Running;
            }

            float distance = (targetObject - game.Player).Length;

            if (distance <= InteractionRange)
            {
                // In range, use the object
                game.CancelActionsByFlag(ActionFlag.Movement);
                state = UseObjectState.UsingObject;
                return TaskResult.Running;
            }

            // Move closer
            if (!movingToObject || (DateTime.Now - lastMoveUpdate).TotalSeconds > 1.0)
            {
                game.CancelActionsByFlag(ActionFlag.Movement);
                game.MoveTo(targetObject.GetPosition());
                movingToObject = true;
                lastMoveUpdate = DateTime.Now;

                if (!game.LastMoveSucceeded)
                {
                    consecutivePathFailures++;
                }
                else
                {
                    consecutivePathFailures = 0;
                }
            }

            return TaskResult.Running;
        }

        private TaskResult HandleUsingObject(BotGame game)
        {
            // Verify object is still valid
            if (targetObject == null || !game.Objects.ContainsKey(targetObject.GUID))
            {
                game.Log("UseObjectTask: Object disappeared before use", LogLevel.Warning);
                ReleaseTarget(game);
                state = UseObjectState.Searching;
                return TaskResult.Running;
            }

            game.Log($"UseObjectTask: Using object entry {targetObject.Entry}", LogLevel.Info);

            // Send CMSG_GAMEOBJ_USE packet
            SendUseObjectPacket(game, targetObject.GUID);

            useStartTime = DateTime.Now;
            state = UseObjectState.WaitingForCompletion;
            return TaskResult.Running;
        }

        private TaskResult HandleWaitingForCompletion(BotGame game)
        {
            // Timeout check
            if ((DateTime.Now - useStartTime).TotalSeconds > maxWaitSeconds)
            {
                game.Log("UseObjectTask: Completion timeout, considering object used", LogLevel.Warning);
                OnObjectUsed(game);
                return TaskResult.Running;
            }

            // Check if object despawned (common completion indicator)
            if (targetObject != null && !game.Objects.ContainsKey(targetObject.GUID))
            {
                game.Log("UseObjectTask: Object despawned, interaction complete", LogLevel.Debug);
                OnObjectUsed(game);
                return TaskResult.Running;
            }

            // Check if object state changed
            if (targetObject != null && !MoveToNPCTask.IsGameObjectUsable(targetObject))
            {
                game.Log("UseObjectTask: Object state changed, interaction complete", LogLevel.Debug);
                OnObjectUsed(game);
                return TaskResult.Running;
            }

            // If we're waiting for loot, check loot window
            if (waitForLoot)
            {
                if (game.CurrentLoot.IsOpen && game.CurrentLoot.LootGuid == targetObject?.GUID)
                {
                    // Loot everything
                    game.LootAllItems();

                    if (game.CurrentLoot.Items.Count == 0 && game.CurrentLoot.Gold == 0)
                    {
                        game.ReleaseLoot();
                        OnObjectUsed(game);
                    }
                    return TaskResult.Running;
                }
            }

            return TaskResult.Running;
        }

        private void OnObjectUsed(BotGame game)
        {
            usesCompleted++;
            game.Log($"UseObjectTask: Object used ({usesCompleted}/{useCount})", LogLevel.Info);

            ReleaseTarget(game);

            // Look for next object if needed
            if (usesCompleted < useCount)
            {
                state = UseObjectState.Searching;
            }
        }

        private WorldObject FindTarget(BotGame game)
        {
            var searchCenter = game.Player.GetPosition();

            var candidates = game.Objects.Values
                .Where(obj => IsValidTarget(obj, game, searchCenter))
                .OrderBy(obj => (obj - game.Player).Length);

            // Try to claim each candidate in order
            foreach (var candidate in candidates)
            {
                if (claimRegistry == null)
                {
                    return candidate;
                }

                if (claimRegistry.TryClaim(candidate.GUID, game.Player.GUID))
                {
                    return candidate;
                }
            }

            return null;
        }

        private bool IsValidTarget(WorldObject obj, BotGame game, Position searchCenter)
        {
            // Must be a game object
            if (!obj.IsType(HighGuid.GameObject))
                return false;

            // Must be on same map
            if (obj.MapID != game.Player.MapID)
                return false;

            // Must match entry
            if (obj.Entry != objectEntry)
                return false;

            // Must be usable
            if (!MoveToNPCTask.IsGameObjectUsable(obj))
                return false;

            // Must be within search radius
            float distance = (obj - searchCenter).Length;
            if (distance > searchRadius)
                return false;

            // Check if claimed by another bot
            if (claimRegistry != null && claimRegistry.IsClaimedByOther(obj.GUID, game.Player.GUID))
                return false;

            // Skip unpathable objects
            if (unpathableObjects.Contains(obj.GUID))
                return false;

            return true;
        }

        private void SendUseObjectPacket(AutomatedGame game, ulong objectGuid)
        {
            // Send CMSG_GAMEOBJ_USE
            OutPacket usePacket = new OutPacket(WorldCommand.CMSG_GAMEOBJ_USE);
            usePacket.Write(objectGuid);
            game.SendPacket(usePacket);
        }

        private void ReleaseTarget(BotGame game)
        {
            if (claimRegistry != null && targetObject != null)
            {
                claimRegistry.Release(targetObject.GUID, game.Player.GUID);
            }
            targetObject = null;
            movingToObject = false;
            consecutivePathFailures = 0;
        }

        public override void Cleanup(AutomatedGame game)
        {
            // Release any claimed target
            if (game is BotGame botGame)
            {
                ReleaseTarget(botGame);
            }

            // Release loot if open
            if (game.CurrentLoot.IsOpen)
            {
                game.ReleaseLoot();
            }
        }
    }
}
