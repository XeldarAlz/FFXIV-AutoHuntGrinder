using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Tasks;
using Dalamud.Bindings.ImGui;

namespace AutoHuntGrinder.Windows.Sections;

// The running panel, the header and the mini player all draw it, so it is measured once per frame.
internal static class RunWorkload
{
    private static int cachedFrame = -1;
    private static BillSelection.Workload cached;

    public static BillSelection.Workload Measure(AutoHuntController controller)
    {
        var frame = ImGui.GetFrameCount();
        if (frame == cachedFrame)
        {
            return cached;
        }

        cached = controller.Mode == HuntMode.MarkBills ? BillSelection.Measure(controller.ActiveBills) : MeasureObjectives(controller.Objectives);
        cachedFrame = frame;
        return cached;
    }

    public static int CountDone(IReadOnlyList<HuntObjective> objectives)
    {
        var done = 0;
        for (var index = 0; index < objectives.Count; index++)
        {
            var objective = objectives[index];
            if (ObjectiveProgress.Killed(objective) >= ObjectiveProgress.Needed(objective))
            {
                done++;
            }
        }

        return done;
    }

    private static BillSelection.Workload MeasureObjectives(IReadOnlyList<HuntObjective> objectives)
    {
        if (objectives.Count == 0)
        {
            return default;
        }

        ObjectiveProgress.Refresh(objectives[0].Source);
        var killsDone = 0;
        var killsNeeded = 0;
        for (var index = 0; index < objectives.Count; index++)
        {
            var objective = objectives[index];
            var needed = ObjectiveProgress.Needed(objective);
            killsDone += Math.Min(ObjectiveProgress.Killed(objective), needed);
            killsNeeded += needed;
        }

        return new BillSelection.Workload(0, 0, killsDone, killsNeeded);
    }
}
