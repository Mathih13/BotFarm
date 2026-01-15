using Client.UI;
using System;

namespace Client.AI.Tasks
{
    public class LogMessageTask : BaseTask
    {
        private readonly string message;
        private readonly LogLevel logLevel;
        
        public override string Name => $"LogMessage";
        
        public LogMessageTask(string message, LogLevel logLevel = LogLevel.Info)
        {
            this.message = message;
            this.logLevel = logLevel;
        }
        
        public override bool Start(AutomatedGame game)
        {
            game.Log($"LogMessage: {message}", logLevel);
            return true;
        }
        
        protected override TaskResult UpdateTask(AutomatedGame game)
        {
            return TaskResult.Success;
        }
    }
}
