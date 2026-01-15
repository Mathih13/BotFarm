using System;
using System.Collections.Concurrent;
using System.Linq;

namespace BotFarm.AI
{
    /// <summary>
    /// Information about a claimed target
    /// </summary>
    public class ClaimInfo
    {
        public ulong TargetGuid { get; }
        public ulong ClaimerGuid { get; }
        public DateTime ClaimTime { get; }
        public DateTime LastUpdate { get; set; }

        public ClaimInfo(ulong targetGuid, ulong claimerGuid)
        {
            TargetGuid = targetGuid;
            ClaimerGuid = claimerGuid;
            ClaimTime = DateTime.UtcNow;
            LastUpdate = ClaimTime;
        }
    }

    /// <summary>
    /// Registry for tracking claimed targets to prevent multiple bots attacking the same mob.
    /// Thread-safe implementation using ConcurrentDictionary.
    /// </summary>
    public class ClaimedTargetRegistry
    {
        // Static registry of all claim registries, keyed by route file path
        private static readonly ConcurrentDictionary<string, ClaimedTargetRegistry> s_registries
            = new ConcurrentDictionary<string, ClaimedTargetRegistry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Get or create a registry for a specific route file
        /// </summary>
        public static ClaimedTargetRegistry GetForRoute(string routeFilePath)
        {
            return s_registries.GetOrAdd(routeFilePath, _ => new ClaimedTargetRegistry());
        }

        // Instance members
        private readonly ConcurrentDictionary<ulong, ClaimInfo> _claims
            = new ConcurrentDictionary<ulong, ClaimInfo>();

        private readonly TimeSpan _claimTimeout = TimeSpan.FromSeconds(30);
        private DateTime _lastCleanup = DateTime.UtcNow;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Try to claim a target for a bot
        /// </summary>
        /// <param name="targetGuid">GUID of the mob to claim</param>
        /// <param name="claimerGuid">GUID of the claiming bot's player</param>
        /// <returns>True if claim successful, false if already claimed by another</returns>
        public bool TryClaim(ulong targetGuid, ulong claimerGuid)
        {
            CleanupStaleClaimsIfNeeded();

            var newClaim = new ClaimInfo(targetGuid, claimerGuid);

            // Try to add new claim
            if (_claims.TryAdd(targetGuid, newClaim))
                return true;

            // Check if we already own the claim
            if (_claims.TryGetValue(targetGuid, out var existing))
            {
                if (existing.ClaimerGuid == claimerGuid)
                {
                    existing.LastUpdate = DateTime.UtcNow;
                    return true;
                }

                // Check if existing claim has timed out
                if (DateTime.UtcNow - existing.LastUpdate > _claimTimeout)
                {
                    // Try to replace stale claim
                    if (_claims.TryUpdate(targetGuid, newClaim, existing))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Release a claimed target
        /// </summary>
        public void Release(ulong targetGuid, ulong claimerGuid)
        {
            if (_claims.TryGetValue(targetGuid, out var claim))
            {
                // Only release if we own the claim
                if (claim.ClaimerGuid == claimerGuid)
                {
                    _claims.TryRemove(targetGuid, out _);
                }
            }
        }

        /// <summary>
        /// Release all claims for a specific bot
        /// </summary>
        public void ReleaseAll(ulong claimerGuid)
        {
            var toRemove = _claims
                .Where(kvp => kvp.Value.ClaimerGuid == claimerGuid)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var guid in toRemove)
            {
                _claims.TryRemove(guid, out _);
            }
        }

        /// <summary>
        /// Check if a target is claimed by another bot
        /// </summary>
        public bool IsClaimedByOther(ulong targetGuid, ulong myGuid)
        {
            if (_claims.TryGetValue(targetGuid, out var claim))
            {
                // Stale claim check
                if (DateTime.UtcNow - claim.LastUpdate > _claimTimeout)
                    return false;

                return claim.ClaimerGuid != myGuid;
            }
            return false;
        }

        /// <summary>
        /// Refresh the claim timestamp (call periodically during combat)
        /// </summary>
        public void RefreshClaim(ulong targetGuid, ulong claimerGuid)
        {
            if (_claims.TryGetValue(targetGuid, out var claim))
            {
                if (claim.ClaimerGuid == claimerGuid)
                {
                    claim.LastUpdate = DateTime.UtcNow;
                }
            }
        }

        private void CleanupStaleClaimsIfNeeded()
        {
            if (DateTime.UtcNow - _lastCleanup < _cleanupInterval)
                return;

            _lastCleanup = DateTime.UtcNow;

            var stale = _claims
                .Where(kvp => DateTime.UtcNow - kvp.Value.LastUpdate > _claimTimeout)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var guid in stale)
            {
                _claims.TryRemove(guid, out _);
            }
        }
    }
}
