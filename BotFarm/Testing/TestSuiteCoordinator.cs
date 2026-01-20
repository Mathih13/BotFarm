using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BotFarm.Testing
{
    /// <summary>
    /// Coordinates test suite execution with dependency management and parallel execution support
    /// </summary>
    public class TestSuiteCoordinator
    {
        private readonly BotFactory factory;
        private readonly TestRunCoordinator testCoordinator;
        private readonly Dictionary<string, TestSuiteRun> activeSuites = new Dictionary<string, TestSuiteRun>();
        private readonly List<TestSuiteRun> completedSuites = new List<TestSuiteRun>();
        private readonly object suiteLock = new object();

        public event EventHandler<TestSuiteRun> SuiteStarted;
        public event EventHandler<TestSuiteRun> SuiteCompleted;
        public event EventHandler<(TestSuiteRun suite, TestRun test)> TestCompleted;

        public TestSuiteCoordinator(BotFactory factory, TestRunCoordinator testCoordinator)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.testCoordinator = testCoordinator ?? throw new ArgumentNullException(nameof(testCoordinator));
        }

        public IReadOnlyList<TestSuiteRun> CompletedSuites
        {
            get
            {
                lock (suiteLock)
                {
                    return new List<TestSuiteRun>(completedSuites);
                }
            }
        }

        public IReadOnlyDictionary<string, TestSuiteRun> ActiveSuites
        {
            get
            {
                lock (suiteLock)
                {
                    return new Dictionary<string, TestSuiteRun>(activeSuites);
                }
            }
        }

        public TestSuiteRun GetSuiteRun(string runId)
        {
            lock (suiteLock)
            {
                if (activeSuites.TryGetValue(runId, out var active))
                    return active;
                return completedSuites.Find(r => r.Id == runId);
            }
        }

        /// <summary>
        /// Load a test suite from a JSON file
        /// </summary>
        public TestSuite LoadSuite(string suitePath)
        {
            if (!File.Exists(suitePath))
            {
                throw new FileNotFoundException($"Suite file not found: {suitePath}");
            }

            string json = File.ReadAllText(suitePath);
            var data = JsonSerializer.Deserialize<TestSuiteData>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data == null || data.tests == null)
            {
                throw new InvalidOperationException("Invalid test suite format");
            }

            var suite = new TestSuite
            {
                Name = data.name ?? Path.GetFileNameWithoutExtension(suitePath),
                Tests = data.tests.Select(t => new TestSuiteEntry
                {
                    Route = t.route,
                    DependsOn = t.dependsOn
                }).ToList()
            };

            return suite;
        }

        /// <summary>
        /// Start a test suite run
        /// </summary>
        /// <param name="suitePath">Path to the suite JSON file</param>
        /// <param name="parallel">If true, run tests at the same dependency level in parallel</param>
        /// <param name="ct">Cancellation token</param>
        public async Task<TestSuiteRun> StartSuiteRunAsync(string suitePath, bool parallel = false, CancellationToken ct = default)
        {
            // Resolve the suite path to an absolute path
            var resolvedPath = ResolveSuitePath(suitePath);

            // Load and validate suite
            var suite = LoadSuite(resolvedPath);
            var errors = suite.Validate();
            if (errors.Count > 0)
            {
                throw new InvalidOperationException($"Suite validation failed:\n{string.Join("\n", errors)}");
            }

            // Resolve route paths relative to suite file
            string suiteDir = Path.GetDirectoryName(resolvedPath);

            var suiteRun = new TestSuiteRun(resolvedPath, suite.Name, parallel)
            {
                Status = TestSuiteRunStatus.Running,
                TotalTests = suite.Tests.Count
            };

            lock (suiteLock)
            {
                activeSuites[suiteRun.Id] = suiteRun;
            }

            factory.Log($"Starting test suite '{suite.Name}' (ID: {suiteRun.Id}, parallel={parallel})");
            SuiteStarted?.Invoke(this, suiteRun);

            var passedTests = new HashSet<string>();
            var failedTests = new HashSet<string>();

            try
            {
                if (parallel)
                {
                    // Execute tests by dependency level, running each level in parallel
                    var levels = suite.GetExecutionLevels();
                    factory.Log($"Suite has {levels.Count} dependency levels");

                    for (int levelIndex = 0; levelIndex < levels.Count; levelIndex++)
                    {
                        ct.ThrowIfCancellationRequested();

                        var level = levels[levelIndex];
                        var levelTests = level.Where(t => CanRunTest(t, passedTests, failedTests)).ToList();

                        if (levelTests.Count == 0)
                        {
                            factory.Log($"[Level {levelIndex}] No tests to run (dependencies failed)");
                            suiteRun.TestsSkipped += level.Count;
                            continue;
                        }

                        factory.Log($"[Level {levelIndex}] Running {levelTests.Count} test(s) in parallel...");

                        // Run all tests at this level in parallel
                        var tasks = levelTests.Select(async test =>
                        {
                            var routePath = ResolveRoutePath(test.Route, suiteDir);
                            return await RunSingleTestAsync(test, routePath, suiteRun, ct);
                        }).ToList();

                        var results = await Task.WhenAll(tasks);

                        // Update pass/fail tracking
                        for (int i = 0; i < levelTests.Count; i++)
                        {
                            var test = levelTests[i];
                            var testRun = results[i];
                            if (testRun != null && testRun.Status == TestRunStatus.Completed && testRun.BotsFailed == 0)
                            {
                                passedTests.Add(test.Name);
                            }
                            else
                            {
                                failedTests.Add(test.Name);
                            }
                        }
                    }
                }
                else
                {
                    // Execute tests sequentially in topological order
                    var orderedTests = suite.GetTopologicalOrder();
                    int testIndex = 1;

                    foreach (var test in orderedTests)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (!CanRunTest(test, passedTests, failedTests))
                        {
                            factory.Log($"[{testIndex}/{orderedTests.Count}] Skipping: {test.Route} (dependency failed)");
                            suiteRun.TestsSkipped++;
                            testIndex++;
                            continue;
                        }

                        factory.Log($"[{testIndex}/{orderedTests.Count}] Running: {test.Route}");

                        var routePath = ResolveRoutePath(test.Route, suiteDir);
                        var testRun = await RunSingleTestAsync(test, routePath, suiteRun, ct);

                        if (testRun != null && testRun.Status == TestRunStatus.Completed && testRun.BotsFailed == 0)
                        {
                            passedTests.Add(test.Name);
                            factory.Log($"      Status: Completed ({testRun.BotsPassed}/{testRun.BotResults.Count} bots passed) - {FormatDuration(testRun.Duration)}");
                        }
                        else
                        {
                            failedTests.Add(test.Name);
                            var status = testRun?.Status.ToString() ?? "Error";
                            factory.Log($"      Status: {status} - {testRun?.ErrorMessage ?? "Unknown error"}");
                        }

                        testIndex++;
                    }
                }

                // Determine final status
                bool allPassed = suiteRun.TestsFailed == 0 && suiteRun.TestsSkipped == 0;
                suiteRun.Complete(allPassed);

                factory.Log("");
                factory.Log(new string('=', 63));
                factory.Log($"SUITE {(allPassed ? "COMPLETED" : "FAILED")}: {suite.Name}");
                factory.Log(new string('=', 63));
                factory.Log($"Tests: {suiteRun.TestsPassed}/{suiteRun.TotalTests} passed" +
                    (suiteRun.TestsFailed > 0 ? $", {suiteRun.TestsFailed} failed" : "") +
                    (suiteRun.TestsSkipped > 0 ? $", {suiteRun.TestsSkipped} skipped" : ""));
                factory.Log($"Total Duration: {FormatDuration(suiteRun.Duration)}");
            }
            catch (OperationCanceledException)
            {
                suiteRun.Cancel();
                factory.Log($"Test suite {suiteRun.Id} was cancelled");
            }
            catch (Exception ex)
            {
                suiteRun.Complete(false, ex.Message);
                factory.Log($"Test suite {suiteRun.Id} failed with error: {ex.Message}");
            }
            finally
            {
                // Move from active to completed
                lock (suiteLock)
                {
                    activeSuites.Remove(suiteRun.Id);
                    completedSuites.Add(suiteRun);
                }

                SuiteCompleted?.Invoke(this, suiteRun);
            }

            return suiteRun;
        }

        /// <summary>
        /// Stop a running suite
        /// </summary>
        public bool StopSuiteRun(string runId)
        {
            lock (suiteLock)
            {
                if (activeSuites.TryGetValue(runId, out var run))
                {
                    run.Cancel();
                    return true;
                }
            }
            return false;
        }

        private async Task<TestRun> RunSingleTestAsync(TestSuiteEntry test, string routePath, TestSuiteRun suiteRun, CancellationToken ct)
        {
            try
            {
                var testRun = await testCoordinator.StartTestRunAsync(routePath, ct);
                suiteRun.AddTestRun(testRun);
                TestCompleted?.Invoke(this, (suiteRun, testRun));
                return testRun;
            }
            catch (Exception ex)
            {
                factory.Log($"Failed to run test {test.Route}: {ex.Message}");
                return null;
            }
        }

        private bool CanRunTest(TestSuiteEntry test, HashSet<string> passedTests, HashSet<string> failedTests)
        {
            if (test.DependsOn == null || test.DependsOn.Count == 0)
            {
                return true;
            }

            // Check if any dependency failed
            foreach (var dep in test.DependsOn)
            {
                if (failedTests.Contains(dep))
                {
                    return false; // Dependency failed, skip this test
                }
                if (!passedTests.Contains(dep))
                {
                    return false; // Dependency hasn't run yet
                }
            }

            return true;
        }

        /// <summary>
        /// Resolve a suite path to its full file system path.
        /// Searches in routes/suites/, routes/, and current directory.
        /// </summary>
        private string ResolveSuitePath(string suitePath)
        {
            // Normalize path separators
            suitePath = suitePath.Replace('/', Path.DirectorySeparatorChar);

            // If it's already an absolute path that exists, use it
            if (Path.IsPathRooted(suitePath) && File.Exists(suitePath))
            {
                return suitePath;
            }

            var routesDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "routes");

            // Try routes/suites/ directory first
            var suitesPath = Path.Combine(routesDirectory, "suites", suitePath);
            if (File.Exists(suitesPath))
            {
                return Path.GetFullPath(suitesPath);
            }

            // Try routes/ directory
            var routesPath = Path.Combine(routesDirectory, suitePath);
            if (File.Exists(routesPath))
            {
                return Path.GetFullPath(routesPath);
            }

            // Try current directory
            if (File.Exists(suitePath))
            {
                return Path.GetFullPath(suitePath);
            }

            throw new FileNotFoundException($"Suite file not found: {suitePath}");
        }

        private string ResolveRoutePath(string route, string suiteDir)
        {
            if (Path.IsPathRooted(route))
            {
                return route;
            }

            // Try relative to suite file first (for paths like "../northshire/test.json")
            string relativeToSuite = Path.Combine(suiteDir, route);
            if (File.Exists(relativeToSuite))
            {
                return Path.GetFullPath(relativeToSuite);
            }

            // Try relative to main routes directory (for paths like "northshire/test.json")
            // Suite files are in routes/suites/, so go up one level to routes/
            string routesDir = Path.GetDirectoryName(suiteDir);
            if (!string.IsNullOrEmpty(routesDir))
            {
                string relativeToRoutes = Path.Combine(routesDir, route);
                if (File.Exists(relativeToRoutes))
                {
                    return Path.GetFullPath(relativeToRoutes);
                }
            }

            // Try relative to app's routes directory directly
            string appRoutesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "routes");
            if (Directory.Exists(appRoutesDir))
            {
                string relativeToAppRoutes = Path.Combine(appRoutesDir, route);
                if (File.Exists(relativeToAppRoutes))
                {
                    return Path.GetFullPath(relativeToAppRoutes);
                }
            }

            // Return as-is and let the test coordinator handle the error
            return Path.GetFullPath(Path.Combine(suiteDir, route));
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
            {
                return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
            }
            if (duration.TotalMinutes >= 1)
            {
                return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
            }
            return $"{duration.TotalSeconds:F1}s";
        }
    }
}
