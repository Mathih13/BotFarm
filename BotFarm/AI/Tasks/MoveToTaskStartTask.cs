using System;
using Client;
using Client.AI.Tasks;
using Client.World.Entities;

namespace BotFarm.AI.Tasks
{
    /// <summary>
    /// Task that teleports the bot back to a previous task's starting position.
    /// Useful for recovery scenarios when bots get stuck.
    /// </summary>
    public class MoveToTaskStartTask : BaseTask
    {
        private readonly int? absoluteTaskIndex;
        private readonly int? relativeOffset;
        private Position targetPosition;
        private bool teleportInitiated = false;
        private DateTime teleportTime;

        public override string Name => absoluteTaskIndex.HasValue
            ? $"MoveToTaskStart[{absoluteTaskIndex}]"
            : $"MoveToTaskStart[offset:{relativeOffset ?? -1}]";

        /// <summary>
        /// Create a MoveToTaskStart task
        /// </summary>
        /// <param name="taskIndex">Absolute task index to return to (0-based)</param>
        /// <param name="relativeOffset">Offset relative to current task (e.g., -1 for previous task)</param>
        public MoveToTaskStartTask(int? taskIndex = null, int? relativeOffset = null)
        {
            this.absoluteTaskIndex = taskIndex;
            // Default to previous task (-1) if neither is specified
            this.relativeOffset = relativeOffset ?? (taskIndex.HasValue ? null : -1);
        }

        public override bool Start(AutomatedGame game)
        {
            var executor = GetTaskExecutor(game);
            if (executor == null)
            {
                ErrorMessage = "No active route executor found";
                return false;
            }

            int targetIndex = absoluteTaskIndex ?? (executor.CurrentTaskIndex + (relativeOffset ?? -1));
            targetPosition = executor.GetTaskStartPosition(targetIndex);

            if (targetPosition == null)
            {
                ErrorMessage = $"No start position recorded for task index {targetIndex}";
                return false;
            }

            return base.Start(game);
        }

        protected override TaskResult UpdateTask(AutomatedGame game)
        {
            if (!teleportInitiated)
            {
                game.Log($"MoveToTaskStart: Teleporting to task start position ({targetPosition.X:F1}, {targetPosition.Y:F1}, {targetPosition.Z:F1})");
                game.TeleportToPosition(targetPosition.X, targetPosition.Y, targetPosition.Z, (uint)targetPosition.MapID);
                teleportInitiated = true;
                teleportTime = DateTime.UtcNow;
                return TaskResult.Running;
            }

            // Verify teleport completed
            var currentPos = game.Player.GetPosition();
            float distance = (currentPos - targetPosition).Length;

            if (distance < 50f) // GM teleport tolerance
            {
                game.Log($"MoveToTaskStart: Arrived at task start position");
                return TaskResult.Success;
            }

            if ((DateTime.UtcNow - teleportTime).TotalSeconds > 5)
            {
                ErrorMessage = $"Teleport verification timeout - still {distance:F1} yards from target";
                return TaskResult.Failed;
            }

            return TaskResult.Running;
        }

        private TaskExecutorAI GetTaskExecutor(AutomatedGame game)
        {
            var botGame = game as BotGame;
            return botGame?.GetRouteExecutor();
        }
    }
}
