using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Client.UI;

namespace BotFarm.Web.Services
{
    /// <summary>
    /// Thread-safe in-memory buffer for log entries with configurable capacity
    /// </summary>
    public class LogBuffer
    {
        private readonly ConcurrentQueue<LogEntry> logs = new();
        private readonly int maxEntries;
        private int totalCount = 0;

        public LogBuffer(int maxEntries = 10000)
        {
            this.maxEntries = maxEntries;
        }

        /// <summary>
        /// A single log entry with timestamp, message, and level
        /// </summary>
        public record LogEntry(DateTime Timestamp, string Message, LogLevel Level);

        /// <summary>
        /// Add a new log entry to the buffer
        /// </summary>
        public void Add(string message, LogLevel level)
        {
            var entry = new LogEntry(DateTime.UtcNow, message, level);
            logs.Enqueue(entry);
            System.Threading.Interlocked.Increment(ref totalCount);

            // Trim old entries if over capacity
            while (logs.Count > maxEntries)
            {
                logs.TryDequeue(out _);
            }
        }

        /// <summary>
        /// Get the most recent log entries
        /// </summary>
        /// <param name="count">Maximum number of entries to return</param>
        /// <param name="filter">Optional filter to match against message content (case-insensitive)</param>
        /// <param name="minLevel">Optional minimum log level to include</param>
        /// <returns>List of matching log entries, most recent first</returns>
        public IReadOnlyList<LogEntry> GetRecent(int count = 100, string filter = null, LogLevel? minLevel = null)
        {
            var entries = logs.ToArray();

            IEnumerable<LogEntry> result = entries.Reverse();

            if (minLevel.HasValue)
            {
                result = result.Where(e => e.Level >= minLevel.Value);
            }

            if (!string.IsNullOrEmpty(filter))
            {
                result = result.Where(e => e.Message.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            return result.Take(count).ToList();
        }

        /// <summary>
        /// Get log entries since a specific timestamp
        /// </summary>
        /// <param name="since">Return entries after this time</param>
        /// <param name="filter">Optional filter to match against message content (case-insensitive)</param>
        /// <param name="minLevel">Optional minimum log level to include</param>
        /// <returns>List of matching log entries, oldest first</returns>
        public IReadOnlyList<LogEntry> GetSince(DateTime since, string filter = null, LogLevel? minLevel = null)
        {
            var entries = logs.ToArray();

            IEnumerable<LogEntry> result = entries.Where(e => e.Timestamp > since);

            if (minLevel.HasValue)
            {
                result = result.Where(e => e.Level >= minLevel.Value);
            }

            if (!string.IsNullOrEmpty(filter))
            {
                result = result.Where(e => e.Message.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            return result.ToList();
        }

        /// <summary>
        /// Get count of entries matching the filter
        /// </summary>
        public int GetFilteredCount(string filter, LogLevel? minLevel = null)
        {
            IEnumerable<LogEntry> result = logs;

            if (minLevel.HasValue)
            {
                result = result.Where(e => e.Level >= minLevel.Value);
            }

            if (!string.IsNullOrEmpty(filter))
            {
                result = result.Where(e => e.Message.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            return result.Count();
        }

        /// <summary>
        /// Total number of log entries ever added (may exceed maxEntries due to trimming)
        /// </summary>
        public int TotalCount => totalCount;

        /// <summary>
        /// Current number of entries in the buffer
        /// </summary>
        public int Count => logs.Count;
    }
}
