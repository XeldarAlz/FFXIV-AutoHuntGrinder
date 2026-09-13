using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Kills;
using ECommons.DalamudServices;

namespace AutoHuntGrinder.Core.Custom;

internal static class CustomMobList
{
    private static KillLedger? activeLedger;

    private static Configuration Configuration => Plugin.Instance.Configuration;

    private static List<CustomMobEntry> Entries => Configuration.CustomMobs;

    public static bool Add(uint nameId)
    {
        if (nameId == 0 || Contains(nameId))
        {
            return false;
        }

        Entries.Add(new CustomMobEntry { NameId = nameId });
        Configuration.Save();
        return true;
    }

    public static void Remove(int index)
    {
        if (!InRange(index))
        {
            return;
        }

        Entries.RemoveAt(index);
        Configuration.Save();
    }

    public static void Reset(int index)
    {
        if (!InRange(index) || Entries[index].Killed == 0)
        {
            return;
        }

        Entries[index].Killed = 0;
        Configuration.Save();
    }

    public static void ResetAll()
    {
        var entries = Entries;
        var changed = false;
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].Killed == 0)
            {
                continue;
            }

            entries[index].Killed = 0;
            changed = true;
        }

        if (changed)
        {
            Configuration.Save();
        }
    }

    public static bool Contains(uint nameId)
    {
        var entries = Entries;
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].NameId == nameId)
            {
                return true;
            }
        }

        return false;
    }

    public static ushort Killed(int index) => InRange(index) ? Entries[index].Killed : (ushort)0;

    public static void BuildObjectives(List<HuntObjective> destination)
    {
        destination.Clear();
        var entries = Entries;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (!NeedsKills(entry))
            {
                continue;
            }

            destination.Add(new HuntObjective(ObjectiveSource.Custom, (ushort)index, entry.NameId, entry.PinnedTerritoryId, entry.Needed, entry.Killed));
        }
    }

    public static void Begin(KillLedger ledger)
    {
        if (activeLedger == ledger)
        {
            return;
        }

        if (activeLedger is not null)
        {
            activeLedger.Credited -= OnCredited;
        }

        activeLedger = ledger;
        ledger.Credited += OnCredited;
    }

    public static void End()
    {
        if (activeLedger is null)
        {
            return;
        }

        activeLedger.Credited -= OnCredited;
        activeLedger = null;
        Configuration.Save();
    }

    private static void OnCredited(uint nameId)
    {
        var entries = Entries;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.NameId != nameId || !NeedsKills(entry))
            {
                continue;
            }

            entry.Killed++;
            Svc.Log.Info($"{AhgConstants.LogPrefix} Custom list: entry {index} (BNpcName {nameId}) now {entry.Killed}/{entry.Needed}");
            Configuration.SaveDebounced();
            return;
        }
    }

    private static bool NeedsKills(CustomMobEntry entry) => entry.Enabled && entry.NameId != 0 && entry.Killed < entry.Needed;

    private static bool InRange(int index) => (uint)index < (uint)Entries.Count;
}
