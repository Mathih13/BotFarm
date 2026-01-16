using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BotFarm.AI.Tasks;
using BotFarm.Properties;
using Client.AI.Tasks;

namespace BotFarm.Testing
{
    /// <summary>
    /// Coordinates test runs - spawns bots, monitors progress, collects results
    /// </summary>
    public class TestRunCoordinator
    {
        private readonly BotFactory factory;
        private readonly SnapshotManager snapshotManager;
        private readonly Dictionary<string, TestRun> activeRuns = new Dictionary<string, TestRun>();
        private readonly List<TestRun> completedRuns = new List<TestRun>();
        private readonly object runLock = new object();

        public event EventHandler<TestRun> TestRunStarted;
        public event EventHandler<TestRun> TestRunCompleted;
        public event EventHandler<TestRun> TestRunStatusChanged;
        public event EventHandler<(TestRun run, BotTestResult bot)> BotCompleted;

        public TestRunCoordinator(BotFactory factory, SnapshotManager snapshotManager = null)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.snapshotManager = snapshotManager;
        }

        public IReadOnlyList<TestRun> CompletedRuns
        {
            get
            {
                lock (runLock)
                {
                    return new List<TestRun>(completedRuns);
                }
            }
        }

        public IReadOnlyDictionary<string, TestRun> ActiveRuns
        {
            get
            {
                lock (runLock)
                {
                    return new Dictionary<string, TestRun>(activeRuns);
                }
            }
        }

        /// <summary>
        /// Get count of bots currently active in test runs
        /// </summary>
        public int GetActiveTestBotCount()
        {
            lock (runLock)
            {
                return activeRuns.Values
                    .Where(r => r.Status == TestRunStatus.SettingUp || r.Status == TestRunStatus.Running)
                    .Sum(r => r.Harness?.BotCount ?? 0);
            }
        }

        public TestRun GetTestRun(string runId)
        {
            lock (runLock)
            {
                if (activeRuns.TryGetValue(runId, out var active))
                    return active;
                return completedRuns.Find(r => r.Id == runId);
            }
        }

        /// <summary>
        /// Start a test run for a route with harness settings.
        /// Uses single-phase approach: bots log in, create characters, then use GM commands
        /// for setup (level, items, quests, teleport) without needing to log out.
        /// </summary>
        public async Task<TestRun> StartTestRunAsync(string routePath, CancellationToken ct = default)
        {
            // Resolve the route path - if it's a relative path, resolve it relative to the routes directory
            var fullRoutePath = ResolveRoutePath(routePath);

            // Load the route
            var route = TaskRouteLoader.LoadFromJson(fullRoutePath);
            if (route == null)
            {
                throw new InvalidOperationException($"Failed to load route from {routePath}");
            }

            if (!route.HasHarness)
            {
                throw new InvalidOperationException($"Route {routePath} does not have harness settings defined");
            }

            var harness = route.Harness;
            var testRun = new TestRun(routePath, harness)
            {
                RouteName = route.Name,
                Status = TestRunStatus.SettingUp
            };

            lock (runLock)
            {
                activeRuns[testRun.Id] = testRun;
            }

            factory.Log($"Starting test run {testRun.Id} for route '{route.Name}' with {harness.BotCount} bots");
            TestRunStarted?.Invoke(this, testRun);

            var testBots = new List<BotGame>();
            var botResults = new Dictionary<BotGame, BotTestResult>();

            try
            {
                // ========== Create and start bots ==========
                factory.Log($"Creating {harness.BotCount} bot accounts and characters...");

                for (int i = 0; i < harness.BotCount; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    string username = $"{harness.AccountPrefix}{i + 1}";
                    string botClass = harness.Classes != null && harness.Classes.Count > 0
                        ? harness.Classes[i % harness.Classes.Count]
                        : "Warrior";

                    // Create bot via factory (handles RA account creation with GM level 2)
                    var bot = factory.CreateTestBot(username, harness, i, startBot: false);

                    var result = testRun.AddBot(username, null, botClass); // Character name filled in after login
                    botResults[bot] = result;
                    testBots.Add(bot);
                }

                // Start bots with staggered delay to avoid auth server throttling
                foreach (var bot in testBots)
                {
                    bot.Start();
                    await Task.Delay(500, ct);
                }

                await WaitForAllBotsLoggedIn(testBots, harness.SetupTimeoutSeconds, ct);

                // Capture character names and update results
                for (int i = 0; i < testBots.Count; i++)
                {
                    var charName = testBots[i].World.SelectedCharacter?.Name;
                    if (string.IsNullOrEmpty(charName))
                    {
                        throw new InvalidOperationException($"Bot {i} failed to create character");
                    }
                    botResults[testBots[i]].CharacterName = charName;
                }

                factory.Log("All bots logged in. Applying harness setup via GM commands...");

                // ========== Apply harness setup using GM commands (bots stay online) ==========
                bool needsSetup = harness.Level > 1
                    || (harness.Items?.Count > 0)
                    || (harness.CompletedQuests?.Count > 0)
                    || harness.StartPosition != null;

                if (needsSetup)
                {
                    factory.Log($"Setup: level={harness.Level}, items={harness.Items?.Count ?? 0}, quests={harness.CompletedQuests?.Count ?? 0}");

                    foreach (var bot in testBots)
                    {
                        bot.ApplyHarnessSetup();
                    }

                    // Wait for GM commands to take effect
                    await Task.Delay(2000, ct);
                }

                // Restore snapshot if specified (requires offline - log warning)
                if (!string.IsNullOrEmpty(harness.RestoreSnapshot) && snapshotManager?.IsAvailable == true)
                {
                    factory.Log($"Warning: Snapshot restore '{harness.RestoreSnapshot}' requires offline characters - skipping in single-phase mode");
                    // TODO: Could implement logout/restore/login if snapshots are critical
                }

                // ========== Start test execution ==========
                testRun.Status = TestRunStatus.Running;
                TestRunStatusChanged?.Invoke(this, testRun);

                factory.Log($"Starting route '{route.Name}' on all {testBots.Count} bots");
                foreach (var bot in testBots)
                {
                    var result = botResults[bot];

                    // Load route and subscribe to events BEFORE starting
                    // This prevents race conditions where tasks complete before handlers are attached
                    var executor = bot.LoadRoute(fullRoutePath);
                    if (executor != null)
                    {
                        executor.TaskCompleted += (sender, args) =>
                        {
                            result.AddTaskResult(args.Task.Name, args.Result, args.Duration, args.ErrorMessage);
                            result.AddLog($"Task '{args.Task.Name}' {args.Result}");
                        };

                        executor.RouteCompleted += (sender, args) =>
                        {
                            result.Complete(args.Success, args.ErrorMessage);
                            result.AddLog($"Route completed: {(args.Success ? "SUCCESS" : "FAILED")}");
                            BotCompleted?.Invoke(this, (testRun, result));
                        };

                        // Now start the route after events are subscribed
                        bot.StartLoadedRoute();
                    }
                }

                // Send status update now that routes are started
                TestRunStatusChanged?.Invoke(this, testRun);

                // Wait for completion or timeout
                var testTimeout = TimeSpan.FromSeconds(harness.TestTimeoutSeconds);
                var testDeadline = DateTime.UtcNow + testTimeout;

                factory.Log($"Waiting for test completion (timeout: {testTimeout.TotalSeconds}s)");

                var lastStatusUpdate = DateTime.UtcNow;
                while (DateTime.UtcNow < testDeadline)
                {
                    ct.ThrowIfCancellationRequested();

                    // Check if all bots completed
                    if (testRun.BotsCompleted == testBots.Count)
                    {
                        break;
                    }

                    // Send periodic status updates (every 2 seconds) for duration tracking
                    if ((DateTime.UtcNow - lastStatusUpdate).TotalSeconds >= 2)
                    {
                        TestRunStatusChanged?.Invoke(this, testRun);
                        lastStatusUpdate = DateTime.UtcNow;
                    }

                    await Task.Delay(1000, ct);
                }

                // Determine final status
                if (testRun.BotsCompleted < testBots.Count)
                {
                    testRun.Timeout();
                    factory.Log($"Test run {testRun.Id} timed out: {testRun.BotsCompleted}/{testBots.Count} bots completed");
                }
                else if (testRun.BotsFailed > 0)
                {
                    testRun.Complete(false, $"{testRun.BotsFailed}/{testBots.Count} bots failed");
                    factory.Log($"Test run {testRun.Id} completed with failures: {testRun.BotsFailed}/{testBots.Count} failed");
                }
                else
                {
                    testRun.Complete(true);
                    factory.Log($"Test run {testRun.Id} completed successfully: {testRun.BotsPassed}/{testBots.Count} passed");

                    // Save snapshot if specified and test passed (must log out first)
                    if (!string.IsNullOrEmpty(harness.SaveSnapshot) && snapshotManager?.IsAvailable == true)
                    {
                        factory.Log($"Saving snapshot '{harness.SaveSnapshot}' for successful test...");

                        // Log out all bots first (snapshot requires offline character)
                        await Task.WhenAll(testBots.Select(bot => bot.Exit()));
                        await Task.Delay(1000); // Wait for server to process logout

                        // Save snapshot for first character (snapshots are per-character, use first bot as representative)
                        var firstCharName = botResults[testBots[0]].CharacterName;
                        if (!string.IsNullOrEmpty(firstCharName))
                        {
                            if (snapshotManager.SaveSnapshot(harness.SaveSnapshot, firstCharName))
                            {
                                factory.Log($"Snapshot '{harness.SaveSnapshot}' saved from character {firstCharName}");
                            }
                            else
                            {
                                factory.Log($"Warning: Failed to save snapshot '{harness.SaveSnapshot}'");
                            }
                        }

                        // Clear testBots so they don't get disposed again in finally block
                        testBots.Clear();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                testRun.Cancel();
                factory.Log($"Test run {testRun.Id} was cancelled");
            }
            catch (Exception ex)
            {
                testRun.Complete(false, ex.Message);
                factory.Log($"Test run {testRun.Id} failed with error: {ex.Message}");
            }
            finally
            {
                // Cleanup bots in parallel
                await Task.WhenAll(testBots.Select(async bot =>
                {
                    try
                    {
                        await bot.DisposeAsync();
                    }
                    catch { }
                }));

                // Move from active to completed
                lock (runLock)
                {
                    activeRuns.Remove(testRun.Id);
                    completedRuns.Add(testRun);
                }

                TestRunCompleted?.Invoke(this, testRun);
            }

            return testRun;
        }

        /// <summary>
        /// Stop a running test
        /// </summary>
        public bool StopTestRun(string runId)
        {
            lock (runLock)
            {
                if (activeRuns.TryGetValue(runId, out var run))
                {
                    run.Cancel();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Wait for all bots to log in with timeout
        /// </summary>
        private async Task WaitForAllBotsLoggedIn(List<BotGame> bots, int timeoutSeconds, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(timeoutSeconds);

            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                int loggedIn = bots.Count(b => b.LoggedIn);
                if (loggedIn == bots.Count)
                {
                    factory.Log($"All {bots.Count} bots logged in successfully");
                    return;
                }

                await Task.Delay(500, ct);
            }

            int final = bots.Count(b => b.LoggedIn);
            if (final < bots.Count)
                throw new TimeoutException($"Only {final}/{bots.Count} bots logged in within timeout");
        }

        /// <summary>
        /// Resolve a route path to its full file system path.
        /// If the path is already absolute and exists, returns it as-is.
        /// Otherwise, resolves it relative to the routes directory.
        /// </summary>
        private string ResolveRoutePath(string routePath)
        {
            // Normalize path separators
            routePath = routePath.Replace('/', Path.DirectorySeparatorChar);

            // If it's already an absolute path that exists, use it
            if (Path.IsPathRooted(routePath) && File.Exists(routePath))
            {
                return routePath;
            }

            // Otherwise, resolve relative to the routes directory
            var routesDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "routes");
            var fullPath = Path.Combine(routesDirectory, routePath);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Route file not found: {routePath}");
            }

            return fullPath;
        }
    }
}
