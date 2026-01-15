using Client;
using Client.AI.Tasks;
using Client.UI;
using Client.World.Definitions;

namespace BotFarm.AI.Tasks
{
    /// <summary>
    /// Assertion task that verifies the player has at least a minimum count of an item.
    /// Fails immediately if the player doesn't have enough of the item.
    /// </summary>
    public class AssertHasItemTask : BaseTask
    {
        private readonly uint itemEntry;
        private readonly int minCount;
        private readonly string message;

        public override string Name => $"AssertHasItem({itemEntry}, >={minCount})";

        /// <summary>
        /// Create an assertion that checks if the player has at least minCount of an item.
        /// </summary>
        /// <param name="itemEntry">The item entry ID to check for</param>
        /// <param name="minCount">The minimum count required (default 1)</param>
        /// <param name="message">Optional message to display on failure</param>
        public AssertHasItemTask(uint itemEntry, int minCount = 1, string message = null)
        {
            this.itemEntry = itemEntry;
            this.minCount = minCount > 0 ? minCount : 1;
            this.message = message;
        }

        public override bool Start(AutomatedGame game)
        {
            if (!base.Start(game))
                return false;

            if (itemEntry == 0)
            {
                game.Log("AssertHasItem: Item entry cannot be 0", LogLevel.Error);
                return false;
            }

            return true;
        }

        protected override TaskResult UpdateTask(AutomatedGame game)
        {
            if (game.Player == null)
            {
                game.Log("AssertHasItem: FAIL - Player is null", LogLevel.Error);
                return TaskResult.Failed;
            }

            int currentCount = GetItemCount(game, itemEntry);

            if (currentCount >= minCount)
            {
                game.Log($"AssertHasItem: PASS - Has {currentCount} of item {itemEntry} (need {minCount})", LogLevel.Info);
                return TaskResult.Success;
            }
            else
            {
                string failMessage = string.IsNullOrEmpty(message)
                    ? $"AssertHasItem: FAIL - Has {currentCount} of item {itemEntry} (need {minCount})"
                    : $"AssertHasItem: FAIL - {message} (Has {currentCount}, need {minCount})";
                game.Log(failMessage, LogLevel.Error);
                return TaskResult.Failed;
            }
        }

        /// <summary>
        /// Count total quantity of a specific item entry in player's inventory (backpack)
        /// </summary>
        private int GetItemCount(AutomatedGame game, uint itemEntry)
        {
            int totalCount = 0;
            int packSlotBase = (int)PlayerField.PLAYER_FIELD_PACK_SLOT_1;

            // Scan backpack (16 slots)
            for (int slot = 0; slot < 16; slot++)
            {
                // Read GUID from two consecutive fields (64-bit GUID)
                uint guidLow = game.Player[packSlotBase + slot * 2];
                uint guidHigh = game.Player[packSlotBase + slot * 2 + 1];
                ulong itemGuid = ((ulong)guidHigh << 32) | guidLow;

                if (itemGuid == 0)
                    continue;

                // Find the item object to get its entry and stack count
                if (game.Objects.TryGetValue(itemGuid, out var itemObject))
                {
                    if (itemObject.Entry == itemEntry)
                    {
                        // Get stack count (defaults to 1 if not set)
                        uint stackCount = itemObject[(int)ItemField.ITEM_FIELD_STACK_COUNT];
                        totalCount += (int)(stackCount > 0 ? stackCount : 1);
                    }
                }
            }

            return totalCount;
        }
    }
}
