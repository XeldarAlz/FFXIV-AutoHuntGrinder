using AutoHuntGrinder.Core.HuntingLog;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoHuntGrinder.Core.Achievements;

internal static class HuntAchievements
{
    // Achievement rows of this type are earned by finishing a whole Hunting Log, and their Key is the log slot.
    private const byte HuntingLogAchievementType = 7;

    private static uint[]? logAchievements;

    public static uint ForLog(byte slot)
    {
        var table = logAchievements ??= LoadLogAchievements();
        return slot < table.Length ? table[slot] : 0;
    }

    private static uint[] LoadLogAchievements()
    {
        var table = new uint[HuntingLogRegistry.SlotCount];
        var sheet = Svc.Data.GetExcelSheet<Achievement>();
        for (var rowIndex = 0; rowIndex < sheet.Count; rowIndex++)
        {
            var achievement = sheet.GetRowAt(rowIndex);
            if (achievement.Type != HuntingLogAchievementType)
            {
                continue;
            }

            var slot = achievement.Key.RowId;
            if (slot >= table.Length || table[slot] != 0)
            {
                continue;
            }

            table[slot] = achievement.RowId;
        }

        return table;
    }
}
