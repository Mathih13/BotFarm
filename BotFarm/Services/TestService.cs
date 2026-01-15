using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BotFarm.Testing;

namespace BotFarm.Services
{
    /// <summary>
    /// Service implementation for test run and test suite management.
    /// Wraps TestRunCoordinator and TestSuiteCoordinator.
    /// </summary>
    internal class TestService : ITestService
    {
        private readonly TestRunCoordinator testCoordinator;
        private readonly TestSuiteCoordinator suiteCoordinator;

        public event EventHandler<TestRun> TestRunStarted;
        public event EventHandler<TestRun> TestRunCompleted;
        public event EventHandler<TestSuiteRun> SuiteStarted;
        public event EventHandler<TestSuiteRun> SuiteCompleted;

        public TestService(TestRunCoordinator testCoordinator, TestSuiteCoordinator suiteCoordinator)
        {
            this.testCoordinator = testCoordinator ?? throw new ArgumentNullException(nameof(testCoordinator));
            this.suiteCoordinator = suiteCoordinator ?? throw new ArgumentNullException(nameof(suiteCoordinator));

            // Forward events from coordinators
            testCoordinator.TestRunStarted += (s, run) => TestRunStarted?.Invoke(this, run);
            testCoordinator.TestRunCompleted += (s, run) => TestRunCompleted?.Invoke(this, run);
            suiteCoordinator.SuiteStarted += (s, suite) => SuiteStarted?.Invoke(this, suite);
            suiteCoordinator.SuiteCompleted += (s, suite) => SuiteCompleted?.Invoke(this, suite);
        }

        // ========== Test Run Methods ==========

        public Task<TestRun> StartTestRunAsync(string routePath, CancellationToken ct = default)
        {
            return testCoordinator.StartTestRunAsync(routePath, ct);
        }

        public bool StopTestRun(string runId)
        {
            return testCoordinator.StopTestRun(runId);
        }

        public TestRun GetTestRun(string runId)
        {
            return testCoordinator.GetTestRun(runId);
        }

        public IReadOnlyList<TestRun> GetActiveRuns()
        {
            var active = testCoordinator.ActiveRuns;
            return new List<TestRun>(active.Values);
        }

        public IReadOnlyList<TestRun> GetCompletedRuns()
        {
            return testCoordinator.CompletedRuns;
        }

        // ========== Test Suite Methods ==========

        public Task<TestSuiteRun> StartSuiteAsync(string suitePath, bool parallel = false, CancellationToken ct = default)
        {
            return suiteCoordinator.StartSuiteRunAsync(suitePath, parallel, ct);
        }

        public bool StopSuite(string suiteId)
        {
            return suiteCoordinator.StopSuiteRun(suiteId);
        }

        public TestSuiteRun GetSuiteRun(string suiteId)
        {
            return suiteCoordinator.GetSuiteRun(suiteId);
        }

        public IReadOnlyList<TestSuiteRun> GetActiveSuites()
        {
            var active = suiteCoordinator.ActiveSuites;
            return new List<TestSuiteRun>(active.Values);
        }

        public IReadOnlyList<TestSuiteRun> GetCompletedSuites()
        {
            return suiteCoordinator.CompletedSuites;
        }
    }
}
