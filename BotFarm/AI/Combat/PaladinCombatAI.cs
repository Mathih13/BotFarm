using Client;
using Client.UI;
using Client.World.Entities;

namespace BotFarm.AI.Combat
{
    /// <summary>
    /// Paladin combat AI - melee with self-healing and buffs
    /// </summary>
    public class PaladinCombatAI : BaseClassCombatAI
    {
        // Paladin spell IDs (level 1-10 abilities)
        private const uint SEAL_OF_RIGHTEOUSNESS = 21084;  // Seal buff, adds holy damage to attacks
        private const uint JUDGEMENT = 20271;              // Consumes seal for damage/effect
        private const uint BLESSING_OF_MIGHT = 19740;      // Attack power buff
        private const uint DEVOTION_AURA = 465;            // Armor aura
        private const uint HOLY_LIGHT = 635;               // Heal
        private const uint DIVINE_PROTECTION = 498;        // Damage reduction (level 6)
        private const uint HAMMER_OF_JUSTICE = 853;        // Stun (level 8)

        // Cast times (at low level, no spell haste)
        private const float HOLY_LIGHT_CAST_TIME = 2.5f;

        public PaladinCombatAI()
        {
            lowHealthThreshold = 30f;  // Paladins can heal themselves
            lowManaThreshold = 20f;
            restUntilHealthPercent = 80f;
            restUntilManaPercent = 60f;
        }

        /// <summary>
        /// Check if seal is active using server-authoritative aura state
        /// </summary>
        private bool HasActiveSeal(AutomatedGame game) => game.HasBuff(SEAL_OF_RIGHTEOUSNESS);

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

            // Priority 1: Heal self if low health
            if (player.HealthPercent < 40 && player.Mana >= 35)
            {
                TryCastSpellOnSelf(game, HOLY_LIGHT, HOLY_LIGHT_CAST_TIME);
                return;
            }

            // Priority 2: Divine Protection if very low health (instant)
            if (player.HealthPercent < 25 && CanCastSpell(game, DIVINE_PROTECTION, 20))
            {
                TryCastSpellOnSelf(game, DIVINE_PROTECTION);
                return;
            }

            // Priority 3: Apply Seal of Righteousness if not active (uses server aura state)
            if (!HasActiveSeal(game) && player.Mana >= 20)
            {
                TryCastSpellOnSelf(game, SEAL_OF_RIGHTEOUSNESS);
                return;
            }

            // Priority 4: Judgement when seal is active (uses server cooldown state)
            // Judgement has ~8-10 second cooldown - now tracked via SMSG_SPELL_COOLDOWN
            if (HasActiveSeal(game) && CanCastSpell(game, JUDGEMENT, 30))
            {
                TryCastSpell(game, JUDGEMENT, target.GUID);
                return;
            }

            // Priority 5: Hammer of Justice to stun (instant, useful for healing window)
            if (player.HealthPercent < 50 && CanCastSpell(game, HAMMER_OF_JUSTICE, 20))
            {
                TryCastSpell(game, HAMMER_OF_JUSTICE, target.GUID);
                return;
            }

            // Otherwise just auto-attack (melee damage)
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

            // Apply Devotion Aura if not active (uses server aura state)
            if (!game.HasBuff(DEVOTION_AURA) && player.Mana >= 20)
            {
                TryCastSpellOnSelf(game, DEVOTION_AURA);
            }

            // Apply Blessing of Might if not active (uses server aura state)
            if (!game.HasBuff(BLESSING_OF_MIGHT) && player.Mana >= 30)
            {
                TryCastSpellOnSelf(game, BLESSING_OF_MIGHT);
            }

            // Apply Seal during rest if not active (instant, so we're ready for combat)
            if (!HasActiveSeal(game) && player.Mana >= 20)
            {
                TryCastSpellOnSelf(game, SEAL_OF_RIGHTEOUSNESS);
            }

            // Heal up if injured
            if (player.HealthPercent < restUntilHealthPercent && player.Mana >= 35)
            {
                TryCastSpellOnSelf(game, HOLY_LIGHT, HOLY_LIGHT_CAST_TIME);
                return false;
            }

            return base.OnRest(game);
        }

        public override float GetPreferredCombatRange()
        {
            return 4.0f; // Melee
        }
    }
}
