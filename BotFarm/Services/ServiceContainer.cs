using System;
using BotFarm.Testing;

namespace BotFarm.Services
{
    /// <summary>
    /// Container for all BotFarm services.
    /// Manages service lifetime and provides access to service instances.
    /// </summary>
    internal class ServiceContainer : IDisposable
    {
        private readonly BotFactory factory;
        private readonly BotService botService;
        private readonly TestService testService;
        private readonly RouteService routeService;
        private bool disposed;

        /// <summary>
        /// Bot lifecycle and query service.
        /// </summary>
        public IBotService Bots => botService;

        /// <summary>
        /// Test run and suite management service.
        /// </summary>
        public ITestService Tests => testService;

        /// <summary>
        /// Route discovery and execution service.
        /// </summary>
        public IRouteService Routes => routeService;

        /// <summary>
        /// Creates a new service container.
        /// Initializes all services with the shared BotFactory instance.
        /// </summary>
        /// <param name="factory">The BotFactory instance (must already be initialized)</param>
        /// <param name="testCoordinator">The TestRunCoordinator instance</param>
        /// <param name="suiteCoordinator">The TestSuiteCoordinator instance</param>
        public ServiceContainer(
            BotFactory factory,
            TestRunCoordinator testCoordinator,
            TestSuiteCoordinator suiteCoordinator)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));

            // Create service instances
            botService = new BotService(factory);
            testService = new TestService(testCoordinator, suiteCoordinator);
            routeService = new RouteService(factory);
        }

        /// <summary>
        /// Gets the underlying BotFactory.
        /// For internal use when direct factory access is needed.
        /// </summary>
        internal BotFactory Factory => factory;

        /// <summary>
        /// Gets the BotService instance (for internal registration).
        /// </summary>
        internal BotService BotServiceInternal => botService;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            // Services don't own factory - factory is disposed separately
            // Just mark as disposed to prevent further use
        }
    }
}
