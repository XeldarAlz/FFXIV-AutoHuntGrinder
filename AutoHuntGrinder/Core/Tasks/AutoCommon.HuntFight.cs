using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Ipc;
using AutoHuntGrinder.Core.Travel;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

public abstract partial class AutoCommon
{
    // Reach is measured from the mark's hitbox edge: melee range with a little slack, and the 25 yalm spell range.
    private const float MarkMeleeReachMeters = 4f;
    private const float MarkRangedReachMeters = 25f;
    private const float MarkMeleeApproachMeters = 3f;
    private const float MarkRangedApproachMeters = 20f;
    private const float MarkRideMinMeters = 40f;
    private const float MarkLandingMeters = 15f;
    // A mark that wanders this far from where a leg was aimed gets a fresh leg toward where it is now.
    private const float MarkDriftMeters = 10f;
    private const float MarkFloorLiftMeters = 3f;
    private const float MarkFloorHalfExtentMeters = 5f;
    private const int MarkTrackIntervalMs = 100;
    private const int MarkApproachWatchdogMs = 40_000;
    private const int MaxMarkApproachLegs = 4;
    private const int MarkFightTickFrames = 15;
    private const int DailyMarkFightBudgetMs = 180_000;
    private const int EliteMarkFightBudgetMs = 420_000;
    // The bill's kill count lands a moment after the mark dies.
    private const int MarkKillSettleMs = 5_000;
    private const int MarkOutOfReachStallMs = 8_000;
    private const int MaxMarkRepositions = 3;
    private const int MarkRepositionWatchdogMs = 30_000;
    private const int MarkHpStallMs = 45_000;
    private const int MaxMarkPresetBounces = 2;
    private const int MarkAggroClearMs = 45_000;
    private const int MaxMarkEngagementsPerVisit = 16;
    private const byte MarkRoleTank = 1;
    private const byte MarkRoleMelee = 2;
    private const string MarkMovementModule = "BossMod.Autorotation.MiscAI.NormalMovement";
    private const string MarkMovementTrack = "Destination";
    private const string MarkMovementParked = "None";

    private bool huntPresetEnsured;
    private bool huntPresetRefusalLogged;

    private enum MarkFight { Reached, Counted, NotCounted, Lost, Unreachable, KnockedOut, Cancelled }

    private async Task<MarkOutcome?> FightVisibleMarks(MarkHuntContext hunt)
    {
        for (var engagement = 1; engagement <= MaxMarkEngagementsPerVisit; engagement++)
        {
            if (CheckMarkState(hunt) is { } stop)
            {
                return stop;
            }

            if (!SightMark(hunt, out var sighting))
            {
                return null;
            }

            var scope = $"mark-fight#{engagement}";
            var fight = await FightMark(hunt, sighting, scope);
            if (hunt.IsHuntMark && EndsHuntMarkSweep(fight))
            {
                return SettleHuntMarkSweep(hunt, sighting.GameObjectId, fight, scope);
            }

            switch (fight)
            {
                case MarkFight.Counted:
                    hunt.UncountedKills = 0;
                    break;
                case MarkFight.NotCounted:
                    hunt.UncountedKills++;
                    break;
                case MarkFight.Unreachable:
                    hunt.Ignore(sighting.GameObjectId);
                    break;
                case MarkFight.KnockedOut:
                    return MarkOutcome.Died;
                case MarkFight.Cancelled:
                    return MarkOutcome.Cancelled;
            }
        }

        return null;
    }

    // A hunt mark is a single spawn. Gone without our kill counting, someone else finished it or it despawned; out of
    // reach or left standing, the rest of the sweep would only find that same one again.
    private MarkOutcome SettleHuntMarkSweep(MarkHuntContext hunt, ulong markId, MarkFight fight, string scope)
    {
        if (CheckMarkState(hunt) is { } stop)
        {
            return stop;
        }

        if (fight == MarkFight.Unreachable)
        {
            hunt.Ignore(markId);
            Diag($"{scope}: {hunt.Name} is up but could not be reached or finished; ending this sweep");
            return MarkOutcome.Unreachable;
        }

        Diag($"{scope}: {hunt.Name} is gone ({fight}) and no kill counted for us; treating it as not found this sweep");
        return MarkOutcome.NotFound;
    }

    private static bool EndsHuntMarkSweep(MarkFight fight) => fight is MarkFight.NotCounted or MarkFight.Lost or MarkFight.Unreachable;

    private async Task<MarkFight> FightMark(MarkHuntContext hunt, MarkSighting sighting, string scope)
    {
        MarkPhase = HuntPhase.Fighting;
        Diag($"{scope}: {hunt.Name} {sighting.DistanceToHitbox:F0}m away at {FormatPosition(sighting.Position)}, {sighting.CurrentHp} hp, fate {sighting.FateId} ({ConditionTag()})");
        var baselineKilled = ReadMarkProgress(hunt, force: true).Killed;
        var approach = await CloseOnMark(hunt, sighting.GameObjectId, scope);
        if (approach != MarkFight.Reached)
        {
            Diag($"{scope}: the approach ended {approach}");
            return approach;
        }

        return await EngageMark(hunt, sighting.GameObjectId, baselineKilled, scope);
    }

    private async Task<MarkFight> CloseOnMark(MarkHuntContext hunt, ulong markId, string scope)
    {
        var approach = ApproachMeters();
        for (var leg = 1; leg <= MaxMarkApproachLegs; leg++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return MarkFight.Cancelled;
            }

            if (IsMarkKnockedOut())
            {
                return MarkFight.KnockedOut;
            }

            if (!TryTrackMark(markId, out var live))
            {
                return MarkFight.Lost;
            }

            var mounted = Svc.Condition[ConditionFlag.Mounted];
            if (live.DistanceToHitbox <= approach || (mounted && live.DistanceToHitbox <= MarkLandingMeters))
            {
                if (mounted)
                {
                    await DismountViaOp($"{scope}-dismount");
                }

                if (live.DistanceToHitbox <= approach)
                {
                    return MarkFight.Reached;
                }

                continue;
            }

            var ride = TerritoryAllowsMount(hunt.TerritoryId) && FreeToMount() && (mounted || live.DistanceToHitbox > MarkRideMinMeters);
            var stopAt = ride ? MarkLandingMeters : approach;
            var destination = MarkFloorNear(live.Position);
            var legScope = $"{scope}-approach#{leg}";
            Diag($"{legScope}: {(ride ? "riding" : "walking")} toward {hunt.Name}, {live.DistanceToHitbox:F0}m out");
            var operation = new MoveOp(move => move.MoveInZone(destination, MovementFor(ride, stopAt), StopWhenMarkWithin(markId, stopAt, destination, hunt.ApproachLabel)));
            await RunCancellable(operation, MarkApproachWatchdogMs, legScope, StuckDetector.MoveStallAbort(legScope));
            if (operation.Fault is { } fault)
            {
                Diag($"{legScope}: faulted: {fault.Message}");
            }
        }

        if (!TryTrackMark(markId, out var last) || last.DistanceToHitbox > approach)
        {
            return MarkFight.Unreachable;
        }

        if (Svc.Condition[ConditionFlag.Mounted])
        {
            await DismountViaOp($"{scope}-dismount");
        }

        return MarkFight.Reached;
    }

    // Keeps the mark targeted and the preset active until the bill counts the kill. A kill only counts once the bill's
    // own number rises, so a copy someone else finished or that despawned is told apart from a real kill.
    private async Task<MarkFight> EngageMark(MarkHuntContext hunt, ulong markId, int baselineKilled, string scope)
    {
        if (hunt.WatchesKills)
        {
            Plugin.Kills.Watch(markId, hunt.NameId);
            Diag($"{scope}: the kill ledger now watches {hunt.Name} ({markId:X})");
        }

        var reach = ReachMeters();
        var deadline = Environment.TickCount64 + hunt.FightBudgetMs;
        var lastHp = uint.MaxValue;
        var hpChangedAt = Environment.TickCount64;
        var bounces = 0;
        var outOfReachSince = 0L;
        var repositions = 0;
        try
        {
            while (true)
            {
                Status = hunt.FightLabel;
                if (CancelToken.IsCancellationRequested)
                {
                    return MarkFight.Cancelled;
                }

                if (IsMarkKnockedOut())
                {
                    return MarkFight.KnockedOut;
                }

                var progress = ReadMarkProgress(hunt, force: true);
                if (KillCounted(progress, baselineKilled))
                {
                    Diag($"{scope}: the kill counted, {progress.Killed}/{progress.Needed} ({DescribeProgress(hunt)})");
                    await ClearMarkAggro(scope);
                    return MarkFight.Counted;
                }

                if (!progress.Tracked)
                {
                    await ClearMarkAggro(scope);
                    return MarkFight.Lost;
                }

                var now = Environment.TickCount64;
                if (now >= deadline)
                {
                    Warn($"{scope}: {hunt.Name} still stands after {hunt.FightBudgetMs / TimeUnits.MillisecondsPerSecond}s; leaving it");
                    return MarkFight.Unreachable;
                }

                if (!TryTrackMark(markId, out var live))
                {
                    return await SettleMarkKill(hunt, baselineKilled, scope);
                }

                if (Svc.Condition[ConditionFlag.Mounted])
                {
                    BossModIPC.Instance.ClearActive();
                    await DismountViaOp($"{scope}-dismount");
                    continue;
                }

                AssertHuntPresetActive();
                if (hunt.FateId != 0)
                {
                    SyncToMarkFate(hunt);
                }

                if (Svc.Targets.Target?.GameObjectId != markId)
                {
                    TargetMark(live, scope);
                }

                if (live.CurrentHp != lastHp)
                {
                    lastHp = live.CurrentHp;
                    hpChangedAt = now;
                    bounces = 0;
                }
                else if (now - hpChangedAt >= MarkHpStallMs)
                {
                    bounces++;
                    if (bounces > MaxMarkPresetBounces)
                    {
                        Diag($"{scope}: {hunt.Name} took no damage through {MaxMarkPresetBounces} preset restarts; leaving it");
                        return MarkFight.Unreachable;
                    }

                    Diag($"{scope}: no damage on {hunt.Name} for {MarkHpStallMs / TimeUnits.MillisecondsPerSecond}s ({ConditionTag()}); restarting the preset ({bounces}/{MaxMarkPresetBounces})");
                    await BounceHuntPreset();
                    hpChangedAt = Environment.TickCount64;
                }

                if (live.DistanceToHitbox <= reach)
                {
                    outOfReachSince = 0;
                }
                else if (outOfReachSince == 0)
                {
                    outOfReachSince = now;
                }
                else if (now - outOfReachSince >= MarkOutOfReachStallMs)
                {
                    repositions++;
                    if (repositions > MaxMarkRepositions)
                    {
                        Diag($"{scope}: {hunt.Name} stayed out of reach through {MaxMarkRepositions} repositions; leaving it");
                        return MarkFight.Unreachable;
                    }

                    await RepositionToMark(hunt, markId, $"{scope}-reposition#{repositions}");
                    outOfReachSince = 0;
                }

                await NextFrame(MarkFightTickFrames);
            }
        }
        finally
        {
            BossModIPC.Instance.ClearActive();
        }
    }

    private async Task<MarkFight> SettleMarkKill(MarkHuntContext hunt, int baselineKilled, string scope)
    {
        var counted = await WaitUntilTimed(
            () => KillCounted(ReadMarkProgress(hunt, force: true), baselineKilled),
            MarkKillSettleMs,
            $"{scope}-count");
        var progress = ReadMarkProgress(hunt, force: true);
        if (counted)
        {
            Diag($"{scope}: {hunt.Name} is down and the kill counted, {progress.Killed}/{progress.Needed}");
            await ClearMarkAggro(scope);
            return MarkFight.Counted;
        }

        Diag($"{scope}: {hunt.Name} is gone and {hunt.SourceName} still reads {progress.Killed}/{progress.Needed}; it did not count");
        return MarkFight.NotCounted;
    }

    // With the target cleared, the preset's targeting takes whatever is still attacking and nothing else.
    private async Task ClearMarkAggro(string scope)
    {
        if (!Svc.Condition[ConditionFlag.InCombat] || IsMarkKnockedOut())
        {
            return;
        }

        Status = "Fighting off what is still attacking";
        Diag($"{scope}: still in combat ({ConditionTag()}); fighting off whatever is attacking");
        var deadline = Environment.TickCount64 + MarkAggroClearMs;
        try
        {
            while (Svc.Condition[ConditionFlag.InCombat] && Environment.TickCount64 < deadline)
            {
                if (CancelToken.IsCancellationRequested || IsMarkKnockedOut())
                {
                    return;
                }

                if (Svc.Condition[ConditionFlag.Mounted])
                {
                    BossModIPC.Instance.ClearActive();
                    await DismountViaOp($"{scope}-aggro-dismount");
                }

                AssertHuntPresetActive();
                if (Svc.Targets.Target is { IsDead: true })
                {
                    Svc.Targets.Target = null;
                }

                await NextFrame(MarkFightTickFrames);
            }
        }
        finally
        {
            BossModIPC.Instance.ClearActive();
        }

        if (Svc.Condition[ConditionFlag.InCombat])
        {
            Diag($"{scope}: still in combat after {MarkAggroClearMs / TimeUnits.MillisecondsPerSecond}s of fighting; carrying on");
        }
    }

    private async Task RepositionToMark(MarkHuntContext hunt, ulong markId, string scope)
    {
        if (!TryTrackMark(markId, out var live))
        {
            return;
        }

        var approach = ApproachMeters();
        var destination = MarkFloorNear(live.Position);
        Diag($"{scope}: {hunt.Name} stayed {live.DistanceToHitbox:F0}m away, out of reach; walking in");
        var parked = ParkHuntPresetMovement();
        try
        {
            var operation = new MoveOp(move => move.MoveInZone(destination, walkMovement.WithTolerance(approach), StopWhenMarkWithin(markId, approach, destination, hunt.FightLabel)));
            await RunCancellable(operation, MarkRepositionWatchdogMs, scope, StuckDetector.MoveStallAbort(scope));
            if (operation.Fault is { } fault)
            {
                Diag($"{scope}: faulted: {fault.Message}");
            }
        }
        finally
        {
            if (parked)
            {
                ResumeHuntPresetMovement();
            }
        }
    }

    // Looked up at most every MarkTrackIntervalMs, because the movement library asks every frame.
    private Func<bool> StopWhenMarkWithin(ulong markId, float meters, Vector3 destination, string label)
    {
        var nextTrackAt = 0L;
        var stop = false;
        return () =>
        {
            Status = label;
            var now = Environment.TickCount64;
            if (now < nextTrackAt)
            {
                return stop;
            }

            nextTrackAt = now + MarkTrackIntervalMs;
            stop = !TryTrackMark(markId, out var live)
                || live.DistanceToHitbox <= meters
                || GroundDistance.Between(live.Position, destination) > MarkDriftMeters;
            return stop;
        };
    }

    private void EnsureHuntCombatPreset()
    {
        if (huntPresetEnsured)
        {
            return;
        }

        var configuration = Plugin.Instance.Configuration;
        var missing = BossModIPC.Instance.GetPreset(HuntCombatPreset.Name) is null;
        var stale = configuration.BundledCombatPresetRevision < HuntCombatPreset.Revision;
        if (!missing && !stale)
        {
            huntPresetEnsured = true;
            return;
        }

        Diag(missing
            ? $"Hunt: creating the '{HuntCombatPreset.Name}' combat preset"
            : $"Hunt: the '{HuntCombatPreset.Name}' preset is at revision {configuration.BundledCombatPresetRevision}, the bundled one is {HuntCombatPreset.Revision}; overwriting it");
        if (!BossModIPC.Instance.CreatePreset(HuntCombatPreset.GetSerialized(), overwrite: true))
        {
            Warn($"Hunt: the combat plugin refused the '{HuntCombatPreset.Name}' preset; the next mark tries again");
            return;
        }

        configuration.BundledCombatPresetRevision = HuntCombatPreset.Revision;
        configuration.Save();
        huntPresetEnsured = true;
    }

    // Re-applied every tick because the combat plugin can drop its active preset whenever combat ends.
    private void AssertHuntPresetActive()
    {
        var bossMod = BossModIPC.Instance;
        if (bossMod.GetActive() == HuntCombatPreset.Name)
        {
            return;
        }

        if (bossMod.SetActive(HuntCombatPreset.Name))
        {
            huntPresetRefusalLogged = false;
            return;
        }

        if (huntPresetRefusalLogged)
        {
            return;
        }

        huntPresetRefusalLogged = true;
        Warn($"Hunt: the combat plugin would not activate the '{HuntCombatPreset.Name}' preset");
    }

    private async Task BounceHuntPreset()
    {
        BossModIPC.Instance.ClearActive();
        await NextFrame(2);
        AssertHuntPresetActive();
    }

    // Hands movement to the pathfinder without dropping the preset, so the rotation keeps attacking on the way in.
    private static bool ParkHuntPresetMovement()
    {
        var bossMod = BossModIPC.Instance;
        if (bossMod.CanClearTransientStrategy
            && bossMod.AddTransientStrategy(HuntCombatPreset.Name, MarkMovementModule, MarkMovementTrack, MarkMovementParked))
        {
            return true;
        }

        bossMod.ClearActive();
        return false;
    }

    private void ResumeHuntPresetMovement()
    {
        if (BossModIPC.Instance.ClearTransientStrategy(HuntCombatPreset.Name, MarkMovementModule, MarkMovementTrack))
        {
            return;
        }

        Diag("Hunt: could not lift the movement override; re-applying the preset instead");
        BossModIPC.Instance.ClearActive();
    }

    private void TargetMark(MarkSighting live, string scope)
    {
        if (MarkFinder.Resolve(live) is not { } mark)
        {
            return;
        }

        Svc.Targets.Target = mark;
        Diag($"{scope}: targeting {mark.Name} {live.DistanceToHitbox:F0}m away");
    }

    private static bool TryTrackMark(ulong markId, out MarkSighting live)
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            live = default;
            return false;
        }

        return MarkFinder.TryGetLive(markId, player.Position, out live);
    }

    private static bool KillCounted(MarkProgress progress, int baselineKilled)
        => progress.Done || (progress.Listed && progress.Killed > baselineKilled);

    private static bool FightsInMelee()
        => Svc.Objects.LocalPlayer?.ClassJob.ValueNullable?.Role is MarkRoleTank or MarkRoleMelee;

    private static float ReachMeters() => FightsInMelee() ? MarkMeleeReachMeters : MarkRangedReachMeters;

    private static float ApproachMeters() => FightsInMelee() ? MarkMeleeApproachMeters : MarkRangedApproachMeters;

    private static Vector3 MarkFloorNear(Vector3 position)
    {
        var navmesh = NavmeshIPC.Instance;
        return navmesh.PointOnFloor(position with { Y = position.Y + MarkFloorLiftMeters }, allowUnlandable: false, MarkFloorHalfExtentMeters)
            ?? navmesh.NearestPointReachable(position, MarkFloorHalfExtentMeters, MarkFloorHalfExtentMeters)
            ?? position;
    }
}
