using Client;
using Client.World.Entities;

namespace BotFarm.AI.Combat
{
    /// <summary>
    /// Rogue combat AI - melee with energy and combo points
    /// </summary>
    public class RogueCombatAI : BaseClassCombatAI
    {
        // Rogue spell IDs (level 1-10 abilities)
        private const uint SINISTER_STRIKE = 1752;   // Combo point builder, 45 energy
        private const uint EVISCERATE = 2098;        // Finisher, 35 energy
        private const uint GOUGE = 1776;             // Incapacitate (level 6), 45 energy
        private const uint SLICE_AND_DICE = 5171;    // Attack speed buff (level 10)
        private const uint EVASION = 5277;           // Dodge buff (level 8)

        private bool hasSliceAndDice = false;
        private bool hasUsedEvasion = false;
        private int comboPoints = 0;

        public RogueCombatAI()
        {
            lowHealthThreshold = 25f;
            restUntilHealthPercent = 80f;
        }

        public override void OnCombatStart(AutomatedGame game, WorldObject target)
        {
            base.OnCombatStart(game, target);
            comboPoints = 0;
            hasSliceAndDice = false;
            hasUsedEvasion = false;
        }

        public override void OnCombatUpdate(AutomatedGame game, WorldObject target)
        {
            var player = game.Player;
            uint energy = player.Energy;

            // Priority 1: Evasion if health is critical
            if (!hasUsedEvasion && player.HealthPercent < 30)
            {
                game.CastSpellOnSelf(EVASION);
                hasUsedEvasion = true;
                return;
            }

            // Priority 2: Gouge if health is very low (buy time)
            if (player.HealthPercent < 25 && energy >= 45)
            {
                game.CastSpell(GOUGE, target.GUID);
                return;
            }

            // Priority 3: Slice and Dice if not active and we have 2+ combo points
            if (!hasSliceAndDice && comboPoints >= 2 && energy >= 25)
            {
                game.CastSpellOnSelf(SLICE_AND_DICE);
                hasSliceAndDice = true;
                comboPoints = 0;
                return;
            }

            // Priority 4: Eviscerate at 3-5 combo points
            if (comboPoints >= 3 && energy >= 35)
            {
                game.CastSpell(EVISCERATE, target.GUID);
                comboPoints = 0;
                return;
            }

            // Priority 5: Sinister Strike to build combo points
            if (energy >= 45)
            {
                game.CastSpell(SINISTER_STRIKE, target.GUID);
                comboPoints++;
                if (comboPoints > 5) comboPoints = 5; // Cap at 5
                return;
            }

            // Low energy - just auto-attack and wait for regen
        }

        public override void OnCombatEnd(AutomatedGame game)
        {
            base.OnCombatEnd(game);
            comboPoints = 0;
            hasSliceAndDice = false;
            hasUsedEvasion = false;
        }

        public override bool NeedsRest(AutomatedGame game)
        {
            // Rogues only need to rest for health, not energy (regenerates quickly)
            return game.Player.HealthPercent < lowHealthThreshold;
        }

        public override float GetPreferredCombatRange()
        {
            return 4.0f; // Melee
        }
    }
}
