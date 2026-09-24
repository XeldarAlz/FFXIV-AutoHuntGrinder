using Newtonsoft.Json;
using System.IO;

namespace AutoHuntGrinder.Core.Stats;

// Kept out of the main config so a long history can't bloat or corrupt the settings file. Disk access is
// best-effort: a failed read or write degrades to an empty or unsaved history instead of throwing.
internal sealed class RunHistory
{
    private const int MaxRecords = 500;
    private const string FileName = "run-history.json";

    public RunHistory()
    {
        Load();
        RecomputeLifetime();
    }

    public List<RunRecord> Records { get; private set; } = [];

    public LifetimeTotals Lifetime { get; private set; }

    private static string FilePath
        => Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, FileName);

    public void Append(RunRecord record)
    {
        Records.Add(record);
        Records.Sort((left, right) => right.EndedAtUtc.CompareTo(left.EndedAtUtc));
        if (Records.Count > MaxRecords)
        {
            Records.RemoveRange(MaxRecords, Records.Count - MaxRecords);
        }

        RecomputeLifetime();
        Save();
    }

    public void Clear()
    {
        Records.Clear();
        RecomputeLifetime();
        Save();
    }

    private void Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
            {
                return;
            }

            var records = JsonConvert.DeserializeObject<List<RunRecord>>(File.ReadAllText(path));
            if (records is not null)
            {
                Records = records;
            }
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "RunHistory load failed; starting empty");
        }
    }

    private void RecomputeLifetime()
    {
        var totals = new LifetimeTotals { Runs = Records.Count };
        for (var index = 0; index < Records.Count; index++)
        {
            var record = Records[index];
            totals.Bills += record.BillsCompleted;
            totals.Marks += record.MarksKilled;
            totals.Seals += record.Seals;
            totals.Nuts += record.Nuts;
            totals.Seconds += record.DurationSeconds;
        }

        Lifetime = totals;
    }

    private void Save()
    {
        try
        {
            var directory = Plugin.PluginInterface.ConfigDirectory;
            if (!directory.Exists)
            {
                directory.Create();
            }

            File.WriteAllText(FilePath, JsonConvert.SerializeObject(Records, Formatting.Indented));
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "RunHistory save failed");
        }
    }

    public struct LifetimeTotals
    {
        public int Runs;
        public int Bills;
        public int Marks;
        public int Seals;
        public int Nuts;
        public double Seconds;

        public readonly double MarksPerHour => Seconds > 0 ? Marks / (Seconds / 3600.0) : 0;
        public readonly TimeSpan Duration => TimeSpan.FromSeconds(Seconds);
    }
}
