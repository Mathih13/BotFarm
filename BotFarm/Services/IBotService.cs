using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.AI.Tasks;

namespace BotFarm.Services
{
    /// <summary>
    /// Service interface for bot lifecycle management and queries.
    /// Internal for now - will be made public when BotGame is made public.
    /// </summary>
    internal interface IBotService
    {
        // Bot lifecycle

        /// <summary>
        /// Creates a new bot with random credentials and starts it.
        /// </summary>
        BotGame CreateBot(bool startBot = true);

        /// <summary>
        /// Creates a test bot with specific username and harness settings.
        /// Used by the test framework for controlled bot creation.
        /// </summary>
        BotGame CreateTestBot(string username, HarnessSettings harness, int botIndex, bool startBot);

        /// <summary>
        /// Removes a bot from the active list and disposes it.
        /// </summary>
        void RemoveBot(BotGame bot);

        // Bot queries

        /// <summary>
        /// Gets all currently active bots.
        /// </summary>
        IReadOnlyList<BotGame> GetAllBots();

        /// <summary>
        /// Gets a bot by username (case-insensitive).
        /// Returns null if not found.
        /// </summary>
        BotGame GetBot(string username);

        /// <summary>
        /// Checks if the given GUID belongs to a bot player.
        /// </summary>
        bool IsBot(ulong guid);

        // Factory management

        /// <summary>
        /// Initializes the bot factory with the specified number of bots.
        /// Loads saved bots or creates new ones as needed.
        /// </summary>
        void Initialize(int botCount);

        /// <summary>
        /// Gracefully shuts down all bots and saves state.
        /// </summary>
        Task ShutdownAsync();

        // Events

        /// <summary>
        /// Fired when a new bot is created.
        /// </summary>
        event EventHandler<BotGame> BotCreated;

        /// <summary>
        /// Fired when a bot is removed.
        /// </summary>
        event EventHandler<BotGame> BotRemoved;
    }
}
