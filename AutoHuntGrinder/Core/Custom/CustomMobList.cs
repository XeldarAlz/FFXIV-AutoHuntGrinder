using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Kills;
using AutoHuntGrinder.Core.Marks;

namespace AutoHuntGrinder.Core.Custom;

internal static class CustomMobList
{
    private const int NotListed = -1;

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

    private static int IndexOf(uint nameId)
    {
        var entries = Entries;
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].NameId == nameId)
            {
                return index;
            }
        }

        return NotListed;
    }

    public static CustomMobEntry? Find(uint nameId)
    {
        var index = IndexOf(nameId);
        return index == NotListed ? null : Entries[index];
    }

    public static bool Contains(uint nameId) => IndexOf(nameId) != NotListed;

    public static bool NeedsKills(CustomMobEntry entry) => entry.Enabled && entry.NameId != 0 && entry.Killed < entry.Needed;

    public static int CountNeedingKills()
    {
        var entries = Entries;
        var count = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            if (NeedsKills(entries[index]))
            {
                count++;
            }
        }

        return count;
    }

    public static bool CanHunt(CustomMobEntry entry) => ObjectivePlanner.CanHunt(ObjectiveFor(entry, 0));

    public static void BuildObjectives(List<HuntObjective> destination)
    {
        destination.Clear();
        var entries = Entries;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (NeedsKills(entry))
            {
                destination.Add(ObjectiveFor(entry, index));
            }
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
        var index = IndexOf(nameId);
        if (index == NotListed)
        {
            return;
        }

        var entry = Entries[index];
        if (!NeedsKills(entry))
        {
            return;
        }

        entry.Killed++;
        RunLog.Info($"Custom list: entry {index} (BNpcName {nameId}) now {entry.Killed}/{entry.Needed}");
        Configuration.SaveDebounced();
    }

    // A hunt mark spawns only in the zone that lists it, whatever other zones the spawn table knows its name in, so an
    // unpinned one is held there; 0 leaves the zone to the planner.
    public static uint SearchTerritory(CustomMobEntry entry)
        => entry.PinnedTerritoryId != 0 ? entry.PinnedTerritoryId : HuntMarkRegistry.SpawnTerritoryOf(entry.NameId);

    private static HuntObjective ObjectiveFor(CustomMobEntry entry, int index)
        => new(ObjectiveSource.Custom, (ushort)index, entry.NameId, SearchTerritory(entry), entry.Needed, entry.Killed);

    private static bool InRange(int index) => (uint)index < (uint)Entries.Count;
}
