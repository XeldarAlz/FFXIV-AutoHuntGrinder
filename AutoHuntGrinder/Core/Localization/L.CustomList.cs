namespace AutoHuntGrinder.Core.Localization;

internal static partial class L
{
    internal static class CustomList
    {
        public static readonly LocString Library = new("customList.library.title", "Custom list");
        public static readonly LocString TitlePick = new("customList.title.pick", "Add a mob to begin");
        public static readonly LocString DetailPick = new("customList.detail.pick", "Search for any monster below and set how many to hunt.");
        public static readonly LocString TitleAllDone = new("customList.title.allDone", "Your list is done");
        public static readonly LocString DetailAllDone = new("customList.detail.allDone", "Every mob on your list has its kills. Reset a row to hunt it again.");
        public static readonly LocString TitleBlocked = new("customList.title.blocked", "Nothing to hunt yet");
        public static readonly LocString DetailBlocked = new("customList.detail.blocked", "Turn on at least one mob in your list.");
        public static readonly LocString DetailReady = new("customList.detail.ready", "Press Start to hunt every mob on your list until each one has its kills.");
        public static readonly LocString StatusAddMobs = new("customList.status.addMobs", "Add mobs");

        public static readonly LocString MobsNone = new("customList.mobsNone", "no mobs yet");
        public static readonly LocPlural MobsCount = new("customList.mobsCount", "{0} mob", "{0} mobs");
        public static readonly LocString WhenDone = new("customList.whenDone", "When every mob is done");
        public static readonly LocString PlanHint = new("customList.plan.hint", "The mobs you add will appear here.");
        public static readonly LocPlural PlanKillsLeft = new("customList.plan.killsLeft", "{0} kill left across your list", "{0} kills left across your list");
        public static readonly LocString ReasonPick = new("customList.reason.pick", "add at least one mob");
        public static readonly LocString ReasonBlocked = new("customList.reason.blocked", "turn on at least one mob");
        public static readonly LocString ReasonAllDone = new("customList.reason.allDone", "every mob on your list is done");

        public static readonly LocString SearchHint = new("customList.search.hint", "Search monsters by name");
        public static readonly LocString SearchMore = new("customList.search.more", "Showing the first {0} matches. Keep typing to narrow it down.");
        public static readonly LocString Add = new("customList.search.add", "Add");
        public static readonly LocString Added = new("customList.search.added", "Added");
        public static readonly LocString ZonesMore = new("customList.search.zonesMore", "{0} +{1} more");

        public static readonly LocPlural Summary = new("customList.summary", "{0} mob  ·  {1} turned on", "{0} mobs  ·  {1} turned on");
        public static readonly LocString ResetAll = new("customList.resetAll", "Reset all");
        public static readonly LocString ResetAllHelp = new("customList.resetAllHelp", "Set every kill count back to 0.");
        public static readonly LocString Empty = new("customList.empty", "Your list is empty. Search above to add a mob.");
        public static readonly LocString AnyZone = new("customList.row.anyZone", "Any zone");
        public static readonly LocString NeededFormat = new("customList.row.neededFormat", "%d needed");
        public static readonly LocString EnabledHelp = new("customList.row.enabledHelp", "A mob turned off stays on the list but is skipped.");
        public static readonly LocString Reset = new("customList.row.reset", "Reset the kill count");
        public static readonly LocString Remove = new("customList.row.remove", "Remove from the list");
    }
}
