using AutoHuntGrinder.Core.Hunts;

namespace AutoHuntGrinder.Core.Tasks;

// Holds the mark or objective as it was picked; the windows read its kills live from its source, so nothing here goes
// stale mid-fight.
internal sealed class HuntProgress
{
    private HuntStop[] route = [];
    private int routeNext;
    private HuntObjective[] objectives = [];
    private int objectiveNext;

    public HuntPhase Phase { get; private set; } = HuntPhase.Idle;

    public bool HasMark { get; private set; }

    public HuntBill Bill { get; private set; }

    public HuntTarget Target { get; private set; }

    public ReadOnlySpan<HuntStop> RouteAhead => route.AsSpan(Math.Min(routeNext, route.Length));

    public bool HasObjective { get; private set; }

    public HuntObjective Objective { get; private set; }

    public IReadOnlyList<HuntObjective> Objectives => objectives;

    public ReadOnlySpan<HuntObjective> ObjectivesAhead => objectives.AsSpan(Math.Min(objectiveNext, objectives.Length));

    public void SetPhase(HuntPhase phase) => Phase = phase;

    public void SetMark(HuntBill bill, HuntTarget target)
    {
        Bill = bill;
        Target = target;
        HasMark = true;
    }

    public void ClearMark()
    {
        HasMark = false;
        Bill = default;
        Target = default;
    }

    public void SetRoute(List<HuntStop> planned)
    {
        route = [.. planned];
        routeNext = 0;
    }

    public void SetRouteStop(int stopIndex) => routeNext = stopIndex;

    public void ClearRoute()
    {
        route = [];
        routeNext = 0;
    }

    public void SetObjective(in HuntObjective objective)
    {
        Objective = objective;
        HasObjective = true;
    }

    public void ClearObjective()
    {
        HasObjective = false;
        Objective = default;
    }

    public void SetObjectives(HuntObjective[] planned)
    {
        objectives = [.. planned];
        objectiveNext = 0;
    }

    public void SetObjectiveStop(int objectiveIndex) => objectiveNext = objectiveIndex;

    public void RefreshObjectives()
    {
        for (var objectiveIndex = 0; objectiveIndex < objectives.Length; objectiveIndex++)
        {
            objectives[objectiveIndex] = ObjectiveProgress.Live(objectives[objectiveIndex]);
        }
    }

    public void ClearObjectives()
    {
        objectives = [];
        objectiveNext = 0;
    }

    public void Reset()
    {
        Phase = HuntPhase.Idle;
        ClearMark();
        ClearRoute();
        ClearObjective();
        ClearObjectives();
    }
}
