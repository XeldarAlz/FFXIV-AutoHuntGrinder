using AutoHuntGrinder.Core.Ipc;
using AutoHuntGrinder.Core.Travel;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int TravelTeleportWatchdogMs = 60_000;
    private const int TravelNavmeshWaitMs = 60_000;
    private const int TravelNavmeshPollFrames = 60;
    private const int TravelPlayerReadyWaitMs = 30_000;
    private const int TravelBaseBudgetMs = 60_000;
    private const int TravelMaxBudgetMs = 900_000;
    // The slowest plausible pace, on foot and around obstacles; it sizes a trip's time budget from its straight-line length.
    private const float TravelMinSpeedMetersPerSecond = 3f;
    private const int TravelProgressLogMs = 15_000;
    private const int MaxZoneEntries = 2;
    private const int MaxTravelStalls = 5;
    private const int StallsBeforeUnstick = 2;
    private const int StallsBeforeTeleportRecovery = 3;
    private const int MaxFalseStarts = 3;
    private const int MaxLandmassTeleports = 2;
    private const int FalseStartSettleMs = 1_500;
    private const int StillnessConfirmMs = 300;
    private const int StillnessPollMs = 100;
    private const float StillnessMeters = 0.2f;
    // The saving at which the movement library itself calls a teleport faster than flying.
    private const float TeleportShortcutMinSavingMeters = 300f;
    // A recovery teleport to an aetheryte this close would land right where the character got stuck.
    private const float TeleportRecoveryMinHopMeters = 50f;
    private const float TeleportMovedMeters = 3f;
    // Below this, summoning a mount costs more time than the walk.
    private const float MountMinMeters = 30f;
    private const float SnapLiftMeters = 3f;
    private const float SnapHalfExtentMeters = 5f;
    // A floor further down than this means the destination is in the air, and snapping would drop it to the ground.
    private const float SnapMaxDropMeters = 10f;
    private const float SnapReportMeters = 1f;
    // General action 2 is Jump, which clears the low ledges and fence posts a stuck walk usually catches on.
    private const uint JumpGeneralActionId = 2;
    private const int UnstickSettleMs = 1_000;
    private const float UnstickStepHalfExtentMeters = 6f;
    private const float UnstickStepMinMeters = 1.5f;
    private const float UnstickStepToleranceMeters = 0.5f;
    private const int UnstickStepWatchdogMs = 8_000;
    private const float UnstickSideStepMeters = 4f;
    private const float UnstickSideStepHalfExtentMeters = 3f;
    private const float AetheryteFloorHalfExtentMeters = 10f;

    private bool outsideMovementWarned;

    private enum ZoneTravelResult { Arrived, Failed, LeftZone }

    private enum LegOutcome { EndedShort, Stalled, Faulted, MountFailed, Remount, FalseStart }

    private enum TeleportPurpose { Shortcut, Recovery, OffMesh }

    protected async Task<bool> TravelTo(uint territoryId, Vector3 destination, float arriveWithin, bool groundStalled = false)
    {
        var zoneName = TerritoryNames.Of(territoryId);
        Diag($"Travel: to {zoneName} ({territoryId}) at {FormatPosition(destination)} within {arriveWithin:F1}m, starting in territory {Svc.ClientState.TerritoryType} ({ConditionTag()})");
        for (var entry = 1; entry <= MaxZoneEntries; entry++)
        {
            if (!await WaitForPlayerReady())
            {
                return false;
            }

            if (Svc.ClientState.TerritoryType != territoryId && !await EnterTerritory(territoryId, destination, zoneName))
            {
                return false;
            }

            var result = await TravelWithinZone(territoryId, destination, arriveWithin, zoneName, groundStalled);
            if (result != ZoneTravelResult.LeftZone)
            {
                return result == ZoneTravelResult.Arrived;
            }

            Diag($"Travel: left {zoneName} on the way (now in territory {Svc.ClientState.TerritoryType}); heading back in");
        }

        Warn($"Travel: kept leaving {zoneName} on the way; giving up");
        return false;
    }

    private async Task<bool> WaitForPlayerReady()
    {
        if (CancelToken.IsCancellationRequested)
        {
            return false;
        }

        if (PlayerReady())
        {
            return true;
        }

        Status = "Waiting for the character to be ready";
        var ready = await WaitUntilTimed(PlayerReady, TravelPlayerReadyWaitMs, "travel-player-ready");
        if (!ready && !CancelToken.IsCancellationRequested)
        {
            Warn("Travel: the character never became ready to move");
        }

        return ready;
    }

    private async Task<bool> EnterTerritory(uint territoryId, Vector3 destination, string zoneName)
    {
        Diag($"Travel: in territory {Svc.ClientState.TerritoryType}, teleporting toward {zoneName}");
        var reached = false;
        await RunWithStatusPinned(
            $"Teleporting to {zoneName}",
            async () => reached = await TeleportToTerritory(territoryId, destination, "travel-teleport", TravelTeleportWatchdogMs));
        if (reached)
        {
            return true;
        }

        if (!CancelToken.IsCancellationRequested)
        {
            Warn($"Travel: could not reach {zoneName} (still in territory {Svc.ClientState.TerritoryType})");
        }

        return false;
    }

    private async Task<ZoneTravelResult> TravelWithinZone(uint territoryId, Vector3 destination, float arriveWithin, string zoneName, bool groundStalled)
    {
        await WaitForNavmeshReady(TravelNavmeshWaitMs, TravelNavmeshPollFrames);
        if (CancelToken.IsCancellationRequested)
        {
            return ZoneTravelResult.Failed;
        }

        var target = SnapToFloor(destination);
        if (!Arrived(target, destination, arriveWithin))
        {
            await TryTeleportShortcut(territoryId, target, "travel-shortcut", TeleportPurpose.Shortcut);
            await TryAethernetShortcut(territoryId, target);
        }

        var mountingAllowed = TerritoryAllowsMount(territoryId);
        var budgetMs = TravelBudgetMs(target);
        var deadline = Environment.TickCount64 + budgetMs;
        var stalls = 0;
        var falseStarts = 0;
        var landmassTeleports = 0;
        var teleportRecoveryUsed = false;
        for (var leg = 1; ; leg++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return ZoneTravelResult.Failed;
            }

            if (Svc.ClientState.TerritoryType != territoryId)
            {
                return ZoneTravelResult.LeftZone;
            }

            if (Arrived(target, destination, arriveWithin))
            {
                Diag($"Travel: arrived in {zoneName}, {DistanceTo(destination):F1}m from the spot ({ConditionTag()})");
                return ZoneTravelResult.Arrived;
            }

            var remainingMs = deadline - Environment.TickCount64;
            if (remainingMs <= 0)
            {
                Warn($"Travel: ran out of time ({budgetMs / TimeUnits.MillisecondsPerSecond}s) {DistanceTo(target):F0}m short of the spot in {zoneName}");
                return ZoneTravelResult.Failed;
            }

            var scope = $"travel-leg#{leg}";
            var plan = await PlanLeg(territoryId, target, arriveWithin, MountMinMeters, mountingAllowed, groundStalled || stalls > 0, scope);
            if (CancelToken.IsCancellationRequested)
            {
                return ZoneTravelResult.Failed;
            }

            if (!plan.Reachable)
            {
                if (landmassTeleports < MaxLandmassTeleports)
                {
                    landmassTeleports++;
                    if (await TryTeleportToConnectedAetheryte(territoryId, target, arriveWithin, scope))
                    {
                        continue;
                    }
                }

                Warn($"Travel: no ground route reaches the spot in {zoneName} from here or from an attuned aetheryte (the nearest floor is {plan.ShortfallMeters:F0}m short of it), and flying is not available; giving up");
                return ZoneTravelResult.Failed;
            }

            var outcome = await RunTravelLeg(plan, target, arriveWithin, mountingAllowed, (int)remainingMs, scope, zoneName);
            if (outcome == LegOutcome.Remount || CancelToken.IsCancellationRequested || Arrived(target, destination, arriveWithin))
            {
                continue;
            }

            if (outcome == LegOutcome.MountFailed)
            {
                mountingAllowed = false;
                Diag($"{scope}: could not mount here; walking the rest of the way");
            }

            if (outcome == LegOutcome.FalseStart && falseStarts < MaxFalseStarts)
            {
                falseStarts++;
                await RecoverFromFalseStart(scope, falseStarts);
                continue;
            }

            NavmeshIPC.Instance.Stop();
            if (await RecoverIfOffMesh(territoryId, target, scope))
            {
                continue;
            }

            stalls++;
            if (stalls >= MaxTravelStalls)
            {
                Warn($"Travel: stuck {stalls} times {DistanceTo(target):F0}m short of the spot in {zoneName}; giving up");
                return ZoneTravelResult.Failed;
            }

            if (stalls >= StallsBeforeTeleportRecovery && !teleportRecoveryUsed)
            {
                teleportRecoveryUsed = true;
                if (await TryTeleportShortcut(territoryId, target, $"{scope}-recovery", TeleportPurpose.Recovery))
                {
                    continue;
                }
            }

            if (stalls >= StallsBeforeUnstick)
            {
                await Unstick(scope, target, stalls);
            }
            else
            {
                Diag($"{scope}: re-pathing from here");
            }
        }
    }

    private async Task<LegOutcome> RunTravelLeg(LegPlan plan, Vector3 target, float arriveWithin, bool mountingAllowed, int watchdogMs, string scope, string zoneName)
    {
        var ride = plan.Rides;
        var canRemount = mountingAllowed && !ride;
        var label = ride ? $"Riding to the spot in {zoneName}" : $"Walking to the spot in {zoneName}";
        var remount = false;

        bool StopCondition()
        {
            Status = label;
            if (WithinReach(target, arriveWithin))
            {
                return true;
            }

            if (!canRemount || !FreeToMount() || Svc.Condition[ConditionFlag.Mounted] || DistanceTo(target) <= MountMinMeters)
            {
                return false;
            }

            remount = true;
            return true;
        }

        var tracker = new MoveStallTracker();
        var nextProgressLogAt = Environment.TickCount64 + TravelProgressLogMs;
        bool AbortIfStalled()
        {
            if (Environment.TickCount64 >= nextProgressLogAt)
            {
                nextProgressLogAt = Environment.TickCount64 + TravelProgressLogMs;
                Diag($"{scope}: {DistanceTo(target):F0}m to go, navigating {NavmeshIPC.Instance.IsRunning()}, {ConditionTag()}");
            }

            var kind = tracker.Check();
            if (kind == StallKind.None)
            {
                return false;
            }

            Diag($"{scope}: stalled ({kind}) {DistanceTo(target):F0}m short of the spot");
            return true;
        }

        Diag($"{scope}: going {plan.Mode}, {DistanceTo(target):F0}m to go");
        var startedAt = Environment.TickCount64;
        var operation = new MoveOp(move => move.MoveInZone(plan.Target, MovementFor(plan.Mode, plan.ToleranceFor(arriveWithin)), StopCondition));
        var completed = await RunCancellable(operation, watchdogMs, scope, AbortIfStalled);
        if (remount)
        {
            Diag($"{scope}: free to mount with {DistanceTo(target):F0}m to go; stopping to mount");
            return LegOutcome.Remount;
        }

        if (operation.Fault is { } fault)
        {
            Diag($"{scope}: faulted: {fault.Message}");
        }

        var failed = operation.Fault is not null || !completed;
        if (ride && failed && !Svc.Condition[ConditionFlag.Mounted])
        {
            return LegOutcome.MountFailed;
        }

        if (operation.Fault is not null)
        {
            return LegOutcome.Faulted;
        }

        if (!completed)
        {
            return LegOutcome.Stalled;
        }

        return Environment.TickCount64 - startedAt < StuckDetector.FalseStartMs ? LegOutcome.FalseStart : LegOutcome.EndedShort;
    }

    // Something else had the character: the game's own descent after a dismount in the air, the combat plugin's
    // automatic movement, a held key or auto-run. The pathfinder drops a path the moment it sees movement input it did
    // not produce. A jump ends a descent; anything else has to stop on its own before the next path stands a chance.
    private async Task RecoverFromFalseStart(string scope, int falseStarts)
    {
        Diag($"{scope}: the path was dropped within {StuckDetector.FalseStartMs}ms of starting ({falseStarts}/{MaxFalseStarts}; {DescribeOutsideMovement()})");
        CancelDescent();
        if (await WaitForStillness(FalseStartSettleMs) || outsideMovementWarned || CancelToken.IsCancellationRequested)
        {
            return;
        }

        outsideMovementWarned = true;
        Warn($"{scope}: the character keeps moving on its own, so every path is dropped as soon as it starts. If the pathfinder's 'Cancel current path on player movement input' setting is on, turn it off; a held movement key, auto-run or a drifting controller does the same.");
        Svc.Chat.PrintError($"{AhgConstants.LogPrefix} The character keeps moving on its own, so paths are dropped as soon as they start. Turn off the pathfinder's 'cancel path on movement input' setting, and check that no key or auto-run is held.");
    }

    private async Task<bool> WaitForStillness(int budgetMs)
    {
        var deadline = Environment.TickCount64 + budgetMs;
        Vector3? anchor = null;
        var stillSinceMs = Environment.TickCount64;
        while (Environment.TickCount64 < deadline && !CancelToken.IsCancellationRequested)
        {
            if (Svc.Objects.LocalPlayer is { } player)
            {
                var now = Environment.TickCount64;
                var position = player.Position;
                if (anchor is null || Vector3.Distance(anchor.Value, position) > StillnessMeters)
                {
                    anchor = position;
                    stillSinceMs = now;
                }
                else if (now - stillSinceMs >= StillnessConfirmMs)
                {
                    return true;
                }
            }

            await DelayMs(StillnessPollMs);
        }

        return false;
    }

    // Under the world's surface the pathfinder still plans routes and the character still moves, so no stall ever
    // fires; a teleport is the one way back up. True once the character stands somewhere else.
    private async Task<bool> RecoverIfOffMesh(uint territoryId, Vector3 target, string scope)
    {
        if (!StuckDetector.IsOffMesh())
        {
            return false;
        }

        await DelayMs(StuckDetector.OffMeshConfirmMs);
        if (CancelToken.IsCancellationRequested || !StuckDetector.IsOffMesh())
        {
            return false;
        }

        var here = Svc.Objects.LocalPlayer?.Position ?? Vector3.Zero;
        Warn($"{scope}: no floor of the world within {StuckDetector.OffMeshProbeMeters:F0}m of {FormatPosition(here)} ({ConditionTag()}); the character is off the world's surface, teleporting out");
        return await TryTeleportShortcut(territoryId, target, $"{scope}-offmesh", TeleportPurpose.OffMesh);
    }

    private async Task<bool> TryTeleportShortcut(uint territoryId, Vector3 target, string label, TeleportPurpose purpose)
    {
        if (Svc.Condition[ConditionFlag.InCombat] || Svc.Objects.LocalPlayer is not { } player)
        {
            return false;
        }

        if (!ZoneAetherytes.TryFindNearest(territoryId, target, out var aetheryte))
        {
            return false;
        }

        var fromHere = Vector3.Distance(player.Position, target);
        var fromAetheryte = Vector3.Distance(aetheryte.Position, target);
        var hop = Vector3.Distance(player.Position, aetheryte.Position);
        var worthIt = purpose switch
        {
            TeleportPurpose.Shortcut => fromHere - fromAetheryte >= TeleportShortcutMinSavingMeters,
            // A recovery that lands farther from the spot than the character already is trades one long trip for another.
            TeleportPurpose.Recovery => hop >= TeleportRecoveryMinHopMeters && fromAetheryte < fromHere,
            _ => true,
        };
        if (!worthIt)
        {
            return false;
        }

        Diag(purpose switch
        {
            TeleportPurpose.Shortcut => $"{label}: {aetheryte.Name} is {fromAetheryte:F0}m from the spot against {fromHere:F0}m from here; teleporting",
            TeleportPurpose.Recovery => $"{label}: teleporting to {aetheryte.Name} to get unstuck, {fromAetheryte:F0}m from the spot",
            _ => $"{label}: teleporting to {aetheryte.Name} to get back onto the world, {fromAetheryte:F0}m from the spot",
        });
        return await TeleportToAetheryte(territoryId, aetheryte, label);
    }

    // A zone can hold landmasses no floor connects, and the aetheryte nearest the spot in a straight line can stand on
    // the wrong one, so each attuned aetheryte is asked for its own ground route and the shortest whole one wins.
    private async Task<bool> TryTeleportToConnectedAetheryte(uint territoryId, Vector3 target, float reachMeters, string scope)
    {
        if (Svc.Condition[ConditionFlag.InCombat] || Svc.Objects.LocalPlayer is not { } player)
        {
            return false;
        }

        var here = player.Position;
        var aetherytes = ZoneAetherytes.TeleportableIn(territoryId);
        var navmesh = NavmeshIPC.Instance;
        ZoneAetheryte? connected = null;
        var shortest = float.MaxValue;
        for (var index = 0; index < aetherytes.Length; index++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            var aetheryte = aetherytes.Span[index];
            if (!ZoneAetherytes.IsAttuned(aetheryte.Id) || Vector3.Distance(here, aetheryte.Position) < TeleportRecoveryMinHopMeters)
            {
                continue;
            }

            if (navmesh.NearestStandablePoint(aetheryte.Position, AetheryteFloorHalfExtentMeters, AetheryteFloorHalfExtentMeters) is not { } floor)
            {
                continue;
            }

            var route = await ProbeGroundRoute(floor, target);
            var reaches = route.Kind == GroundRouteKind.Complete || (route.Kind == GroundRouteKind.Partial && route.ShortfallMeters <= reachMeters);
            Diag($"{scope}: from {aetheryte.Name} the ground route is {route.Kind}, {route.LengthMeters:F0}m long and {route.ShortfallMeters:F0}m short of the spot");
            if (!reaches || route.LengthMeters >= shortest)
            {
                continue;
            }

            shortest = route.LengthMeters;
            connected = aetheryte;
        }

        if (connected is not { } chosen)
        {
            return false;
        }

        Diag($"{scope}: the spot is on another landmass; {chosen.Name} reaches it over {shortest:F0}m of ground, teleporting there");
        return await TeleportToAetheryte(territoryId, chosen, $"{scope}-landmass");
    }

    private async Task<bool> TeleportToAetheryte(uint territoryId, ZoneAetheryte aetheryte, string label)
    {
        var moved = false;
        await RunWithStatusPinned($"Teleporting to {aetheryte.Name}", async () =>
        {
            await PrepareForTeleport(label);
            if (CancelToken.IsCancellationRequested || Svc.Objects.LocalPlayer is not { } current)
            {
                return;
            }

            // Measured after any dismount, so a descent from flight does not pass for teleport progress.
            var before = current.Position;
            var operation = new MoveOp(move => move.Teleport(territoryId, aetheryte.Position, allowSameZoneTeleport: true));
            await RunCancellable(operation, TravelTeleportWatchdogMs, label, StuckDetector.IdleStallAbort(StuckDetector.IdleStallTimeoutMs));
            if (operation.Fault is { } fault)
            {
                Diag($"{label}: teleport faulted: {fault.Message}");
                return;
            }

            moved = Svc.Objects.LocalPlayer is { } landed && Vector3.Distance(before, landed.Position) >= TeleportMovedMeters;
        });

        if (!moved)
        {
            Diag($"{label}: the teleport to {aetheryte.Name} did not move the character; carrying on from here");
            return false;
        }

        await WaitForNavmeshReady(TravelNavmeshWaitMs, TravelNavmeshPollFrames);
        return true;
    }

    private async Task TryAethernetShortcut(uint territoryId, Vector3 target)
    {
        if (Svc.Condition[ConditionFlag.InCombat] || Svc.Objects.LocalPlayer is not { } player)
        {
            return;
        }

        if (!ZoneAetherytes.TryFindAethernetShortcut(territoryId, player.Position, target, out var shortcut))
        {
            return;
        }

        Diag($"Travel: riding the aethernet from {shortcut.Source.Name} to {shortcut.Destination.Name}, about {shortcut.SavedMeters:F0}m shorter than walking");
        await RunWithStatusPinned($"Riding the aethernet to {shortcut.Destination.Name}", async () =>
        {
            // Aiming at the chosen stop itself makes the library ride to exactly that one.
            var operation = new MoveOp(move => move.Aethernet(territoryId, shortcut.Destination.Position));
            await RunCancellable(operation, AethernetLegMs, "travel-aethernet");
            if (operation.Fault is { } fault)
            {
                Diag($"Travel: the aethernet ride faulted: {fault.Message}; walking instead");
            }
        });

        await WaitForNavmeshReady(TravelNavmeshWaitMs, TravelNavmeshPollFrames);
    }

    // In the air the mount is pressed against something, and only backing away frees it. On the ground the jump clears
    // a low snag; when the character is still on the mesh after that, a step to the side, alternating sides on each
    // stall, gets it off the corner or fence post the straight route keeps pushing into.
    private async Task Unstick(string scope, Vector3 target, int stalls)
    {
        if (Svc.Condition[ConditionFlag.InFlight])
        {
            Diag($"{scope}: stuck in the air ({ConditionTag()})");
            await BackOffInFlight(scope);
            return;
        }

        if (Svc.Condition[ConditionFlag.Swimming] || Svc.Condition[ConditionFlag.Diving])
        {
            Diag($"{scope}: stuck in the water ({ConditionTag()}); re-pathing");
            return;
        }

        Status = "Getting unstuck";
        Diag($"{scope}: stuck ({ConditionTag()}); jumping and stepping off the snag");
        UseGeneralAction(JumpGeneralActionId);
        await DelayMs(UnstickSettleMs);
        if (CancelToken.IsCancellationRequested || Svc.Objects.LocalPlayer is not { } player)
        {
            return;
        }

        var position = player.Position;
        var navmesh = NavmeshIPC.Instance;
        var step = navmesh.NearestStandablePoint(position, UnstickStepHalfExtentMeters, UnstickStepHalfExtentMeters);
        if (step is null || Vector3.Distance(position, step.Value) < UnstickStepMinMeters)
        {
            step = SideStep(navmesh, position, target, stalls);
        }

        if (step is not { } destination)
        {
            return;
        }

        var stepScope = $"{scope}-step";
        Diag($"{stepScope}: stepping {Vector3.Distance(position, destination):F1}m to {FormatPosition(destination)}");
        var operation = new MoveOp(move => move.MoveInZone(destination, walkMovement.WithTolerance(UnstickStepToleranceMeters), null));
        await RunCancellable(operation, UnstickStepWatchdogMs, stepScope, StuckDetector.MoveStallAbort(stepScope));
    }

    private static Vector3? SideStep(NavmeshIPC navmesh, Vector3 position, Vector3 target, int stalls)
    {
        var toward = new Vector2(target.X - position.X, target.Z - position.Z);
        if (toward.LengthSquared() < UnstickStepMinMeters * UnstickStepMinMeters)
        {
            return null;
        }

        toward = Vector2.Normalize(toward);
        var side = stalls % 2 == 0 ? new Vector2(-toward.Y, toward.X) : new Vector2(toward.Y, -toward.X);
        var candidate = position + new Vector3(side.X * UnstickSideStepMeters, 0f, side.Y * UnstickSideStepMeters);
        var snapped = navmesh.NearestStandablePoint(candidate, UnstickSideStepHalfExtentMeters, UnstickSideStepHalfExtentMeters);
        return snapped is { } onMesh && Vector3.Distance(position, onMesh) >= UnstickStepMinMeters ? onMesh : null;
    }

    // Data-set spawn points can sit a little inside the terrain or above it; the floor under them is what the pathfinder accepts.
    private Vector3 SnapToFloor(Vector3 destination)
    {
        var navmesh = NavmeshIPC.Instance;
        var lifted = destination with { Y = destination.Y + SnapLiftMeters };
        var floor = navmesh.PointOnFloor(lifted, allowUnlandable: false, SnapHalfExtentMeters)
            ?? navmesh.PointOnFloor(lifted, allowUnlandable: true, SnapHalfExtentMeters);
        var target = floor is { } point && destination.Y - point.Y <= SnapMaxDropMeters
            ? point
            : navmesh.NearestStandablePoint(destination, SnapHalfExtentMeters, SnapHalfExtentMeters) ?? destination;
        var moved = Vector3.Distance(destination, target);
        if (moved >= SnapReportMeters)
        {
            Diag($"Travel: destination snapped {moved:F1}m onto the floor at {FormatPosition(target)}");
        }

        return target;
    }

    private static bool PlayerReady()
        => Svc.Objects.LocalPlayer is not null
        && !Svc.Condition[ConditionFlag.BetweenAreas]
        && !Svc.Condition[ConditionFlag.BetweenAreas51];

    private static bool FreeToMount()
        => !Svc.Condition[ConditionFlag.InCombat]
        && !Svc.Condition[ConditionFlag.Swimming]
        && !Svc.Condition[ConditionFlag.Diving];

    private static bool Arrived(Vector3 target, Vector3 destination, float arriveWithin)
        => WithinReach(target, arriveWithin) || WithinReach(destination, arriveWithin);

    private static float DistanceTo(Vector3 target)
        => Svc.Objects.LocalPlayer is { } player ? Vector3.Distance(player.Position, target) : float.MaxValue;

    private static int TravelBudgetMs(Vector3 target)
    {
        var distance = Svc.Objects.LocalPlayer is { } player ? Vector3.Distance(player.Position, target) : 0f;
        var travelMs = distance / TravelMinSpeedMetersPerSecond * TimeUnits.MillisecondsPerSecond;
        return (int)Math.Min(TravelMaxBudgetMs, TravelBaseBudgetMs + travelMs);
    }

    private static bool TerritoryAllowsMount(uint territoryId)
        => Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().GetRowOrDefault(territoryId)?.Mount ?? false;

    private static string FormatPosition(Vector3 position)
        => $"({position.X:F1}, {position.Y:F1}, {position.Z:F1})";
}
