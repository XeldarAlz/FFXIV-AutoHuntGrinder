using AutoHuntGrinder.Core.Hunts;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Numerics;
using System.Threading.Tasks;
using CSFateManager = FFXIVClientStructs.FFXIV.Client.Game.Fate.FateManager;

namespace AutoHuntGrinder.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int MarkFatePollMs = 1_000;
    // A FATE about to start is worth staying for past the end of a visit.
    private const int MarkFatePreparingGraceMs = 60_000;
    private const int MarkFateSyncRetryMs = 5_000;
    private const float MarkFateMinArriveMeters = 5f;
    private const float MarkFateMaxArriveMeters = 20f;
    // Halfway into the ring keeps the character inside the FATE while its mobs load in.
    private const float MarkFateInnerRingShare = 0.5f;

    private readonly record struct MarkFateState(FateState State, Vector3 Location, float Radius, byte Progress, int StartTimeEpoch, short Duration);

    private async Task<MarkOutcome> HuntFateMark(MarkHuntContext hunt, MarkFateWait wait)
    {
        if (!await EnterMarkTerritory(hunt))
        {
            return CancelToken.IsCancellationRequested ? MarkOutcome.Cancelled : MarkOutcome.Unreachable;
        }

        while (true)
        {
            if (await WaitForMarkFate(hunt, wait) is { } stop)
            {
                return stop;
            }

            if (await FightMarkFate(hunt) is { } outcome)
            {
                return outcome;
            }

            if (wait.Rotates)
            {
                Diag($"Fate: {hunt.Fate.Name} ended before {hunt.Name} counted; looking in again later");
                return wait.Spent ? MarkOutcome.FateMissed : MarkOutcome.FateNotUp;
            }

            Diag($"Fate: {hunt.Fate.Name} ended before {hunt.Name} counted; waiting for it to come back");
            wait.PlanWaitOut();
        }
    }

    // The visit's clock ends it FateNotUp while budget is left for another visit, and FateMissed once none is.
    private async Task<MarkOutcome?> WaitForMarkFate(MarkHuntContext hunt, MarkFateWait wait)
    {
        var visitMs = Math.Min(wait.VisitMs, wait.RemainingMs);
        hunt.StartClock(visitMs, visitMs >= wait.RemainingMs ? MarkOutcome.FateMissed : MarkOutcome.FateNotUp);
        var startedAt = Environment.TickCount64;
        try
        {
            return await WatchMarkFate(hunt, wait.Dwells, visitMs);
        }
        finally
        {
            wait.Spend(Environment.TickCount64 - startedAt);
        }
    }

    // The FATE list covers the whole zone, so a look-in reads it where the character lands; only a dwell walks to where
    // the FATE starts. Once the FATE runs the clock stops, because the FATE's own end bounds the fight.
    private async Task<MarkOutcome?> WatchMarkFate(MarkHuntContext hunt, bool dwells, int visitMs)
    {
        var label = dwells ? $"Waiting for {hunt.Fate.Name} to start" : $"Looking in on {hunt.Fate.Name}";
        var announced = false;
        var atStart = !dwells;
        while (true)
        {
            if (CheckMarkState(hunt) is { } stop)
            {
                return stop;
            }

            var known = TryReadMarkFate(hunt.FateId, out var fate);
            if (known && fate.State == FateState.Running)
            {
                var secondsLeft = fate.StartTimeEpoch + fate.Duration - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                Diag($"Fate: {hunt.Fate.Name} ({hunt.FateId}) is running at {FormatPosition(fate.Location)}, radius {fate.Radius:F0}m, {fate.Progress}%, {secondsLeft}s left of {fate.Duration}s");
                hunt.StopClock();
                return null;
            }

            if (known && fate.State == FateState.Preparing)
            {
                hunt.ExtendClock(MarkFatePreparingGraceMs);
            }

            if (!announced)
            {
                announced = true;
                Diag($"Fate: {(dwells ? "waiting" : "looking in")} up to {visitMs / TimeUnits.MillisecondsPerSecond}s for {hunt.Fate.Name} ({hunt.FateId}), {(known ? $"now {fate.State}" : "not up")}");
            }

            if (!atStart)
            {
                atStart = true;
                await MoveToMarkFateStart(hunt);
                continue;
            }

            MarkPhase = HuntPhase.Searching;
            Status = label;
            if (Svc.Condition[ConditionFlag.InCombat])
            {
                await FightOffAttackers("fate-wait");
            }

            await DelayMs(MarkFatePollMs);
        }
    }

    private async Task MoveToMarkFateStart(MarkHuntContext hunt)
    {
        var points = hunt.SpawnPoints.Length > 0 ? ResolveMarkPoints(hunt) : [];
        if (points.Length == 0 || WithinReach(points[0], MarkSweepArriveMeters))
        {
            return;
        }

        MarkPhase = HuntPhase.Travelling;
        Diag($"Fate: moving to where {hunt.Fate.Name} starts to wait for it");
        await TravelTo(hunt.TerritoryId, points[0], MarkSweepArriveMeters);
    }

    private async Task<MarkOutcome?> FightMarkFate(MarkHuntContext hunt)
    {
        var label = $"Looking for {hunt.Name} in {hunt.Fate.Name}";
        while (TryReadMarkFate(hunt.FateId, out var fate) && fate.State == FateState.Running)
        {
            if (CheckMarkState(hunt) is { } stop)
            {
                return stop;
            }

            var arriveWithin = Math.Clamp(fate.Radius * MarkFateInnerRingShare, MarkFateMinArriveMeters, MarkFateMaxArriveMeters);
            if (!IsInMarkFate(hunt.FateId) && !WithinReach(fate.Location, arriveWithin))
            {
                MarkPhase = HuntPhase.Travelling;
                Diag($"Fate: heading into {hunt.Fate.Name}, {DistanceTo(fate.Location):F0}m away");
                if (!await TravelTo(hunt.TerritoryId, fate.Location, arriveWithin))
                {
                    if (CancelToken.IsCancellationRequested)
                    {
                        return MarkOutcome.Cancelled;
                    }

                    Diag($"Fate: could not get into {hunt.Fate.Name} this time; trying again");
                    await DelayMs(MarkFatePollMs);
                    continue;
                }
            }

            SyncToMarkFate(hunt);
            if (await FightVisibleMarks(hunt) is { } fought)
            {
                return fought;
            }

            if (Svc.Condition[ConditionFlag.InCombat])
            {
                await FightOffAttackers("fate-fight");
            }

            MarkPhase = HuntPhase.Searching;
            Status = label;
            await DelayMs(MarkFatePollMs);
        }

        return null;
    }

    // An over-level character earns nothing from a FATE until it syncs, and the combat plugin leaves the mobs of a FATE
    // the character is not synced to alone.
    private unsafe void SyncToMarkFate(MarkHuntContext hunt)
    {
        var now = Environment.TickCount64;
        if (now < hunt.NextSyncAttemptAt)
        {
            return;
        }

        var manager = CSFateManager.Instance();
        if (manager == null || manager->CurrentFate == null || manager->CurrentFate->FateId != hunt.FateId || manager->SyncedFateId == hunt.FateId)
        {
            return;
        }

        var level = Svc.Objects.LocalPlayer?.Level ?? 0;
        var maxLevel = hunt.Fate.MaxLevel != 0 ? hunt.Fate.MaxLevel : manager->CurrentFate->MaxLevel;
        if (level <= maxLevel)
        {
            return;
        }

        hunt.NextSyncAttemptAt = now + MarkFateSyncRetryMs;
        Diag($"Fate: syncing from level {level} down to {hunt.Fate.Name}'s level {maxLevel}");
        manager->LevelSync();
    }

    private protected static bool IsMarkFateUp(uint fateId)
        => TryReadMarkFate(fateId, out var fate) && fate.State is FateState.Running or FateState.Preparing;

    private static unsafe bool TryReadMarkFate(uint fateId, out MarkFateState state)
    {
        state = default;
        var manager = CSFateManager.Instance();
        if (manager == null)
        {
            return false;
        }

        var fate = manager->GetFateById((ushort)fateId);
        if (fate == null)
        {
            return false;
        }

        state = new MarkFateState(fate->State, fate->Location, fate->Radius, fate->Progress, fate->StartTimeEpoch, fate->Duration);
        return true;
    }

    private static unsafe bool IsInMarkFate(uint fateId)
    {
        var manager = CSFateManager.Instance();
        return manager != null && manager->CurrentFate != null && manager->CurrentFate->FateId == fateId;
    }
}
