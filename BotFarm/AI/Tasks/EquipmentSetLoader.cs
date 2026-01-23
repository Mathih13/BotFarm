using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Client.AI.Tasks;

namespace BotFarm.AI.Tasks
{
    /// <summary>
    /// JSON data structure for equipment set files
    /// </summary>
    public class EquipmentSetData
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("classRestriction")]
        public string ClassRestriction { get; set; }

        [JsonPropertyName("items")]
        public List<EquipmentSetItemData> Items { get; set; }
    }

    /// <summary>
    /// JSON data structure for equipment set items
    /// </summary>
    public class EquipmentSetItemData
    {
        [JsonPropertyName("entry")]
        public uint Entry { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; } = 1;
    }

    /// <summary>
    /// Loads equipment sets from JSON files in the routes/equipment-sets directory
    /// </summary>
    public static class EquipmentSetLoader
    {
        private static readonly string EquipmentSetsDirectory;
        private static readonly JsonSerializerOptions JsonOptions;

        static EquipmentSetLoader()
        {
            EquipmentSetsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "routes", "equipment-sets");
            JsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
        }

        /// <summary>
        /// Get the equipment sets directory path
        /// </summary>
        public static string GetEquipmentSetsDirectory() => EquipmentSetsDirectory;

        /// <summary>
        /// Sanitize a name to prevent path traversal attacks.
        /// Returns null if the name contains invalid characters.
        /// </summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            // Reject any path traversal attempts or invalid filename characters
            if (name.Contains("..") || name.Contains("/") || name.Contains("\\") ||
                name.Contains(":") || name.Contains("*") || name.Contains("?") ||
                name.Contains("\"") || name.Contains("<") || name.Contains(">") ||
                name.Contains("|"))
            {
                return null;
            }

            return name;
        }

        /// <summary>
        /// Verify that a file path is within the allowed directory.
        /// </summary>
        private static bool IsPathWithinDirectory(string filePath, string directory)
        {
            var fullPath = Path.GetFullPath(filePath);
            var fullDirectory = Path.GetFullPath(directory);
            return fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || fullPath.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Load an equipment set by name
        /// </summary>
        /// <param name="name">The name of the equipment set (without .json extension)</param>
        /// <returns>The loaded equipment set, or null if not found</returns>
        public static EquipmentSet LoadByName(string name)
        {
            var sanitizedName = SanitizeName(name);
            if (sanitizedName == null)
                return null;

            // Try to find a file matching the name
            var filePath = Path.Combine(EquipmentSetsDirectory, $"{sanitizedName}.json");

            // Verify path is within allowed directory
            if (!IsPathWithinDirectory(filePath, EquipmentSetsDirectory))
                return null;
            if (!File.Exists(filePath))
            {
                // Try searching recursively
                if (Directory.Exists(EquipmentSetsDirectory))
                {
                    var files = Directory.GetFiles(EquipmentSetsDirectory, "*.json", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var set = LoadFromFile(file);
                        if (set != null && string.Equals(set.Name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            return set;
                        }
                    }
                }
                return null;
            }

            return LoadFromFile(filePath);
        }

        /// <summary>
        /// Load an equipment set from a specific file path
        /// </summary>
        public static EquipmentSet LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = File.ReadAllText(filePath);
                var data = JsonSerializer.Deserialize<EquipmentSetData>(json, JsonOptions);
                if (data == null)
                    return null;

                return BuildEquipmentSet(data);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Get all equipment sets
        /// </summary>
        public static List<EquipmentSet> GetAllSets()
        {
            var sets = new List<EquipmentSet>();

            if (!Directory.Exists(EquipmentSetsDirectory))
                return sets;

            var files = Directory.GetFiles(EquipmentSetsDirectory, "*.json", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var set = LoadFromFile(file);
                if (set != null)
                {
                    sets.Add(set);
                }
            }

            return sets;
        }

        /// <summary>
        /// Get all equipment sets that can be used by a specific class
        /// </summary>
        /// <param name="className">The class name (e.g., "Warrior", "Priest")</param>
        /// <returns>Equipment sets with no class restriction or matching the specified class</returns>
        public static List<EquipmentSet> GetSetsForClass(string className)
        {
            return GetAllSets()
                .Where(s => string.IsNullOrEmpty(s.ClassRestriction) ||
                           string.Equals(s.ClassRestriction, className, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// Save an equipment set to file
        /// </summary>
        /// <param name="set">The equipment set to save</param>
        /// <param name="fileName">Optional file name (defaults to set name)</param>
        public static void SaveToFile(EquipmentSet set, string fileName = null)
        {
            if (set == null)
                throw new ArgumentNullException(nameof(set));

            fileName = fileName ?? set.Name;

            // Remove .json extension for sanitization check
            var nameToSanitize = fileName;
            if (nameToSanitize.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                nameToSanitize = nameToSanitize.Substring(0, nameToSanitize.Length - 5);

            var sanitizedName = SanitizeName(nameToSanitize);
            if (sanitizedName == null)
                throw new ArgumentException("Invalid file name: contains path traversal characters", nameof(fileName));

            if (!Directory.Exists(EquipmentSetsDirectory))
                Directory.CreateDirectory(EquipmentSetsDirectory);

            fileName = sanitizedName + ".json";
            var filePath = Path.Combine(EquipmentSetsDirectory, fileName);

            // Verify path is within allowed directory
            if (!IsPathWithinDirectory(filePath, EquipmentSetsDirectory))
                throw new ArgumentException("Invalid file name: path traversal detected", nameof(fileName));

            var data = new EquipmentSetData
            {
                Name = set.Name,
                Description = set.Description,
                ClassRestriction = set.ClassRestriction,
                Items = set.Items?.Select(i => new EquipmentSetItemData
                {
                    Entry = i.Entry,
                    Count = i.Count
                }).ToList() ?? new List<EquipmentSetItemData>()
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(data, options);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Delete an equipment set file by name
        /// </summary>
        public static bool DeleteByName(string name)
        {
            var sanitizedName = SanitizeName(name);
            if (sanitizedName == null)
                return false;

            var filePath = Path.Combine(EquipmentSetsDirectory, $"{sanitizedName}.json");

            // Verify path is within allowed directory
            if (!IsPathWithinDirectory(filePath, EquipmentSetsDirectory))
                return false;
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }

            // Try searching recursively
            if (Directory.Exists(EquipmentSetsDirectory))
            {
                var files = Directory.GetFiles(EquipmentSetsDirectory, "*.json", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var set = LoadFromFile(file);
                    if (set != null && string.Equals(set.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(file);
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Check if an equipment set exists by name
        /// </summary>
        public static bool Exists(string name)
        {
            return LoadByName(name) != null;
        }

        private static EquipmentSet BuildEquipmentSet(EquipmentSetData data)
        {
            var set = new EquipmentSet
            {
                Name = data.Name,
                Description = data.Description,
                ClassRestriction = data.ClassRestriction,
                Items = new List<EquipmentSetItem>()
            };

            if (data.Items != null)
            {
                foreach (var item in data.Items)
                {
                    set.Items.Add(new EquipmentSetItem
                    {
                        Entry = item.Entry,
                        Count = item.Count > 0 ? item.Count : 1
                    });
                }
            }

            return set;
        }
    }
}
