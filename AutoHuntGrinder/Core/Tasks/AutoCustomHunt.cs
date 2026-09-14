using AutoHuntGrinder.Core.Custom;
using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Marks;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

internal sealed class AutoCustomHunt(AutoHuntSession session, HuntProgress progress) : AutoObjectiveHunt(session, progress)
{
    private const int MaxCustomPasses = 10;

    // Resume builds the next task before the stopped one unwinds, so only the newest may switch the list's tracking off.
    private static AutoCustomHunt? trackingOwner;

    private readonly List<HuntObjective> objectives = [];
    private readonly List<string> leftOutNames = [];
    private readonly List<string> notUpNames = [];
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
            var reason = objective.TerritoryId != 0 ? "has no known spawn points outside FATEs in its pinned zone" : "has no known spawn points outside FATEs";
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

        Svc.Chat.Print($"{AhgConstants.LogPrefix} No spawn points outside FATEs are known for {string.Join(", ", leftOutNames)}, so the run leaves {(leftOutNames.Count == 1 ? "it" : "them")} to you.");
    }

    // An A rank no sweep found is not up and stays down for hours; a mark that appears only after an in-game trigger may
    // stay away for days. Either way the run does not look again.
    private protected override bool LeavesForRun(in HuntObjective objective, string name, MarkOutcome outcome)
    {
        if (outcome != MarkOutcome.NotFound || !HuntMarkRegistry.TryGet(objective.NameId, out var mark))
        {
            return false;
        }

        var onTrigger = HuntMarkRegistry.AppearsOnTrigger(mark.NameId);
        if (mark.Rank != HuntMarkRank.A && !onTrigger)
        {
            return false;
        }

        RunSession.MarksNotUp.Add(objective.NameId);
        Svc.Chat.Print(NotUpLine(name, mark.Rank, onTrigger));
        return true;
    }

    private static string NotUpLine(string name, HuntMarkRank rank, bool onTrigger)
    {
        if (!onTrigger)
        {
            return $"{AhgConstants.LogPrefix} {name} was not found. It may not be up, since A rank marks respawn over hours, so the run skips it for the rest of this run.";
        }

        return rank == HuntMarkRank.S
            ? $"{AhgConstants.LogPrefix} {name} was not found. S rank marks appear only after an in-game trigger, so the run skips it for the rest of this run."
            : $"{AhgConstants.LogPrefix} {name} was not found. It appears only after an in-game trigger, anywhere in its expansion, so the run skips it for the rest of this run.";
    }

    // Mobs the run cannot hunt and hunt marks that were not up do not hold back the after-run action, as marks without
    // spawn data do not in a bill run; a mob given up for any other reason does.
    private void Finish()
    {
        CustomMobList.BuildObjectives(objectives);
        leftOutNames.Clear();
        notUpNames.Clear();
        var kills = 0;
        var huntable = 0;
        for (var objectiveIndex = 0; objectiveIndex < objectives.Count; objectiveIndex++)
        {
            var objective = objectives[objectiveIndex];
            kills += objective.Remaining;
            if (RunSession.MarksNotUp.Contains(objective.NameId))
            {
                notUpNames.Add(ObjectiveProgress.Name(objective));
                continue;
            }

            if (ObjectivePlanner.CanHunt(objective))
            {
                huntable++;
                continue;
            }

            leftOutNames.Add(ObjectiveProgress.Name(objective));
        }

        var left = DescribeLeft();
        if (huntable > 0)
        {
            Diag($"Run: finished with {kills} kill(s) left on {objectives.Count} custom mob(s), {huntable} of them huntable, {notUpNames.Count} hunt mark(s) not found, {leftOutNames.Count} without spawn data");
            Svc.Chat.Print($"{AhgConstants.LogPrefix} Hunt ended with {kills} kill(s) left on {objectives.Count} mob(s).{left} The log has the details.");
            return;
        }

        RunSession.CompletedByStopCondition = true;
        if (left.Length == 0)
        {
            Diag("Run: every mob on the custom list reached its count");
            Svc.Chat.Print($"{AhgConstants.LogPrefix} Custom list complete: every mob on it reached its count.");
            return;
        }

        Diag($"Run: every mob the run can hunt reached its count; left to the player: {notUpNames.Count} hunt mark(s) not found, {leftOutNames.Count} without spawn data");
        Svc.Chat.Print($"{AhgConstants.LogPrefix} Custom list complete as far as the run can go.{left}");
    }

    // Each part opens with a space, so it follows the sentence before it.
    private string DescribeLeft()
    {
        var notUp = notUpNames.Count == 0 ? string.Empty : $" Hunt marks not found: {string.Join(", ", notUpNames)}.";
        var noSpawns = leftOutNames.Count == 0 ? string.Empty : $" Left for you, with no spawn points outside FATEs: {string.Join(", ", leftOutNames)}.";
        return notUp + noSpawns;
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
