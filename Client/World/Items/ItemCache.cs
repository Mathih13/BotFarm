using System.Collections.Concurrent;
using System.Collections.Generic;
using Client.World.Definitions;

namespace Client.World.Items
{
    /// <summary>
    /// Thread-safe cache for item templates queried from the server.
    /// Shared across all bot instances to avoid redundant queries.
    /// </summary>
    public static class ItemCache
    {
        private static readonly ConcurrentDictionary<uint, ItemTemplate> _templates = new ConcurrentDictionary<uint, ItemTemplate>();
        private static readonly HashSet<uint> _pendingQueries = new HashSet<uint>();
        private static readonly object _pendingLock = new object();

        /// <summary>
        /// Get an item template from the cache
        /// </summary>
        /// <param name="entry">Item entry ID</param>
        /// <returns>ItemTemplate if cached, null otherwise</returns>
        public static ItemTemplate Get(uint entry)
        {
            _templates.TryGetValue(entry, out var template);
            return template;
        }

        /// <summary>
        /// Add an item template to the cache
        /// </summary>
        public static void Add(ItemTemplate template)
        {
            if (template == null) return;
            _templates[template.Entry] = template;
            ClearPending(template.Entry);
        }

        /// <summary>
        /// Check if an item template exists in the cache
        /// </summary>
        public static bool Contains(uint entry)
        {
            return _templates.ContainsKey(entry);
        }

        /// <summary>
        /// Check if a query is pending for this item
        /// </summary>
        public static bool IsPending(uint entry)
        {
            lock (_pendingLock)
            {
                return _pendingQueries.Contains(entry);
            }
        }

        /// <summary>
        /// Mark an item as having a pending query
        /// </summary>
        public static void MarkPending(uint entry)
        {
            lock (_pendingLock)
            {
                _pendingQueries.Add(entry);
            }
        }

        /// <summary>
        /// Clear the pending flag for an item
        /// </summary>
        public static void ClearPending(uint entry)
        {
            lock (_pendingLock)
            {
                _pendingQueries.Remove(entry);
            }
        }

        /// <summary>
        /// Get all cached templates (for debugging)
        /// </summary>
        public static ICollection<ItemTemplate> GetAll()
        {
            return _templates.Values;
        }

        /// <summary>
        /// Get the number of cached templates
        /// </summary>
        public static int Count => _templates.Count;

        /// <summary>
        /// Clear the entire cache (for testing/reset)
        /// </summary>
        public static void Clear()
        {
            _templates.Clear();
            lock (_pendingLock)
            {
                _pendingQueries.Clear();
            }
        }
    }
}
