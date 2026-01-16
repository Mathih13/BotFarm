using System;
using System.Collections.Generic;
using System.Linq;
using BotFarm.Testing;
using Client.AI.Tasks;

namespace BotFarm.Web.Models
{
    // ============ Request Models ============

    public class StartTestRequest
    {
        public string RoutePath { get; set; }
    }

    public class StartSuiteRequest
    {
        public string SuitePath { get; set; }
        public bool Parallel { get; set; }
    }

    // ============ Response Models ============

    public class ApiStatusResponse
    {
        public bool Online { get; set; }
        public int ActiveBots { get; set; }
        public int ActiveTestRuns { get; set; }
        public int ActiveSuiteRuns { get; set; }
        public int CompletedTestRuns { get; set; }
        public int CompletedSuiteRuns { get; set; }
        public DateTime ServerTime { get; set; }
    }

    public class ApiTestRun
    {
        public string Id { get; set; }
        public string RoutePath { get; set; }
        public string RouteName { get; set; }
        public string Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double DurationSeconds { get; set; }
        public string ErrorMessage { get; set; }

        // Harness info
        public int BotCount { get; set; }
        public int Level { get; set; }
        public List<string> Classes { get; set; }

        // Results
        public int BotsCompleted { get; set; }
        public int BotsPassed { get; set; }
        public int BotsFailed { get; set; }
        public List<ApiBotResult> BotResults { get; set; }

        public static ApiTestRun FromTestRun(TestRun run, bool includeDetails = true)
        {
            var dto = new ApiTestRun
            {
                Id = run.Id,
                RoutePath = run.RoutePath,
                RouteName = run.RouteName,
                Status = run.Status.ToString(),
                StartTime = run.StartTime,
                EndTime = run.EndTime,
                DurationSeconds = run.Duration.TotalSeconds,
                ErrorMessage = run.ErrorMessage,
                BotCount = run.Harness?.BotCount ?? 0,
                Level = run.Harness?.Level ?? 1,
                Classes = run.Harness?.Classes ?? new List<string>(),
                BotsCompleted = run.BotsCompleted,
                BotsPassed = run.BotsPassed,
                BotsFailed = run.BotsFailed,
            };

            if (includeDetails)
            {
                dto.BotResults = run.BotResults.Select(ApiBotResult.FromBotTestResult).ToList();
            }

            return dto;
        }
    }

    public class ApiBotResult
    {
        public string BotName { get; set; }
        public string CharacterName { get; set; }
        public string CharacterClass { get; set; }
        public bool Success { get; set; }
        public bool IsComplete { get; set; }
        public DateTime StartTime { get; set; }
        public double DurationSeconds { get; set; }
        public string ErrorMessage { get; set; }

        // Task summary
        public int TasksCompleted { get; set; }
        public int TasksFailed { get; set; }
        public int TasksSkipped { get; set; }
        public int TotalTasks { get; set; }

        // Detailed results
        public List<ApiTaskResult> TaskResults { get; set; }
        public List<string> Logs { get; set; }

        public static ApiBotResult FromBotTestResult(BotTestResult result)
        {
            return new ApiBotResult
            {
                BotName = result.BotName,
                CharacterName = result.CharacterName,
                CharacterClass = result.CharacterClass,
                Success = result.Success,
                IsComplete = result.IsComplete,
                StartTime = result.StartTime,
                DurationSeconds = result.Duration.TotalSeconds,
                ErrorMessage = result.ErrorMessage,
                TasksCompleted = result.TasksCompleted,
                TasksFailed = result.TasksFailed,
                TasksSkipped = result.TasksSkipped,
                TotalTasks = result.TotalTasks,
                TaskResults = result.TaskResults.Select(ApiTaskResult.FromTaskTestResult).ToList(),
                Logs = result.Logs.ToList()
            };
        }
    }

    public class ApiTaskResult
    {
        public string TaskName { get; set; }
        public string Result { get; set; }
        public double DurationSeconds { get; set; }
        public string ErrorMessage { get; set; }

        public static ApiTaskResult FromTaskTestResult(TaskTestResult result)
        {
            return new ApiTaskResult
            {
                TaskName = result.TaskName,
                Result = result.Result.ToString(),
                DurationSeconds = result.Duration.TotalSeconds,
                ErrorMessage = result.ErrorMessage
            };
        }
    }

    public class ApiTestSuiteRun
    {
        public string Id { get; set; }
        public string SuiteName { get; set; }
        public string SuitePath { get; set; }
        public bool ParallelMode { get; set; }
        public string Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double DurationSeconds { get; set; }
        public string ErrorMessage { get; set; }

        // Metrics
        public int TotalTests { get; set; }
        public int TestsCompleted { get; set; }
        public int TestsPassed { get; set; }
        public int TestsFailed { get; set; }
        public int TestsSkipped { get; set; }

        // Individual test runs
        public List<ApiTestRun> TestRuns { get; set; }

        public static ApiTestSuiteRun FromTestSuiteRun(TestSuiteRun run, bool includeDetails = true)
        {
            var dto = new ApiTestSuiteRun
            {
                Id = run.Id,
                SuiteName = run.SuiteName,
                SuitePath = run.SuitePath,
                ParallelMode = run.ParallelMode,
                Status = run.Status.ToString(),
                StartTime = run.StartTime,
                EndTime = run.EndTime,
                DurationSeconds = run.Duration.TotalSeconds,
                ErrorMessage = run.ErrorMessage,
                TotalTests = run.TotalTests,
                TestsCompleted = run.TestsCompleted,
                TestsPassed = run.TestsPassed,
                TestsFailed = run.TestsFailed,
                TestsSkipped = run.TestsSkipped
            };

            if (includeDetails)
            {
                dto.TestRuns = run.TestRuns.Select(t => ApiTestRun.FromTestRun(t, includeDetails: false)).ToList();
            }

            return dto;
        }
    }

    public class ApiRouteInfo
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public string Directory { get; set; }
        public bool HasHarness { get; set; }
        public int? BotCount { get; set; }
        public int? Level { get; set; }
        public int? TimeoutSeconds { get; set; }
    }

    public class ApiRouteDetail
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool Loop { get; set; }
        public ApiHarnessSettings Harness { get; set; }
        public List<ApiTaskInfo> Tasks { get; set; }
        public string RawJson { get; set; }
    }

    public class ApiHarnessSettings
    {
        public int BotCount { get; set; }
        public string AccountPrefix { get; set; }
        public List<string> Classes { get; set; }
        public string Race { get; set; }
        public int Level { get; set; }
        public int SetupTimeoutSeconds { get; set; }
        public int TestTimeoutSeconds { get; set; }
        public ApiStartPosition StartPosition { get; set; }
    }

    public class ApiStartPosition
    {
        public uint MapId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }

    public class ApiTaskInfo
    {
        public string Type { get; set; }
        public object Parameters { get; set; }
    }

    public class ApiSuiteInfo
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public int TestCount { get; set; }
    }
}
