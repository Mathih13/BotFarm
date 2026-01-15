using Client;
using Client.UI;
using Client.World.Entities;

namespace BotFarm.AI.Combat
{
    /// <summary>
    /// Warrior combat AI - basic melee with rage abilities
    /// </summary>
    public class WarriorCombatAI : BaseClassCombatAI
    {
        // Warrior spell IDs (level 1-10 abilities)
        private const uint BATTLE_SHOUT = 6673;      // Buff, 10 rage
        private const uint HEROIC_STRIKE = 78;       // Next melee +11 damage, 15 rage
        private const uint REND = 772;               // DoT, 10 rage
        private const uint THUNDER_CLAP = 6343;      // AoE slow, 20 rage (level 6)
        private const uint CHARGE = 100;             // Gap closer, generates 15 rage (level 4)
        
        private bool hasBattleShout = false;
        private bool hasAppliedRend = false;

        public WarriorCombatAI()
        {
            lowHealthThreshold = 20f;  // Warriors can take more damage
            restUntilHealthPercent = 70f;
        }

        public override void OnCombatStart(AutomatedGame game, WorldObject target)
        {
            base.OnCombatStart(game, target);
            hasAppliedRend = false;
        }

        public override void OnCombatUpdate(AutomatedGame game, WorldObject target)
        {
            var player = game.Player;
            uint rage = player.Rage;
            
            // Priority 1: Battle Shout if not active (buff)
            if (!hasBattleShout && rage >= 10)
            {
                game.CastSpellOnSelf(BATTLE_SHOUT);
                hasBattleShout = true;
                return;
            }
            
            // Priority 2: Rend if not applied (DoT for damage over time)
            if (!hasAppliedRend && rage >= 10)
            {
                game.CastSpell(REND, target.GUID);
                hasAppliedRend = true;
                return;
            }
            
            // Priority 3: Heroic Strike as rage dump
            if (rage >= 15)
            {
                game.CastSpell(HEROIC_STRIKE, target.GUID);
                return;
            }
            
            // Otherwise just auto-attack (handled by server once attack started)
        }

        public override void OnCombatEnd(AutomatedGame game)
        {
            base.OnCombatEnd(game);
            hasAppliedRend = false;
        }

        public override bool NeedsRest(AutomatedGame game)
        {
            // Warriors only need to rest for health, not rage
            return game.Player.HealthPercent < lowHealthThreshold;
        }

        public override float GetPreferredCombatRange()
        {
            return 4.0f; // Melee
        }
    }
}
