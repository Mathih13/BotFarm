using BotFarm.Properties;
using Client;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotFarm
{
    class Program
    {
        static void Main(string[] args)
        {
            bool restartRequested = false;
            Console.WriteLine("BotFarm starting...");
            Console.WriteLine($"Current directory: {Environment.CurrentDirectory}");

            // Parse command-line options
            var options = LaunchOptions.Parse(args);

            if (options.ConsoleOnly)
                Console.WriteLine("Running in console-only mode (--no-ui)");
            if (options.DevUI)
                Console.WriteLine("Using development UI server (--dev-ui)");
            if (options.NoBrowser)
                Console.WriteLine("Browser auto-open disabled (--no-browser)");

            try
            {
                Console.WriteLine("Creating BotFactory...");
                Console.WriteLine("Flushing console...");
                Console.Out.Flush();

                using (BotFactory factory = new BotFactory(options))
                {
                    int botCount;
                    if (options.AutoMode)
                    {
                        Random random = new Random();
                        botCount = random.Next(Settings.Default.MinBotsCount, Settings.Default.MaxBotsCount);
                        Console.WriteLine($"AUTO MODE - spawning {botCount} bots...");
                    }
                    else
                    {
                        Console.WriteLine("Ready. Use 'test run <routefile>' to start a test run");
                        Console.WriteLine("Use --auto flag to auto-spawn bots on startup");
                        botCount = 0;
                    }

                    factory.SetupFactory(botCount);
                    GC.KeepAlive(factory);

                    // Capture restart flag before disposal
                    restartRequested = factory.RestartRequested;
                }

                // Handle restart after factory is disposed
                if (restartRequested)
                {
                    Console.WriteLine("Restarting application...");
                    var processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(processPath))
                    {
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = processPath,
                            Arguments = string.Join(" ", args),
                            UseShellExecute = true
                        };
                        Process.Start(startInfo);
                    }
                    return;
                }
            }
            catch(UnauthorizedAccessException ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine("Try running the application as Administrator or check if the files have the Read-Only flag set");
                Console.ReadLine();
            }
            catch(ConfigurationException ex)
            {
                Console.WriteLine(ex.Message);
                if (ex.InnerException != null)
                    Console.WriteLine(ex.InnerException.Message);
                Console.ReadLine();
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Unhandled exception: {ex.GetType().Name}");
                Console.WriteLine($"Message: {ex.Message}");
                Console.WriteLine($"Stack trace:\n{ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"\nInner exception: {ex.InnerException.GetType().Name}");
                    Console.WriteLine($"Message: {ex.InnerException.Message}");
                }
                Console.ReadLine();
            }
        }
    }
}
