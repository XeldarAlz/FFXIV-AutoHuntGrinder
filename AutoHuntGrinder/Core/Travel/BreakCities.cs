using AutoHuntGrinder.Core.Hunts;

namespace AutoHuntGrinder.Core.Travel;

internal readonly record struct BreakCity(uint TerritoryId, ExpansionKind Expansion);

internal static class BreakCities
{
    private const uint TuliyollalTerritoryId = 1185;
    private const uint SolutionNineTerritoryId = 1186;
    private const uint LimsaLominsaLowerDecksTerritoryId = 129;
    private const uint NewGridaniaTerritoryId = 132;

    // Only hubs with a clean navmesh and open ground to wander; the cramped ones stalled or faulted the break.
    // Newest expansion first, the order the settings list shows them in.
    private static readonly BreakCity[] all =
    [
        new(TuliyollalTerritoryId, ExpansionKind.DT),
        new(SolutionNineTerritoryId, ExpansionKind.DT),
        new(LimsaLominsaLowerDecksTerritoryId, ExpansionKind.ARR),
        new(NewGridaniaTerritoryId, ExpansionKind.ARR),
    ];

    public static ReadOnlySpan<BreakCity> All => all;

    public static bool Contains(uint territoryId)
    {
        for (var index = 0; index < all.Length; index++)
        {
            if (all[index].TerritoryId == territoryId)
            {
                return true;
            }
        }

        return false;
    }

    public static HashSet<uint> NewDefaultSelection()
    {
        var selection = new HashSet<uint>(all.Length);
        for (var index = 0; index < all.Length; index++)
        {
            selection.Add(all[index].TerritoryId);
        }

        return selection;
    }
}
