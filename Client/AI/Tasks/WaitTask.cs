using Client.UI;
using System;

namespace Client.AI.Tasks
{
    public class WaitTask : BaseTask
    {
        private readonly TimeSpan duration;
        private DateTime startTime;
        
        public override string Name => $"Wait({duration.TotalSeconds:F1}s)";
        
        public WaitTask(TimeSpan duration)
        {
            this.duration = duration;
        }
        
        public WaitTask(double seconds)
        {
            this.duration = TimeSpan.FromSeconds(seconds);
        }
        
        public override bool Start(AutomatedGame game)
        {
            if (!base.Start(game))
                return false;
            
            startTime = DateTime.Now;
            game.Log($"WaitTask: Waiting for {duration.TotalSeconds:F1} seconds", LogLevel.Debug);
            return true;
        }
        
        protected override TaskResult UpdateTask(AutomatedGame game)
        {
            if ((DateTime.Now - startTime) >= duration)
            {
                return TaskResult.Success;
            }
            
            return TaskResult.Running;
        }
    }
}
