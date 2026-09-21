using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;
using System.Numerics;

namespace AutoHuntGrinder.Core.Ipc;

internal sealed class NavmeshIPC
{
    public const int WaypointsUnavailable = -1;

    private const float AboveEveryTerrainY = 1024f;

    private const float BuildIdle = -1f;
    private const string IsReadyFailed = AhgConstants.LogPrefix + " Navmesh IsReady failed";
    private const string BuildProgressFailed = AhgConstants.LogPrefix + " Navmesh BuildProgress failed";
    private const string IsRunningFailed = AhgConstants.LogPrefix + " Navmesh IsRunning failed";
    private const string SimpleMovePathfindFailed = AhgConstants.LogPrefix + " Navmesh SimpleMove.PathfindInProgress failed";
    private const string NavPathfindFailed = AhgConstants.LogPrefix + " Navmesh Nav.PathfindInProgress failed";
    private const string NearestPointFailed = AhgConstants.LogPrefix + " Navmesh NearestPoint failed";
    private const string NearestPointReachableFailed = AhgConstants.LogPrefix + " Navmesh NearestPointReachable failed";
    private const string PointOnFloorFailed = AhgConstants.LogPrefix + " Navmesh PointOnFloor failed";
    private const string NumWaypointsFailed = AhgConstants.LogPrefix + " Navmesh NumWaypoints failed";
    private const string ListWaypointsFailed = AhgConstants.LogPrefix + " Navmesh ListWaypoints failed";
    private const string StopFailed = AhgConstants.LogPrefix + " Navmesh Stop failed";
    private const string MoveToFailed = AhgConstants.LogPrefix + " Navmesh Path.MoveTo failed";

    private static NavmeshIPC? instance;

    private readonly ICallGateSubscriber<bool> pathIsRunning;
    private readonly ICallGateSubscriber<bool> simpleMovePathfindInProgress;
    private readonly ICallGateSubscriber<bool> navPathfindInProgress;
    private readonly ICallGateSubscriber<bool> navIsReady;
    private readonly ICallGateSubscriber<float> navBuildProgress;
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> nearestPoint;
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> nearestPointReachable;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> pointOnFloor;
    private readonly ICallGateSubscriber<object> pathStop;
    private readonly ICallGateSubscriber<List<Vector3>, bool, object> pathMoveTo;
    private readonly ICallGateSubscriber<int> pathNumWaypoints;
    private readonly ICallGateSubscriber<List<Vector3>> pathListWaypoints;

    // Cached once so the per-frame stall checks do not allocate a delegate on every call.
    private readonly Func<bool> isRunningCall;
    private readonly Func<bool> simpleMovePathfindInProgressCall;
    private readonly Func<bool> navPathfindInProgressCall;
    private readonly Func<bool> isReadyCall;
    private readonly Func<float> buildProgressCall;
    private readonly Func<int> numWaypointsCall;
    private readonly Func<List<Vector3>?> listWaypointsCall;
    private readonly Action stopCall;

    private NavmeshIPC()
    {
        var pluginInterface = Svc.PluginInterface;
        pathIsRunning = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        simpleMovePathfindInProgress = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        navPathfindInProgress = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress");
        navIsReady = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        navBuildProgress = pluginInterface.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
        nearestPoint = pluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPoint");
        nearestPointReachable = pluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable");
        pointOnFloor = pluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
        pathStop = pluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        pathMoveTo = pluginInterface.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo");
        pathNumWaypoints = pluginInterface.GetIpcSubscriber<int>("vnavmesh.Path.NumWaypoints");
        pathListWaypoints = pluginInterface.GetIpcSubscriber<List<Vector3>>("vnavmesh.Path.ListWaypoints");

        isRunningCall = pathIsRunning.InvokeFunc;
        simpleMovePathfindInProgressCall = simpleMovePathfindInProgress.InvokeFunc;
        navPathfindInProgressCall = navPathfindInProgress.InvokeFunc;
        isReadyCall = navIsReady.InvokeFunc;
        buildProgressCall = navBuildProgress.InvokeFunc;
        numWaypointsCall = pathNumWaypoints.InvokeFunc;
        listWaypointsCall = pathListWaypoints.InvokeFunc;
        stopCall = pathStop.InvokeAction;
    }

    public static NavmeshIPC Instance => instance ??= new NavmeshIPC();

    // Pathfind queries throw while the zone mesh is still building. A build without this gate reads as ready so it never blocks.
    public bool IsReady()
        => IpcGate.Invoke(navIsReady.HasFunction, isReadyCall, true, IsReadyFailed);

    // 0 to 1 while a build runs, BuildIdle once it is done.
    public float BuildProgress()
        => IpcGate.Invoke(navBuildProgress.HasFunction, buildProgressCall, BuildIdle, BuildProgressFailed);

    public bool IsRunning()
        => IpcGate.Invoke(pathIsRunning.HasFunction, isRunningCall, false, IsRunningFailed);

    public bool IsPathfinding()
        => IpcGate.Invoke(simpleMovePathfindInProgress.HasFunction, simpleMovePathfindInProgressCall, false, SimpleMovePathfindFailed)
        || IpcGate.Invoke(navPathfindInProgress.HasFunction, navPathfindInProgressCall, false, NavPathfindFailed);

    public bool IsBusy()
        => IsRunning() || IsPathfinding();

    // The pathfinder calls a polygon reachable only when it connects to one of its own seed points for the zone. A zone
    // seeded on one landmass reads every other landmass as unreachable, so this answers "is any floor here at all".
    public Vector3? NearestPoint(Vector3 position, float halfExtentXZ = 5f, float halfExtentY = 5f)
        => IpcGate.Invoke(
            nearestPoint.HasFunction,
            () => nearestPoint.InvokeFunc(position, halfExtentXZ, halfExtentY),
            (Vector3?)null,
            NearestPointFailed);

    public Vector3? NearestPointReachable(Vector3 position, float halfExtentXZ = 5f, float halfExtentY = 5f)
        => IpcGate.Invoke(
            nearestPointReachable.HasFunction,
            () => nearestPointReachable.InvokeFunc(position, halfExtentXZ, halfExtentY),
            (Vector3?)null,
            NearestPointReachableFailed);

    public Vector3? NearestStandablePoint(Vector3 position, float halfExtentXZ = 5f, float halfExtentY = 5f)
        => NearestPointReachable(position, halfExtentXZ, halfExtentY) ?? NearestPoint(position, halfExtentXZ, halfExtentY);

    public Vector3? PointOnFloor(Vector3 point, bool allowUnlandable, float halfExtentXZ)
        => IpcGate.Invoke(
            pointOnFloor.HasFunction,
            () => pointOnFloor.InvokeFunc(point, allowUnlandable, halfExtentXZ),
            (Vector3?)null,
            PointOnFloorFailed);

    public Vector3? HighestFloor(Vector3 point, bool allowUnlandable, float halfExtentXZ)
        => PointOnFloor(point with { Y = AboveEveryTerrainY }, allowUnlandable, halfExtentXZ);

    public int NumWaypoints()
        => IpcGate.Invoke(pathNumWaypoints.HasFunction, numWaypointsCall, WaypointsUnavailable, NumWaypointsFailed);

    // The call copies the whole waypoint list, so callers cache the answer.
    public Vector3? CurrentWaypoint()
    {
        var waypoints = IpcGate.Invoke(pathListWaypoints.HasFunction, listWaypointsCall, null, ListWaypointsFailed);
        return waypoints is { Count: > 0 } ? waypoints[0] : null;
    }

    // Registered as an action on the far side, so it is HasAction that says whether it can be called.
    public void Stop()
        => IpcGate.Run(pathStop.HasAction, stopCall, StopFailed);

    // Follows the given waypoints as they are, with no path search, so the caller has to know the way is clear.
    public void MoveAlong(List<Vector3> waypoints, bool fly)
        => IpcGate.Run(pathMoveTo.HasAction, () => pathMoveTo.InvokeAction(waypoints, fly), MoveToFailed);
}
