using Client;
using Client.UI;
using Client.World.Entities;
using System;

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
        
        // Seal lasts 30 seconds, but we'll refresh more conservatively
        private const float SEAL_DURATION_SECONDS = 25f;
        
        private DateTime sealAppliedTime = DateTime.MinValue;
        private DateTime lastJudgementTime = DateTime.MinValue;
        private bool hasBlessing = false;
        private bool hasAura = false;

        public PaladinCombatAI()
        {
            lowHealthThreshold = 30f;  // Paladins can heal themselves
            lowManaThreshold = 20f;
            restUntilHealthPercent = 80f;
            restUntilManaPercent = 60f;
        }

        private bool HasActiveSeal => (DateTime.Now - sealAppliedTime).TotalSeconds < SEAL_DURATION_SECONDS;

        public override void OnCombatStart(AutomatedGame game, WorldObject target)
        {
            base.OnCombatStart(game, target);
            // Don't reset seal timer - seal persists between combats
        }

        public override void OnCombatUpdate(AutomatedGame game, WorldObject target)
        {
            var player = game.Player;

            // Don't interrupt current cast
            if (IsCasting)
                return;

            // Priority 1: Heal self if low health
            if (player.HealthPercent < 40 && player.Mana >= 35)
            {
                TryCastSpellOnSelf(game, HOLY_LIGHT, HOLY_LIGHT_CAST_TIME);
                return;
            }

            // Priority 2: Divine Protection if very low health (instant)
            if (player.HealthPercent < 25 && player.Mana >= 20)
            {
                game.CastSpellOnSelf(DIVINE_PROTECTION);
                return;
            }

            // Priority 3: Apply Seal of Righteousness if not active or expired (instant)
            if (!HasActiveSeal && player.Mana >= 20)
            {
                game.CastSpellOnSelf(SEAL_OF_RIGHTEOUSNESS);
                sealAppliedTime = DateTime.Now;
                return;
            }

            // Priority 4: Judgement when seal is active (instant, doesn't consume seal in WotLK)
            // Add cooldown check - Judgement has ~8-10 second cooldown
            if (HasActiveSeal && player.Mana >= 30 && (DateTime.Now - lastJudgementTime).TotalSeconds > 10)
            {
                game.CastSpell(JUDGEMENT, target.GUID);
                lastJudgementTime = DateTime.Now;
                return;
            }

            // Priority 5: Hammer of Justice to stun (instant, useful for healing window)
            if (player.HealthPercent < 50 && player.Mana >= 20)
            {
                game.CastSpell(HAMMER_OF_JUSTICE, target.GUID);
                return;
            }

            // Otherwise just auto-attack (melee damage)
        }

        public override void OnCombatEnd(AutomatedGame game)
        {
            base.OnCombatEnd(game);
            // Don't reset seal - it persists
        }

        public override bool OnRest(AutomatedGame game)
        {
            var player = game.Player;

            // Don't interrupt current cast
            if (IsCasting)
                return false;

            // Apply Devotion Aura if not active (instant)
            if (!hasAura && player.Mana >= 20)
            {
                game.CastSpellOnSelf(DEVOTION_AURA);
                hasAura = true;
            }

            // Apply Blessing of Might if not active (instant)
            if (!hasBlessing && player.Mana >= 30)
            {
                game.CastSpellOnSelf(BLESSING_OF_MIGHT);
                hasBlessing = true;
            }

            // Apply Seal during rest if not active (instant, so we're ready for combat)
            if (!HasActiveSeal && player.Mana >= 20)
            {
                game.CastSpellOnSelf(SEAL_OF_RIGHTEOUSNESS);
                sealAppliedTime = DateTime.Now;
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
