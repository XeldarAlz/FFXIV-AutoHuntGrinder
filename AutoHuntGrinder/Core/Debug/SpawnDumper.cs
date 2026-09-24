using AutoHuntGrinder.Core.Spawns;
using AutoHuntGrinder.Core.Travel;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Utility;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Numerics;
using System.Text;

namespace AutoHuntGrinder.Core.Debug;

internal static class SpawnDumper
{
    private const int ListedZoneLimit = 5;

    public static void DumpTarget()
    {
        if (Svc.Targets.Target is not IBattleNpc target)
        {
            Report("Spawns: no monster targeted. Target one, then re-run /ahg target.");
            return;
        }

        uint territoryId = Svc.ClientState.TerritoryType;
        var nameId = target.NameId;
        var position = target.Position;
        Report(FormattableString.Invariant(
            $"Spawns: NameId={nameId} in territory {territoryId} ({TerritoryNames.Of(territoryId)}) at ({position.X:0.0}, {position.Y:0.0}, {position.Z:0.0})"));
        ReportMapRoundTrip(position);
        if (MobSpawns.TryGet(nameId, territoryId, out var points))
        {
            ReportNearest(points, position, MobSpawns.IsFateOnly(nameId, territoryId));
            return;
        }

        ReportOtherZones(nameId);
    }

    private static void ReportNearest(ReadOnlySpan<SpawnPoint> points, Vector3 position, bool fateOnly)
    {
        var nearestIndex = 0;
        var nearestDistance = float.MaxValue;
        for (var index = 0; index < points.Length; index++)
        {
            var distance = GroundDistance.Between(points[index].Position, position);
            if (distance >= nearestDistance)
            {
                continue;
            }

            nearestIndex = index;
            nearestDistance = distance;
        }

        var nearest = points[nearestIndex].Position;
        var height = float.IsNaN(nearest.Y) ? "unknown" : FormattableString.Invariant($"{nearest.Y - position.Y:+0.0;-0.0} y");
        var source = fateOnly ? ", known only from FATEs, so a run skips them" : string.Empty;
        Report(FormattableString.Invariant(
            $"Spawns: the table has {points.Length} {points[nearestIndex].Kind} point(s) here{source}; the nearest, #{nearestIndex + 1} at ({nearest.X:0}, {nearest.Z:0}), is {nearestDistance:0.0} y from the target on the ground, height hint {height}"));
    }

    private static void ReportOtherZones(uint nameId)
    {
        var territories = MobSpawns.Territories(nameId);
        if (territories.IsEmpty)
        {
            Report("Spawns: the table has no points for this NameId in any zone.");
            return;
        }

        var listed = Math.Min(territories.Length, ListedZoneLimit);
        var builder = new StringBuilder("Spawns: the table has no points for this NameId here, only in ");
        for (var index = 0; index < listed; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append(TerritoryNames.Of(territories[index])).Append(" (").Append(territories[index]).Append(')');
        }

        if (territories.Length > listed)
        {
            builder.Append(" and ").Append(territories.Length - listed).Append(" more");
        }

        Report(builder.ToString());
    }

    private static void ReportMapRoundTrip(Vector3 position)
    {
        var mapId = Svc.ClientState.MapId;
        if (Svc.Data.GetExcelSheet<Map>().GetRowOrDefault(mapId) is not { SizeFactor: > 0 } map)
        {
            Report(FormattableString.Invariant($"Spawns: map {mapId} has no size factor; map check skipped."));
            return;
        }

        var planar = new Vector2(position.X, position.Z);
        var shown = MapUtil.WorldToMap(planar, map);
        var restored = new Vector2(
            MapCoordinates.ToWorld(shown.X, map.SizeFactor, map.OffsetX),
            MapCoordinates.ToWorld(shown.Y, map.SizeFactor, map.OffsetY));
        Report(FormattableString.Invariant(
            $"Spawns: map {mapId} (size {map.SizeFactor}, offset {map.OffsetX}/{map.OffsetY}) shows X {shown.X:0.0} Y {shown.Y:0.0}; the table's conversion puts that {Vector2.Distance(restored, planar):0.00} y from the target"));
    }

    private static void Report(string message)
    {
        Svc.Chat.Print($"{AhgConstants.LogPrefix} {message}");
        RunLog.Info(message);
    }
}
