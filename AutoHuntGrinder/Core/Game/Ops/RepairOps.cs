using AutoHuntGrinder.Core.Game.Player;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Numerics;
using ObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind;

namespace AutoHuntGrinder.Core.Game.Ops;

internal readonly record struct RepairMender(uint TerritoryId, Vector3 Position, uint DataId, string Name);

internal static unsafe class RepairOps
{
    private const uint RepairGeneralActionId = 6;
    // Condition counts 300 per percent, so an intact item reads 30000.
    private const float ConditionPerPercent = 300f;
    private const uint MaxConditionRaw = 30_000;
    private const float IntactConditionPercent = 100f;
    // The first 13 equipped slots hold gear; the last is the soul crystal, which never wears.
    private const int EquippedGearSlotCount = 13;
    // Only English menus carry the word; other clients fall back to the first entry.
    private const string RepairMenuKeyword = "repair";
    private const int FallbackRepairMenuIndex = 0;
    private const string RepairAddonName = "Repair";
    private const string SelectYesnoAddonName = "SelectYesno";
    private const string SelectIconStringAddonName = "SelectIconString";
    // The game ignores actions and addon clicks repeated faster than this.
    private const int InteractThrottleMs = 500;
    private const string TriggerThrottleKey = "AutoHuntGrinder.Repair.Trigger";
    private const string RepairAllThrottleKey = "AutoHuntGrinder.Repair.RepairAll";
    private const string YesnoThrottleKey = "AutoHuntGrinder.Repair.Yesno";
    private const string MenuThrottleKey = "AutoHuntGrinder.Repair.Menu";
    private const string MenderFallbackName = "the Grand Company mender";

    private const uint MaelstromMenderTerritoryId = 128;
    private const uint MaelstromMenderDataId = 1003251;
    private const uint TwinAdderMenderTerritoryId = 132;
    private const uint TwinAdderMenderDataId = 1000394;
    private const uint ImmortalFlamesMenderTerritoryId = 130;
    private const uint ImmortalFlamesMenderDataId = 1004416;

    private static readonly Vector3 MaelstromMenderPosition = new(17.715698f, 40.200005f, 3.9520264f);
    private static readonly Vector3 TwinAdderMenderPosition = new(24.826416f, -8f, 93.18677f);
    private static readonly Vector3 ImmortalFlamesMenderPosition = new(32.85266f, 6.999999f, -81.31531f);

    public static float LowestEquippedConditionPercent()
    {
        var container = EquippedItems();
        if (container is null)
        {
            return IntactConditionPercent;
        }

        var lowest = MaxConditionRaw;
        var anyWorn = false;
        for (var slotIndex = 0; slotIndex < EquippedGearSlotCount; slotIndex++)
        {
            var slot = container->GetInventorySlot(slotIndex);
            if (slot is null || slot->ItemId == 0)
            {
                continue;
            }

            anyWorn = true;
            if (slot->Condition < lowest)
            {
                lowest = slot->Condition;
            }
        }

        return anyWorn ? lowest / ConditionPerPercent : IntactConditionPercent;
    }

    public static bool NeedsRepair(int thresholdPercent)
        => LowestEquippedConditionPercent() <= thresholdPercent;

    // Every worn piece names the Dark Matter grade it needs; any higher grade repairs it too.
    public static bool HasDarkMatterForAllEquipped()
    {
        var inventory = InventoryManager.Instance();
        var container = EquippedItems();
        if (inventory is null || container is null)
        {
            return false;
        }

        var items = Svc.Data.GetExcelSheet<Item>();
        for (var slotIndex = 0; slotIndex < EquippedGearSlotCount; slotIndex++)
        {
            var slot = container->GetInventorySlot(slotIndex);
            if (slot is null || slot->ItemId == 0 || !items.TryGetRow(slot->ItemId, out var item))
            {
                continue;
            }

            var darkMatterId = item.ItemRepair.ValueNullable?.Item.RowId ?? 0;
            if (darkMatterId != 0 && !HasDarkMatterOrBetter(darkMatterId, inventory))
            {
                return false;
            }
        }

        return true;
    }

    public static bool TriggerRepairAction()
    {
        if (!EzThrottler.Throttle(TriggerThrottleKey, InteractThrottleMs))
        {
            return false;
        }

        var actions = ActionManager.Instance();
        if (actions is null)
        {
            return false;
        }

        actions->UseAction(ActionType.GeneralAction, RepairGeneralActionId);
        return true;
    }

    public static bool RepairWindowOpen() => AddonReady(RepairAddonName);

    public static bool SelectYesnoOpen() => AddonReady(SelectYesnoAddonName);

    public static bool TalkMenuOpen() => AddonReady(SelectIconStringAddonName);

    public static bool MenuOrWindowOpen() => RepairWindowOpen() || TalkMenuOpen();

    public static bool ClickRepairAll()
    {
        if (!EzThrottler.Throttle(RepairAllThrottleKey, InteractThrottleMs) || !TryGetReadyAddon(RepairAddonName, out var addon))
        {
            return false;
        }

        new AddonMaster.Repair(addon).RepairAll();
        return true;
    }

    public static bool ClickSelectYesno()
    {
        if (!EzThrottler.Throttle(YesnoThrottleKey, InteractThrottleMs) || !TryGetReadyAddon(SelectYesnoAddonName, out var addon))
        {
            return false;
        }

        new AddonMaster.SelectYesno(addon).Yes();
        return true;
    }

    public static bool ClickMenuEntry(int index)
    {
        if (!EzThrottler.Throttle(MenuThrottleKey, InteractThrottleMs) || !TryGetReadyAddon(SelectIconStringAddonName, out var addon))
        {
            return false;
        }

        var entries = new AddonMaster.SelectIconString(addon).Entries;
        if (index < 0 || index >= entries.Length)
        {
            return false;
        }

        entries[index].Select();
        return true;
    }

    // The talk-menu entry that reads as a repair option, so a custom repair NPC needs no hand-tuned index.
    public static int RepairMenuEntryIndex(out bool matchedByText)
    {
        matchedByText = false;
        if (!TryGetReadyAddon(SelectIconStringAddonName, out var addon))
        {
            return FallbackRepairMenuIndex;
        }

        var entries = new AddonMaster.SelectIconString(addon).Entries;
        for (var entryIndex = 0; entryIndex < entries.Length; entryIndex++)
        {
            var text = entries[entryIndex].Text;
            if (!string.IsNullOrEmpty(text) && text.Contains(RepairMenuKeyword, StringComparison.OrdinalIgnoreCase))
            {
                matchedByText = true;
                return entryIndex;
            }
        }

        return FallbackRepairMenuIndex;
    }

    public static void HideRepairWindow()
    {
        var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Repair);
        if (agent is null)
        {
            return;
        }

        agent->Hide();
    }

    public static RepairMender? ResolveMender(Configuration configuration)
    {
        if (configuration.PreferredRepairNpc is { } custom)
        {
            return new RepairMender(custom.TerritoryId, new Vector3(custom.X, custom.Y, custom.Z), custom.DataId, custom.Name);
        }

        var state = PlayerState.Instance();
        if (state is null)
        {
            return null;
        }

        return state->GrandCompany switch
        {
            GrandCompanyId.Maelstrom => GrandCompanyMender(MaelstromMenderTerritoryId, MaelstromMenderPosition, MaelstromMenderDataId),
            GrandCompanyId.TwinAdder => GrandCompanyMender(TwinAdderMenderTerritoryId, TwinAdderMenderPosition, TwinAdderMenderDataId),
            GrandCompanyId.ImmortalFlames => GrandCompanyMender(ImmortalFlamesMenderTerritoryId, ImmortalFlamesMenderPosition, ImmortalFlamesMenderDataId),
            _ => null,
        };
    }

    // Several objects can share a base id, so the one nearest the player wins.
    public static IGameObject? FindNearestObjectByBaseId(uint baseId)
    {
        var objects = Svc.Objects;
        var origin = objects.LocalPlayer?.Position ?? Vector3.Zero;
        IGameObject? nearest = null;
        var nearestDistance = float.MaxValue;
        for (var index = 0; index < objects.Length; index++)
        {
            var candidate = objects[index];
            if (candidate is null || candidate.BaseId != baseId)
            {
                continue;
            }

            var distance = Vector3.DistanceSquared(candidate.Position, origin);
            if (distance >= nearestDistance)
            {
                continue;
            }

            nearest = candidate;
            nearestDistance = distance;
        }

        return nearest;
    }

    // Null unless the current target is an event NPC, the only kind that can run a repair.
    public static RepairNpc? CaptureTargetAsRepairNpc()
    {
        var targets = TargetSystem.Instance();
        var target = targets is null ? null : targets->Target;
        if (target is null || target->ObjectKind != ObjectKind.EventNpc)
        {
            return null;
        }

        var baseId = target->BaseId;
        var name = NpcName(baseId);
        if (string.IsNullOrEmpty(name))
        {
            name = target->NameString;
        }

        var position = target->Position;
        return new RepairNpc
        {
            TerritoryId = Svc.ClientState.TerritoryType,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            DataId = baseId,
            Name = name,
        };
    }

    private static RepairMender GrandCompanyMender(uint territoryId, Vector3 position, uint dataId)
    {
        var name = NpcName(dataId);
        return new RepairMender(territoryId, position, dataId, string.IsNullOrEmpty(name) ? MenderFallbackName : name);
    }

    private static string NpcName(uint dataId)
        => Svc.Data.GetExcelSheet<ENpcResident>().GetRowOrDefault(dataId)?.Singular.ExtractText() ?? string.Empty;

    private static bool HasDarkMatterOrBetter(uint requiredDarkMatterId, InventoryManager* inventory)
    {
        var grades = Svc.Data.GetExcelSheet<ItemRepairResource>();
        for (var rowIndex = 0; rowIndex < grades.Count; rowIndex++)
        {
            var darkMatterId = grades.GetRowAt(rowIndex).Item.RowId;
            if (darkMatterId >= requiredDarkMatterId && inventory->GetInventoryItemCount(darkMatterId) > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static InventoryContainer* EquippedItems()
    {
        var inventory = InventoryManager.Instance();
        if (inventory is null)
        {
            return null;
        }

        var container = inventory->GetInventoryContainer(InventoryType.EquippedItems);
        return container is not null && container->IsLoaded ? container : null;
    }

    private static bool AddonReady(string addonName)
        => TryGetReadyAddon(addonName, out _);

    private static bool TryGetReadyAddon(string addonName, out AtkUnitBase* addon)
        => GenericHelpers.TryGetAddonByName(addonName, out addon) && GenericHelpers.IsAddonReady(addon);
}
