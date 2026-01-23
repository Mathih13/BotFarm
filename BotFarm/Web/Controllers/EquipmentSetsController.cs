using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BotFarm.AI.Tasks;
using BotFarm.Web.Models;
using BotFarm.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace BotFarm.Web.Controllers
{
    [ApiController]
    [Route("api/equipment-sets")]
    public class EquipmentSetsController : ControllerBase
    {
        private readonly BotFactory factory;
        private readonly WorldDatabaseService worldDb;
        private readonly string equipmentSetsDirectory;

        public EquipmentSetsController(BotFactory factory, WorldDatabaseService worldDb)
        {
            this.factory = factory;
            this.worldDb = worldDb;
            equipmentSetsDirectory = EquipmentSetLoader.GetEquipmentSetsDirectory();
        }

        /// <summary>
        /// GET /api/equipment-sets - List all equipment sets
        /// </summary>
        [HttpGet]
        public ActionResult<List<ApiEquipmentSetInfo>> GetAllSets()
        {
            var sets = EquipmentSetLoader.GetAllSets();
            return sets.Select(s => new ApiEquipmentSetInfo
            {
                Name = s.Name,
                Description = s.Description,
                ClassRestriction = s.ClassRestriction,
                ItemCount = s.Items?.Count ?? 0
            }).OrderBy(s => s.Name).ToList();
        }

        /// <summary>
        /// GET /api/equipment-sets/{name} - Get equipment set by name
        /// </summary>
        [HttpGet("{name}")]
        public ActionResult<ApiEquipmentSetDetail> GetByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(new { error = "name is required" });
            }

            // Validate name to prevent path traversal
            var sanitizedName = EquipmentSetLoader.SanitizeName(name);
            if (sanitizedName == null)
            {
                return BadRequest(new { error = "Invalid name: contains illegal characters" });
            }

            var set = EquipmentSetLoader.LoadByName(sanitizedName);
            if (set == null)
            {
                return NotFound(new { error = $"Equipment set '{name}' not found" });
            }

            // Get item names from database
            var itemEntries = set.Items?.Select(i => i.Entry).ToArray() ?? Array.Empty<uint>();
            var itemNames = worldDb.IsConnected
                ? worldDb.GetItemNames(itemEntries)
                : new Dictionary<uint, string>();

            // Build the raw JSON for editing
            var rawJsonPath = Path.Combine(equipmentSetsDirectory, $"{sanitizedName}.json");
            string rawJson = "";
            if (System.IO.File.Exists(rawJsonPath))
            {
                rawJson = System.IO.File.ReadAllText(rawJsonPath);
            }
            else
            {
                // Search for the file by set name
                if (Directory.Exists(equipmentSetsDirectory))
                {
                    var files = Directory.GetFiles(equipmentSetsDirectory, "*.json", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var loadedSet = EquipmentSetLoader.LoadFromFile(file);
                        if (loadedSet != null && string.Equals(loadedSet.Name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            rawJson = System.IO.File.ReadAllText(file);
                            break;
                        }
                    }
                }
            }

            return new ApiEquipmentSetDetail
            {
                Name = set.Name,
                Description = set.Description,
                ClassRestriction = set.ClassRestriction,
                Items = set.Items?.Select(i => new ApiEquipmentSetItem
                {
                    Entry = i.Entry,
                    Count = i.Count,
                    Name = itemNames.TryGetValue(i.Entry, out var n) ? n : null
                }).ToList() ?? new List<ApiEquipmentSetItem>(),
                RawJson = rawJson
            };
        }

        /// <summary>
        /// POST /api/equipment-sets - Create new equipment set
        /// </summary>
        [HttpPost]
        public ActionResult<ApiEquipmentSetDetail> CreateSet([FromBody] CreateEquipmentSetRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Name))
            {
                return BadRequest(new { error = "name is required" });
            }

            // Validate name to prevent path traversal
            var sanitizedName = EquipmentSetLoader.SanitizeName(request.Name);
            if (sanitizedName == null)
            {
                return BadRequest(new { error = "Invalid name: contains illegal characters" });
            }

            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return BadRequest(new { error = "content is required" });
            }

            // Check if set already exists
            if (EquipmentSetLoader.Exists(sanitizedName))
            {
                return Conflict(new { error = $"Equipment set '{sanitizedName}' already exists" });
            }

            try
            {
                // Validate JSON is parseable
                var testParse = JsonSerializer.Deserialize<EquipmentSetData>(request.Content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Ensure directory exists
                if (!Directory.Exists(equipmentSetsDirectory))
                {
                    Directory.CreateDirectory(equipmentSetsDirectory);
                }

                // Write file
                var filePath = Path.Combine(equipmentSetsDirectory, $"{sanitizedName}.json");
                System.IO.File.WriteAllText(filePath, request.Content);

                factory.Log($"Created equipment set: {sanitizedName}");

                // Return the created set
                return GetByName(sanitizedName);
            }
            catch (JsonException ex)
            {
                return BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
            }
            catch (Exception ex)
            {
                factory.Log($"Failed to create equipment set {request.Name}: {ex.Message}");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// PUT /api/equipment-sets/{name} - Update existing equipment set
        /// </summary>
        [HttpPut("{name}")]
        public ActionResult<ApiEquipmentSetDetail> UpdateSet(string name, [FromBody] UpdateEquipmentSetRequest request)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(new { error = "name is required" });
            }

            // Validate name to prevent path traversal
            var sanitizedName = EquipmentSetLoader.SanitizeName(name);
            if (sanitizedName == null)
            {
                return BadRequest(new { error = "Invalid name: contains illegal characters" });
            }

            if (string.IsNullOrWhiteSpace(request?.Content))
            {
                return BadRequest(new { error = "content is required" });
            }

            // Find the file
            var filePath = FindEquipmentSetFile(sanitizedName);
            if (filePath == null)
            {
                return NotFound(new { error = $"Equipment set '{sanitizedName}' not found" });
            }

            try
            {
                // Validate JSON is parseable
                var testParse = JsonSerializer.Deserialize<EquipmentSetData>(request.Content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Write file
                System.IO.File.WriteAllText(filePath, request.Content);

                factory.Log($"Updated equipment set: {sanitizedName}");

                // Get the new name from the content (in case it changed)
                var newName = testParse?.Name ?? sanitizedName;
                return GetByName(newName);
            }
            catch (JsonException ex)
            {
                return BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
            }
            catch (Exception ex)
            {
                factory.Log($"Failed to update equipment set {sanitizedName}: {ex.Message}");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// DELETE /api/equipment-sets/{name} - Delete equipment set
        /// </summary>
        [HttpDelete("{name}")]
        public ActionResult DeleteSet(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(new { error = "name is required" });
            }

            // Validate name to prevent path traversal
            var sanitizedName = EquipmentSetLoader.SanitizeName(name);
            if (sanitizedName == null)
            {
                return BadRequest(new { error = "Invalid name: contains illegal characters" });
            }

            var filePath = FindEquipmentSetFile(sanitizedName);
            if (filePath == null)
            {
                return NotFound(new { error = $"Equipment set '{sanitizedName}' not found" });
            }

            try
            {
                System.IO.File.Delete(filePath);
                factory.Log($"Deleted equipment set: {sanitizedName}");

                return Ok(new { message = $"Equipment set '{sanitizedName}' deleted" });
            }
            catch (Exception ex)
            {
                factory.Log($"Failed to delete equipment set {sanitizedName}: {ex.Message}");
                return BadRequest(new { error = ex.Message });
            }
        }

        private string FindEquipmentSetFile(string name)
        {
            // Sanitize the name (defense in depth - callers should also sanitize)
            var sanitizedName = EquipmentSetLoader.SanitizeName(name);
            if (sanitizedName == null)
                return null;

            // Try direct path first
            var filePath = Path.Combine(equipmentSetsDirectory, $"{sanitizedName}.json");
            if (System.IO.File.Exists(filePath))
            {
                return filePath;
            }

            // Search by set name
            if (Directory.Exists(equipmentSetsDirectory))
            {
                var files = Directory.GetFiles(equipmentSetsDirectory, "*.json", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var set = EquipmentSetLoader.LoadFromFile(file);
                    if (set != null && string.Equals(set.Name, sanitizedName, StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }
                }
            }

            return null;
        }
    }
}
