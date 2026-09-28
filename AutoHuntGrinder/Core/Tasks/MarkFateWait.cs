namespace AutoHuntGrinder.Core.Tasks;

// One FATE mark's wait over a pass. Only time spent waiting in its zone counts against the budget, so a look-in between
// other marks costs it little and a fight inside the running FATE costs it nothing.
internal sealed class MarkFateWait(int budgetMs, bool rotates)
{
    // A look-in polls the zone's FATE list for a few seconds, in case it is still filling in after the teleport.
    private const int LookInMs = 10_000;

    public int RemainingMs { get; private set; } = budgetMs;

    // A rotating wait leaves when the FATE is not up and comes back later; otherwise one visit waits out the budget.
    public bool Rotates { get; } = rotates;

    public int VisitMs { get; private set; } = budgetMs;

    // A look-in reads the FATE list where the character lands; a dwell walks to where the FATE starts and waits there.
    public bool Dwells { get; private set; } = true;

    public long NextLookAt { get; set; }

    public bool Spent => RemainingMs <= 0;

    public void PlanLookIn()
    {
        VisitMs = LookInMs;
        Dwells = false;
    }

    public void PlanDwell(int dwellMs)
    {
        VisitMs = dwellMs;
        Dwells = true;
    }

    public void PlanWaitOut() => PlanDwell(RemainingMs);

    public void Spend(long elapsedMs) => RemainingMs = (int)Math.Max(0L, RemainingMs - elapsedMs);
}
