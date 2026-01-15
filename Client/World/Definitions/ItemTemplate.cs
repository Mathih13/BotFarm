using System.Collections.Generic;

namespace Client.World.Definitions
{
    /// <summary>
    /// Item class type (weapon, armor, consumable, etc.)
    /// </summary>
    public enum ItemClass : uint
    {
        Consumable = 0,
        Container = 1,
        Weapon = 2,
        Gem = 3,
        Armor = 4,
        Reagent = 5,
        Projectile = 6,
        TradeGoods = 7,
        Generic = 8,
        Recipe = 9,
        Money = 10,
        Quiver = 11,
        Quest = 12,
        Key = 13,
        Permanent = 14,
        Misc = 15,
        Glyph = 16
    }

    /// <summary>
    /// Armor subclass types
    /// </summary>
    public enum ArmorSubclass : uint
    {
        Misc = 0,
        Cloth = 1,
        Leather = 2,
        Mail = 3,
        Plate = 4,
        Buckler = 5,  // Obsolete
        Shield = 6,
        Libram = 7,
        Idol = 8,
        Totem = 9,
        Sigil = 10
    }

    /// <summary>
    /// Weapon subclass types
    /// </summary>
    public enum WeaponSubclass : uint
    {
        Axe1H = 0,
        Axe2H = 1,
        Bow = 2,
        Gun = 3,
        Mace1H = 4,
        Mace2H = 5,
        Polearm = 6,
        Sword1H = 7,
        Sword2H = 8,
        Obsolete = 9,
        Staff = 10,
        Exotic1H = 11,
        Exotic2H = 12,
        Fist = 13,
        Misc = 14,
        Dagger = 15,
        Thrown = 16,
        Spear = 17,
        Crossbow = 18,
        Wand = 19,
        FishingPole = 20
    }

    /// <summary>
    /// Inventory type determines which slot an item can be equipped to
    /// </summary>
    public enum InventoryType : uint
    {
        NonEquip = 0,
        Head = 1,
        Neck = 2,
        Shoulders = 3,
        Body = 4,       // Shirt
        Chest = 5,
        Waist = 6,
        Legs = 7,
        Feet = 8,
        Wrists = 9,
        Hands = 10,
        Finger = 11,
        Trinket = 12,
        Weapon = 13,    // One-hand weapon
        Shield = 14,
        Ranged = 15,    // Bows
        Cloak = 16,
        TwoHandWeapon = 17,
        Bag = 18,
        Tabard = 19,
        Robe = 20,      // Chest
        WeaponMainHand = 21,
        WeaponOffHand = 22,
        Holdable = 23,  // Off-hand non-weapon
        Ammo = 24,
        Thrown = 25,
        RangedRight = 26,  // Wands, guns
        Quiver = 27,
        Relic = 28
    }

    /// <summary>
    /// Item stat types
    /// </summary>
    public enum ItemStatType : uint
    {
        Mana = 0,
        Health = 1,
        Agility = 3,
        Strength = 4,
        Intellect = 5,
        Spirit = 6,
        Stamina = 7,
        DefenseSkillRating = 12,
        DodgeRating = 13,
        ParryRating = 14,
        BlockRating = 15,
        HitMeleeRating = 16,
        HitRangedRating = 17,
        HitSpellRating = 18,
        CritMeleeRating = 19,
        CritRangedRating = 20,
        CritSpellRating = 21,
        HitTakenMeleeRating = 22,
        HitTakenRangedRating = 23,
        HitTakenSpellRating = 24,
        CritTakenMeleeRating = 25,
        CritTakenRangedRating = 26,
        CritTakenSpellRating = 27,
        HasteMeleeRating = 28,
        HasteRangedRating = 29,
        HasteSpellRating = 30,
        HitRating = 31,
        CritRating = 32,
        HitTakenRating = 33,
        CritTakenRating = 34,
        ResilienceRating = 35,
        HasteRating = 36,
        ExpertiseRating = 37,
        AttackPower = 38,
        RangedAttackPower = 39,
        FeralAttackPower = 40,  // Obsolete
        SpellHealingDone = 41,  // Obsolete
        SpellDamageDone = 42,   // Obsolete
        ManaRegeneration = 43,
        ArmorPenetrationRating = 44,
        SpellPower = 45,
        HealthRegen = 46,
        SpellPenetration = 47,
        BlockValue = 48
    }

    /// <summary>
    /// Item quality/rarity
    /// </summary>
    public enum ItemQuality : uint
    {
        Poor = 0,       // Gray
        Common = 1,     // White
        Uncommon = 2,   // Green
        Rare = 3,       // Blue
        Epic = 4,       // Purple
        Legendary = 5,  // Orange
        Artifact = 6,   // Light gold
        Heirloom = 7    // Blizzard blue
    }

    /// <summary>
    /// A single stat on an item
    /// </summary>
    public struct ItemStat
    {
        public ItemStatType Type;
        public int Value;

        public ItemStat(ItemStatType type, int value)
        {
            Type = type;
            Value = value;
        }
    }

    /// <summary>
    /// Item template data parsed from SMSG_ITEM_QUERY_SINGLE_RESPONSE
    /// </summary>
    public class ItemTemplate
    {
        public uint Entry { get; set; }
        public ItemClass ItemClass { get; set; }
        public uint Subclass { get; set; }
        public string Name { get; set; }
        public uint DisplayId { get; set; }
        public ItemQuality Quality { get; set; }
        public uint Flags { get; set; }
        public uint Flags2 { get; set; }
        public uint BuyPrice { get; set; }
        public uint SellPrice { get; set; }
        public InventoryType InventoryType { get; set; }
        public uint AllowableClass { get; set; }
        public uint AllowableRace { get; set; }
        public uint ItemLevel { get; set; }
        public uint RequiredLevel { get; set; }
        public uint RequiredSkill { get; set; }
        public uint RequiredSkillRank { get; set; }
        public uint Armor { get; set; }
        public float MinDamage { get; set; }
        public float MaxDamage { get; set; }
        public float AttackSpeed { get; set; }  // In milliseconds
        public List<ItemStat> Stats { get; set; } = new List<ItemStat>();

        /// <summary>
        /// Calculate DPS for weapons
        /// </summary>
        public float DPS
        {
            get
            {
                if (AttackSpeed <= 0) return 0;
                return ((MinDamage + MaxDamage) / 2f) / (AttackSpeed / 1000f);
            }
        }

        /// <summary>
        /// Check if this item is equippable armor or weapon
        /// </summary>
        public bool IsEquippableGear
        {
            get
            {
                return ItemClass == ItemClass.Armor || ItemClass == ItemClass.Weapon;
            }
        }

        /// <summary>
        /// Get the equipment slot index for this item's inventory type
        /// Returns -1 if not equippable to a slot we handle
        /// </summary>
        public int GetEquipmentSlot()
        {
            switch (InventoryType)
            {
                case InventoryType.Head: return 0;
                case InventoryType.Shoulders: return 2;
                case InventoryType.Chest:
                case InventoryType.Robe: return 4;
                case InventoryType.Waist: return 5;
                case InventoryType.Legs: return 6;
                case InventoryType.Feet: return 7;
                case InventoryType.Wrists: return 8;
                case InventoryType.Hands: return 9;
                case InventoryType.Weapon:
                case InventoryType.TwoHandWeapon:
                case InventoryType.WeaponMainHand: return 15;
                case InventoryType.Shield:
                case InventoryType.WeaponOffHand:
                case InventoryType.Holdable: return 16;
                case InventoryType.Ranged:
                case InventoryType.RangedRight:
                case InventoryType.Thrown: return 17;
                // We skip: Neck (1), Finger (10,11), Trinket (12,13), Cloak (14), Tabard (18)
                default: return -1;
            }
        }
    }
}
