using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BotFarm.Testing;
using BotFarm.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace BotFarm.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestsController : ControllerBase
    {
        private readonly BotFactory factory;
        private readonly TestRunCoordinator testCoordinator;

        public TestsController(BotFactory factory, TestRunCoordinator testCoordinator)
        {
            this.factory = factory;
            this.testCoordinator = testCoordinator;
        }

        /// <summary>
        /// GET /api/tests - List all test runs (active + completed)
        /// </summary>
        [HttpGet]
        public ActionResult<List<ApiTestRun>> GetAllTests()
        {
            var results = new List<ApiTestRun>();

            // Add active runs
            foreach (var run in testCoordinator.ActiveRuns.Values)
            {
                results.Add(ApiTestRun.FromTestRun(run, includeDetails: false));
            }

            // Add completed runs
            foreach (var run in testCoordinator.CompletedRuns)
            {
                results.Add(ApiTestRun.FromTestRun(run, includeDetails: false));
            }

            // Sort by start time descending (newest first)
            return results.OrderByDescending(r => r.StartTime).ToList();
        }

        /// <summary>
        /// GET /api/tests/active - List only active test runs
        /// </summary>
        [HttpGet("active")]
        public ActionResult<List<ApiTestRun>> GetActiveTests()
        {
            return testCoordinator.ActiveRuns.Values
                .Select(r => ApiTestRun.FromTestRun(r, includeDetails: false))
                .OrderByDescending(r => r.StartTime)
                .ToList();
        }

        /// <summary>
        /// GET /api/tests/completed - List only completed test runs
        /// </summary>
        [HttpGet("completed")]
        public ActionResult<List<ApiTestRun>> GetCompletedTests()
        {
            return testCoordinator.CompletedRuns
                .Select(r => ApiTestRun.FromTestRun(r, includeDetails: false))
                .OrderByDescending(r => r.StartTime)
                .ToList();
        }

        /// <summary>
        /// GET /api/tests/{runId} - Get specific test run with full details
        /// </summary>
        [HttpGet("{runId}")]
        public ActionResult<ApiTestRun> GetTestRun(string runId)
        {
            var run = testCoordinator.GetTestRun(runId);
            if (run == null)
            {
                return NotFound(new { error = $"Test run '{runId}' not found" });
            }

            return ApiTestRun.FromTestRun(run, includeDetails: true);
        }

        /// <summary>
        /// POST /api/tests - Start a new test run
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<ApiTestRun>> StartTestRun([FromBody] StartTestRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.RoutePath))
            {
                return BadRequest(new { error = "routePath is required" });
            }

            try
            {
                // Start the test run asynchronously (fire and forget the actual execution)
                var cts = new CancellationTokenSource();
                var runTask = testCoordinator.StartTestRunAsync(request.RoutePath, cts.Token);

                // Wait briefly to get the initial test run object
                var timeout = Task.Delay(TimeSpan.FromSeconds(5));
                var completedTask = await Task.WhenAny(runTask, timeout);

                if (completedTask == timeout)
                {
                    // Test is still starting up, but we have the run in active runs
                    var activeRun = testCoordinator.ActiveRuns.Values
                        .FirstOrDefault(r => r.RoutePath == request.RoutePath);

                    if (activeRun != null)
                    {
                        return Accepted(ApiTestRun.FromTestRun(activeRun, includeDetails: false));
                    }

                    return Accepted(new { message = "Test run started", routePath = request.RoutePath });
                }

                var run = await runTask;
                return Ok(ApiTestRun.FromTestRun(run, includeDetails: false));
            }
            catch (Exception ex)
            {
                factory.Log($"Failed to start test run: {ex.Message}");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// DELETE /api/tests/{runId} - Stop/cancel a running test
        /// </summary>
        [HttpDelete("{runId}")]
        public ActionResult StopTestRun(string runId)
        {
            if (testCoordinator.StopTestRun(runId))
            {
                return Ok(new { message = $"Test run '{runId}' stopped" });
            }

            return NotFound(new { error = $"Test run '{runId}' not found or already completed" });
        }

        /// <summary>
        /// GET /api/tests/{runId}/report - Get JSON report for test run
        /// </summary>
        [HttpGet("{runId}/report")]
        public ActionResult<object> GetTestReport(string runId)
        {
            var run = testCoordinator.GetTestRun(runId);
            if (run == null)
            {
                return NotFound(new { error = $"Test run '{runId}' not found" });
            }

            // Return the full detailed test run as the report
            return ApiTestRun.FromTestRun(run, includeDetails: true);
        }
    }
}
