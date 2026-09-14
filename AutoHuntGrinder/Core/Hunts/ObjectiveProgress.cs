using AutoHuntGrinder.Core.Custom;
using AutoHuntGrinder.Core.HuntingLog;

namespace AutoHuntGrinder.Core.Hunts;

// Reads an objective's live state from its own source, so a snapshot taken when a pass was planned never goes stale.
internal static class ObjectiveProgress
{
    private const int SourceKeyBits = 16;
    private const string CustomListName = "your custom list";

    public static ushort Killed(in HuntObjective objective) => objective.Source switch
    {
        ObjectiveSource.HuntingLog => HuntingLogReader.KilledForKey(objective.SourceKey),
        ObjectiveSource.Custom => CustomEntry(objective)?.Killed ?? objective.Killed,
        _ => objective.Killed,
    };

    public static ushort Needed(in HuntObjective objective) => objective.Source switch
    {
        ObjectiveSource.HuntingLog => HuntingLogRegistry.TryFindTarget(objective.SourceKey, out var target, out _) ? target.Needed : objective.Needed,
        ObjectiveSource.Custom => CustomEntry(objective)?.Needed ?? objective.Needed,
        _ => objective.Needed,
    };

    // False once kills can no longer count: the log is closed to this character, or the mob left the list or was switched off.
    public static bool IsTracked(in HuntObjective objective) => objective.Source switch
    {
        ObjectiveSource.HuntingLog => HuntingLogRegistry.TryParseSourceKey(objective.SourceKey, out var slot, out _, out _, out _)
            && HuntingLogReader.Status(slot) != HuntingLogStatus.Unavailable,
        ObjectiveSource.Custom => CustomEntry(objective) is { Enabled: true },
        _ => false,
    };

    public static string Name(in HuntObjective objective)
    {
        if (objective.Source == ObjectiveSource.HuntingLog && HuntingLogRegistry.TryFindTarget(objective.SourceKey, out _, out var targetIndex))
        {
            var name = HuntingLogRegistry.TargetName(targetIndex);
            if (name.Length > 0)
            {
                return name;
            }
        }

        return CustomMobCatalog.NameOf(objective.NameId);
    }

    public static string SourceName(in HuntObjective objective)
    {
        if (objective.Source != ObjectiveSource.HuntingLog)
        {
            return CustomListName;
        }

        return HuntingLogRegistry.TryParseSourceKey(objective.SourceKey, out var slot, out _, out _, out _)
            ? $"the {HuntingLogRegistry.BookName(slot)} Hunting Log"
            : "the Hunting Log";
    }

    // For log lines only.
    public static string Describe(in HuntObjective objective)
    {
        if (objective.Source == ObjectiveSource.HuntingLog)
        {
            return HuntingLogRegistry.TryParseSourceKey(objective.SourceKey, out var slot, out _, out _, out _)
                ? HuntingLogReader.Status(slot).ToString()
                : "an unknown slot";
        }

        return CustomEntry(objective) switch
        {
            null => "no longer listed",
            { Enabled: false } => "switched off",
            _ => "listed",
        };
    }

    public static HuntObjective Live(in HuntObjective objective) => objective with { Killed = Killed(objective), Needed = Needed(objective) };

    public static void Refresh(ObjectiveSource source, bool force = false)
    {
        if (source == ObjectiveSource.HuntingLog)
        {
            HuntingLogReader.Refresh(force);
        }
    }

    // Unique across sources, so one set can hold objectives from any mode.
    public static uint Key(in HuntObjective objective) => (uint)objective.Source << SourceKeyBits | objective.SourceKey;

    // The list index comes first, and the name check catches an entry removed or reordered since the pass was planned.
    private static CustomMobEntry? CustomEntry(in HuntObjective objective)
    {
        var entries = Plugin.Instance.Configuration.CustomMobs;
        int listIndex = objective.SourceKey;
        if (listIndex < entries.Count && entries[listIndex].NameId == objective.NameId)
        {
            return entries[listIndex];
        }

        return CustomMobList.Find(objective.NameId);
    }
}
