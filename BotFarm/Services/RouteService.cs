using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BotFarm.AI.Tasks;
using Client.AI.Tasks;

namespace BotFarm.Services
{
    /// <summary>
    /// Service implementation for route discovery and execution.
    /// </summary>
    internal class RouteService : IRouteService
    {
        private readonly BotFactory factory;

        public RouteService(BotFactory factory)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        // ========== Route Discovery ==========

        public IReadOnlyList<RouteInfo> GetAllRoutes(string directory = "routes")
        {
            var routes = new List<RouteInfo>();

            if (!Directory.Exists(directory))
            {
                factory.Log($"Routes directory not found: {directory}");
                return routes;
            }

            // Recursively find all JSON files
            var jsonFiles = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories);

            foreach (var file in jsonFiles)
            {
                try
                {
                    var info = GetRouteInfo(file);
                    if (info != null)
                    {
                        routes.Add(info);
                    }
                }
                catch (Exception ex)
                {
                    factory.Log($"Error reading route {file}: {ex.Message}");
                }
            }

            return routes;
        }

        public TaskRoute LoadRoute(string path)
        {
            return TaskRouteLoader.LoadFromJson(path);
        }

        // ========== Route Execution ==========

        public bool StartRoute(BotGame bot, string routePath)
        {
            if (bot == null) return false;
            return bot.LoadAndStartRoute(routePath);
        }

        public bool StartRoute(BotGame bot, TaskRoute route)
        {
            if (bot == null || route == null) return false;
            return bot.StartRoute(route);
        }

        public void StopRoute(BotGame bot)
        {
            bot?.StopRoute();
        }

        public void PauseRoute(BotGame bot)
        {
            bot?.PauseRoute();
        }

        public void ResumeRoute(BotGame bot)
        {
            bot?.ResumeRoute();
        }

        public string GetRouteStatus(BotGame bot)
        {
            return bot?.GetRouteStatus() ?? "No bot";
        }

        // ========== Private Helpers ==========

        /// <summary>
        /// Read route metadata without fully parsing the route.
        /// Uses lightweight JSON parsing for performance.
        /// </summary>
        private RouteInfo GetRouteInfo(string path)
        {
            string json = File.ReadAllText(path);

            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip
            });

            var root = doc.RootElement;

            // Skip files that don't look like routes (e.g., suite files)
            if (!root.TryGetProperty("tasks", out var tasksElement))
            {
                return null;
            }

            string name = null;
            string description = null;
            bool hasHarness = false;
            int taskCount = 0;

            if (root.TryGetProperty("name", out var nameElement))
            {
                name = nameElement.GetString();
            }

            if (root.TryGetProperty("description", out var descElement))
            {
                description = descElement.GetString();
            }

            if (root.TryGetProperty("harness", out _))
            {
                hasHarness = true;
            }

            if (tasksElement.ValueKind == JsonValueKind.Array)
            {
                taskCount = tasksElement.GetArrayLength();
            }

            return new RouteInfo
            {
                Path = Path.GetFullPath(path),
                Name = name ?? Path.GetFileNameWithoutExtension(path),
                Description = description ?? "",
                HasHarness = hasHarness,
                TaskCount = taskCount
            };
        }
    }
}
