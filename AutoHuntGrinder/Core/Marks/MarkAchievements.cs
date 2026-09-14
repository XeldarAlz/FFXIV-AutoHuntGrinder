using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoHuntGrinder.Core.Marks;

// The "Mark of the ..." achievements keep their mark lists server-side in an order no sheet follows, so each row names
// its rank and zones here, and the achievement's Key (how many unique marks it asks for) confirms the pairing.
internal static class MarkAchievements
{
    private static readonly ushort[] HoltTerritories = [148, 152, 153, 154];
    private static readonly ushort[] DesertTerritories = [140, 141, 145, 146, 147];
    private static readonly ushort[] SeaTerritories = [134, 135, 137, 138, 139, 180];
    private static readonly ushort[] LakeTerritories = [155, 156];
    private static readonly ushort[] DragonTerritories = [398, 399, 400];
    private static readonly ushort[] CloudAndIceTerritories = [397, 401, 402];
    private static readonly ushort[] WastesTerritories = [612, 620, 621];
    private static readonly ushort[] EastTerritories = [613, 614, 622];

    private static readonly Source[] Sources =
    [
        new(964, HuntMarkRank.B, HoltTerritories),
        new(969, HuntMarkRank.A, HoltTerritories),
        new(974, HuntMarkRank.S, HoltTerritories),
        new(965, HuntMarkRank.B, DesertTerritories),
        new(970, HuntMarkRank.A, DesertTerritories),
        new(975, HuntMarkRank.S, DesertTerritories),
        new(966, HuntMarkRank.B, SeaTerritories),
        new(971, HuntMarkRank.A, SeaTerritories),
        new(976, HuntMarkRank.S, SeaTerritories),
        new(967, HuntMarkRank.B, LakeTerritories),
        new(972, HuntMarkRank.A, LakeTerritories),
        new(977, HuntMarkRank.S, LakeTerritories),
        new(1259, HuntMarkRank.B, DragonTerritories),
        new(1261, HuntMarkRank.A, DragonTerritories),
        new(1263, HuntMarkRank.S, DragonTerritories),
        new(1260, HuntMarkRank.B, CloudAndIceTerritories),
        new(1262, HuntMarkRank.A, CloudAndIceTerritories),
        new(1264, HuntMarkRank.S, CloudAndIceTerritories),
        new(1910, HuntMarkRank.B, WastesTerritories),
        new(1911, HuntMarkRank.A, WastesTerritories),
        new(1912, HuntMarkRank.S, WastesTerritories),
        new(1913, HuntMarkRank.B, EastTerritories),
        new(1914, HuntMarkRank.A, EastTerritories),
        new(1915, HuntMarkRank.S, EastTerritories),
    ];

    private static Resolved? resolved;

    private static Resolved Data => resolved ??= Resolve();

    public static int TableSize => Sources.Length;

    public static ReadOnlySpan<MarkAchievement> All => Data.Achievements;

    public static ReadOnlySpan<HuntMark> Marks(in MarkAchievement achievement)
        => new(Data.Marks, achievement.FirstMark, achievement.MarkCount);

    private static Resolved Resolve()
    {
        var sheet = Svc.Data.GetExcelSheet<Achievement>();
        var achievements = new List<MarkAchievement>(Sources.Length);
        var marks = new List<HuntMark>();
        for (var sourceIndex = 0; sourceIndex < Sources.Length; sourceIndex++)
        {
            var source = Sources[sourceIndex];
            if (!sheet.TryGetRow(source.AchievementId, out var row))
            {
                Svc.Log.Warning($"{AhgConstants.LogPrefix} Mark achievements: achievement {source.AchievementId} is not in the sheet; dropped");
                continue;
            }

            var firstMark = marks.Count;
            CollectMarks(source, marks);
            var markCount = marks.Count - firstMark;
            if (markCount == 0 || markCount != row.Key.RowId)
            {
                Svc.Log.Warning($"{AhgConstants.LogPrefix} Mark achievements: {source.AchievementId} asks for {row.Key.RowId} marks but rank {source.Rank} in its zones has {markCount}; dropped");
                marks.RemoveRange(firstMark, markCount);
                continue;
            }

            achievements.Add(new MarkAchievement(source.AchievementId, source.Rank, marks[firstMark].Expansion, row.Name.ExtractText(), row.Icon, (ushort)firstMark, (byte)markCount));
        }

        Svc.Log.Info($"{AhgConstants.LogPrefix} Mark achievements: {achievements.Count} of {Sources.Length} resolved over {marks.Count} marks");
        return new Resolved([.. achievements], [.. marks]);
    }

    private static void CollectMarks(in Source source, List<HuntMark> marks)
    {
        var all = HuntMarkRegistry.Marks;
        var order = HuntMarkRegistry.DisplayOrder;
        var territories = source.Territories.AsSpan();
        for (var position = 0; position < order.Length; position++)
        {
            var mark = all[order[position]];
            if (mark.Rank == source.Rank && territories.Contains(mark.TerritoryId))
            {
                marks.Add(mark);
            }
        }
    }

    private readonly record struct Source(uint AchievementId, HuntMarkRank Rank, ushort[] Territories);

    private sealed record Resolved(MarkAchievement[] Achievements, HuntMark[] Marks);
}
