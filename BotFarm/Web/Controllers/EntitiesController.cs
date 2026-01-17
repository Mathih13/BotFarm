using System;
using BotFarm.Web.Models;
using BotFarm.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace BotFarm.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EntitiesController : ControllerBase
    {
        private readonly WorldDatabaseService worldDb;

        public EntitiesController(WorldDatabaseService worldDb)
        {
            this.worldDb = worldDb;
        }

        /// <summary>
        /// POST /api/entities/lookup - Look up entity names by their IDs
        /// </summary>
        [HttpPost("lookup")]
        public ActionResult<EntityLookupResponse> LookupEntities([FromBody] EntityLookupRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { error = "Request body is required" });
            }

            if (!worldDb.IsConnected)
            {
                return StatusCode(503, new { error = "World database is not connected" });
            }

            var response = new EntityLookupResponse();

            if (request.NpcEntries != null && request.NpcEntries.Length > 0)
            {
                response.Npcs = worldDb.GetNPCNames(request.NpcEntries);
            }

            if (request.QuestIds != null && request.QuestIds.Length > 0)
            {
                response.Quests = worldDb.GetQuestNames(request.QuestIds);
            }

            if (request.ItemEntries != null && request.ItemEntries.Length > 0)
            {
                response.Items = worldDb.GetItemNames(request.ItemEntries);
            }

            if (request.ObjectEntries != null && request.ObjectEntries.Length > 0)
            {
                response.Objects = worldDb.GetGameObjectNames(request.ObjectEntries);
            }

            return response;
        }

        /// <summary>
        /// GET /api/entities/status - Check if world database is connected
        /// </summary>
        [HttpGet("status")]
        public ActionResult GetStatus()
        {
            return Ok(new { connected = worldDb.IsConnected });
        }

        /// <summary>
        /// GET /api/entities/search - Search entities by name
        /// </summary>
        [HttpGet("search")]
        public ActionResult<EntitySearchResponse> SearchEntities(
            [FromQuery] string type,
            [FromQuery] string query,
            [FromQuery] int limit = 20)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return BadRequest(new { error = "Type parameter is required (npc, quest, item, object)" });
            }

            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            {
                return BadRequest(new { error = "Query must be at least 2 characters" });
            }

            if (!worldDb.IsConnected)
            {
                return StatusCode(503, new { error = "World database is not connected" });
            }

            // Clamp limit between 1 and 50
            limit = Math.Max(1, Math.Min(50, limit));

            var response = new EntitySearchResponse();

            switch (type.ToLowerInvariant())
            {
                case "npc":
                    response.Results = worldDb.SearchNPCs(query, limit);
                    break;
                case "quest":
                    response.Results = worldDb.SearchQuests(query, limit);
                    break;
                case "item":
                    response.Results = worldDb.SearchItems(query, limit);
                    break;
                case "object":
                    response.Results = worldDb.SearchGameObjects(query, limit);
                    break;
                default:
                    return BadRequest(new { error = $"Invalid type '{type}'. Valid types: npc, quest, item, object" });
            }

            return response;
        }
    }
}
