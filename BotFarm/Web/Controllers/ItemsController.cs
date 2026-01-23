using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BotFarm.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace BotFarm.Web.Controllers
{
    [ApiController]
    [Route("api/items")]
    public class ItemsController : ControllerBase
    {
        private readonly ItemIconService iconService;

        public ItemsController(ItemIconService iconService)
        {
            this.iconService = iconService;
        }

        /// <summary>
        /// GET /api/items/icons?entries=2488&entries=2089
        /// Returns a map of item entry IDs to icon CDN URLs
        /// </summary>
        [HttpGet("icons")]
        public async Task<ActionResult<Dictionary<uint, string>>> GetIcons([FromQuery] uint[] entries)
        {
            if (entries == null || entries.Length == 0)
            {
                return Ok(new Dictionary<uint, string>());
            }

            // Limit to prevent abuse
            if (entries.Length > 100)
            {
                return BadRequest(new { error = "Maximum 100 items per request" });
            }

            var iconNames = await iconService.GetIconNames(entries);

            // Convert icon names to full URLs
            var result = iconNames.ToDictionary(
                kvp => kvp.Key,
                kvp => iconService.GetIconUrl(kvp.Value, "medium")
            );

            return Ok(result);
        }

        /// <summary>
        /// GET /api/items/{entry}/icon
        /// Returns the icon URL for a single item
        /// </summary>
        [HttpGet("{entry}/icon")]
        public async Task<ActionResult<ItemIconResponse>> GetIcon(uint entry)
        {
            var iconName = await iconService.GetIconName(entry);

            if (string.IsNullOrEmpty(iconName))
            {
                return Ok(new ItemIconResponse { Entry = entry, IconUrl = null });
            }

            return Ok(new ItemIconResponse
            {
                Entry = entry,
                IconUrl = iconService.GetIconUrl(iconName, "medium")
            });
        }
    }

    public class ItemIconResponse
    {
        public uint Entry { get; set; }
        public string IconUrl { get; set; }
    }
}
