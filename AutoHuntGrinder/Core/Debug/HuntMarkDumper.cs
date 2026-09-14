using AutoHuntGrinder.Core.Achievements;
using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Marks;
using AutoHuntGrinder.Core.Spawns;
using AutoHuntGrinder.Core.Travel;
using ECommons.DalamudServices;
using System.Text;
using ClientAchievementState = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement.AchievementState;

namespace AutoHuntGrinder.Core.Debug;

internal static class HuntMarkDumper
{
    public static void Dump()
    {
        DumpRegistry();
        DumpAchievementState();
        DumpAchievements();
        Svc.Chat.Print($"{AhgConstants.LogPrefix} Hunt mark dump written to the plugin log (/xllog).");
    }

    private static void DumpRegistry()
    {
        var marks = HuntMarkRegistry.Marks;
        Log($"registry: {marks.Length} marks, {LinkedCount(marks)} of them count toward a Mark achievement");
        for (var expansion = ExpansionKind.ARR; expansion <= ExpansionKind.DT; expansion++)
        {
            var line = new StringBuilder(expansion.ShortName()).Append(": ").Append(ZoneCount(expansion)).Append(" zones;");
            for (var rank = HuntMarkRank.B; rank <= HuntMarkRank.S; rank++)
            {
                AppendRankCount(line, marks, expansion, rank);
            }

            Log(line.ToString());
        }

        DumpExpansionWide(marks);
    }

    private static void AppendRankCount(StringBuilder line, ReadOnlySpan<HuntMark> marks, ExpansionKind expansion, HuntMarkRank rank)
    {
        var count = 0;
        var searchable = 0;
        for (var index = 0; index < marks.Length; index++)
        {
            var mark = marks[index];
            if (mark.Expansion != expansion || mark.Rank != rank)
            {
                continue;
            }

            count++;
            if (MobSpawns.IsSearchable(mark.NameId, HuntMarkRegistry.SpawnTerritoryAt(index)))
            {
                searchable++;
            }
        }

        line.Append(' ').Append(rank).Append(' ').Append(count).Append(" (").Append(searchable).Append(" with non-FATE points)");
    }

    private static void DumpExpansionWide(ReadOnlySpan<HuntMark> marks)
    {
        for (var index = 0; index < marks.Length; index++)
        {
            if (!HuntMarkRegistry.IsExpansionWideAt(index))
            {
                continue;
            }

            var mark = marks[index];
            var firstTerritory = MobSpawns.FirstSearchableTerritory(mark.NameId);
            var searchable = firstTerritory == 0
                ? "none with non-FATE points"
                : $"the first with non-FATE points is {TerritoryNames.Of(firstTerritory)} ({firstTerritory})";
            Log($"expansion-wide: {HuntMarkRegistry.NameAt(index)} (BNpcName {mark.NameId}, rank {mark.Rank}, {mark.Expansion.ShortName()}, first listed in {mark.TerritoryId}): the spawn table knows {MobSpawns.Territories(mark.NameId).Length} zone(s), {searchable}");
        }
    }

    // Expansion-wide marks keep the first zone that lists them, so they are left out of the zone count.
    private static int ZoneCount(ExpansionKind expansion)
    {
        var marks = HuntMarkRegistry.Marks;
        var order = HuntMarkRegistry.DisplayOrder;
        var zones = 0;
        uint previousTerritory = 0;
        for (var position = 0; position < order.Length; position++)
        {
            var markIndex = order[position];
            var mark = marks[markIndex];
            if (mark.Expansion != expansion || HuntMarkRegistry.IsExpansionWideAt(markIndex) || mark.TerritoryId == previousTerritory)
            {
                continue;
            }

            zones++;
            previousTerritory = mark.TerritoryId;
        }

        return zones;
    }

    private static int LinkedCount(ReadOnlySpan<HuntMark> marks)
    {
        var linked = 0;
        for (var index = 0; index < marks.Length; index++)
        {
            if (!MarkAchievements.ForMark(marks[index].NameId).IsEmpty)
            {
                linked++;
            }
        }

        return linked;
    }

    private static void DumpAchievementState()
    {
        var state = AchievementReader.LoadState();
        Log($"achievements: state {state?.ToString() ?? "unreadable"}, last load request {AchievementReader.MillisecondsSinceLoadRequest} ms ago (-1 = never)");
        if (state != ClientAchievementState.Invalid)
        {
            return;
        }

        Log(AchievementReader.RequestLoad()
            ? "achievements: requested the completion list; run the dump again in a few seconds to see it load"
            : "achievements: a load request went out under 30 s ago; not asking again yet");
    }

    private static void DumpAchievements()
    {
        var achievements = MarkAchievements.All;
        Log($"Mark achievements: {achievements.Length} of {MarkAchievements.TableSize} table rows resolved");
        for (var index = 0; index < achievements.Length; index++)
        {
            var achievement = achievements[index];
            var marks = MarkAchievements.Marks(achievement);
            Log($"{achievement.AchievementId} {achievement.Name} ({achievement.Expansion.ShortName()}, rank {achievement.Rank}, {marks.Length} marks, icon {achievement.IconId}): {AchievementReader.Status(achievement.AchievementId)}");
            for (var markIndex = 0; markIndex < marks.Length; markIndex++)
            {
                var mark = marks[markIndex];
                Log($"  {HuntMarkRegistry.NameOf(mark.NameId)} (BNpcName {mark.NameId}) in {TerritoryNames.Of(mark.TerritoryId)} ({mark.TerritoryId}): {SpawnText(mark)}");
            }
        }
    }

    private static string SpawnText(in HuntMark mark)
    {
        if (MobSpawns.TryGetSearchable(mark.NameId, mark.TerritoryId, out var points))
        {
            return $"{points.Length} non-FATE point(s)";
        }

        return MobSpawns.IsFateOnly(mark.NameId, mark.TerritoryId) ? "FATE points only" : "no points";
    }

    private static void Log(string message) => Svc.Log.Info($"{AhgConstants.LogPrefix} [HuntMarkDump] {message}");
}
