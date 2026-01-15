using System;
using System.Collections.Generic;

namespace Client.World.Definitions
{
    /// <summary>
    /// Loot type as sent in SMSG_LOOT_RESPONSE
    /// </summary>
    public enum LootType : byte
    {
        None = 0,
        Corpse = 1,
        Pickpocketing = 2,
        Fishing = 3,
        Disenchanting = 4,
        Skinning = 6
    }

    /// <summary>
    /// Loot slot type determining how item can be looted
    /// </summary>
    public enum LootSlotType : byte
    {
        AllowLoot = 0,
        RollOngoing = 1,
        Locked = 2,
        Master = 3,
        Owner = 4
    }

    /// <summary>
    /// Individual loot item data
    /// </summary>
    public class LootItem
    {
        public byte Slot { get; set; }
        public uint ItemId { get; set; }
        public uint Count { get; set; }
        public uint DisplayId { get; set; }
        public int RandomPropertySeed { get; set; }
        public int RandomPropertyId { get; set; }
        public LootSlotType SlotType { get; set; }
    }

    /// <summary>
    /// Current loot window state
    /// </summary>
    public class LootWindowState
    {
        public ulong LootGuid { get; set; }
        public LootType Type { get; set; }
        public uint Gold { get; set; }
        public List<LootItem> Items { get; set; } = new List<LootItem>();
        public bool IsOpen { get; set; }

        public void Clear()
        {
            LootGuid = 0;
            Type = LootType.None;
            Gold = 0;
            Items.Clear();
            IsOpen = false;
        }
    }
}
