namespace AutoHuntGrinder.Core.Localization;

internal static partial class L
{
    internal static class Progress
    {
        public static readonly LocString PhaseUpkeep = new("progress.phase.upkeep", "Upkeep");
        public static readonly LocString MarkLine = new("progress.markLine", "{0}  ·  {1}  ·  {2}/{3}");
        public static readonly LocString Kills = new("progress.kills", "{0}/{1}");
        public static readonly LocString NoSpawnData = new("progress.tooltip.noSpawnData", "{0} has no known spawn points, so the run leaves it to you.");
    }
}
