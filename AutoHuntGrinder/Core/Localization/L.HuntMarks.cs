namespace AutoHuntGrinder.Core.Localization;

internal static partial class L
{
    internal static class HuntMarks
    {
        public static readonly LocString ViewMyList = new("huntMarks.view.myList", "My list");
        public static readonly LocString ViewMarks = new("huntMarks.view.marks", "Hunt marks");
        public static readonly LocString ViewAchievements = new("huntMarks.view.achievements", "Mark achievements");

        public static readonly LocString SearchHint = new("huntMarks.search.hint", "Search marks by name or zone");
        public static readonly LocString AllExpansions = new("huntMarks.filter.allExpansions", "All expansions");
        public static readonly LocString RankChip = new("huntMarks.filter.rank", "{0} rank");
        public static readonly LocString RankChipHelp = new("huntMarks.filter.rankHelp", "Show or hide rank {0} marks.");
        public static readonly LocPlural Count = new("huntMarks.count", "{0} mark", "{0} marks");
        public static readonly LocString NoRanks = new("huntMarks.noRanks", "Turn on a rank to see its marks.");
        public static readonly LocString NoMarks = new("huntMarks.noMarks", "No marks to show.");
        public static readonly LocString Add = new("huntMarks.add", "Add");
        public static readonly LocString InList = new("huntMarks.inList", "In list");
        public static readonly LocString AddHelp = new("huntMarks.addHelp", "Adds this mark to your list with one kill to hunt.");
        public static readonly LocString SRankHelp = new("huntMarks.sRankHelp", "S ranks appear only after an in-game trigger, so a run checks their known spawn points once and moves on.");
        public static readonly LocString ExpansionWideHelp = new("huntMarks.expansionWideHelp", "Appears only after an in-game trigger, in any zone of its expansion, so a run checks its known spawn points once and moves on.");
        public static readonly LocString NoSpawnsHelp = new("huntMarks.noSpawnsHelp", "No known spawn points, so a run leaves this mark to you.");

        public static readonly LocString EarnedSummary = new("huntMarks.achievements.earnedSummary", "{0} of {1} earned");
        public static readonly LocString NotLoaded = new("huntMarks.achievements.notLoaded", "Press Check on any card to load your achievements.");
        public static readonly LocPlural UniqueMarks = new("huntMarks.achievements.uniqueMarks", "{0} unique mark", "{0} unique marks");
        public static readonly LocString Progress = new("huntMarks.achievements.progress", "Progress");
        public static readonly LocString Asking = new("huntMarks.achievements.asking", "Asking…");
        public static readonly LocString ProgressOf = new("huntMarks.achievements.progressOf", "{0} of {1}");
        public static readonly LocString ProgressHelp = new("huntMarks.achievements.progressHelp", "Asks the server how many of these marks you have slain.");
        public static readonly LocString ProgressBusy = new("huntMarks.achievements.progressBusy", "Waiting for the answer to another request.");
        public static readonly LocString ProgressNoAnswer = new("huntMarks.achievements.progressNoAnswer", "No answer came back. Try again in a moment.");
        public static readonly LocString InListCount = new("huntMarks.achievements.inListCount", "{0} of {1} in your list");
        public static readonly LocString AddAll = new("huntMarks.achievements.addAll", "Add all");
        public static readonly LocString AllInList = new("huntMarks.achievements.allInList", "All in list");
        public static readonly LocString AddAllHelp = new("huntMarks.achievements.addAllHelp", "Adds every mark here that is not on your list yet, one kill each. The server does not say which ones you have already slain.");
        public static readonly LocString AddOne = new("huntMarks.achievements.addOne", "Add to your list");
    }
}
