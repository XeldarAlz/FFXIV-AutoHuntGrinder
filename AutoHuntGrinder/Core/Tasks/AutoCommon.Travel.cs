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

    private static readonly MovementConfig rideMovement = MovementConfig.Everything.WithOptions(MovementOptions.Mount | MovementOptions.Fly);
    private static readonly MovementConfig walkMovement = MovementConfig.Default;

    private enum ZoneTravelResult { Arrived, Failed, LeftZone }

    private enum LegOutcome { EndedShort, Stalled, Faulted, MountFailed, Remount }

    protected async Task<bool> TravelTo(uint territoryId, Vector3 destination, float arriveWithin)
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

            var result = await TravelWithinZone(territoryId, destination, arriveWithin, zoneName);
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

    private async Task<ZoneTravelResult> TravelWithinZone(uint territoryId, Vector3 destination, float arriveWithin, string zoneName)
    {
        await WaitForNavmeshReady(TravelNavmeshWaitMs, TravelNavmeshPollFrames);
        if (CancelToken.IsCancellationRequested)
        {
            return ZoneTravelResult.Failed;
        }

        var target = SnapToFloor(destination);
        if (!Arrived(target, destination, arriveWithin))
        {
            await TryTeleportShortcut(territoryId, target, "travel-shortcut", recovery: false);
            await TryAethernetShortcut(territoryId, target);
        }

        var mountingAllowed = TerritoryAllowsMount(territoryId);
        var budgetMs = TravelBudgetMs(target);
        var deadline = Environment.TickCount64 + budgetMs;
        var stalls = 0;
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
            var outcome = await RunTravelLeg(target, arriveWithin, mountingAllowed, (int)remainingMs, scope, zoneName);
            if (outcome == LegOutcome.Remount || CancelToken.IsCancellationRequested || Arrived(target, destination, arriveWithin))
            {
                continue;
            }

            if (outcome == LegOutcome.MountFailed)
            {
                mountingAllowed = false;
                Diag($"{scope}: could not mount here; walking the rest of the way");
            }

            stalls++;
            if (stalls >= MaxTravelStalls)
            {
                Warn($"Travel: stuck {stalls} times {DistanceTo(target):F0}m short of the spot in {zoneName}; giving up");
                return ZoneTravelResult.Failed;
            }

            NavmeshIPC.Instance.Stop();
            if (stalls >= StallsBeforeTeleportRecovery && !teleportRecoveryUsed)
            {
                teleportRecoveryUsed = true;
                if (await TryTeleportShortcut(territoryId, target, $"{scope}-recovery", recovery: true))
                {
                    continue;
                }
            }

            if (stalls >= StallsBeforeUnstick)
            {
                await Unstick(scope);
            }
            else
            {
                Diag($"{scope}: re-pathing from here");
            }
        }
    }

    private async Task<LegOutcome> RunTravelLeg(Vector3 target, float arriveWithin, bool mountingAllowed, int watchdogMs, string scope, string zoneName)
    {
        var ride = mountingAllowed && FreeToMount() && (Svc.Condition[ConditionFlag.Mounted] || DistanceTo(target) > MountMinMeters);
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

        Diag($"{scope}: {(ride ? "mounted" : "on foot")}, flight {(ECommons.GameHelpers.Player.CanFly ? "available" : "unavailable")}, {DistanceTo(target):F0}m to go");
        var operation = new MoveOp(move => move.MoveInZone(target, MovementFor(ride, arriveWithin), StopCondition));
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

        return completed ? LegOutcome.EndedShort : LegOutcome.Stalled;
    }

    private async Task<bool> TryTeleportShortcut(uint territoryId, Vector3 target, string label, bool recovery)
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
        var worthIt = recovery
            ? Vector3.Distance(player.Position, aetheryte.Position) >= TeleportRecoveryMinHopMeters
            : fromHere - fromAetheryte >= TeleportShortcutMinSavingMeters;
        if (!worthIt)
        {
            return false;
        }

        Diag(recovery
            ? $"{label}: teleporting to {aetheryte.Name} to get unstuck, {fromAetheryte:F0}m from the spot"
            : $"{label}: {aetheryte.Name} is {fromAetheryte:F0}m from the spot against {fromHere:F0}m from here; teleporting");
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

    private async Task Unstick(string scope)
    {
        if (Svc.Condition[ConditionFlag.InFlight] || Svc.Condition[ConditionFlag.Swimming] || Svc.Condition[ConditionFlag.Diving])
        {
            Diag($"{scope}: stuck off the ground ({ConditionTag()}); re-pathing");
            return;
        }

        Status = "Getting unstuck";
        Diag($"{scope}: stuck ({ConditionTag()}); jumping and stepping back onto the mesh");
        UseGeneralAction(JumpGeneralActionId);
        await DelayMs(UnstickSettleMs);
        if (CancelToken.IsCancellationRequested || Svc.Objects.LocalPlayer is not { } player)
        {
            return;
        }

        var position = player.Position;
        if (NavmeshIPC.Instance.NearestPointReachable(position, UnstickStepHalfExtentMeters, UnstickStepHalfExtentMeters) is not { } step
            || Vector3.Distance(position, step) < UnstickStepMinMeters)
        {
            return;
        }

        var stepScope = $"{scope}-step";
        var operation = new MoveOp(move => move.MoveInZone(step, walkMovement.WithTolerance(UnstickStepToleranceMeters), null));
        await RunCancellable(operation, UnstickStepWatchdogMs, stepScope, StuckDetector.MoveStallAbort(stepScope));
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
            : navmesh.NearestPointReachable(destination, SnapHalfExtentMeters, SnapHalfExtentMeters) ?? destination;
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

    private static MovementConfig MovementFor(bool ride, float tolerance)
        => (ride ? rideMovement : walkMovement).WithTolerance(tolerance);

    private static bool TerritoryAllowsMount(uint territoryId)
        => Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().GetRowOrDefault(territoryId)?.Mount ?? false;

    private static string FormatPosition(Vector3 position)
        => $"({position.X:F1}, {position.Y:F1}, {position.Z:F1})";
}
