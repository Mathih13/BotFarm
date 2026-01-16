using Client.UI;
using System;
using System.Collections.Generic;

namespace Client.AI.Tasks
{
    public class TaskExecutorAI : IStrategicAI
    {
        private AutomatedGame game;
        private TaskRoute route;
        private int currentTaskIndex = -1;
        private ITask currentTask = null;
        private bool isActive = false;

        // Event system for test framework
        public event EventHandler<TaskCompletedEventArgs> TaskCompleted;
        public event EventHandler<RouteCompletedEventArgs> RouteCompleted;

        // Timing tracking
        private DateTime routeStartTime;
        private DateTime taskStartTime;
        private List<TaskCompletedEventArgs> taskResults = new List<TaskCompletedEventArgs>();
        private int tasksCompleted = 0;
        private int tasksFailed = 0;
        private int tasksSkipped = 0;

        public TaskRoute Route => route;
        public int CurrentTaskIndex => currentTaskIndex;
        public ITask CurrentTask => currentTask;
        public bool IsComplete => currentTaskIndex >= route.Tasks.Count && !route.Loop;
        public IReadOnlyList<TaskCompletedEventArgs> TaskResults => taskResults;
        
        public TaskExecutorAI(TaskRoute route)
        {
            this.route = route ?? throw new ArgumentNullException(nameof(route));
        }
        
        public bool Activate(AutomatedGame game)
        {
            this.game = game;
            isActive = true;
            routeStartTime = DateTime.UtcNow;
            taskResults.Clear();
            tasksCompleted = 0;
            tasksFailed = 0;
            tasksSkipped = 0;

            if (route.Tasks.Count == 0)
            {
                game.Log($"TaskExecutorAI: Route '{route.Name}' has no tasks", LogLevel.Warning);
                FireRouteCompleted(true, null);
                return false;
            }

            game.Log($"TaskExecutorAI: Starting route '{route.Name}' with {route.Tasks.Count} tasks", LogLevel.Info);
            StartNextTask();
            return true;
        }
        
        public void Update()
        {
            if (!isActive || currentTask == null)
                return;

            try
            {
                var result = currentTask.Update(game);

                switch (result)
                {
                    case TaskResult.Success:
                        game.Log($"TaskExecutorAI: Task '{currentTask.Name}' completed successfully", LogLevel.Info);
                        FireTaskCompleted(currentTask, TaskResult.Success, null);
                        tasksCompleted++;
                        currentTask.Cleanup(game);
                        StartNextTask();
                        break;

                    case TaskResult.Failed:
                        var errorMsg = currentTask.ErrorMessage ?? "Task failed";
                        game.Log($"TaskExecutorAI: Task '{currentTask.Name}' failed: {errorMsg}", LogLevel.Error);
                        FireTaskCompleted(currentTask, TaskResult.Failed, errorMsg);
                        tasksFailed++;
                        currentTask.Cleanup(game);

                        if (route.Loop)
                        {
                            game.Log($"TaskExecutorAI: Route is looped, restarting from beginning", LogLevel.Info);
                            currentTaskIndex = -1;
                            StartNextTask();
                        }
                        else
                        {
                            isActive = false;
                            game.Log($"TaskExecutorAI: Route '{route.Name}' failed", LogLevel.Error);
                            FireRouteCompleted(false, $"Task '{currentTask.Name}' failed");
                        }
                        break;

                    case TaskResult.Skipped:
                        game.Log($"TaskExecutorAI: Task '{currentTask.Name}' was skipped", LogLevel.Info);
                        FireTaskCompleted(currentTask, TaskResult.Skipped, null);
                        tasksSkipped++;
                        currentTask.Cleanup(game);
                        StartNextTask();
                        break;

                    case TaskResult.Running:
                        // Continue running
                        break;
                }
            }
            catch (Exception ex)
            {
                game.LogException($"TaskExecutorAI: Exception in task '{currentTask.Name}': {ex.Message}");
                FireTaskCompleted(currentTask, TaskResult.Failed, ex.Message);
                tasksFailed++;
                currentTask?.Cleanup(game);
                isActive = false;
                FireRouteCompleted(false, $"Exception in task '{currentTask?.Name}': {ex.Message}");
            }
        }
        
        private void StartNextTask()
        {
            currentTaskIndex++;

            // Check if route is complete
            if (currentTaskIndex >= route.Tasks.Count)
            {
                if (route.Loop)
                {
                    game.Log($"TaskExecutorAI: Route '{route.Name}' completed, looping back to start", LogLevel.Info);
                    currentTaskIndex = 0;
                }
                else
                {
                    game.Log($"TaskExecutorAI: Route '{route.Name}' completed successfully", LogLevel.Info);
                    currentTask = null;
                    isActive = false;
                    FireRouteCompleted(true, null);
                    return;
                }
            }

            currentTask = route.Tasks[currentTaskIndex];
            taskStartTime = DateTime.UtcNow;
            game.Log($"TaskExecutorAI: Starting task {currentTaskIndex + 1}/{route.Tasks.Count}: '{currentTask.Name}'", LogLevel.Info);

            if (!currentTask.Start(game))
            {
                game.Log($"TaskExecutorAI: Task '{currentTask.Name}' failed to start", LogLevel.Error);
                FireTaskCompleted(currentTask, TaskResult.Failed, "Failed to start");
                tasksFailed++;
                currentTask.Cleanup(game);

                if (route.Loop)
                {
                    currentTaskIndex = -1;
                    StartNextTask();
                }
                else
                {
                    isActive = false;
                    FireRouteCompleted(false, $"Task '{currentTask.Name}' failed to start");
                }
            }
        }
        
        public void Deactivate()
        {
            isActive = false;
            currentTask?.Cleanup(game);
            game.Log($"TaskExecutorAI: Route '{route.Name}' deactivated", LogLevel.Info);
        }
        
        public void Pause()
        {
            isActive = false;
            game.Log($"TaskExecutorAI: Route '{route.Name}' paused", LogLevel.Info);
        }
        
        public void Resume()
        {
            isActive = true;
            game.Log($"TaskExecutorAI: Route '{route.Name}' resumed", LogLevel.Info);
        }
        
        public bool AllowPause()
        {
            return true;
        }

        private void FireTaskCompleted(ITask task, TaskResult result, string errorMessage)
        {
            var duration = DateTime.UtcNow - taskStartTime;
            var eventArgs = new TaskCompletedEventArgs(
                task,
                result,
                duration,
                currentTaskIndex,
                route.Tasks.Count,
                errorMessage
            );
            taskResults.Add(eventArgs);
            TaskCompleted?.Invoke(this, eventArgs);
        }

        private void FireRouteCompleted(bool success, string errorMessage)
        {
            var totalDuration = DateTime.UtcNow - routeStartTime;
            var eventArgs = new RouteCompletedEventArgs(
                route,
                success,
                tasksCompleted,
                tasksFailed,
                tasksSkipped,
                totalDuration,
                new List<TaskCompletedEventArgs>(taskResults),
                errorMessage
            );
            RouteCompleted?.Invoke(this, eventArgs);
        }
    }
}
