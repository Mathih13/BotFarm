using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace BotFarm.Web.Hubs
{
    /// <summary>
    /// SignalR hub for real-time test updates
    /// </summary>
    public class TestHub : Hub
    {
        private readonly BotFactory factory;

        public TestHub(BotFactory factory)
        {
            this.factory = factory;
        }

        /// <summary>
        /// Subscribe to updates for a specific test run
        /// </summary>
        public async Task SubscribeToRun(string runId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"run_{runId}");
            factory.Log($"Client {Context.ConnectionId} subscribed to run {runId}");
        }

        /// <summary>
        /// Unsubscribe from updates for a specific test run
        /// </summary>
        public async Task UnsubscribeFromRun(string runId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"run_{runId}");
        }

        /// <summary>
        /// Subscribe to updates for a specific suite run
        /// </summary>
        public async Task SubscribeToSuite(string suiteId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"suite_{suiteId}");
            factory.Log($"Client {Context.ConnectionId} subscribed to suite {suiteId}");
        }

        /// <summary>
        /// Unsubscribe from updates for a specific suite run
        /// </summary>
        public async Task UnsubscribeFromSuite(string suiteId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"suite_{suiteId}");
        }

        public override async Task OnConnectedAsync()
        {
            factory.Log($"SignalR client connected: {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(System.Exception exception)
        {
            factory.Log($"SignalR client disconnected: {Context.ConnectionId}");
            await base.OnDisconnectedAsync(exception);
        }
    }
}
