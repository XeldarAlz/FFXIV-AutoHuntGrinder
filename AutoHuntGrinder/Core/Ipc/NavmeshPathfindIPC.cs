using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Ipc;

internal sealed class NavmeshPathfindIPC
{
    private const string PathfindFailed = AhgConstants.LogPrefix + " Navmesh Nav.Pathfind failed";

    private static NavmeshPathfindIPC? instance;

    private readonly ICallGateSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>> pathfind;

    private NavmeshPathfindIPC()
    {
        pathfind = Svc.PluginInterface.GetIpcSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>>("vnavmesh.Nav.Pathfind");
    }

    public static NavmeshPathfindIPC Instance => instance ??= new NavmeshPathfindIPC();

    // Plans a route without walking it. Null when the navmesh plugin is missing or refused the query.
    public Task<List<Vector3>>? Pathfind(Vector3 from, Vector3 to, bool fly)
        => IpcGate.Invoke<Task<List<Vector3>>?>(
            pathfind.HasFunction,
            () => pathfind.InvokeFunc(from, to, fly),
            null,
            PathfindFailed);
}
