using AutoHuntGrinder.Core.Spawns.Data;
using System.Numerics;

namespace AutoHuntGrinder.Core.Spawns;

internal static class MobSpawns
{
    public const int NotFound = -1;

    private const int CoordinatesPerPoint = 2;

    private static SpawnPoint[]? decodedPoints;

    public static ReadOnlySpan<ushort> NameIds => MobSpawnTable.NameIds;

    private static SpawnPoint[] Points => decodedPoints ??= Decode();

    public static bool TryGet(uint nameId, uint territoryId, out ReadOnlySpan<SpawnPoint> points)
    {
        var entry = EntryOf(IndexOf(nameId), territoryId);
        if (entry == NotFound)
        {
            points = default;
            return false;
        }

        points = PointsAt(entry);
        return true;
    }

    // A FATE's mobs are never fought outside it, so a zone known only from FATE reports gives a search nothing to find.
    public static bool TryGetSearchable(uint nameId, uint territoryId, out ReadOnlySpan<SpawnPoint> points)
    {
        var entry = EntryOf(IndexOf(nameId), territoryId);
        if (entry == NotFound || IsFateOnlyAt(entry))
        {
            points = default;
            return false;
        }

        points = PointsAt(entry);
        return points.Length > 0;
    }

    // territoryId 0 asks about every zone the mob is known in.
    public static bool IsFateOnly(uint nameId, uint territoryId)
    {
        var nameIndex = IndexOf(nameId);
        if (nameIndex == NotFound)
        {
            return false;
        }

        if (territoryId == 0)
        {
            return FirstSearchableEntry(nameIndex) == NotFound;
        }

        var entry = EntryOf(nameIndex, territoryId);
        return entry != NotFound && IsFateOnlyAt(entry);
    }

    // territoryId 0 asks about every zone the mob is known in.
    public static bool IsSearchable(uint nameId, uint territoryId)
        => territoryId == 0 ? FirstSearchableEntry(IndexOf(nameId)) != NotFound : TryGetSearchable(nameId, territoryId, out _);

    // The table lists a mob's busiest searchable zone first; 0 when no zone can be searched.
    public static uint FirstSearchableTerritory(uint nameId)
    {
        var entry = FirstSearchableEntry(IndexOf(nameId));
        return entry == NotFound ? 0u : MobSpawnTable.EntryTerritoryIds[entry];
    }

    public static ReadOnlySpan<ushort> Territories(uint nameId)
    {
        var nameIndex = IndexOf(nameId);
        return nameIndex == NotFound ? default : TerritoriesAt(nameIndex);
    }

    public static int IndexOf(uint nameId)
    {
        if (nameId > ushort.MaxValue)
        {
            return NotFound;
        }

        var index = MobSpawnTable.NameIds.BinarySearch((ushort)nameId);
        return index < 0 ? NotFound : index;
    }

    private static ReadOnlySpan<ushort> TerritoriesAt(int nameIndex)
        => MobSpawnTable.EntryTerritoryIds.Slice(MobSpawnTable.NameEntryStarts[nameIndex], MobSpawnTable.NameEntryCounts[nameIndex]);

    private static ReadOnlySpan<SpawnPoint> PointsAt(int entry)
        => new(Points, MobSpawnTable.EntryPointStarts[entry], MobSpawnTable.EntryPointCounts[entry]);

    private static bool IsFateOnlyAt(int entry) => MobSpawnTable.EntryFateOnly[entry] != 0;

    private static int EntryOf(int nameIndex, uint territoryId)
    {
        if (nameIndex == NotFound || territoryId > ushort.MaxValue)
        {
            return NotFound;
        }

        var offset = TerritoriesAt(nameIndex).IndexOf((ushort)territoryId);
        return offset < 0 ? NotFound : MobSpawnTable.NameEntryStarts[nameIndex] + offset;
    }

    private static int FirstSearchableEntry(int nameIndex)
    {
        if (nameIndex == NotFound)
        {
            return NotFound;
        }

        int start = MobSpawnTable.NameEntryStarts[nameIndex];
        var end = start + MobSpawnTable.NameEntryCounts[nameIndex];
        for (var entry = start; entry < end; entry++)
        {
            if (!IsFateOnlyAt(entry) && MobSpawnTable.EntryPointCounts[entry] > 0)
            {
                return entry;
            }
        }

        return NotFound;
    }

    private static SpawnPoint[] Decode()
    {
        var planar = MobSpawnTable.PlanarCoordinates;
        var heights = MobSpawnTable.Heights;
        var kinds = MobSpawnTable.EntryKinds;
        var starts = MobSpawnTable.EntryPointStarts;
        var counts = MobSpawnTable.EntryPointCounts;
        var decoded = new SpawnPoint[heights.Length];
        for (var entry = 0; entry < kinds.Length; entry++)
        {
            var kind = (SpawnKind)kinds[entry];
            int start = starts[entry];
            var end = start + counts[entry];
            for (var point = start; point < end; point++)
            {
                var coordinate = point * CoordinatesPerPoint;
                var position = new Vector3(planar[coordinate], HeightOf(heights[point]), planar[coordinate + 1]);
                decoded[point] = new SpawnPoint(position, kind);
            }
        }

        return decoded;
    }

    private static float HeightOf(sbyte steps)
        => steps == MobSpawnTable.UnknownHeight ? float.NaN : steps * MobSpawnTable.HeightStep;
}
