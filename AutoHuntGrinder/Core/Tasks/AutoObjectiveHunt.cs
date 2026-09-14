using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Ipc;
using AutoHuntGrinder.Core.Travel;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

internal abstract class AutoObjectiveHunt(AutoHuntSession session, HuntProgress progress) : AutoCommon
{
    private const int CharacterReadyWaitMs = 30_000;

    private protected AutoHuntSession RunSession { get; } = session;

    private protected HuntProgress RunProgress { get; } = progress;

    private protected override void OnMarkPhaseChanged(HuntPhase phase)
    {
        if (phase != HuntPhase.Idle)
        {
            ReportPhase(phase);
        }
    }

    private protected async Task<bool> PrepareToHunt(string readingLabel)
    {
        Status = readingLabel;
        if (!await WaitUntilTimed(CharacterLoaded, CharacterReadyWaitMs, "objective-run-ready"))
        {
            if (!CancelToken.IsCancellationRequested)
            {
                Warn("Run: the character never loaded, so the run could not start");
                Svc.Chat.PrintError($"{AhgConstants.LogPrefix} The character did not finish loading, so the hunt stops.");
            }

            return false;
        }

        if (BossModIPC.Instance.IsAvailable)
        {
            return true;
        }

        Warn("Run: the combat plugin is not answering; not starting");
        Svc.Chat.PrintError($"{AhgConstants.LogPrefix} The combat plugin is not answering, so no monster can be fought. Check the Plugins page.");
        return false;
    }

    private protected async Task<bool> HuntObjectives(HuntObjective[] planned, int pass)
    {
        ReportObjectives(planned);
        for (var objectiveIndex = 0; objectiveIndex < planned.Length; objectiveIndex++)
        {
            ReportObjectiveStop(objectiveIndex);
            var objective = ObjectiveProgress.Live(planned[objectiveIndex]);
            if (objective.Done || RunSession.IsGivenUp(objective))
            {
                continue;
            }

            ReportPhase(HuntPhase.Upkeep);
            if (await RunUpkeep())
            {
                Diag("Run: upkeep ran; the next target starts from wherever it left the character");
            }

            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            var name = ObjectiveProgress.Name(objective);
            Diag($"Run: pass {pass}, target {objectiveIndex + 1}/{planned.Length}: {name} for {ObjectiveProgress.SourceName(objective)}");
            ReportObjective(objective);
            ReportPhase(HuntPhase.Travelling);
            var killsBefore = RunSession.MarksKilled;
            var outcome = await HuntQuarry(objective);
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            RunSession.Sample();
            CountKills(RunSession.MarksKilled - killsBefore);
            ReportNoObjective();
            if (!await EnsureStanding())
            {
                return false;
            }

            if (!Settle(objective, name, outcome))
            {
                return false;
            }
        }

        ReportObjectiveStop(planned.Length);
        return true;
    }

    private protected void LogPlan(HuntObjective[] planned, int pass)
    {
        Diag($"Run: pass {pass} plans {planned.Length} target(s)");
        for (var objectiveIndex = 0; objectiveIndex < planned.Length; objectiveIndex++)
        {
            var objective = planned[objectiveIndex];
            Diag($"Plan {objectiveIndex + 1}/{planned.Length}: {ObjectiveProgress.Name(objective)} for {ObjectiveProgress.SourceName(objective)}, {objective.Killed}/{objective.Needed} in {TerritoryNames.Of(objective.TerritoryId)} (territory {objective.TerritoryId})");
        }
    }

    // Logged once per run, and true only that first time, so the caller can gather names for a single chat line.
    private protected bool NoteLeftOut(in HuntObjective objective, string reason)
    {
        if (!RunSession.Notice(ObjectiveProgress.Key(objective)))
        {
            return false;
        }

        Diag($"Run: {ObjectiveProgress.Name(objective)} for {ObjectiveProgress.SourceName(objective)} {reason}; the run leaves it to you");
        return true;
    }

    // Lets a mode leave an objective for the rest of the run on an outcome the shared rules would let the next pass retry.
    private protected virtual bool LeavesForRun(in HuntObjective objective, string name, MarkOutcome outcome) => false;

    // A Stop or Pause cancels the task before its last lines run, and those lines must not overwrite what the controller set.
    private protected void ReportPhase(HuntPhase phase)
    {
        if (!CancelToken.IsCancellationRequested)
        {
            RunProgress.SetPhase(phase);
        }
    }

    private bool Settle(in HuntObjective objective, string name, MarkOutcome outcome)
    {
        switch (outcome)
        {
            case MarkOutcome.Killed:
                Diag($"Run: {name} is done for {ObjectiveProgress.SourceName(objective)}");
                return true;
            case MarkOutcome.Cancelled:
                return false;
            case MarkOutcome.CombatUnavailable:
                Svc.Chat.PrintError($"{AhgConstants.LogPrefix} The combat plugin stopped answering, so the hunt stops.");
                return false;
            case MarkOutcome.Died:
                GiveUp(objective, name, outcome);
                Svc.Chat.PrintError($"{AhgConstants.LogPrefix} Knocked out too often hunting {name}; skipping it for the rest of this run.");
                return true;
            case MarkOutcome.KillsNotCounted:
                GiveUp(objective, name, outcome);
                Svc.Chat.PrintError($"{AhgConstants.LogPrefix} Kills on {name} did not count toward {ObjectiveProgress.SourceName(objective)}; skipping it for the rest of this run.");
                return true;
            case MarkOutcome.Unsupported:
                GiveUp(objective, name, outcome);
                return true;
            default:
                if (LeavesForRun(objective, name, outcome))
                {
                    GiveUp(objective, name, outcome);
                    return true;
                }

                Diag($"Run: {name} ended {outcome}; moving on, and the next pass may try it again");
                return true;
        }
    }

    private void GiveUp(in HuntObjective objective, string name, MarkOutcome outcome)
    {
        RunSession.GiveUp(objective);
        Diag($"Run: giving up on {name} for the rest of this run ({outcome})");
    }

    private void CountKills(int kills)
    {
        for (var kill = 0; kill < kills; kill++)
        {
            NoteMarkKilled();
        }
    }

    private void ReportObjective(in HuntObjective objective)
    {
        if (!CancelToken.IsCancellationRequested)
        {
            RunProgress.SetObjective(objective);
        }
    }

    private void ReportNoObjective()
    {
        if (CancelToken.IsCancellationRequested)
        {
            return;
        }

        RunProgress.ClearObjective();
        RunProgress.RefreshObjectives();
    }

    private void ReportObjectives(HuntObjective[] planned)
    {
        if (!CancelToken.IsCancellationRequested)
        {
            RunProgress.SetObjectives(planned);
        }
    }

    private void ReportObjectiveStop(int objectiveIndex)
    {
        if (!CancelToken.IsCancellationRequested)
        {
            RunProgress.SetObjectiveStop(objectiveIndex);
        }
    }

    private static bool CharacterLoaded() => Svc.ClientState.IsLoggedIn && Svc.Objects.LocalPlayer is not null;
}
