using Client;
using Client.World.Entities;

namespace BotFarm.AI.Combat
{
    /// <summary>
    /// Warlock combat AI - ranged caster with DoTs and Life Tap
    /// </summary>
    public class WarlockCombatAI : BaseClassCombatAI
    {
        // Warlock spell IDs (level 1-14 abilities)
        private const uint SHADOW_BOLT = 686;        // 2.5s cast, main nuke
        private const uint IMMOLATE = 348;           // DoT + direct damage, 2.0s cast
        private const uint CORRUPTION = 172;         // Instant DoT (level 4)
        private const uint CURSE_OF_AGONY = 980;     // DoT curse (level 8)
        private const uint DRAIN_LIFE = 689;         // Channel, heals self (level 14)
        private const uint LIFE_TAP = 1454;          // Convert health to mana (level 6)
        private const uint DEMON_SKIN = 687;         // Armor + health regen buff

        // Cast times
        private const float SHADOW_BOLT_CAST_TIME = 2.5f;
        private const float IMMOLATE_CAST_TIME = 2.0f;
        private const float DRAIN_LIFE_CHANNEL_TIME = 5.0f;

        // Track DoTs applied this combat (server doesn't track debuffs on enemy)
        private bool hasCorruption = false;
        private bool hasImmolate = false;
        private bool hasCurseOfAgony = false;

        public WarlockCombatAI()
        {
            lowHealthThreshold = 35f;
            lowManaThreshold = 20f;
            restUntilHealthPercent = 80f;
            restUntilManaPercent = 70f;
        }

        private bool HasActiveArmor(AutomatedGame game) => game.HasBuff(DEMON_SKIN);


        public override void OnCombatStart(AutomatedGame game, WorldObject target)
        {
            base.OnCombatStart(game, target);
            hasCorruption = false;
            hasImmolate = false;
            hasCurseOfAgony = false;
        }

        public override void OnCombatUpdate(AutomatedGame game, WorldObject target)
        {
            var player = game.Player;

            // Don't interrupt current cast
            if (IsCasting(game))
                return;

            if (!HasActiveArmor(game) && player.Mana >= 40)
            {
                TryCastSpellOnSelf(game, DEMON_SKIN);
                return;
            }

            // Priority 1: Drain Life if health is low - level 14
            if (player.Level >= 14 && player.HealthPercent < 40 && player.Mana >= 55)
            {
                TryCastSpell(game, DRAIN_LIFE, target.GUID, DRAIN_LIFE_CHANNEL_TIME);
                return;
            }

            // Priority 2: Life Tap if low mana but healthy - level 6
            if (player.Level >= 6 && player.ManaPercent < 20 && player.HealthPercent > 50)
            {
                TryCastSpellOnSelf(game, LIFE_TAP);
                return;
            }

            // Priority 3: Corruption if not applied (instant) - level 4
            if (player.Level >= 4 && !hasCorruption && player.Mana >= 35)
            {
                TryCastSpell(game, CORRUPTION, target.GUID);
                hasCorruption = true;
                return;
            }

            // Priority 4: Immolate if not applied - level 1 (costs ~25 mana at rank 1)
            if (!hasImmolate && player.Mana >= 25)
            {
                TryCastSpell(game, IMMOLATE, target.GUID, IMMOLATE_CAST_TIME);
                hasImmolate = true;
                return;
            }

            // Priority 5: Curse of Agony if not applied (instant) - level 8
            if (player.Level >= 8 && !hasCurseOfAgony && player.Mana >= 25)
            {
                TryCastSpell(game, CURSE_OF_AGONY, target.GUID);
                hasCurseOfAgony = true;
                return;
            }

            // Priority 6: Shadow Bolt as filler - level 1
            if (player.Mana >= 25)
            {
                TryCastSpell(game, SHADOW_BOLT, target.GUID, SHADOW_BOLT_CAST_TIME);
                return;
            }

            // Out of mana - wand/auto-attack
        }

        public override void OnCombatEnd(AutomatedGame game)
        {
            base.OnCombatEnd(game);
            hasCorruption = false;
            hasImmolate = false;
            hasCurseOfAgony = false;
        }

        public override bool OnRest(AutomatedGame game)
        {
            var player = game.Player;

            // Don't interrupt current cast
            if (IsCasting(game))
                return false;

            // Buff Demon Skin if not active (uses server aura state) - level 1
            if (!game.HasBuff(DEMON_SKIN) && player.Mana >= 40)
            {
                TryCastSpellOnSelf(game, DEMON_SKIN);
            }

            // Life Tap to restore mana if healthy - level 6
            if (player.Level >= 6 && player.ManaPercent < restUntilManaPercent && player.HealthPercent > 60)
            {
                TryCastSpellOnSelf(game, LIFE_TAP);
                return false;
            }

            return base.OnRest(game);
        }

        public override float GetPreferredCombatRange()
        {
            return 25.0f; // Ranged caster
        }
    }
}
