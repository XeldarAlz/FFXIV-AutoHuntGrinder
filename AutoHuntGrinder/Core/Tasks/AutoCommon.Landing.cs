using AutoHuntGrinder.Core.Ipc;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int MaxLandingSpots = 3;
    private const float LandingRingRadiusMeters = 6f;
    private const int LandingRingPoints = 6;
    private const int LandingCandidateCount = 1 + LandingRingPoints;
    private const float LandingFloorHalfExtentMeters = 3f;
    // Probed from well above, so the floor found is the top layer under the spot and not a cave or a tunnel below it.
    private const float LandingProbeLiftMeters = 30f;
    private const float LandingArriveMeters = 1.5f;
    private const int LandingFlightWatchdogMs = 20_000;
    private const int LandingDismountWatchdogMs = 6_000;
    private const int GroundDismountWatchdogMs = 8_000;
    private const float LandingBackOffMeters = 8f;
    private const int LandingBackOffMs = 1_500;

    private readonly Vector3[] landingSpots = new Vector3[LandingCandidateCount];

    protected async Task<bool> SafeDismount(string scope)
    {
        if (!Svc.Condition[ConditionFlag.Mounted])
        {
            return true;
        }

        if (!Svc.Condition[ConditionFlag.InFlight])
        {
            return await GroundDismount(scope);
        }

        var here = Svc.Objects.LocalPlayer?.Position ?? Vector3.Zero;
        return await LandAndDismount(here, scope);
    }

    // From the air, the movement library's dismount descends straight down from wherever the flight ended; over a tent,
    // a canopy or a cliff face that hovers for good or drops the character through the world. So the landing is aimed
    // at a landable floor point first, and a descent that stops moving is cut short and tried from the next spot.
    protected async Task<bool> LandAndDismount(Vector3 around, string scope)
    {
        if (!Svc.Condition[ConditionFlag.Mounted])
        {
            return true;
        }

        if (!Svc.Condition[ConditionFlag.InFlight])
        {
            return await GroundDismount(scope);
        }

        Status = "Landing";
        var found = FindLandingSpots(around, landingSpots);
        if (found == 0)
        {
            Diag($"{scope}: no landable floor within {LandingRingRadiusMeters:F0}m of {FormatPosition(around)}; descending where the flight ended");
            return await DescendAndDismount(scope);
        }

        var attempts = Math.Min(found, MaxLandingSpots);
        for (var spotIndex = 0; spotIndex < attempts; spotIndex++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            var spot = landingSpots[spotIndex];
            var legScope = $"{scope}-land#{spotIndex + 1}";
            Diag($"{legScope}: flying {DistanceTo(spot):F0}m to a landable spot at {FormatPosition(spot)}");
            var flight = new MoveOp(move => move.MoveInZone(spot, walkMovement.WithTolerance(LandingArriveMeters), null));
            await RunCancellable(flight, LandingFlightWatchdogMs, legScope, StuckDetector.MoveStallAbort(legScope));
            if (flight.Fault is { } fault)
            {
                Diag($"{legScope}: the landing flight faulted: {fault.Message}");
            }

            if (await DescendAndDismount(legScope))
            {
                return true;
            }

            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            await BackOffInFlight(legScope);
        }

        Warn($"{scope}: could not land near {FormatPosition(around)} after {attempts} spot(s) ({ConditionTag()})");
        return false;
    }

    private async Task<bool> DescendAndDismount(string scope)
    {
        if (!Svc.Condition[ConditionFlag.Mounted])
        {
            return true;
        }

        if (!Svc.Condition[ConditionFlag.InFlight])
        {
            return await GroundDismount(scope);
        }

        await DismountViaOp(scope, LandingDismountWatchdogMs, StuckDetector.AirborneFreezeAbort(scope));
        if (!Svc.Condition[ConditionFlag.Mounted])
        {
            return true;
        }

        Diag($"{scope}: the descent did not land ({ConditionTag()})");
        CancelDescent();
        return false;
    }

    private async Task<bool> GroundDismount(string scope)
    {
        await DismountViaOp(scope, GroundDismountWatchdogMs);
        if (!Svc.Condition[ConditionFlag.Mounted])
        {
            return true;
        }

        Diag($"{scope}: still mounted after the dismount ({ConditionTag()})");
        return false;
    }

    // Pressing jump in the air ends the game's own descent, which otherwise carries on after the operation is cancelled
    // and reads as player input to the pathfinder, so every path queued meanwhile would be dropped at once.
    private void CancelDescent()
    {
        if (Svc.Condition[ConditionFlag.InFlight])
        {
            UseGeneralAction(JumpGeneralActionId);
        }
    }

    // Straight up is the one direction known to be clear, since the flight came from there.
    private async Task BackOffInFlight(string scope)
    {
        if (Svc.Objects.LocalPlayer is not { } player || !Svc.Condition[ConditionFlag.InFlight])
        {
            return;
        }

        var above = player.Position with { Y = player.Position.Y + LandingBackOffMeters };
        Diag($"{scope}: backing off {LandingBackOffMeters:F0}m upward");
        NavmeshIPC.Instance.MoveAlong([above], fly: true);
        await DelayMs(LandingBackOffMs);
        NavmeshIPC.Instance.Stop();
    }

    // The spot itself first, then a ring around it, each snapped to the highest landable floor under it.
    private static int FindLandingSpots(Vector3 around, Vector3[] spots)
    {
        var navmesh = NavmeshIPC.Instance;
        var found = 0;
        if (LandableFloor(navmesh, around) is { } center)
        {
            spots[found++] = center;
        }

        for (var step = 0; step < LandingRingPoints; step++)
        {
            var angle = MathF.Tau * step / LandingRingPoints;
            var candidate = around + new Vector3(MathF.Cos(angle) * LandingRingRadiusMeters, 0f, MathF.Sin(angle) * LandingRingRadiusMeters);
            if (LandableFloor(navmesh, candidate) is { } floor)
            {
                spots[found++] = floor;
            }
        }

        return found;
    }

    private static Vector3? LandableFloor(NavmeshIPC navmesh, Vector3 point)
        => navmesh.PointOnFloor(point with { Y = point.Y + LandingProbeLiftMeters }, allowUnlandable: false, LandingFloorHalfExtentMeters);
}
