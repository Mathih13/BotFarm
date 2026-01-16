using System;
using BotFarm.Testing;
using BotFarm.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace BotFarm.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StatusController : ControllerBase
    {
        private readonly BotFactory factory;
        private readonly TestRunCoordinator testCoordinator;
        private readonly TestSuiteCoordinator suiteCoordinator;

        public StatusController(BotFactory factory, TestRunCoordinator testCoordinator, TestSuiteCoordinator suiteCoordinator)
        {
            this.factory = factory;
            this.testCoordinator = testCoordinator;
            this.suiteCoordinator = suiteCoordinator;
        }

        /// <summary>
        /// GET /api/status - Get system status
        /// </summary>
        [HttpGet]
        public ActionResult<ApiStatusResponse> GetStatus()
        {
            return new ApiStatusResponse
            {
                Online = true,
                ActiveBots = factory.GetActiveBotCount() + testCoordinator.GetActiveTestBotCount(),
                ActiveTestRuns = testCoordinator.ActiveRuns.Count,
                ActiveSuiteRuns = suiteCoordinator.ActiveSuites.Count,
                CompletedTestRuns = testCoordinator.CompletedRuns.Count,
                CompletedSuiteRuns = suiteCoordinator.CompletedSuites.Count,
                ServerTime = DateTime.UtcNow
            };
        }
    }
}
