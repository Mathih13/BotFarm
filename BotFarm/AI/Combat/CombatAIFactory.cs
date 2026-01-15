using Client.World.Definitions;

namespace BotFarm.AI.Combat
{
    /// <summary>
    /// Factory to create the appropriate combat AI for a player's class
    /// </summary>
    public static class CombatAIFactory
    {
        public static IClassCombatAI CreateForClass(Class playerClass)
        {
            return playerClass switch
            {
                Class.Warrior => new WarriorCombatAI(),
                Class.Priest => new PriestCombatAI(),
                // TODO: Add more class implementations
                Class.Paladin => new PaladinCombatAI(),
                Class.Hunter => new GenericCombatAI(),   // TODO: Implement
                Class.Rogue => new GenericCombatAI(),    // TODO: Implement
                Class.Shaman => new GenericCombatAI(),   // TODO: Implement
                Class.Mage => new GenericCombatAI(),     // TODO: Implement
                Class.Warlock => new GenericCombatAI(),  // TODO: Implement
                Class.Druid => new GenericCombatAI(),    // TODO: Implement
                Class.DeathKnight => new GenericCombatAI(), // TODO: Implement
                _ => new GenericCombatAI()
            };
        }
    }
}
