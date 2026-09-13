namespace AutoHuntGrinder.Core.Tasks;

public sealed class AutoHuntSession
{
    public DateTime StartedAt = DateTime.UtcNow;
    public string JobAbbreviation = "";

    public int MarksKilled;
    public int BillsCompleted;
    public int AlliedSeals;
    public int CenturioSeals;
    public int Nuts;

    public bool Recorded;
    public bool CompletedByStopCondition;

    private long pausedMs;
    private long pauseStartedAtMs;

    public bool DidNothing => MarksKilled == 0 && BillsCompleted == 0;

    public int Seals => AlliedSeals + CenturioSeals;

    public TimeSpan Elapsed => DateTime.UtcNow - StartedAt - TimeSpan.FromMilliseconds(PausedTotalMs);

    public double MarksPerHour => Elapsed.TotalHours > 0 ? MarksKilled / Elapsed.TotalHours : 0;

    private long PausedTotalMs
        => pausedMs + (pauseStartedAtMs == 0 ? 0 : Environment.TickCount64 - pauseStartedAtMs);

    public void BeginPause()
    {
        if (pauseStartedAtMs != 0)
        {
            return;
        }

        pauseStartedAtMs = Environment.TickCount64;
    }

    public void EndPause()
    {
        if (pauseStartedAtMs == 0)
        {
            return;
        }

        pausedMs += Environment.TickCount64 - pauseStartedAtMs;
        pauseStartedAtMs = 0;
    }
}
