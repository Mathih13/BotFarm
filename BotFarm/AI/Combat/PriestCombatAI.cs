using Client;
using Client.UI;
using Client.World.Entities;

namespace BotFarm.AI.Combat
{
    /// <summary>
    /// Priest combat AI - ranged caster with healing
    /// </summary>
    public class PriestCombatAI : BaseClassCombatAI
    {
        // Priest spell IDs (level 1-10 abilities)
        private const uint SMITE = 585;              // Direct damage, 2.5s cast
        private const uint SHADOW_WORD_PAIN = 589;   // DoT (instant)
        private const uint POWER_WORD_SHIELD = 17;   // Absorb shield (instant)
        private const uint LESSER_HEAL = 2050;       // Heal self, 2s cast
        private const uint POWER_WORD_FORTITUDE = 1243; // Stamina buff (instant)

        // Cast times
        private const float SMITE_CAST_TIME = 2.5f;
        private const float LESSER_HEAL_CAST_TIME = 2.0f;

        private bool hasShield = false;
        private bool hasAppliedSWP = false;
        private bool hasFortitude = false;

        public PriestCombatAI()
        {
            lowHealthThreshold = 40f;  // Priests are squishy
            lowManaThreshold = 30f;
            restUntilHealthPercent = 90f;
            restUntilManaPercent = 80f;
        }

        public override void OnCombatStart(AutomatedGame game, WorldObject target)
        {
            base.OnCombatStart(game, target);
            hasAppliedSWP = false;
            hasShield = false;
        }

        public override void OnCombatUpdate(AutomatedGame game, WorldObject target)
        {
            var player = game.Player;

            // Don't interrupt current cast
            if (IsCasting)
                return;

            // Priority 1: Heal self if low health
            if (player.HealthPercent < 50 && player.Mana >= 45)
            {
                TryCastSpellOnSelf(game, LESSER_HEAL, LESSER_HEAL_CAST_TIME);
                return;
            }

            // Priority 2: Shield self if not active and taking damage (instant)
            if (!hasShield && player.HealthPercent < 80 && player.Mana >= 45)
            {
                game.CastSpellOnSelf(POWER_WORD_SHIELD);
                hasShield = true;
                return;
            }

            // Priority 3: Apply Shadow Word: Pain if not on target (instant)
            if (!hasAppliedSWP && player.Mana >= 25)
            {
                game.CastSpell(SHADOW_WORD_PAIN, target.GUID);
                hasAppliedSWP = true;
                return;
            }

            // Priority 4: Smite as filler
            if (player.Mana >= 30)
            {
                TryCastSpell(game, SMITE, target.GUID, SMITE_CAST_TIME);
                return;
            }

            // Out of mana - just wand/auto-attack
        }

        public override void OnCombatEnd(AutomatedGame game)
        {
            base.OnCombatEnd(game);
            hasAppliedSWP = false;
            hasShield = false;
        }

        public override bool OnRest(AutomatedGame game)
        {
            var player = game.Player;

            // Don't interrupt current cast
            if (IsCasting)
                return false;

            // Buff Fortitude if not active (instant)
            if (!hasFortitude && player.Mana >= 60)
            {
                game.CastSpellOnSelf(POWER_WORD_FORTITUDE);
                hasFortitude = true;
            }

            // Heal up if injured
            if (player.HealthPercent < restUntilHealthPercent && player.Mana >= 45)
            {
                TryCastSpellOnSelf(game, LESSER_HEAL, LESSER_HEAL_CAST_TIME);
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
