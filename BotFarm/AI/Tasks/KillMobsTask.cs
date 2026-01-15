using Client;
using Client.AI.Tasks;
using Client.UI;
using Client.World;
using Client.World.Entities;
using Client.World.Definitions;
using BotFarm.AI.Combat;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BotFarm.AI.Tasks
{
    /// <summary>
    /// Represents a kill requirement for a specific creature entry
    /// </summary>
    public class KillRequirement
    {
        public uint Entry { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// Represents an item collection requirement
    /// </summary>
    public class ItemCollectionRequirement
    {
        public uint ItemEntry { get; set; }
        public int Count { get; set; }
        /// <summary>
        /// Creature entries that drop this item. Used for target prioritization.
        /// </summary>
        public HashSet<uint> DroppedBy { get; set; }
    }

    /// <summary>
    /// Task that kills mobs in an area until kill and item collection requirements are met.
    /// Supports multiple kill requirements per mob type and multiple item collection requirements.
    /// Uses class-specific combat AI for abilities.
    /// </summary>
    public class KillMobsTask : BaseTask
    {
        private readonly HashSet<uint> targetEntries;
        private readonly float searchRadius;
        private readonly int killCount;
        private readonly float maxDurationSeconds;
        private readonly Position centerPosition;

        // Item collection parameters (single item - backward compat)
        private readonly uint collectItemEntry;
        private readonly int collectItemCount;

        // Multiple kill requirements (per mob type)
        private readonly List<KillRequirement> killRequirements;

        // Multiple item collection requirements
        private readonly List<ItemCollectionRequirement> collectItems;

        // Track kills per creature entry
        private Dictionary<uint, int> killsByEntry = new Dictionary<uint, int>();

        private IClassCombatAI combatAI;
        private WorldObject currentTarget;
        private int killsCompleted;
        private DateTime startTime;
        private DateTime lastCombatUpdate;
        private DateTime lastSearchTime;
        private DateTime lastMoveUpdate;
        private DateTime lastClaimRefresh;
        private DateTime engagementStartTime;
        private KillMobsState state;
        private bool movingToTarget;

        // Target claiming for multi-bot coordination
        private ClaimedTargetRegistry claimRegistry;
        private string routeFilePath;

        // Looting state tracking
        private ulong corpseGuid;
        private Position corpsePosition;
        private DateTime lootAttemptTime;
        private bool lootWindowRequested;
        private bool movingToCorpse;
        private const float LootRange = 5.0f;
        private const float LootTimeoutSeconds = 5.0f;

        // Track targets that can't be pathed to (cleared periodically)
        private HashSet<ulong> unpathableTargets = new HashSet<ulong>();
        private DateTime lastUnpathableClear = DateTime.Now;
        private int consecutivePathFailures;
        private const int MaxPathFailuresBeforeSkip = 3;
        private const float UnpathableClearIntervalSeconds = 30f;

        // Stuck detection
        private Position lastStuckCheckPosition;
        private DateTime lastStuckCheckTime = DateTime.Now;
        private int stuckCounter;
        private const float StuckDistanceThreshold = 1.0f;  // If moved less than this in 5 seconds while trying to move
        private const float StuckCheckIntervalSeconds = 5f;
        private const int MaxStuckCountBeforeUnstuck = 2;

        // Total path failure tracking for logout
        private int totalPathFailures;
        private const int MaxTotalPathFailuresBeforeLogout = 20;
        
        private enum KillMobsState
        {
            Searching,      // Looking for a mob to kill
            MovingToTarget, // Moving to engage
            InCombat,       // Fighting
            Resting,        // Recovering health/mana
            Looting         // Looting corpse after kill
        }
        
        public override string Name
        {
            get
            {
                var parts = new List<string>();

                // Kill requirements
                if (killRequirements != null && killRequirements.Count > 0)
                {
                    var killParts = killRequirements.Select(r => $"{r.Count}x entry {r.Entry}");
                    parts.Add($"kill [{string.Join(", ", killParts)}]");
                }
                else if (killCount > 0)
                {
                    parts.Add($"{killCount} kills");
                }

                // Item collection requirements
                if (collectItems != null && collectItems.Count > 0)
                {
                    var itemParts = collectItems.Select(r => $"{r.Count}x item {r.ItemEntry}");
                    parts.Add($"collect [{string.Join(", ", itemParts)}]");
                }
                else if (collectItemEntry > 0 && collectItemCount > 0)
                {
                    parts.Add($"collect {collectItemCount}x item {collectItemEntry}");
                }

                if (parts.Count == 0)
                    parts.Add("unlimited");

                return $"KillMobs({string.Join(" + ", parts)})";
            }
        }
        
        /// <summary>
        /// Create a kill mobs task
        /// </summary>
        /// <param name="targetEntries">Creature entry IDs to kill (empty = any hostile mob)</param>
        /// <param name="killCount">Number of kills required (0 = infinite/until time limit). Ignored if killRequirements specified.</param>
        /// <param name="searchRadius">Radius to search for mobs (default 50)</param>
        /// <param name="maxDurationSeconds">Maximum time to spend (0 = no limit)</param>
        /// <param name="centerX">Center X position to search around (0 = use current position)</param>
        /// <param name="centerY">Center Y position</param>
        /// <param name="centerZ">Center Z position</param>
        /// <param name="mapId">Map ID for center position</param>
        /// <param name="collectItemEntry">Item entry ID to collect (0 = use killCount instead). Ignored if collectItems specified.</param>
        /// <param name="collectItemCount">Number of items to collect before completing</param>
        /// <param name="killRequirements">Per-mob-type kill requirements. Takes precedence over killCount.</param>
        /// <param name="collectItems">Multiple item collection requirements. Takes precedence over collectItemEntry/Count.</param>
        public KillMobsTask(
            uint[] targetEntries = null,
            int killCount = 0,
            float searchRadius = 50f,
            float maxDurationSeconds = 0,
            float centerX = 0, float centerY = 0, float centerZ = 0, int mapId = 0,
            uint collectItemEntry = 0, int collectItemCount = 0,
            List<KillRequirement> killRequirements = null,
            List<ItemCollectionRequirement> collectItems = null)
        {
            this.targetEntries = targetEntries != null ? new HashSet<uint>(targetEntries) : new HashSet<uint>();
            this.killCount = killCount;
            this.searchRadius = searchRadius;
            this.maxDurationSeconds = maxDurationSeconds;
            this.collectItemEntry = collectItemEntry;
            this.collectItemCount = collectItemCount;
            this.killRequirements = killRequirements;
            this.collectItems = collectItems;

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
                game.Log("KillMobsTask: Game is not a BotGame instance", LogLevel.Error);
                return false;
            }
            
            // Get combat AI for the player's class
            var playerClass = game.World.SelectedCharacter?.Class ?? Client.World.Definitions.Class.Warrior;
            combatAI = CombatAIFactory.CreateForClass(playerClass);

            // Reset tracking state
            killsCompleted = 0;
            killsByEntry.Clear();
            unpathableTargets.Clear();
            consecutivePathFailures = 0;
            startTime = DateTime.Now;
            state = KillMobsState.Searching;

            // Log all requirements
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
                game.Log("KillMobsTask: Player died", LogLevel.Warning);
                return TaskResult.Failed;
            }

            // Periodically clear unpathable targets (mobs may have moved to reachable positions)
            if ((DateTime.Now - lastUnpathableClear).TotalSeconds > UnpathableClearIntervalSeconds)
            {
                if (unpathableTargets.Count > 0)
                {
                    game.Log($"KillMobsTask: Clearing {unpathableTargets.Count} unpathable targets for retry", LogLevel.Debug);
                    unpathableTargets.Clear();
                }
                lastUnpathableClear = DateTime.Now;
            }

            // Stuck detection - check if we're moving but not making progress
            if (state == KillMobsState.MovingToTarget || movingToCorpse)
            {
                CheckIfStuck(botGame);
            }
            else
            {
                // Reset stuck detection when not moving
                stuckCounter = 0;
                lastStuckCheckPosition = null;
            }
            
            // Check completion conditions (AND logic - ALL requirements must be met)
            bool killsSatisfied = AreAllKillRequirementsMet();
            bool itemsSatisfied = AreAllItemsCollected(game);

            if (killsSatisfied && itemsSatisfied && HasAnyRequirements())
            {
                LogCompletionStatus(game);
                return TaskResult.Success;
            }
            
            if (maxDurationSeconds > 0 && (DateTime.Now - startTime).TotalSeconds > maxDurationSeconds)
            {
                game.Log($"KillMobsTask: Time limit reached. Completed {killsCompleted} kills", LogLevel.Info);
                return TaskResult.Success;
            }
            
            // State machine
            switch (state)
            {
                case KillMobsState.Resting:
                    return HandleResting(botGame);

                case KillMobsState.Searching:
                    return HandleSearching(botGame);

                case KillMobsState.MovingToTarget:
                    return HandleMovingToTarget(botGame);

                case KillMobsState.InCombat:
                    return HandleCombat(botGame);

                case KillMobsState.Looting:
                    return HandleLooting(botGame);
            }
            
            return TaskResult.Running;
        }
        
        private TaskResult HandleResting(BotGame game)
        {
            // Check if we need to rest
            if (combatAI.NeedsRest(game))
            {
                if (!combatAI.OnRest(game))
                {
                    // Still resting
                    return TaskResult.Running;
                }
            }
            
            // Done resting, look for next target
            state = KillMobsState.Searching;
            return TaskResult.Running;
        }
        
        private TaskResult HandleSearching(BotGame game)
        {
            // Check if we need to rest first
            if (combatAI.NeedsRest(game))
            {
                state = KillMobsState.Resting;
                return TaskResult.Running;
            }
            
            // Only search every 1 second to avoid spam
            if ((DateTime.Now - lastSearchTime).TotalSeconds < 1.0)
            {
                return TaskResult.Running;
            }
            lastSearchTime = DateTime.Now;
            
            // Find a valid target
            currentTarget = FindTarget(game);
            
            if (currentTarget == null)
            {
                // No targets found - log periodically (every 5 seconds)
                if ((DateTime.Now - startTime).TotalSeconds % 5 < 1)
                {
                    game.Log($"KillMobsTask: No valid targets in range (searching for entries: [{string.Join(",", targetEntries)}])", LogLevel.Debug);
                }
                return TaskResult.Running;
            }
            
            game.Log($"KillMobsTask: Found target entry {currentTarget.Entry} at distance {(currentTarget - game.Player).Length:F1}m", LogLevel.Info);
            state = KillMobsState.MovingToTarget;
            engagementStartTime = DateTime.Now;
            movingToTarget = false;
            return TaskResult.Running;
        }

        private TaskResult HandleMovingToTarget(BotGame game)
        {
            // Check if target is still valid
            if (currentTarget == null || !game.Objects.ContainsKey(currentTarget.GUID))
            {
                state = KillMobsState.Searching;
                currentTarget = null;
                consecutivePathFailures = 0;
                return TaskResult.Running;
            }
            
            // Check if target is dead
            var targetHealth = currentTarget[UnitField.UNIT_FIELD_HEALTH];
            if (targetHealth == 0)
            {
                OnTargetKilled(game);
                return TaskResult.Running;
            }

            // Check if too many consecutive path failures - skip this target
            if (consecutivePathFailures >= MaxPathFailuresBeforeSkip)
            {
                game.Log($"KillMobsTask: Target unreachable after {consecutivePathFailures} path failures, skipping", LogLevel.Warning);
                unpathableTargets.Add(currentTarget.GUID);
                
                if (claimRegistry != null)
                {
                    claimRegistry.Release(currentTarget.GUID, game.Player.GUID);
                }

                game.CancelActionsByFlag(ActionFlag.Movement);
                currentTarget = null;
                consecutivePathFailures = 0;
                state = KillMobsState.Searching;
                return TaskResult.Running;
            }

            // Check engagement timeout - retarget if we can't reach the mob
            const float engagementTimeoutSeconds = 15f;
            if ((DateTime.Now - engagementStartTime).TotalSeconds > engagementTimeoutSeconds)
            {
                game.Log($"KillMobsTask: Failed to reach target after {engagementTimeoutSeconds}s, retargeting", LogLevel.Warning);

                // Release claim if we have one
                if (claimRegistry != null)
                {
                    claimRegistry.Release(currentTarget.GUID, game.Player.GUID);
                }

                game.CancelActionsByFlag(ActionFlag.Movement);
                currentTarget = null;
                consecutivePathFailures = 0;
                state = KillMobsState.Searching;
                return TaskResult.Running;
            }

            float distance = (currentTarget - game.Player).Length;
            float combatRange = combatAI.GetPreferredCombatRange();
            
            if (distance <= combatRange)
            {
                // In range, start combat
                game.CancelActionsByFlag(ActionFlag.Movement);
                combatAI.OnCombatStart(game, currentTarget);
                state = KillMobsState.InCombat;
                lastCombatUpdate = DateTime.Now;
                consecutivePathFailures = 0;
                return TaskResult.Running;
            }
            
            // Move closer - update movement periodically in case target moves
            if (!movingToTarget || (DateTime.Now - lastMoveUpdate).TotalSeconds > 1.0)
            {
                game.CancelActionsByFlag(ActionFlag.Movement);
                game.MoveTo(currentTarget.GetPosition());
                movingToTarget = true;
                lastMoveUpdate = DateTime.Now;

                // Track path failures
                if (!game.LastMoveSucceeded)
                {
                    consecutivePathFailures++;
                    totalPathFailures++;

                    // Check if too many total failures - bot is probably stuck in bad position
                    if (totalPathFailures >= MaxTotalPathFailuresBeforeLogout)
                    {
                        game.Log($"KillMobsTask: Too many path failures ({totalPathFailures}), logging out to reset position", LogLevel.Error);
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
            // Check if target is still valid
            if (currentTarget == null || !game.Objects.ContainsKey(currentTarget.GUID))
            {
                game.Log("KillMobsTask: Target lost during combat", LogLevel.Warning);
                // Release claim on lost target
                if (claimRegistry != null && currentTarget != null)
                {
                    claimRegistry.Release(currentTarget.GUID, game.Player.GUID);
                }
                combatAI.OnCombatEnd(game);
                state = KillMobsState.Searching;
                currentTarget = null;
                return TaskResult.Running;
            }

            // Check if target is dead
            var targetHealth = currentTarget[UnitField.UNIT_FIELD_HEALTH];
            if (targetHealth == 0)
            {
                OnTargetKilled(game);
                return TaskResult.Running;
            }

            // Refresh claim periodically to prevent timeout
            if (claimRegistry != null && (DateTime.Now - lastClaimRefresh).TotalSeconds > 10)
            {
                claimRegistry.RefreshClaim(currentTarget.GUID, game.Player.GUID);
                lastClaimRefresh = DateTime.Now;
            }

            // Stay in range and facing target - check frequently for mobile enemies
            float distance = (currentTarget - game.Player).Length;
            float combatRange = combatAI.GetPreferredCombatRange();

            // Check movement/facing every 250ms for responsive combat
            if ((DateTime.Now - lastMoveUpdate).TotalMilliseconds > 250)
            {
                if (distance > combatRange)
                {
                    // Target moved out of range, chase it
                    game.CancelActionsByFlag(ActionFlag.Movement);
                    game.MoveTo(currentTarget.GetPosition());
                }
                else
                {
                    // In range - face the target and send position heartbeat
                    // The heartbeat prevents server-side position desync that can cause
                    // both bot and mob to stand still thinking they're out of range
                    game.FaceTarget(currentTarget);
                    game.SendPositionHeartbeat();
                }
                lastMoveUpdate = DateTime.Now;
            }

            // Update combat AI (throttled to avoid spam)
            if ((DateTime.Now - lastCombatUpdate).TotalMilliseconds > 1500)
            {
                combatAI.OnCombatUpdate(game, currentTarget);
                lastCombatUpdate = DateTime.Now;
            }

            return TaskResult.Running;
        }
        
        private void OnTargetKilled(BotGame game)
        {
            // Track kills per entry
            uint killedEntry = currentTarget.Entry;
            if (!killsByEntry.ContainsKey(killedEntry))
                killsByEntry[killedEntry] = 0;
            killsByEntry[killedEntry]++;

            // Also track total kills for backward compat
            killsCompleted++;

            // Log progress
            LogKillProgress(game, killedEntry);

            // Store corpse info for looting before clearing target
            corpseGuid = currentTarget.GUID;
            corpsePosition = currentTarget.GetPosition();

            // Release the claim when mob dies
            if (claimRegistry != null && currentTarget != null)
            {
                claimRegistry.Release(currentTarget.GUID, game.Player.GUID);
            }

            combatAI.OnCombatEnd(game);
            currentTarget = null;

            // Transition to looting state
            lootWindowRequested = false;
            movingToCorpse = false;
            lootAttemptTime = DateTime.Now;
            state = KillMobsState.Looting;
        }

        private TaskResult HandleLooting(BotGame game)
        {
            // Timeout check - don't get stuck if looting fails
            if ((DateTime.Now - lootAttemptTime).TotalSeconds > LootTimeoutSeconds)
            {
                game.Log("KillMobsTask: Loot timeout, moving on", LogLevel.Warning);
                FinishLooting(game);
                return TaskResult.Running;
            }

            // Check if corpse is still valid (hasn't despawned)
            WorldObject corpse;
            if (!game.Objects.TryGetValue(corpseGuid, out corpse))
            {
                game.Log("KillMobsTask: Corpse no longer exists, skipping loot", LogLevel.Debug);
                FinishLooting(game);
                return TaskResult.Running;
            }

            // Check if we're in loot range
            float distance = (corpsePosition - game.Player.GetPosition()).Length;

            if (distance > LootRange)
            {
                // Move to corpse
                if (!movingToCorpse)
                {
                    game.CancelActionsByFlag(ActionFlag.Movement);
                    game.MoveTo(corpsePosition);
                    movingToCorpse = true;
                    game.Log($"KillMobsTask: Moving to corpse (distance: {distance:F1}m)", LogLevel.Debug);
                }
                return TaskResult.Running;
            }

            // We're in range - stop moving if we were
            if (movingToCorpse)
            {
                game.CancelActionsByFlag(ActionFlag.Movement);
                movingToCorpse = false;
            }

            // Check if loot window is open
            if (game.CurrentLoot.IsOpen && game.CurrentLoot.LootGuid == corpseGuid)
            {
                // Loot everything
                game.LootAllItems();

                // Check if all items have been looted
                if (game.CurrentLoot.Items.Count == 0 && game.CurrentLoot.Gold == 0)
                {
                    game.ReleaseLoot();
                    FinishLooting(game);
                }
                return TaskResult.Running;
            }

            // Request loot if we haven't yet
            if (!lootWindowRequested)
            {
                game.RequestLoot(corpseGuid);
                lootWindowRequested = true;
                game.Log("KillMobsTask: Requesting loot window", LogLevel.Debug);
            }

            return TaskResult.Running;
        }

        private void FinishLooting(BotGame game)
        {
            corpseGuid = 0;
            corpsePosition = null;
            lootWindowRequested = false;
            movingToCorpse = false;

            // Check if we need to rest
            if (combatAI.NeedsRest(game))
            {
                state = KillMobsState.Resting;
            }
            else
            {
                state = KillMobsState.Searching;
            }
        }

        private WorldObject FindTarget(BotGame game)
        {
            var searchCenter = centerPosition ?? game.Player.GetPosition();

            // Get sets of mob entries we need for incomplete requirements
            var neededForItems = GetMobsNeededForItems(game);
            var neededForKills = GetMobsNeededForKills();

            // Order candidates by priority, then by distance
            // Priority 0 = needed for item drops (highest)
            // Priority 1 = needed for kill count
            // Priority 2 = any other valid target
            var candidates = game.Objects.Values
                .Where(obj => IsValidTarget(obj, game, searchCenter))
                .OrderBy(obj => GetTargetPriority(obj.Entry, neededForItems, neededForKills))
                .ThenBy(obj => (obj - game.Player).Length);

            // Try to claim each candidate in order until one succeeds
            foreach (var candidate in candidates)
            {
                if (claimRegistry == null)
                {
                    // No registry - just return the first valid target
                    return candidate;
                }

                // Try to claim this target atomically
                if (claimRegistry.TryClaim(candidate.GUID, game.Player.GUID))
                {
                    return candidate;
                }
                // Claim failed (another bot got it first), try next candidate
            }

            return null;
        }
        
        private bool IsValidTarget(WorldObject obj, BotGame game, Position searchCenter)
        {
            // Must be a creature (Unit type)
            if (!obj.IsType(HighGuid.Unit))
                return false;

            // Must not be player
            if (obj.GUID == game.Player.GUID)
                return false;

            // Must be on same map
            if (obj.MapID != game.Player.MapID)
                return false;

            // Must be alive
            if (obj[UnitField.UNIT_FIELD_HEALTH] == 0)
                return false;

            // Must match entry filter (if specified)
            if (targetEntries.Count > 0 && !targetEntries.Contains(obj.Entry))
                return false;

            // Must be within search radius of center
            float distanceFromCenter = (obj - searchCenter).Length;
            if (distanceFromCenter > searchRadius)
                return false;

            // Check if already claimed by another bot in same route
            if (claimRegistry != null && claimRegistry.IsClaimedByOther(obj.GUID, game.Player.GUID))
                return false;

            // Skip targets we've recently failed to path to
            if (unpathableTargets.Contains(obj.GUID))
                return false;

            // Check if mob is already in combat with a non-bot entity
            if (IsInCombatWithNonBot(obj, game))
                return false;

            return true;
        }

        /// <summary>
        /// Check if a mob is in combat with a non-bot entity (player or NPC)
        /// </summary>
        private bool IsInCombatWithNonBot(WorldObject obj, BotGame game)
        {
            // Read mob's target GUID from UNIT_FIELD_TARGET (64-bit field split into two 32-bit parts)
            uint lowTarget = obj[UnitField.UNIT_FIELD_TARGET];
            uint highTarget = obj[(int)UnitField.UNIT_FIELD_TARGET + 1];
            ulong mobTargetGuid = ((ulong)highTarget << 32) | lowTarget;

            if (mobTargetGuid == 0)
                return false;  // Not targeting anything

            // If target is us, that's fine
            if (mobTargetGuid == game.Player.GUID)
                return false;

            // Check if target is another bot - that's acceptable
            if (BotFactory.Instance.IsBot(mobTargetGuid))
                return false;

            // Mob is targeting a non-bot entity (real player or NPC)
            return true;
        }

        /// <summary>
        /// Check if bot is stuck (not making movement progress) and attempt to unstick
        /// </summary>
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
                    game.Log($"KillMobsTask: Possible stuck detected (moved {distanceMoved:F1}m in {StuckCheckIntervalSeconds}s, count: {stuckCounter})", LogLevel.Warning);

                    if (stuckCounter >= MaxStuckCountBeforeUnstuck)
                    {
                        TryUnstuck(game);
                        stuckCounter = 0;
                    }
                }
                else
                {
                    // Making progress, reset counter
                    stuckCounter = 0;
                }
            }

            lastStuckCheckPosition = currentPos;
        }

        /// <summary>
        /// Attempt to unstick the bot by abandoning current target and moving to a random nearby position
        /// </summary>
        private void TryUnstuck(BotGame game)
        {
            game.Log("KillMobsTask: Attempting to unstick by abandoning target", LogLevel.Warning);

            // Cancel current movement
            game.CancelActionsByFlag(ActionFlag.Movement);

            // Release current target claim
            if (claimRegistry != null && currentTarget != null)
            {
                claimRegistry.Release(currentTarget.GUID, game.Player.GUID);
            }

            // Mark current target as unpathable so we don't immediately re-target it
            if (currentTarget != null)
            {
                unpathableTargets.Add(currentTarget.GUID);
                currentTarget = null;
            }

            // Reset to searching state
            consecutivePathFailures = 0;
            movingToTarget = false;
            movingToCorpse = false;
            state = KillMobsState.Searching;
        }
        
        public override void Cleanup(AutomatedGame game)
        {
            // Release any claimed target when task ends
            if (claimRegistry != null && currentTarget != null && game is BotGame botGame)
            {
                claimRegistry.Release(currentTarget.GUID, botGame.Player.GUID);
            }

            // Release loot if we were in the middle of looting
            if (game.CurrentLoot.IsOpen)
            {
                game.ReleaseLoot();
            }

            if (combatAI != null && game is BotGame bg)
            {
                combatAI.OnCombatEnd(bg);
            }
            currentTarget = null;
            combatAI = null;
            corpseGuid = 0;
            corpsePosition = null;
        }

        /// <summary>
        /// Get mob entries needed for incomplete item collection requirements
        /// </summary>
        private HashSet<uint> GetMobsNeededForItems(AutomatedGame game)
        {
            var needed = new HashSet<uint>();

            // Check collectItems array
            if (collectItems != null)
            {
                foreach (var req in collectItems)
                {
                    if (GetItemCount(game, req.ItemEntry) < req.Count && req.DroppedBy != null)
                    {
                        foreach (var entry in req.DroppedBy)
                            needed.Add(entry);
                    }
                }
            }

            return needed;
        }

        /// <summary>
        /// Get mob entries needed for incomplete kill requirements
        /// </summary>
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

        /// <summary>
        /// Get priority for a target (lower = higher priority)
        /// </summary>
        private int GetTargetPriority(uint entry, HashSet<uint> neededForItems, HashSet<uint> neededForKills)
        {
            // Priority 0: needed for item drops (highest priority)
            if (neededForItems.Contains(entry))
                return 0;

            // Priority 1: needed for kill requirements
            if (neededForKills.Contains(entry))
                return 1;

            // Priority 2: any other valid target
            return 2;
        }

        /// <summary>
        /// Check if all kill requirements are met (per-entry tracking or total count)
        /// </summary>
        private bool AreAllKillRequirementsMet()
        {
            // If killRequirements specified, check each one
            if (killRequirements != null && killRequirements.Count > 0)
            {
                foreach (var req in killRequirements)
                {
                    int killed = killsByEntry.TryGetValue(req.Entry, out var count) ? count : 0;
                    if (killed < req.Count)
                        return false;
                }
                return true;
            }
            // Otherwise fall back to simple killCount
            return killCount <= 0 || killsCompleted >= killCount;
        }

        /// <summary>
        /// Check if all item collection requirements are met
        /// </summary>
        private bool AreAllItemsCollected(AutomatedGame game)
        {
            // If collectItems specified, check each one
            if (collectItems != null && collectItems.Count > 0)
            {
                foreach (var req in collectItems)
                {
                    if (GetItemCount(game, req.ItemEntry) < req.Count)
                        return false;
                }
                return true;
            }
            // Otherwise fall back to single item
            return collectItemEntry <= 0 || collectItemCount <= 0
                || GetItemCount(game, collectItemEntry) >= collectItemCount;
        }

        /// <summary>
        /// Check if at least one completion requirement is specified
        /// </summary>
        private bool HasAnyRequirements()
        {
            return killCount > 0
                || (killRequirements != null && killRequirements.Count > 0)
                || (collectItemEntry > 0 && collectItemCount > 0)
                || (collectItems != null && collectItems.Count > 0);
        }

        /// <summary>
        /// Log kill progress with per-entry tracking and item collection status
        /// </summary>
        private void LogKillProgress(BotGame game, uint killedEntry)
        {
            var parts = new List<string>();

            // Log per-entry kills if killRequirements specified
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
            else
            {
                parts.Add($"kills: {killsCompleted}");
            }

            // Log item collection progress
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

            game.Log($"KillMobsTask: Killed entry {killedEntry} - Progress: {string.Join(", ", parts)}", LogLevel.Info);
        }

        /// <summary>
        /// Log starting status showing all requirements
        /// </summary>
        private void LogStartStatus(AutomatedGame game, Client.World.Definitions.Class playerClass)
        {
            var parts = new List<string>();
            parts.Add($"Class: {playerClass}");
            parts.Add($"Targets: [{string.Join(",", targetEntries)}]");

            if (killRequirements != null && killRequirements.Count > 0)
            {
                var killParts = killRequirements.Select(r => $"entry {r.Entry}: 0/{r.Count}");
                parts.Add($"Kill requirements: {string.Join(", ", killParts)}");
            }
            else if (killCount > 0)
            {
                parts.Add($"Kill count: 0/{killCount}");
            }

            if (collectItems != null && collectItems.Count > 0)
            {
                var itemParts = collectItems.Select(r => $"item {r.ItemEntry}: {GetItemCount(game, r.ItemEntry)}/{r.Count}");
                parts.Add($"Item requirements: {string.Join(", ", itemParts)}");
            }
            else if (collectItemEntry > 0 && collectItemCount > 0)
            {
                int currentCount = GetItemCount(game, collectItemEntry);
                parts.Add($"Collect item {collectItemEntry}: {currentCount}/{collectItemCount}");
            }

            game.Log($"KillMobsTask: Starting. {string.Join(", ", parts)}", LogLevel.Info);
        }

        /// <summary>
        /// Log completion status showing all requirements that were met
        /// </summary>
        private void LogCompletionStatus(AutomatedGame game)
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

            game.Log($"KillMobsTask: All requirements met! {string.Join(", ", parts)}", LogLevel.Info);
        }

        /// <summary>
        /// Count total quantity of a specific item entry in player's inventory (backpack)
        /// </summary>
        private int GetItemCount(AutomatedGame game, uint itemEntry)
        {
            int totalCount = 0;
            int packSlotBase = (int)PlayerField.PLAYER_FIELD_PACK_SLOT_1;

            // Scan backpack (16 slots)
            for (int slot = 0; slot < 16; slot++)
            {
                // Read GUID from two consecutive fields (64-bit GUID)
                uint guidLow = game.Player[packSlotBase + slot * 2];
                uint guidHigh = game.Player[packSlotBase + slot * 2 + 1];
                ulong itemGuid = ((ulong)guidHigh << 32) | guidLow;

                if (itemGuid == 0)
                    continue;

                // Find the item object to get its entry and stack count
                if (game.Objects.TryGetValue(itemGuid, out var itemObject))
                {
                    if (itemObject.Entry == itemEntry)
                    {
                        // Get stack count (defaults to 1 if not set)
                        uint stackCount = itemObject[(int)ItemField.ITEM_FIELD_STACK_COUNT];
                        totalCount += (int)(stackCount > 0 ? stackCount : 1);
                    }
                }
            }

            return totalCount;
        }
    }
}
