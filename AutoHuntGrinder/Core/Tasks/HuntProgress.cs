using AutoHuntGrinder.Core.Hunts;

namespace AutoHuntGrinder.Core.Tasks;

// Holds the mark as it was picked; the windows read its kills live from the bill, so nothing here goes stale mid-fight.
internal sealed class HuntProgress
{
    private HuntStop[] route = [];
    private int routeNext;

    public HuntPhase Phase { get; private set; } = HuntPhase.Idle;

    public bool HasMark { get; private set; }

    public HuntBill Bill { get; private set; }

    public HuntTarget Target { get; private set; }

    public ReadOnlySpan<HuntStop> RouteAhead => route.AsSpan(Math.Min(routeNext, route.Length));

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

    public void Reset()
    {
        Phase = HuntPhase.Idle;
        ClearMark();
        ClearRoute();
    }
}
