using Client;
using Client.World.Entities;

namespace BotFarm.AI.Combat
{
    /// <summary>
    /// Mage combat AI - ranged caster with frost/fire spells and kiting
    /// </summary>
    public class MageCombatAI : BaseClassCombatAI
    {
        // Mage spell IDs (level 1-10 abilities)
        private const uint FIREBALL = 133;           // 3.0s cast, main nuke
        private const uint FROSTBOLT = 116;          // 2.5s cast, slows target
        private const uint FIRE_BLAST = 2136;        // Instant, 8s cooldown (level 6)
        private const uint FROST_NOVA = 122;         // AoE root (level 10)
        private const uint ARCANE_INTELLECT = 1459;  // Intellect buff

        // Cast times
        private const float FIREBALL_CAST_TIME = 3.0f;
        private const float FROSTBOLT_CAST_TIME = 2.5f;

        private bool hasArcaneIntellect = false;

        public MageCombatAI()
        {
            lowHealthThreshold = 40f;
            lowManaThreshold = 20f;
            restUntilHealthPercent = 90f;
            restUntilManaPercent = 80f;
        }

        public override void OnCombatStart(AutomatedGame game, WorldObject target)
        {
            base.OnCombatStart(game, target);
        }

        public override void OnCombatUpdate(AutomatedGame game, WorldObject target)
        {
            var player = game.Player;

            // Don't interrupt current cast
            if (IsCasting)
                return;

            // Priority 1: Frost Nova if target is too close (kiting)
            float distanceToTarget = (player.GetPosition() - target.GetPosition()).Length;
            if (distanceToTarget < 8.0f && player.Mana >= 55)
            {
                game.CastSpellOnSelf(FROST_NOVA);
                return;
            }

            // Priority 2: Fire Blast (instant, use when available)
            if (player.Mana >= 40)
            {
                game.CastSpell(FIRE_BLAST, target.GUID);
                return;
            }

            // Priority 3: Fireball as filler (higher mana cost, check first)
            if (player.Mana >= 30)
            {
                TryCastSpell(game, FIREBALL, target.GUID, FIREBALL_CAST_TIME);
                return;
            }

            // Priority 4: Frostbolt (preferred for slow + damage)
            if (player.Mana >= 25)
            {
                TryCastSpell(game, FROSTBOLT, target.GUID, FROSTBOLT_CAST_TIME);
                return;
            }

            // Out of mana - wand/auto-attack
        }

        public override void OnCombatEnd(AutomatedGame game)
        {
            base.OnCombatEnd(game);
        }

        public override bool OnRest(AutomatedGame game)
        {
            var player = game.Player;

            // Don't interrupt current cast
            if (IsCasting)
                return false;

            // Buff Arcane Intellect if not active
            if (!hasArcaneIntellect && player.Mana >= 60)
            {
                game.CastSpellOnSelf(ARCANE_INTELLECT);
                hasArcaneIntellect = true;
            }

            return base.OnRest(game);
        }

        public override float GetPreferredCombatRange()
        {
            return 25.0f; // Ranged caster
        }
    }
}
