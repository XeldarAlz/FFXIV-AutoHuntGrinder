using AutoHuntGrinder.Core.Ipc;
using ECommons.DalamudServices;
using System.Numerics;

namespace AutoHuntGrinder.Core.Tasks;

internal enum StallKind { None, NavWedge, Idle }

// NavWedge: navigation is engaged yet the character is not closing on the waypoint it steers at. Progress is
// measured against that waypoint because sliding along a wall displaces the character without closing, and the
// navigator's own stop-and-retry cycle flickers its running flag. Idle: no movement while nothing legitimate is
// happening, almost always a teleport that was issued and never started casting.
internal sealed class MoveStallTracker
{
    private Vector3? anchor;
    private Vector3? target;
    private float bestTargetDistance = float.MaxValue;
    private int lastWaypointCount;
    private bool followedOnce;
    private bool pathInterrupted;
    private long navWedgeSinceMs = Environment.TickCount64;
    private long idleSinceMs = Environment.TickCount64;

    public StallKind Check()
    {
        var player = Svc.Objects.LocalPlayer;
        if (player is null)
        {
            return StallKind.None;
        }

        var now = Environment.TickCount64;
        var position = player.Position;
        var navmesh = NavmeshIPC.Instance;
        var navigating = navmesh.IsRunning();
        var navigationBusy = navigating || navmesh.IsPathfinding();
        var frozenLegitimately = StuckDetector.IsPositionFrozenLegit();

        if (frozenLegitimately || navigationBusy)
        {
            idleSinceMs = now;
        }
        else if (now - idleSinceMs >= StuckDetector.IdleStallTimeoutMs)
        {
            return StallKind.Idle;
        }

        if (frozenLegitimately)
        {
            navWedgeSinceMs = now;
            anchor = position;
            return StallKind.None;
        }

        if (!navigating)
        {
            if (followedOnce)
            {
                pathInterrupted = true;
            }

            if (!followedOnce || !navigationBusy)
            {
                navWedgeSinceMs = now;
            }

            return StallKind.None;
        }

        followedOnce = true;
        var waypointCount = navmesh.NumWaypoints();
        var progressed = waypointCount == NavmeshIPC.WaypointsUnavailable
            ? DisplacedFromAnchor(position)
            : ClosedOnWaypoint(navmesh, waypointCount, position);

        if (progressed)
        {
            navWedgeSinceMs = now;
            return StallKind.None;
        }

        return now - navWedgeSinceMs >= StuckDetector.NavWedgeTimeoutMs ? StallKind.NavWedge : StallKind.None;
    }

    private bool DisplacedFromAnchor(Vector3 position)
    {
        if (anchor is not null && Vector3.Distance(anchor.Value, position) <= StuckDetector.StuckMoveThresholdMeters)
        {
            return false;
        }

        anchor = position;
        return true;
    }

    private bool ClosedOnWaypoint(NavmeshIPC navmesh, int waypointCount, Vector3 position)
    {
        var consumed = target is not null && !pathInterrupted && waypointCount < lastWaypointCount;
        var newPath = target is null || pathInterrupted || waypointCount > lastWaypointCount;
        if (consumed || newPath)
        {
            pathInterrupted = false;
            lastWaypointCount = waypointCount;
            target = navmesh.CurrentWaypoint();
            bestTargetDistance = target is { } fresh ? Vector3.Distance(position, fresh) : float.MaxValue;
            if (target is null)
            {
                return DisplacedFromAnchor(position);
            }

            return consumed;
        }

        if (target is null)
        {
            return DisplacedFromAnchor(position);
        }

        var distance = Vector3.Distance(position, target.Value);
        if (distance >= bestTargetDistance - StuckDetector.ProgressEpsilonMeters)
        {
            return false;
        }

        bestTargetDistance = distance;
        return true;
    }
}
