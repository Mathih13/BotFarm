using System;

namespace Client.AI.Tasks
{
    public enum TaskResult
    {
        Running,    // Task is still executing
        Success,    // Task completed successfully
        Failed,     // Task failed and cannot continue
        Skipped     // Task was skipped (optional tasks)
    }

    public interface ITask
    {
        string Name { get; }

        /// <summary>
        /// Error message set when task fails. Used by test framework to report detailed failures.
        /// </summary>
        string ErrorMessage { get; }

        /// <summary>
        /// Called once when task starts. Return false to fail immediately.
        /// </summary>
        bool Start(AutomatedGame game);

        /// <summary>
        /// Called every update tick while task is running.
        /// </summary>
        TaskResult Update(AutomatedGame game);

        /// <summary>
        /// Called when task is complete or interrupted.
        /// </summary>
        void Cleanup(AutomatedGame game);
    }

    public abstract class BaseTask : ITask
    {
        public abstract string Name { get; }

        /// <summary>
        /// Error message to report when task fails. Set this before returning TaskResult.Failed.
        /// </summary>
        public string ErrorMessage { get; protected set; }

        protected TaskResult currentResult = TaskResult.Running;

        // Delay padding to reduce server load
        protected float preDelaySeconds = 0f;
        protected float postDelaySeconds = 0f;
        private DateTime? delayStartTime;
        private bool preDelayComplete;
        private bool taskComplete;
        private TaskResult taskFinalResult;
        private static readonly Random sharedRandom = new Random();

        /// <summary>
        /// Set delay padding for this task. Called by task constructors.
        /// Adds random variance (0-50% extra) to simulate natural player behavior.
        /// </summary>
        protected void SetDelayPadding(float preDelay, float postDelay)
        {
            // Add 0-50% random variance to each delay
            float preVariance = preDelay * (float)(sharedRandom.NextDouble() * 0.5);
            float postVariance = postDelay * (float)(sharedRandom.NextDouble() * 0.5);
            preDelaySeconds = preDelay + preVariance;
            postDelaySeconds = postDelay + postVariance;
        }

        /// <summary>
        /// Generate a random delay between min and max seconds
        /// </summary>
        protected static float RandomDelay(float min, float max)
        {
            return min + (float)(new Random().NextDouble() * (max - min));
        }
        
        public virtual bool Start(AutomatedGame game)
        {
            currentResult = TaskResult.Running;
            preDelayComplete = preDelaySeconds <= 0;
            taskComplete = false;
            delayStartTime = preDelaySeconds > 0 ? DateTime.Now : (DateTime?)null;
            return true;
        }

        /// <summary>
        /// Wrapper around Update that handles pre/post delays
        /// </summary>
        public TaskResult Update(AutomatedGame game)
        {
            // Handle pre-delay
            if (!preDelayComplete)
            {
                if ((DateTime.Now - delayStartTime.Value).TotalSeconds >= preDelaySeconds)
                {
                    preDelayComplete = true;
                    delayStartTime = null;
                }
                else
                {
                    return TaskResult.Running;
                }
            }

            // Run the actual task
            if (!taskComplete)
            {
                var result = UpdateTask(game);
                if (result != TaskResult.Running)
                {
                    taskComplete = true;
                    taskFinalResult = result;

                    // Start post-delay if needed
                    if (postDelaySeconds > 0)
                    {
                        delayStartTime = DateTime.Now;
                    }
                }
                else
                {
                    return TaskResult.Running;
                }
            }

            // Handle post-delay
            if (postDelaySeconds > 0 && delayStartTime.HasValue)
            {
                if ((DateTime.Now - delayStartTime.Value).TotalSeconds >= postDelaySeconds)
                {
                    return taskFinalResult;
                }
                return TaskResult.Running;
            }

            return taskFinalResult;
        }

        /// <summary>
        /// Override this instead of Update() to implement task logic
        /// </summary>
        protected abstract TaskResult UpdateTask(AutomatedGame game);
        
        public virtual void Cleanup(AutomatedGame game)
        {
            // Default empty cleanup
        }
    }
}
