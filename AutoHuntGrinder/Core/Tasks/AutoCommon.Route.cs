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
    // The pathfinder answers an unreachable target with the route to the nearest floor it can reach and then the raw
    // target appended, so a last leg longer than mesh noise runs the character into whatever lies between the two.
    private const float PartialRouteGapMeters = 0.75f;
    private const float RouteGoalHalfExtentMeters = 5f;
    // Taking off, the flight path and the landing cost more time than the ground does over anything shorter.
    private const float FlightMinMeters = 80f;
    private const float MinLegToleranceMeters = 1.5f;

    private static readonly MovementConfig flightMovement = MovementConfig.Default.WithOptions(MovementOptions.Mount | MovementOptions.Fly);
    private static readonly MovementConfig groundMountMovement = MovementConfig.Default.WithOptions(MovementOptions.Mount);
    private static readonly MovementConfig walkMovement = MovementConfig.Default;

    private enum GroundRouteKind : byte { Unknown, Complete, Partial }

    private enum LegMode : byte { OnFoot, GroundMount, Flight }

    // End is the last point the route reaches on the floor, and ShortfallMeters how far that is from the target.
    private readonly record struct GroundRoute(GroundRouteKind Kind, Vector3 End, float LengthMeters, float ShortfallMeters);

    private readonly record struct LegPlan(LegMode Mode, Vector3 Target, float ShortfallMeters, bool Reachable)
    {
        public bool Rides => Mode != LegMode.OnFoot;

        // A leg aimed at the end of a partial route has to get closer to it than the caller asked, so that stopping
        // there still counts as being within reach of the real target.
        public float ToleranceFor(float wantedMeters)
            => ShortfallMeters <= 0f ? wantedMeters : MathF.Max(MinLegToleranceMeters, wantedMeters - ShortfallMeters);
    }

    // From the air there is no floor under the character to plan a ground route from, so a flight carries on as one.
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
            return new LegPlan(LegMode.Flight, destination, 0f, true);
        }

        var mountable = mountingAllowed && FreeToMount();
        var mounted = Svc.Condition[ConditionFlag.Mounted];
        var flightAvailable = mountable && FlightAccess.IsAvailableIn(territoryId);
        var straight = Vector3.Distance(player.Position, destination);
        if (flightAvailable && (groundStalled || straight > FlightMinMeters))
        {
            return new LegPlan(LegMode.Flight, destination, 0f, true);
        }

        var route = await ProbeGroundRoute(player.Position, destination);
        var plan = PlanFromRoute(route, destination, straight, reachMeters, mountMinMeters, mountable, mounted, flightAvailable);
        Diag($"{scope}: {DescribeLegPlan(plan, route, straight, flightAvailable)}");
        return plan;
    }

    private static LegPlan PlanFromRoute(in GroundRoute route, Vector3 destination, float straight, float reachMeters, float mountMinMeters, bool mountable, bool mounted, bool flightAvailable)
    {
        if (route.Kind == GroundRouteKind.Unknown)
        {
            return new LegPlan(LegModeFor(mountable && (mounted || straight > mountMinMeters), flightAvailable), destination, 0f, true);
        }

        var withinReach = route.Kind == GroundRouteKind.Complete || route.ShortfallMeters <= reachMeters;
        if (!withinReach)
        {
            return flightAvailable
                ? new LegPlan(LegMode.Flight, destination, 0f, true)
                : new LegPlan(LegMode.OnFoot, route.End, route.ShortfallMeters, false);
        }

        var ride = mountable && (mounted || route.LengthMeters > mountMinMeters);
        var mode = LegModeFor(ride, flightAvailable && route.LengthMeters > FlightMinMeters);
        return route.Kind == GroundRouteKind.Complete || mode == LegMode.Flight
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

    private static string DescribeLegPlan(in LegPlan plan, in GroundRoute route, float straight, bool flightAvailable)
    {
        var ground = route.Kind switch
        {
            GroundRouteKind.Complete => $"the ground route is {route.LengthMeters:F0}m for {straight:F0}m straight",
            GroundRouteKind.Partial => $"the ground route ends {route.ShortfallMeters:F0}m short of the spot after {route.LengthMeters:F0}m",
            _ => $"no ground route answer for {straight:F0}m straight",
        };
        var verdict = plan.Reachable ? $"going {plan.Mode}" : "out of reach on the ground";
        return $"{ground}, flight {(flightAvailable ? "available" : "unavailable")}; {verdict}";
    }

    // The pathfinder needs floor within its own search box at both ends; asking without it only logs an error there.
    private async Task<GroundRoute> ProbeGroundRoute(Vector3 from, Vector3 to)
    {
        var navmesh = NavmeshIPC.Instance;
        if (navmesh.NearestPoint(from, RouteGoalHalfExtentMeters, RouteGoalHalfExtentMeters) is null
            || navmesh.NearestPoint(to, RouteGoalHalfExtentMeters, RouteGoalHalfExtentMeters) is not { } goal)
        {
            return default;
        }

        var pending = NavmeshPathfindIPC.Instance.Pathfind(from, goal, fly: false);
        if (pending is null)
        {
            return default;
        }

        var deadline = Environment.TickCount64 + RouteQueryTimeoutMs;
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
            Diag($"Route: the ground route query faulted: {pending.Exception?.GetBaseException().Message}");
            return default;
        }

        return MeasureGroundRoute(pending.Result, from, goal);
    }

    private static GroundRoute MeasureGroundRoute(List<Vector3>? waypoints, Vector3 from, Vector3 goal)
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
            ? new GroundRoute(GroundRouteKind.Partial, end, length, shortfall)
            : new GroundRoute(GroundRouteKind.Complete, goal, length, 0f);
    }

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
