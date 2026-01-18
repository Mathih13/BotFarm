using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BotFarm.Web.Models
{
    // ============ Config Response Models ============

    /// <summary>
    /// Response containing all configuration settings
    /// </summary>
    public class ConfigResponse
    {
        // Server Connection
        [JsonPropertyName("hostname")]
        public string Hostname { get; set; }
        [JsonPropertyName("port")]
        public int Port { get; set; }
        [JsonPropertyName("username")]
        public string Username { get; set; }
        [JsonPropertyName("password")]
        public string Password { get; set; }
        [JsonPropertyName("raPort")]
        public int RAPort { get; set; }
        [JsonPropertyName("realmID")]
        public int RealmID { get; set; }

        // Bot Settings
        [JsonPropertyName("minBotsCount")]
        public int MinBotsCount { get; set; }
        [JsonPropertyName("maxBotsCount")]
        public int MaxBotsCount { get; set; }
        [JsonPropertyName("randomBots")]
        public bool RandomBots { get; set; }
        [JsonPropertyName("createAccountOnly")]
        public bool CreateAccountOnly { get; set; }

        // Data Paths
        [JsonPropertyName("mmapsFolderPath")]
        public string MMAPsFolderPath { get; set; }
        [JsonPropertyName("vmapsFolderPath")]
        public string VMAPsFolderPath { get; set; }
        [JsonPropertyName("mapsFolderPath")]
        public string MAPsFolderPath { get; set; }
        [JsonPropertyName("dbcsFolderPath")]
        public string DBCsFolderPath { get; set; }

        // MySQL Settings
        [JsonPropertyName("mySQLHost")]
        public string MySQLHost { get; set; }
        [JsonPropertyName("mySQLPort")]
        public int MySQLPort { get; set; }
        [JsonPropertyName("mySQLUser")]
        public string MySQLUser { get; set; }
        [JsonPropertyName("mySQLPassword")]
        public string MySQLPassword { get; set; }
        [JsonPropertyName("mySQLCharactersDB")]
        public string MySQLCharactersDB { get; set; }
        [JsonPropertyName("mySQLWorldDB")]
        public string MySQLWorldDB { get; set; }

        // Web UI Settings
        [JsonPropertyName("enableWebUI")]
        public bool EnableWebUI { get; set; }
        [JsonPropertyName("webUIPort")]
        public int WebUIPort { get; set; }
    }

    /// <summary>
    /// Request to update configuration settings
    /// </summary>
    public class ConfigUpdateRequest
    {
        // Server Connection
        [JsonPropertyName("hostname")]
        public string Hostname { get; set; }
        [JsonPropertyName("port")]
        public int Port { get; set; }
        [JsonPropertyName("username")]
        public string Username { get; set; }
        [JsonPropertyName("password")]
        public string Password { get; set; }
        [JsonPropertyName("raPort")]
        public int RAPort { get; set; }
        [JsonPropertyName("realmID")]
        public int RealmID { get; set; }

        // Bot Settings
        [JsonPropertyName("minBotsCount")]
        public int MinBotsCount { get; set; }
        [JsonPropertyName("maxBotsCount")]
        public int MaxBotsCount { get; set; }
        [JsonPropertyName("randomBots")]
        public bool RandomBots { get; set; }
        [JsonPropertyName("createAccountOnly")]
        public bool CreateAccountOnly { get; set; }

        // Data Paths
        [JsonPropertyName("mmapsFolderPath")]
        public string MMAPsFolderPath { get; set; }
        [JsonPropertyName("vmapsFolderPath")]
        public string VMAPsFolderPath { get; set; }
        [JsonPropertyName("mapsFolderPath")]
        public string MAPsFolderPath { get; set; }
        [JsonPropertyName("dbcsFolderPath")]
        public string DBCsFolderPath { get; set; }

        // MySQL Settings
        [JsonPropertyName("mySQLHost")]
        public string MySQLHost { get; set; }
        [JsonPropertyName("mySQLPort")]
        public int MySQLPort { get; set; }
        [JsonPropertyName("mySQLUser")]
        public string MySQLUser { get; set; }
        [JsonPropertyName("mySQLPassword")]
        public string MySQLPassword { get; set; }
        [JsonPropertyName("mySQLCharactersDB")]
        public string MySQLCharactersDB { get; set; }
        [JsonPropertyName("mySQLWorldDB")]
        public string MySQLWorldDB { get; set; }

        // Web UI Settings
        [JsonPropertyName("enableWebUI")]
        public bool EnableWebUI { get; set; }
        [JsonPropertyName("webUIPort")]
        public int WebUIPort { get; set; }
    }

    /// <summary>
    /// Response after updating configuration
    /// </summary>
    public class ConfigUpdateResponse
    {
        public bool Success { get; set; }
        public bool RestartRequired { get; set; }
        public string Message { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    /// <summary>
    /// Response containing configuration status information
    /// </summary>
    public class ConfigStatusResponse
    {
        [JsonPropertyName("isFirstRun")]
        public bool IsFirstRun { get; set; }

        [JsonPropertyName("setupModeRequired")]
        public bool SetupModeRequired { get; set; }

        [JsonPropertyName("missingPaths")]
        public List<string> MissingPaths { get; set; } = new List<string>();

        [JsonPropertyName("invalidPaths")]
        public List<string> InvalidPaths { get; set; } = new List<string>();
    }

    /// <summary>
    /// Request to validate a path
    /// </summary>
    public class PathValidationRequest
    {
        public string Path { get; set; }
        public string PathType { get; set; }  // "mmaps", "vmaps", "maps", "dbcs"
    }

    /// <summary>
    /// Response for path validation
    /// </summary>
    public class PathValidationResponse
    {
        public bool Valid { get; set; }
        public bool Exists { get; set; }
        public bool HasExpectedFiles { get; set; }
        public string Message { get; set; }
        public List<string> FoundFiles { get; set; } = new List<string>();
    }
}
