using AutoHuntGrinder.Core.Travel;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int WalkAttempts = 3;
    // The movement library parks a mount a few metres off the point before its own arrival check runs.
    private const float WalkArrivalSlackMeters = 2f;
    // A flying mount stops a few metres above a ground point rather than landing on it.
    private const float FlightHoverSlackMeters = 6f;
    private const int DismountWatchdogMs = 30_000;

    internal static bool WithinReach(Vector3 destination, float tolerance)
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return false;
        }

        var position = player.Position;
        if (!Svc.Condition[ConditionFlag.InFlight])
        {
            return Vector3.Distance(position, destination) <= tolerance + WalkArrivalSlackMeters;
        }

        return GroundDistance.Between(position, destination) <= tolerance + WalkArrivalSlackMeters
            && MathF.Abs(position.Y - destination.Y) <= tolerance + FlightHoverSlackMeters;
    }

    // Its own cancellable operation, so a landing that never happens cannot park the run.
    internal Task<bool> DismountViaOp(string label)
        => RunCancellable(new MoveOp(move => move.DismountNow()), DismountWatchdogMs, label);

    // Re-paths from wherever the character stopped, because re-issuing the same route tends to run into the same wall.
    internal async Task<bool> WalkWithRetries(Func<MoveOp> createMove, int watchdogMs, string label, Func<bool> arrived)
    {
        for (var attempt = 1; attempt <= WalkAttempts; attempt++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            var scope = $"{label}#{attempt}";
            var move = createMove();
            var completed = await RunCancellable(move, watchdogMs, scope, StuckDetector.MoveStallAbort(scope));
            if (arrived())
            {
                return true;
            }

            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            if (move.Fault is { } fault)
            {
                Diag($"{scope} faulted: {fault.Message}; re-pathing from here");
            }
            else if (completed)
            {
                Diag($"{scope} ended short of the destination; re-pathing from here");
            }
            else
            {
                Diag($"{scope} stalled; re-pathing from here");
            }
        }

        return arrived();
    }
}
