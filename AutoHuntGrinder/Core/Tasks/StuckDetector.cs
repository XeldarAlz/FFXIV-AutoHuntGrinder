using AutoHuntGrinder.Core.Ipc;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Numerics;

namespace AutoHuntGrinder.Core.Tasks;

internal static class StuckDetector
{
    internal const float StuckMoveThresholdMeters = 1.5f;
    // Long enough that the second or two before a real teleport starts casting cannot trip it.
    internal const int IdleStallTimeoutMs = 8_000;
    internal const int NavWedgeTimeoutMs = 3_000;
    internal const float ProgressEpsilonMeters = 1.0f;
    // A path the pathfinder drops this soon after starting was never walked; something else had hold of the character.
    internal const int FalseStartMs = 1_500;
    internal const float OffMeshProbeMeters = 5f;
    // A second look this much later, so a hop off a ledge or a seam between mesh tiles does not read as a fall through the world.
    internal const int OffMeshConfirmMs = 1_000;
    internal const int AirborneFreezeMs = 2_000;
    private const float AirborneFreezeMeters = 0.25f;

    // Mounting holds the character still for a moment, so it counts; Mounted does not, because a mount snagged on terrain is a real freeze.
    internal static bool IsPositionFrozenLegit()
    {
        var condition = Svc.Condition;
        return condition[ConditionFlag.Casting]
            || condition[ConditionFlag.Casting87]
            || condition[ConditionFlag.Mounting]
            || condition[ConditionFlag.Mounting71]
            || condition[ConditionFlag.BetweenAreas]
            || condition[ConditionFlag.BetweenAreas51]
            || condition[ConditionFlag.OccupiedInCutSceneEvent]
            || condition[ConditionFlag.WatchingCutscene]
            || condition[ConditionFlag.WatchingCutscene78];
    }

    // The world's floor catches a character that fell through the terrain, and a ledge nothing leads to holds one that
    // landed on it. Neither has a reachable mesh point anywhere near, and neither is a stall, because the character can
    // still move; the pathfinder even keeps planning routes from there.
    internal static bool IsOffMesh()
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return false;
        }

        var condition = Svc.Condition;
        if (condition[ConditionFlag.InFlight]
            || condition[ConditionFlag.Swimming]
            || condition[ConditionFlag.Diving]
            || condition[ConditionFlag.Jumping]
            || condition[ConditionFlag.Jumping61]
            || condition[ConditionFlag.BetweenAreas]
            || condition[ConditionFlag.BetweenAreas51])
        {
            return false;
        }

        var navmesh = NavmeshIPC.Instance;
        return navmesh.IsReady() && navmesh.NearestPointReachable(player.Position, OffMeshProbeMeters, OffMeshProbeMeters) is null;
    }

    internal static Func<bool> MoveStallAbort(string label)
    {
        var tracker = new MoveStallTracker();
        return () =>
        {
            var kind = tracker.Check();
            if (kind == StallKind.None)
            {
                return false;
            }

            Svc.Log.Info($"{AhgConstants.LogPrefix} {label} stalled ({kind}); aborting the move");
            return true;
        };
    }

    internal static Func<bool> IdleStallAbort(int timeoutMs)
    {
        Vector3? anchor = null;
        var idleSinceMs = Environment.TickCount64;
        return () =>
        {
            var player = Svc.Objects.LocalPlayer;
            if (player is null)
            {
                return false;
            }

            var now = Environment.TickCount64;
            var position = player.Position;
            if (anchor is null
                || Vector3.Distance(anchor.Value, position) > StuckMoveThresholdMeters
                || NavmeshIPC.Instance.IsBusy()
                || IsPositionFrozenLegit())
            {
                anchor = position;
                idleSinceMs = now;
                return false;
            }

            return now - idleSinceMs >= timeoutMs;
        };
    }

    // The descent the game runs after a dismount in the air is not the navigator's, so the stall tracker never sees it;
    // a position that stops changing while still airborne is the mount pressed against something it cannot land on.
    internal static Func<bool> AirborneFreezeAbort(string label)
    {
        Vector3? anchor = null;
        var frozenSinceMs = Environment.TickCount64;
        return () =>
        {
            var now = Environment.TickCount64;
            if (Svc.Objects.LocalPlayer is not { } player || !Svc.Condition[ConditionFlag.InFlight])
            {
                anchor = null;
                frozenSinceMs = now;
                return false;
            }

            var position = player.Position;
            if (anchor is null || Vector3.Distance(anchor.Value, position) > AirborneFreezeMeters)
            {
                anchor = position;
                frozenSinceMs = now;
                return false;
            }

            if (now - frozenSinceMs < AirborneFreezeMs)
            {
                return false;
            }

            Svc.Log.Info($"{AhgConstants.LogPrefix} {label} froze in the air for {AirborneFreezeMs}ms at {position}; aborting the descent");
            return true;
        };
    }
}
