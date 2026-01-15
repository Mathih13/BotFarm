using System;
using System.Collections.Generic;
using Client.AI.Tasks;

namespace BotFarm.Testing
{
    /// <summary>
    /// Status of a test run
    /// </summary>
    internal enum TestRunStatus
    {
        Pending,
        SettingUp,
        Running,
        Completed,
        Failed,
        TimedOut,
        Cancelled
    }

    /// <summary>
    /// Represents a single test run with one or more bots executing a route
    /// </summary>
    internal class TestRun
    {
        public string Id { get; }
        public string RoutePath { get; }
        public string RouteName { get; set; }
        public HarnessSettings Harness { get; }
        public DateTime StartTime { get; }
        public DateTime? EndTime { get; set; }
        public TestRunStatus Status { get; set; }
        public List<BotTestResult> BotResults { get; } = new List<BotTestResult>();
        public string ErrorMessage { get; set; }

        public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;

        public int BotsCompleted => BotResults.FindAll(r => r.IsComplete).Count;
        public int BotsPassed => BotResults.FindAll(r => r.Success).Count;
        public int BotsFailed => BotResults.FindAll(r => r.IsComplete && !r.Success).Count;

        public TestRun(string routePath, HarnessSettings harness)
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            RoutePath = routePath;
            Harness = harness;
            StartTime = DateTime.UtcNow;
            Status = TestRunStatus.Pending;
        }

        public BotTestResult AddBot(string botName, string characterName, string characterClass)
        {
            var result = new BotTestResult(botName, characterName, characterClass);
            BotResults.Add(result);
            return result;
        }

        public void Complete(bool success, string errorMessage = null)
        {
            EndTime = DateTime.UtcNow;
            Status = success ? TestRunStatus.Completed : TestRunStatus.Failed;
            ErrorMessage = errorMessage;
        }

        public void Timeout()
        {
            EndTime = DateTime.UtcNow;
            Status = TestRunStatus.TimedOut;
            ErrorMessage = $"Test run timed out after {Harness?.TestTimeoutSeconds ?? 600} seconds";
        }

        public void Cancel()
        {
            EndTime = DateTime.UtcNow;
            Status = TestRunStatus.Cancelled;
            ErrorMessage = "Test run was cancelled";
        }
    }

    /// <summary>
    /// Test results for a single bot within a test run
    /// </summary>
    internal class BotTestResult
    {
        public string BotName { get; }
        public string CharacterName { get; }
        public string CharacterClass { get; }
        public bool Success { get; set; }
        public bool IsComplete { get; set; }
        public List<TaskTestResult> TaskResults { get; } = new List<TaskTestResult>();
        public List<string> Logs { get; } = new List<string>();
        public string ErrorMessage { get; set; }
        public DateTime StartTime { get; }
        public DateTime? EndTime { get; set; }

        public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;
        public int TasksCompleted => TaskResults.FindAll(t => t.Result == TaskResult.Success).Count;
        public int TasksFailed => TaskResults.FindAll(t => t.Result == TaskResult.Failed).Count;
        public int TasksSkipped => TaskResults.FindAll(t => t.Result == TaskResult.Skipped).Count;
        public int TotalTasks => TaskResults.Count;

        public BotTestResult(string botName, string characterName, string characterClass)
        {
            BotName = botName;
            CharacterName = characterName;
            CharacterClass = characterClass;
            StartTime = DateTime.UtcNow;
        }

        public void AddTaskResult(string taskName, TaskResult result, TimeSpan duration, string errorMessage = null)
        {
            TaskResults.Add(new TaskTestResult(taskName, result, duration, errorMessage));
        }

        public void AddLog(string message)
        {
            Logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] {message}");
        }

        public void Complete(bool success, string errorMessage = null)
        {
            EndTime = DateTime.UtcNow;
            IsComplete = true;
            Success = success;
            ErrorMessage = errorMessage;
        }
    }

    /// <summary>
    /// Result for a single task within a bot's test
    /// </summary>
    internal class TaskTestResult
    {
        public string TaskName { get; }
        public TaskResult Result { get; }
        public TimeSpan Duration { get; }
        public string ErrorMessage { get; }

        public TaskTestResult(string taskName, TaskResult result, TimeSpan duration, string errorMessage = null)
        {
            TaskName = taskName;
            Result = result;
            Duration = duration;
            ErrorMessage = errorMessage;
        }
    }
}
