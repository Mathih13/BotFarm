using System;
using System.Collections.Generic;

namespace Client.AI.Tasks
{
    /// <summary>
    /// Event args for when a single task completes within a route
    /// </summary>
    public class TaskCompletedEventArgs : EventArgs
    {
        public ITask Task { get; init; }
        public TaskResult Result { get; init; }
        public TimeSpan Duration { get; init; }
        public string ErrorMessage { get; init; }
        public int TaskIndex { get; init; }
        public int TotalTasks { get; init; }

        public TaskCompletedEventArgs(ITask task, TaskResult result, TimeSpan duration, int taskIndex, int totalTasks, string errorMessage = null)
        {
            Task = task;
            Result = result;
            Duration = duration;
            TaskIndex = taskIndex;
            TotalTasks = totalTasks;
            ErrorMessage = errorMessage;
        }
    }

    /// <summary>
    /// Event args for when an entire route completes (success or failure)
    /// </summary>
    public class RouteCompletedEventArgs : EventArgs
    {
        public TaskRoute Route { get; init; }
        public bool Success { get; init; }
        public int TasksCompleted { get; init; }
        public int TasksFailed { get; init; }
        public int TasksSkipped { get; init; }
        public TimeSpan TotalDuration { get; init; }
        public List<TaskCompletedEventArgs> TaskResults { get; init; }
        public string ErrorMessage { get; init; }

        public RouteCompletedEventArgs(
            TaskRoute route,
            bool success,
            int tasksCompleted,
            int tasksFailed,
            int tasksSkipped,
            TimeSpan totalDuration,
            List<TaskCompletedEventArgs> taskResults,
            string errorMessage = null)
        {
            Route = route;
            Success = success;
            TasksCompleted = tasksCompleted;
            TasksFailed = tasksFailed;
            TasksSkipped = tasksSkipped;
            TotalDuration = totalDuration;
            TaskResults = taskResults ?? new List<TaskCompletedEventArgs>();
            ErrorMessage = errorMessage;
        }
    }
}
