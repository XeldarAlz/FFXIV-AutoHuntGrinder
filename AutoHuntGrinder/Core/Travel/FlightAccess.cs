using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace AutoHuntGrinder.Core.Travel;

internal static unsafe class FlightAccess
{
    private static readonly Dictionary<uint, uint> completionFlagSetByTerritory = new();

    // The game answers whether flight is allowed only for a mounted character, so on foot the zone's aether current
    // completion stands in for it.
    public static bool IsAvailableIn(uint territoryId)
    {
        if (Svc.Condition[ConditionFlag.Mounted] && Svc.ClientState.TerritoryType == territoryId)
        {
            return Control.CanFly;
        }

        var completionFlagSet = CompletionFlagSetOf(territoryId);
        if (completionFlagSet == 0)
        {
            return false;
        }

        var playerState = PlayerState.Instance();
        return playerState is not null && playerState->IsAetherCurrentZoneComplete(completionFlagSet);
    }

    private static uint CompletionFlagSetOf(uint territoryId)
    {
        if (completionFlagSetByTerritory.TryGetValue(territoryId, out var cached))
        {
            return cached;
        }

        var resolved = Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId)?.AetherCurrentCompFlgSet.RowId ?? 0u;
        completionFlagSetByTerritory[territoryId] = resolved;
        return resolved;
    }
}
