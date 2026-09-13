using AutoHuntGrinder.Core.Spawns.Data;
using System.Numerics;

namespace AutoHuntGrinder.Core.Spawns;

internal static class MobSpawns
{
    public const int NotFound = -1;

    private const int CoordinatesPerPoint = 2;

    private static SpawnPoint[]? points;

    public static ReadOnlySpan<ushort> NameIds => MobSpawnTable.NameIds;

    private static SpawnPoint[] Points => points ??= Decode();

    public static bool TryGet(uint nameId, uint territoryId, out ReadOnlySpan<SpawnPoint> found)
    {
        var entry = EntryOf(IndexOf(nameId), territoryId);
        if (entry == NotFound)
        {
            found = default;
            return false;
        }

        found = new ReadOnlySpan<SpawnPoint>(Points, MobSpawnTable.EntryPointStarts[entry], MobSpawnTable.EntryPointCounts[entry]);
        return true;
    }

    public static bool IsSupported(uint nameId) => IndexOf(nameId) != NotFound;

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

    private static int EntryOf(int nameIndex, uint territoryId)
    {
        if (nameIndex == NotFound || territoryId > ushort.MaxValue)
        {
            return NotFound;
        }

        var offset = TerritoriesAt(nameIndex).IndexOf((ushort)territoryId);
        return offset < 0 ? NotFound : MobSpawnTable.NameEntryStarts[nameIndex] + offset;
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
