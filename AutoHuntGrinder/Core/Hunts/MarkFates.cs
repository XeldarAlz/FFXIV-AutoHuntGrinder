using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoHuntGrinder.Core.Hunts;

internal readonly record struct MarkFate(uint FateId, string Name, byte MaxLevel);

// Some A Realm Reborn marks only spawn as the boss of one FATE; MobHuntTarget names that FATE.
internal static class MarkFates
{
    private static readonly Dictionary<uint, MarkFate> byTarget = [];

    public static bool TryGet(uint targetRowId, out MarkFate fate)
    {
        if (!byTarget.TryGetValue(targetRowId, out fate))
        {
            fate = Resolve(targetRowId);
            byTarget[targetRowId] = fate;
        }

        return fate.FateId != 0;
    }

    public static uint FateIdOf(uint targetRowId) => TryGet(targetRowId, out var fate) ? fate.FateId : 0;

    private static MarkFate Resolve(uint targetRowId)
    {
        if (!Svc.Data.GetExcelSheet<MobHuntTarget>().TryGetRow(targetRowId, out var target) || target.FATE.RowId == 0)
        {
            return default;
        }

        var fateId = target.FATE.RowId;
        var row = target.FATE.ValueNullable;
        var name = row?.Name.ExtractText() ?? string.Empty;
        return new MarkFate(fateId, name.Length == 0 ? $"FATE {fateId}" : name, row?.ClassJobLevelMax ?? 0);
    }
}
