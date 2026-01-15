using System;
using System.Collections.Generic;
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
    internal class TestRunCoordinator
    {
        private readonly BotFactory factory;
        private readonly SnapshotManager snapshotManager;
        private readonly Dictionary<string, TestRun> activeRuns = new Dictionary<string, TestRun>();
        private readonly List<TestRun> completedRuns = new List<TestRun>();
        private readonly object runLock = new object();

        public event EventHandler<TestRun> TestRunStarted;
        public event EventHandler<TestRun> TestRunCompleted;
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

            var testBots = new List<BotGame>();
            var botResults = new Dictionary<BotGame, BotTestResult>();
            var characterNames = new Dictionary<int, string>(); // index -> character name

            try
            {
                // ========== PHASE 1: Character Creation ==========
                // Bots must log in to create characters, then log out for level/item/quest setup
                bool needsSetup = harness.Level > 1
                    || (harness.Items?.Count > 0)
                    || (harness.CompletedQuests?.Count > 0)
                    || !string.IsNullOrEmpty(harness.RestoreSnapshot);

                factory.Log($"Phase 1: Creating {harness.BotCount} bot accounts and characters...");

                var phase1Bots = new List<BotGame>();

                // Create bots for character creation
                for (int i = 0; i < harness.BotCount; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    string username = $"{harness.AccountPrefix}{i + 1}";

                    // Create bot via factory (handles RA account creation with fixed password)
                    var bot = factory.CreateTestBot(username, harness, i, startBot: false);
                    phase1Bots.Add(bot);
                }

                // Start phase 1 bots with staggered delay to avoid auth server throttling
                foreach (var bot in phase1Bots)
                {
                    bot.Start();
                    await Task.Delay(500, ct); // Small delay between starts
                }

                await WaitForAllBotsLoggedIn(phase1Bots, harness.SetupTimeoutSeconds, ct);

                // Capture character names before logout
                for (int i = 0; i < phase1Bots.Count; i++)
                {
                    characterNames[i] = phase1Bots[i].World.SelectedCharacter?.Name;
                    if (string.IsNullOrEmpty(characterNames[i]))
                    {
                        throw new InvalidOperationException($"Bot {i} failed to create character");
                    }
                }

                // If we need to set level/items, must log out first (TrinityCore requirement)
                if (needsSetup)
                {
                    factory.Log("Phase 1 complete. Logging out for character setup...");

                    // Exit all phase 1 bots in parallel (awaitable logout + dispose)
                    await Task.WhenAll(phase1Bots.Select(bot => bot.Exit()));

                    await Task.Delay(1000, ct); // Brief wait for server to process

                    // ========== CHARACTER SETUP VIA RA ==========
                    factory.Log($"Setting up characters: level={harness.Level}, items={harness.Items?.Count ?? 0}, quests={harness.CompletedQuests?.Count ?? 0}");

                    foreach (var kvp in characterNames)
                    {
                        factory.SetupCharacterViaRA(kvp.Value, harness.Level, harness.Items, harness.CompletedQuests);
                    }

                    // Restore snapshot if specified (character must be offline)
                    if (!string.IsNullOrEmpty(harness.RestoreSnapshot) && snapshotManager?.IsAvailable == true)
                    {
                        factory.Log($"Restoring snapshot '{harness.RestoreSnapshot}' for all characters...");
                        foreach (var kvp in characterNames)
                        {
                            if (!snapshotManager.RestoreSnapshot(harness.RestoreSnapshot, kvp.Value))
                            {
                                factory.Log($"Warning: Failed to restore snapshot for {kvp.Value}");
                            }
                        }
                    }

                    await Task.Delay(500, ct); // Brief wait for RA commands

                    // ========== PHASE 2: Test Execution ==========
                    factory.Log("Phase 2: Reconnecting bots for test execution...");

                    // Create fresh bot instances for test run
                    for (int i = 0; i < harness.BotCount; i++)
                    {
                        ct.ThrowIfCancellationRequested();

                        string username = $"{harness.AccountPrefix}{i + 1}";
                        string botClass = harness.Classes != null && harness.Classes.Count > 0
                            ? harness.Classes[i % harness.Classes.Count]
                            : "Warrior";

                        var bot = factory.CreateTestBot(username, harness, i, startBot: false);

                        // Tell bot to use existing character (don't delete and recreate)
                        bot.SetSkipCharacterCreation(true);

                        var result = testRun.AddBot(username, characterNames[i], botClass);
                        botResults[bot] = result;
                        testBots.Add(bot);
                    }

                    // Start phase 2 bots with staggered delay
                    testRun.Status = TestRunStatus.Running;
                    foreach (var bot in testBots)
                    {
                        bot.Start();
                        await Task.Delay(500, ct); // Small delay between starts
                    }

                    await WaitForAllBotsLoggedIn(testBots, harness.SetupTimeoutSeconds, ct);
                }
                else
                {
                    // No setup needed - use phase 1 bots directly
                    factory.Log("No level/item setup needed. Continuing with current bots...");
                    testRun.Status = TestRunStatus.Running;

                    for (int i = 0; i < phase1Bots.Count; i++)
                    {
                        string botClass = harness.Classes != null && harness.Classes.Count > 0
                            ? harness.Classes[i % harness.Classes.Count]
                            : "Warrior";

                        var result = testRun.AddBot($"{harness.AccountPrefix}{i + 1}", characterNames[i], botClass);
                        botResults[phase1Bots[i]] = result;
                        testBots.Add(phase1Bots[i]);
                    }
                }

                // Teleport to start position (bots must be online)
                if (harness.StartPosition != null)
                {
                    factory.Log($"Teleporting bots to start position...");
                    foreach (var kvp in characterNames)
                    {
                        factory.TeleportCharacterViaRA(
                            kvp.Value,
                            harness.StartPosition.MapId,
                            harness.StartPosition.X,
                            harness.StartPosition.Y,
                            harness.StartPosition.Z);
                    }
                    await Task.Delay(1000, ct); // Wait for teleport
                }

                // Start routes on all bots
                factory.Log($"Starting route '{route.Name}' on all {testBots.Count} bots");
                foreach (var bot in testBots)
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
                    if (testRun.BotsCompleted == testBots.Count)
                    {
                        break;
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
                        if (characterNames.TryGetValue(0, out var firstCharName))
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
    }
}
