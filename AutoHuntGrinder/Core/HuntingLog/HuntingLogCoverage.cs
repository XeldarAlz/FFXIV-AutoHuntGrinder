using AutoHuntGrinder.Core.Spawns;

namespace AutoHuntGrinder.Core.HuntingLog;

internal static class HuntingLogCoverage
{
    private const byte Unresolved = 0;

    private static byte[]? coverageByTarget;

    // targetIndex counts through the registry's whole target table (entry.FirstTarget plus the offset in the entry).
    // Spawn data never changes, so each target is classified once.
    public static SpawnCoverage Of(int targetIndex, in HuntingLogTarget target)
    {
        var table = coverageByTarget ??= new byte[HuntingLogRegistry.TargetCount];
        if ((uint)targetIndex >= (uint)table.Length)
        {
            return Classify(target);
        }

        var stored = table[targetIndex];
        if (stored == Unresolved)
        {
            stored = (byte)(Classify(target) + 1);
            table[targetIndex] = stored;
        }

        return (SpawnCoverage)(stored - 1);
    }

    public static bool IsHuntable(SpawnCoverage coverage) => coverage is SpawnCoverage.Points or SpawnCoverage.AreaOnly;

    // A zone the log lists decides first; otherwise the run hunts the mob wherever a search can find it, so that zone does.
    private static SpawnCoverage Classify(in HuntingLogTarget target)
    {
        if (target.InDuty)
        {
            return SpawnCoverage.InDuty;
        }

        var zones = HuntingLogRegistry.Zones(target);
        var areaFound = false;
        for (var zoneIndex = 0; zoneIndex < zones.Length; zoneIndex++)
        {
            if (!MobSpawns.TryGetSearchable(target.NameId, zones[zoneIndex], out var points))
            {
                continue;
            }

            if (points[0].Kind == SpawnKind.Point)
            {
                return SpawnCoverage.Points;
            }

            areaFound = true;
        }

        if (areaFound)
        {
            return SpawnCoverage.AreaOnly;
        }

        var fallback = MobSpawns.FirstSearchableTerritory(target.NameId);
        if (fallback != 0 && MobSpawns.TryGetSearchable(target.NameId, fallback, out var fallbackPoints))
        {
            return fallbackPoints[0].Kind == SpawnKind.Point ? SpawnCoverage.Points : SpawnCoverage.AreaOnly;
        }

        return MobSpawns.Territories(target.NameId).IsEmpty ? SpawnCoverage.NoData : SpawnCoverage.FateOnly;
    }
}
