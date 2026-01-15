using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BotFarm.Services;
using BotFarm.Testing;
using Client.UI;

namespace BotFarm
{
    /// <summary>
    /// Console command host that uses the service layer.
    /// Separates console I/O from business logic.
    /// </summary>
    internal class ConsoleHost
    {
        private readonly ServiceContainer services;
        private readonly BotFactory factory;
        private volatile bool running = true;

        public ConsoleHost(ServiceContainer services, BotFactory factory)
        {
            this.services = services ?? throw new ArgumentNullException(nameof(services));
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <summary>
        /// Run the console command loop.
        /// Returns when user requests exit.
        /// </summary>
        public void Run()
        {
            while (running)
            {
                string line = Console.ReadLine();
                if (line == null)
                    return;

                string[] args = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (args.Length == 0)
                    continue;

                try
                {
                    HandleCommand(args);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Signal the host to stop.
        /// </summary>
        public void Stop()
        {
            running = false;
        }

        private void HandleCommand(string[] args)
        {
            switch (args[0].ToLowerInvariant())
            {
                case "quit":
                case "exit":
                case "close":
                case "shutdown":
                    running = false;
                    break;

                case "info":
                case "infos":
                case "stats":
                case "statistics":
                    DisplayStatistics(args.Length > 1 ? args[1] : null);
                    break;

                case "route":
                    HandleRouteCommand(args);
                    break;

                case "test":
                    HandleTestCommand(args);
                    break;

                case "routes":
                    HandleRoutesCommand(args);
                    break;

                case "help":
                    DisplayHelp();
                    break;

                default:
                    Console.WriteLine($"Unknown command: {args[0]}. Type 'help' for available commands.");
                    break;
            }
        }

        private void HandleRoutesCommand(string[] args)
        {
            string directory = args.Length > 1 ? args[1] : "routes";

            Console.WriteLine($"Scanning routes in '{directory}'...");
            var routes = services.Routes.GetAllRoutes(directory);

            if (routes.Count == 0)
            {
                Console.WriteLine("No routes found.");
                return;
            }

            Console.WriteLine($"Found {routes.Count} route(s):");
            Console.WriteLine();

            foreach (var route in routes)
            {
                string testMarker = route.HasHarness ? " [TEST]" : "";
                Console.WriteLine($"  {route.Name}{testMarker}");
                if (!string.IsNullOrEmpty(route.Description))
                {
                    Console.WriteLine($"    {route.Description}");
                }
                Console.WriteLine($"    Path: {route.Path}");
                Console.WriteLine($"    Tasks: {route.TaskCount}");
                Console.WriteLine();
            }
        }

        private void HandleRouteCommand(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: route <command> [bot] [args...]");
                Console.WriteLine("Commands:");
                Console.WriteLine("  route start <bot> <routefile> - Start a route for a specific bot");
                Console.WriteLine("  route stop <bot>              - Stop current route for a bot");
                Console.WriteLine("  route status [bot]            - Show route status (all bots or specific)");
                Console.WriteLine("  route startall <routefile>    - Start route for all bots");
                Console.WriteLine("  route stopall                 - Stop routes for all bots");
                return;
            }

            string command = args[1].ToLowerInvariant();

            switch (command)
            {
                case "start":
                    if (args.Length < 4)
                    {
                        Console.WriteLine("Usage: route start <bot> <routefile>");
                        return;
                    }
                    var botToStart = services.Bots.GetBot(args[2]);
                    if (botToStart != null)
                    {
                        string routePath = ResolvePath(args[3], "routes");
                        if (services.Routes.StartRoute(botToStart, routePath))
                            Console.WriteLine($"Started route for bot {botToStart.Username}");
                        else
                            Console.WriteLine($"Failed to start route for bot {botToStart.Username}");
                    }
                    else
                    {
                        Console.WriteLine($"Bot '{args[2]}' not found");
                    }
                    break;

                case "stop":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: route stop <bot>");
                        return;
                    }
                    var botToStop = services.Bots.GetBot(args[2]);
                    if (botToStop != null)
                    {
                        services.Routes.StopRoute(botToStop);
                        Console.WriteLine($"Stopped route for bot {botToStop.Username}");
                    }
                    else
                    {
                        Console.WriteLine($"Bot '{args[2]}' not found");
                    }
                    break;

                case "status":
                    if (args.Length >= 3)
                    {
                        var botForStatus = services.Bots.GetBot(args[2]);
                        if (botForStatus != null)
                        {
                            Console.WriteLine($"{botForStatus.Username}: {services.Routes.GetRouteStatus(botForStatus)}");
                        }
                        else
                        {
                            Console.WriteLine($"Bot '{args[2]}' not found");
                        }
                    }
                    else
                    {
                        var allBots = services.Bots.GetAllBots();
                        foreach (var bot in allBots)
                        {
                            Console.WriteLine($"{bot.Username}: {services.Routes.GetRouteStatus(bot)}");
                        }
                    }
                    break;

                case "startall":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: route startall <routefile>");
                        return;
                    }
                    string routePathAll = ResolvePath(args[2], "routes");
                    int startedCount = 0;
                    var botsToStart = services.Bots.GetAllBots();
                    foreach (var bot in botsToStart)
                    {
                        if (services.Routes.StartRoute(bot, routePathAll))
                            startedCount++;
                    }
                    Console.WriteLine($"Started route for {startedCount}/{botsToStart.Count} bots");
                    break;

                case "stopall":
                    var botsToStop = services.Bots.GetAllBots();
                    foreach (var bot in botsToStop)
                    {
                        services.Routes.StopRoute(bot);
                    }
                    Console.WriteLine($"Stopped routes for all {botsToStop.Count} bots");
                    break;

                default:
                    Console.WriteLine($"Unknown route command: {command}");
                    break;
            }
        }

        private void HandleTestCommand(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: test <command> [args...]");
                Console.WriteLine("Commands:");
                Console.WriteLine("  test run <routefile>           - Start a test run with harness settings");
                Console.WriteLine("  test run-suite <suitefile> [--parallel] - Run a test suite");
                Console.WriteLine("  test status [runId]            - Show status of test runs");
                Console.WriteLine("  test suite-status [suiteId]    - Show status of suite runs");
                Console.WriteLine("  test report <runId> [json]     - Generate report for a test run");
                Console.WriteLine("  test list                      - List all test runs");
                Console.WriteLine("  test suite-list                - List all suite runs");
                Console.WriteLine("  test stop <runId>              - Stop a running test");
                Console.WriteLine("  test suite-stop <suiteId>      - Stop a running suite");
                return;
            }

            string command = args[1].ToLowerInvariant();

            switch (command)
            {
                case "run":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: test run <routefile>");
                        return;
                    }
                    string routePath = ResolvePath(args[2], "routes");
                    if (!File.Exists(routePath))
                    {
                        Console.WriteLine($"Route file not found: {routePath}");
                        return;
                    }

                    Task.Run(async () =>
                    {
                        try
                        {
                            var run = await services.Tests.StartTestRunAsync(routePath);
                            factory.Log($"Test run {run.Id} finished with status: {run.Status}");
                        }
                        catch (Exception ex)
                        {
                            factory.Log($"Test run failed: {ex.Message}", LogLevel.Error);
                        }
                    });
                    Console.WriteLine($"Test run started for route: {routePath}");
                    break;

                case "status":
                    if (args.Length >= 3)
                    {
                        var specificRun = services.Tests.GetTestRun(args[2]);
                        if (specificRun != null)
                        {
                            DisplayTestRunStatus(specificRun);
                        }
                        else
                        {
                            Console.WriteLine($"Test run '{args[2]}' not found");
                        }
                    }
                    else
                    {
                        var activeRuns = services.Tests.GetActiveRuns();
                        if (activeRuns.Count == 0)
                        {
                            Console.WriteLine("No active test runs");
                        }
                        else
                        {
                            Console.WriteLine($"Active test runs: {activeRuns.Count}");
                            foreach (var run in activeRuns)
                            {
                                DisplayTestRunStatus(run);
                            }
                        }
                    }
                    break;

                case "report":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: test report <runId> [json]");
                        return;
                    }
                    var runForReport = services.Tests.GetTestRun(args[2]);
                    if (runForReport == null)
                    {
                        Console.WriteLine($"Test run '{args[2]}' not found");
                        return;
                    }
                    bool jsonFormat = args.Length > 3 && args[3].ToLowerInvariant() == "json";
                    if (jsonFormat)
                    {
                        Console.WriteLine(TestReportGenerator.GenerateJsonReport(runForReport));
                    }
                    else
                    {
                        Console.WriteLine(TestReportGenerator.GenerateConsoleReport(runForReport));
                    }
                    break;

                case "list":
                    Console.WriteLine("=== Active Test Runs ===");
                    foreach (var run in services.Tests.GetActiveRuns())
                    {
                        Console.WriteLine($"  {run.Id}: {run.RouteName ?? run.RoutePath} - {run.Status}");
                    }
                    Console.WriteLine("=== Completed Test Runs ===");
                    foreach (var completedRun in services.Tests.GetCompletedRuns())
                    {
                        string statusSymbol = completedRun.Status == TestRunStatus.Completed ? "[OK]" : "[X]";
                        Console.WriteLine($"  {completedRun.Id}: {completedRun.RouteName ?? completedRun.RoutePath} - {statusSymbol} {completedRun.Status}");
                    }
                    break;

                case "stop":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: test stop <runId>");
                        return;
                    }
                    if (services.Tests.StopTestRun(args[2]))
                    {
                        Console.WriteLine($"Stopping test run {args[2]}");
                    }
                    else
                    {
                        Console.WriteLine($"Test run '{args[2]}' not found or already completed");
                    }
                    break;

                case "run-suite":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: test run-suite <suitefile> [--parallel]");
                        return;
                    }
                    string suitePath = ResolvePath(args[2], Path.Combine("routes", "suites"));
                    if (!File.Exists(suitePath))
                    {
                        suitePath = ResolvePath(args[2], "routes");
                        if (!File.Exists(suitePath))
                        {
                            Console.WriteLine($"Suite file not found: {args[2]}");
                            return;
                        }
                    }
                    bool parallel = args.Length > 3 && args[3].ToLowerInvariant() == "--parallel";

                    Task.Run(async () =>
                    {
                        try
                        {
                            var suite = await services.Tests.StartSuiteAsync(suitePath, parallel);
                            factory.Log($"Test suite {suite.Id} finished with status: {suite.Status}");
                        }
                        catch (Exception ex)
                        {
                            factory.Log($"Test suite failed: {ex.Message}", LogLevel.Error);
                        }
                    });
                    Console.WriteLine($"Test suite started: {suitePath}" + (parallel ? " (parallel mode)" : ""));
                    break;

                case "suite-status":
                    if (args.Length >= 3)
                    {
                        var specificSuite = services.Tests.GetSuiteRun(args[2]);
                        if (specificSuite != null)
                        {
                            DisplaySuiteRunStatus(specificSuite);
                        }
                        else
                        {
                            Console.WriteLine($"Suite run '{args[2]}' not found");
                        }
                    }
                    else
                    {
                        var activeSuites = services.Tests.GetActiveSuites();
                        if (activeSuites.Count == 0)
                        {
                            Console.WriteLine("No active suite runs");
                        }
                        else
                        {
                            Console.WriteLine($"Active suite runs: {activeSuites.Count}");
                            foreach (var suite in activeSuites)
                            {
                                DisplaySuiteRunStatus(suite);
                            }
                        }
                    }
                    break;

                case "suite-list":
                    Console.WriteLine("=== Active Suite Runs ===");
                    foreach (var suite in services.Tests.GetActiveSuites())
                    {
                        Console.WriteLine($"  {suite.Id}: {suite.SuiteName} - {suite.Status} ({suite.TestRuns.Count}/{suite.TotalTests} tests)");
                    }
                    Console.WriteLine("=== Completed Suite Runs ===");
                    foreach (var completedSuite in services.Tests.GetCompletedSuites())
                    {
                        string statusSymbol = completedSuite.Status == TestSuiteRunStatus.Completed ? "[OK]" : "[X]";
                        Console.WriteLine($"  {completedSuite.Id}: {completedSuite.SuiteName} - {statusSymbol} {completedSuite.Status} ({completedSuite.TestsPassed}/{completedSuite.TotalTests} passed)");
                    }
                    break;

                case "suite-stop":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: test suite-stop <suiteId>");
                        return;
                    }
                    if (services.Tests.StopSuite(args[2]))
                    {
                        Console.WriteLine($"Stopping suite run {args[2]}");
                    }
                    else
                    {
                        Console.WriteLine($"Suite run '{args[2]}' not found or already completed");
                    }
                    break;

                default:
                    Console.WriteLine($"Unknown test command: {command}");
                    break;
            }
        }

        private void DisplayStatistics(string botname)
        {
            var allBots = services.Bots.GetAllBots();

            if (string.IsNullOrEmpty(botname))
            {
                foreach (var bot in allBots)
                    DisplayBotStatistics(bot);

                Console.WriteLine($"{allBots.Count(b => b.Running)} bots are active");
                Console.WriteLine($"{allBots.Count(b => b.Connected)} bots are connected");
                Console.WriteLine($"{allBots.Count(b => b.LoggedIn)} bots are ingame");
            }
            else
            {
                var bot = services.Bots.GetBot(botname);
                if (bot == null)
                    Console.WriteLine($"Bot with username '{botname}' not found");
                else
                    DisplayBotStatistics(bot);
            }
        }

        private void DisplayBotStatistics(BotGame bot)
        {
            Console.WriteLine($"Bot username: {bot.Username}");
            Console.WriteLine($"\tBehavior: {bot.Behavior.Name}");
            Console.WriteLine($"\tRunning: {bot.Running}");
            Console.WriteLine($"\tConnected: {bot.Connected}");
            Console.WriteLine($"\tLogged In: {bot.LoggedIn}");
            Console.WriteLine($"\tPosition: {bot.Player.GetPosition()}");
            if (bot.GroupLeaderGuid == 0)
                Console.WriteLine("\tGroup Leader: Not in group");
            else if (!bot.World.PlayerNameLookup.ContainsKey(bot.GroupLeaderGuid))
                Console.WriteLine("\tGroup Leader: Not found");
            else
                Console.WriteLine($"\tGroup Leader: {bot.World.PlayerNameLookup[bot.GroupLeaderGuid]}");
            Console.WriteLine($"\tLast Received Packet: {bot.LastReceivedPacket}");
            Console.WriteLine($"\tLast Received Packet Time: {bot.LastReceivedPacketTime.ToLongTimeString()}");
            Console.WriteLine($"\tLast Sent Packet: {bot.LastSentPacket}");
            Console.WriteLine($"\tLast Sent Packet Time: {bot.LastSentPacketTime.ToLongTimeString()}");
            Console.WriteLine($"\tLast Update() call: {bot.LastUpdate.ToLongTimeString()}");
            Console.WriteLine($"\tSchedule Actions: {bot.ScheduledActionsCount}");
        }

        private void DisplayTestRunStatus(TestRun run)
        {
            Console.WriteLine($"Test Run: {run.Id}");
            Console.WriteLine($"  Route: {run.RouteName ?? run.RoutePath}");
            Console.WriteLine($"  Status: {run.Status}");
            Console.WriteLine($"  Duration: {FormatDuration(run.Duration)}");
            Console.WriteLine($"  Bots: {run.BotsCompleted}/{run.BotResults.Count} completed, {run.BotsPassed} passed, {run.BotsFailed} failed");
        }

        private void DisplaySuiteRunStatus(TestSuiteRun suite)
        {
            Console.WriteLine($"Suite Run: {suite.Id}");
            Console.WriteLine($"  Name: {suite.SuiteName}");
            Console.WriteLine($"  Status: {suite.Status}");
            Console.WriteLine($"  Mode: {(suite.ParallelMode ? "Parallel" : "Sequential")}");
            Console.WriteLine($"  Tests: {suite.TestRuns.Count}/{suite.TotalTests}");
            Console.WriteLine($"  Passed: {suite.TestsPassed}, Failed: {suite.TestsFailed}, Skipped: {suite.TestsSkipped}");
            Console.WriteLine($"  Duration: {suite.Duration.TotalSeconds:F1}s");
        }

        private void DisplayHelp()
        {
            Console.WriteLine("Available commands:");
            Console.WriteLine("  info / stats [bot]    - Display bot statistics");
            Console.WriteLine("  route <command>       - Manage bot task routes");
            Console.WriteLine("  routes [directory]    - List available routes");
            Console.WriteLine("  test <command>        - Run tests with harness settings");
            Console.WriteLine("  help                  - Show this help");
            Console.WriteLine("  quit / exit           - Shutdown BotFarm");
        }

        private static string ResolvePath(string path, string defaultDirectory)
        {
            if (Path.IsPathRooted(path))
                return path;
            return Path.Combine(defaultDirectory, path);
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m";
            if (duration.TotalMinutes >= 1)
                return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
            return $"{duration.Seconds}.{duration.Milliseconds / 100}s";
        }
    }
}
