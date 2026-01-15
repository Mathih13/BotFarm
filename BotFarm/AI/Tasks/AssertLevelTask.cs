using Client;
using Client.AI.Tasks;
using Client.UI;
using Client.World.Definitions;

namespace BotFarm.AI.Tasks
{
    /// <summary>
    /// Assertion task that verifies the player's level is at least a minimum value.
    /// Fails immediately if the player's level is below the minimum.
    /// </summary>
    public class AssertLevelTask : BaseTask
    {
        private readonly uint minLevel;
        private readonly string message;

        public override string Name => $"AssertLevel(>={minLevel})";

        /// <summary>
        /// Create an assertion that checks if the player's level is at least the minimum.
        /// </summary>
        /// <param name="minLevel">The minimum level required</param>
        /// <param name="message">Optional message to display on failure</param>
        public AssertLevelTask(uint minLevel, string message = null)
        {
            this.minLevel = minLevel;
            this.message = message;
        }

        public override bool Start(AutomatedGame game)
        {
            if (!base.Start(game))
                return false;

            if (minLevel == 0)
            {
                game.Log("AssertLevel: Minimum level cannot be 0", LogLevel.Error);
                return false;
            }

            return true;
        }

        protected override TaskResult UpdateTask(AutomatedGame game)
        {
            if (game.Player == null)
            {
                game.Log("AssertLevel: FAIL - Player is null", LogLevel.Error);
                return TaskResult.Failed;
            }

            uint currentLevel = game.Player[(int)UnitField.UNIT_FIELD_LEVEL];

            if (currentLevel >= minLevel)
            {
                game.Log($"AssertLevel: PASS - Player level {currentLevel} >= {minLevel}", LogLevel.Info);
                return TaskResult.Success;
            }
            else
            {
                string failMessage = string.IsNullOrEmpty(message)
                    ? $"AssertLevel: FAIL - Player level {currentLevel} < {minLevel}"
                    : $"AssertLevel: FAIL - {message} (Level {currentLevel} < {minLevel})";
                game.Log(failMessage, LogLevel.Error);
                return TaskResult.Failed;
            }
        }
    }
}
