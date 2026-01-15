using System.Collections.Generic;
using Client.AI.Tasks;

namespace BotFarm.Services
{
    /// <summary>
    /// Service interface for route discovery and execution.
    /// Internal for now - will be made public when BotGame is made public.
    /// </summary>
    internal interface IRouteService
    {
        // Route discovery

        /// <summary>
        /// Gets all available routes from the specified directory (recursively).
        /// Returns metadata about each route without loading full task definitions.
        /// </summary>
        IReadOnlyList<RouteInfo> GetAllRoutes(string directory = "routes");

        /// <summary>
        /// Loads a route from the specified path.
        /// Returns the full TaskRoute with all task definitions.
        /// </summary>
        TaskRoute LoadRoute(string path);

        // Route execution (per bot)

        /// <summary>
        /// Loads and starts a route for the specified bot.
        /// Returns true if the route was loaded and started successfully.
        /// </summary>
        bool StartRoute(BotGame bot, string routePath);

        /// <summary>
        /// Starts an already-loaded route for the specified bot.
        /// Returns true if the route was started successfully.
        /// </summary>
        bool StartRoute(BotGame bot, TaskRoute route);

        /// <summary>
        /// Stops the currently running route for the specified bot.
        /// </summary>
        void StopRoute(BotGame bot);

        /// <summary>
        /// Pauses the currently running route for the specified bot.
        /// </summary>
        void PauseRoute(BotGame bot);

        /// <summary>
        /// Resumes a paused route for the specified bot.
        /// </summary>
        void ResumeRoute(BotGame bot);

        /// <summary>
        /// Gets the current route status for the specified bot.
        /// Returns a human-readable status string.
        /// </summary>
        string GetRouteStatus(BotGame bot);
    }

    /// <summary>
    /// Lightweight route metadata for discovery/listing purposes.
    /// Does not include full task definitions.
    /// </summary>
    public class RouteInfo
    {
        /// <summary>
        /// Full path to the route JSON file.
        /// </summary>
        public string Path { get; init; }

        /// <summary>
        /// Route name from the JSON file.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Route description from the JSON file.
        /// </summary>
        public string Description { get; init; }

        /// <summary>
        /// Whether this route has a harness section (can be used as a test).
        /// </summary>
        public bool HasHarness { get; init; }

        /// <summary>
        /// Number of tasks in the route.
        /// </summary>
        public int TaskCount { get; init; }
    }
}
