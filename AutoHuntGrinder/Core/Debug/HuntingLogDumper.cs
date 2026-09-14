using AutoHuntGrinder.Core.Achievements;
using AutoHuntGrinder.Core.HuntingLog;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System.Runtime.CompilerServices;
using System.Text;
using ClientAchievementState = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement.AchievementState;

namespace AutoHuntGrinder.Core.Debug;

internal static unsafe class HuntingLogDumper
{
    // MonsterNoteRankInfo fields ClientStructs keeps private: suspected flags at 0x34 and what looks like padding at 0x3C.
    private const int SuspectedFlagsOffset = 0x34;
    private const int SuspectedPaddingOffset = 0x3C;

    public static void Dump()
    {
        HuntingLogReader.Refresh(force: true);
        DumpPlayer();
        DumpClassJobColumn();
        DumpAchievements();
        DumpRankRecords();
        Svc.Chat.Print($"{AhgConstants.LogPrefix} Hunting Log dump written to the plugin log (/xllog).");
    }

    private static void DumpPlayer()
    {
        var classJobId = Svc.PlayerState.IsLoaded ? Svc.PlayerState.ClassJob.RowId : 0;
        var state = PlayerState.Instance();
        var grandCompany = state == null ? 0 : state->GrandCompany;
        Log($"player: ClassJob {classJobId} -> slot {SlotText(HuntingLogReader.PlayerClassSlot())}, GrandCompany {grandCompany} -> slot {SlotText(HuntingLogReader.PlayerGrandCompanySlot())}");
    }

    private static void DumpClassJobColumn()
    {
        var sheet = Svc.Data.GetExcelSheet<ClassJob>();
        var line = new StringBuilder("ClassJob.MonsterNote raw RowId:");
        for (var rowIndex = 0; rowIndex < sheet.Count; rowIndex++)
        {
            var classJob = sheet.GetRowAt(rowIndex);
            line.Append(' ').Append(classJob.RowId).Append("=0x").Append(classJob.MonsterNote.RowId.ToString("X"));
        }

        Log(line.ToString());
    }

    private static void DumpAchievements()
    {
        var state = AchievementReader.LoadState();
        Log($"achievements: state {state?.ToString() ?? "unreadable"}, last load request {AchievementReader.MillisecondsSinceLoadRequest} ms ago (-1 = never)");
        if (state == ClientAchievementState.Invalid)
        {
            Log(AchievementReader.RequestLoad()
                ? "achievements: requested the completion list; run the dump again in a few seconds to see it load"
                : "achievements: a load request went out under 30 s ago; not asking again yet");
        }

        var books = HuntingLogRegistry.Books;
        for (var index = 0; index < books.Length; index++)
        {
            var book = books[index];
            Log($"slot {book.Slot} {HuntingLogRegistry.BookName(book.Slot)}: achievement {book.AchievementId} -> {AchievementReader.Status(book.AchievementId)}");
        }
    }

    private static void DumpRankRecords()
    {
        var manager = Svc.ClientState.IsLoggedIn ? MonsterNoteManager.Instance() : null;
        if (manager == null)
        {
            Log("MonsterNoteManager is not available");
            return;
        }

        Log($"MonsterNoteManager at 0x{(nint)manager:X}");
        for (var slot = 0; slot < HuntingLogRegistry.SlotCount; slot++)
        {
            var record = (MonsterNoteRankInfo*)Unsafe.AsPointer(ref manager->RankData[slot]);
            DumpRankRecord((byte)slot, record);
        }
    }

    private static void DumpRankRecord(byte slot, MonsterNoteRankInfo* record)
    {
        var suspectedFlags = *(int*)((byte*)record + SuspectedFlagsOffset);
        var suspectedPadding = *(int*)((byte*)record + SuspectedPaddingOffset);
        var hasBook = HuntingLogRegistry.TryGetBook(slot, out var book);
        var label = hasBook ? $"{HuntingLogRegistry.BookName(slot)} ({book.Kind}, {book.RankCount} ranks, rows from {book.RowBase + 1})" : "no log";
        Log($"slot {slot} {label}: Index {record->Index} Rank {record->Rank} Flags 0x{record->Flags:X16} +0x34 0x{suspectedFlags:X8} +0x3C 0x{suspectedPadding:X8}"
            + $" | reader {HuntingLogReader.Status(slot)}, rank {HuntingLogReader.CurrentRank(slot)}, possibly stale {HuntingLogReader.IsPossiblyStale(slot)}");

        var rank = hasBook ? (byte)Math.Clamp(record->Rank, 0, Math.Max(0, book.RankCount - 1)) : (byte)0;
        var line = new StringBuilder($"slot {slot} counts (kills/MonsterNote.Count) for rank {rank}:");
        for (var entryIndex = 0; entryIndex < HuntingLogRegistry.EntriesPerRank; entryIndex++)
        {
            var entryCounts = record->RankData[entryIndex].Counts;
            line.Append(" |e").Append(entryIndex);
            for (var targetSlot = 0; targetSlot < HuntingLogRegistry.TargetsPerEntry; targetSlot++)
            {
                line.Append(' ').Append(entryCounts[targetSlot]).Append('/');
                if (HuntingLogRegistry.TryFindTarget(slot, rank, (byte)entryIndex, (byte)targetSlot, out var target, out _))
                {
                    line.Append(target.Needed);
                }
                else
                {
                    line.Append('-');
                }
            }
        }

        Log(line.ToString());
    }

    private static string SlotText(byte slot) => slot == HuntingLogRegistry.NoLog ? "none" : slot.ToString();

    private static void Log(string message) => Svc.Log.Info($"{AhgConstants.LogPrefix} [HuntingLogDump] {message}");
}
