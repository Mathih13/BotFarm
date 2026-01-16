using System;
using System.Linq;
using BotFarm.Web.Models;
using BotFarm.Web.Services;
using Client.UI;
using Microsoft.AspNetCore.Mvc;

namespace BotFarm.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LogsController : ControllerBase
    {
        private readonly LogBuffer logBuffer;

        public LogsController(LogBuffer logBuffer)
        {
            this.logBuffer = logBuffer;
        }

        /// <summary>
        /// GET /api/logs - Get recent log entries
        /// </summary>
        /// <param name="count">Number of entries to return (default 100, max 1000)</param>
        /// <param name="filter">Optional filter to match against message content (e.g., bot username)</param>
        /// <param name="since">Optional UTC timestamp to get entries after</param>
        /// <param name="minLevel">Optional minimum log level (Debug, Detail, Warning, Info, Error)</param>
        [HttpGet]
        public ActionResult<ApiLogsResponse> GetLogs(
            [FromQuery] int count = 100,
            [FromQuery] string filter = null,
            [FromQuery] DateTime? since = null,
            [FromQuery] string minLevel = null)
        {
            // Clamp count to reasonable limits
            count = Math.Clamp(count, 1, 1000);

            // Parse minLevel if provided
            LogLevel? parsedMinLevel = null;
            if (!string.IsNullOrEmpty(minLevel) && Enum.TryParse<LogLevel>(minLevel, ignoreCase: true, out var level))
            {
                parsedMinLevel = level;
            }

            var entries = since.HasValue
                ? logBuffer.GetSince(since.Value, filter, parsedMinLevel)
                : logBuffer.GetRecent(count, filter, parsedMinLevel);

            // If using GetSince, take the last 'count' entries
            if (since.HasValue && entries.Count > count)
            {
                entries = entries.Skip(entries.Count - count).ToList();
            }

            var response = new ApiLogsResponse
            {
                Logs = entries.Select(e => new ApiLogEntry
                {
                    Timestamp = e.Timestamp,
                    Message = e.Message,
                    Level = e.Level.ToString()
                }).ToList(),
                TotalCount = logBuffer.TotalCount,
                FilteredCount = logBuffer.GetFilteredCount(filter, parsedMinLevel)
            };

            return response;
        }
    }
}
