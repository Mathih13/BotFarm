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
    public class SuitesController : ControllerBase
    {
        private readonly BotFactory factory;
        private readonly TestSuiteCoordinator suiteCoordinator;

        public SuitesController(BotFactory factory, TestSuiteCoordinator suiteCoordinator)
        {
            this.factory = factory;
            this.suiteCoordinator = suiteCoordinator;
        }

        /// <summary>
        /// GET /api/suites - List all suite runs (active + completed)
        /// </summary>
        [HttpGet]
        public ActionResult<List<ApiTestSuiteRun>> GetAllSuites()
        {
            var results = new List<ApiTestSuiteRun>();

            // Add active suites
            foreach (var suite in suiteCoordinator.ActiveSuites.Values)
            {
                results.Add(ApiTestSuiteRun.FromTestSuiteRun(suite, includeDetails: false));
            }

            // Add completed suites
            foreach (var suite in suiteCoordinator.CompletedSuites)
            {
                results.Add(ApiTestSuiteRun.FromTestSuiteRun(suite, includeDetails: false));
            }

            // Sort by start time descending (newest first)
            return results.OrderByDescending(r => r.StartTime).ToList();
        }

        /// <summary>
        /// GET /api/suites/active - List only active suite runs
        /// </summary>
        [HttpGet("active")]
        public ActionResult<List<ApiTestSuiteRun>> GetActiveSuites()
        {
            return suiteCoordinator.ActiveSuites.Values
                .Select(s => ApiTestSuiteRun.FromTestSuiteRun(s, includeDetails: false))
                .OrderByDescending(s => s.StartTime)
                .ToList();
        }

        /// <summary>
        /// GET /api/suites/completed - List only completed suite runs
        /// </summary>
        [HttpGet("completed")]
        public ActionResult<List<ApiTestSuiteRun>> GetCompletedSuites()
        {
            return suiteCoordinator.CompletedSuites
                .Select(s => ApiTestSuiteRun.FromTestSuiteRun(s, includeDetails: false))
                .OrderByDescending(s => s.StartTime)
                .ToList();
        }

        /// <summary>
        /// GET /api/suites/{suiteId} - Get specific suite run with full details
        /// </summary>
        [HttpGet("{suiteId}")]
        public ActionResult<ApiTestSuiteRun> GetSuiteRun(string suiteId)
        {
            var suite = suiteCoordinator.GetSuiteRun(suiteId);
            if (suite == null)
            {
                return NotFound(new { error = $"Suite run '{suiteId}' not found" });
            }

            return ApiTestSuiteRun.FromTestSuiteRun(suite, includeDetails: true);
        }

        /// <summary>
        /// POST /api/suites - Start a new suite run
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<ApiTestSuiteRun>> StartSuiteRun([FromBody] StartSuiteRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.SuitePath))
            {
                return BadRequest(new { error = "suitePath is required" });
            }

            try
            {
                // Start the suite run (fire and forget)
                var cts = new CancellationTokenSource();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await suiteCoordinator.StartSuiteRunAsync(request.SuitePath, request.Parallel, cts.Token);
                    }
                    catch (Exception ex)
                    {
                        factory.Log($"Suite run failed: {ex.Message}");
                    }
                });

                // Wait briefly for the suite to appear in active suites
                await Task.Delay(500);

                var activeSuite = suiteCoordinator.ActiveSuites.Values
                    .FirstOrDefault(s => s.SuitePath == request.SuitePath);

                if (activeSuite != null)
                {
                    return Accepted(ApiTestSuiteRun.FromTestSuiteRun(activeSuite, includeDetails: false));
                }

                return Accepted(new { message = "Suite run started", suitePath = request.SuitePath, parallel = request.Parallel });
            }
            catch (Exception ex)
            {
                factory.Log($"Failed to start suite run: {ex.Message}");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// DELETE /api/suites/{suiteId} - Stop/cancel a running suite
        /// </summary>
        [HttpDelete("{suiteId}")]
        public ActionResult StopSuiteRun(string suiteId)
        {
            if (suiteCoordinator.StopSuiteRun(suiteId))
            {
                return Ok(new { message = $"Suite run '{suiteId}' stopped" });
            }

            return NotFound(new { error = $"Suite run '{suiteId}' not found or already completed" });
        }
    }
}
