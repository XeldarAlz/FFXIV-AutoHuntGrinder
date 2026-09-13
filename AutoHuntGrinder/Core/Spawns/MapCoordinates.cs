namespace AutoHuntGrinder.Core.Spawns;

internal static class MapCoordinates
{
    // Inverse of the client's map coordinate (0.02 map units per yalm, plus 2048 / SizeFactor, plus 1);
    // tools/MobSpawns converts the position dataset with the same formula.
    public static float ToWorld(float mapCoordinate, float sizeFactor, float offset)
        => 50f * (mapCoordinate - 1f) - 102400f / sizeFactor - offset;
}
