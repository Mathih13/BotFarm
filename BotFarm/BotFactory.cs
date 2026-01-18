using BotFarm.Properties;
using Client;
using Client.UI;
using Client.World;
using Client.World.Entities;
using Client.World.Network;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Client.AI.Tasks;
using BotFarm.Testing;
using BotFarm.Web;
using BotFarm.Web.Services;

namespace BotFarm
{
    public class BotFactory : IDisposable
    {
        public static BotFactory Instance
        {
            get;
            private set;
        }

        List<BotGame> bots = new List<BotGame>();
        RemoteAccess remoteAccess;
        DatabaseAccess databaseAccess;
        List<BotInfo> botInfos;
        const string botsInfosPath = "botsinfos.xml";
        const string logPath = "botfactory.log";
        const string defaultBehaviorName = "Default";
        TextWriter logger;
        Random randomGenerator = new Random();
        Dictionary<string, BotBehaviorSettings> botBehaviors = new Dictionary<string, BotBehaviorSettings>();
        TestRunCoordinator testCoordinator;
        TestSuiteCoordinator suiteCoordinator;
        SnapshotManager snapshotManager;
        WebApiHost webApiHost;
        UILauncher uiLauncher;

        /// <summary>
        /// In-memory buffer for log entries, accessible via Web API
        /// </summary>
        public LogBuffer Logs { get; } = new LogBuffer(maxEntries: 10000);

        // Accessors for service layer
        internal TestRunCoordinator TestCoordinator => testCoordinator;
        internal TestSuiteCoordinator SuiteCoordinator => suiteCoordinator;
        internal IReadOnlyList<BotGame> Bots => bots;

        /// <summary>
        /// Flag indicating the application should restart after disposal
        /// </summary>
        public bool RestartRequested { get; private set; }

        /// <summary>
        /// Flag indicating the application is in setup mode (paths not configured)
        /// </summary>
        public bool SetupModeRequired { get; private set; }

        // Event signaled when shutdown is requested
        private readonly ManualResetEventSlim shutdownEvent = new ManualResetEventSlim(false);

        // Store launch options for later access
        private readonly LaunchOptions launchOptions;

        public BotFactory() : this(new LaunchOptions()) { }

        public BotFactory(LaunchOptions options)
        {
            Instance = this;
            launchOptions = options ?? new LaunchOptions();

            try
            {
                logger = TextWriter.Synchronized(new StreamWriter(logPath));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create log file: {ex.Message}");
                throw;
            }
            
            logger.WriteLine("Starting BotFactory");
            logger.Flush();

            if (!File.Exists(botsInfosPath))
                botInfos = new List<BotInfo>();
            else using (StreamReader sr = new StreamReader(botsInfosPath))
            {
                try
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(List<BotInfo>));
                    botInfos = (List<BotInfo>)serializer.Deserialize(sr);
                }
                catch(InvalidOperationException)
                {
                    botInfos = new List<BotInfo>();
                }
            }

            foreach (BotBehaviorSettings behavior in Settings.Default.Behaviors)
                botBehaviors[behavior.Name] = behavior;

            if (botBehaviors.Count == 0)
            {
                Log("Behaviors not found in the configuration file, exiting");
                Environment.Exit(0);
            }

            if (!botBehaviors.ContainsKey(defaultBehaviorName))
            {
                Log("'" + defaultBehaviorName + "' behavior not found in the configuration file, exiting");
                Environment.Exit(0);
            }

            if (botBehaviors.Sum(behavior => behavior.Value.Probability) != 100)
            {
                Log("Behaviors total Probability != 100 (" + botBehaviors.Sum(behavior => behavior.Value.Probability) + "), exiting");
                Environment.Exit(0);
            }

            foreach (BotInfo botInfo in botInfos)
            {
                if (string.IsNullOrEmpty(botInfo.BehaviorName))
                {
                    Log(botInfo.Username + " has missing behavior, setting to default one");
                    botInfo.BehaviorName = defaultBehaviorName;
                    continue;
                }

                if (!botBehaviors.ContainsKey(botInfo.BehaviorName))
                {
                    Log(botInfo.Username + " has inexistent behavior '" + botInfo.BehaviorName + "', setting to default one");
                    botInfo.BehaviorName = defaultBehaviorName;
                    continue;
                }
            }

            try
            {
                // Log the config file location for debugging
                var config = System.Configuration.ConfigurationManager.OpenExeConfiguration(System.Configuration.ConfigurationUserLevel.PerUserRoamingAndLocal);
                Log($"User config file path: {config.FilePath}");
                Log($"User config file exists: {System.IO.File.Exists(config.FilePath)}");

                // Trim paths to remove any leading/trailing whitespace from App.config
                string mmapsPath = Settings.Default.MMAPsFolderPath?.Trim();
                string vmapsPath = Settings.Default.VMAPsFolderPath?.Trim();
                string mapsPath = Settings.Default.MAPsFolderPath?.Trim();
                string dbcsPath = Settings.Default.DBCsFolderPath?.Trim();

                // Check if paths are valid before attempting initialization
                bool pathsValid = ArePathsValid(mmapsPath, vmapsPath, mapsPath, dbcsPath);

                if (!pathsValid)
                {
                    Log("Data paths are not configured or invalid - running in setup mode");
                    Log("Please configure paths via the Web UI settings page");
                    SetupModeRequired = true;
                }
                else
                {
                    Log($"Raw MMAPsFolderPath from Settings.Default: '{Settings.Default.MMAPsFolderPath}'");
                    Log("Initializing Detour with path: " + mmapsPath);
                    logger.Flush();
                    DetourCLI.Detour.Initialize(mmapsPath);

                    Log("Initializing VMap with path: " + vmapsPath);
                    logger.Flush();
                    VMapCLI.VMap.Initialize(vmapsPath);

                    Log("Initializing Map with path: " + mapsPath);
                    logger.Flush();
                    MapCLI.Map.Initialize(mapsPath);

                    Log("Initializing DBCStores with path: " + dbcsPath);
                    logger.Flush();
                    DBCStoresCLI.DBCStores.Initialize(dbcsPath);

                    Log("Loading DBCs");
                    logger.Flush();
                    DBCStoresCLI.DBCStores.LoadDBCs();

                    Log("All initializations complete");
                    logger.Flush();

                    // Initialize RemoteAccess connection once for all bots
                    Log("Connecting to Remote Access for account creation");
                    remoteAccess = new RemoteAccess(Settings.Default.Hostname, Settings.Default.RAPort,
                                                     Settings.Default.Username, Settings.Default.Password);
                    if (!remoteAccess.Connect())
                    {
                        Log("Failed to connect to Remote Access - account creation will fail!");
                    }
                    else
                    {
                        Log("Remote Access connected successfully");
                    }
                    logger.Flush();

                    // Initialize MySQL database access for quest completion
                    try
                    {
                        databaseAccess = new DatabaseAccess(
                            Settings.Default.MySQLHost,
                            Settings.Default.MySQLPort,
                            Settings.Default.MySQLUser,
                            Settings.Default.MySQLPassword,
                            Settings.Default.MySQLCharactersDB
                        );
                        databaseAccess.Connect();
                        Log("MySQL database connected for quest completion");
                    }
                    catch (Exception ex)
                    {
                        Log($"MySQL connection failed (quest completion disabled): {ex.Message}");
                        databaseAccess = null;
                    }
                    logger.Flush();
                }

                // Initialize snapshot manager (requires database)
                snapshotManager = new SnapshotManager(databaseAccess);
                if (databaseAccess != null)
                {
                    snapshotManager.EnsureTablesExist();
                }

                // Initialize test coordinator
                testCoordinator = new TestRunCoordinator(this, snapshotManager);
                testCoordinator.TestRunStarted += (s, run) => Log($"Test run started: {run.Id}");
                testCoordinator.TestRunCompleted += (s, run) =>
                {
                    Log($"Test run completed: {run.Id} - Status: {run.Status}");
                    Console.WriteLine(TestReportGenerator.GenerateConsoleReport(run));
                };
                testCoordinator.BotCompleted += (s, args) =>
                    Log($"Bot {args.bot.BotName} completed: {(args.bot.Success ? "PASS" : "FAIL")}");

                // Initialize test suite coordinator
                suiteCoordinator = new TestSuiteCoordinator(this, testCoordinator);
                suiteCoordinator.SuiteStarted += (s, suite) => Log($"Test suite started: {suite.Id} - {suite.SuiteName}");
                suiteCoordinator.SuiteCompleted += (s, suite) =>
                    Log($"Test suite completed: {suite.Id} - Status: {suite.Status} ({suite.TestsPassed}/{suite.TotalTests} passed)");

                // Initialize Web API host if enabled (always start in setup mode so user can configure)
                if (Settings.Default.EnableWebUI || SetupModeRequired)
                {
                    try
                    {
                        webApiHost = new WebApiHost(this, testCoordinator, suiteCoordinator, Settings.Default.WebUIPort);
                        webApiHost.StartAsync().Wait();

                        if (SetupModeRequired)
                        {
                            Log($"Web UI available at http://localhost:{Settings.Default.WebUIPort} - please configure settings");
                        }
                    }
                    catch (Exception webEx)
                    {
                        Log($"Failed to start Web API: {webEx.Message}");
                        webApiHost = null;
                    }
                }

                // Initialize UI launcher if web UI is enabled and not running in console-only mode
                if (Settings.Default.EnableWebUI && !launchOptions.ConsoleOnly)
                {
                    try
                    {
                        uiLauncher = new UILauncher(this, Settings.Default.WebUIPort, launchOptions.DevUI, !launchOptions.NoBrowser);
                        _ = uiLauncher.StartAsync(); // Fire-and-forget, don't block startup
                    }
                    catch (Exception uiEx)
                    {
                        Log($"Failed to start UI launcher: {uiEx.Message}");
                        uiLauncher = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Failed during CLI initialization: " + ex.GetType().Name + " - " + ex.Message);
                if (ex.InnerException != null)
                    Log("Inner exception: " + ex.InnerException.Message);
                logger.Flush();
                throw;
            }
        }

        public BotGame CreateBot(bool startBot)
        {
            Log("Creating new bot");
            Console.WriteLine("CreateBot: Starting account creation via RA");

            string username = "BOT" + randomGenerator.Next();
            string password = randomGenerator.Next().ToString();
            
            Console.WriteLine($"CreateBot: Generated username={username}, password={password}");
            
            // Use the shared RemoteAccess connection with locking for thread safety
            if (remoteAccess != null)
            {
                lock (remoteAccess)
                {
                    Console.WriteLine($"CreateBot: Sending create account command for {username} with GM level 2");
                    string response = remoteAccess.SendCommand($".account create {username} {password} 2");
                    Log($"RA create account {username} (GM level 2) response: {response}");
                    Console.WriteLine($"CreateBot: RA response: {response}");
                }
            }
            else
            {
                Log("RemoteAccess is null, cannot create account!");
                Console.WriteLine("CreateBot: RemoteAccess is null!");
            }

            uint behaviorRandomIndex = (uint)randomGenerator.Next(100);
            uint behaviorCurrentIndex = 0;
            BotBehaviorSettings botBehavior = botBehaviors.Values.First();
            foreach (var behavior in botBehaviors.Values)
            {
                if (behaviorRandomIndex < behaviorCurrentIndex + behavior.Probability)
                {
                    botBehavior = behavior;
                    break;
                }

                behaviorCurrentIndex += behavior.Probability;
            }

            BotGame game = new BotGame(Settings.Default.Hostname,
                                                Settings.Default.Port,
                                                username,
                                                password,
                                                Settings.Default.RealmID,
                                                0,
                                                botBehavior);
            if (startBot)
                game.Start();
            botInfos.Add(new BotInfo(username, password, botBehavior.Name));

            return game;
        }

        public BotGame LoadBot(BotInfo info)
        {
            BotGame game = new BotGame(Settings.Default.Hostname,
                                                   Settings.Default.Port,
                                                   info.Username,
                                                   info.Password,
                                                   Settings.Default.RealmID,
                                                   0,
                                                   botBehaviors[info.BehaviorName]);
            game.Start();
            return game;
        }

        public bool IsBot(WorldObject obj)
        {
            return bots.FirstOrDefault(bot => bot.Player.GUID == obj.GUID) != null;
        }

        /// <summary>
        /// Create a bot for test harness with specific username and harness settings
        /// Uses a fixed password for test accounts to allow reuse across runs
        /// </summary>
        public BotGame CreateTestBot(string username, HarnessSettings harness, int botIndex, bool startBot)
        {
            const string testPassword = "test1234";

            Log($"Creating test bot: {username}");

            // Create account via RA with GM level 2 (will succeed if new, fail silently if exists)
            if (remoteAccess != null)
            {
                lock (remoteAccess)
                {
                    string response = remoteAccess.SendCommand($".account create {username} {testPassword} 2");
                    Log($"RA create account {username} (GM level 2) response: {response}");
                }
            }
            else
            {
                Log("RemoteAccess is null, cannot create account!", LogLevel.Warning);
            }

            BotGame game = new BotGame(Settings.Default.Hostname,
                                       Settings.Default.Port,
                                       username,
                                       testPassword,
                                       Settings.Default.RealmID,
                                       0,
                                       GetDefaultBehavior());

            // Apply harness settings
            game.SetHarnessSettings(harness, botIndex);

            if (startBot)
                game.Start();

            return game;
        }

        /// <summary>
        /// Set up a character via Remote Access commands (level, items, completed quests)
        /// Character must be offline for these commands to work
        /// </summary>
        public void SetupCharacterViaRA(string characterName, int level, List<ItemGrant> items, List<uint> completedQuests = null)
        {
            if (remoteAccess == null)
            {
                Log("RemoteAccess is null, cannot setup character via RA", LogLevel.Warning);
                return;
            }

            lock (remoteAccess)
            {
                // Set character level if > 1
                if (level > 1)
                {
                    string levelCmd = $".character level {characterName} {level}";
                    Log($"RA: Setting character {characterName} to level {level}");
                    string response = remoteAccess.SendCommand(levelCmd);
                    Log($"RA level response: {response}");
                }

                // Send items via mail (character must be offline)
                if (items != null && items.Count > 0)
                {
                    foreach (var item in items)
                    {
                        string itemCmd = $".send items {characterName} \"Test Setup\" \"Items for testing\" {item.Entry}:{item.Count}";
                        Log($"RA: Sending item {item.Entry}x{item.Count} to {characterName}");
                        string response = remoteAccess.SendCommand(itemCmd);
                        Log($"RA item response: {response}");
                    }
                }

            }

            // Complete prerequisite quests via direct MySQL (RA doesn't support quest complete for offline chars)
            if (completedQuests != null && completedQuests.Count > 0)
            {
                if (databaseAccess != null)
                {
                    Log($"Completing {completedQuests.Count} quest(s) for {characterName} via MySQL");
                    databaseAccess.CompleteQuestsForCharacter(characterName, completedQuests);
                }
                else
                {
                    Log($"Cannot complete quests for {characterName} - MySQL not connected", LogLevel.Warning);
                }
            }
        }

        /// <summary>
        /// Teleport a character to a specific position via Remote Access
        /// Character must be online for this command to work
        /// </summary>
        public void TeleportCharacterViaRA(string characterName, uint mapId, float x, float y, float z)
        {
            if (remoteAccess == null)
            {
                Log("RemoteAccess is null, cannot teleport character via RA", LogLevel.Warning);
                return;
            }

            lock (remoteAccess)
            {
                string teleportCmd = $".tele name {characterName} {mapId} {x} {y} {z}";
                Log($"RA: Teleporting {characterName} to ({x}, {y}, {z}) on map {mapId}");
                string response = remoteAccess.SendCommand(teleportCmd);
                Log($"RA teleport response: {response}");
            }
        }

        public void SetupFactory(int botCount)
        {
            Log("Setting up bot factory with " + botCount + " bots");
            Stopwatch watch = new Stopwatch();
            watch.Start();

            int createdBots = 0;
            List<BotInfo> infos;
            if (Settings.Default.RandomBots)
                infos = botInfos.TakeRandom(botCount).ToList();
            else
                infos = botInfos.Take(botCount).ToList();
            Parallel.ForEach<BotInfo>(infos, info =>
            {
                var bot = LoadBot(info);
                lock(bots)
                    bots.Add(bot);
                Interlocked.Increment(ref createdBots);
            });

            Parallel.For(createdBots, botCount, index =>
            {
                try
                {
                    var bot = CreateBot(!Settings.Default.CreateAccountOnly);
                    lock (bots)
                    {
                        bots.Add(bot);
                        if (bots.Count % 100 == 0)
                            SaveBotInfos();
                    }
                }
                catch(Exception ex)
                {
                    Log("Error creating new bot: " + ex.Message + "\n" + ex.StackTrace, LogLevel.Error);
                }
            });

            watch.Stop();
            Log("Finished setting up bot factory with " + botCount + " bots in " + watch.Elapsed);

            SaveBotInfos();

            // Start a background thread for console input
            var consoleThread = new Thread(() =>
            {
                try
                {
                    while (!shutdownEvent.IsSet)
                    {
                        string line = Console.ReadLine();
                        if (line == null)
                        {
                            // EOF - console closed
                            break;
                        }

                        string[] lineSplit = line.Split(' ');
                        switch(lineSplit[0])
                        {
                            case "quit":
                            case "exit":
                            case "close":
                            case "shutdown":
                                shutdownEvent.Set();
                                return;
                            case "info":
                            case "infos":
                            case "stats":
                            case "statistics":
                                DisplayStatistics(lineSplit.Length > 1 ? lineSplit[1] : "");
                                break;
                            case "route":
                                HandleRouteCommand(lineSplit);
                                break;
                            case "test":
                                HandleTestCommand(lineSplit);
                                break;
                            case "help":
                                DisplayHelp();
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Console not available (e.g., running without console, or debugger attached)
                    LogDebug($"Console thread stopped: {ex.Message}");
                }
            });
            consoleThread.IsBackground = true;
            consoleThread.Name = "ConsoleInput";
            consoleThread.Start();

            // Wait for shutdown signal (from console thread, web API, or external signal)
            shutdownEvent.Wait();

            if (RestartRequested)
            {
                Log("Exiting for restart...");
            }
        }

        void HandleRouteCommand(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: route <command> [bot] [args...]");
                Console.WriteLine("Commands:");
                Console.WriteLine("  route start <bot> <routefile> - Start a route for a specific bot");
                Console.WriteLine("  route stop <bot>              - Stop current route for a bot");
                Console.WriteLine("  route status [bot]            - Show route status (all bots or specific)");
                Console.WriteLine("  route startall <routefile>    - Start route for all bots");
                Console.WriteLine("  route stopall                 - Stop routes for all bots");
                return;
            }

            string command = args[1].ToLower();

            switch (command)
            {
                case "start":
                    if (args.Length < 4)
                    {
                        Console.WriteLine("Usage: route start <bot> <routefile>");
                        return;
                    }
                    var botToStart = FindBot(args[2]);
                    if (botToStart != null)
                    {
                        string routePath = args[3];
                        if (!System.IO.Path.IsPathRooted(routePath))
                        {
                            routePath = System.IO.Path.Combine("routes", routePath);
                        }
                        if (botToStart.LoadAndStartRoute(routePath))
                            Console.WriteLine($"Started route for bot {botToStart.Username}");
                        else
                            Console.WriteLine($"Failed to start route for bot {botToStart.Username}");
                    }
                    break;

                case "stop":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: route stop <bot>");
                        return;
                    }
                    var botToStop = FindBot(args[2]);
                    if (botToStop != null)
                    {
                        botToStop.StopRoute();
                        Console.WriteLine($"Stopped route for bot {botToStop.Username}");
                    }
                    break;

                case "status":
                    if (args.Length >= 3)
                    {
                        var botForStatus = FindBot(args[2]);
                        if (botForStatus != null)
                        {
                            Console.WriteLine($"{botForStatus.Username}: {botForStatus.GetRouteStatus()}");
                        }
                    }
                    else
                    {
                        foreach (var bot in bots)
                        {
                            Console.WriteLine($"{bot.Username}: {bot.GetRouteStatus()}");
                        }
                    }
                    break;

                case "startall":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: route startall <routefile>");
                        return;
                    }
                    string routePathAll = args[2];
                    if (!System.IO.Path.IsPathRooted(routePathAll))
                    {
                        routePathAll = System.IO.Path.Combine("routes", routePathAll);
                    }
                    int startedCount = 0;
                    foreach (var bot in bots)
                    {
                        if (bot.LoadAndStartRoute(routePathAll))
                            startedCount++;
                    }
                    Console.WriteLine($"Started route for {startedCount}/{bots.Count} bots");
                    break;

                case "stopall":
                    foreach (var bot in bots)
                    {
                        bot.StopRoute();
                    }
                    Console.WriteLine($"Stopped routes for all {bots.Count} bots");
                    break;

                default:
                    Console.WriteLine($"Unknown route command: {command}");
                    break;
            }
        }

        void HandleTestCommand(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: test <command> [args...]");
                Console.WriteLine("Commands:");
                Console.WriteLine("  test run <routefile>           - Start a test run with harness settings");
                Console.WriteLine("  test run-suite <suitefile> [--parallel] - Run a test suite");
                Console.WriteLine("  test status [runId]            - Show status of test runs");
                Console.WriteLine("  test suite-status [suiteId]    - Show status of suite runs");
                Console.WriteLine("  test report <runId> [json]     - Generate report for a test run");
                Console.WriteLine("  test list                      - List all test runs");
                Console.WriteLine("  test suite-list                - List all suite runs");
                Console.WriteLine("  test stop <runId>              - Stop a running test");
                Console.WriteLine("  test suite-stop <suiteId>      - Stop a running suite");
                return;
            }

            string command = args[1].ToLower();

            switch (command)
            {
                case "run":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: test run <routefile>");
                        return;
                    }
                    string routePath = args[2];
                    if (!System.IO.Path.IsPathRooted(routePath))
                    {
                        routePath = System.IO.Path.Combine("routes", routePath);
                    }
                    if (!File.Exists(routePath))
                    {
                        Console.WriteLine($"Route file not found: {routePath}");
                        return;
                    }

                    // Start the test run asynchronously
                    Task.Run(async () =>
                    {
                        try
                        {
                            var run = await testCoordinator.StartTestRunAsync(routePath);
                            Log($"Test run {run.Id} finished with status: {run.Status}");
                        }
                        catch (Exception ex)
                        {
                            Log($"Test run failed: {ex.Message}", LogLevel.Error);
                        }
                    });
                    Console.WriteLine($"Test run started for route: {routePath}");
                    break;

                case "status":
                    if (args.Length >= 3)
                    {
                        // Show status for specific run
                        var specificRun = testCoordinator.GetTestRun(args[2]);
                        if (specificRun != null)
                        {
                            DisplayTestRunStatus(specificRun);
                        }
                        else
                        {
                            Console.WriteLine($"Test run '{args[2]}' not found");
                        }
                    }
                    else
                    {
                        // Show all active runs
                        var activeRuns = testCoordinator.ActiveRuns;
                        if (activeRuns.Count == 0)
                        {
                            Console.WriteLine("No active test runs");
                        }
                        else
                        {
                            Console.WriteLine($"Active test runs: {activeRuns.Count}");
                            foreach (var kvp in activeRuns)
                            {
                                DisplayTestRunStatus(kvp.Value);
                            }
                        }
                    }
                    break;

                case "report":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: test report <runId> [json]");
                        return;
                    }
                    var runForReport = testCoordinator.GetTestRun(args[2]);
                    if (runForReport == null)
                    {
                        Console.WriteLine($"Test run '{args[2]}' not found");
                        return;
                    }
                    bool jsonFormat = args.Length > 3 && args[3].ToLower() == "json";
                    if (jsonFormat)
                    {
                        Console.WriteLine(TestReportGenerator.GenerateJsonReport(runForReport));
                    }
                    else
                    {
                        Console.WriteLine(TestReportGenerator.GenerateConsoleReport(runForReport));
                    }
                    break;

                case "list":
                    Console.WriteLine("=== Active Test Runs ===");
                    foreach (var kvp in testCoordinator.ActiveRuns)
                    {
                        Console.WriteLine($"  {kvp.Key}: {kvp.Value.RouteName ?? kvp.Value.RoutePath} - {kvp.Value.Status}");
                    }
                    Console.WriteLine("=== Completed Test Runs ===");
                    foreach (var completedRun in testCoordinator.CompletedRuns)
                    {
                        string statusSymbol = completedRun.Status == TestRunStatus.Completed ? "[OK]" : "[X]";
                        Console.WriteLine($"  {completedRun.Id}: {completedRun.RouteName ?? completedRun.RoutePath} - {statusSymbol} {completedRun.Status}");
                    }
                    break;

                case "stop":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: test stop <runId>");
                        return;
                    }
                    if (testCoordinator.StopTestRun(args[2]))
                    {
                        Console.WriteLine($"Stopping test run {args[2]}");
                    }
                    else
                    {
                        Console.WriteLine($"Test run '{args[2]}' not found or already completed");
                    }
                    break;

                // ========== Test Suite Commands ==========
                case "run-suite":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: test run-suite <suitefile> [--parallel]");
                        return;
                    }
                    string suitePath = args[2];
                    if (!System.IO.Path.IsPathRooted(suitePath))
                    {
                        suitePath = System.IO.Path.Combine("routes", "suites", suitePath);
                    }
                    if (!File.Exists(suitePath))
                    {
                        // Try without suites subdirectory
                        suitePath = System.IO.Path.Combine("routes", args[2]);
                        if (!File.Exists(suitePath))
                        {
                            Console.WriteLine($"Suite file not found: {args[2]}");
                            return;
                        }
                    }
                    bool parallel = args.Length > 3 && args[3].ToLower() == "--parallel";

                    Task.Run(async () =>
                    {
                        try
                        {
                            var suite = await suiteCoordinator.StartSuiteRunAsync(suitePath, parallel);
                            Log($"Test suite {suite.Id} finished with status: {suite.Status}");
                        }
                        catch (Exception ex)
                        {
                            Log($"Test suite failed: {ex.Message}", LogLevel.Error);
                        }
                    });
                    Console.WriteLine($"Test suite started: {suitePath}" + (parallel ? " (parallel mode)" : ""));
                    break;

                case "suite-status":
                    if (args.Length >= 3)
                    {
                        var specificSuite = suiteCoordinator.GetSuiteRun(args[2]);
                        if (specificSuite != null)
                        {
                            DisplaySuiteRunStatus(specificSuite);
                        }
                        else
                        {
                            Console.WriteLine($"Suite run '{args[2]}' not found");
                        }
                    }
                    else
                    {
                        var activeSuites = suiteCoordinator.ActiveSuites;
                        if (activeSuites.Count == 0)
                        {
                            Console.WriteLine("No active suite runs");
                        }
                        else
                        {
                            Console.WriteLine($"Active suite runs: {activeSuites.Count}");
                            foreach (var kvp in activeSuites)
                            {
                                DisplaySuiteRunStatus(kvp.Value);
                            }
                        }
                    }
                    break;

                case "suite-list":
                    Console.WriteLine("=== Active Suite Runs ===");
                    foreach (var kvp in suiteCoordinator.ActiveSuites)
                    {
                        Console.WriteLine($"  {kvp.Key}: {kvp.Value.SuiteName} - {kvp.Value.Status} ({kvp.Value.TestRuns.Count}/{kvp.Value.TotalTests} tests)");
                    }
                    Console.WriteLine("=== Completed Suite Runs ===");
                    foreach (var completedSuite in suiteCoordinator.CompletedSuites)
                    {
                        string statusSymbol = completedSuite.Status == TestSuiteRunStatus.Completed ? "[OK]" : "[X]";
                        Console.WriteLine($"  {completedSuite.Id}: {completedSuite.SuiteName} - {statusSymbol} {completedSuite.Status} ({completedSuite.TestsPassed}/{completedSuite.TotalTests} passed)");
                    }
                    break;

                case "suite-stop":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: test suite-stop <suiteId>");
                        return;
                    }
                    if (suiteCoordinator.StopSuiteRun(args[2]))
                    {
                        Console.WriteLine($"Stopping suite run {args[2]}");
                    }
                    else
                    {
                        Console.WriteLine($"Suite run '{args[2]}' not found or already completed");
                    }
                    break;

                default:
                    Console.WriteLine($"Unknown test command: {command}");
                    break;
            }
        }

        void DisplaySuiteRunStatus(TestSuiteRun suite)
        {
            Console.WriteLine($"Suite Run: {suite.Id}");
            Console.WriteLine($"  Name: {suite.SuiteName}");
            Console.WriteLine($"  Status: {suite.Status}");
            Console.WriteLine($"  Mode: {(suite.ParallelMode ? "Parallel" : "Sequential")}");
            Console.WriteLine($"  Tests: {suite.TestRuns.Count}/{suite.TotalTests}");
            Console.WriteLine($"  Passed: {suite.TestsPassed}, Failed: {suite.TestsFailed}, Skipped: {suite.TestsSkipped}");
            Console.WriteLine($"  Duration: {suite.Duration.TotalSeconds:F1}s");
        }

        void DisplayTestRunStatus(TestRun run)
        {
            Console.WriteLine($"Test Run: {run.Id}");
            Console.WriteLine($"  Route: {run.RouteName ?? run.RoutePath}");
            Console.WriteLine($"  Status: {run.Status}");
            Console.WriteLine($"  Duration: {FormatDuration(run.Duration)}");
            Console.WriteLine($"  Bots: {run.BotsCompleted}/{run.BotResults.Count} completed, {run.BotsPassed} passed, {run.BotsFailed} failed");
        }

        static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m";
            if (duration.TotalMinutes >= 1)
                return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
            return $"{duration.Seconds}.{duration.Milliseconds / 100}s";
        }

        BotGame FindBot(string botName)
        {
            var bot = bots.FirstOrDefault(b => b.Username.Equals(botName, StringComparison.InvariantCultureIgnoreCase));
            if (bot == null)
                Console.WriteLine($"Bot with username '{botName}' not found");
            return bot;
        }

        void DisplayHelp()
        {
            Console.WriteLine("Available commands:");
            Console.WriteLine("  info / stats [bot]    - Display bot statistics");
            Console.WriteLine("  route <command>       - Manage bot task routes");
            Console.WriteLine("  test <command>        - Run tests with harness settings");
            Console.WriteLine("  help                  - Show this help");
            Console.WriteLine("  quit / exit           - Shutdown BotFarm");
        }

        void DisplayStatistics(string botname)
        {
            if (String.IsNullOrEmpty(botname))
            {
                foreach (var bot in bots)
                    DisplayStatistics(bot);

                // Display stats about all bots
                Console.WriteLine(bots.Where(bot => bot.Running).Count() + " bots are active");
                Console.WriteLine(bots.Where(bot => bot.Connected).Count() + " bots are connected");
                Console.WriteLine(bots.Where(bot => bot.LoggedIn).Count() + " bots are ingame");
            }
            else
            {
                // Display stats about a single bot
                var bot = bots.SingleOrDefault(b => b.Username.Equals(botname, StringComparison.InvariantCultureIgnoreCase));
                if (bot == null)
                    Console.WriteLine("Bot with username '" + botname + "' not found");
                else
                    DisplayStatistics(bot);
            }
        }

        void DisplayStatistics(BotGame bot)
        {
            Console.WriteLine("Bot username: " + bot.Username);
            Console.WriteLine("\tBehavior: " + bot.Behavior.Name);
            Console.WriteLine("\tRunning: " + bot.Running);
            Console.WriteLine("\tConnected: " + bot.Connected);
            Console.WriteLine("\tLogged In: " + bot.LoggedIn);
            Console.WriteLine("\tPosition: " + bot.Player.GetPosition());
            if (bot.GroupLeaderGuid == 0)
                Console.WriteLine("\tGroup Leader: " + "Not in group");
            else if (!bot.World.PlayerNameLookup.ContainsKey(bot.GroupLeaderGuid))
                Console.WriteLine("\tGroup Leader: " + "Not found");
            else
                Console.WriteLine("\tGroup Leader: " + bot.World.PlayerNameLookup[bot.GroupLeaderGuid]);
            Console.WriteLine("\tLast Received Packet: " + bot.LastReceivedPacket);
            Console.WriteLine("\tLast Received Packet Time: " + bot.LastReceivedPacketTime.ToLongTimeString());
            Console.WriteLine("\tLast Sent Packet: " + bot.LastSentPacket);
            Console.WriteLine("\tLast Sent Packet Time: " + bot.LastSentPacketTime.ToLongTimeString());
            Console.WriteLine("\tLast Update() call: " + bot.LastUpdate.ToLongTimeString());
            Console.WriteLine("\tSchedule Actions: " + bot.ScheduledActionsCount);
        }

        public void Dispose()
        {
            Log("Shutting down BotFactory");
            Log("This might take at least 20 seconds to allow all bots to properly logout");

            // Stop UI launcher first
            if (uiLauncher != null)
            {
                try
                {
                    uiLauncher.Dispose();
                }
                catch { }
                uiLauncher = null;
            }

            // Stop web API
            if (webApiHost != null)
            {
                try
                {
                    webApiHost.StopAsync().Wait(TimeSpan.FromSeconds(5));
                    webApiHost.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
                }
                catch { }
                webApiHost = null;
            }

            List<Task> botsDisposing = new List<Task>(bots.Count);
            foreach (var bot in bots)
                botsDisposing.Add(bot.DisposeAsync().AsTask());

            Task.WaitAll(botsDisposing.ToArray(), new TimeSpan(0, 2, 0));

            if (remoteAccess != null)
            {
                remoteAccess.Dispose();
                remoteAccess = null;
            }

            if (databaseAccess != null)
            {
                databaseAccess.Dispose();
                databaseAccess = null;
            }

            SaveBotInfos();

            logger.Dispose();
            logger = null;

            shutdownEvent.Dispose();
        }

        private void SaveBotInfos()
        {
            using (StreamWriter sw = new StreamWriter(botsInfosPath))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(List<BotInfo>));
                serializer.Serialize(sw, botInfos);
            }
        }

        [Conditional("DEBUG")]
        public void LogDebug(string message)
        {
            Log(message, LogLevel.Debug);
        }

        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            try
            {
#if !DEBUG_LOG
                if (level > LogLevel.Debug)
#endif
                {
                    Console.WriteLine(message);
                    logger.WriteLine(message);
                }

                // Always add to log buffer for Web API access
                Logs.Add(message, level);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Check if a GUID belongs to a bot player
        /// </summary>
        public bool IsBot(ulong guid)
        {
            lock (bots)
            {
                return bots.Any(b => b.Player?.GUID == guid);
            }
        }

        /// <summary>
        /// Get the default behavior settings
        /// </summary>
        public BotBehaviorSettings GetDefaultBehavior()
        {
            return botBehaviors.ContainsKey(defaultBehaviorName)
                ? botBehaviors[defaultBehaviorName]
                : botBehaviors.Values.First();
        }

        /// <summary>
        /// Get count of active (running) bots for status API
        /// </summary>
        public int GetActiveBotCount()
        {
            lock (bots)
            {
                return bots.Count(b => b.Running);
            }
        }

        public void RemoveBot(BotGame bot)
        {
            lock (bots)
            {
                botInfos.Remove(botInfos.Single(info => info.Username == bot.Username && info.Password == bot.Password));
                bots.Remove(bot);
            }

            bot.DisposeAsync().AsTask().Wait();
        }

        /// <summary>
        /// Create a ServiceContainer for this factory.
        /// Services provide a clean API for external consumers.
        /// </summary>
        internal Services.ServiceContainer CreateServiceContainer()
        {
            return new Services.ServiceContainer(this, testCoordinator, suiteCoordinator);
        }

        /// <summary>
        /// Request a restart of the application after disposal.
        /// This sets a flag and signals the main loop to exit.
        /// </summary>
        public void RequestRestart()
        {
            Log("Restart requested - shutting down for restart");
            RestartRequested = true;
            shutdownEvent.Set();
        }

        /// <summary>
        /// Wait for shutdown signal or console input
        /// </summary>
        public void WaitForShutdown()
        {
            shutdownEvent.Wait();
        }

        /// <summary>
        /// Signal shutdown without restart
        /// </summary>
        public void SignalShutdown()
        {
            shutdownEvent.Set();
        }

        /// <summary>
        /// Check if data paths are valid (not default and directories exist)
        /// </summary>
        private static bool ArePathsValid(string mmapsPath, string vmapsPath, string mapsPath, string dbcsPath)
        {
            return IsPathValid(mmapsPath) &&
                   IsPathValid(vmapsPath) &&
                   IsPathValid(mapsPath) &&
                   IsPathValid(dbcsPath);
        }

        /// <summary>
        /// Check if a single path is valid (not default and directory exists)
        /// </summary>
        private static bool IsPathValid(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string trimmed = path.Trim();

            // Check for default/placeholder values
            if (trimmed == "C:\\" || trimmed == "C:/" || trimmed == "C:" || trimmed.Length <= 3)
                return false;

            // Check if directory exists
            return Directory.Exists(trimmed);
        }
    }
}
