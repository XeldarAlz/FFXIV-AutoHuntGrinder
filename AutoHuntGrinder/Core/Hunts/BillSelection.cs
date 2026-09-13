using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace AutoHuntGrinder.Core.Hunts;

internal static class BillSelection
{
    private static readonly List<HuntBill> startList = new(MobHunt.MaxMarkIndex);

    public readonly record struct Workload(int PickUps, int Held, int KillsDone, int KillsNeeded)
    {
        public int KillsLeft => KillsNeeded - KillsDone;
    }

    // Shared buffer rebuilt on every call; copy it before holding on to it.
    public static IReadOnlyList<HuntBill> ResolveStartList(Configuration configuration)
    {
        MarkBillReader.Refresh();
        startList.Clear();
        var bills = HuntRegistry.Bills;
        for (var index = 0; index < bills.Length; index++)
        {
            var bill = bills[index];
            if (!configuration.SelectedBills.Contains(bill.MarkIndex))
            {
                continue;
            }

            var status = MarkBillReader.Status(bill.MarkIndex);
            if (status is BillStatus.Locked or BillStatus.Done)
            {
                continue;
            }

            startList.Add(bill);
        }

        return startList;
    }

    public static int CountSelected(Configuration configuration)
    {
        var bills = HuntRegistry.Bills;
        var count = 0;
        for (var index = 0; index < bills.Length; index++)
        {
            if (configuration.SelectedBills.Contains(bills[index].MarkIndex))
            {
                count++;
            }
        }

        return count;
    }

    public static Workload Measure(IReadOnlyList<HuntBill> bills)
    {
        var pickUps = 0;
        var held = 0;
        var killsDone = 0;
        var killsNeeded = 0;
        for (var index = 0; index < bills.Count; index++)
        {
            var markIndex = bills[index].MarkIndex;
            switch (MarkBillReader.Status(markIndex))
            {
                case BillStatus.Available:
                    pickUps++;
                    break;
                case BillStatus.Held or BillStatus.Stale or BillStatus.Done:
                    held++;
                    var (killed, needed) = MarkBillReader.Kills(markIndex);
                    killsDone += killed;
                    killsNeeded += needed;
                    break;
            }
        }

        return new Workload(pickUps, held, killsDone, killsNeeded);
    }
}
