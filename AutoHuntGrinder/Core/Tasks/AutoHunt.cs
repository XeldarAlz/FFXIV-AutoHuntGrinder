using AutoHuntGrinder.Core.Hunts;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

// v0.1 scaffold: reads the plan and reports it. Pickup, travel and combat land in later phases.
public sealed class AutoHunt(IReadOnlyList<HuntBill> bills) : AutoCommon
{
    private const int ReadSettleMs = 1_500;

    protected override async Task Execute()
    {
        Status = "Reading your bills…";
        MarkBillReader.Refresh(force: true);
        await DelayMs(ReadSettleMs);

        var workload = BillSelection.Measure(bills);
        var zones = new HashSet<uint>();
        for (var billIndex = 0; billIndex < bills.Count; billIndex++)
        {
            var bill = bills[billIndex];
            var targets = MarkBillReader.Targets(bill.MarkIndex);
            for (var targetIndex = 0; targetIndex < targets.Length; targetIndex++)
            {
                var target = targets[targetIndex];
                if (target.Done)
                {
                    continue;
                }

                zones.Add(target.TerritoryId);
                Diag($"Plan: {bill.Name} -> {target.Name} {target.Killed}/{target.Needed} in {target.ZoneName} (territory {target.TerritoryId}).");
            }
        }

        Diag($"Dry run: {bills.Count} bill(s), {workload.PickUps} to pick up, {workload.KillsLeft} kill(s) left across {zones.Count} zone(s).");
        Svc.Chat.Print($"{AhgConstants.LogPrefix} Dry run: {workload.PickUps} bill(s) to pick up, {workload.KillsLeft} kill(s) left across {zones.Count} zone(s). The hunting itself is not built yet.");
    }
}
