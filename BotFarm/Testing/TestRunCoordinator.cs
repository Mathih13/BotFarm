using System;
using System.Collections.Generic;
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
    internal class TestRunCoordinator
    {
        private readonly BotFactory factory;
        private readonly Dictionary<string, TestRun> activeRuns = new Dictionary<string, TestRun>();
        private readonly List<TestRun> completedRuns = new List<TestRun>();
        private readonly object runLock = new object();

        public event EventHandler<TestRun> TestRunStarted;
        public event EventHandler<TestRun> TestRunCompleted;
        public event EventHandler<(TestRun run, BotTestResult bot)> BotCompleted;

        public TestRunCoordinator(BotFactory factory)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
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
        /// Start a test run for a route with harness settings
        /// </summary>
        public async Task<TestRun> StartTestRunAsync(string routePath, CancellationToken ct = default)
        {
            // Load the route
            var route = TaskRouteLoader.LoadFromJson(routePath);
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

            var bots = new List<BotGame>();
            var botResults = new Dictionary<BotGame, BotTestResult>();

            try
            {
                // Create bots
                for (int i = 0; i < harness.BotCount; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    string username = $"{harness.AccountPrefix}{i + 1}";

                    // Determine class for this bot
                    string botClass = harness.Classes != null && harness.Classes.Count > 0
                        ? harness.Classes[i % harness.Classes.Count]
                        : "Warrior";

                    // Create bot via factory (handles RA account creation with fixed password)
                    var bot = factory.CreateTestBot(username, harness, i, startBot: false);

                    var result = testRun.AddBot(username, $"char_{i + 1}", botClass);
                    botResults[bot] = result;
                    bots.Add(bot);
                }

                // Start all bots
                testRun.Status = TestRunStatus.Running;
                foreach (var bot in bots)
                {
                    bot.Start();
                }

                // Wait for all bots to login (with timeout)
                var setupTimeout = TimeSpan.FromSeconds(harness.SetupTimeoutSeconds);
                var setupDeadline = DateTime.UtcNow + setupTimeout;

                factory.Log($"Waiting for {bots.Count} bots to login (timeout: {setupTimeout.TotalSeconds}s)");

                while (DateTime.UtcNow < setupDeadline)
                {
                    ct.ThrowIfCancellationRequested();

                    int loggedIn = 0;
                    foreach (var bot in bots)
                    {
                        if (bot.LoggedIn)
                            loggedIn++;
                    }

                    if (loggedIn == bots.Count)
                    {
                        factory.Log($"All {bots.Count} bots logged in successfully");
                        break;
                    }

                    await Task.Delay(500, ct);
                }

                // Check if all bots logged in
                int finalLoggedIn = 0;
                foreach (var bot in bots)
                {
                    if (bot.LoggedIn)
                        finalLoggedIn++;
                }

                if (finalLoggedIn < bots.Count)
                {
                    throw new TimeoutException($"Only {finalLoggedIn}/{bots.Count} bots logged in within timeout");
                }

                // Setup characters via RA (level, items) - bots must be logged out for level command
                // Note: This is tricky because bots are now online. We may need to use in-game commands instead.
                // For now, we'll skip level/item setup if the harness level > 1 since .character level needs offline char
                // TODO: Implement proper character setup flow

                // Start routes on all bots
                factory.Log($"Starting route '{route.Name}' on all {bots.Count} bots");
                foreach (var bot in bots)
                {
                    var result = botResults[bot];

                    // Subscribe to route events
                    bot.LoadAndStartRoute(routePath);
                    var executor = bot.GetRouteExecutor();
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
                    }
                }

                // Wait for completion or timeout
                var testTimeout = TimeSpan.FromSeconds(harness.TestTimeoutSeconds);
                var testDeadline = DateTime.UtcNow + testTimeout;

                factory.Log($"Waiting for test completion (timeout: {testTimeout.TotalSeconds}s)");

                while (DateTime.UtcNow < testDeadline)
                {
                    ct.ThrowIfCancellationRequested();

                    // Check if all bots completed
                    if (testRun.BotsCompleted == bots.Count)
                    {
                        break;
                    }

                    await Task.Delay(1000, ct);
                }

                // Determine final status
                if (testRun.BotsCompleted < bots.Count)
                {
                    testRun.Timeout();
                    factory.Log($"Test run {testRun.Id} timed out: {testRun.BotsCompleted}/{bots.Count} bots completed");
                }
                else if (testRun.BotsFailed > 0)
                {
                    testRun.Complete(false, $"{testRun.BotsFailed}/{bots.Count} bots failed");
                    factory.Log($"Test run {testRun.Id} completed with failures: {testRun.BotsFailed}/{bots.Count} failed");
                }
                else
                {
                    testRun.Complete(true);
                    factory.Log($"Test run {testRun.Id} completed successfully: {testRun.BotsPassed}/{bots.Count} passed");
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
                // Cleanup bots
                foreach (var bot in bots)
                {
                    try
                    {
                        await bot.DisposeAsync();
                    }
                    catch { }
                }

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
    }
}
