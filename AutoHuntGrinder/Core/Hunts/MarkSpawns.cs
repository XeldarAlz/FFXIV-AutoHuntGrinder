using AutoHuntGrinder.Core.Hunts.Data;
using System.Numerics;
using System.Runtime.InteropServices;

namespace AutoHuntGrinder.Core.Hunts;

internal static class MarkSpawns
{
    private const int CoordinatesPerPoint = 3;

    public static bool TryGet(uint targetRowId, out uint territoryId, out ReadOnlySpan<Vector3> points)
    {
        var index = IndexOf(targetRowId);
        if (index < 0)
        {
            territoryId = 0;
            points = default;
            return false;
        }

        territoryId = MarkSpawnTable.TerritoryIds[index];
        var coordinates = MarkSpawnTable.Coordinates.Slice(
            MarkSpawnTable.PointStarts[index] * CoordinatesPerPoint,
            MarkSpawnTable.PointCounts[index] * CoordinatesPerPoint);
        points = MemoryMarshal.Cast<float, Vector3>(coordinates);
        return true;
    }

    public static bool IsSupported(uint targetRowId) => IndexOf(targetRowId) >= 0;

    private static int IndexOf(uint targetRowId)
    {
        if (targetRowId > ushort.MaxValue)
        {
            return -1;
        }

        return MarkSpawnTable.TargetRowIds.BinarySearch((ushort)targetRowId);
    }
}
