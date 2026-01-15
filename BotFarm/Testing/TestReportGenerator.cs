using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Client.AI.Tasks;

namespace BotFarm.Testing
{
    /// <summary>
    /// Generates test reports in various formats
    /// </summary>
    internal static class TestReportGenerator
    {
        /// <summary>
        /// Generate a console-friendly report
        /// </summary>
        public static string GenerateConsoleReport(TestRun run)
        {
            var sb = new StringBuilder();
            var separator = new string('=', 65);

            sb.AppendLine();
            sb.AppendLine(separator);
            sb.AppendLine($"TEST RUN: {run.RouteName ?? run.RoutePath}");
            sb.AppendLine(separator);
            sb.AppendLine($"Run ID:   {run.Id}");
            sb.AppendLine($"Status:   {run.Status}");
            sb.AppendLine($"Duration: {FormatDuration(run.Duration)}");
            sb.AppendLine($"Bots:     {run.BotsPassed}/{run.BotResults.Count} passed");

            if (!string.IsNullOrEmpty(run.ErrorMessage))
            {
                sb.AppendLine($"Error:    {run.ErrorMessage}");
            }

            sb.AppendLine();
            sb.AppendLine("BOT RESULTS:");
            sb.AppendLine("+-------------------+----------+--------+-----------+----------+");
            sb.AppendLine("| Bot               | Class    | Tasks  | Duration  | Status   |");
            sb.AppendLine("+-------------------+----------+--------+-----------+----------+");

            foreach (var bot in run.BotResults)
            {
                string status = bot.Success ? "PASS" : (bot.IsComplete ? "FAIL" : "RUNNING");
                string statusSymbol = bot.Success ? "[OK]" : (bot.IsComplete ? "[X]" : "[..]");
                string taskProgress = $"{bot.TasksCompleted}/{bot.TotalTasks}";

                sb.AppendLine($"| {Truncate(bot.BotName, 17),-17} | {Truncate(bot.CharacterClass, 8),-8} | {taskProgress,-6} | {FormatDuration(bot.Duration),-9} | {statusSymbol} {status,-4} |");
            }

            sb.AppendLine("+-------------------+----------+--------+-----------+----------+");
            sb.AppendLine();

            int passed = run.BotsPassed;
            int failed = run.BotsFailed;
            int incomplete = run.BotResults.Count - run.BotsCompleted;

            sb.AppendLine($"SUMMARY: {passed} passed, {failed} failed, {incomplete} incomplete");
            sb.AppendLine(separator);
            sb.AppendLine();

            return sb.ToString();
        }

        /// <summary>
        /// Generate a JSON report
        /// </summary>
        public static string GenerateJsonReport(TestRun run)
        {
            var report = new TestRunReport
            {
                Id = run.Id,
                RoutePath = run.RoutePath,
                RouteName = run.RouteName,
                Status = run.Status.ToString(),
                StartTime = run.StartTime,
                EndTime = run.EndTime,
                DurationSeconds = run.Duration.TotalSeconds,
                BotCount = run.BotResults.Count,
                BotsPassed = run.BotsPassed,
                BotsFailed = run.BotsFailed,
                ErrorMessage = run.ErrorMessage,
                Bots = new BotReport[run.BotResults.Count]
            };

            for (int i = 0; i < run.BotResults.Count; i++)
            {
                var bot = run.BotResults[i];
                report.Bots[i] = new BotReport
                {
                    BotName = bot.BotName,
                    CharacterName = bot.CharacterName,
                    CharacterClass = bot.CharacterClass,
                    Success = bot.Success,
                    IsComplete = bot.IsComplete,
                    TasksCompleted = bot.TasksCompleted,
                    TasksFailed = bot.TasksFailed,
                    TasksSkipped = bot.TasksSkipped,
                    TotalTasks = bot.TotalTasks,
                    DurationSeconds = bot.Duration.TotalSeconds,
                    ErrorMessage = bot.ErrorMessage,
                    Tasks = new TaskReport[bot.TaskResults.Count]
                };

                for (int j = 0; j < bot.TaskResults.Count; j++)
                {
                    var task = bot.TaskResults[j];
                    report.Bots[i].Tasks[j] = new TaskReport
                    {
                        TaskName = task.TaskName,
                        Result = task.Result.ToString(),
                        DurationSeconds = task.Duration.TotalSeconds,
                        ErrorMessage = task.ErrorMessage
                    };
                }
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            return JsonSerializer.Serialize(report, options);
        }

        /// <summary>
        /// Save report to file
        /// </summary>
        public static void SaveReport(TestRun run, string outputPath, bool json = false)
        {
            string content = json ? GenerateJsonReport(run) : GenerateConsoleReport(run);
            File.WriteAllText(outputPath, content);
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m";
            if (duration.TotalMinutes >= 1)
                return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
            return $"{duration.Seconds}.{duration.Milliseconds / 100}s";
        }

        private static string Truncate(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Length <= maxLength ? str : str.Substring(0, maxLength - 2) + "..";
        }

        // JSON report models
        private class TestRunReport
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("routePath")]
            public string RoutePath { get; set; }

            [JsonPropertyName("routeName")]
            public string RouteName { get; set; }

            [JsonPropertyName("status")]
            public string Status { get; set; }

            [JsonPropertyName("startTime")]
            public DateTime StartTime { get; set; }

            [JsonPropertyName("endTime")]
            public DateTime? EndTime { get; set; }

            [JsonPropertyName("durationSeconds")]
            public double DurationSeconds { get; set; }

            [JsonPropertyName("botCount")]
            public int BotCount { get; set; }

            [JsonPropertyName("botsPassed")]
            public int BotsPassed { get; set; }

            [JsonPropertyName("botsFailed")]
            public int BotsFailed { get; set; }

            [JsonPropertyName("errorMessage")]
            public string ErrorMessage { get; set; }

            [JsonPropertyName("bots")]
            public BotReport[] Bots { get; set; }
        }

        private class BotReport
        {
            [JsonPropertyName("botName")]
            public string BotName { get; set; }

            [JsonPropertyName("characterName")]
            public string CharacterName { get; set; }

            [JsonPropertyName("characterClass")]
            public string CharacterClass { get; set; }

            [JsonPropertyName("success")]
            public bool Success { get; set; }

            [JsonPropertyName("isComplete")]
            public bool IsComplete { get; set; }

            [JsonPropertyName("tasksCompleted")]
            public int TasksCompleted { get; set; }

            [JsonPropertyName("tasksFailed")]
            public int TasksFailed { get; set; }

            [JsonPropertyName("tasksSkipped")]
            public int TasksSkipped { get; set; }

            [JsonPropertyName("totalTasks")]
            public int TotalTasks { get; set; }

            [JsonPropertyName("durationSeconds")]
            public double DurationSeconds { get; set; }

            [JsonPropertyName("errorMessage")]
            public string ErrorMessage { get; set; }

            [JsonPropertyName("tasks")]
            public TaskReport[] Tasks { get; set; }
        }

        private class TaskReport
        {
            [JsonPropertyName("taskName")]
            public string TaskName { get; set; }

            [JsonPropertyName("result")]
            public string Result { get; set; }

            [JsonPropertyName("durationSeconds")]
            public double DurationSeconds { get; set; }

            [JsonPropertyName("errorMessage")]
            public string ErrorMessage { get; set; }
        }
    }
}
