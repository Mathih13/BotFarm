using System.Collections.Generic;
using Client.World.Definitions;

namespace Client.World.Items
{
    /// <summary>
    /// Calculates item scores for comparison and handles equip eligibility checks.
    /// </summary>
    public static class ItemScorer
    {
        // Stat weights per class - higher weight = more desirable
        private static readonly Dictionary<Class, Dictionary<ItemStatType, float>> StatWeights = new Dictionary<Class, Dictionary<ItemStatType, float>>
        {
            // Warrior: Strength, Stamina, Attack Power
            [Class.Warrior] = new Dictionary<ItemStatType, float>
            {
                [ItemStatType.Strength] = 2.5f,
                [ItemStatType.Stamina] = 1.5f,
                [ItemStatType.AttackPower] = 1.0f,
                [ItemStatType.CritRating] = 0.8f,
                [ItemStatType.CritMeleeRating] = 0.8f,
                [ItemStatType.HitRating] = 0.7f,
                [ItemStatType.Agility] = 0.5f,
                [ItemStatType.ExpertiseRating] = 0.6f,
                [ItemStatType.ArmorPenetrationRating] = 0.5f
            },
            // Paladin: Strength (Ret/Prot), Stamina, Intellect (Holy)
            [Class.Paladin] = new Dictionary<ItemStatType, float>
            {
                [ItemStatType.Strength] = 2.0f,
                [ItemStatType.Stamina] = 1.5f,
                [ItemStatType.Intellect] = 1.5f,
                [ItemStatType.AttackPower] = 0.8f,
                [ItemStatType.SpellPower] = 1.0f,
                [ItemStatType.CritRating] = 0.6f,
                [ItemStatType.HitRating] = 0.5f
            },
            // Hunter: Agility, Attack Power, Stamina
            [Class.Hunter] = new Dictionary<ItemStatType, float>
            {
                [ItemStatType.Agility] = 2.5f,
                [ItemStatType.RangedAttackPower] = 1.2f,
                [ItemStatType.AttackPower] = 1.0f,
                [ItemStatType.Stamina] = 1.0f,
                [ItemStatType.CritRating] = 0.8f,
                [ItemStatType.HitRating] = 0.7f,
                [ItemStatType.Intellect] = 0.3f,
                [ItemStatType.ArmorPenetrationRating] = 0.6f
            },
            // Rogue: Agility, Attack Power, Stamina
            [Class.Rogue] = new Dictionary<ItemStatType, float>
            {
                [ItemStatType.Agility] = 2.5f,
                [ItemStatType.AttackPower] = 1.2f,
                [ItemStatType.Stamina] = 1.0f,
                [ItemStatType.CritRating] = 0.8f,
                [ItemStatType.HitRating] = 0.7f,
                [ItemStatType.ExpertiseRating] = 0.6f,
                [ItemStatType.ArmorPenetrationRating] = 0.6f,
                [ItemStatType.HasteRating] = 0.5f
            },
            // Priest: Intellect, Spirit, Stamina, Spell Power
            [Class.Priest] = new Dictionary<ItemStatType, float>
            {
                [ItemStatType.Intellect] = 2.5f,
                [ItemStatType.Spirit] = 2.0f,
                [ItemStatType.Stamina] = 1.0f,
                [ItemStatType.SpellPower] = 1.5f,
                [ItemStatType.CritRating] = 0.6f,
                [ItemStatType.HasteRating] = 0.7f,
                [ItemStatType.ManaRegeneration] = 1.0f
            },
            // Death Knight: Strength, Stamina
            [Class.DeathKnight] = new Dictionary<ItemStatType, float>
            {
                [ItemStatType.Strength] = 2.5f,
                [ItemStatType.Stamina] = 1.5f,
                [ItemStatType.AttackPower] = 1.0f,
                [ItemStatType.CritRating] = 0.8f,
                [ItemStatType.HitRating] = 0.7f,
                [ItemStatType.ExpertiseRating] = 0.6f,
                [ItemStatType.ArmorPenetrationRating] = 0.5f
            },
            // Shaman: Intellect (Resto/Ele), Agility (Enh), Stamina
            [Class.Shaman] = new Dictionary<ItemStatType, float>
            {
                [ItemStatType.Intellect] = 2.0f,
                [ItemStatType.Agility] = 1.5f,
                [ItemStatType.Stamina] = 1.2f,
                [ItemStatType.SpellPower] = 1.0f,
                [ItemStatType.AttackPower] = 0.8f,
                [ItemStatType.CritRating] = 0.6f,
                [ItemStatType.Spirit] = 0.5f
            },
            // Mage: Intellect, Spell Power, Stamina
            [Class.Mage] = new Dictionary<ItemStatType, float>
            {
                [ItemStatType.Intellect] = 2.5f,
                [ItemStatType.SpellPower] = 1.8f,
                [ItemStatType.Stamina] = 1.0f,
                [ItemStatType.CritRating] = 0.8f,
                [ItemStatType.HasteRating] = 0.8f,
                [ItemStatType.HitRating] = 0.7f,
                [ItemStatType.Spirit] = 0.3f
            },
            // Warlock: Intellect, Spell Power, Stamina
            [Class.Warlock] = new Dictionary<ItemStatType, float>
            {
                [ItemStatType.Intellect] = 2.5f,
                [ItemStatType.SpellPower] = 1.8f,
                [ItemStatType.Stamina] = 1.2f,
                [ItemStatType.CritRating] = 0.7f,
                [ItemStatType.HasteRating] = 0.8f,
                [ItemStatType.HitRating] = 0.7f,
                [ItemStatType.Spirit] = 0.5f
            },
            // Druid: Varies by spec, balance weights
            [Class.Druid] = new Dictionary<ItemStatType, float>
            {
                [ItemStatType.Intellect] = 1.8f,
                [ItemStatType.Agility] = 1.5f,
                [ItemStatType.Stamina] = 1.2f,
                [ItemStatType.Spirit] = 1.0f,
                [ItemStatType.SpellPower] = 1.0f,
                [ItemStatType.AttackPower] = 0.8f,
                [ItemStatType.CritRating] = 0.6f
            }
        };

        // Armor type allowed by class and level
        // Warriors/Paladins/DKs: Plate at 40+, Mail before
        // Hunters/Shamans: Mail at 40+, Leather before
        // Rogues/Druids: Leather only
        // Cloth classes: Cloth only
        private static readonly Dictionary<Class, ArmorSubclass[]> ArmorRestrictions = new Dictionary<Class, ArmorSubclass[]>
        {
            [Class.Warrior] = new[] { ArmorSubclass.Plate, ArmorSubclass.Mail, ArmorSubclass.Leather, ArmorSubclass.Cloth, ArmorSubclass.Shield },
            [Class.Paladin] = new[] { ArmorSubclass.Plate, ArmorSubclass.Mail, ArmorSubclass.Leather, ArmorSubclass.Cloth, ArmorSubclass.Shield },
            [Class.DeathKnight] = new[] { ArmorSubclass.Plate, ArmorSubclass.Mail, ArmorSubclass.Leather, ArmorSubclass.Cloth },
            [Class.Hunter] = new[] { ArmorSubclass.Mail, ArmorSubclass.Leather, ArmorSubclass.Cloth },
            [Class.Shaman] = new[] { ArmorSubclass.Mail, ArmorSubclass.Leather, ArmorSubclass.Cloth, ArmorSubclass.Shield },
            [Class.Rogue] = new[] { ArmorSubclass.Leather, ArmorSubclass.Cloth },
            [Class.Druid] = new[] { ArmorSubclass.Leather, ArmorSubclass.Cloth },
            [Class.Priest] = new[] { ArmorSubclass.Cloth },
            [Class.Mage] = new[] { ArmorSubclass.Cloth },
            [Class.Warlock] = new[] { ArmorSubclass.Cloth }
        };

        /// <summary>
        /// Calculate a score for an item based on stats and player class
        /// </summary>
        public static float CalculateScore(ItemTemplate item, Class playerClass, uint playerLevel)
        {
            if (item == null) return 0;

            // Base score from item level and quality
            float score = item.ItemLevel * ((uint)item.Quality + 1) * 0.1f;

            // Add armor value (more valuable for tanks, but useful for all)
            score += item.Armor * 0.3f;

            // Add weapon DPS for weapons
            if (item.ItemClass == ItemClass.Weapon && item.DPS > 0)
            {
                score += item.DPS * 2.0f;
            }

            // Add weighted stat values
            if (StatWeights.TryGetValue(playerClass, out var weights))
            {
                foreach (var stat in item.Stats)
                {
                    float weight = 0.5f;  // Default weight for unknown stats
                    if (weights.TryGetValue(stat.Type, out var classWeight))
                    {
                        weight = classWeight;
                    }
                    score += stat.Value * weight;
                }
            }
            else
            {
                // Unknown class, just sum stats equally
                foreach (var stat in item.Stats)
                {
                    score += stat.Value * 0.5f;
                }
            }

            return score;
        }

        /// <summary>
        /// Check if a player can equip an item based on class, level, and armor type
        /// </summary>
        public static bool CanEquip(ItemTemplate item, Class playerClass, uint playerLevel)
        {
            if (item == null) return false;

            // Check level requirement
            if (item.RequiredLevel > playerLevel) return false;

            // Check class restriction bitmask (0 means all classes allowed)
            if (item.AllowableClass != 0)
            {
                uint classMask = 1u << ((int)playerClass - 1);
                if ((item.AllowableClass & classMask) == 0) return false;
            }

            // Check armor type restrictions
            if (item.ItemClass == ItemClass.Armor)
            {
                var armorType = (ArmorSubclass)item.Subclass;

                // Skip misc armor (always allowed - includes cloaks, tabards, etc.)
                if (armorType == ArmorSubclass.Misc) return true;

                // Check if class can wear this armor type
                if (!ArmorRestrictions.TryGetValue(playerClass, out var allowed))
                    return false;

                bool canWear = false;
                foreach (var type in allowed)
                {
                    if (type == armorType)
                    {
                        canWear = true;
                        break;
                    }
                }

                if (!canWear) return false;

                // Plate classes can only wear plate at 40+
                if ((playerClass == Class.Warrior || playerClass == Class.Paladin || playerClass == Class.DeathKnight)
                    && armorType == ArmorSubclass.Plate && playerLevel < 40)
                {
                    return false;
                }

                // Mail classes can only wear mail at 40+ (before that, leather)
                if ((playerClass == Class.Hunter || playerClass == Class.Shaman)
                    && armorType == ArmorSubclass.Mail && playerLevel < 40)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Compare two items and determine if the new item is better
        /// </summary>
        public static bool IsBetter(ItemTemplate newItem, ItemTemplate currentItem, Class playerClass, uint playerLevel)
        {
            if (newItem == null) return false;
            if (!CanEquip(newItem, playerClass, playerLevel)) return false;

            // If no current item, new item is always better
            if (currentItem == null) return true;

            float newScore = CalculateScore(newItem, playerClass, playerLevel);
            float currentScore = CalculateScore(currentItem, playerClass, playerLevel);

            return newScore > currentScore;
        }
    }
}
