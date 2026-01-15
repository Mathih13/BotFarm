using Client;
using Client.AI.Tasks;
using Client.UI;

namespace BotFarm.AI.Tasks
{
    /// <summary>
    /// Assertion task that verifies a quest is NOT in the player's quest log.
    /// Useful for verifying quest completion or that a quest was never accepted.
    /// Fails immediately if the quest is found.
    /// </summary>
    public class AssertQuestNotInLogTask : BaseTask
    {
        private readonly uint questId;
        private readonly string message;

        public override string Name => $"AssertQuestNotInLog({questId})";

        /// <summary>
        /// Create an assertion that checks if a quest is NOT in the player's quest log.
        /// </summary>
        /// <param name="questId">The quest ID to check for absence</param>
        /// <param name="message">Optional message to display on failure</param>
        public AssertQuestNotInLogTask(uint questId, string message = null)
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
                game.Log("AssertQuestNotInLog: Quest ID cannot be 0", LogLevel.Error);
                return false;
            }

            return true;
        }

        protected override TaskResult UpdateTask(AutomatedGame game)
        {
            bool isInLog = game.IsQuestInLog(questId);

            if (!isInLog)
            {
                game.Log($"AssertQuestNotInLog: PASS - Quest {questId} is not in log", LogLevel.Info);
                return TaskResult.Success;
            }
            else
            {
                string failMessage = string.IsNullOrEmpty(message)
                    ? $"AssertQuestNotInLog: FAIL - Quest {questId} IS still in log"
                    : $"AssertQuestNotInLog: FAIL - {message} (Quest {questId} still in log)";
                game.Log(failMessage, LogLevel.Error);
                return TaskResult.Failed;
            }
        }
    }
}
