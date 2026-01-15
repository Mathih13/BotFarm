using Client;
using Client.AI.Tasks;
using Client.UI;
using Client.World.Definitions;
using Client.World.Entities;
using Client.World.Items;
using System;
using System.Collections.Generic;

namespace BotFarm.AI.Tasks
{
    /// <summary>
    /// Task that sells all non-consumable items from bags to a vendor.
    /// Prerequisites: Must be within interaction range of the vendor NPC.
    /// This task should be used after TalkToNPCTask to open the vendor window.
    /// </summary>
    public class SellItemsTask : BaseTask
    {
        private readonly uint npcEntry;

        private enum TaskState
        {
            Scanning,           // Scanning bag items
            QueryingItem,       // Querying item template from server
            WaitingForQuery,    // Waiting for query response
            Selling,            // Deciding whether to sell
            WaitingForSell,     // Brief delay after selling
            Complete
        }

        // State machine
        private TaskState state = TaskState.Scanning;
        private DateTime stateStartTime;

        // Bag items to process
        private struct BagItem
        {
            public byte Slot;
            public ulong Guid;
            public uint Entry;
            public uint StackCount;
        }
        private List<BagItem> bagItems = new List<BagItem>();
        private int currentItemIndex = 0;

        // Current item being processed
        private uint currentEntry;
        private ulong currentItemGuid;
        private uint currentStackCount;

        // Vendor info
        private WorldObject vendorNpc;
        private ulong vendorGuid;

        // Timeouts
        private const double QueryTimeoutSeconds = 5.0;
        private const double SellDelaySeconds = 0.2;

        // Results tracking
        private int itemsSold = 0;
        private int itemsKept = 0;

        public override string Name => $"SellItems({itemsSold} sold, {itemsKept} kept)";

        /// <summary>
        /// Create a sell items task
        /// </summary>
        /// <param name="npcEntry">NPC entry ID of the vendor. If 0, must find vendor another way.</param>
        public SellItemsTask(uint npcEntry = 0)
        {
            this.npcEntry = npcEntry;
            SetDelayPadding(RandomDelay(0.3f, 0.6f), RandomDelay(0.3f, 0.6f));
        }

        public override bool Start(AutomatedGame game)
        {
            if (!base.Start(game))
                return false;

            // Find the vendor NPC
            if (npcEntry != 0)
            {
                vendorNpc = MoveToNPCTask.FindNPCByEntry(game, npcEntry);
                if (vendorNpc == null)
                {
                    game.Log($"SellItemsTask: Vendor NPC with entry {npcEntry} not found", LogLevel.Warning);
                    return false;
                }
                vendorGuid = vendorNpc.GUID;
            }
            else
            {
                game.Log("SellItemsTask: No vendor NPC entry specified", LogLevel.Warning);
                return false;
            }

            state = TaskState.Scanning;
            stateStartTime = DateTime.Now;
            bagItems.Clear();
            currentItemIndex = 0;
            itemsSold = 0;
            itemsKept = 0;

            return true;
        }

        protected override TaskResult UpdateTask(AutomatedGame game)
        {
            if (!game.Player.IsAlive)
            {
                game.Log("SellItemsTask: Player is dead", LogLevel.Warning);
                return TaskResult.Failed;
            }

            switch (state)
            {
                case TaskState.Scanning:
                    return DoScanning(game);

                case TaskState.QueryingItem:
                    return DoQueryingItem(game);

                case TaskState.WaitingForQuery:
                    return DoWaitingForQuery(game);

                case TaskState.Selling:
                    return DoSelling(game);

                case TaskState.WaitingForSell:
                    return DoWaitingForSell(game);

                case TaskState.Complete:
                    return TaskResult.Success;

                default:
                    return TaskResult.Failed;
            }
        }

        private TaskResult DoScanning(AutomatedGame game)
        {
            bagItems.Clear();

            // Scan backpack slots (16 slots)
            int packSlotBase = (int)PlayerField.PLAYER_FIELD_PACK_SLOT_1;

            for (int slot = 0; slot < 16; slot++)
            {
                // Read GUID from two consecutive fields (64-bit GUID)
                uint guidLow = game.Player[packSlotBase + slot * 2];
                uint guidHigh = game.Player[packSlotBase + slot * 2 + 1];
                ulong itemGuid = ((ulong)guidHigh << 32) | guidLow;

                if (itemGuid == 0)
                    continue;

                // Find item object to get entry and stack count
                if (game.Objects.TryGetValue(itemGuid, out var itemObject))
                {
                    uint entry = itemObject.Entry;
                    if (entry > 0)
                    {
                        // Read stack count from item fields (default to 1 if not set)
                        uint stackCount = itemObject[(int)ItemField.ITEM_FIELD_STACK_COUNT];
                        if (stackCount == 0) stackCount = 1;

                        bagItems.Add(new BagItem { Slot = (byte)slot, Guid = itemGuid, Entry = entry, StackCount = stackCount });
                    }
                }
            }

            if (bagItems.Count == 0)
            {
                game.Log("SellItemsTask: No items in bags", LogLevel.Debug);
                state = TaskState.Complete;
                return TaskResult.Running;
            }

            game.Log($"SellItemsTask: Found {bagItems.Count} items to evaluate", LogLevel.Debug);
            currentItemIndex = 0;
            state = TaskState.QueryingItem;
            stateStartTime = DateTime.Now;
            return TaskResult.Running;
        }

        private TaskResult DoQueryingItem(AutomatedGame game)
        {
            if (currentItemIndex >= bagItems.Count)
            {
                game.Log($"SellItemsTask: Finished. Sold {itemsSold} items, kept {itemsKept} consumables", LogLevel.Info);
                state = TaskState.Complete;
                return TaskResult.Running;
            }

            var bagItem = bagItems[currentItemIndex];
            currentEntry = bagItem.Entry;
            currentItemGuid = bagItem.Guid;
            currentStackCount = bagItem.StackCount;

            // Check if already cached
            var template = ItemCache.Get(currentEntry);
            if (template != null)
            {
                state = TaskState.Selling;
                stateStartTime = DateTime.Now;
                return TaskResult.Running;
            }

            // Query from server
            game.QueryItem(currentEntry);
            state = TaskState.WaitingForQuery;
            stateStartTime = DateTime.Now;
            return TaskResult.Running;
        }

        private TaskResult DoWaitingForQuery(AutomatedGame game)
        {
            var template = ItemCache.Get(currentEntry);
            if (template != null)
            {
                state = TaskState.Selling;
                stateStartTime = DateTime.Now;
                return TaskResult.Running;
            }

            // Check timeout
            if ((DateTime.Now - stateStartTime).TotalSeconds > QueryTimeoutSeconds)
            {
                game.Log($"SellItemsTask: Timeout waiting for item {currentEntry} query, skipping", LogLevel.Warning);
                MoveToNextItem();
                return TaskResult.Running;
            }

            return TaskResult.Running;
        }

        private TaskResult DoSelling(AutomatedGame game)
        {
            var template = ItemCache.Get(currentEntry);
            if (template == null)
            {
                // Unknown item, skip it
                MoveToNextItem();
                return TaskResult.Running;
            }

            // Keep consumables, sell everything else
            if (template.ItemClass == ItemClass.Consumable)
            {
                game.Log($"SellItemsTask: Keeping consumable: {template.Name}", LogLevel.Debug);
                itemsKept++;
                MoveToNextItem();
                return TaskResult.Running;
            }

            // Sell this item (pass full stack count)
            game.Log($"SellItemsTask: Selling {template.Name} x{currentStackCount} (Entry: {template.Entry})", LogLevel.Info);
            game.SellItem(vendorGuid, currentItemGuid, currentStackCount);
            itemsSold++;

            state = TaskState.WaitingForSell;
            stateStartTime = DateTime.Now;
            return TaskResult.Running;
        }

        private TaskResult DoWaitingForSell(AutomatedGame game)
        {
            // Brief delay to let server process
            if ((DateTime.Now - stateStartTime).TotalSeconds >= SellDelaySeconds)
            {
                MoveToNextItem();
            }
            return TaskResult.Running;
        }

        private void MoveToNextItem()
        {
            currentItemIndex++;
            state = TaskState.QueryingItem;
            stateStartTime = DateTime.Now;
        }

        public override void Cleanup(AutomatedGame game)
        {
            bagItems.Clear();
            vendorNpc = null;
        }
    }
}
