using AutoHuntGrinder.Core.Marks.Data;
using AutoHuntGrinder.Core.Travel;
using System.Numerics;

namespace AutoHuntGrinder.Core.Marks;

// The spawn points a zone's hunt marks share, by rank. A mark stands at one of them when it is up, so a search that
// visits every point of its rank settles whether it is. Every point is a map position with no height, so it has to be
// snapped to the floor before use.
internal static class HuntSpawns
{
    private const int NotFound = -1;
    private const int CoordinatesPerPoint = 2;
    // A reported or landed position this close to a zone spawn point is that spawn point.
    private const float SameSpotMeters = 15f;
    private const float SameSpotSquared = SameSpotMeters * SameSpotMeters;

    // True when the registry lists the mark in territoryId, or in one zone at all for 0, and that zone has points of its
    // rank; an expansion-wide mark has no zone to search.
    public static bool Covers(uint nameId, uint territoryId) => ZoneOf(nameId, territoryId) != 0;

    // The registry's zone for the mark when Covers holds; 0 otherwise.
    public static uint ZoneOf(uint nameId, uint territoryId)
        => TryLocate(nameId, territoryId, out var zone, out _, out _) ? zone : 0;

    public static bool TryGetAnchor(uint nameId, uint territoryId, out Vector3 anchor)
    {
        if (!TryLocate(nameId, territoryId, out _, out var entry, out var rankBit))
        {
            anchor = default;
            return false;
        }

        var start = HuntSpawnTable.PointStarts[entry];
        var end = start + HuntSpawnTable.PointCounts[entry];
        for (var point = start; point < end; point++)
        {
            if ((HuntSpawnTable.RankBits[point] & rankBit) != 0)
            {
                anchor = PointAt(point);
                return true;
            }
        }

        anchor = default;
        return false;
    }

    // The known points first, then every spawn point of the mark's rank in its zone that none of them already stands on.
    public static Vector3[] Merge(uint nameId, uint territoryId, ReadOnlySpan<Vector3> known)
    {
        if (!TryLocate(nameId, territoryId, out _, out var entry, out var rankBit))
        {
            return known.ToArray();
        }

        var start = HuntSpawnTable.PointStarts[entry];
        var end = start + HuntSpawnTable.PointCounts[entry];
        var merged = new Vector3[known.Length + CountIn(start, end, rankBit)];
        known.CopyTo(merged);
        var kept = known.Length;
        for (var point = start; point < end; point++)
        {
            if ((HuntSpawnTable.RankBits[point] & rankBit) == 0)
            {
                continue;
            }

            var candidate = PointAt(point);
            if (!NearAny(merged, kept, candidate))
            {
                merged[kept++] = candidate;
            }
        }

        if (kept < merged.Length)
        {
            Array.Resize(ref merged, kept);
        }

        return merged;
    }

    private static bool TryLocate(uint nameId, uint territoryId, out uint zone, out int entry, out int rankBit)
    {
        zone = 0;
        entry = NotFound;
        rankBit = 0;
        var index = HuntMarkRegistry.IndexOf(nameId);
        if (index == HuntMarkRegistry.NotFound)
        {
            return false;
        }

        var listed = HuntMarkRegistry.SpawnTerritoryAt(index);
        if (listed == 0 || listed > ushort.MaxValue || (territoryId != 0 && territoryId != listed))
        {
            return false;
        }

        entry = HuntSpawnTable.TerritoryIds.BinarySearch((ushort)listed);
        if (entry < 0)
        {
            entry = NotFound;
            return false;
        }

        rankBit = HuntMarkRegistry.RankBit(HuntMarkRegistry.Marks[index].Rank);
        var start = HuntSpawnTable.PointStarts[entry];
        if (CountIn(start, start + HuntSpawnTable.PointCounts[entry], rankBit) == 0)
        {
            return false;
        }

        zone = listed;
        return true;
    }

    private static int CountIn(int start, int end, int rankBit)
    {
        var count = 0;
        for (var point = start; point < end; point++)
        {
            if ((HuntSpawnTable.RankBits[point] & rankBit) != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static Vector3 PointAt(int point)
    {
        var coordinate = point * CoordinatesPerPoint;
        return new Vector3(HuntSpawnTable.Coordinates[coordinate], float.NaN, HuntSpawnTable.Coordinates[coordinate + 1]);
    }

    private static bool NearAny(Vector3[] points, int count, Vector3 candidate)
    {
        for (var pointIndex = 0; pointIndex < count; pointIndex++)
        {
            if (GroundDistance.SquaredBetween(points[pointIndex], candidate) < SameSpotSquared)
            {
                return true;
            }
        }

        return false;
    }
}
