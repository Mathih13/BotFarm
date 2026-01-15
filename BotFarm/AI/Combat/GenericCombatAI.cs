using Client;
using Client.UI;
using Client.World.Entities;

namespace BotFarm.AI.Combat
{
    /// <summary>
    /// Generic combat AI that just auto-attacks.
    /// Used as fallback for classes without specific implementations.
    /// </summary>
    public class GenericCombatAI : BaseClassCombatAI
    {
        public GenericCombatAI()
        {
            lowHealthThreshold = 30f;
            restUntilHealthPercent = 80f;
        }

        public override void OnCombatStart(AutomatedGame game, WorldObject target)
        {
            base.OnCombatStart(game, target);
        }

        public override void OnCombatUpdate(AutomatedGame game, WorldObject target)
        {
            // Just auto-attack - no special abilities
            // Make sure we're still attacking
            if (!game.IsInCombat)
            {
                game.StartAttack(target.GUID);
            }
        }

        public override void OnCombatEnd(AutomatedGame game)
        {
            base.OnCombatEnd(game);
        }

        public override float GetPreferredCombatRange()
        {
            return 5.0f; // Default melee
        }
    }
}
