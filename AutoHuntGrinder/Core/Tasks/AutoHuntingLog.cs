using AutoHuntGrinder.Core.Achievements;
using AutoHuntGrinder.Core.Game.Ops;
using AutoHuntGrinder.Core.HuntingLog;
using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Spawns;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

internal sealed class AutoHuntingLog(IReadOnlyList<byte> slots, AutoHuntSession session, HuntProgress progress) : AutoObjectiveHunt(session, progress)
{
    // Five ranks, each normally one or two passes, with room for targets that stay hidden a while.
    private const int MaxPassesPerBook = 25;
    // The next rank opens a moment after the last count of the old one lands.
    private const int RankOpenWaitMs = 10_000;
    private const int AchievementLoadWaitMs = 5_000;
    // Notice keys sit above every objective key, which fits in 17 bits.
    private const uint ForeignCompanyNotice = 0x0100_0000;
    private const uint NoGearsetNotice = 0x0200_0000;
    private const uint GearsetFailedNotice = 0x0300_0000;
    private const uint LevelNotice = 0x0400_0000;
    private const int RankNoticeFactor = 10;

    private readonly IReadOnlyList<byte> slots = slots;
    private readonly List<HuntObjective> objectives = new(HuntingLogRegistry.EntriesPerRank * HuntingLogRegistry.TargetsPerEntry);
    private readonly List<string> leftOutNames = [];
    private int givenUpTargets;

    private enum BookEnd : byte { Complete, LeftToPlayer, Skipped, Stalled, Stopped }

    protected override async Task Execute()
    {
        try
        {
            await Hunt();
        }
        catch (Exception exception)
        {
            RunSession.RecordFault(exception, CancelToken);
            throw;
        }
        finally
        {
            ReleaseCombatMovement("run");
        }
    }

    private async Task Hunt()
    {
        if (!await PrepareToHunt("Reading your Hunting Log"))
        {
            return;
        }

        await LoadAchievements();
        if (CancelToken.IsCancellationRequested)
        {
            return;
        }

        for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            var slot = slots[slotIndex];
            if (RunSession.IsLogEnded(slot))
            {
                Diag($"Run: the {HuntingLogRegistry.BookName(slot)} log already ended earlier in this run; skipping it");
                continue;
            }

            var end = await WorkBook(slot);
            Diag($"Run: the {HuntingLogRegistry.BookName(slot)} log (slot {slot}) ended {end}");
            if (end == BookEnd.Stopped)
            {
                return;
            }

            // A skipped log gets another try after a Resume, since a gearset saved during the pause lets it start.
            if (end != BookEnd.Skipped)
            {
                RunSession.EndLog(slot, metStopCondition: end != BookEnd.Stalled);
            }
        }

        Finish();
    }

    // A finished log may keep its last rank with its counts cleared, and then only its achievement shows it complete.
    private async Task LoadAchievements()
    {
        if (AchievementReader.IsLoaded)
        {
            return;
        }

        Status = "Reading your achievements";
        var requested = AchievementReader.RequestLoad();
        var loaded = await WaitUntilTimed(static () => AchievementReader.IsLoaded, AchievementLoadWaitMs, "achievements-loaded");
        if (CancelToken.IsCancellationRequested)
        {
            return;
        }

        Diag(loaded
            ? $"Run: the achievement list is loaded ({(requested ? "asked for it now" : "a request was already out")})"
            : $"Run: the achievement list did not load within {AchievementLoadWaitMs / TimeUnits.MillisecondsPerSecond}s (state {AchievementReader.LoadState()?.ToString() ?? "unreadable"}); log completion rests on the counts alone");
    }

    private async Task<BookEnd> WorkBook(byte slot)
    {
        if (!HuntingLogRegistry.TryGetBook(slot, out var book))
        {
            Diag($"Run: slot {slot} holds no Hunting Log; skipping it");
            return BookEnd.Skipped;
        }

        var bookName = HuntingLogRegistry.BookName(slot);
        HuntingLogReader.Refresh(force: true);
        if (HuntingLogReader.Status(slot) == HuntingLogStatus.Complete)
        {
            Diag($"Run: the {bookName} log is already complete");
            return BookEnd.Complete;
        }

        if (!await EnsureStanding())
        {
            return BookEnd.Stopped;
        }

        if (!await PrepareBook(book, bookName))
        {
            return CancelToken.IsCancellationRequested ? BookEnd.Stopped : BookEnd.Skipped;
        }

        for (var pass = RunSession.LogPassesUsed(slot) + 1; pass <= MaxPassesPerBook; pass++)
        {
            if (await WorkPass(slot, bookName, pass) is { } end)
            {
                return end;
            }
        }

        Warn($"Run: the {bookName} log used all {MaxPassesPerBook} passes; moving on");
        return BookEnd.Stalled;
    }

    private async Task<BookEnd?> WorkPass(byte slot, string bookName, int pass)
    {
        if (!await EnsureStanding())
        {
            return BookEnd.Stopped;
        }

        HuntingLogReader.Refresh(force: true);
        if (EndIfSettled(slot, bookName) is { } settled)
        {
            return settled;
        }

        var rank = HuntingLogReader.CurrentRank(slot);
        WarnIfUnderLevel(slot, rank, bookName);
        CollectObjectives(slot, rank, bookName);
        if (objectives.Count == 0)
        {
            return await WaitForNextRank(slot, rank, bookName);
        }

        Status = "Planning the route";
        var planned = ObjectivePlanner.Plan(objectives);
        LogPlan(planned, pass);
        var killedBefore = HuntingLogReader.RankProgress(slot, rank).Killed;
        if (!await HuntObjectives(planned, pass))
        {
            return BookEnd.Stopped;
        }

        RunSession.HuntPassesCompleted++;
        RunSession.CountLogPass(slot);
        HuntingLogReader.Refresh(force: true);
        if (EndIfSettled(slot, bookName) is { } end)
        {
            return end;
        }

        var rankNow = HuntingLogReader.CurrentRank(slot);
        if (rankNow != rank)
        {
            AnnounceRank(bookName, rank, rankNow);
            return null;
        }

        if (HuntingLogReader.RankProgress(slot, rank).Killed > killedBefore)
        {
            return null;
        }

        Diag($"Run: pass {pass} on the {bookName} log credited no kill; not planning another");
        return BookEnd.Stalled;
    }

    private BookEnd? EndIfSettled(byte slot, string bookName)
    {
        switch (HuntingLogReader.Status(slot))
        {
            case HuntingLogStatus.Complete:
                AnnounceComplete(bookName);
                return BookEnd.Complete;
            case HuntingLogStatus.Unavailable:
                Warn($"Run: the {bookName} log reads unavailable; skipping it");
                return BookEnd.Skipped;
            default:
                return null;
        }
    }

    private async Task<bool> PrepareBook(HuntingLogBook book, string bookName)
    {
        if (book.Kind == HuntingLogKind.GrandCompany)
        {
            if (book.Slot == HuntingLogReader.PlayerGrandCompanySlot())
            {
                return true;
            }

            Diag($"Run: the {bookName} log is not the character's Grand Company's; skipping it");
            if (RunSession.Notice(ForeignCompanyNotice | book.Slot))
            {
                Svc.Chat.Print($"{AhgConstants.LogPrefix} The {bookName} log belongs to a Grand Company you do not serve, so the run skips it.");
            }

            return false;
        }

        var result = await EquipGearset(book.Slot);
        switch (result)
        {
            case GearsetSwitchResult.AlreadyOnClass or GearsetSwitchResult.Switched:
                HuntingLogReader.Refresh(force: true);
                if (HuntingLogReader.Status(book.Slot) != HuntingLogStatus.Unavailable)
                {
                    return true;
                }

                Warn($"Run: the {bookName} log reads unavailable even on its class; skipping it");
                return false;
            case GearsetSwitchResult.Cancelled:
                return false;
            case GearsetSwitchResult.NoGearset:
                Diag($"Run: no gearset maps to the {bookName} log; skipping it");
                if (RunSession.Notice(NoGearsetNotice | book.Slot))
                {
                    Svc.Chat.Print($"{AhgConstants.LogPrefix} No saved gearset is a {bookName} or its job, so the run skips the {bookName} log.");
                }

                return false;
            default:
                Warn($"Run: equipping a gearset for the {bookName} log ended {result}; skipping the log");
                if (RunSession.Notice(GearsetFailedNotice | book.Slot))
                {
                    Svc.Chat.PrintError($"{AhgConstants.LogPrefix} Could not change to {bookName} for its Hunting Log, so the run skips it.");
                }

                return false;
        }
    }

    // The class cannot change in combat, so whatever is still attacking is fought off first.
    private async Task<GearsetSwitchResult> EquipGearset(byte slot)
    {
        Status = $"Changing to {HuntingLogRegistry.BookName(slot)}";
        var result = await GearsetSwitcher.EquipForSlot(slot, CancelToken);
        if (result != GearsetSwitchResult.InCombat)
        {
            return result;
        }

        await FightOffAttackers("gearset");
        return CancelToken.IsCancellationRequested ? GearsetSwitchResult.Cancelled : await GearsetSwitcher.EquipForSlot(slot, CancelToken);
    }

    private void CollectObjectives(byte slot, byte rank, string bookName)
    {
        objectives.Clear();
        leftOutNames.Clear();
        givenUpTargets = 0;
        var entries = HuntingLogRegistry.Rank(slot, rank);
        for (var entryOffset = 0; entryOffset < entries.Length; entryOffset++)
        {
            var entry = entries[entryOffset];
            var targets = HuntingLogRegistry.Targets(entry);
            for (var targetOffset = 0; targetOffset < targets.Length; targetOffset++)
            {
                var target = targets[targetOffset];
                var killed = HuntingLogReader.Killed(slot, entry.EntryIndex, target.TargetSlot);
                if (killed >= target.Needed)
                {
                    continue;
                }

                var objective = new HuntObjective(ObjectiveSource.HuntingLog, HuntingLogRegistry.SourceKey(entry, target), target.NameId, TerritoryFor(target), target.Needed, killed);
                if (RunSession.IsGivenUp(objective))
                {
                    givenUpTargets++;
                    continue;
                }

                var coverage = HuntingLogCoverage.Of(entry.FirstTarget + targetOffset, target);
                if (HuntingLogCoverage.IsHuntable(coverage))
                {
                    objectives.Add(objective);
                    continue;
                }

                if (NoteLeftOut(objective, LeftOutReason(coverage)))
                {
                    leftOutNames.Add(ObjectiveProgress.Name(objective));
                }
            }
        }

        if (leftOutNames.Count > 0)
        {
            var one = leftOutNames.Count == 1;
            Svc.Chat.Print($"{AhgConstants.LogPrefix} On {bookName} rank {rank + 1}, {string.Join(", ", leftOutNames)} {(one ? "is" : "are")} inside a duty, only known from FATEs or without known spawn points, so the run leaves {(one ? "it" : "them")} to you.");
        }
    }

    // A rank whose every count is met opens the next. Targets the run cannot hunt do not hold back the after-run action,
    // as marks without spawn data do not in a bill run; a target given up does.
    private async Task<BookEnd?> WaitForNextRank(byte slot, byte rank, string bookName)
    {
        var (killed, needed) = HuntingLogReader.RankProgress(slot, rank);
        if (needed == 0 || killed < needed)
        {
            Diag($"Run: nothing the run can hunt is left on {bookName} rank {rank + 1} ({killed}/{needed}, {givenUpTargets} target(s) given up)");
            return givenUpTargets == 0 ? BookEnd.LeftToPlayer : BookEnd.Stalled;
        }

        Status = $"Waiting for {bookName} rank {rank + 2} to open";
        if (await WaitUntilTimed(() => RankMoved(slot, rank), RankOpenWaitMs, "hunting-log-rank-open"))
        {
            return null;
        }

        if (CancelToken.IsCancellationRequested)
        {
            return BookEnd.Stopped;
        }

        Warn($"Run: {bookName} rank {rank + 1} reads full, but the next rank did not open within {RankOpenWaitMs / TimeUnits.MillisecondsPerSecond}s");
        return BookEnd.Stalled;
    }

    private void WarnIfUnderLevel(byte slot, byte rank, string bookName)
    {
        var floor = HuntingLogRegistry.LevelFloor(slot, rank);
        var level = Svc.Objects.LocalPlayer?.Level ?? 0;
        if (floor == 0 || level == 0 || level >= floor)
        {
            return;
        }

        if (!RunSession.Notice(LevelNotice | (uint)(slot * RankNoticeFactor + rank)))
        {
            return;
        }

        Warn($"Run: level {level} is below {bookName} rank {rank + 1}'s lowest monster level {floor}");
        Svc.Chat.Print($"{AhgConstants.LogPrefix} At level {level} you are below every monster on {bookName} rank {rank + 1} (level {floor} and up), so its fights may go badly.");
    }

    private void Finish()
    {
        HuntingLogReader.Refresh(force: true);
        var complete = 0;
        var finished = 0;
        for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            var slot = slots[slotIndex];
            if (HuntingLogReader.Status(slot) == HuntingLogStatus.Complete)
            {
                complete++;
                finished++;
            }
            else if (RunSession.LogMetStopCondition(slot))
            {
                finished++;
            }
        }

        Diag($"Run: finished with {complete} of {slots.Count} log(s) complete and {finished - complete} worked as far as the run can go");
        if (finished < slots.Count)
        {
            Svc.Chat.Print($"{AhgConstants.LogPrefix} The Hunting Log run ended with {complete} of {slots.Count} log(s) complete. The plugin log has the details.");
            return;
        }

        RunSession.CompletedByStopCondition = true;
        Svc.Chat.Print(complete == slots.Count
            ? $"{AhgConstants.LogPrefix} Hunting Log complete: every log you queued is finished."
            : $"{AhgConstants.LogPrefix} Hunting Log complete as far as the run can go: {complete} of {slots.Count} log(s) finished, and the targets left are inside duties, only known from FATEs or without known spawn points.");
    }

    private void AnnounceRank(string bookName, byte finishedRank, byte openRank)
    {
        Diag($"Run: {bookName} rank {finishedRank + 1} is done; rank {openRank + 1} is open");
        Svc.Chat.Print($"{AhgConstants.LogPrefix} {bookName} rank {finishedRank + 1} is done; moving on to rank {openRank + 1}.");
    }

    private static void AnnounceComplete(string bookName)
        => Svc.Chat.Print($"{AhgConstants.LogPrefix} The {bookName} Hunting Log is complete.");

    private static string LeftOutReason(SpawnCoverage coverage) => coverage switch
    {
        SpawnCoverage.InDuty => "lives inside a duty",
        SpawnCoverage.FateOnly => "is only known to spawn in FATEs",
        _ => "has no known spawn points",
    };

    // Anything but the same rank still in progress ends the wait; the next pass sorts out what it means.
    private static bool RankMoved(byte slot, byte rank)
    {
        HuntingLogReader.Refresh(force: true);
        return HuntingLogReader.Status(slot) != HuntingLogStatus.InProgress || HuntingLogReader.CurrentRank(slot) != rank;
    }

    // The current zone when the log lists it and a search can use it, else the first such listed zone; 0 lets the
    // planner choose among every zone the mob can be found in.
    private static uint TerritoryFor(in HuntingLogTarget target)
    {
        var zones = HuntingLogRegistry.Zones(target);
        uint currentTerritory = Svc.ClientState.TerritoryType;
        uint firstKnown = 0;
        for (var zoneIndex = 0; zoneIndex < zones.Length; zoneIndex++)
        {
            var zone = zones[zoneIndex];
            if (!MobSpawns.TryGetSearchable(target.NameId, zone, out _))
            {
                continue;
            }

            if (zone == currentTerritory)
            {
                return zone;
            }

            if (firstKnown == 0)
            {
                firstKnown = zone;
            }
        }

        return firstKnown;
    }
}
