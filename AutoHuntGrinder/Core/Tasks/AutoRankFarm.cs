using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Ipc;
using AutoHuntGrinder.Core.Marks;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

internal sealed class AutoRankFarm(AutoHuntSession session, HuntProgress progress) : AutoObjectiveHunt(session, progress)
{
    private const int WorldTravelTimeoutMs = 10 * TimeUnits.MillisecondsPerMinute;
    private static AutoRankFarm? owner;
    private readonly RankFarmState farm = session.RankFarm!;
    private bool ownsTravel;
    private bool wrongWorldReported;

    protected override async Task Execute()
    {
        Release();
        owner = this;
        Plugin.Kills.Credited += OnCredited;
        try
        {
            if (!await PrepareToHunt("Reading your rank farm")) return;
            while (!farm.Finished && !CancelToken.IsCancellationRequested)
            {
                if (!await EnterWorld()) return;
                if (!await EnsureStanding()) return;
                var pending = farm.Pending();
                Plugin.Kills.SetInterest(pending.Select(objective => objective.NameId).ToArray());
                var planned = ObjectivePlanner.Plan(pending);
                Diag($"Rank farm: sweeping {RankFarmSelection.WorldName(farm.World)} ({farm.WorldIndex + 1}/{farm.Worlds.Length}), {planned.Length} mark(s)");
                LogPlan(planned, farm.WorldIndex + 1);
                if (!await HuntObjectives(planned, farm.WorldIndex + 1)) return;
                if (CancelToken.IsCancellationRequested || !CanContinueObjectives()) return;
                Plugin.Kills.ClearTracking();
                RunProgress.ClearObjectives();
                farm.AdvanceWorld();
            }

            if (CancelToken.IsCancellationRequested) return;
            RunSession.CompletedByStopCondition = farm.FailedTargets == 0;
            Svc.Chat.Print($"{AhgConstants.LogPrefix} Rank farm finished: {farm.Worlds.Length} world(s) swept, {farm.TotalKills} kill(s), {farm.FailedTargets} target(s) could not be hunted. Marks not found were left for another run.");
        }
        catch (Exception exception)
        {
            RunSession.RecordFault(exception, CancelToken);
            throw;
        }
        finally
        {
            if (ReferenceEquals(owner, this))
            {
                Release();
                ReleaseCombatMovement("rank-farm");
            }
        }
    }

    private void OnCredited(uint nameId)
    {
        if (!CancelToken.IsCancellationRequested && !ownsTravel && Svc.Objects.LocalPlayer is { } player)
            farm.Credit(player.CurrentWorld.RowId, nameId);
    }

    private protected override bool CanContinueObjectives()
    {
        if (Svc.Objects.LocalPlayer?.CurrentWorld.RowId == farm.World) return true;
        if (!wrongWorldReported && !CancelToken.IsCancellationRequested)
        {
            wrongWorldReported = true;
            Svc.Chat.PrintError($"{AhgConstants.LogPrefix} Rank farm stopped: the character left the planned world, {RankFarmSelection.WorldName(farm.World)}.");
        }
        return false;
    }

    private protected override void OnObjectiveFinished(in HuntObjective objective, MarkOutcome outcome)
    {
        if (outcome is MarkOutcome.Cancelled or MarkOutcome.CombatUnavailable or MarkOutcome.NoCombatJob) return;
        farm.CompleteAttempt(objective.NameId, outcome is not (MarkOutcome.Killed or MarkOutcome.NotFound));
    }

    private async Task<bool> EnterWorld()
    {
        if (Svc.Objects.LocalPlayer?.CurrentWorld.RowId == farm.World) return true;
        var lifestream = LifestreamIPC.Instance;
        if (!lifestream.Available || lifestream.IsBusy())
        {
            Svc.Chat.PrintError($"{AhgConstants.LogPrefix} Rank farm stopped: Lifestream is unavailable or already busy.");
            return false;
        }

        await FightOffAttackers("rank-farm-world");
        if (CancelToken.IsCancellationRequested) return false;
        BossModIPC.Instance.ClearActive();
        NavmeshIPC.Instance.Stop();
        Plugin.Kills.ClearTracking();
        ReportPhase(HuntPhase.Travelling);
        Status = $"Visiting {RankFarmSelection.WorldName(farm.World)} ({farm.WorldIndex + 1}/{farm.Worlds.Length})";
        if (!lifestream.ChangeWorld(farm.World))
        {
            Svc.Chat.PrintError($"{AhgConstants.LogPrefix} Rank farm stopped: Lifestream declined travel to {RankFarmSelection.WorldName(farm.World)}.");
            return false;
        }

        ownsTravel = true;
        try
        {
            var arrived = await WaitUntilTimed(() => lifestream.Available && !lifestream.IsBusy()
                && Svc.Objects.LocalPlayer?.CurrentWorld.RowId == farm.World
                && !Svc.Condition[ConditionFlag.BetweenAreas] && !Svc.Condition[ConditionFlag.BetweenAreas51],
                WorldTravelTimeoutMs, "rank-farm-world");
            if (!arrived && !CancelToken.IsCancellationRequested)
                Svc.Chat.PrintError($"{AhgConstants.LogPrefix} Rank farm stopped: travel to {RankFarmSelection.WorldName(farm.World)} did not finish.");
            if (arrived) ownsTravel = false;
            return arrived && !CancelToken.IsCancellationRequested;
        }
        finally
        {
            if (ownsTravel)
            {
                lifestream.Abort();
                ownsTravel = false;
            }
        }
    }

    // Stop/Pause also call this synchronously; a task might not get another frame to unwind during unload.
    internal static void Release()
    {
        if (owner is not { } active) return;
        owner = null;
        Plugin.Kills.Credited -= active.OnCredited;
        Plugin.Kills.ClearTracking();
        if (active.ownsTravel)
        {
            active.ownsTravel = false;
            LifestreamIPC.Instance.Abort();
        }
    }
}
