using Client.UI;
using System;

namespace Client.AI.Tasks
{
    public class WaitForConditionTask : BaseTask
    {
        private readonly Func<AutomatedGame, bool> condition;
        private readonly string conditionDescription;
        private readonly TimeSpan timeout;
        private DateTime startTime;
        
        public override string Name => $"WaitForCondition({conditionDescription})";
        
        public WaitForConditionTask(Func<AutomatedGame, bool> condition, string description = "condition", TimeSpan timeout = default)
        {
            this.condition = condition ?? throw new ArgumentNullException(nameof(condition));
            this.conditionDescription = description;
            this.timeout = timeout == default ? TimeSpan.FromMinutes(5) : timeout;
        }
        
        public override bool Start(AutomatedGame game)
        {
            if (!base.Start(game))
                return false;
            
            startTime = DateTime.Now;
            game.Log($"WaitForConditionTask: Waiting for {conditionDescription}", LogLevel.Debug);
            return true;
        }
        
        protected override TaskResult UpdateTask(AutomatedGame game)
        {
            if ((DateTime.Now - startTime) > timeout)
            {
                game.Log($"WaitForConditionTask: Timeout waiting for {conditionDescription}", LogLevel.Warning);
                return TaskResult.Failed;
            }
            
            try
            {
                if (condition(game))
                {
                    game.Log($"WaitForConditionTask: Condition '{conditionDescription}' met", LogLevel.Debug);
                    return TaskResult.Success;
                }
            }
            catch (Exception ex)
            {
                game.LogException($"WaitForConditionTask: Exception checking condition: {ex.Message}");
                return TaskResult.Failed;
            }
            
            return TaskResult.Running;
        }
    }
}
