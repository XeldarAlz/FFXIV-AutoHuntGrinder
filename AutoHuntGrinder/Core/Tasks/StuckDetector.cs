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
}
