using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BotFarm.Web.Services
{
    /// <summary>
    /// Fetches item icons from Wowhead's tooltip API and provides CDN URLs.
    /// Icons are cached in memory since they never change.
    /// </summary>
    public class ItemIconService
    {
        private readonly ConcurrentDictionary<uint, string> iconCache = new();
        private readonly HttpClient httpClient;

        private const string WowheadTooltipUrl = "https://nether.wowhead.com/tooltip/item/{0}?dataEnv=1&locale=0";
        private const string IconCdnUrl = "https://wow.zamimg.com/images/wow/icons/{0}/{1}.jpg";

        public ItemIconService()
        {
            httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "BotFarm/1.0");
        }

        /// <summary>
        /// Get the icon name for a single item entry
        /// </summary>
        public async Task<string> GetIconName(uint itemEntry)
        {
            if (iconCache.TryGetValue(itemEntry, out var cached))
            {
                return cached;
            }

            try
            {
                var url = string.Format(WowheadTooltipUrl, itemEntry);
                var response = await httpClient.GetStringAsync(url);

                var iconName = ParseIconFromTooltip(response);
                if (!string.IsNullOrEmpty(iconName))
                {
                    iconCache[itemEntry] = iconName;
                    return iconName;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ItemIconService] Failed to fetch icon for item {itemEntry}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Get icon names for multiple item entries (batched)
        /// </summary>
        public async Task<Dictionary<uint, string>> GetIconNames(uint[] entries)
        {
            var result = new Dictionary<uint, string>();
            var toFetch = new List<uint>();

            // Check cache first
            foreach (var entry in entries.Distinct())
            {
                if (iconCache.TryGetValue(entry, out var cached))
                {
                    result[entry] = cached;
                }
                else
                {
                    toFetch.Add(entry);
                }
            }

            // Fetch missing icons in parallel (with rate limiting)
            if (toFetch.Count > 0)
            {
                // Batch requests to avoid hammering Wowhead
                var batchSize = 5;
                for (int i = 0; i < toFetch.Count; i += batchSize)
                {
                    var batch = toFetch.Skip(i).Take(batchSize);
                    var tasks = batch.Select(async entry =>
                    {
                        var iconName = await GetIconName(entry);
                        return (entry, iconName);
                    });

                    var batchResults = await Task.WhenAll(tasks);
                    foreach (var (entry, iconName) in batchResults)
                    {
                        if (!string.IsNullOrEmpty(iconName))
                        {
                            result[entry] = iconName;
                        }
                    }

                    // Small delay between batches to be respectful
                    if (i + batchSize < toFetch.Count)
                    {
                        await Task.Delay(100);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get the full CDN URL for an icon
        /// </summary>
        public string GetIconUrl(string iconName, string size = "medium")
        {
            if (string.IsNullOrEmpty(iconName))
            {
                return null;
            }

            // Valid sizes: tiny (15x15), small (18x18), medium (36x36), large (56x56)
            return string.Format(IconCdnUrl, size, iconName.ToLowerInvariant());
        }

        /// <summary>
        /// Parse the icon name from Wowhead's tooltip JSON response
        /// </summary>
        private string ParseIconFromTooltip(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("icon", out var iconElement))
                {
                    return iconElement.GetString();
                }
            }
            catch (JsonException)
            {
                // JSON parsing failed
            }

            return null;
        }
    }
}
