using System.Numerics;

namespace AutoHuntGrinder.Core.Spawns;

public enum SpawnKind : byte
{
    Point,
    Area,
}

// Y is float.NaN when the height is unknown, so it has to be snapped to the navmesh floor before use.
public readonly record struct SpawnPoint(Vector3 Position, SpawnKind Kind);
