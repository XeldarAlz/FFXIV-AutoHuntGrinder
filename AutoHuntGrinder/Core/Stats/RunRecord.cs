namespace AutoHuntGrinder.Core.Stats;

[Serializable]
public sealed class RunRecord
{
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }
    public double DurationSeconds { get; set; }

    public int BillsCompleted { get; set; }
    public int MarksKilled { get; set; }
    public int AlliedSeals { get; set; }
    public int CenturioSeals { get; set; }
    public int Nuts { get; set; }

    public string JobAbbreviation { get; set; } = "";
    public List<string> BillNames { get; set; } = [];

    public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);
    public int Seals => AlliedSeals + CenturioSeals;
    public double MarksPerHour => DurationSeconds > 0 ? MarksKilled / (DurationSeconds / 3600.0) : 0;
}
