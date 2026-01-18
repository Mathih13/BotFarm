using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BotFarm.Properties;
using BotFarm.Web.Models;
using Client.UI;
using Microsoft.AspNetCore.Mvc;

namespace BotFarm.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ConfigController : ControllerBase
    {
        private readonly BotFactory factory;

        public ConfigController(BotFactory factory)
        {
            this.factory = factory;
        }

        /// <summary>
        /// GET /api/config - Return all current settings
        /// </summary>
        [HttpGet]
        public ActionResult<ConfigResponse> GetConfig()
        {
            var settings = Settings.Default;

            // Get paths - trim whitespace from XML formatting
            string mmapsPath = settings.MMAPsFolderPath?.Trim() ?? "";
            string vmapsPath = settings.VMAPsFolderPath?.Trim() ?? "";
            string mapsPath = settings.MAPsFolderPath?.Trim() ?? "";
            string dbcsPath = settings.DBCsFolderPath?.Trim() ?? "";

            // Check if paths look like unconfigured defaults
            if (IsDefaultPath(mmapsPath) && IsDefaultPath(vmapsPath) &&
                IsDefaultPath(mapsPath) && IsDefaultPath(dbcsPath))
            {
                // Try to read from app.config directly
                var appConfigPaths = TryReadPathsFromAppConfig();
                if (appConfigPaths != null)
                {
                    mmapsPath = appConfigPaths.Value.mmaps?.Trim() ?? "";
                    vmapsPath = appConfigPaths.Value.vmaps?.Trim() ?? "";
                    mapsPath = appConfigPaths.Value.maps?.Trim() ?? "";
                    dbcsPath = appConfigPaths.Value.dbcs?.Trim() ?? "";
                }
            }

            return new ConfigResponse
            {
                // Server Connection
                Hostname = settings.Hostname?.Trim() ?? "localhost",
                Port = settings.Port,
                Username = settings.Username?.Trim() ?? "",
                Password = settings.Password ?? "",
                RAPort = settings.RAPort,
                RealmID = settings.RealmID,

                // Bot Settings
                MinBotsCount = settings.MinBotsCount,
                MaxBotsCount = settings.MaxBotsCount,
                RandomBots = settings.RandomBots,
                CreateAccountOnly = settings.CreateAccountOnly,

                // Data Paths - use potentially overridden values
                MMAPsFolderPath = mmapsPath,
                VMAPsFolderPath = vmapsPath,
                MAPsFolderPath = mapsPath,
                DBCsFolderPath = dbcsPath,

                // MySQL Settings
                MySQLHost = settings.MySQLHost?.Trim() ?? "localhost",
                MySQLPort = settings.MySQLPort,
                MySQLUser = settings.MySQLUser?.Trim() ?? "",
                MySQLPassword = settings.MySQLPassword ?? "",
                MySQLCharactersDB = settings.MySQLCharactersDB?.Trim() ?? "characters",
                MySQLWorldDB = settings.MySQLWorldDB?.Trim() ?? "world",

                // Web UI Settings
                EnableWebUI = settings.EnableWebUI,
                WebUIPort = settings.WebUIPort
            };
        }

        /// <summary>
        /// PUT /api/config - Update settings and trigger restart
        /// </summary>
        [HttpPut]
        public ActionResult<ConfigUpdateResponse> UpdateConfig([FromBody] ConfigUpdateRequest request)
        {
            var errors = new List<string>();
            var settings = Settings.Default;

            try
            {
                // Log what we're saving for debugging
                factory.Log($"ConfigController: Saving settings - MMAPsFolderPath will be: {request.MMAPsFolderPath}");

                // Validate required fields
                if (string.IsNullOrWhiteSpace(request.Hostname))
                    errors.Add("Hostname is required");
                if (request.Port <= 0 || request.Port > 65535)
                    errors.Add("Port must be between 1 and 65535");
                if (request.MinBotsCount < 0)
                    errors.Add("MinBotsCount cannot be negative");
                if (request.MaxBotsCount < request.MinBotsCount)
                    errors.Add("MaxBotsCount must be >= MinBotsCount");
                if (request.WebUIPort <= 0 || request.WebUIPort > 65535)
                    errors.Add("WebUIPort must be between 1 and 65535");

                if (errors.Count > 0)
                {
                    return BadRequest(new ConfigUpdateResponse
                    {
                        Success = false,
                        RestartRequired = false,
                        Message = "Validation failed",
                        Errors = errors
                    });
                }

                // Apply settings
                settings.Hostname = request.Hostname?.Trim() ?? "localhost";
                settings.Port = request.Port;
                settings.Username = request.Username?.Trim() ?? "admin";
                settings.Password = request.Password ?? "admin";
                settings.RAPort = request.RAPort;
                settings.RealmID = request.RealmID;

                settings.MinBotsCount = request.MinBotsCount;
                settings.MaxBotsCount = request.MaxBotsCount;
                settings.RandomBots = request.RandomBots;
                settings.CreateAccountOnly = request.CreateAccountOnly;

                settings.MMAPsFolderPath = request.MMAPsFolderPath?.Trim() ?? "C:\\";
                settings.VMAPsFolderPath = request.VMAPsFolderPath?.Trim() ?? "C:\\";
                settings.MAPsFolderPath = request.MAPsFolderPath?.Trim() ?? "C:\\";
                settings.DBCsFolderPath = request.DBCsFolderPath?.Trim() ?? "C:\\";

                settings.MySQLHost = request.MySQLHost?.Trim() ?? "localhost";
                settings.MySQLPort = request.MySQLPort;
                settings.MySQLUser = request.MySQLUser?.Trim() ?? "trinity";
                settings.MySQLPassword = request.MySQLPassword ?? "trinity";
                settings.MySQLCharactersDB = request.MySQLCharactersDB?.Trim() ?? "characters";
                settings.MySQLWorldDB = request.MySQLWorldDB?.Trim() ?? "world";

                settings.EnableWebUI = request.EnableWebUI;
                settings.WebUIPort = request.WebUIPort;

                // Save settings
                settings.Save();

                // Verify the save worked
                factory.Log($"ConfigController: After save, MMAPsFolderPath is now: {Settings.Default.MMAPsFolderPath}");
                factory.Log("Configuration updated, requesting restart");

                // Request restart
                factory.RequestRestart();

                return Ok(new ConfigUpdateResponse
                {
                    Success = true,
                    RestartRequired = true,
                    Message = "Configuration saved. Restarting application..."
                });
            }
            catch (Exception ex)
            {
                factory.Log($"Failed to update configuration: {ex.Message}", LogLevel.Error);
                return StatusCode(500, new ConfigUpdateResponse
                {
                    Success = false,
                    RestartRequired = false,
                    Message = $"Failed to save configuration: {ex.Message}",
                    Errors = new List<string> { ex.Message }
                });
            }
        }

        /// <summary>
        /// GET /api/config/status - Return isFirstRun flag and path validation info
        /// </summary>
        [HttpGet("status")]
        public ActionResult<ConfigStatusResponse> GetConfigStatus()
        {
            var settings = Settings.Default;
            var response = new ConfigStatusResponse();

            // Check if paths are default/invalid (trim whitespace from XML formatting)
            var pathsToCheck = new Dictionary<string, string>
            {
                { "MMAPsFolderPath", settings.MMAPsFolderPath?.Trim() ?? "" },
                { "VMAPsFolderPath", settings.VMAPsFolderPath?.Trim() ?? "" },
                { "MAPsFolderPath", settings.MAPsFolderPath?.Trim() ?? "" },
                { "DBCsFolderPath", settings.DBCsFolderPath?.Trim() ?? "" }
            };

            foreach (var kvp in pathsToCheck)
            {
                if (IsDefaultPath(kvp.Value))
                {
                    response.MissingPaths.Add(kvp.Key);
                }
                else if (!Directory.Exists(kvp.Value))
                {
                    response.InvalidPaths.Add(kvp.Key);
                }
            }

            // First run if any path is default or doesn't exist
            response.IsFirstRun = response.MissingPaths.Count > 0 || response.InvalidPaths.Count > 0;

            // Also include the setup mode flag from BotFactory
            response.SetupModeRequired = factory.SetupModeRequired;

            return response;
        }

        /// <summary>
        /// POST /api/config/validate-paths - Validate a single path
        /// </summary>
        [HttpPost("validate-paths")]
        public ActionResult<PathValidationResponse> ValidatePath([FromBody] PathValidationRequest request)
        {
            var response = new PathValidationResponse();

            if (string.IsNullOrWhiteSpace(request.Path))
            {
                response.Valid = false;
                response.Message = "Path is required";
                return response;
            }

            string path = request.Path.Trim();

            // Check if path exists
            response.Exists = Directory.Exists(path);
            if (!response.Exists)
            {
                response.Valid = false;
                response.Message = "Directory does not exist";
                return response;
            }

            // Check for expected files based on path type
            var (expectedPatterns, subdirectories) = GetExpectedFilePatternsAndSubdirs(request.PathType?.ToLower());
            if (expectedPatterns != null && expectedPatterns.Length > 0)
            {
                try
                {
                    var foundFiles = new List<string>();
                    string foundInPath = null;

                    // First check the path itself
                    foreach (var pattern in expectedPatterns)
                    {
                        var files = Directory.GetFiles(path, pattern, SearchOption.TopDirectoryOnly);
                        if (files.Length > 0)
                        {
                            foundFiles.AddRange(files.Take(5).Select(f => Path.GetFileName(f)));
                            foundInPath = path;
                            break;
                        }
                    }

                    // If not found, check common subdirectories
                    if (foundFiles.Count == 0 && subdirectories != null)
                    {
                        foreach (var subdir in subdirectories)
                        {
                            string subPath = Path.Combine(path, subdir);
                            if (Directory.Exists(subPath))
                            {
                                foreach (var pattern in expectedPatterns)
                                {
                                    var files = Directory.GetFiles(subPath, pattern, SearchOption.TopDirectoryOnly);
                                    if (files.Length > 0)
                                    {
                                        foundFiles.AddRange(files.Take(5).Select(f => Path.GetFileName(f)));
                                        foundInPath = subPath;
                                        break;
                                    }
                                }
                                if (foundFiles.Count > 0) break;
                            }
                        }
                    }

                    response.FoundFiles = foundFiles.Take(5).ToList();
                    response.HasExpectedFiles = foundFiles.Count > 0;

                    if (!response.HasExpectedFiles)
                    {
                        response.Valid = false;
                        response.Message = $"Directory exists but no expected files found (looking for {string.Join(", ", expectedPatterns)})";
                        return response;
                    }

                    // Note if files were found in a subdirectory
                    if (foundInPath != null && foundInPath != path)
                    {
                        string relativePath = Path.GetFileName(foundInPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        response.Message = $"Path is valid (files found in '{relativePath}' subdirectory)";
                    }
                }
                catch (Exception ex)
                {
                    response.Valid = false;
                    response.Message = $"Error checking directory contents: {ex.Message}";
                    return response;
                }
            }

            response.Valid = true;
            if (string.IsNullOrEmpty(response.Message))
                response.Message = "Path is valid";
            return response;
        }

        private static bool IsDefaultPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return true;

            string trimmed = path.Trim();
            return trimmed == "C:\\" ||
                   trimmed == "C:/" ||
                   trimmed == "C:" ||
                   trimmed.Length <= 3;
        }

        /// <summary>
        /// Try to read path settings directly from the app.config file.
        /// This is used when Settings.Default returns defaults instead of app.config values.
        /// </summary>
        private static (string mmaps, string vmaps, string maps, string dbcs)? TryReadPathsFromAppConfig()
        {
            try
            {
                // Get the path to the app.config file (executable + .config)
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string configPath = exePath + ".config";

                // Also try without .dll extension (BotFarm.dll.config -> BotFarm.config)
                if (!System.IO.File.Exists(configPath))
                {
                    configPath = Path.ChangeExtension(exePath, null) + ".config";
                }

                if (!System.IO.File.Exists(configPath))
                    return null;

                var doc = new System.Xml.XmlDocument();
                doc.Load(configPath);

                var nsMgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
                string basePath = "//userSettings/BotFarm.Properties.Settings/setting[@name='{0}']/value";

                string GetSettingValue(string name)
                {
                    var node = doc.SelectSingleNode(string.Format(basePath, name), nsMgr);
                    return node?.InnerText?.Trim();
                }

                string mmaps = GetSettingValue("MMAPsFolderPath");
                string vmaps = GetSettingValue("VMAPsFolderPath");
                string maps = GetSettingValue("MAPsFolderPath");
                string dbcs = GetSettingValue("DBCsFolderPath");

                // Only return if at least one path is non-default
                if (!IsDefaultPath(mmaps) || !IsDefaultPath(vmaps) ||
                    !IsDefaultPath(maps) || !IsDefaultPath(dbcs))
                {
                    return (mmaps ?? "", vmaps ?? "", maps ?? "", dbcs ?? "");
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static (string[] patterns, string[] subdirs) GetExpectedFilePatternsAndSubdirs(string pathType)
        {
            return pathType switch
            {
                "mmaps" => (new[] { "*.mmtile", "*.mmap" }, new[] { "mmaps" }),
                "vmaps" => (new[] { "*.vmtile", "*.vmo" }, new[] { "vmaps" }),
                "maps" => (new[] { "*.map" }, new[] { "maps" }),
                "dbcs" => (new[] { "*.dbc" }, new[] { "dbc", "dbcs", "DBC", "DBCs" }),
                _ => (null, null)
            };
        }
    }
}
