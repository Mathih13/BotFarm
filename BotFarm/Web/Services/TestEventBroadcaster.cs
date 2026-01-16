using System;
using BotFarm.Testing;
using BotFarm.Web.Hubs;
using BotFarm.Web.Models;
using Microsoft.AspNetCore.SignalR;

namespace BotFarm.Web.Services
{
    /// <summary>
    /// Bridges test coordinator events to SignalR for real-time UI updates
    /// </summary>
    public class TestEventBroadcaster
    {
        private readonly IHubContext<TestHub> hubContext;
        private readonly TestRunCoordinator testCoordinator;
        private readonly TestSuiteCoordinator suiteCoordinator;
        private readonly BotFactory factory;
        private bool initialized = false;

        public TestEventBroadcaster(
            IHubContext<TestHub> hubContext,
            TestRunCoordinator testCoordinator,
            TestSuiteCoordinator suiteCoordinator,
            BotFactory factory)
        {
            this.hubContext = hubContext;
            this.testCoordinator = testCoordinator;
            this.suiteCoordinator = suiteCoordinator;
            this.factory = factory;
        }

        /// <summary>
        /// Initialize event subscriptions. Call after DI container is fully built.
        /// </summary>
        public void Initialize()
        {
            if (initialized) return;
            initialized = true;

            // Test run events
            testCoordinator.TestRunStarted += OnTestRunStarted;
            testCoordinator.TestRunCompleted += OnTestRunCompleted;
            testCoordinator.TestRunStatusChanged += OnTestRunStatusChanged;
            testCoordinator.BotCompleted += OnBotCompleted;

            // Suite events
            suiteCoordinator.SuiteStarted += OnSuiteStarted;
            suiteCoordinator.SuiteCompleted += OnSuiteCompleted;
            suiteCoordinator.TestCompleted += OnSuiteTestCompleted;

            factory.Log("TestEventBroadcaster initialized");
        }

        private async void OnTestRunStarted(object sender, TestRun run)
        {
            try
            {
                var dto = ApiTestRun.FromTestRun(run, includeDetails: false);
                await hubContext.Clients.All.SendAsync("testRunStarted", dto);
            }
            catch (Exception ex)
            {
                factory.Log($"Error broadcasting testRunStarted: {ex.Message}");
            }
        }

        private async void OnTestRunCompleted(object sender, TestRun run)
        {
            try
            {
                var dto = ApiTestRun.FromTestRun(run, includeDetails: true);

                // Broadcast to all clients
                await hubContext.Clients.All.SendAsync("testRunCompleted", dto);

                // Also broadcast to group subscribed to this specific run
                await hubContext.Clients.Group($"run_{run.Id}").SendAsync("testRunCompleted", dto);
            }
            catch (Exception ex)
            {
                factory.Log($"Error broadcasting testRunCompleted: {ex.Message}");
            }
        }

        private async void OnTestRunStatusChanged(object sender, TestRun run)
        {
            try
            {
                var dto = ApiTestRun.FromTestRun(run, includeDetails: false);

                // Broadcast status update to all clients
                await hubContext.Clients.All.SendAsync("testRunStatus", dto);

                // Also broadcast to group subscribed to this specific run
                await hubContext.Clients.Group($"run_{run.Id}").SendAsync("testRunStatus", dto);
            }
            catch (Exception ex)
            {
                factory.Log($"Error broadcasting testRunStatus: {ex.Message}");
            }
        }

        private async void OnBotCompleted(object sender, (TestRun run, BotTestResult bot) args)
        {
            try
            {
                var botDto = ApiBotResult.FromBotTestResult(args.bot);

                // Broadcast to all clients
                await hubContext.Clients.All.SendAsync("botCompleted", args.run.Id, botDto);

                // Also broadcast to group subscribed to this specific run
                await hubContext.Clients.Group($"run_{args.run.Id}").SendAsync("botCompleted", args.run.Id, botDto);

                // Send progress update
                var runDto = ApiTestRun.FromTestRun(args.run, includeDetails: false);
                await hubContext.Clients.All.SendAsync("testRunStatus", runDto);
            }
            catch (Exception ex)
            {
                factory.Log($"Error broadcasting botCompleted: {ex.Message}");
            }
        }

        private async void OnSuiteStarted(object sender, TestSuiteRun suite)
        {
            try
            {
                var dto = ApiTestSuiteRun.FromTestSuiteRun(suite, includeDetails: false);
                await hubContext.Clients.All.SendAsync("suiteStarted", dto);
            }
            catch (Exception ex)
            {
                factory.Log($"Error broadcasting suiteStarted: {ex.Message}");
            }
        }

        private async void OnSuiteCompleted(object sender, TestSuiteRun suite)
        {
            try
            {
                var dto = ApiTestSuiteRun.FromTestSuiteRun(suite, includeDetails: true);

                // Broadcast to all clients
                await hubContext.Clients.All.SendAsync("suiteCompleted", dto);

                // Also broadcast to group subscribed to this specific suite
                await hubContext.Clients.Group($"suite_{suite.Id}").SendAsync("suiteCompleted", dto);
            }
            catch (Exception ex)
            {
                factory.Log($"Error broadcasting suiteCompleted: {ex.Message}");
            }
        }

        private async void OnSuiteTestCompleted(object sender, (TestSuiteRun suite, TestRun test) args)
        {
            try
            {
                var testDto = ApiTestRun.FromTestRun(args.test, includeDetails: false);

                // Broadcast to all clients
                await hubContext.Clients.All.SendAsync("suiteTestCompleted", args.suite.Id, testDto);

                // Also broadcast to group subscribed to this specific suite
                await hubContext.Clients.Group($"suite_{args.suite.Id}").SendAsync("suiteTestCompleted", args.suite.Id, testDto);

                // Send suite progress update
                var suiteDto = ApiTestSuiteRun.FromTestSuiteRun(args.suite, includeDetails: false);
                await hubContext.Clients.All.SendAsync("suiteStatus", suiteDto);
            }
            catch (Exception ex)
            {
                factory.Log($"Error broadcasting suiteTestCompleted: {ex.Message}");
            }
        }
    }
}
