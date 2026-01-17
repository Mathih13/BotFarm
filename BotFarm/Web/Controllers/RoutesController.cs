using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BotFarm.AI.Tasks;
using BotFarm.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace BotFarm.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RoutesController : ControllerBase
    {
        private readonly BotFactory factory;
        private readonly string routesDirectory;

        public RoutesController(BotFactory factory)
        {
            this.factory = factory;
            // Routes are in the 'routes' subdirectory relative to the executable
            routesDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "routes");
        }

        /// <summary>
        /// GET /api/routes - List all available route files
        /// </summary>
        [HttpGet]
        public ActionResult<List<ApiRouteInfo>> GetRoutes()
        {
            if (!Directory.Exists(routesDirectory))
            {
                return new List<ApiRouteInfo>();
            }

            var routes = new List<ApiRouteInfo>();

            foreach (var file in Directory.GetFiles(routesDirectory, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var relativePath = Path.GetRelativePath(routesDirectory, file);
                    var route = LoadRouteInfo(file, relativePath);
                    if (route != null)
                    {
                        routes.Add(route);
                    }
                }
                catch (Exception ex)
                {
                    factory.Log($"Failed to load route info from {file}: {ex.Message}");
                }
            }

            return routes.OrderBy(r => r.Path).ToList();
        }

        /// <summary>
        /// GET /api/routes/suites - List all suite definition files
        /// </summary>
        [HttpGet("suites")]
        public ActionResult<List<ApiSuiteInfo>> GetSuites()
        {
            // Look for suite files in routes/suites directory
            var suitesDirectory = Path.Combine(routesDirectory, "suites");
            if (!Directory.Exists(suitesDirectory))
            {
                return new List<ApiSuiteInfo>();
            }

            var suites = new List<ApiSuiteInfo>();

            foreach (var file in Directory.GetFiles(suitesDirectory, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var relativePath = Path.GetRelativePath(routesDirectory, file);
                    var json = System.IO.File.ReadAllText(file);
                    var data = JsonSerializer.Deserialize<SuiteFileData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    suites.Add(new ApiSuiteInfo
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
        /// POST /api/routes - Create new route file
        /// </summary>
        [HttpPost]
        public ActionResult<ApiRouteDetail> CreateRoute([FromBody] CreateRouteRequest request)
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
            var fullPath = Path.Combine(routesDirectory, path);

            // Security: ensure path is within routes directory
            var normalizedPath = Path.GetFullPath(fullPath);
            var normalizedRoutesDir = Path.GetFullPath(routesDirectory);
            if (!normalizedPath.StartsWith(normalizedRoutesDir))
            {
                return BadRequest(new { error = "Invalid path" });
            }

            if (System.IO.File.Exists(fullPath))
            {
                return Conflict(new { error = $"Route file '{path}' already exists" });
            }

            try
            {
                // Validate JSON is parseable
                var testParse = JsonSerializer.Deserialize<RouteFileData>(request.Content, new JsonSerializerOptions
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

                factory.Log($"Created route file: {path}");

                // Return the created route detail
                return GetRoute(path.Replace('\\', '/'));
            }
            catch (JsonException ex)
            {
                return BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
            }
            catch (Exception ex)
            {
                factory.Log($"Failed to create route file {path}: {ex.Message}");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// PUT /api/routes/{*path} - Update existing route file
        /// </summary>
        [HttpPut("{**path}")]
        public ActionResult<ApiRouteDetail> UpdateRoute(string path, [FromBody] UpdateRouteRequest request)
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
            var fullPath = Path.Combine(routesDirectory, path);

            // Security: ensure path is within routes directory
            var normalizedPath = Path.GetFullPath(fullPath);
            var normalizedRoutesDir = Path.GetFullPath(routesDirectory);
            if (!normalizedPath.StartsWith(normalizedRoutesDir))
            {
                return BadRequest(new { error = "Invalid path" });
            }

            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(new { error = $"Route file '{path}' not found" });
            }

            try
            {
                // Validate JSON is parseable
                var testParse = JsonSerializer.Deserialize<RouteFileData>(request.Content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Write file
                System.IO.File.WriteAllText(fullPath, request.Content);

                factory.Log($"Updated route file: {path}");

                // Return the updated route detail
                return GetRoute(path.Replace('\\', '/'));
            }
            catch (JsonException ex)
            {
                return BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
            }
            catch (Exception ex)
            {
                factory.Log($"Failed to update route file {path}: {ex.Message}");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// DELETE /api/routes/{*path} - Delete route file
        /// </summary>
        [HttpDelete("{**path}")]
        public ActionResult DeleteRoute(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return BadRequest(new { error = "path is required" });
            }

            // Normalize path separators
            path = path.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(routesDirectory, path);

            // Security: ensure path is within routes directory
            var normalizedPath = Path.GetFullPath(fullPath);
            var normalizedRoutesDir = Path.GetFullPath(routesDirectory);
            if (!normalizedPath.StartsWith(normalizedRoutesDir))
            {
                return BadRequest(new { error = "Invalid path" });
            }

            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(new { error = $"Route file '{path}' not found" });
            }

            try
            {
                System.IO.File.Delete(fullPath);
                factory.Log($"Deleted route file: {path}");

                return Ok(new { message = $"Route file '{path}' deleted" });
            }
            catch (Exception ex)
            {
                factory.Log($"Failed to delete route file {path}: {ex.Message}");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// GET /api/routes/{*path} - Get route file contents
        /// </summary>
        [HttpGet("{**path}")]
        public ActionResult<ApiRouteDetail> GetRoute(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return BadRequest(new { error = "path is required" });
            }

            // Normalize path separators
            path = path.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(routesDirectory, path);

            // Security: ensure path is within routes directory
            var normalizedPath = Path.GetFullPath(fullPath);
            var normalizedRoutesDir = Path.GetFullPath(routesDirectory);
            if (!normalizedPath.StartsWith(normalizedRoutesDir))
            {
                return BadRequest(new { error = "Invalid path" });
            }

            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(new { error = $"Route file '{path}' not found" });
            }

            try
            {
                var json = System.IO.File.ReadAllText(fullPath);
                var route = TaskRouteLoader.LoadFromJson(fullPath);

                if (route == null)
                {
                    return BadRequest(new { error = "Failed to parse route file" });
                }

                var detail = new ApiRouteDetail
                {
                    Path = path.Replace('\\', '/'),
                    Name = route.Name,
                    Description = route.Description,
                    Loop = route.Loop,
                    RawJson = json,
                    Tasks = route.Tasks?.Select(t => new ApiTaskInfo
                    {
                        Type = t.GetType().Name,
                        Parameters = new { name = t.Name }
                    }).ToList() ?? new List<ApiTaskInfo>()
                };

                if (route.HasHarness && route.Harness != null)
                {
                    detail.Harness = new ApiHarnessSettings
                    {
                        BotCount = route.Harness.BotCount,
                        AccountPrefix = route.Harness.AccountPrefix,
                        Classes = route.Harness.Classes,
                        Race = route.Harness.Race,
                        Level = route.Harness.Level,
                        SetupTimeoutSeconds = route.Harness.SetupTimeoutSeconds,
                        TestTimeoutSeconds = route.Harness.TestTimeoutSeconds,
                        StartPosition = route.Harness.StartPosition != null ? new ApiStartPosition
                        {
                            MapId = route.Harness.StartPosition.MapId,
                            X = route.Harness.StartPosition.X,
                            Y = route.Harness.StartPosition.Y,
                            Z = route.Harness.StartPosition.Z
                        } : null
                    };
                }

                return detail;
            }
            catch (Exception ex)
            {
                factory.Log($"Failed to load route detail from {path}: {ex.Message}");
                return BadRequest(new { error = ex.Message });
            }
        }

        private ApiRouteInfo LoadRouteInfo(string fullPath, string relativePath)
        {
            var json = System.IO.File.ReadAllText(fullPath);
            var data = JsonSerializer.Deserialize<RouteFileData>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Skip suite files (they have "tests" array instead of "tasks")
            if (data?.Tests != null)
            {
                return null;
            }

            return new ApiRouteInfo
            {
                Path = relativePath.Replace('\\', '/'),
                Name = data?.Name ?? Path.GetFileNameWithoutExtension(fullPath),
                Directory = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "",
                HasHarness = data?.Harness != null,
                BotCount = data?.Harness?.BotCount,
                Level = data?.Harness?.Level,
                TimeoutSeconds = data?.Harness?.TestTimeoutSeconds
            };
        }

        // Simple classes for JSON deserialization
        private class RouteFileData
        {
            public string Name { get; set; }
            public HarnessData Harness { get; set; }
            public List<object> Tasks { get; set; }
            public List<object> Tests { get; set; } // For detecting suite files
        }

        private class HarnessData
        {
            public int BotCount { get; set; }
            public int Level { get; set; }
            public int TestTimeoutSeconds { get; set; }
        }

        private class SuiteFileData
        {
            public string Name { get; set; }
            public List<object> Tests { get; set; }
        }
    }
}
