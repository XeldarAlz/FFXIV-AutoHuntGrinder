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
    private const int HumanizeTeleportWatchdogMs = 60_000;
    private const int HumanizeWalkWatchdogMs = 90_000;
    private const int HumanizeNavmeshWaitMs = 60_000;
    private const int HumanizePlayerWaitMs = 500;
    private const int HumanizeIdleWithoutRouteMs = 1_500;
    private const int HumanizeMinHopBudgetMs = 4_000;
    private const int HumanizeCandidateAttempts = 8;
    private const float HumanizeArrivalToleranceMeters = 4f;
    private const float HumanizeCandidateHalfExtentXZ = 10f;
    private const float HumanizeCandidateHalfExtentY = 5f;
    // A candidate the mesh snaps back to within half the shortest walk is not worth the trip.
    private const float HumanizeMinSnapFraction = 0.5f;
    private const float HumanizeMaxDetourRatio = 2f;

    // A break that never reached the city is not counted as taken.
    protected async Task<bool> TakeCityBreak(uint cityTerritoryId, int durationMs)
    {
        var cityName = TerritoryNames.Of(cityTerritoryId);
        Diag($"Humanize: {durationMs / TimeUnits.MillisecondsPerSecond}s break in {cityName} ({cityTerritoryId})");
        Svc.Chat.Print($"{AhgConstants.LogPrefix} Humanize: taking a ~{Math.Max(1, durationMs / TimeUnits.MillisecondsPerMinute)}m break in {cityName}.");

        if (Svc.ClientState.TerritoryType != cityTerritoryId)
        {
            var reached = false;
            await RunWithStatusPinned(
                $"Teleporting to {cityName}",
                async () => reached = await TeleportToTerritory(cityTerritoryId, Vector3.Zero, "humanize-teleport", HumanizeTeleportWatchdogMs));
            if (!reached)
            {
                Diag($"Humanize: could not reach {cityName} (still in territory {Svc.ClientState.TerritoryType}); no break taken");
                return false;
            }
        }

        await WaitForNavmeshReady(HumanizeNavmeshWaitMs);
        if (CancelToken.IsCancellationRequested)
        {
            return true;
        }

        if (Svc.Condition[ConditionFlag.Mounted])
        {
            await SafeDismount("humanize-dismount");
        }

        var walks = await WanderUntil(cityTerritoryId, cityName, Environment.TickCount64 + durationMs);
        Diag($"Humanize: break in {cityName} over after {walks} walk(s)");
        return true;
    }

    private async Task<int> WanderUntil(uint cityTerritoryId, string cityName, long deadline)
    {
        var walks = 0;
        while (Environment.TickCount64 < deadline && !CancelToken.IsCancellationRequested)
        {
            if (Svc.ClientState.TerritoryType != cityTerritoryId)
            {
                Diag($"Humanize: now in territory {Svc.ClientState.TerritoryType}, not {cityTerritoryId}; ending the break early");
                break;
            }

            if (Svc.Objects.LocalPlayer is not { } player)
            {
                await DelayMs(HumanizePlayerWaitMs);
                continue;
            }

            var from = player.Position;
            var destination = await PickWanderDestination(from);
            if (CancelToken.IsCancellationRequested)
            {
                break;
            }

            if (destination is not { } spot)
            {
                Status = $"Idling in {cityName}";
                await IdleUntil(HumanizeIdleWithoutRouteMs, deadline);
                continue;
            }

            var budgetMs = (int)Math.Min(HumanizeWalkWatchdogMs, deadline - Environment.TickCount64);
            if (budgetMs < HumanizeMinHopBudgetMs)
            {
                break;
            }

            walks++;
            Status = $"Wandering in {cityName} (~{(deadline - Environment.TickCount64) / TimeUnits.MillisecondsPerSecond}s left)";
            Diag($"Humanize walk {walks}: {Vector3.Distance(from, spot):F0}m to {spot}");
            var scope = $"humanize-walk#{walks}";
            var move = new MoveOp(operation => operation.MoveInZone(
                spot,
                MovementConfig.Default.WithTolerance(HumanizeArrivalToleranceMeters),
                () => Environment.TickCount64 >= deadline));
            await RunCancellable(move, budgetMs, scope, StuckDetector.MoveStallAbort(scope));
            if (CancelToken.IsCancellationRequested || Environment.TickCount64 >= deadline)
            {
                break;
            }

            await IdleUntil(RollWanderPauseMs(), deadline);
        }

        return walks;
    }

    private async Task IdleUntil(int pauseMs, long deadline)
    {
        var remainingMs = Math.Max(0L, deadline - Environment.TickCount64);
        await DelayMs((int)Math.Min(pauseMs, remainingMs));
    }

    private async Task<Vector3?> PickWanderDestination(Vector3 from)
    {
        var configuration = Plugin.Instance.Configuration;
        var shortest = (float)Math.Max(1, configuration.HumanizerWanderMinMeters);
        var longest = Math.Max(shortest, configuration.HumanizerWanderMaxMeters);
        for (var attempt = 0; attempt < HumanizeCandidateAttempts; attempt++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return null;
            }

            var angle = Random.Shared.NextSingle() * MathF.Tau;
            var radius = shortest + Random.Shared.NextSingle() * (longest - shortest);
            var candidate = new Vector3(from.X + MathF.Cos(angle) * radius, from.Y, from.Z + MathF.Sin(angle) * radius);
            var snapped = NavmeshIPC.Instance.NearestPointReachable(candidate, HumanizeCandidateHalfExtentXZ, HumanizeCandidateHalfExtentY);
            if (snapped is not { } reachable || Vector3.Distance(reachable, from) < shortest * HumanizeMinSnapFraction)
            {
                continue;
            }

            var rejection = RejectWanderRoute(await ProbeGroundRoute(from, reachable), Vector3.Distance(from, reachable));
            if (rejection is null)
            {
                return reachable;
            }

            Diag($"Humanize: candidate {reachable} rejected, {rejection}");
        }

        return null;
    }

    private static string? RejectWanderRoute(in GroundRoute route, float straight) => route.Kind switch
    {
        GroundRouteKind.Unknown => "no route",
        GroundRouteKind.Partial => $"the route stops {route.ShortfallMeters:F1}m short (partial path)",
        _ => route.LengthMeters > straight * HumanizeMaxDetourRatio ? $"a {route.LengthMeters:F0}m detour for {straight:F0}m straight" : null,
    };

    // Read on every walk, so a settings change during a break applies to the next pause.
    private static int RollWanderPauseMs()
    {
        var configuration = Plugin.Instance.Configuration;
        var shortest = Math.Max(0, configuration.HumanizerPauseMinSeconds);
        var longest = Math.Max(shortest, configuration.HumanizerPauseMaxSeconds);
        return Random.Shared.Next(shortest, longest + 1) * TimeUnits.MillisecondsPerSecond;
    }
}
