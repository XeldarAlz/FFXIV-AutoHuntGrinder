using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoHuntGrinder.Core.Travel;

internal static class TerritoryNames
{
    private static readonly Dictionary<uint, string> names = new();

    public static string Of(uint territoryId)
    {
        if (names.TryGetValue(territoryId, out var cached))
        {
            return cached;
        }

        var name = Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId)?.PlaceName.ValueNullable?.Name.ExtractText();
        var resolved = string.IsNullOrWhiteSpace(name) ? $"territory {territoryId}" : name;
        names[territoryId] = resolved;
        return resolved;
    }
}
