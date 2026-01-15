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
        
        public TaskRoute Route => route;
        public int CurrentTaskIndex => currentTaskIndex;
        public ITask CurrentTask => currentTask;
        public bool IsComplete => currentTaskIndex >= route.Tasks.Count && !route.Loop;
        
        public TaskExecutorAI(TaskRoute route)
        {
            this.route = route ?? throw new ArgumentNullException(nameof(route));
        }
        
        public bool Activate(AutomatedGame game)
        {
            this.game = game;
            isActive = true;
            
            if (route.Tasks.Count == 0)
            {
                game.Log($"TaskExecutorAI: Route '{route.Name}' has no tasks", LogLevel.Warning);
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
                        currentTask.Cleanup(game);
                        StartNextTask();
                        break;
                        
                    case TaskResult.Failed:
                        game.Log($"TaskExecutorAI: Task '{currentTask.Name}' failed", LogLevel.Error);
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
                        }
                        break;
                        
                    case TaskResult.Skipped:
                        game.Log($"TaskExecutorAI: Task '{currentTask.Name}' was skipped", LogLevel.Info);
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
                currentTask?.Cleanup(game);
                isActive = false;
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
                    return;
                }
            }
            
            currentTask = route.Tasks[currentTaskIndex];
            game.Log($"TaskExecutorAI: Starting task {currentTaskIndex + 1}/{route.Tasks.Count}: '{currentTask.Name}'", LogLevel.Info);
            
            if (!currentTask.Start(game))
            {
                game.Log($"TaskExecutorAI: Task '{currentTask.Name}' failed to start", LogLevel.Error);
                currentTask.Cleanup(game);
                
                if (route.Loop)
                {
                    currentTaskIndex = -1;
                    StartNextTask();
                }
                else
                {
                    isActive = false;
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
    }
}
