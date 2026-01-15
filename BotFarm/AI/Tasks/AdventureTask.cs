using Client;
using Client.AI.Tasks;
using Client.UI;
using Client.World;
using Client.World.Entities;
using Client.World.Definitions;
using Client.World.Network;
using BotFarm.AI.Combat;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BotFarm.AI.Tasks
{
    /// <summary>
    /// Represents an object use requirement
    /// </summary>
    public class ObjectUseRequirement
    {
        public uint Entry { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// Comprehensive quest/adventure task that handles:
    /// - Killing specific mobs with count requirements
    /// - Collecting items from mobs
    /// - Interacting with game objects
    /// - Collecting items from objects
    /// - Reactive combat (defending when attacked)
    /// Uses class-specific combat AI for abilities.
    /// </summary>
    public class AdventureTask : BaseTask
    {
        // Mob targeting parameters
        private readonly HashSet<uint> targetEntries;
        private readonly float searchRadius;
        private readonly float maxDurationSeconds;
        private readonly Position centerPosition;

        // Object interaction parameters
        private readonly HashSet<uint> objectEntries;
        private readonly List<ObjectUseRequirement> objectRequirements;
        private readonly bool objectsGiveLoot;

        // Kill requirements (per mob type)
        private readonly List<KillRequirement> killRequirements;
        private readonly int killCount;

        // Item collection requirements
        private readonly List<ItemCollectionRequirement> collectItems;
        private readonly uint collectItemEntry;
        private readonly int collectItemCount;

        // Reactive combat
        private readonly bool defendSelf;

        // Combat state
        private IClassCombatAI combatAI;
        private WorldObject currentTarget;
        private int killsCompleted;
        private Dictionary<uint, int> killsByEntry = new Dictionary<uint, int>();
        private DateTime startTime;
        private DateTime lastCombatUpdate;
        private DateTime lastSearchTime;
        private DateTime lastMoveUpdate;
        private DateTime lastClaimRefresh;
        private DateTime engagementStartTime;
        private AdventureState state;
        private bool movingToTarget;

        // Combat stall detection - re-engage if no damage dealt
        private int lastTargetHealth;
        private DateTime lastHealthChangeTime;
        private const float CombatStallTimeoutSeconds = 6f;

        // Object interaction state
        private WorldObject currentObjectTarget;
        private bool movingToObject;
        private DateTime objectUseTime;
        private Dictionary<uint, int> usesByObjectEntry = new Dictionary<uint, int>();

        // Target claiming for multi-bot coordination
        private ClaimedTargetRegistry claimRegistry;
        private string routeFilePath;

        // Looting state
        private ulong corpseGuid;
        private Position corpsePosition;
        private DateTime lootAttemptTime;
        private bool lootWindowRequested;
        private bool movingToCorpse;
        private const float LootRange = 5.0f;
        private const float LootTimeoutSeconds = 5.0f;

        // Pathing failure tracking
        private HashSet<ulong> unpathableTargets = new HashSet<ulong>();
        private DateTime lastUnpathableClear = DateTime.Now;
        private int consecutivePathFailures;
        private const int MaxPathFailuresBeforeSkip = 3;
        private const float UnpathableClearIntervalSeconds = 30f;

        // Stuck detection
        private Position lastStuckCheckPosition;
        private DateTime lastStuckCheckTime = DateTime.Now;
        private int stuckCounter;
        private const float StuckDistanceThreshold = 1.0f;
        private const float StuckCheckIntervalSeconds = 5f;
        private const int MaxStuckCountBeforeUnstuck = 2;

        private int totalPathFailures;
        private const int MaxTotalPathFailuresBeforeLogout = 20;

        private const float ObjectInteractionRange = 5.0f;
        private const float ObjectUseTimeout = 5.0f;

        private enum AdventureState
        {
            Searching,          // Looking for a mob or object
            MovingToTarget,     // Moving to engage mob
            InCombat,           // Fighting
            Resting,            // Recovering health/mana
            Looting,            // Looting corpse after kill
            MovingToObject,     // Moving to game object
            UsingObject,        // Interacting with object
            LootingObject,      // Looting from object
            ReturningToCenter   // Moving back to center after getting stuck
        }

        public override string Name
        {
            get
            {
                var parts = new List<string>();

                // Kill requirements
                if (killRequirements != null && killRequirements.Count > 0)
                {
                    var killParts = killRequirements.Select(r =>
                    {
                        int killed = killsByEntry.TryGetValue(r.Entry, out var c) ? c : 0;
                        return $"{killed}/{r.Count}x{r.Entry}";
                    });
                    parts.Add($"kill[{string.Join(",", killParts)}]");
                }
                else if (killCount > 0)
                {
                    parts.Add($"kills:{killsCompleted}/{killCount}");
                }

                // Object requirements
                if (objectRequirements != null && objectRequirements.Count > 0)
                {
                    var objParts = objectRequirements.Select(r =>
                    {
                        int used = usesByObjectEntry.TryGetValue(r.Entry, out var c) ? c : 0;
                        return $"{used}/{r.Count}x{r.Entry}";
                    });
                    parts.Add($"obj[{string.Join(",", objParts)}]");
                }

                return $"Adventure({string.Join(" + ", parts)})";
            }
        }

        /// <summary>
        /// Create an adventure task
        /// </summary>
        public AdventureTask(
            uint[] targetEntries = null,
            uint[] objectEntries = null,
            int killCount = 0,
            float searchRadius = 50f,
            float maxDurationSeconds = 0,
            float centerX = 0, float centerY = 0, float centerZ = 0, int mapId = 0,
            uint collectItemEntry = 0, int collectItemCount = 0,
            List<KillRequirement> killRequirements = null,
            List<ItemCollectionRequirement> collectItems = null,
            List<ObjectUseRequirement> objectRequirements = null,
            bool objectsGiveLoot = false,
            bool defendSelf = true)
        {
            this.targetEntries = targetEntries != null ? new HashSet<uint>(targetEntries) : new HashSet<uint>();
            this.objectEntries = objectEntries != null ? new HashSet<uint>(objectEntries) : new HashSet<uint>();
            this.killCount = killCount;
            this.searchRadius = searchRadius;
            this.maxDurationSeconds = maxDurationSeconds;
            this.collectItemEntry = collectItemEntry;
            this.collectItemCount = collectItemCount;
            this.killRequirements = killRequirements;
            this.collectItems = collectItems;
            this.objectRequirements = objectRequirements;
            this.objectsGiveLoot = objectsGiveLoot;
            this.defendSelf = defendSelf;

            if (centerX != 0 || centerY != 0 || centerZ != 0)
            {
                this.centerPosition = new Position(centerX, centerY, centerZ, 0, mapId);
            }
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
                game.Log("AdventureTask: Game is not a BotGame instance", LogLevel.Error);
                return false;
            }

            // Get combat AI for the player's class
            var playerClass = game.World.SelectedCharacter?.Class ?? Class.Warrior;
            combatAI = CombatAIFactory.CreateForClass(playerClass);

            // Reset tracking
            killsCompleted = 0;
            killsByEntry.Clear();
            usesByObjectEntry.Clear();
            unpathableTargets.Clear();
            consecutivePathFailures = 0;
            startTime = DateTime.Now;
            state = AdventureState.Searching;

            LogStartStatus(game, playerClass);
            return true;
        }

        protected override TaskResult UpdateTask(AutomatedGame game)
        {
            var botGame = game as BotGame;
            if (botGame == null)
                return TaskResult.Failed;

            if (!game.Player.IsAlive)
            {
                game.Log("AdventureTask: Player died", LogLevel.Warning);
                return TaskResult.Failed;
            }

            // Reactive combat: Check if we're being attacked
            if (defendSelf && ShouldEnterReactiveCombat(botGame))
            {
                return TaskResult.Running;
            }

            // Clear unpathable targets periodically
            if ((DateTime.Now - lastUnpathableClear).TotalSeconds > UnpathableClearIntervalSeconds)
            {
                if (unpathableTargets.Count > 0)
                {
                    game.Log($"AdventureTask: Clearing {unpathableTargets.Count} unpathable targets for retry", LogLevel.Debug);
                    unpathableTargets.Clear();
                }
                lastUnpathableClear = DateTime.Now;
            }

            // Stuck detection
            if (state == AdventureState.MovingToTarget || state == AdventureState.MovingToObject || movingToCorpse)
            {
                CheckIfStuck(botGame);
            }
            else
            {
                stuckCounter = 0;
                lastStuckCheckPosition = null;
            }

            // Check completion conditions (AND logic)
            if (AreAllRequirementsMet(game))
            {
                LogCompletionStatus(game);
                return TaskResult.Success;
            }

            // Time limit
            if (maxDurationSeconds > 0 && (DateTime.Now - startTime).TotalSeconds > maxDurationSeconds)
            {
                game.Log($"AdventureTask: Time limit reached", LogLevel.Info);
                return TaskResult.Success;
            }

            // State machine
            switch (state)
            {
                case AdventureState.Resting:
                    return HandleResting(botGame);

                case AdventureState.Searching:
                    return HandleSearching(botGame);

                case AdventureState.MovingToTarget:
                    return HandleMovingToTarget(botGame);

                case AdventureState.InCombat:
                    return HandleCombat(botGame);

                case AdventureState.Looting:
                    return HandleLooting(botGame);

                case AdventureState.MovingToObject:
                    return HandleMovingToObject(botGame);

                case AdventureState.UsingObject:
                    return HandleUsingObject(botGame);

                case AdventureState.LootingObject:
                    return HandleLootingObject(botGame);

                case AdventureState.ReturningToCenter:
                    return HandleReturningToCenter(botGame);
            }

            return TaskResult.Running;
        }

        #region Reactive Combat

        /// <summary>
        /// Check if player is being attacked and should enter reactive combat
        /// </summary>
        private bool ShouldEnterReactiveCombat(BotGame game)
        {
            // Only enter reactive combat when not already in combat-related states
            if (state == AdventureState.InCombat)
                return false;

            var attacker = FindAttacker(game);
            if (attacker == null)
                return false;

            game.Log($"AdventureTask: Under attack by entry {attacker.Entry}, entering reactive combat", LogLevel.Warning);

            // Cancel any current movement
            game.CancelActionsByFlag(ActionFlag.Movement);

            // Clear current targets
            if (claimRegistry != null)
            {
                if (currentTarget != null)
                    claimRegistry.Release(currentTarget.GUID, game.Player.GUID);
                if (currentObjectTarget != null)
                    claimRegistry.Release(currentObjectTarget.GUID, game.Player.GUID);
            }

            currentTarget = attacker;
            currentObjectTarget = null;
            movingToTarget = false;
            movingToObject = false;

            // Try to claim the attacker
            if (claimRegistry != null)
            {
                claimRegistry.TryClaim(attacker.GUID, game.Player.GUID);
            }

            // Start combat
            combatAI.OnCombatStart(game, currentTarget);
            state = AdventureState.InCombat;
            lastCombatUpdate = DateTime.Now;

            // Initialize combat stall tracking
            lastTargetHealth = (int)currentTarget[UnitField.UNIT_FIELD_HEALTH];
            lastHealthChangeTime = DateTime.Now;

            return true;
        }

        /// <summary>
        /// Find an enemy that is attacking the player
        /// </summary>
        private WorldObject FindAttacker(BotGame game)
        {
            return game.Objects.Values
                .Where(obj => obj.IsType(HighGuid.Unit) &&
                              obj.GUID != game.Player.GUID &&
                              obj.MapID == game.Player.MapID &&
                              obj[UnitField.UNIT_FIELD_HEALTH] > 0 &&
                              IsTargetingPlayer(obj, game))
                .OrderBy(obj => (obj - game.Player).Length)
                .FirstOrDefault();
        }

        /// <summary>
        /// Check if a unit is targeting the player
        /// </summary>
        private bool IsTargetingPlayer(WorldObject obj, BotGame game)
        {
            uint lowTarget = obj[UnitField.UNIT_FIELD_TARGET];
            uint highTarget = obj[(int)UnitField.UNIT_FIELD_TARGET + 1];
            ulong targetGuid = ((ulong)highTarget << 32) | lowTarget;
            return targetGuid == game.Player.GUID;
        }

        #endregion

        #region State Handlers

        private TaskResult HandleResting(BotGame game)
        {
            if (combatAI.NeedsRest(game))
            {
                if (!combatAI.OnRest(game))
                {
                    return TaskResult.Running;
                }
            }

            state = AdventureState.Searching;
            return TaskResult.Running;
        }

        private TaskResult HandleSearching(BotGame game)
        {
            // Check if we need to rest first
            if (combatAI.NeedsRest(game))
            {
                state = AdventureState.Resting;
                return TaskResult.Running;
            }

            // Throttle searches
            if ((DateTime.Now - lastSearchTime).TotalSeconds < 1.0)
            {
                return TaskResult.Running;
            }
            lastSearchTime = DateTime.Now;

            // Decide what to search for based on incomplete requirements
            var (target, targetType) = FindNextTarget(game);

            if (target == null)
            {
                if ((DateTime.Now - startTime).TotalSeconds % 5 < 1)
                {
                    game.Log($"AdventureTask: No valid targets in range", LogLevel.Debug);
                }
                return TaskResult.Running;
            }

            if (targetType == TargetType.Mob)
            {
                currentTarget = target;
                currentObjectTarget = null;
                game.Log($"AdventureTask: Found mob entry {target.Entry} at distance {(target - game.Player).Length:F1}m", LogLevel.Info);
                state = AdventureState.MovingToTarget;
                engagementStartTime = DateTime.Now;
                movingToTarget = false;
            }
            else
            {
                currentObjectTarget = target;
                currentTarget = null;
                game.Log($"AdventureTask: Found object entry {target.Entry} at distance {(target - game.Player).Length:F1}m", LogLevel.Info);
                state = AdventureState.MovingToObject;
                movingToObject = false;
            }

            consecutivePathFailures = 0;
            return TaskResult.Running;
        }

        private TaskResult HandleMovingToTarget(BotGame game)
        {
            if (currentTarget == null || !game.Objects.ContainsKey(currentTarget.GUID))
            {
                state = AdventureState.Searching;
                currentTarget = null;
                consecutivePathFailures = 0;
                return TaskResult.Running;
            }

            var targetHealth = currentTarget[UnitField.UNIT_FIELD_HEALTH];
            if (targetHealth == 0)
            {
                OnTargetKilled(game);
                return TaskResult.Running;
            }

            if (consecutivePathFailures >= MaxPathFailuresBeforeSkip)
            {
                game.Log($"AdventureTask: Target unreachable after {consecutivePathFailures} attempts, returning to center", LogLevel.Warning);
                unpathableTargets.Add(currentTarget.GUID);
                ReleaseCurrentTarget(game);

                // Return to center position if available
                if (centerPosition != null)
                {
                    state = AdventureState.ReturningToCenter;
                    game.MoveTo(centerPosition);
                }
                return TaskResult.Running;
            }

            const float engagementTimeoutSeconds = 15f;
            if ((DateTime.Now - engagementStartTime).TotalSeconds > engagementTimeoutSeconds)
            {
                game.Log($"AdventureTask: Failed to reach target, retargeting", LogLevel.Warning);
                ReleaseCurrentTarget(game);
                return TaskResult.Running;
            }

            float distance = (currentTarget - game.Player).Length;
            float combatRange = combatAI.GetPreferredCombatRange();

            if (distance <= combatRange)
            {
                game.CancelActionsByFlag(ActionFlag.Movement);
                combatAI.OnCombatStart(game, currentTarget);
                state = AdventureState.InCombat;
                lastCombatUpdate = DateTime.Now;
                consecutivePathFailures = 0;

                // Initialize combat stall tracking
                lastTargetHealth = (int)currentTarget[UnitField.UNIT_FIELD_HEALTH];
                lastHealthChangeTime = DateTime.Now;

                return TaskResult.Running;
            }

            if (!movingToTarget || (DateTime.Now - lastMoveUpdate).TotalSeconds > 1.0)
            {
                game.CancelActionsByFlag(ActionFlag.Movement);
                game.MoveTo(currentTarget.GetPosition());
                movingToTarget = true;
                lastMoveUpdate = DateTime.Now;

                if (!game.LastMoveSucceeded)
                {
                    consecutivePathFailures++;
                    totalPathFailures++;

                    if (totalPathFailures >= MaxTotalPathFailuresBeforeLogout)
                    {
                        game.Log($"AdventureTask: Too many path failures, logging out", LogLevel.Error);
                        game.Logout();
                        return TaskResult.Failed;
                    }
                }
                else
                {
                    consecutivePathFailures = 0;
                }
            }

            return TaskResult.Running;
        }

        private TaskResult HandleCombat(BotGame game)
        {
            if (currentTarget == null || !game.Objects.ContainsKey(currentTarget.GUID))
            {
                game.Log("AdventureTask: Target lost during combat", LogLevel.Warning);
                if (claimRegistry != null && currentTarget != null)
                {
                    claimRegistry.Release(currentTarget.GUID, game.Player.GUID);
                }
                combatAI.OnCombatEnd(game);
                state = AdventureState.Searching;
                currentTarget = null;
                return TaskResult.Running;
            }

            var targetHealth = (int)currentTarget[UnitField.UNIT_FIELD_HEALTH];
            if (targetHealth == 0)
            {
                OnTargetKilled(game);
                return TaskResult.Running;
            }

            // Track target health changes for combat stall detection
            if (targetHealth != lastTargetHealth)
            {
                lastTargetHealth = targetHealth;
                lastHealthChangeTime = DateTime.Now;
            }

            // Check for combat stall - no damage dealt for too long
            if ((DateTime.Now - lastHealthChangeTime).TotalSeconds > CombatStallTimeoutSeconds)
            {
                game.Log($"AdventureTask: Combat stalled for {CombatStallTimeoutSeconds}s, re-engaging target", LogLevel.Warning);

                // Stop all current actions
                game.CancelActionsByFlag(ActionFlag.Movement);
                game.StopAttack();

                // Force re-pathfind to target
                game.MoveTo(currentTarget.GetPosition());

                // Restart attack
                game.StartAttack(currentTarget.GUID);
                combatAI.OnCombatStart(game, currentTarget);

                // Reset stall tracking
                lastHealthChangeTime = DateTime.Now;
                lastTargetHealth = targetHealth;
            }

            // Refresh claim
            if (claimRegistry != null && (DateTime.Now - lastClaimRefresh).TotalSeconds > 10)
            {
                claimRegistry.RefreshClaim(currentTarget.GUID, game.Player.GUID);
                lastClaimRefresh = DateTime.Now;
            }

            float distance = (currentTarget - game.Player).Length;
            float combatRange = combatAI.GetPreferredCombatRange();

            if ((DateTime.Now - lastMoveUpdate).TotalMilliseconds > 250)
            {
                if (distance > combatRange)
                {
                    game.CancelActionsByFlag(ActionFlag.Movement);
                    game.MoveTo(currentTarget.GetPosition());
                }
                else
                {
                    game.FaceTarget(currentTarget);
                    game.SendPositionHeartbeat();
                }
                lastMoveUpdate = DateTime.Now;
            }

            if ((DateTime.Now - lastCombatUpdate).TotalMilliseconds > 1500)
            {
                combatAI.OnCombatUpdate(game, currentTarget);
                lastCombatUpdate = DateTime.Now;
            }

            return TaskResult.Running;
        }

        private void OnTargetKilled(BotGame game)
        {
            uint killedEntry = currentTarget.Entry;
            if (!killsByEntry.ContainsKey(killedEntry))
                killsByEntry[killedEntry] = 0;
            killsByEntry[killedEntry]++;
            killsCompleted++;

            LogKillProgress(game, killedEntry);

            corpseGuid = currentTarget.GUID;
            corpsePosition = currentTarget.GetPosition();

            if (claimRegistry != null && currentTarget != null)
            {
                claimRegistry.Release(currentTarget.GUID, game.Player.GUID);
            }

            combatAI.OnCombatEnd(game);
            currentTarget = null;

            lootWindowRequested = false;
            movingToCorpse = false;
            lootAttemptTime = DateTime.Now;
            state = AdventureState.Looting;
        }

        private TaskResult HandleLooting(BotGame game)
        {
            if ((DateTime.Now - lootAttemptTime).TotalSeconds > LootTimeoutSeconds)
            {
                game.Log("AdventureTask: Loot timeout, moving on", LogLevel.Warning);
                FinishLooting(game);
                return TaskResult.Running;
            }

            WorldObject corpse;
            if (!game.Objects.TryGetValue(corpseGuid, out corpse))
            {
                game.Log("AdventureTask: Corpse no longer exists", LogLevel.Debug);
                FinishLooting(game);
                return TaskResult.Running;
            }

            float distance = (corpsePosition - game.Player.GetPosition()).Length;

            if (distance > LootRange)
            {
                if (!movingToCorpse)
                {
                    game.CancelActionsByFlag(ActionFlag.Movement);
                    game.MoveTo(corpsePosition);
                    movingToCorpse = true;
                }
                return TaskResult.Running;
            }

            if (movingToCorpse)
            {
                game.CancelActionsByFlag(ActionFlag.Movement);
                movingToCorpse = false;
            }

            if (game.CurrentLoot.IsOpen && game.CurrentLoot.LootGuid == corpseGuid)
            {
                game.LootAllItems();
                if (game.CurrentLoot.Items.Count == 0 && game.CurrentLoot.Gold == 0)
                {
                    game.ReleaseLoot();
                    FinishLooting(game);
                }
                return TaskResult.Running;
            }

            if (!lootWindowRequested)
            {
                game.RequestLoot(corpseGuid);
                lootWindowRequested = true;
            }

            return TaskResult.Running;
        }

        private void FinishLooting(BotGame game)
        {
            corpseGuid = 0;
            corpsePosition = null;
            lootWindowRequested = false;
            movingToCorpse = false;

            if (combatAI.NeedsRest(game))
            {
                state = AdventureState.Resting;
            }
            else
            {
                state = AdventureState.Searching;
            }
        }

        private TaskResult HandleMovingToObject(BotGame game)
        {
            if (currentObjectTarget == null || !game.Objects.ContainsKey(currentObjectTarget.GUID))
            {
                game.Log("AdventureTask: Object disappeared", LogLevel.Warning);
                ReleaseCurrentObject(game);
                return TaskResult.Running;
            }

            if (!MoveToNPCTask.IsGameObjectUsable(currentObjectTarget))
            {
                game.Log("AdventureTask: Object no longer usable", LogLevel.Debug);
                ReleaseCurrentObject(game);
                return TaskResult.Running;
            }

            if (consecutivePathFailures >= MaxPathFailuresBeforeSkip)
            {
                game.Log($"AdventureTask: Object unreachable after {consecutivePathFailures} attempts, returning to center", LogLevel.Warning);
                unpathableTargets.Add(currentObjectTarget.GUID);
                ReleaseCurrentObject(game);

                // Return to center position if available
                if (centerPosition != null)
                {
                    state = AdventureState.ReturningToCenter;
                    game.MoveTo(centerPosition);
                }
                return TaskResult.Running;
            }

            float distance = (currentObjectTarget - game.Player).Length;

            if (distance <= ObjectInteractionRange)
            {
                game.CancelActionsByFlag(ActionFlag.Movement);
                state = AdventureState.UsingObject;
                return TaskResult.Running;
            }

            if (!movingToObject || (DateTime.Now - lastMoveUpdate).TotalSeconds > 1.0)
            {
                game.CancelActionsByFlag(ActionFlag.Movement);
                game.MoveTo(currentObjectTarget.GetPosition());
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
            if (currentObjectTarget == null || !game.Objects.ContainsKey(currentObjectTarget.GUID))
            {
                game.Log("AdventureTask: Object disappeared before use", LogLevel.Warning);
                ReleaseCurrentObject(game);
                return TaskResult.Running;
            }

            game.Log($"AdventureTask: Using object entry {currentObjectTarget.Entry}", LogLevel.Info);

            // Send use packet
            OutPacket usePacket = new OutPacket(WorldCommand.CMSG_GAMEOBJ_USE);
            usePacket.Write(currentObjectTarget.GUID);
            game.SendPacket(usePacket);

            objectUseTime = DateTime.Now;

            if (objectsGiveLoot)
            {
                state = AdventureState.LootingObject;
            }
            else
            {
                OnObjectUsed(game);
            }

            return TaskResult.Running;
        }

        private TaskResult HandleLootingObject(BotGame game)
        {
            if ((DateTime.Now - objectUseTime).TotalSeconds > ObjectUseTimeout)
            {
                game.Log("AdventureTask: Object loot timeout", LogLevel.Warning);
                OnObjectUsed(game);
                return TaskResult.Running;
            }

            // Check if object despawned
            if (currentObjectTarget == null || !game.Objects.ContainsKey(currentObjectTarget.GUID))
            {
                game.Log("AdventureTask: Object despawned after use", LogLevel.Debug);
                OnObjectUsed(game);
                return TaskResult.Running;
            }

            // Check loot window
            if (game.CurrentLoot.IsOpen && game.CurrentLoot.LootGuid == currentObjectTarget.GUID)
            {
                game.LootAllItems();
                if (game.CurrentLoot.Items.Count == 0 && game.CurrentLoot.Gold == 0)
                {
                    game.ReleaseLoot();
                    OnObjectUsed(game);
                }
                return TaskResult.Running;
            }

            return TaskResult.Running;
        }

        private TaskResult HandleReturningToCenter(BotGame game)
        {
            // Check if we've reached the center
            if (centerPosition == null)
            {
                state = AdventureState.Searching;
                return TaskResult.Running;
            }

            float distance = (centerPosition - game.Player.GetPosition()).Length;
            if (distance <= 5.0f)
            {
                game.Log("AdventureTask: Returned to center, resuming search", LogLevel.Info);
                game.CancelActionsByFlag(ActionFlag.Movement);
                state = AdventureState.Searching;
                return TaskResult.Running;
            }

            // Keep moving to center
            if ((DateTime.Now - lastMoveUpdate).TotalSeconds > 1.0)
            {
                game.MoveTo(centerPosition);
                lastMoveUpdate = DateTime.Now;
            }

            return TaskResult.Running;
        }

        private void OnObjectUsed(BotGame game)
        {
            if (currentObjectTarget != null)
            {
                uint usedEntry = currentObjectTarget.Entry;
                if (!usesByObjectEntry.ContainsKey(usedEntry))
                    usesByObjectEntry[usedEntry] = 0;
                usesByObjectEntry[usedEntry]++;

                game.Log($"AdventureTask: Object used (entry {usedEntry}: {usesByObjectEntry[usedEntry]})", LogLevel.Info);
            }

            ReleaseCurrentObject(game);
        }

        #endregion

        #region Target Finding

        private enum TargetType { Mob, Object }

        private (WorldObject target, TargetType type) FindNextTarget(BotGame game)
        {
            var searchCenter = centerPosition ?? game.Player.GetPosition();

            // Determine what we still need
            var neededMobsForItems = GetMobsNeededForItems(game);
            var neededMobsForKills = GetMobsNeededForKills();
            var neededObjectsForItems = GetObjectsNeededForItems(game);
            var neededObjectsForUses = GetObjectsNeededForUses();

            // Build combined candidate list with priorities
            var candidates = new List<(WorldObject obj, TargetType type, int priority)>();

            // Add mobs
            foreach (var obj in game.Objects.Values.Where(o => IsValidMobTarget(o, game, searchCenter)))
            {
                int priority = GetMobPriority(obj.Entry, neededMobsForItems, neededMobsForKills);
                candidates.Add((obj, TargetType.Mob, priority));
            }

            // Add objects
            foreach (var obj in game.Objects.Values.Where(o => IsValidObjectTarget(o, game, searchCenter)))
            {
                int priority = GetObjectPriority(obj.Entry, neededObjectsForItems, neededObjectsForUses);
                candidates.Add((obj, TargetType.Object, priority));
            }

            // Sort by priority then distance
            var sorted = candidates
                .OrderBy(c => c.priority)
                .ThenBy(c => (c.obj - game.Player).Length);

            // Try to claim
            foreach (var (obj, type, priority) in sorted)
            {
                if (claimRegistry == null || claimRegistry.TryClaim(obj.GUID, game.Player.GUID))
                {
                    return (obj, type);
                }
            }

            return (null, TargetType.Mob);
        }

        private bool IsValidMobTarget(WorldObject obj, BotGame game, Position searchCenter)
        {
            if (!obj.IsType(HighGuid.Unit)) return false;
            if (obj.GUID == game.Player.GUID) return false;
            if (obj.MapID != game.Player.MapID) return false;
            if (obj[UnitField.UNIT_FIELD_HEALTH] == 0) return false;
            if (targetEntries.Count > 0 && !targetEntries.Contains(obj.Entry)) return false;
            if ((obj - searchCenter).Length > searchRadius) return false;
            if (claimRegistry != null && claimRegistry.IsClaimedByOther(obj.GUID, game.Player.GUID)) return false;
            if (unpathableTargets.Contains(obj.GUID)) return false;
            if (IsInCombatWithNonBot(obj, game)) return false;
            return true;
        }

        private bool IsValidObjectTarget(WorldObject obj, BotGame game, Position searchCenter)
        {
            if (!obj.IsType(HighGuid.GameObject)) return false;
            if (obj.MapID != game.Player.MapID) return false;
            if (objectEntries.Count > 0 && !objectEntries.Contains(obj.Entry)) return false;
            if (!MoveToNPCTask.IsGameObjectUsable(obj)) return false;
            if ((obj - searchCenter).Length > searchRadius) return false;
            if (claimRegistry != null && claimRegistry.IsClaimedByOther(obj.GUID, game.Player.GUID)) return false;
            if (unpathableTargets.Contains(obj.GUID)) return false;
            return true;
        }

        private bool IsInCombatWithNonBot(WorldObject obj, BotGame game)
        {
            uint lowTarget = obj[UnitField.UNIT_FIELD_TARGET];
            uint highTarget = obj[(int)UnitField.UNIT_FIELD_TARGET + 1];
            ulong mobTargetGuid = ((ulong)highTarget << 32) | lowTarget;

            if (mobTargetGuid == 0) return false;
            if (mobTargetGuid == game.Player.GUID) return false;
            if (BotFactory.Instance.IsBot(mobTargetGuid)) return false;
            return true;
        }

        private int GetMobPriority(uint entry, HashSet<uint> neededForItems, HashSet<uint> neededForKills)
        {
            if (neededForItems.Contains(entry)) return 0;
            if (neededForKills.Contains(entry)) return 1;
            return 2;
        }

        private int GetObjectPriority(uint entry, HashSet<uint> neededForItems, HashSet<uint> neededForUses)
        {
            if (neededForItems.Contains(entry)) return 0;
            if (neededForUses.Contains(entry)) return 1;
            return 2;
        }

        #endregion

        #region Requirement Tracking

        private HashSet<uint> GetMobsNeededForItems(AutomatedGame game)
        {
            var needed = new HashSet<uint>();
            if (collectItems != null)
            {
                foreach (var req in collectItems)
                {
                    if (GetItemCount(game, req.ItemEntry) < req.Count && req.DroppedBy != null)
                    {
                        foreach (var entry in req.DroppedBy)
                        {
                            // Only add if it's a mob entry (not in objectEntries)
                            if (!objectEntries.Contains(entry))
                                needed.Add(entry);
                        }
                    }
                }
            }
            return needed;
        }

        private HashSet<uint> GetMobsNeededForKills()
        {
            var needed = new HashSet<uint>();
            if (killRequirements != null)
            {
                foreach (var req in killRequirements)
                {
                    int killed = killsByEntry.TryGetValue(req.Entry, out var count) ? count : 0;
                    if (killed < req.Count)
                        needed.Add(req.Entry);
                }
            }
            return needed;
        }

        private HashSet<uint> GetObjectsNeededForItems(AutomatedGame game)
        {
            var needed = new HashSet<uint>();
            if (collectItems != null)
            {
                foreach (var req in collectItems)
                {
                    if (GetItemCount(game, req.ItemEntry) < req.Count && req.DroppedBy != null)
                    {
                        foreach (var entry in req.DroppedBy)
                        {
                            // Only add if it's an object entry
                            if (objectEntries.Contains(entry))
                                needed.Add(entry);
                        }
                    }
                }
            }
            return needed;
        }

        private HashSet<uint> GetObjectsNeededForUses()
        {
            var needed = new HashSet<uint>();
            if (objectRequirements != null)
            {
                foreach (var req in objectRequirements)
                {
                    int used = usesByObjectEntry.TryGetValue(req.Entry, out var count) ? count : 0;
                    if (used < req.Count)
                        needed.Add(req.Entry);
                }
            }
            return needed;
        }

        private bool AreAllRequirementsMet(AutomatedGame game)
        {
            return AreAllKillRequirementsMet() &&
                   AreAllObjectRequirementsMet() &&
                   AreAllItemsCollected(game) &&
                   HasAnyRequirements();
        }

        private bool AreAllKillRequirementsMet()
        {
            if (killRequirements != null && killRequirements.Count > 0)
            {
                foreach (var req in killRequirements)
                {
                    int killed = killsByEntry.TryGetValue(req.Entry, out var count) ? count : 0;
                    if (killed < req.Count) return false;
                }
                return true;
            }
            return killCount <= 0 || killsCompleted >= killCount;
        }

        private bool AreAllObjectRequirementsMet()
        {
            if (objectRequirements != null && objectRequirements.Count > 0)
            {
                foreach (var req in objectRequirements)
                {
                    int used = usesByObjectEntry.TryGetValue(req.Entry, out var count) ? count : 0;
                    if (used < req.Count) return false;
                }
                return true;
            }
            return true;
        }

        private bool AreAllItemsCollected(AutomatedGame game)
        {
            if (collectItems != null && collectItems.Count > 0)
            {
                foreach (var req in collectItems)
                {
                    if (GetItemCount(game, req.ItemEntry) < req.Count)
                        return false;
                }
                return true;
            }
            return collectItemEntry <= 0 || collectItemCount <= 0
                || GetItemCount(game, collectItemEntry) >= collectItemCount;
        }

        private bool HasAnyRequirements()
        {
            return killCount > 0
                || (killRequirements != null && killRequirements.Count > 0)
                || (collectItemEntry > 0 && collectItemCount > 0)
                || (collectItems != null && collectItems.Count > 0)
                || (objectRequirements != null && objectRequirements.Count > 0);
        }

        private int GetItemCount(AutomatedGame game, uint itemEntry)
        {
            int totalCount = 0;
            int packSlotBase = (int)PlayerField.PLAYER_FIELD_PACK_SLOT_1;

            for (int slot = 0; slot < 16; slot++)
            {
                uint guidLow = game.Player[packSlotBase + slot * 2];
                uint guidHigh = game.Player[packSlotBase + slot * 2 + 1];
                ulong itemGuid = ((ulong)guidHigh << 32) | guidLow;

                if (itemGuid == 0) continue;

                if (game.Objects.TryGetValue(itemGuid, out var itemObject))
                {
                    if (itemObject.Entry == itemEntry)
                    {
                        uint stackCount = itemObject[(int)ItemField.ITEM_FIELD_STACK_COUNT];
                        totalCount += (int)(stackCount > 0 ? stackCount : 1);
                    }
                }
            }

            return totalCount;
        }

        #endregion

        #region Utilities

        private void ReleaseCurrentTarget(BotGame game)
        {
            if (claimRegistry != null && currentTarget != null)
            {
                claimRegistry.Release(currentTarget.GUID, game.Player.GUID);
            }
            game.CancelActionsByFlag(ActionFlag.Movement);
            currentTarget = null;
            consecutivePathFailures = 0;
            state = AdventureState.Searching;
        }

        private void ReleaseCurrentObject(BotGame game)
        {
            if (claimRegistry != null && currentObjectTarget != null)
            {
                claimRegistry.Release(currentObjectTarget.GUID, game.Player.GUID);
            }
            game.CancelActionsByFlag(ActionFlag.Movement);
            currentObjectTarget = null;
            movingToObject = false;
            consecutivePathFailures = 0;
            state = AdventureState.Searching;
        }

        private void CheckIfStuck(BotGame game)
        {
            if ((DateTime.Now - lastStuckCheckTime).TotalSeconds < StuckCheckIntervalSeconds)
                return;

            var currentPos = game.Player.GetPosition();
            lastStuckCheckTime = DateTime.Now;

            if (lastStuckCheckPosition != null)
            {
                float distanceMoved = (currentPos - lastStuckCheckPosition).Length;

                if (distanceMoved < StuckDistanceThreshold)
                {
                    stuckCounter++;
                    game.Log($"AdventureTask: Possible stuck detected (count: {stuckCounter})", LogLevel.Warning);

                    if (stuckCounter >= MaxStuckCountBeforeUnstuck)
                    {
                        TryUnstuck(game);
                        stuckCounter = 0;
                    }
                }
                else
                {
                    stuckCounter = 0;
                }
            }

            lastStuckCheckPosition = currentPos;
        }

        private void TryUnstuck(BotGame game)
        {
            game.Log("AdventureTask: Attempting to unstick", LogLevel.Warning);
            game.CancelActionsByFlag(ActionFlag.Movement);

            if (currentTarget != null)
            {
                unpathableTargets.Add(currentTarget.GUID);
                ReleaseCurrentTarget(game);
            }
            else if (currentObjectTarget != null)
            {
                unpathableTargets.Add(currentObjectTarget.GUID);
                ReleaseCurrentObject(game);
            }

            consecutivePathFailures = 0;
            movingToTarget = false;
            movingToObject = false;
            movingToCorpse = false;
            state = AdventureState.Searching;
        }

        #endregion

        #region Logging

        private void LogStartStatus(AutomatedGame game, Class playerClass)
        {
            var parts = new List<string>();
            parts.Add($"Class: {playerClass}");

            if (targetEntries.Count > 0)
                parts.Add($"Mob targets: [{string.Join(",", targetEntries)}]");
            if (objectEntries.Count > 0)
                parts.Add($"Object targets: [{string.Join(",", objectEntries)}]");

            if (killRequirements != null && killRequirements.Count > 0)
                parts.Add($"Kill reqs: {string.Join(", ", killRequirements.Select(r => $"{r.Entry}x{r.Count}"))}");
            if (objectRequirements != null && objectRequirements.Count > 0)
                parts.Add($"Object reqs: {string.Join(", ", objectRequirements.Select(r => $"{r.Entry}x{r.Count}"))}");
            if (collectItems != null && collectItems.Count > 0)
                parts.Add($"Item reqs: {string.Join(", ", collectItems.Select(r => $"item{r.ItemEntry}x{r.Count}"))}");

            if (defendSelf)
                parts.Add("defendSelf=ON");

            game.Log($"AdventureTask: Starting. {string.Join(", ", parts)}", LogLevel.Info);
        }

        private void LogKillProgress(BotGame game, uint killedEntry)
        {
            var parts = new List<string>();

            if (killRequirements != null && killRequirements.Count > 0)
            {
                foreach (var req in killRequirements)
                {
                    int killed = killsByEntry.TryGetValue(req.Entry, out var count) ? count : 0;
                    parts.Add($"entry {req.Entry}: {killed}/{req.Count}");
                }
            }
            else if (killCount > 0)
            {
                parts.Add($"kills: {killsCompleted}/{killCount}");
            }

            // Add item collection progress
            if (collectItems != null && collectItems.Count > 0)
            {
                foreach (var req in collectItems)
                {
                    int collected = GetItemCount(game, req.ItemEntry);
                    parts.Add($"item {req.ItemEntry}: {collected}/{req.Count}");
                }
            }
            else if (collectItemEntry > 0 && collectItemCount > 0)
            {
                int collected = GetItemCount(game, collectItemEntry);
                parts.Add($"item {collectItemEntry}: {collected}/{collectItemCount}");
            }

            game.Log($"AdventureTask: Killed entry {killedEntry} - Progress: {string.Join(", ", parts)}", LogLevel.Info);
        }

        private void LogCompletionStatus(AutomatedGame game)
        {
            var parts = new List<string>();

            if (killRequirements != null && killRequirements.Count > 0)
            {
                foreach (var req in killRequirements)
                {
                    int killed = killsByEntry.TryGetValue(req.Entry, out var count) ? count : 0;
                    parts.Add($"kills {req.Entry}: {killed}/{req.Count}");
                }
            }

            if (objectRequirements != null && objectRequirements.Count > 0)
            {
                foreach (var req in objectRequirements)
                {
                    int used = usesByObjectEntry.TryGetValue(req.Entry, out var count) ? count : 0;
                    parts.Add($"objects {req.Entry}: {used}/{req.Count}");
                }
            }

            if (collectItems != null && collectItems.Count > 0)
            {
                foreach (var req in collectItems)
                {
                    int collected = GetItemCount(game, req.ItemEntry);
                    parts.Add($"item {req.ItemEntry}: {collected}/{req.Count}");
                }
            }

            game.Log($"AdventureTask: All requirements met! {string.Join(", ", parts)}", LogLevel.Info);
        }

        #endregion

        public override void Cleanup(AutomatedGame game)
        {
            if (game is BotGame botGame)
            {
                if (claimRegistry != null)
                {
                    if (currentTarget != null)
                        claimRegistry.Release(currentTarget.GUID, botGame.Player.GUID);
                    if (currentObjectTarget != null)
                        claimRegistry.Release(currentObjectTarget.GUID, botGame.Player.GUID);
                }
            }

            if (game.CurrentLoot.IsOpen)
            {
                game.ReleaseLoot();
            }

            if (combatAI != null && game is BotGame bg)
            {
                combatAI.OnCombatEnd(bg);
            }

            currentTarget = null;
            currentObjectTarget = null;
            combatAI = null;
            corpseGuid = 0;
            corpsePosition = null;
        }
    }
}
