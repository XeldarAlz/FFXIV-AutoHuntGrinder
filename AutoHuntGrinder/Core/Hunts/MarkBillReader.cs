using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace AutoHuntGrinder.Core.Hunts;

internal static unsafe class MarkBillReader
{
    private const long RefreshIntervalMs = 250;
    // A daily bill lists five marks, an elite bill lists one.
    private const int MaxTargetsPerBill = 5;

    private static readonly BillStatus[] statuses = new BillStatus[MobHunt.MaxMarkIndex];
    private static readonly byte[] targetCounts = new byte[MobHunt.MaxMarkIndex];
    private static readonly HuntTarget[] targets = new HuntTarget[MobHunt.MaxMarkIndex * MaxTargetsPerBill];
    private static readonly Dictionary<uint, TargetInfo> targetInfos = [];

    private static SubrowExcelSheet<MobHuntOrder>? orderSheet;
    private static long refreshedAtTick = -RefreshIntervalMs;

    private readonly record struct TargetInfo(uint NameId, string Name, uint TerritoryId, string ZoneName);

    public static BillStatus Status(byte markIndex) => statuses[markIndex];

    public static ReadOnlySpan<HuntTarget> Targets(byte markIndex)
        => new(targets, markIndex * MaxTargetsPerBill, targetCounts[markIndex]);

    public static bool TryFindTarget(byte markIndex, uint targetRowId, out HuntTarget target)
    {
        var list = Targets(markIndex);
        for (var index = 0; index < list.Length; index++)
        {
            if (list[index].TargetRowId == targetRowId)
            {
                target = list[index];
                return true;
            }
        }

        target = default;
        return false;
    }

    public static (int Killed, int Needed) Kills(byte markIndex)
    {
        var list = Targets(markIndex);
        var killed = 0;
        var needed = 0;
        for (var index = 0; index < list.Length; index++)
        {
            killed += Math.Min(list[index].Killed, list[index].Needed);
            needed += list[index].Needed;
        }

        return (killed, needed);
    }

    public static void Refresh(bool force = false)
    {
        var now = Environment.TickCount64;
        if (!force && now - refreshedAtTick < RefreshIntervalMs)
        {
            return;
        }

        refreshedAtTick = now;
        var mobHunt = Svc.ClientState.IsLoggedIn ? MobHunt.Instance() : null;
        var bills = HuntRegistry.Bills;
        for (var index = 0; index < bills.Length; index++)
        {
            if (mobHunt == null)
            {
                statuses[bills[index].MarkIndex] = BillStatus.Locked;
                targetCounts[bills[index].MarkIndex] = 0;
                continue;
            }

            Read(mobHunt, bills[index]);
        }
    }

    private static void Read(MobHunt* mobHunt, in HuntBill bill)
    {
        var markIndex = bill.MarkIndex;
        targetCounts[markIndex] = 0;
        if (!mobHunt->IsMarkBillUnlocked(markIndex))
        {
            statuses[markIndex] = BillStatus.Locked;
            return;
        }

        var held = mobHunt->IsMarkBillObtained(markIndex);
        var obtainedRow = (uint)mobHunt->GetObtainedHuntOrderRowId(markIndex);
        var availableRow = (uint)mobHunt->GetAvailableHuntOrderRowId(markIndex);
        var allKilled = ReadTargets(mobHunt, bill, obtainedRow);
        var status = Classify(held, obtainedRow, availableRow, allKilled);
        statuses[markIndex] = status;
        if (status == BillStatus.Available)
        {
            targetCounts[markIndex] = 0;
        }
    }

    // ObtainedMarkId survives completion, so "held" comes from ObtainedFlags and "done" means the posted
    // order is the one already cleared. A held order that no longer matches the posted one is left over
    // from an earlier reset.
    private static BillStatus Classify(bool held, uint obtainedRow, uint availableRow, bool allKilled)
    {
        if (held)
        {
            return obtainedRow == availableRow ? BillStatus.Held : BillStatus.Stale;
        }

        return obtainedRow != 0 && obtainedRow == availableRow && allKilled ? BillStatus.Done : BillStatus.Available;
    }

    private static bool ReadTargets(MobHunt* mobHunt, in HuntBill bill, uint orderRow)
    {
        if (orderRow < bill.OrderStart || orderRow >= bill.OrderStart + bill.OrderAmount)
        {
            return false;
        }

        orderSheet ??= Svc.Data.GetSubrowExcelSheet<MobHuntOrder>();
        if (!orderSheet.TryGetRow(orderRow, out var order))
        {
            return false;
        }

        var markIndex = bill.MarkIndex;
        var firstSlot = markIndex * MaxTargetsPerBill;
        var count = Math.Min(order.Count, MaxTargetsPerBill);
        var allKilled = count > 0;
        for (var subrowIndex = 0; subrowIndex < count; subrowIndex++)
        {
            var entry = order[subrowIndex];
            var info = Resolve(entry.Target.RowId);
            var killed = (byte)Math.Clamp(mobHunt->GetKillCount(markIndex, (byte)entry.SubrowId), 0, byte.MaxValue);
            targets[firstSlot + subrowIndex] = new HuntTarget(entry.Target.RowId, info.NameId, info.Name, info.TerritoryId, info.ZoneName, entry.NeededKills, killed);
            allKilled &= killed >= entry.NeededKills;
        }

        targetCounts[markIndex] = (byte)count;
        return allKilled;
    }

    private static TargetInfo Resolve(uint targetRowId)
    {
        if (targetInfos.TryGetValue(targetRowId, out var cached))
        {
            return cached;
        }

        var info = new TargetInfo(0, string.Empty, 0, string.Empty);
        if (Svc.Data.GetExcelSheet<MobHuntTarget>().TryGetRow(targetRowId, out var target))
        {
            var map = target.Map.ValueNullable;
            info = new TargetInfo(
                target.Name.RowId,
                GameText.Title(target.Name.ValueNullable?.Singular.ExtractText() ?? string.Empty),
                map?.TerritoryType.RowId ?? 0,
                map?.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty);
        }

        targetInfos[targetRowId] = info;
        return info;
    }
}
