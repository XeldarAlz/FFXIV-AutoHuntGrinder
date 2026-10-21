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
    private const int RouteQueryTimeoutMs = 5_000;
    // Before it answers a spot the air cannot reach, the pathfinder searches all the open air it can; from Camp Overlook
    // to the tunnels of Outer La Noscea that took about 4.5 s (issue #19).
    private const int AirRouteQueryTimeoutMs = 15_000;
    // The pathfinder answers an unreachable target with the route to the nearest floor it can reach and then the raw
    // target appended, so a last leg longer than mesh noise runs the character into whatever lies between the two.
    private const float PartialRouteGapMeters = 0.75f;
    private const float RouteGoalHalfExtentMeters = 5f;
    // Taking off, the flight path and the landing cost more time than the ground does over anything shorter.
    private const float FlightMinMeters = 80f;
    private const float MinLegToleranceMeters = 1.5f;
    // A flight to the end of the air route parks a few metres off it, and from there another one gains nothing.
    private const float AirRouteEndReachedMeters = 10f;
    // The character's feet can sit a little under the mesh's own floor.
    private const float FloorBelowLiftMeters = 2f;

    private static readonly MovementConfig flightMovement = MovementConfig.Default.WithOptions(MovementOptions.Mount | MovementOptions.Fly);
    private static readonly MovementConfig groundMountMovement = MovementConfig.Default.WithOptions(MovementOptions.Mount);
    private static readonly MovementConfig walkMovement = MovementConfig.Default;

    private enum RouteKind : byte { Unknown, Complete, Partial }

    private enum LegMode : byte { OnFoot, GroundMount, Flight, Dive }

    // End is the last point the route reaches, on the floor or in the air, and ShortfallMeters how far that is from the target.
    private readonly record struct ProbedRoute(RouteKind Kind, Vector3 End, float LengthMeters, float ShortfallMeters);

    private readonly record struct LegPlan(LegMode Mode, Vector3 Target, float ShortfallMeters, bool Reachable)
    {
        public bool Rides => Mode is LegMode.GroundMount or LegMode.Flight;

        // A flight to where the air route ends, short of the destination; the rest of the way is planned from there.
        public bool Stopover => Mode == LegMode.Flight && ShortfallMeters > 0f;

        // A leg aimed at the end of a partial route has to get closer to it than the caller asked, so that stopping
        // there still counts as being within reach of the real target.
        public float ToleranceFor(float wantedMeters)
            => ShortfallMeters <= 0f ? wantedMeters : MathF.Max(MinLegToleranceMeters, wantedMeters - ShortfallMeters);
    }

    // From the air a flight carries on as one unless the air route stops short, and under water only the pathfinder's
    // flight volume has a way through.
    // The mesh can call a route whole that the game blocks with a wall it has no collision for, so a caller whose
    // ground leg already stalled asks for the air.
    private async Task<LegPlan> PlanLeg(uint territoryId, Vector3 destination, float reachMeters, float mountMinMeters, bool mountingAllowed, bool groundStalled, string scope)
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return new LegPlan(LegMode.OnFoot, destination, 0f, true);
        }

        if (Svc.Condition[ConditionFlag.InFlight])
        {
            return await PlanFlight(destination, reachMeters, mountMinMeters, mountingAllowed, scope);
        }

        if (Svc.Condition[ConditionFlag.Diving])
        {
            return new LegPlan(LegMode.Dive, destination, 0f, true);
        }

        var mounted = Svc.Condition[ConditionFlag.Mounted];
        var mountable = Mountable(mountingAllowed, mounted);
        var flightAvailable = mountable && FlightAccess.IsAvailableIn(territoryId);
        var straight = Vector3.Distance(player.Position, destination);
        if (flightAvailable && (groundStalled || straight > FlightMinMeters))
        {
            return await PlanFlight(destination, reachMeters, mountMinMeters, mountingAllowed, scope);
        }

        var route = await ProbeGroundRoute(player.Position, destination);
        var plan = PlanFromRoute(route, destination, straight, reachMeters, mountMinMeters, mountable, mounted, flightAvailable);
        Diag($"{scope}: {DescribeLegPlan(plan, route, straight, flightAvailable)}");
        return plan.Mode == LegMode.Flight ? await PlanFlight(destination, reachMeters, mountMinMeters, mountingAllowed, scope) : plan;
    }

    // The air route answers a spot it cannot reach, such as one inside a tunnel, the way the ground route does: the way
    // to the nearest open air and then the raw target, a straight line into the rock that every flight wedged on (issue
    // #19). So a flight goes only as far as the air route does, and from there the ground takes over.
    private async Task<LegPlan> PlanFlight(Vector3 destination, float reachMeters, float mountMinMeters, bool mountingAllowed, string scope)
    {
        var straightFlight = new LegPlan(LegMode.Flight, destination, 0f, true);
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return straightFlight;
        }

        var air = await ProbeAirRoute(player.Position, destination);
        if (air.Kind != RouteKind.Partial || air.ShortfallMeters <= reachMeters)
        {
            return straightFlight;
        }

        var toAirEnd = Vector3.Distance(player.Position, air.End);
        Diag($"{scope}: the air route ends {air.ShortfallMeters:F0}m short of the spot at {FormatPosition(air.End)}, {toAirEnd:F0}m from here");
        if (toAirEnd <= FlightMinMeters && await PlanGroundFromHere(destination, reachMeters, mountMinMeters, mountingAllowed, scope) is { } ground)
        {
            return ground;
        }

        if (toAirEnd > AirRouteEndReachedMeters)
        {
            return new LegPlan(LegMode.Flight, air.End, air.ShortfallMeters, true);
        }

        Diag($"{scope}: neither the air nor the ground reaches the spot from here; flying straight at it");
        return straightFlight;
    }

    // Planned from the floor under the character, since that is where an airborne character lands before the ground leg.
    private async Task<LegPlan?> PlanGroundFromHere(Vector3 destination, float reachMeters, float mountMinMeters, bool mountingAllowed, string scope)
    {
        if (Svc.Objects.LocalPlayer is not { } player
            || NavmeshIPC.Instance.PointOnFloor(player.Position with { Y = player.Position.Y + FloorBelowLiftMeters }, allowUnlandable: true, RouteGoalHalfExtentMeters) is not { } floor)
        {
            return null;
        }

        var route = await ProbeGroundRoute(floor, destination);
        if (route.Kind == RouteKind.Unknown || route.ShortfallMeters > reachMeters)
        {
            Diag($"{scope}: no ground route from {FormatPosition(floor)} reaches the spot");
            return null;
        }

        if (Svc.Condition[ConditionFlag.InFlight] && !await DescendAndDismount($"{scope}-land"))
        {
            return null;
        }

        var mounted = Svc.Condition[ConditionFlag.Mounted];
        var plan = PlanFromRoute(route, destination, DistanceTo(destination), reachMeters, mountMinMeters, Mountable(mountingAllowed, mounted), mounted, flightAvailable: false);
        Diag($"{scope}: going on {plan.Mode} along the {route.LengthMeters:F0}m ground route from {FormatPosition(floor)}");
        return plan;
    }

    // A mount already summoned stays usable in the water, and whether it may take off there the game answers itself.
    private static bool Mountable(bool mountingAllowed, bool mounted)
        => mountingAllowed && (mounted ? !Svc.Condition[ConditionFlag.InCombat] : FreeToMount());

    private static LegPlan PlanFromRoute(in ProbedRoute route, Vector3 destination, float straight, float reachMeters, float mountMinMeters, bool mountable, bool mounted, bool flightAvailable)
    {
        if (route.Kind == RouteKind.Unknown)
        {
            return new LegPlan(LegModeFor(mountable && (mounted || straight > mountMinMeters), flightAvailable), destination, 0f, true);
        }

        var withinReach = route.Kind == RouteKind.Complete || route.ShortfallMeters <= reachMeters;
        if (!withinReach)
        {
            return flightAvailable
                ? new LegPlan(LegMode.Flight, destination, 0f, true)
                : new LegPlan(LegMode.OnFoot, route.End, route.ShortfallMeters, false);
        }

        var ride = mountable && (mounted || route.LengthMeters > mountMinMeters);
        var mode = LegModeFor(ride, flightAvailable && route.LengthMeters > FlightMinMeters);
        return route.Kind == RouteKind.Complete || mode == LegMode.Flight
            ? new LegPlan(mode, destination, 0f, true)
            : new LegPlan(mode, route.End, route.ShortfallMeters, true);
    }

    private static LegMode LegModeFor(bool ride, bool fly)
    {
        if (!ride)
        {
            return LegMode.OnFoot;
        }

        return fly ? LegMode.Flight : LegMode.GroundMount;
    }

    private static string DescribeLegPlan(in LegPlan plan, in ProbedRoute route, float straight, bool flightAvailable)
    {
        var ground = route.Kind switch
        {
            RouteKind.Complete => $"the ground route is {route.LengthMeters:F0}m for {straight:F0}m straight",
            RouteKind.Partial => $"the ground route ends {route.ShortfallMeters:F0}m short of the spot after {route.LengthMeters:F0}m",
            _ => $"no ground route answer for {straight:F0}m straight",
        };
        var verdict = plan.Reachable ? $"going {plan.Mode}" : "out of reach on the ground";
        return $"{ground}, flight {(flightAvailable ? "available" : "unavailable")}; {verdict}";
    }

    // The pathfinder needs floor within its own search box at both ends; asking without it only logs an error there.
    private async Task<ProbedRoute> ProbeGroundRoute(Vector3 from, Vector3 to)
    {
        var navmesh = NavmeshIPC.Instance;
        if (navmesh.NearestPoint(from, RouteGoalHalfExtentMeters, RouteGoalHalfExtentMeters) is null
            || navmesh.NearestPoint(to, RouteGoalHalfExtentMeters, RouteGoalHalfExtentMeters) is not { } goal)
        {
            return default;
        }

        return await QueryRoute(from, goal, fly: false, RouteQueryTimeoutMs);
    }

    private async Task<ProbedRoute> ProbeAirRoute(Vector3 from, Vector3 to)
        => await QueryRoute(from, to, fly: true, AirRouteQueryTimeoutMs);

    private async Task<ProbedRoute> QueryRoute(Vector3 from, Vector3 goal, bool fly, int timeoutMs)
    {
        var pending = NavmeshPathfindIPC.Instance.Pathfind(from, goal, fly);
        if (pending is null)
        {
            return default;
        }

        var deadline = Environment.TickCount64 + timeoutMs;
        while (!pending.IsCompleted)
        {
            if (CancelToken.IsCancellationRequested || Environment.TickCount64 >= deadline)
            {
                return default;
            }

            await NextFrame();
        }

        if (!pending.IsCompletedSuccessfully)
        {
            Diag($"Route: the {(fly ? "air" : "ground")} route query faulted: {pending.Exception?.GetBaseException().Message}");
            return default;
        }

        return MeasureRoute(pending.Result, from, goal);
    }

    private static ProbedRoute MeasureRoute(List<Vector3>? waypoints, Vector3 from, Vector3 goal)
    {
        if (waypoints is null || waypoints.Count < 2)
        {
            return default;
        }

        var endIndex = waypoints.Count - 2;
        var length = Vector3.Distance(from, waypoints[0]);
        for (var waypointIndex = 1; waypointIndex <= endIndex; waypointIndex++)
        {
            length += Vector3.Distance(waypoints[waypointIndex - 1], waypoints[waypointIndex]);
        }

        var end = waypoints[endIndex];
        var shortfall = Vector3.Distance(end, goal);
        return shortfall > PartialRouteGapMeters
            ? new ProbedRoute(RouteKind.Partial, end, length, shortfall)
            : new ProbedRoute(RouteKind.Complete, goal, length, 0f);
    }

    private static MoveOp LegOperation(LegMode mode, Vector3 target, float tolerance, Func<bool>? stopCondition)
        => mode == LegMode.Dive
            ? new MoveOp(move => move.DiveInZone(target, tolerance, stopCondition))
            : new MoveOp(move => move.MoveInZone(target, MovementFor(mode, tolerance), stopCondition));

    // A character swimming under water has no floor to walk on, so a short step there is a dive as well.
    private static LegMode StepMode() => Svc.Condition[ConditionFlag.Diving] ? LegMode.Dive : LegMode.OnFoot;

    private static MovementConfig MovementFor(LegMode mode, float tolerance)
    {
        var movement = mode switch
        {
            LegMode.Flight => flightMovement,
            LegMode.GroundMount => groundMountMovement,
            _ => walkMovement,
        };
        return movement.WithTolerance(tolerance);
    }
}
