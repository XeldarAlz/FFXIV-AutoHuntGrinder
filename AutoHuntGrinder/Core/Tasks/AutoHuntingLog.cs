using AutoHuntGrinder.Core.Game.Ops;
using AutoHuntGrinder.Core.HuntingLog;
using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Spawns;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

// Works the queued logs in order, one rank at a time. Each pass hunts what the current rank still needs; a rank that
// fills opens the next, and a log ends once it is complete, nothing left on it can be hunted, or a pass credits nothing.
internal sealed class AutoHuntingLog(IReadOnlyList<byte> slots, AutoHuntSession session, HuntProgress progress) : AutoObjectiveHunt(session, progress)
{
    // Five ranks, each normally one or two passes, with room for targets that stay hidden a while.
    private const int MaxPassesPerBook = 25;
    private const int GearsetCombatClearMs = 30_000;
    // The next rank opens a moment after the last count of the old one lands.
    private const int RankOpenWaitMs = 10_000;
    // Notice keys sit above every objective key, which fits in 17 bits.
    private const uint ForeignCompanyNotice = 0x0100_0000;
    private const uint NoGearsetNotice = 0x0200_0000;
    private const uint GearsetFailedNotice = 0x0300_0000;
    private const uint LevelNotice = 0x0400_0000;
    private const int RankNoticeFactor = 10;

    private readonly IReadOnlyList<byte> slots = slots;
    private readonly List<HuntObjective> objectives = new(HuntingLogRegistry.EntriesPerRank * HuntingLogRegistry.TargetsPerEntry);
    private readonly List<string> leftOutNames = [];

    private enum BookEnd : byte { Complete, Skipped, Stalled, Stopped }

    protected override async Task Execute()
    {
        Plugin.Kills.HuntingLogChanged += OnHuntingLogChanged;
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
            Plugin.Kills.HuntingLogChanged -= OnHuntingLogChanged;
        }
    }

    // The counts are written with the log line, so the reader catches them at once instead of on its next throttle tick.
    private static void OnHuntingLogChanged() => HuntingLogReader.Refresh(force: true);

    private async Task Hunt()
    {
        if (!await PrepareToHunt("Reading your Hunting Log"))
        {
            return;
        }

        for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            var slot = slots[slotIndex];
            var end = await WorkBook(slot);
            Diag($"Run: the {HuntingLogRegistry.BookName(slot)} log (slot {slot}) ended {end}");
            if (end == BookEnd.Stopped)
            {
                return;
            }
        }

        Finish();
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

        for (var pass = 1; pass <= MaxPassesPerBook; pass++)
        {
            if (await WorkPass(slot, bookName, pass) is { } end)
            {
                return end;
            }
        }

        Warn($"Run: the {bookName} log used all {MaxPassesPerBook} passes; moving on");
        return BookEnd.Stalled;
    }

    // Null when the log wants another pass.
    private async Task<BookEnd?> WorkPass(byte slot, string bookName, int pass)
    {
        if (!await EnsureStanding())
        {
            return BookEnd.Stopped;
        }

        HuntingLogReader.Refresh(force: true);
        switch (HuntingLogReader.Status(slot))
        {
            case HuntingLogStatus.Complete:
                AnnounceComplete(bookName);
                return BookEnd.Complete;
            case HuntingLogStatus.Unavailable:
                Warn($"Run: the {bookName} log reads unavailable; skipping it");
                return BookEnd.Skipped;
        }

        var rank = HuntingLogReader.CurrentRank(slot);
        WarnIfUnderLevel(slot, rank, bookName);
        CollectObjectives(slot, rank, bookName);
        if (objectives.Count == 0)
        {
            return await WaitForNextRank(slot, rank, bookName) ? null : BookEnd.Stalled;
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
        HuntingLogReader.Refresh(force: true);
        if (HuntingLogReader.Status(slot) == HuntingLogStatus.Complete)
        {
            AnnounceComplete(bookName);
            return BookEnd.Complete;
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

    // The class cannot change in combat, so a pull still going gets the time to end first.
    private async Task<GearsetSwitchResult> EquipGearset(byte slot)
    {
        Status = $"Changing to {HuntingLogRegistry.BookName(slot)}";
        var result = await GearsetSwitcher.EquipForSlot(slot, CancelToken);
        if (result != GearsetSwitchResult.InCombat)
        {
            return result;
        }

        Status = "Waiting for combat to clear to change class";
        await WaitUntilTimed(static () => !Svc.Condition[ConditionFlag.InCombat], GearsetCombatClearMs, "gearset-combat-clear");
        return CancelToken.IsCancellationRequested ? GearsetSwitchResult.Cancelled : await GearsetSwitcher.EquipForSlot(slot, CancelToken);
    }

    private void CollectObjectives(byte slot, byte rank, string bookName)
    {
        objectives.Clear();
        leftOutNames.Clear();
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
                    continue;
                }

                if (!target.InDuty && MobSpawns.IsSupported(target.NameId))
                {
                    objectives.Add(objective);
                    continue;
                }

                if (NoteLeftOut(objective, target.InDuty ? "lives inside a duty" : "has no known spawn points"))
                {
                    leftOutNames.Add(ObjectiveProgress.Name(objective));
                }
            }
        }

        if (leftOutNames.Count > 0)
        {
            var one = leftOutNames.Count == 1;
            Svc.Chat.Print($"{AhgConstants.LogPrefix} On {bookName} rank {rank + 1}, {string.Join(", ", leftOutNames)} {(one ? "is" : "are")} inside a duty or without known spawn points, so the run leaves {(one ? "it" : "them")} to you.");
        }
    }

    // A rank whose every count is met opens the next; anything else still open on it is beyond the run.
    private async Task<bool> WaitForNextRank(byte slot, byte rank, string bookName)
    {
        var (killed, needed) = HuntingLogReader.RankProgress(slot, rank);
        if (needed == 0 || killed < needed)
        {
            Diag($"Run: nothing the run can hunt is left on {bookName} rank {rank + 1} ({killed}/{needed})");
            return false;
        }

        Status = $"Waiting for {bookName} rank {rank + 2} to open";
        var opened = await WaitUntilTimed(() => RankMoved(slot, rank), RankOpenWaitMs, "hunting-log-rank-open");
        if (!opened && !CancelToken.IsCancellationRequested)
        {
            Warn($"Run: {bookName} rank {rank + 1} reads full, but the next rank did not open within {RankOpenWaitMs / TimeUnits.MillisecondsPerSecond}s");
        }

        return opened;
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
        for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            if (HuntingLogReader.Status(slots[slotIndex]) == HuntingLogStatus.Complete)
            {
                complete++;
            }
        }

        Diag($"Run: finished with {complete} of {slots.Count} log(s) complete");
        if (complete == slots.Count)
        {
            RunSession.CompletedByStopCondition = true;
            Svc.Chat.Print($"{AhgConstants.LogPrefix} Hunting Log complete: every log you queued is finished.");
            return;
        }

        Svc.Chat.Print($"{AhgConstants.LogPrefix} The Hunting Log run ended with {complete} of {slots.Count} log(s) complete. The plugin log has the details.");
    }

    private void AnnounceRank(string bookName, byte finishedRank, byte openRank)
    {
        Diag($"Run: {bookName} rank {finishedRank + 1} is done; rank {openRank + 1} is open");
        Svc.Chat.Print($"{AhgConstants.LogPrefix} {bookName} rank {finishedRank + 1} is done; moving on to rank {openRank + 1}.");
    }

    private static void AnnounceComplete(string bookName)
        => Svc.Chat.Print($"{AhgConstants.LogPrefix} The {bookName} Hunting Log is complete.");

    private static bool RankMoved(byte slot, byte rank)
    {
        HuntingLogReader.Refresh(force: true);
        return HuntingLogReader.Status(slot) == HuntingLogStatus.Complete || HuntingLogReader.CurrentRank(slot) != rank;
    }

    // The current zone when the log lists it and it has points, else the first listed zone with points; 0 lets the hunt
    // choose among every zone the mob is known in.
    private static uint TerritoryFor(in HuntingLogTarget target)
    {
        var zones = HuntingLogRegistry.Zones(target);
        uint currentTerritory = Svc.ClientState.TerritoryType;
        uint firstKnown = 0;
        for (var zoneIndex = 0; zoneIndex < zones.Length; zoneIndex++)
        {
            var zone = zones[zoneIndex];
            if (!MobSpawns.TryGet(target.NameId, zone, out _))
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
