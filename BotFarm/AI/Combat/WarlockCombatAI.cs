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

        private bool hasCorruption = false;
        private bool hasImmolate = false;
        private bool hasCurseOfAgony = false;
        private bool hasDemonSkin = false;

        public WarlockCombatAI()
        {
            lowHealthThreshold = 35f;
            lowManaThreshold = 20f;
            restUntilHealthPercent = 80f;
            restUntilManaPercent = 70f;
        }

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
            if (IsCasting)
                return;

            // Priority 1: Drain Life if health is low
            if (player.HealthPercent < 40 && player.Mana >= 55)
            {
                TryCastSpell(game, DRAIN_LIFE, target.GUID, DRAIN_LIFE_CHANNEL_TIME);
                return;
            }

            // Priority 2: Life Tap if low mana but healthy
            if (player.ManaPercent < 20 && player.HealthPercent > 50)
            {
                game.CastSpellOnSelf(LIFE_TAP);
                return;
            }

            // Priority 3: Corruption if not applied (instant)
            if (!hasCorruption && player.Mana >= 35)
            {
                game.CastSpell(CORRUPTION, target.GUID);
                hasCorruption = true;
                return;
            }

            // Priority 4: Immolate if not applied
            if (!hasImmolate && player.Mana >= 50)
            {
                TryCastSpell(game, IMMOLATE, target.GUID, IMMOLATE_CAST_TIME);
                hasImmolate = true;
                return;
            }

            // Priority 5: Curse of Agony if not applied (instant)
            if (!hasCurseOfAgony && player.Mana >= 25)
            {
                game.CastSpell(CURSE_OF_AGONY, target.GUID);
                hasCurseOfAgony = true;
                return;
            }

            // Priority 6: Shadow Bolt as filler
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
            if (IsCasting)
                return false;

            // Buff Demon Skin if not active
            if (!hasDemonSkin && player.Mana >= 40)
            {
                game.CastSpellOnSelf(DEMON_SKIN);
                hasDemonSkin = true;
            }

            // Life Tap to restore mana if healthy
            if (player.ManaPercent < restUntilManaPercent && player.HealthPercent > 60)
            {
                game.CastSpellOnSelf(LIFE_TAP);
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
