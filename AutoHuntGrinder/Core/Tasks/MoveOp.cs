using AutoHuntGrinder.Core.Ipc;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;
using AutoTask = clib.Services.AutoTask;
using OnDispose = clib.Services.OnDispose;

namespace AutoHuntGrinder.Core.Tasks;

// One movement or teleport run as its own task, so it owns its cancellation source. The parent run can cancel exactly
// this operation, and cancelling fires its registered cleanups instead of leaving it running. It builds on the plain
// task base rather than the library's TaskBase, whose every instance installs two native movement hooks.
internal sealed partial class MoveOp(Func<MoveOp, Task> body) : AutoTask
{
    // The task runner swallows exceptions, so a failed move would otherwise look like a clean arrival.
    public Exception? Fault { get; private set; }

    protected override async Task Execute()
    {
        try
        {
            await body(this);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Fault = exception;
        }
    }

    public Task MoveInZone(Vector3 destination, MovementConfig config, Func<bool>? stopCondition)
        => Navigate(destination, config, stopCondition);

    // Under water a pathfind-and-move request asks for a floor route, and there is no floor to start one from
    // mid-water, so a dive asks for the pathfinder's flight route itself and follows that.
    public async Task DiveInZone(Vector3 destination, float tolerance, Func<bool>? stopCondition)
    {
        var navmesh = NavmeshIPC.Instance;
        var pending = Svc.Objects.LocalPlayer is { } player ? NavmeshPathfindIPC.Instance.Pathfind(player.Position, destination, fly: true) : null;
        if (pending is null)
        {
            throw new InvalidOperationException("the pathfinder did not take the route request");
        }

        await WaitUntil(() => pending.IsCompleted, "DiveRoute");
        if (!pending.IsCompletedSuccessfully || pending.Result is not { Count: > 0 } waypoints)
        {
            throw new InvalidOperationException("the pathfinder has no route through the water");
        }

        using (new OnDispose(StopOwnedPath))
        {
            navmesh.MoveAlong(waypoints, fly: true);
            await NextFrame();
            await WaitWhile(() => !AutoCommon.WithinReach(destination, tolerance) && stopCondition?.Invoke() != true && navmesh.IsRunning(), "Dive");
        }
    }

    public Task Teleport(uint territoryId, Vector3 destination, bool allowSameZoneTeleport)
        => TeleportToTerritory(territoryId, destination, allowSameZoneTeleport);

    public Task Aethernet(uint territoryId, Vector3 destination)
        => UseAethernet(territoryId, destination);

    public Task Interact(IGameObject gameObject, Func<bool>? waitUntil, UiSkipOptions skip)
        => InteractWith(gameObject, waitUntil, skip);

    public Task DismountNow() => Dismount();
}
