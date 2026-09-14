using AutoHuntGrinder.Core.HuntingLog;
using AutoHuntGrinder.Core.Localization;
using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace AutoHuntGrinder.Windows.Components;

internal static class SpawnBadge
{
    // Returns 0 for full point coverage, which carries no badge.
    public static float Draw(ImDrawListPtr drawList, SpawnCoverage coverage, float rightX, float midY)
    {
        var text = Text(coverage);
        return text.Length == 0 ? 0f : Badge.Draw(drawList, text, Color(coverage), rightX, midY);
    }

    private static string Text(SpawnCoverage coverage) => coverage switch
    {
        SpawnCoverage.InDuty => Loc.T(L.HuntingLog.BadgeInDuty),
        SpawnCoverage.AreaOnly => Loc.T(L.HuntingLog.BadgeAreaOnly),
        SpawnCoverage.FateOnly => Loc.T(L.HuntingLog.BadgeFateOnly),
        SpawnCoverage.NoData => Loc.T(L.HuntingLog.BadgeNoSpawns),
        _ => string.Empty,
    };

    private static Vector4 Color(SpawnCoverage coverage) => coverage switch
    {
        SpawnCoverage.InDuty => Styling.AccentAmber,
        SpawnCoverage.AreaOnly => Styling.AccentBlue,
        SpawnCoverage.FateOnly => Styling.AccentNebula,
        _ => Styling.AccentRose,
    };
}
