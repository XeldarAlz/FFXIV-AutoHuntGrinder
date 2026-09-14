using AutoHuntGrinder.Core.Travel;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

public abstract partial class AutoCommon
{
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
    internal Task<bool> DismountViaOp(string label, int watchdogMs = DismountWatchdogMs, Func<bool>? abortIf = null)
        => RunCancellable(new MoveOp(move => move.DismountNow()), watchdogMs, label, abortIf);
}
