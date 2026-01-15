using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BotFarm.Testing;

namespace BotFarm.Services
{
    /// <summary>
    /// Service interface for test run and test suite management.
    /// Internal for now - will be made public when TestRun/TestSuiteRun are made public.
    /// </summary>
    internal interface ITestService
    {
        // Test runs

        /// <summary>
        /// Starts a test run for the specified route file.
        /// The route must have a harness section defining bot requirements.
        /// </summary>
        Task<TestRun> StartTestRunAsync(string routePath, CancellationToken ct = default);

        /// <summary>
        /// Stops a running test by its ID.
        /// Returns true if the test was found and stopped.
        /// </summary>
        bool StopTestRun(string runId);

        /// <summary>
        /// Gets a test run by ID (active or completed).
        /// Returns null if not found.
        /// </summary>
        TestRun GetTestRun(string runId);

        /// <summary>
        /// Gets all currently running test runs.
        /// </summary>
        IReadOnlyList<TestRun> GetActiveRuns();

        /// <summary>
        /// Gets all completed test runs (success, failed, or timed out).
        /// </summary>
        IReadOnlyList<TestRun> GetCompletedRuns();

        // Test suites

        /// <summary>
        /// Starts a test suite from the specified suite file.
        /// Executes tests in dependency order, optionally in parallel.
        /// </summary>
        Task<TestSuiteRun> StartSuiteAsync(string suitePath, bool parallel = false, CancellationToken ct = default);

        /// <summary>
        /// Stops a running test suite by its ID.
        /// Returns true if the suite was found and stopped.
        /// </summary>
        bool StopSuite(string suiteId);

        /// <summary>
        /// Gets a test suite run by ID (active or completed).
        /// Returns null if not found.
        /// </summary>
        TestSuiteRun GetSuiteRun(string suiteId);

        /// <summary>
        /// Gets all currently running test suites.
        /// </summary>
        IReadOnlyList<TestSuiteRun> GetActiveSuites();

        /// <summary>
        /// Gets all completed test suites.
        /// </summary>
        IReadOnlyList<TestSuiteRun> GetCompletedSuites();

        // Events

        /// <summary>
        /// Fired when a test run starts execution.
        /// </summary>
        event EventHandler<TestRun> TestRunStarted;

        /// <summary>
        /// Fired when a test run completes (success, failure, or timeout).
        /// </summary>
        event EventHandler<TestRun> TestRunCompleted;

        /// <summary>
        /// Fired when a test suite starts execution.
        /// </summary>
        event EventHandler<TestSuiteRun> SuiteStarted;

        /// <summary>
        /// Fired when a test suite completes.
        /// </summary>
        event EventHandler<TestSuiteRun> SuiteCompleted;
    }
}
