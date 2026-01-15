using Client;
using Client.AI.Tasks;
using Client.UI;

namespace BotFarm.AI.Tasks
{
    /// <summary>
    /// Assertion task that verifies a quest is in the player's quest log.
    /// Fails immediately if the quest is not found.
    /// </summary>
    public class AssertQuestInLogTask : BaseTask
    {
        private readonly uint questId;
        private readonly string message;

        public override string Name => $"AssertQuestInLog({questId})";

        /// <summary>
        /// Create an assertion that checks if a quest is in the player's quest log.
        /// </summary>
        /// <param name="questId">The quest ID to check for</param>
        /// <param name="message">Optional message to display on failure</param>
        public AssertQuestInLogTask(uint questId, string message = null)
        {
            this.questId = questId;
            this.message = message;
        }

        public override bool Start(AutomatedGame game)
        {
            if (!base.Start(game))
                return false;

            if (questId == 0)
            {
                game.Log("AssertQuestInLog: Quest ID cannot be 0", LogLevel.Error);
                return false;
            }

            return true;
        }

        protected override TaskResult UpdateTask(AutomatedGame game)
        {
            bool isInLog = game.IsQuestInLog(questId);

            if (isInLog)
            {
                game.Log($"AssertQuestInLog: PASS - Quest {questId} is in log", LogLevel.Info);
                return TaskResult.Success;
            }
            else
            {
                string failMessage = string.IsNullOrEmpty(message)
                    ? $"AssertQuestInLog: FAIL - Quest {questId} is NOT in log"
                    : $"AssertQuestInLog: FAIL - {message} (Quest {questId} not in log)";
                game.Log(failMessage, LogLevel.Error);
                return TaskResult.Failed;
            }
        }
    }
}
