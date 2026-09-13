using ECommons.DalamudServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace AutoHuntGrinder.Core.Game.Ops;

// Items are used through UseAction(ActionType.Item), with a high-quality copy addressed as its item id plus one million.
internal static unsafe class FoodOps
{
    public const uint WellFedStatusId = 48;
    public const uint MedicatedStatusId = 49;

    private const uint HighQualityItemIdOffset = 1_000_000;
    // Lets the game take the item from whichever inventory slot holds it.
    private const uint UseFromAnySlot = 65535;
    private const string UseThrottleKey = "AutoHuntGrinder.Food.Use";
    // The game drops item uses sent faster than this.
    private const int UseThrottleMs = 500;
    private const float SecondsPerMinute = 60f;

    // Item UI categories holding food and medicine; the granted status weeds out everything else in them.
    private static readonly uint[] ConsumableUiCategories = [44, 45, 46];
    private static readonly InventoryType[] Bags = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    private static readonly HashSet<uint> ownedItemIds = [];

    private static ConsumableEntry[]? catalog;

    private static ConsumableEntry[] Catalog => catalog ??= BuildCatalog();

    public static float MinimumBuffSeconds(Configuration configuration)
        => Math.Max(0, configuration.AutoConsumeMinMinutes) * SecondsPerMinute;

    public static bool HasStatus(uint statusId, float minimumSeconds)
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return false;
        }

        var statuses = player.StatusList;
        for (var index = 0; index < statuses.Length; index++)
        {
            var status = statuses[index];
            if (status is null || status.StatusId != statusId)
            {
                continue;
            }

            if (minimumSeconds <= 0 || status.RemainingTime > minimumSeconds)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsAvailable(ConsumableEntry entry)
        => (entry.CanBeHq && ItemCount(entry.ItemId, highQuality: true) > 0)
        || ItemCount(entry.ItemId, highQuality: false) > 0;

    // Cheap enough to run before every upkeep, so a healthy buff costs nothing.
    public static bool AnyNeeded(Configuration configuration)
    {
        var minimumSeconds = MinimumBuffSeconds(configuration);
        var items = configuration.AutoConsumeItems;
        for (var index = 0; index < items.Count; index++)
        {
            var entry = items[index];
            if (!HasStatus(entry.StatusId, minimumSeconds) && IsAvailable(entry))
            {
                return true;
            }
        }

        return false;
    }

    // Throttled, so a caller polling until the buff lands cannot send UseAction faster than the game takes it.
    public static bool UseConsumable(ConsumableEntry entry)
    {
        if (!EzThrottler.Throttle(UseThrottleKey, UseThrottleMs))
        {
            return false;
        }

        var actions = ActionManager.Instance();
        if (actions is null)
        {
            return false;
        }

        var useHighQuality = entry.CanBeHq && ItemCount(entry.ItemId, highQuality: true) > 0;
        if (!useHighQuality && ItemCount(entry.ItemId, highQuality: false) == 0)
        {
            return false;
        }

        var actionId = useHighQuality ? entry.ItemId + HighQualityItemIdOffset : entry.ItemId;
        actions->UseAction(ActionType.Item, actionId, extraParam: UseFromAnySlot);
        return true;
    }

    // The catalog entries with at least one copy in the bags, found in a single pass over the bags.
    public static void FillAvailable(List<ConsumableEntry> destination)
    {
        destination.Clear();
        CollectOwnedItemIds();
        var entries = Catalog;
        for (var index = 0; index < entries.Length; index++)
        {
            if (ownedItemIds.Contains(entries[index].ItemId))
            {
                destination.Add(entries[index]);
            }
        }
    }

    private static int ItemCount(uint itemId, bool highQuality)
    {
        var inventory = InventoryManager.Instance();
        return inventory is null ? 0 : inventory->GetInventoryItemCount(itemId, highQuality);
    }

    private static void CollectOwnedItemIds()
    {
        ownedItemIds.Clear();
        var inventory = InventoryManager.Instance();
        if (inventory is null)
        {
            return;
        }

        for (var bagIndex = 0; bagIndex < Bags.Length; bagIndex++)
        {
            var container = inventory->GetInventoryContainer(Bags[bagIndex]);
            if (container is null || !container->IsLoaded)
            {
                continue;
            }

            for (var slotIndex = 0; slotIndex < container->Size; slotIndex++)
            {
                var slot = container->GetInventorySlot(slotIndex);
                if (slot is not null && slot->ItemId != 0)
                {
                    ownedItemIds.Add(slot->ItemId);
                }
            }
        }
    }

    private static ConsumableEntry[] BuildCatalog()
    {
        var entries = new List<ConsumableEntry>();
        foreach (var item in Svc.Data.GetExcelSheet<Item>())
        {
            if (Array.IndexOf(ConsumableUiCategories, item.ItemUICategory.RowId) < 0 || item.ItemAction.ValueNullable is not { } action)
            {
                continue;
            }

            // The first data value of a meal or medicine action is the status it grants.
            var statusId = (uint)action.Data[0];
            if (statusId is not (WellFedStatusId or MedicatedStatusId))
            {
                continue;
            }

            var name = item.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            entries.Add(new ConsumableEntry
            {
                ItemId = item.RowId,
                Name = name,
                StatusId = statusId,
                CanBeHq = item.CanBeHq,
            });
        }

        var sorted = entries.ToArray();
        Array.Sort(sorted, static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        return sorted;
    }
}
