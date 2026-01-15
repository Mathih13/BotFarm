using Client;
using Client.AI.Tasks;
using Client.UI;
using Client.World.Definitions;
using Client.World.Items;
using System;
using System.Collections.Generic;

namespace BotFarm.AI.Tasks
{
    /// <summary>
    /// Task that scans inventory and equips items that are better than currently equipped items.
    /// Handles armor and weapons only (skips rings, trinkets, neck, back).
    /// </summary>
    public class EquipItemTask : BaseTask
    {
        // Equipment slots we handle (equipment slot ID -> PlayerField offset for VISIBLE_ITEM)
        private static readonly Dictionary<int, int> HandledEquipmentSlots = new Dictionary<int, int>
        {
            // Armor slots
            [0] = 0,   // Head -> PLAYER_VISIBLE_ITEM_1
            [2] = 2,   // Shoulder -> PLAYER_VISIBLE_ITEM_3
            [4] = 4,   // Chest -> PLAYER_VISIBLE_ITEM_5
            [5] = 5,   // Waist -> PLAYER_VISIBLE_ITEM_6
            [6] = 6,   // Legs -> PLAYER_VISIBLE_ITEM_7
            [7] = 7,   // Feet -> PLAYER_VISIBLE_ITEM_8
            [8] = 8,   // Wrist -> PLAYER_VISIBLE_ITEM_9
            [9] = 9,   // Hands -> PLAYER_VISIBLE_ITEM_10
            // Weapon slots
            [15] = 15, // MainHand -> PLAYER_VISIBLE_ITEM_16
            [16] = 16, // OffHand -> PLAYER_VISIBLE_ITEM_17
            [17] = 17  // Ranged -> PLAYER_VISIBLE_ITEM_18
        };

        private enum TaskState
        {
            Scanning,           // Scanning bag items
            QueryingBagItem,    // Querying a bag item from server
            WaitingForBagQuery, // Waiting for bag item query response
            QueryingEquippedItem,    // Querying an equipped item
            WaitingForEquippedQuery, // Waiting for equipped item query response
            Comparing,          // Comparing bag item vs equipped
            Equipping,          // Sending equip command
            WaitingForEquip,    // Waiting for equip to complete
            Complete
        }

        // State machine state
        private TaskState state = TaskState.Scanning;
        private DateTime stateStartTime;

        // Bag items to process
        private struct BagItem
        {
            public byte Slot;
            public uint Entry;
        }
        private List<BagItem> bagItems = new List<BagItem>();
        private int currentBagItemIndex = 0;

        // Current item being processed
        private uint currentBagItemEntry;
        private byte currentBagSlot;
        private int currentTargetEquipSlot;
        private uint currentEquippedEntry;

        // Timeouts
        private const double QueryTimeoutSeconds = 5.0;
        private const double EquipDelaySeconds = 0.5;

        // Results tracking
        private int itemsEquipped = 0;
        private int itemsSkipped = 0;

        public override string Name => $"EquipItems({itemsEquipped} equipped)";

        public EquipItemTask()
        {
            SetDelayPadding(RandomDelay(0.2f, 0.4f), RandomDelay(0.3f, 0.5f));
        }

        public override bool Start(AutomatedGame game)
        {
            if (!base.Start(game))
                return false;

            state = TaskState.Scanning;
            stateStartTime = DateTime.Now;
            bagItems.Clear();
            currentBagItemIndex = 0;
            itemsEquipped = 0;
            itemsSkipped = 0;

            return true;
        }

        protected override TaskResult UpdateTask(AutomatedGame game)
        {
            if (!game.Player.IsAlive)
            {
                game.Log("EquipItemTask: Player is dead", LogLevel.Warning);
                return TaskResult.Failed;
            }

            switch (state)
            {
                case TaskState.Scanning:
                    return DoScanning(game);

                case TaskState.QueryingBagItem:
                    return DoQueryingBagItem(game);

                case TaskState.WaitingForBagQuery:
                    return DoWaitingForBagQuery(game);

                case TaskState.QueryingEquippedItem:
                    return DoQueryingEquippedItem(game);

                case TaskState.WaitingForEquippedQuery:
                    return DoWaitingForEquippedQuery(game);

                case TaskState.Comparing:
                    return DoComparing(game);

                case TaskState.Equipping:
                    return DoEquipping(game);

                case TaskState.WaitingForEquip:
                    return DoWaitingForEquip(game);

                case TaskState.Complete:
                    return TaskResult.Success;

                default:
                    return TaskResult.Failed;
            }
        }

        private TaskResult DoScanning(AutomatedGame game)
        {
            bagItems.Clear();

            // PLAYER_FIELD_PACK_SLOT_1 contains GUIDs of items in backpack
            // Each GUID is 64 bits = 2 uint fields (low, high)
            // Backpack has 16 slots
            int packSlotBase = (int)PlayerField.PLAYER_FIELD_PACK_SLOT_1;

            for (int slot = 0; slot < 16; slot++)
            {
                // Read GUID from two consecutive fields
                uint guidLow = game.Player[packSlotBase + slot * 2];
                uint guidHigh = game.Player[packSlotBase + slot * 2 + 1];
                ulong itemGuid = ((ulong)guidHigh << 32) | guidLow;

                if (itemGuid == 0)
                    continue;

                // Find the item object to get its entry
                if (game.Objects.TryGetValue(itemGuid, out var itemObject))
                {
                    uint entry = itemObject.Entry;
                    if (entry > 0)
                    {
                        bagItems.Add(new BagItem { Slot = (byte)slot, Entry = entry });
                        game.Log($"EquipItemTask: Found bag item entry {entry} in slot {slot}", LogLevel.Debug);
                    }
                }
            }

            if (bagItems.Count == 0)
            {
                game.Log("EquipItemTask: No items in bags to evaluate", LogLevel.Debug);
                state = TaskState.Complete;
                return TaskResult.Running;
            }

            game.Log($"EquipItemTask: Found {bagItems.Count} items to evaluate", LogLevel.Debug);
            currentBagItemIndex = 0;
            state = TaskState.QueryingBagItem;
            stateStartTime = DateTime.Now;
            return TaskResult.Running;
        }

        private TaskResult DoQueryingBagItem(AutomatedGame game)
        {
            if (currentBagItemIndex >= bagItems.Count)
            {
                game.Log($"EquipItemTask: Finished processing. Equipped {itemsEquipped} items, skipped {itemsSkipped}", LogLevel.Info);
                state = TaskState.Complete;
                return TaskResult.Running;
            }

            var bagItem = bagItems[currentBagItemIndex];
            currentBagItemEntry = bagItem.Entry;
            currentBagSlot = bagItem.Slot;

            // Check if already cached
            var template = ItemCache.Get(currentBagItemEntry);
            if (template != null)
            {
                state = TaskState.Comparing;
                stateStartTime = DateTime.Now;
                return TaskResult.Running;
            }

            // Query from server
            game.QueryItem(currentBagItemEntry);
            state = TaskState.WaitingForBagQuery;
            stateStartTime = DateTime.Now;
            return TaskResult.Running;
        }

        private TaskResult DoWaitingForBagQuery(AutomatedGame game)
        {
            var template = ItemCache.Get(currentBagItemEntry);
            if (template != null)
            {
                state = TaskState.Comparing;
                stateStartTime = DateTime.Now;
                return TaskResult.Running;
            }

            // Check timeout
            if ((DateTime.Now - stateStartTime).TotalSeconds > QueryTimeoutSeconds)
            {
                game.Log($"EquipItemTask: Timeout waiting for item {currentBagItemEntry} query", LogLevel.Warning);
                MoveToNextItem();
                return TaskResult.Running;
            }

            return TaskResult.Running;
        }

        private TaskResult DoQueryingEquippedItem(AutomatedGame game)
        {
            if (currentEquippedEntry == 0)
            {
                // No equipped item in this slot - can equip directly
                state = TaskState.Equipping;
                stateStartTime = DateTime.Now;
                return TaskResult.Running;
            }

            // Check if already cached
            var template = ItemCache.Get(currentEquippedEntry);
            if (template != null)
            {
                // Ready to compare
                return DoComparison(game);
            }

            // Query from server
            game.QueryItem(currentEquippedEntry);
            state = TaskState.WaitingForEquippedQuery;
            stateStartTime = DateTime.Now;
            return TaskResult.Running;
        }

        private TaskResult DoWaitingForEquippedQuery(AutomatedGame game)
        {
            var template = ItemCache.Get(currentEquippedEntry);
            if (template != null)
            {
                return DoComparison(game);
            }

            // Check timeout
            if ((DateTime.Now - stateStartTime).TotalSeconds > QueryTimeoutSeconds)
            {
                game.Log($"EquipItemTask: Timeout waiting for equipped item {currentEquippedEntry} query", LogLevel.Warning);
                MoveToNextItem();
                return TaskResult.Running;
            }

            return TaskResult.Running;
        }

        private TaskResult DoComparing(AutomatedGame game)
        {
            var bagTemplate = ItemCache.Get(currentBagItemEntry);
            if (bagTemplate == null)
            {
                MoveToNextItem();
                return TaskResult.Running;
            }

            // Check if this is equippable gear we handle
            if (!bagTemplate.IsEquippableGear)
            {
                game.Log($"EquipItemTask: {bagTemplate.Name} is not equippable gear", LogLevel.Debug);
                MoveToNextItem();
                return TaskResult.Running;
            }

            int equipSlot = bagTemplate.GetEquipmentSlot();
            if (equipSlot < 0 || !HandledEquipmentSlots.ContainsKey(equipSlot))
            {
                game.Log($"EquipItemTask: {bagTemplate.Name} slot ({bagTemplate.InventoryType}) not handled", LogLevel.Debug);
                MoveToNextItem();
                return TaskResult.Running;
            }

            currentTargetEquipSlot = equipSlot;

            // Get currently equipped item entry
            // PLAYER_VISIBLE_ITEM_X_ENTRYID fields are at consecutive offsets
            int visibleItemField = (int)PlayerField.PLAYER_VISIBLE_ITEM_1_ENTRYID + HandledEquipmentSlots[equipSlot] * 2;
            currentEquippedEntry = game.Player[visibleItemField];

            if (currentEquippedEntry == 0)
            {
                // Slot is empty - equip directly
                game.Log($"EquipItemTask: Slot {equipSlot} is empty, will equip {bagTemplate.Name}", LogLevel.Debug);
                state = TaskState.Equipping;
                stateStartTime = DateTime.Now;
                return TaskResult.Running;
            }

            // Need to query equipped item for comparison
            state = TaskState.QueryingEquippedItem;
            stateStartTime = DateTime.Now;
            return TaskResult.Running;
        }

        private TaskResult DoComparison(AutomatedGame game)
        {
            var bagTemplate = ItemCache.Get(currentBagItemEntry);
            var equippedTemplate = ItemCache.Get(currentEquippedEntry);

            if (bagTemplate == null)
            {
                MoveToNextItem();
                return TaskResult.Running;
            }

            var playerClass = game.World.SelectedCharacter?.Class ?? Class.Warrior;
            uint playerLevel = (uint)game.Player.Level;

            // Check if player can equip this item
            if (!ItemScorer.CanEquip(bagTemplate, playerClass, playerLevel))
            {
                game.Log($"EquipItemTask: Cannot equip {bagTemplate.Name} (level/class restriction)", LogLevel.Debug);
                MoveToNextItem();
                return TaskResult.Running;
            }

            // Compare scores
            if (ItemScorer.IsBetter(bagTemplate, equippedTemplate, playerClass, playerLevel))
            {
                float bagScore = ItemScorer.CalculateScore(bagTemplate, playerClass, playerLevel);
                float equippedScore = equippedTemplate != null ? ItemScorer.CalculateScore(equippedTemplate, playerClass, playerLevel) : 0;

                game.Log($"EquipItemTask: {bagTemplate.Name} (score: {bagScore:F1}) is better than current (score: {equippedScore:F1})", LogLevel.Debug);
                state = TaskState.Equipping;
                stateStartTime = DateTime.Now;
            }
            else
            {
                game.Log($"EquipItemTask: {bagTemplate.Name} is not better than current equipment", LogLevel.Debug);
                itemsSkipped++;
                MoveToNextItem();
            }

            return TaskResult.Running;
        }

        private TaskResult DoEquipping(AutomatedGame game)
        {
            var bagTemplate = ItemCache.Get(currentBagItemEntry);
            string itemName = bagTemplate?.Name ?? $"item {currentBagItemEntry}";

            game.Log($"EquipItemTask: Equipping {itemName} from bag slot {currentBagSlot}", LogLevel.Info);

            // 255 = main backpack container
            game.AutoEquipItem(255, currentBagSlot);

            itemsEquipped++;
            state = TaskState.WaitingForEquip;
            stateStartTime = DateTime.Now;
            return TaskResult.Running;
        }

        private TaskResult DoWaitingForEquip(AutomatedGame game)
        {
            // Small delay to let server process
            if ((DateTime.Now - stateStartTime).TotalSeconds >= EquipDelaySeconds)
            {
                MoveToNextItem();
            }
            return TaskResult.Running;
        }

        private void MoveToNextItem()
        {
            currentBagItemIndex++;
            state = TaskState.QueryingBagItem;
            stateStartTime = DateTime.Now;
        }

        public override void Cleanup(AutomatedGame game)
        {
            bagItems.Clear();
        }
    }
}
