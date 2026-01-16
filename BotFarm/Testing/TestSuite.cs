using System;
using System.Collections.Generic;
using System.Linq;

namespace BotFarm.Testing
{
    /// <summary>
    /// Status of a test suite run
    /// </summary>
    public enum TestSuiteRunStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Defines a test suite loaded from JSON
    /// </summary>
    public class TestSuite
    {
        public string Name { get; set; }
        public List<TestSuiteEntry> Tests { get; set; } = new List<TestSuiteEntry>();

        /// <summary>
        /// Validates the suite definition for errors like missing dependencies or cycles
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            var testNames = new HashSet<string>(Tests.Select(t => t.Name));

            foreach (var test in Tests)
            {
                if (string.IsNullOrWhiteSpace(test.Route))
                {
                    errors.Add($"Test '{test.Name}' has no route specified");
                }

                if (test.DependsOn != null)
                {
                    foreach (var dep in test.DependsOn)
                    {
                        if (!testNames.Contains(dep))
                        {
                            errors.Add($"Test '{test.Name}' depends on unknown test '{dep}'");
                        }
                    }
                }
            }

            // Check for cycles
            if (HasCycle())
            {
                errors.Add("Test suite has circular dependencies");
            }

            return errors;
        }

        /// <summary>
        /// Returns tests grouped by dependency level (0 = no dependencies, 1 = depends on level 0, etc.)
        /// </summary>
        public List<List<TestSuiteEntry>> GetExecutionLevels()
        {
            var levels = new List<List<TestSuiteEntry>>();
            var remaining = new HashSet<TestSuiteEntry>(Tests);
            var completed = new HashSet<string>();

            while (remaining.Count > 0)
            {
                var level = new List<TestSuiteEntry>();

                foreach (var test in remaining.ToList())
                {
                    var deps = test.DependsOn ?? new List<string>();
                    if (deps.All(d => completed.Contains(d)))
                    {
                        level.Add(test);
                    }
                }

                if (level.Count == 0)
                {
                    // Cycle detected or invalid state
                    break;
                }

                foreach (var test in level)
                {
                    remaining.Remove(test);
                    completed.Add(test.Name);
                }

                levels.Add(level);
            }

            return levels;
        }

        /// <summary>
        /// Returns tests in topological order (dependencies first)
        /// </summary>
        public List<TestSuiteEntry> GetTopologicalOrder()
        {
            var result = new List<TestSuiteEntry>();
            foreach (var level in GetExecutionLevels())
            {
                result.AddRange(level);
            }
            return result;
        }

        private bool HasCycle()
        {
            var visited = new HashSet<string>();
            var recursionStack = new HashSet<string>();
            var testMap = Tests.ToDictionary(t => t.Name);

            foreach (var test in Tests)
            {
                if (HasCycleDFS(test.Name, testMap, visited, recursionStack))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasCycleDFS(string testName, Dictionary<string, TestSuiteEntry> testMap,
            HashSet<string> visited, HashSet<string> recursionStack)
        {
            if (recursionStack.Contains(testName))
                return true;

            if (visited.Contains(testName))
                return false;

            visited.Add(testName);
            recursionStack.Add(testName);

            if (testMap.TryGetValue(testName, out var test) && test.DependsOn != null)
            {
                foreach (var dep in test.DependsOn)
                {
                    if (HasCycleDFS(dep, testMap, visited, recursionStack))
                    {
                        return true;
                    }
                }
            }

            recursionStack.Remove(testName);
            return false;
        }
    }

    /// <summary>
    /// A single test entry within a test suite
    /// </summary>
    public class TestSuiteEntry
    {
        public string Route { get; set; }
        public List<string> DependsOn { get; set; }

        /// <summary>
        /// The test name derived from the route filename without extension
        /// </summary>
        public string Name => System.IO.Path.GetFileNameWithoutExtension(Route);
    }

    /// <summary>
    /// Represents a running or completed test suite execution
    /// </summary>
    public class TestSuiteRun
    {
        public string Id { get; }
        public string SuiteName { get; }
        public string SuitePath { get; }
        public bool ParallelMode { get; }
        public TestSuiteRunStatus Status { get; set; }
        public List<TestRun> TestRuns { get; } = new List<TestRun>();
        public DateTime StartTime { get; }
        public DateTime? EndTime { get; set; }
        public string ErrorMessage { get; set; }

        public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;

        // Aggregate metrics
        public int TestsCompleted => TestRuns.Count(r => r.Status == TestRunStatus.Completed);
        public int TestsPassed => TestRuns.Count(r => r.Status == TestRunStatus.Completed && r.BotsFailed == 0);
        public int TestsFailed => TestRuns.Count(r => r.Status == TestRunStatus.Failed || r.Status == TestRunStatus.TimedOut ||
            (r.Status == TestRunStatus.Completed && r.BotsFailed > 0));
        public int TestsSkipped { get; set; }
        public int TotalTests { get; set; }

        public TestSuiteRun(string suitePath, string suiteName, bool parallelMode)
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            SuitePath = suitePath;
            SuiteName = suiteName;
            ParallelMode = parallelMode;
            StartTime = DateTime.UtcNow;
            Status = TestSuiteRunStatus.Pending;
        }

        public void AddTestRun(TestRun run)
        {
            TestRuns.Add(run);
        }

        public void Complete(bool success, string errorMessage = null)
        {
            EndTime = DateTime.UtcNow;
            Status = success ? TestSuiteRunStatus.Completed : TestSuiteRunStatus.Failed;
            ErrorMessage = errorMessage;
        }

        public void Cancel()
        {
            EndTime = DateTime.UtcNow;
            Status = TestSuiteRunStatus.Cancelled;
            ErrorMessage = "Test suite was cancelled";
        }
    }

    /// <summary>
    /// JSON deserialization classes for test suite files
    /// </summary>
    internal class TestSuiteData
    {
        public string name { get; set; }
        public List<TestSuiteEntryData> tests { get; set; }
    }

    internal class TestSuiteEntryData
    {
        public string route { get; set; }
        public List<string> dependsOn { get; set; }
    }
}
