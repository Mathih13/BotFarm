using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.AI.Tasks;

namespace BotFarm.Services
{
    /// <summary>
    /// Service implementation for bot lifecycle management.
    /// Wraps BotFactory functionality for the service layer.
    /// </summary>
    internal class BotService : IBotService
    {
        private readonly BotFactory factory;
        private readonly List<BotGame> bots = new List<BotGame>();
        private readonly object botsLock = new object();

        public event EventHandler<BotGame> BotCreated;
        public event EventHandler<BotGame> BotRemoved;

        public BotService(BotFactory factory)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public BotGame CreateBot(bool startBot = true)
        {
            var bot = factory.CreateBot(startBot);
            AddBot(bot);
            return bot;
        }

        public BotGame CreateTestBot(string username, HarnessSettings harness, int botIndex, bool startBot)
        {
            var bot = factory.CreateTestBot(username, harness, botIndex, startBot);
            AddBot(bot);
            return bot;
        }

        public void RemoveBot(BotGame bot)
        {
            if (bot == null) return;

            lock (botsLock)
            {
                bots.Remove(bot);
            }

            factory.RemoveBot(bot);
            BotRemoved?.Invoke(this, bot);
        }

        public IReadOnlyList<BotGame> GetAllBots()
        {
            lock (botsLock)
            {
                return bots.ToList().AsReadOnly();
            }
        }

        public BotGame GetBot(string username)
        {
            if (string.IsNullOrEmpty(username)) return null;

            lock (botsLock)
            {
                return bots.FirstOrDefault(b =>
                    b.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            }
        }

        public bool IsBot(ulong guid)
        {
            lock (botsLock)
            {
                return bots.Any(b => b.Player?.GUID == guid);
            }
        }

        public void Initialize(int botCount)
        {
            // This delegates to factory's SetupFactory but without the command loop
            // For now, we use factory's existing initialization
            // Future: Extract bot loading logic here
            factory.Log($"BotService: Initializing with {botCount} bots");
        }

        public async Task ShutdownAsync()
        {
            List<BotGame> botsToDispose;
            lock (botsLock)
            {
                botsToDispose = bots.ToList();
                bots.Clear();
            }

            var disposeTasks = botsToDispose.Select(b => b.DisposeAsync().AsTask());
            await Task.WhenAll(disposeTasks);
        }

        private void AddBot(BotGame bot)
        {
            lock (botsLock)
            {
                bots.Add(bot);
            }
            BotCreated?.Invoke(this, bot);
        }

        /// <summary>
        /// Register an externally created bot with this service.
        /// Used when bots are created through BotFactory directly.
        /// </summary>
        internal void RegisterBot(BotGame bot)
        {
            AddBot(bot);
        }
    }
}
