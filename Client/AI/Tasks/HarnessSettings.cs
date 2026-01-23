using System.Collections.Generic;

namespace Client.AI.Tasks
{
    /// <summary>
    /// Configuration for test harness - defines bot requirements for a route
    /// </summary>
    public class HarnessSettings
    {
        /// <summary>
        /// Number of bots to spawn for this route
        /// </summary>
        public int BotCount { get; set; } = 1;

        /// <summary>
        /// Prefix for bot account names (e.g., "test_northshire_" becomes "test_northshire_1", "test_northshire_2", etc.)
        /// </summary>
        public string AccountPrefix { get; set; } = "testbot_";

        /// <summary>
        /// List of allowed character classes - bots are distributed round-robin among these
        /// </summary>
        public List<string> Classes { get; set; } = new List<string> { "Warrior" };

        /// <summary>
        /// Character race (must be compatible with specified classes)
        /// </summary>
        public string Race { get; set; } = "Human";

        /// <summary>
        /// Starting level for characters (set via Remote Access command)
        /// </summary>
        public int Level { get; set; } = 1;

        /// <summary>
        /// List of items to give characters at start (sent via Remote Access mail)
        /// </summary>
        public List<ItemGrant> Items { get; set; } = new List<ItemGrant>();

        /// <summary>
        /// Quest IDs to mark as completed before test starts (for setting up prerequisites)
        /// </summary>
        public List<uint> CompletedQuests { get; set; } = new List<uint>();

        /// <summary>
        /// Starting position for bots (teleported after login)
        /// </summary>
        public StartPosition StartPosition { get; set; }

        /// <summary>
        /// Timeout in seconds for bot creation, character creation, and login
        /// </summary>
        public int SetupTimeoutSeconds { get; set; } = 120;

        /// <summary>
        /// Timeout in seconds for route completion
        /// </summary>
        public int TestTimeoutSeconds { get; set; } = 600;

        /// <summary>
        /// Name of a snapshot to restore before running this test.
        /// The character will be restored to this saved state before execution.
        /// </summary>
        public string RestoreSnapshot { get; set; }

        /// <summary>
        /// Name of a snapshot to save after completing this test.
        /// The character state will be saved for use by dependent tests.
        /// </summary>
        public string SaveSnapshot { get; set; }

        /// <summary>
        /// List of equipment set names to apply to all bots (filtered by class restriction)
        /// </summary>
        public List<string> EquipmentSets { get; set; } = new List<string>();

        /// <summary>
        /// Class-specific equipment set overrides (key is class name, value is equipment set name)
        /// </summary>
        public Dictionary<string, string> ClassEquipmentSets { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Represents an item to give to a character
    /// </summary>
    public class ItemGrant
    {
        /// <summary>
        /// Item entry ID from the database
        /// </summary>
        public uint Entry { get; set; }

        /// <summary>
        /// Number of items to give
        /// </summary>
        public int Count { get; set; } = 1;
    }

    /// <summary>
    /// Represents a starting position in the world
    /// </summary>
    public class StartPosition
    {
        /// <summary>
        /// Map ID (0 = Eastern Kingdoms, 1 = Kalimdor, etc.)
        /// </summary>
        public uint MapId { get; set; }

        /// <summary>
        /// X coordinate
        /// </summary>
        public float X { get; set; }

        /// <summary>
        /// Y coordinate
        /// </summary>
        public float Y { get; set; }

        /// <summary>
        /// Z coordinate
        /// </summary>
        public float Z { get; set; }
    }
}
