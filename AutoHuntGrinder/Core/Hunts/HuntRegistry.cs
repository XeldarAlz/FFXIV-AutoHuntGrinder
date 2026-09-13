using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace AutoHuntGrinder.Core.Hunts;

internal static class HuntRegistry
{
    private static HuntBill[]? bills;

    public static HuntBill[] Bills => bills ??= Load();

    public static bool TryGet(byte markIndex, out HuntBill bill)
    {
        var all = Bills;
        for (var index = 0; index < all.Length; index++)
        {
            if (all[index].MarkIndex != markIndex)
            {
                continue;
            }

            bill = all[index];
            return true;
        }

        bill = default;
        return false;
    }

    private static HuntBill[] Load()
    {
        var sheet = Svc.Data.GetExcelSheet<MobHuntOrderType>();
        var loaded = new List<HuntBill>(MobHunt.MaxMarkIndex);
        for (var rowIndex = 0; rowIndex < sheet.Count; rowIndex++)
        {
            var row = sheet.GetRowAt(rowIndex);
            if (row.RowId >= MobHunt.MaxMarkIndex || row.EventItem.RowId == 0 || row.OrderAmount == 0)
            {
                continue;
            }

            loaded.Add(Create(row));
        }

        loaded.Sort(Compare);
        return [.. loaded];
    }

    private static HuntBill Create(MobHuntOrderType row)
    {
        var quest = row.Quest.ValueNullable;
        var expansion = quest is { } unlock ? ExpansionKindExtensions.FromExVersion(unlock.Expansion.RowId) : ExpansionKind.ARR;
        var item = row.EventItem.Value;
        var name = item.Name.ExtractText();
        if (name.Length == 0)
        {
            name = GameText.Title(item.Singular.ExtractText());
        }

        return new HuntBill(
            (byte)row.RowId,
            expansion,
            row.Type == (byte)BillCadence.Weekly ? BillCadence.Weekly : BillCadence.Daily,
            name,
            item.Icon,
            row.Quest.RowId,
            quest?.Name.ExtractText() ?? string.Empty,
            row.OrderStart.RowId,
            row.OrderAmount);
    }

    private static int Compare(HuntBill left, HuntBill right)
    {
        var byExpansion = left.Expansion.CompareTo(right.Expansion);
        if (byExpansion != 0)
        {
            return byExpansion;
        }

        var byCadence = left.Cadence.CompareTo(right.Cadence);
        return byCadence != 0 ? byCadence : left.MarkIndex.CompareTo(right.MarkIndex);
    }
}
