using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoHuntGrinder.Core.Marks;

internal static class RankFarmSelection
{
    public static HuntMark[] Marks(Configuration configuration)
    {
        var result = new List<HuntMark>();
        var marks = HuntMarkRegistry.Marks;
        for (var index = 0; index < marks.Length; index++)
        {
            // Expansion-wide SS marks/minions do not have a single zone to sweep.
            if (!HuntMarkRegistry.IsExpansionWideAt(index)
                && RankFarmState.Matches(marks[index], configuration.FarmARanks, configuration.FarmSRanks, configuration.FarmExpansions))
            {
                result.Add(marks[index]);
            }
        }

        return [.. result];
    }

    public static uint[] Worlds(bool visitWorlds)
    {
        if (Svc.Objects.LocalPlayer is not { } player) return [];
        var current = player.CurrentWorld.RowId;
        if (!visitWorlds) return [current];
        var sheet = Svc.Data.GetExcelSheet<World>();
        var dataCenter = sheet.GetRow(current).DataCenter.RowId;
        return new[] { current }.Concat(sheet.Where(world => world.IsPublic && world.DataCenter.RowId == dataCenter && world.RowId != current)
            .OrderBy(world => world.Name.ToString()).Select(world => world.RowId)).ToArray();
    }

    public static string WorldName(uint world) => Svc.Data.GetExcelSheet<World>().GetRowOrDefault(world)?.Name.ToString() ?? world.ToString();
}
