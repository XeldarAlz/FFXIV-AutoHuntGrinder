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
    // FATEs come back on their own timers, and a boss FATE can take a long while to come around again.
    private const int MarkFateWaitBudgetMs = 1_200_000;
    private const int MarkFatePollMs = 1_000;
    private const int MarkFateSyncRetryMs = 5_000;
    private const float MarkFateMinArriveMeters = 5f;
    private const float MarkFateMaxArriveMeters = 20f;
    // Halfway into the ring keeps the character inside the FATE while its mobs load in.
    private const float MarkFateInnerRingShare = 0.5f;

    private readonly record struct MarkFateState(FateState State, Vector3 Location, float Radius, byte Progress);

    private async Task<MarkOutcome> HuntFateMark(MarkHuntContext hunt)
    {
        if (!await EnterMarkTerritory(hunt))
        {
            return CancelToken.IsCancellationRequested ? MarkOutcome.Cancelled : MarkOutcome.Unreachable;
        }

        hunt.StartClock(MarkFateWaitBudgetMs, MarkOutcome.FateMissed);
        var points = hunt.SpawnPoints.Length > 0 ? ResolveMarkPoints(hunt) : [];
        if (points.Length > 0 && !IsMarkFateRunning(hunt.FateId) && !WithinReach(points[0], MarkSweepArriveMeters))
        {
            MarkPhase = HuntPhase.Travelling;
            Diag($"Fate: moving to where {hunt.Fate.Name} starts to wait for it");
            await TravelTo(hunt.TerritoryId, points[0], MarkSweepArriveMeters);
        }

        while (true)
        {
            if (await WaitForMarkFate(hunt) is { } stop)
            {
                return stop;
            }

            if (await FightMarkFate(hunt) is { } outcome)
            {
                return outcome;
            }

            Diag($"Fate: {hunt.Fate.Name} ended before {hunt.Name} counted; waiting for it to come back");
        }
    }

    private async Task<MarkOutcome?> WaitForMarkFate(MarkHuntContext hunt)
    {
        var label = $"Waiting for {hunt.Fate.Name} to start";
        var announced = false;
        while (true)
        {
            if (CheckMarkState(hunt) is { } stop)
            {
                return stop;
            }

            var known = TryReadMarkFate(hunt.FateId, out var fate);
            if (known && fate.State == FateState.Running)
            {
                Diag($"Fate: {hunt.Fate.Name} ({hunt.FateId}) is running at {FormatPosition(fate.Location)}, radius {fate.Radius:F0}m, {fate.Progress}%");
                return null;
            }

            if (!announced)
            {
                announced = true;
                Diag($"Fate: waiting up to {MarkFateWaitBudgetMs / TimeUnits.MillisecondsPerMinute} minutes for {hunt.Fate.Name} ({hunt.FateId}), {(known ? $"now {fate.State}" : "not up")}");
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

        state = new MarkFateState(fate->State, fate->Location, fate->Radius, fate->Progress);
        return true;
    }

    private static unsafe bool IsInMarkFate(uint fateId)
    {
        var manager = CSFateManager.Instance();
        return manager != null && manager->CurrentFate != null && manager->CurrentFate->FateId == fateId;
    }

    private static bool IsMarkFateRunning(uint fateId) => TryReadMarkFate(fateId, out var fate) && fate.State == FateState.Running;
}
