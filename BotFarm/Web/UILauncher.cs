using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Client.UI;

namespace BotFarm.Web
{
    /// <summary>
    /// Manages the lifecycle of the botfarm-ui web frontend process
    /// </summary>
    internal class UILauncher : IDisposable
    {
        private Process uiProcess;
        private readonly BotFactory factory;
        private readonly int backendPort;
        private readonly bool isDevelopment;
        private readonly bool openBrowser;
        private readonly string uiProjectPath;
        private bool disposed;

        // Port numbers
        private const int DevServerPort = 5173;
        private const int ProductionServerPort = 3000;

        public UILauncher(BotFactory factory, int backendPort, bool isDevelopment, bool openBrowser)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.backendPort = backendPort;
            this.isDevelopment = isDevelopment;
            this.openBrowser = openBrowser;
            this.uiProjectPath = FindUIProjectPath();
        }

        /// <summary>
        /// Find the botfarm-ui project directory relative to the executable
        /// </summary>
        private string FindUIProjectPath()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;

            // Option 1: Packaged release - botfarm-ui is next to exe
            string releasePath = Path.Combine(exeDir, "botfarm-ui");
            if (Directory.Exists(releasePath))
                return releasePath;

            // Option 2: Development - exe is in BotFarm/bin/x64/Debug|Release
            // Navigate up 4 levels to repo root, then into botfarm-ui
            string devPath = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "botfarm-ui"));
            if (Directory.Exists(devPath))
                return devPath;

            // Option 3: Development - exe is in BotFarm/bin/Debug|Release (without platform folder)
            devPath = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "botfarm-ui"));
            if (Directory.Exists(devPath))
                return devPath;

            return null;
        }

        /// <summary>
        /// Start the UI server (dev or production mode)
        /// </summary>
        public Task StartAsync()
        {
            if (string.IsNullOrEmpty(uiProjectPath))
            {
                factory.Log("UILauncher: botfarm-ui project not found. UI will not be started.", LogLevel.Warning);
                factory.Log($"UILauncher: Searched paths relative to: {AppDomain.CurrentDomain.BaseDirectory}", LogLevel.Debug);
                return Task.CompletedTask;
            }

            factory.Log($"UILauncher: Found UI project at {uiProjectPath}");

            try
            {
                if (isDevelopment)
                {
                    StartDevServer();
                }
                else
                {
                    StartProductionServer();
                }

                if (openBrowser && uiProcess != null && !uiProcess.HasExited)
                {
                    _ = OpenBrowserWhenReadyAsync();
                }
            }
            catch (Exception ex)
            {
                factory.Log($"UILauncher: Failed to start UI server: {ex.Message}", LogLevel.Error);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Start the Vite development server (npm run dev)
        /// </summary>
        private void StartDevServer()
        {
            factory.Log("UILauncher: Starting Vite dev server...");

            // Check if npm is available
            string npmPath = FindNpmPath();
            if (string.IsNullOrEmpty(npmPath))
            {
                factory.Log("UILauncher: npm not found. Please ensure Node.js is installed and in PATH.", LogLevel.Error);
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = npmPath,
                Arguments = "run dev",
                WorkingDirectory = uiProjectPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Pass backend URL to the dev server
            startInfo.Environment["VITE_BACKEND_URL"] = $"http://localhost:{backendPort}";

            uiProcess = new Process { StartInfo = startInfo };

            // Capture output for logging
            uiProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    factory.LogDebug($"[UI] {e.Data}");
            };
            uiProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    factory.LogDebug($"[UI-ERR] {e.Data}");
            };

            uiProcess.Start();
            uiProcess.BeginOutputReadLine();
            uiProcess.BeginErrorReadLine();

            factory.Log($"UILauncher: Dev server starting on http://localhost:{DevServerPort}");
        }

        /// <summary>
        /// Start the production Nitro server (node .output/server/index.mjs)
        /// </summary>
        private void StartProductionServer()
        {
            string outputPath = Path.Combine(uiProjectPath, ".output", "server", "index.mjs");

            if (!File.Exists(outputPath))
            {
                factory.Log("UILauncher: Production build not found. Run 'npm run build' in botfarm-ui first.", LogLevel.Warning);
                factory.Log($"UILauncher: Expected: {outputPath}", LogLevel.Debug);

                // Fall back to dev mode if --dev-ui wasn't specified but no production build exists
                factory.Log("UILauncher: Falling back to development server...", LogLevel.Info);
                StartDevServer();
                return;
            }

            factory.Log("UILauncher: Starting production server...");

            // Find Node.js
            string nodePath = FindNodePath();
            if (string.IsNullOrEmpty(nodePath))
            {
                factory.Log("UILauncher: node not found. Please ensure Node.js is installed and in PATH.", LogLevel.Error);
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = nodePath,
                Arguments = ".output/server/index.mjs",
                WorkingDirectory = uiProjectPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Set environment for production
            startInfo.Environment["NODE_ENV"] = "production";
            startInfo.Environment["NITRO_PORT"] = ProductionServerPort.ToString();

            uiProcess = new Process { StartInfo = startInfo };

            // Capture output for logging
            uiProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    factory.LogDebug($"[UI] {e.Data}");
            };
            uiProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    factory.LogDebug($"[UI-ERR] {e.Data}");
            };

            uiProcess.Start();
            uiProcess.BeginOutputReadLine();
            uiProcess.BeginErrorReadLine();

            factory.Log($"UILauncher: Production server starting on http://localhost:{ProductionServerPort}");
        }

        /// <summary>
        /// Poll the server URL and open browser when ready
        /// </summary>
        private async Task OpenBrowserWhenReadyAsync()
        {
            int port = isDevelopment ? DevServerPort : ProductionServerPort;
            string url = $"http://localhost:{port}";

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(2);

            int maxRetries = 30; // 30 seconds max wait
            for (int i = 0; i < maxRetries; i++)
            {
                if (disposed || uiProcess == null || uiProcess.HasExited)
                    return;

                try
                {
                    var response = await httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        factory.Log($"UILauncher: Server ready, opening browser at {url}");
                        OpenBrowser(url);
                        return;
                    }
                }
                catch
                {
                    // Server not ready yet
                }

                await Task.Delay(1000);
            }

            factory.Log("UILauncher: Timeout waiting for server to be ready", LogLevel.Warning);
        }

        /// <summary>
        /// Open the default browser with the specified URL
        /// </summary>
        private void OpenBrowser(string url)
        {
            try
            {
                // Use shell execute to open the default browser
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                factory.Log($"UILauncher: Failed to open browser: {ex.Message}", LogLevel.Warning);
            }
        }

        /// <summary>
        /// Find npm.cmd on Windows
        /// </summary>
        private string FindNpmPath()
        {
            // On Windows, use npm.cmd
            if (OperatingSystem.IsWindows())
            {
                // Check if npm is in PATH
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = "npm.cmd",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(startInfo);
                    string output = proc.StandardOutput.ReadLine();
                    proc.WaitForExit();
                    if (!string.IsNullOrEmpty(output) && File.Exists(output))
                        return output;
                }
                catch { }

                // Fallback to npm.cmd (relies on PATH)
                return "npm.cmd";
            }

            // On Unix-like systems, use npm directly
            return "npm";
        }

        /// <summary>
        /// Find node executable
        /// </summary>
        private string FindNodePath()
        {
            if (OperatingSystem.IsWindows())
            {
                // Check if node is in PATH
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = "node.exe",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(startInfo);
                    string output = proc.StandardOutput.ReadLine();
                    proc.WaitForExit();
                    if (!string.IsNullOrEmpty(output) && File.Exists(output))
                        return output;
                }
                catch { }

                return "node.exe";
            }

            return "node";
        }

        /// <summary>
        /// Stop the UI process and clean up
        /// </summary>
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            if (uiProcess != null && !uiProcess.HasExited)
            {
                factory.Log("UILauncher: Stopping UI server...");

                try
                {
                    // On Windows, npm spawns child processes. Use taskkill to kill the entire tree.
                    if (OperatingSystem.IsWindows())
                    {
                        KillProcessTree(uiProcess.Id);
                    }
                    else
                    {
                        // On Unix, send SIGTERM
                        uiProcess.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex)
                {
                    factory.LogDebug($"UILauncher: Error killing UI process: {ex.Message}");
                }

                try
                {
                    uiProcess.WaitForExit(5000);
                }
                catch { }

                uiProcess.Dispose();
                uiProcess = null;

                factory.Log("UILauncher: UI server stopped");
            }
        }

        /// <summary>
        /// Kill a process and all its children on Windows using taskkill
        /// </summary>
        private void KillProcessTree(int pid)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/T /F /PID {pid}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(startInfo);
                proc.WaitForExit(5000);
            }
            catch { }
        }
    }
}
