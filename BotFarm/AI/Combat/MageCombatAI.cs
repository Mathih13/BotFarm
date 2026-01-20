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
            if (IsCasting(game))
                return;

            // Priority 1: Frost Nova if target is too close (kiting) - level 10
            float distanceToTarget = (player.GetPosition() - target.GetPosition()).Length;
            if (distanceToTarget < 8.0f && player.Level >= 10 && CanCastSpell(game, FROST_NOVA, 55))
            {
                TryCastSpellOnSelf(game, FROST_NOVA);
                return;
            }

            // Priority 2: Fire Blast (instant, level 6) - weave between Fireballs when off cooldown
            if (player.Level >= 6 && CanCastSpell(game, FIRE_BLAST, 40))
            {
                TryCastSpell(game, FIRE_BLAST, target.GUID);
                return;
            }

            // Priority 3: Fireball as main nuke - level 1
            if (player.Mana >= 30)
            {
                TryCastSpell(game, FIREBALL, target.GUID, FIREBALL_CAST_TIME);
                return;
            }

            // Priority 4: Frostbolt as backup (lower mana cost) - level 4
            if (player.Level >= 4 && player.Mana >= 25)
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
            if (IsCasting(game))
                return false;

            // Buff Arcane Intellect if not active (check via server aura state)
            if (!game.HasBuff(ARCANE_INTELLECT) && player.Mana >= 60)
            {
                TryCastSpellOnSelf(game, ARCANE_INTELLECT);
            }

            return base.OnRest(game);
        }

        public override float GetPreferredCombatRange()
        {
            return 25.0f; // Ranged caster
        }
    }
}
