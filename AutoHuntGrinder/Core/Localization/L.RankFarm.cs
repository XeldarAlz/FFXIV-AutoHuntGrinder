namespace AutoHuntGrinder.Core.Localization;

internal static partial class L
{
    internal static class RankFarm
    {
        public static readonly LocString Mode = new("rankFarm.mode", "Rank farm");
        public static readonly LocString Hint = new("rankFarm.hint", "Choose A and/or S ranks and expansions. Search their known spawn points on each world once; no per-mob kill counts needed.");
        public static readonly LocString Pick = new("rankFarm.pick", "Choose ranks and expansions");
        public static readonly LocString Ranks = new("rankFarm.ranks", "Ranks");
        public static readonly LocString Expansions = new("rankFarm.expansions", "Expansions");
        public static readonly LocString Worlds = new("rankFarm.worlds", "World visits");
        public static readonly LocString Visit = new("rankFarm.visit", "Visit other worlds with Lifestream");
        public static readonly LocString VisitHelp = new("rankFarm.visitHelp", "Sweep the current world, then each other public world on this data center once. Stop after the last world. Requires Lifestream.");
        public static readonly LocString CurrentOnly = new("rankFarm.currentOnly", "Current world only");
        public static readonly LocString WorldPlan = new("rankFarm.worldPlan", "World order: {0}");
        public static readonly LocString Marks = new("rankFarm.marks", "{0} hunt marks");
        public static readonly LocString Blocked = new("rankFarm.blocked", "Lifestream must be loaded and idle for world visits.");
        public static readonly LocString Ready = new("rankFarm.ready", "Search the selected ranks and expansions, then finish after all selected worlds have been swept.");
        public static readonly LocString Limits = new("rankFarm.limits", "S ranks are hunted only if already spawned; this mode does not perform spawn triggers. SS marks and their minions are excluded. A completed sweep does not mean an achievement is complete.");
        public static readonly LocString Reading = new("rankFarm.reading", "Planning rank farm");
        public static readonly LocString Progress = new("rankFarm.progress", "World {0}/{1}: {2}");
        public static readonly LocString WhenDone = new("rankFarm.whenDone", "After all worlds have been swept");
        public static readonly LocString LifestreamPurpose = new("rankFarm.lifestreamPurpose", "Optional world travel between rank farming sweeps.");
    }
}
