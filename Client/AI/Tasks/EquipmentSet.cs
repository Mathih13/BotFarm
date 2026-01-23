using System.Collections.Generic;

namespace Client.AI.Tasks
{
    /// <summary>
    /// Represents a reusable equipment set that can be attached to test harnesses.
    /// When a test runs, bots receive items from the appropriate equipment set.
    /// </summary>
    public class EquipmentSet
    {
        /// <summary>
        /// Unique name identifier for this equipment set
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Optional description of the equipment set
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Optional class restriction - null means all classes can use this set
        /// </summary>
        public string ClassRestriction { get; set; }

        /// <summary>
        /// List of items included in this equipment set
        /// </summary>
        public List<EquipmentSetItem> Items { get; set; } = new List<EquipmentSetItem>();
    }

    /// <summary>
    /// Represents a single item within an equipment set
    /// </summary>
    public class EquipmentSetItem
    {
        /// <summary>
        /// Item entry ID from the database
        /// </summary>
        public uint Entry { get; set; }

        /// <summary>
        /// Number of items to give (defaults to 1)
        /// </summary>
        public int Count { get; set; } = 1;

        /// <summary>
        /// Whether to auto-equip this item (defaults to true for equipment sets)
        /// Set to false for consumables, ammo, etc. that should stay in bags
        /// </summary>
        public bool Equip { get; set; } = true;
    }
}
