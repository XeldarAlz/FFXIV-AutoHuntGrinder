using AutoHuntGrinder.Core.Game.Player;
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
    // A flight touches down this far outside the mark's hitbox, on the side it came from, instead of at the mark's
    // feet: a few steps short of melee range, and inside casting range without standing in the mark's reach.
    private const float MarkMeleeLandingGapMeters = 6f;
    private const float MarkRangedLandingGapMeters = 10f;
    // A mark that wanders this far from where a leg was aimed gets a fresh leg toward where it is now.
    private const float MarkDriftMeters = 10f;
    private const float MarkFloorLiftMeters = 3f;
    private const float MarkFloorHalfExtentMeters = 5f;
    // A leg stops this far inside the mark's reach, so rounding at the edge cannot leave it a hair outside.
    private const float MarkLegInsetMeters = 0.5f;
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

    private enum MarkFight { Reached, Counted, NotCounted, Lost, Unreachable, Relocated, KnockedOut, Cancelled }

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
            if (hunt.IsHuntMark && EndsHuntMarkSweep(hunt, fight))
            {
                return await SettleHuntMarkSweep(hunt, sighting.GameObjectId, fight, scope);
            }

            switch (fight)
            {
                case MarkFight.Counted:
                    hunt.UncountedKills = 0;
                    break;
                // A hunt mark someone else finished credits nobody who never fought it; that is no failed credit.
                case MarkFight.NotCounted when !hunt.IsHuntMark:
                    hunt.UncountedKills++;
                    break;
                case MarkFight.Unreachable:
                    hunt.Ignore(sighting.GameObjectId);
                    break;
                // A teleport out from under the world moved the character; the sweep looks again from where it landed.
                case MarkFight.Relocated:
                    return null;
                case MarkFight.KnockedOut:
                    return MarkOutcome.Died;
                case MarkFight.Cancelled:
                    return MarkOutcome.Cancelled;
            }
        }

        return null;
    }

    // A mark left standing still hits back, so whatever attacks is fought off here, where a knockout is charged to this
    // mark and not to the next target. A fight that ran past the search clock was with a mark that was up, so the clock
    // is not read: an unreachable mark stays unreachable, and only a gone one reads as not found.
    private async Task<MarkOutcome> SettleHuntMarkSweep(MarkHuntContext hunt, ulong markId, MarkFight fight, string scope)
    {
        if (fight == MarkFight.Unreachable)
        {
            hunt.Ignore(markId);
            if (Svc.Targets.Target?.GameObjectId == markId)
            {
                Svc.Targets.Target = null;
            }
        }

        await FightOffAttackers(scope);
        if (CheckMarkStanding(hunt) is { } stop)
        {
            return stop;
        }

        if (fight == MarkFight.Unreachable)
        {
            Diag($"{scope}: {hunt.Name} is up but could not be reached or finished; ending this sweep");
            return MarkOutcome.Unreachable;
        }

        Diag($"{scope}: {hunt.Name} is gone ({fight}) and no kill counted for us; treating it as not found this sweep");
        return MarkOutcome.NotFound;
    }

    // A mark out of reach or left standing would only be found again. One gone without our kill ends the sweep unless it
    // comes back within it.
    private static bool EndsHuntMarkSweep(MarkHuntContext hunt, MarkFight fight) => fight switch
    {
        MarkFight.Unreachable => true,
        MarkFight.NotCounted or MarkFight.Lost => !hunt.RespawnsWithinSearch,
        _ => false,
    };

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
        var mountingAllowed = TerritoryAllowsMount(hunt.TerritoryId);
        var legs = 0;
        var falseStarts = 0;
        var groundLegFailed = false;
        while (legs < MaxMarkApproachLegs)
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

            var destination = MarkFloorNear(live.Position);
            if (await RecoverIfOffMesh(hunt.TerritoryId, destination, scope))
            {
                return MarkFight.Relocated;
            }

            var mounted = Svc.Condition[ConditionFlag.Mounted];
            if (live.DistanceToHitbox <= approach || (mounted && live.DistanceToHitbox <= MarkLandingMeters))
            {
                if (mounted && !await LandAndDismount(destination, LandingStandOffMeters(live), $"{scope}-dismount"))
                {
                    return MarkFight.Unreachable;
                }

                if (TryTrackMark(markId, out live) && live.DistanceToHitbox <= approach)
                {
                    return MarkFight.Reached;
                }

                groundLegFailed = false;
                legs++;
                continue;
            }

            var legScope = $"{scope}-approach#{legs + 1}";
            var plan = await PlanLeg(hunt.TerritoryId, destination, approach, MarkRideMinMeters, mountingAllowed, groundLegFailed, legScope);
            if (CancelToken.IsCancellationRequested)
            {
                return MarkFight.Cancelled;
            }

            if (!plan.Reachable)
            {
                Diag($"{legScope}: the nearest floor the ground reaches is {plan.ShortfallMeters:F0}m short of {hunt.Name}; leaving this one");
                return MarkFight.Unreachable;
            }

            var stopAt = plan.Rides ? MarkLandingMeters : approach;
            var legMovement = MovementFor(plan.Mode, MarkLegTolerance(plan.Target, live, stopAt));
            Diag($"{legScope}: going {plan.Mode} toward {hunt.Name}, {live.DistanceToHitbox:F0}m out");
            var startedAt = Environment.TickCount64;
            var operation = new MoveOp(move => move.MoveInZone(plan.Target, legMovement, StopWhenMarkWithin(markId, stopAt, destination, hunt.ApproachLabel)));
            var completed = await RunCancellable(operation, MarkApproachWatchdogMs, legScope, StuckDetector.MoveStallAbort(legScope));
            if (operation.Fault is { } fault)
            {
                Diag($"{legScope}: faulted: {fault.Message}");
            }

            if (falseStarts < MaxFalseStarts && WasFalseStart(completed, operation, startedAt, markId, stopAt, destination))
            {
                falseStarts++;
                await RecoverFromFalseStart(legScope, falseStarts);
                continue;
            }

            groundLegFailed |= plan.Mode != LegMode.Flight && EndedShortOfStandingMark(markId, stopAt, destination);
            legs++;
        }

        if (!TryTrackMark(markId, out var last) || last.DistanceToHitbox > approach)
        {
            return MarkFight.Unreachable;
        }

        if (Svc.Condition[ConditionFlag.Mounted] && !await LandAndDismount(MarkFloorNear(last.Position), LandingStandOffMeters(last), $"{scope}-dismount"))
        {
            return MarkFight.Unreachable;
        }

        return MarkFight.Reached;
    }

    // A leg that ended at once, with the mark still where it was aimed and still out of reach, never moved the character.
    private static bool WasFalseStart(bool completed, MoveOp operation, long startedAt, ulong markId, float stopAt, Vector3 destination)
    {
        if (!completed || operation.Fault is not null || Environment.TickCount64 - startedAt >= StuckDetector.FalseStartMs)
        {
            return false;
        }

        return EndedShortOfStandingMark(markId, stopAt, destination);
    }

    // A leg also ends when the mark wanders off from where it was aimed; that is a fresh leg, not a failed one.
    private static bool EndedShortOfStandingMark(ulong markId, float stopAt, Vector3 destination)
        => TryTrackMark(markId, out var live)
        && live.DistanceToHitbox > stopAt
        && GroundDistance.Between(live.Position, destination) <= MarkDriftMeters;

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
        // The fight is the combat plugin's to move in; the hold comes back the moment it is over.
        ReleaseCombatMovement(scope);
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
                    await FightOffAttackers(scope);
                    return MarkFight.Counted;
                }

                if (!progress.Tracked)
                {
                    await FightOffAttackers(scope);
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
                    await SafeDismount($"{scope}-dismount");
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
            HoldCombatMovement(scope);
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
            await FightOffAttackers(scope);
            return MarkFight.Counted;
        }

        Diag($"{scope}: {hunt.Name} is gone and {hunt.SourceName} still reads {progress.Killed}/{progress.Needed}; it did not count");
        return MarkFight.NotCounted;
    }

    // Fights back with the preset until the character is out of combat. Every step that needs the character free of
    // combat runs this first: a mob that has aggroed keeps attacking a character that stands still, so waiting never ends
    // it. The preset's targeting only switches while the character has no target, and only to a mob on the enemy list, so
    // a target that is dead or holds no enmity is dropped to let it take an attacker.
    private protected async Task FightOffAttackers(string scope)
    {
        if (!Svc.Condition[ConditionFlag.InCombat] || IsMarkKnockedOut())
        {
            return;
        }

        EnsureHuntCombatPreset();
        Status = "Fighting off what is still attacking";
        Diag($"{scope}: still in combat ({ConditionTag()}); fighting off whatever is attacking");
        var deadline = Environment.TickCount64 + MarkAggroClearMs;
        ReleaseCombatMovement(scope);
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
                    await SafeDismount($"{scope}-aggro-dismount");
                }

                AssertHuntPresetActive();
                if (Svc.Targets.Target is { } target && (target.IsDead || !EnmityList.Contains(target.EntityId)))
                {
                    Svc.Targets.Target = null;
                }

                await NextFrame(MarkFightTickFrames);
            }
        }
        finally
        {
            BossModIPC.Instance.ClearActive();
            HoldCombatMovement(scope);
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
        HoldCombatMovement(scope);
        try
        {
            var legMovement = walkMovement.WithTolerance(MarkLegTolerance(destination, live, approach));
            var operation = new MoveOp(move => move.MoveInZone(destination, legMovement, StopWhenMarkWithin(markId, approach, destination, hunt.FightLabel)));
            await RunCancellable(operation, MarkRepositionWatchdogMs, scope, StuckDetector.MoveStallAbort(scope));
            if (operation.Fault is { } fault)
            {
                Diag($"{scope}: faulted: {fault.Message}");
            }
        }
        finally
        {
            ReleaseCombatMovement(scope);
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

    private static float LandingStandOffMeters(in MarkSighting mark)
        => mark.HitboxRadius + (FightsInMelee() ? MarkMeleeLandingGapMeters : MarkRangedLandingGapMeters);

    // The floor nearest the mark is the floor it stands on. The floor query answers with the highest floor anywhere in
    // its column, which on a slope is the uphill neighbour metres to the side, so it only serves a mark hovering clear
    // of every floor.
    private static Vector3 MarkFloorNear(Vector3 position)
    {
        var navmesh = NavmeshIPC.Instance;
        return navmesh.NearestStandablePoint(position, MarkFloorHalfExtentMeters, MarkFloorHalfExtentMeters)
            ?? navmesh.PointOnFloor(position with { Y = position.Y + MarkFloorLiftMeters }, allowUnlandable: false, MarkFloorHalfExtentMeters)
            ?? position;
    }

    // The pathfinder calls a leg arrived by its distance to the leg's goal, and the fight calls a mark reached by its
    // distance to the mark's hitbox. The goal is floor near the mark, not the mark, so the leg gets what is left of the
    // reach once the gap between the two is taken out, and arriving always means reaching.
    private static float MarkLegTolerance(Vector3 legGoal, in MarkSighting mark, float reachMeters)
        => MathF.Max(MinLegToleranceMeters, reachMeters + mark.HitboxRadius - Vector3.Distance(legGoal, mark.Position) - MarkLegInsetMeters);
}
