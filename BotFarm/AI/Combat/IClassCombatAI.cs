using Client;
using Client.World.Definitions;
using Client.World.Entities;
using System;

namespace BotFarm.AI.Combat
{
    /// <summary>
    /// Interface for class-specific combat behaviors.
    /// Each class (Warrior, Priest, etc.) implements this to define its combat rotation.
    /// </summary>
    public interface IClassCombatAI
    {
        /// <summary>
        /// Called when combat starts with a new target
        /// </summary>
        void OnCombatStart(AutomatedGame game, WorldObject target);

        /// <summary>
        /// Called every update tick while in combat
        /// </summary>
        void OnCombatUpdate(AutomatedGame game, WorldObject target);

        /// <summary>
        /// Called when combat ends
        /// </summary>
        void OnCombatEnd(AutomatedGame game);

        /// <summary>
        /// Called when resting between fights
        /// Returns true if ready to fight, false if still resting
        /// </summary>
        bool OnRest(AutomatedGame game);

        /// <summary>
        /// Returns the ideal combat range for this class (melee = 5, ranged = 30, etc.)
        /// </summary>
        float GetPreferredCombatRange();

        /// <summary>
        /// Returns true if the class needs to rest (low mana, etc.)
        /// </summary>
        bool NeedsRest(AutomatedGame game);
    }

    /// <summary>
    /// Base implementation with common combat logic
    /// </summary>
    public abstract class BaseClassCombatAI : IClassCombatAI
    {
        protected float lowHealthThreshold = 30f;
        protected float lowManaThreshold = 20f;
        protected float restUntilHealthPercent = 80f;
        protected float restUntilManaPercent = 80f;

        // Fallback cast tracking (used when server state not yet received)
        protected DateTime castEndTime = DateTime.MinValue;

        /// <summary>
        /// Checks if the player is currently casting a spell.
        /// Uses server-authoritative state (IsCastingSpell) when available,
        /// with fallback to local tracking for compatibility.
        /// </summary>
        protected bool IsCasting(AutomatedGame game)
        {
            // Prefer server-authoritative state
            if (game.IsCastingSpell)
                return true;
            // Fallback to local tracking for cases where server packets haven't arrived yet
            return DateTime.Now < castEndTime;
        }

        public virtual void OnCombatStart(AutomatedGame game, WorldObject target)
        {
            // Default: start auto-attack
            game.StartAttack(target.GUID);
        }

        public abstract void OnCombatUpdate(AutomatedGame game, WorldObject target);

        public virtual void OnCombatEnd(AutomatedGame game)
        {
            game.StopAttack();
        }

        public virtual bool OnRest(AutomatedGame game)
        {
            // Default rest logic - wait until health/mana are restored
            var player = game.Player;

            if (player.HealthPercent < restUntilHealthPercent)
                return false;

            // Only check mana for classes that use it
            if (player.MaxMana > 0 && player.ManaPercent < restUntilManaPercent)
                return false;

            return true;
        }

        public virtual float GetPreferredCombatRange()
        {
            return 5.0f; // Default melee range
        }

        public virtual bool NeedsRest(AutomatedGame game)
        {
            var player = game.Player;

            // Rest if low health
            if (player.HealthPercent < lowHealthThreshold)
                return true;

            // Rest if low mana (for mana users)
            if (player.MaxMana > 0 && player.ManaPercent < lowManaThreshold)
                return true;

            return false;
        }

        /// <summary>
        /// Helper to check if a spell can be cast (cooldown, mana, etc.)
        /// Checks both mana cost and server-authoritative cooldown state.
        /// </summary>
        /// <param name="game">The game instance</param>
        /// <param name="spellId">The spell ID to check</param>
        /// <param name="manaCost">Mana cost of the spell</param>
        protected bool CanCastSpell(AutomatedGame game, uint spellId, uint manaCost)
        {
            if (game.Player.Mana < manaCost)
                return false;
            if (game.IsSpellOnCooldown(spellId))
                return false;
            return true;
        }

        /// <summary>
        /// Cast a spell on self with cooldown and cast state tracking.
        /// Returns true if cast was initiated, false if already casting or on cooldown.
        /// </summary>
        /// <param name="game">The game instance</param>
        /// <param name="spellId">The spell ID to cast</param>
        /// <param name="castTimeSeconds">Cast time in seconds (0 for instant). Used as fallback tracking.</param>
        protected bool TryCastSpellOnSelf(AutomatedGame game, uint spellId, float castTimeSeconds = 0f)
        {
            if (IsCasting(game))
                return false;

            // Check cooldown
            if (game.IsSpellOnCooldown(spellId))
                return false;

            game.CastSpellOnSelf(spellId);
            // Set short fallback to cover network round-trip until server confirms cast start
            // Server will set IsCastingSpell=true via SMSG_SPELL_START, or clear via SMSG_SPELL_FAILURE
            // We only need fallback for the brief period before server response arrives
            if (castTimeSeconds > 0)
                castEndTime = DateTime.Now.AddSeconds(0.5);
            return true;
        }

        /// <summary>
        /// Cast a spell on target with cooldown and cast state tracking.
        /// Returns true if cast was initiated, false if already casting or on cooldown.
        /// </summary>
        /// <param name="game">The game instance</param>
        /// <param name="spellId">The spell ID to cast</param>
        /// <param name="targetGuid">The target GUID</param>
        /// <param name="castTimeSeconds">Cast time in seconds (0 for instant). Used as fallback tracking.</param>
        protected bool TryCastSpell(AutomatedGame game, uint spellId, ulong targetGuid, float castTimeSeconds = 0f)
        {
            if (IsCasting(game))
                return false;

            // Check cooldown
            if (game.IsSpellOnCooldown(spellId))
                return false;

            game.CastSpell(spellId, targetGuid);
            // Set short fallback to cover network round-trip until server confirms cast start
            // Server will set IsCastingSpell=true via SMSG_SPELL_START, or clear via SMSG_SPELL_FAILURE
            // We only need fallback for the brief period before server response arrives
            if (castTimeSeconds > 0)
                castEndTime = DateTime.Now.AddSeconds(0.5);
            return true;
        }
    }
}
