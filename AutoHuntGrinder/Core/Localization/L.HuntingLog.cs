namespace AutoHuntGrinder.Core.Localization;

internal static partial class L
{
    internal static class HuntingLog
    {
        public static readonly LocString ModeBills = new("huntingLog.mode.bills", "Mark bills");
        public static readonly LocString ModeHuntingLog = new("huntingLog.mode.huntingLog", "Hunting Log");
        public static readonly LocString ModeCustom = new("huntingLog.mode.custom", "Custom list");

        public static readonly LocString TitlePick = new("huntingLog.title.pick", "Pick a log to begin");
        public static readonly LocString DetailPick = new("huntingLog.detail.pick", "Queue a class or Grand Company log below and it will appear in your plan.");
        public static readonly LocString TitleAllDone = new("huntingLog.title.allDone", "Every queued log is complete");
        public static readonly LocString DetailAllDone = new("huntingLog.detail.allDone", "Queue another log below, or switch to another mode.");
        public static readonly LocString TitleBlocked = new("huntingLog.title.blocked", "Nothing to hunt yet");
        public static readonly LocString DetailBlocked = new("huntingLog.detail.blocked", "Your queued logs need a matching gearset or an unlocked class, or only have targets inside duties left.");
        public static readonly LocString DetailReady = new("huntingLog.detail.ready", "Press Start to work through your queued logs, one rank at a time.");
        public static readonly LocString StatusPickLogs = new("huntingLog.status.pickLogs", "Pick logs");
        public static readonly LocString StatusNothingToHunt = new("huntingLog.status.nothingToHunt", "Nothing to hunt");

        public static readonly LocString SentenceFinish = new("huntingLog.sentence.finish", "Finish");
        public static readonly LocString LogsNone = new("huntingLog.logsNone", "no logs yet");
        public static readonly LocPlural LogsCount = new("huntingLog.logsCount", "{0} log", "{0} logs");
        public static readonly LocString BookRank = new("huntingLog.bookRank", "{0}, rank {1}");
        public static readonly LocString WhenDone = new("huntingLog.whenDone", "When every log is done");
        public static readonly LocString PlanHint = new("huntingLog.plan.hint", "The logs you queue will appear here.");
        public static readonly LocString PlanNext = new("huntingLog.plan.next", "Up first: {0}  ·  {1}");
        public static readonly LocPlural KillsLeftInRank = new("huntingLog.plan.killsLeftInRank", "{0} kill left in this rank", "{0} kills left in this rank");
        public static readonly LocString PlanAllDone = new("huntingLog.plan.allDone", "Every log you queued is complete.");
        public static readonly LocString PlanBlocked = new("huntingLog.plan.blocked", "None of your queued logs can advance right now.");
        public static readonly LocString ReasonPick = new("huntingLog.reason.pick", "queue at least one log");
        public static readonly LocString ReasonAllDone = new("huntingLog.reason.allDone", "every log you queued is complete");
        public static readonly LocString ReasonBlocked = new("huntingLog.reason.blocked", "none of your queued logs can advance");

        public static readonly LocString QueueTitle = new("huntingLog.queue.title", "Your queue");
        public static readonly LocString QueueHint = new("huntingLog.queue.hint", "Logs are worked from the top down.");
        public static readonly LocString QueueEmpty = new("huntingLog.queue.empty", "No logs queued.");
        public static readonly LocString MoveUp = new("huntingLog.queue.moveUp", "Move up");
        public static readonly LocString MoveDown = new("huntingLog.queue.moveDown", "Move down");
        public static readonly LocString RemoveFromQueue = new("huntingLog.queue.remove", "Remove from the queue");

        public static readonly LocString Library = new("huntingLog.library.title", "Hunting Log");
        public static readonly LocString NotLoggedIn = new("huntingLog.library.notLoggedIn", "Log in to read your Hunting Log.");
        public static readonly LocString QueueToggle = new("huntingLog.library.queue", "Queue this log");
        public static readonly LocString QueueToggleHelp = new("huntingLog.library.queueHelp", "Queued logs are worked in order when you press Start.");
        public static readonly LocString QueuePosition = new("huntingLog.library.queuePosition", "Number {0} in your queue.");

        public static readonly LocString RankOf = new("huntingLog.book.rankOf", "Rank {0} of {1}");
        public static readonly LocString Complete = new("huntingLog.book.complete", "Complete");
        public static readonly LocString NotUnlocked = new("huntingLog.book.notUnlocked", "Not unlocked");
        public static readonly LocString Unavailable = new("huntingLog.book.unavailable", "Unavailable");
        public static readonly LocString NeedsGearset = new("huntingLog.book.needsGearset", "Needs a gearset");
        public static readonly LocString NeedsGearsetHelp = new("huntingLog.book.needsGearsetHelp", "No saved gearset uses this class or its job, so the run skips this log. Save one in the Gear Set list and it is picked up.");
        public static readonly LocString NotUnlockedHelp = new("huntingLog.book.notUnlockedHelp", "This character has not taken up the class yet, so its log cannot advance.");
        public static readonly LocString RankProgress = new("huntingLog.book.rankProgress", "{0}/{1} kills this rank");

        public static readonly LocString Rank = new("huntingLog.rank", "Rank {0}");
        public static readonly LocString RankDone = new("huntingLog.rank.done", "Done.");
        public static readonly LocString RankCurrent = new("huntingLog.rank.current", "In progress.");
        public static readonly LocString RankLocked = new("huntingLog.rank.locked", "Opens once the rank before it is done.");
        public static readonly LocString RankLevelFloor = new("huntingLog.rank.levelFloor", "Targets from level {0}.");

        public static readonly LocString ZoneLine = new("huntingLog.entry.zoneLine", "{0} · {1}");
        public static readonly LocString NoTargets = new("huntingLog.entry.none", "This rank lists no targets.");
        public static readonly LocString BadgeInDuty = new("huntingLog.badge.inDuty", "In a duty: skipped");
        public static readonly LocString BadgeAreaOnly = new("huntingLog.badge.areaOnly", "Area only");
        public static readonly LocString BadgeNoSpawns = new("huntingLog.badge.noSpawns", "No spawn data");
        public static readonly LocString BadgeFateOnly = new("huntingLog.badge.fateOnly", "FATE only");
        public static readonly LocString InDutyHelp = new("huntingLog.badge.inDutyHelp", "This target lives inside a duty, so the run leaves it to you.");
        public static readonly LocString AreaOnlyHelp = new("huntingLog.badge.areaOnlyHelp", "Only the sub-area is known, so the run sweeps it until the target shows up.");
        public static readonly LocString NoSpawnsHelp = new("huntingLog.badge.noSpawnsHelp", "No known spawn points, so the run leaves this target to you.");
        public static readonly LocString FateOnlyHelp = new("huntingLog.badge.fateOnlyHelp", "Only known to spawn in FATEs, and the run never fights a FATE's monsters, so it leaves this target to you.");

        public static readonly LocString Completes = new("huntingLog.footer.completes", "Completes {0}");
        public static readonly LocString Earned = new("huntingLog.footer.earned", "Earned");
        public static readonly LocString NotYet = new("huntingLog.footer.notYet", "Not yet");
        public static readonly LocString Unknown = new("huntingLog.footer.unknown", "Unknown");
        public static readonly LocString Check = new("huntingLog.footer.check", "Check");
        public static readonly LocString Checking = new("huntingLog.footer.checking", "Checking…");
        public static readonly LocString CheckHelp = new("huntingLog.footer.checkHelp", "Asks the server for your achievements, the same way opening the Achievements window does.");
        public static readonly LocString CheckWait = new("huntingLog.footer.checkWait", "Asked a moment ago. You can ask again in a few seconds.");
    }
}
