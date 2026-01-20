using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BotFarm.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace BotFarm.Web.Controllers
{
    [ApiController]
    [Route("api/suites/definitions")]
    public class SuiteDefinitionsController : ControllerBase
    {
        private readonly BotFactory factory;
        private readonly string routesDirectory;
        private readonly string suitesDirectory;

        public SuiteDefinitionsController(BotFactory factory)
        {
            this.factory = factory;
            routesDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "routes");
            suitesDirectory = Path.Combine(routesDirectory, "suites");
        }

        /// <summary>
        /// GET /api/suites/definitions - List all suite definition files
        /// </summary>
        [HttpGet]
        public ActionResult<List<ApiSuiteDefinitionInfo>> GetAllDefinitions()
        {
            if (!Directory.Exists(suitesDirectory))
            {
                return new List<ApiSuiteDefinitionInfo>();
            }

            var suites = new List<ApiSuiteDefinitionInfo>();

            foreach (var file in Directory.GetFiles(suitesDirectory, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var relativePath = Path.GetRelativePath(suitesDirectory, file);
                    var json = System.IO.File.ReadAllText(file);
                    var data = JsonSerializer.Deserialize<SuiteFileData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    suites.Add(new ApiSuiteDefinitionInfo
                    {
                        Path = relativePath.Replace('\\', '/'),
                        Name = data?.Name ?? Path.GetFileNameWithoutExtension(file),
                        TestCount = data?.Tests?.Count ?? 0
                    });
                }
                catch (Exception ex)
                {
                    factory.Log($"Failed to load suite info from {file}: {ex.Message}");
                }
            }

            return suites.OrderBy(s => s.Path).ToList();
        }

        /// <summary>
        /// GET /api/suites/definitions/{*path} - Get suite definition by path
        /// </summary>
        [HttpGet("{**path}")]
        public ActionResult<ApiSuiteDefinitionDetail> GetDefinition(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return BadRequest(new { error = "path is required" });
            }

            // Normalize path separators
            path = path.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(suitesDirectory, path);

            // Security: ensure path is within suites directory
            var normalizedPath = Path.GetFullPath(fullPath);
            var normalizedSuitesDir = Path.GetFullPath(suitesDirectory);
            if (!normalizedPath.StartsWith(normalizedSuitesDir))
            {
                return BadRequest(new { error = "Invalid path" });
            }

            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(new { error = $"Suite file '{path}' not found" });
            }

            try
            {
                var json = System.IO.File.ReadAllText(fullPath);
                var data = JsonSerializer.Deserialize<SuiteFileData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var detail = new ApiSuiteDefinitionDetail
                {
                    Path = path.Replace('\\', '/'),
                    Name = data?.Name ?? Path.GetFileNameWithoutExtension(fullPath),
                    RawJson = json,
                    Tests = data?.Tests?.Select(t => new ApiSuiteTestEntry
                    {
                        Route = t.Route,
                        DependsOn = t.DependsOn ?? new List<string>()
                    }).ToList() ?? new List<ApiSuiteTestEntry>()
                };

                return detail;
            }
            catch (Exception ex)
            {
                factory.Log($"Failed to load suite detail from {path}: {ex.Message}");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// POST /api/suites/definitions - Create new suite definition
        /// </summary>
        [HttpPost]
        public ActionResult<ApiSuiteDefinitionDetail> CreateDefinition([FromBody] CreateSuiteDefinitionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Path))
            {
                return BadRequest(new { error = "path is required" });
            }

            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return BadRequest(new { error = "content is required" });
            }

            // Ensure path ends with .json
            var path = request.Path;
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                path += ".json";
            }

            // Normalize path separators
            path = path.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(suitesDirectory, path);

            // Security: ensure path is within suites directory
            var normalizedPath = Path.GetFullPath(fullPath);
            var normalizedSuitesDir = Path.GetFullPath(suitesDirectory);
            if (!normalizedPath.StartsWith(normalizedSuitesDir))
            {
                return BadRequest(new { error = "Invalid path" });
            }

            if (System.IO.File.Exists(fullPath))
            {
                return Conflict(new { error = $"Suite file '{path}' already exists" });
            }

            try
            {
                // Validate JSON is parseable as a suite
                var testParse = JsonSerializer.Deserialize<SuiteFileData>(request.Content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Create directory if needed
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Write file
                System.IO.File.WriteAllText(fullPath, request.Content);

                factory.Log($"Created suite file: {path}");

                // Return the created suite detail
                return GetDefinition(path.Replace('\\', '/'));
            }
            catch (JsonException ex)
            {
                return BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
            }
            catch (Exception ex)
            {
                factory.Log($"Failed to create suite file {path}: {ex.Message}");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// PUT /api/suites/definitions/{*path} - Update existing suite definition
        /// </summary>
        [HttpPut("{**path}")]
        public ActionResult<ApiSuiteDefinitionDetail> UpdateDefinition(string path, [FromBody] UpdateSuiteDefinitionRequest request)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return BadRequest(new { error = "path is required" });
            }

            if (string.IsNullOrWhiteSpace(request?.Content))
            {
                return BadRequest(new { error = "content is required" });
            }

            // Normalize path separators
            path = path.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(suitesDirectory, path);

            // Security: ensure path is within suites directory
            var normalizedPath = Path.GetFullPath(fullPath);
            var normalizedSuitesDir = Path.GetFullPath(suitesDirectory);
            if (!normalizedPath.StartsWith(normalizedSuitesDir))
            {
                return BadRequest(new { error = "Invalid path" });
            }

            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(new { error = $"Suite file '{path}' not found" });
            }

            try
            {
                // Validate JSON is parseable as a suite
                var testParse = JsonSerializer.Deserialize<SuiteFileData>(request.Content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Write file
                System.IO.File.WriteAllText(fullPath, request.Content);

                factory.Log($"Updated suite file: {path}");

                // Return the updated suite detail
                return GetDefinition(path.Replace('\\', '/'));
            }
            catch (JsonException ex)
            {
                return BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
            }
            catch (Exception ex)
            {
                factory.Log($"Failed to update suite file {path}: {ex.Message}");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// DELETE /api/suites/definitions/{*path} - Delete suite definition
        /// </summary>
        [HttpDelete("{**path}")]
        public ActionResult DeleteDefinition(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return BadRequest(new { error = "path is required" });
            }

            // Normalize path separators
            path = path.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(suitesDirectory, path);

            // Security: ensure path is within suites directory
            var normalizedPath = Path.GetFullPath(fullPath);
            var normalizedSuitesDir = Path.GetFullPath(suitesDirectory);
            if (!normalizedPath.StartsWith(normalizedSuitesDir))
            {
                return BadRequest(new { error = "Invalid path" });
            }

            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(new { error = $"Suite file '{path}' not found" });
            }

            try
            {
                System.IO.File.Delete(fullPath);
                factory.Log($"Deleted suite file: {path}");

                return Ok(new { message = $"Suite file '{path}' deleted" });
            }
            catch (Exception ex)
            {
                factory.Log($"Failed to delete suite file {path}: {ex.Message}");
                return BadRequest(new { error = ex.Message });
            }
        }

        // Helper classes for JSON deserialization
        private class SuiteFileData
        {
            public string Name { get; set; }
            public List<SuiteTestData> Tests { get; set; }
        }

        private class SuiteTestData
        {
            public string Route { get; set; }
            public List<string> DependsOn { get; set; }
        }
    }
}
