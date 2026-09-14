using AutoHuntGrinder.Core.Custom;
using AutoHuntGrinder.Core.Hunts;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

// Hunts every enabled mob on the custom list until each reaches its count. The game keeps no counter for these, so the
// kill ledger credits the list while the run lasts, and each pass plans again from what the list still needs.
internal sealed class AutoCustomHunt(AutoHuntSession session, HuntProgress progress) : AutoObjectiveHunt(session, progress)
{
    private const int MaxCustomPasses = 10;

    // Resume builds the next task before the stopped one unwinds, so only the newest may switch the list's tracking off.
    private static AutoCustomHunt? trackingOwner;

    private readonly List<HuntObjective> objectives = [];
    private readonly List<string> leftOutNames = [];
    private uint[] interest = [];

    protected override async Task Execute()
    {
        trackingOwner = this;
        CustomMobList.Begin(Plugin.Kills);
        try
        {
            await Hunt();
        }
        catch (Exception exception)
        {
            RunSession.RecordFault(exception, CancelToken);
            throw;
        }
        finally
        {
            EndTracking();
        }
    }

    private async Task Hunt()
    {
        if (!await PrepareToHunt("Reading your custom list"))
        {
            return;
        }

        while (RunSession.HuntPassesCompleted < MaxCustomPasses)
        {
            var pass = RunSession.HuntPassesCompleted + 1;
            if (!await EnsureStanding())
            {
                return;
            }

            var planned = PlanPass(pass);
            if (planned.Length == 0)
            {
                break;
            }

            var killsBefore = RunSession.MarksKilled;
            if (!await HuntObjectives(planned, pass))
            {
                return;
            }

            RunSession.HuntPassesCompleted++;
            if (RunSession.MarksKilled == killsBefore)
            {
                Diag($"Run: pass {pass} credited no kill; not planning another");
                break;
            }
        }

        Finish();
    }

    private HuntObjective[] PlanPass(int pass)
    {
        Status = "Planning the route";
        CustomMobList.BuildObjectives(objectives);
        SetInterest();
        leftOutNames.Clear();
        for (var objectiveIndex = objectives.Count - 1; objectiveIndex >= 0; objectiveIndex--)
        {
            var objective = objectives[objectiveIndex];
            if (RunSession.IsGivenUp(objective))
            {
                objectives.RemoveAt(objectiveIndex);
                continue;
            }

            if (ObjectivePlanner.CanHunt(objective))
            {
                continue;
            }

            objectives.RemoveAt(objectiveIndex);
            var reason = objective.TerritoryId != 0 ? "has no known spawn points in its pinned zone" : "has no known spawn points";
            if (NoteLeftOut(objective, reason))
            {
                leftOutNames.Add(ObjectiveProgress.Name(objective));
            }
        }

        ReportLeftOut();
        var planned = ObjectivePlanner.Plan(objectives);
        LogPlan(planned, pass);
        return planned;
    }

    // Every mob that still needs kills, before any is left out, so one that falls to a stray pull is credited too.
    private void SetInterest()
    {
        if (interest.Length < objectives.Count)
        {
            interest = new uint[objectives.Count];
        }

        for (var objectiveIndex = 0; objectiveIndex < objectives.Count; objectiveIndex++)
        {
            interest[objectiveIndex] = objectives[objectiveIndex].NameId;
        }

        Plugin.Kills.SetInterest(interest.AsSpan(0, objectives.Count));
    }

    private void ReportLeftOut()
    {
        if (leftOutNames.Count == 0)
        {
            return;
        }

        Svc.Chat.Print($"{AhgConstants.LogPrefix} No spawn points are known for {string.Join(", ", leftOutNames)}, so the run leaves {(leftOutNames.Count == 1 ? "it" : "them")} to you.");
    }

    private void Finish()
    {
        CustomMobList.BuildObjectives(objectives);
        if (objectives.Count == 0)
        {
            Diag("Run: every mob on the custom list reached its count");
            RunSession.CompletedByStopCondition = true;
            Svc.Chat.Print($"{AhgConstants.LogPrefix} Custom list complete: every mob on it reached its count.");
            return;
        }

        var kills = 0;
        for (var objectiveIndex = 0; objectiveIndex < objectives.Count; objectiveIndex++)
        {
            kills += objectives[objectiveIndex].Remaining;
        }

        Diag($"Run: finished with {kills} kill(s) left on {objectives.Count} custom mob(s)");
        Svc.Chat.Print($"{AhgConstants.LogPrefix} Hunt ended with {kills} kill(s) left on {objectives.Count} mob(s). The log has the details.");
    }

    private void EndTracking()
    {
        if (!ReferenceEquals(trackingOwner, this))
        {
            Diag("Run: a newer custom run owns the kill tracking; leaving it on");
            return;
        }

        trackingOwner = null;
        Plugin.Kills.ClearInterest();
        CustomMobList.End();
    }
}
