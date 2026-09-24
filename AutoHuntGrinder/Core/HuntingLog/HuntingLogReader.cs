using AutoHuntGrinder.Core.Achievements;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using static AutoHuntGrinder.Core.HuntingLog.HuntingLogRegistry;

namespace AutoHuntGrinder.Core.HuntingLog;

// The client keeps only the current rank of each log: Rank is that rank, every lower one is finished, and the counts
// are the kills toward it. Flags is unexplained, so completion comes from the counts against MonsterNote.Count and
// from the log's achievement.
internal static unsafe class HuntingLogReader
{
    private const long RefreshIntervalMs = 250;
    private const int CountsPerSlot = EntriesPerRank * TargetsPerEntry;

    private static readonly HuntingLogStatus[] statuses = new HuntingLogStatus[SlotCount];
    private static readonly byte[] ranks = new byte[SlotCount];
    private static readonly byte[] counts = new byte[SlotCount * CountsPerSlot];

    private static long refreshedAtTick = -RefreshIntervalMs;
    private static bool recordsRefusedLogged;

    public static HuntingLogStatus Status(byte slot) => slot < SlotCount ? statuses[slot] : HuntingLogStatus.Unavailable;

    // The 0-based rank being worked; RankCount once the log is complete and 0 while it is unavailable.
    public static byte CurrentRank(byte slot)
    {
        if (!TryGetBook(slot, out var book))
        {
            return 0;
        }

        return statuses[slot] switch
        {
            HuntingLogStatus.Complete => book.RankCount,
            HuntingLogStatus.InProgress => Math.Min(ranks[slot], (byte)Math.Max(0, book.RankCount - 1)),
            _ => 0,
        };
    }

    // Kills toward the current rank only; the counts start over whenever a rank opens.
    public static byte Killed(byte slot, byte entryIndex, byte targetSlot)
    {
        if (slot >= SlotCount || entryIndex >= EntriesPerRank || targetSlot >= TargetsPerEntry || statuses[slot] == HuntingLogStatus.Unavailable)
        {
            return 0;
        }

        return counts[slot * CountsPerSlot + entryIndex * TargetsPerEntry + targetSlot];
    }

    // Kills for a target of any rank: a finished rank reads full and a rank not yet open reads zero, so an objective
    // taken from a rank that has since closed does not fall back to the new rank's counts.
    public static byte Killed(byte slot, byte rank, byte entryIndex, byte targetSlot)
    {
        if (!TryFindTarget(slot, rank, entryIndex, targetSlot, out var target, out _))
        {
            return 0;
        }

        var status = Status(slot);
        if (status == HuntingLogStatus.Unavailable)
        {
            return 0;
        }

        var current = CurrentRank(slot);
        if (status == HuntingLogStatus.Complete || rank < current)
        {
            return target.Needed;
        }

        return rank > current ? (byte)0 : Killed(slot, entryIndex, targetSlot);
    }

    public static byte KilledForKey(ushort sourceKey)
        => TryParseSourceKey(sourceKey, out var slot, out var rank, out var entryIndex, out var targetSlot) ? Killed(slot, rank, entryIndex, targetSlot) : (byte)0;

    public static (int Killed, int Needed) RankProgress(byte slot, byte rank)
    {
        var entries = Rank(slot, rank);
        var status = Status(slot);
        var current = CurrentRank(slot);
        var killed = 0;
        var needed = 0;
        for (var entryOffset = 0; entryOffset < entries.Length; entryOffset++)
        {
            var targets = Targets(entries[entryOffset]);
            for (var targetOffset = 0; targetOffset < targets.Length; targetOffset++)
            {
                var target = targets[targetOffset];
                needed += target.Needed;
                if (status == HuntingLogStatus.InProgress && rank == current)
                {
                    killed += Math.Min(Killed(slot, entries[entryOffset].EntryIndex, target.TargetSlot), target.Needed);
                }
            }
        }

        var full = status == HuntingLogStatus.Complete || (status == HuntingLogStatus.InProgress && rank < current);
        return (full ? needed : killed, needed);
    }

    // A company log other than the player's own is not refreshed by the client, so its counts may be old.
    public static bool IsPossiblyStale(byte slot)
        => TryGetBook(slot, out var book) && book.Kind == HuntingLogKind.GrandCompany && slot != PlayerGrandCompanySlot();

    public static byte PlayerClassSlot()
        => Svc.PlayerState.IsLoaded ? SlotForClassJob(Svc.PlayerState.ClassJob.RowId) : NoLog;

    public static byte PlayerGrandCompanySlot()
    {
        var state = PlayerState.Instance();
        return state == null ? NoLog : SlotForGrandCompany(state->GrandCompany);
    }

    public static void Refresh(bool force = false)
    {
        var now = Environment.TickCount64;
        if (!force && now - refreshedAtTick < RefreshIntervalMs)
        {
            return;
        }

        refreshedAtTick = now;
        var manager = Svc.ClientState.IsLoggedIn ? MonsterNoteManager.Instance() : null;
        if (manager == null || !RecordsUsable(manager))
        {
            MarkAllUnavailable();
            return;
        }

        var playerCompanySlot = PlayerGrandCompanySlot();
        for (var slot = 0; slot < SlotCount; slot++)
        {
            Read(manager, (byte)slot, playerCompanySlot);
        }
    }

    // Index should repeat the record's own position. A record the game never filled reads zero throughout, Index
    // included, and stands for a log with no progress yet. A filled record with another Index, or no record past the
    // first carrying its own, means the layout moved or nothing has loaded, so no record is trusted.
    private static bool RecordsUsable(MonsterNoteManager* manager)
    {
        var ownIndexSeen = false;
        for (var slot = 0; slot < SlotCount; slot++)
        {
            ref var info = ref manager->RankData[slot];
            if (info.Index == slot)
            {
                ownIndexSeen |= slot > 0;
                continue;
            }

            if (!IsUnfilled(ref info))
            {
                return RefuseRecords($"rank record {slot} reports Index {info.Index}");
            }
        }

        if (!ownIndexSeen)
        {
            return RefuseRecords("no rank record past the first carries its own Index");
        }

        if (recordsRefusedLogged)
        {
            recordsRefusedLogged = false;
            RunLog.Info("Hunting Log: the rank records line up again");
        }

        return true;
    }

    private static bool RefuseRecords(string reason)
    {
        if (!recordsRefusedLogged)
        {
            recordsRefusedLogged = true;
            RunLog.Info($"Hunting Log: {reason}; treating every log as unavailable until the records line up");
        }

        return false;
    }

    private static bool IsUnfilled(ref MonsterNoteRankInfo info)
    {
        if (info.Index != 0 || info.Rank != 0)
        {
            return false;
        }

        for (var entryIndex = 0; entryIndex < EntriesPerRank; entryIndex++)
        {
            var entryCounts = info.RankData[entryIndex].Counts;
            for (var targetSlot = 0; targetSlot < TargetsPerEntry; targetSlot++)
            {
                if (entryCounts[targetSlot] != 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void Read(MonsterNoteManager* manager, byte slot, byte playerCompanySlot)
    {
        ref var info = ref manager->RankData[slot];
        var offset = slot * CountsPerSlot;
        for (var entryIndex = 0; entryIndex < EntriesPerRank; entryIndex++)
        {
            var entryCounts = info.RankData[entryIndex].Counts;
            for (var targetSlot = 0; targetSlot < TargetsPerEntry; targetSlot++)
            {
                counts[offset + entryIndex * TargetsPerEntry + targetSlot] = entryCounts[targetSlot];
            }
        }

        ranks[slot] = (byte)Math.Clamp(info.Rank, 0, byte.MaxValue);
        statuses[slot] = Classify(slot, info.Rank, playerCompanySlot);
    }

    private static HuntingLogStatus Classify(byte slot, int rawRank, byte playerCompanySlot)
    {
        if (!TryGetBook(slot, out var book) || book.RankCount == 0)
        {
            return HuntingLogStatus.Unavailable;
        }

        if (AchievementReader.Status(book.AchievementId) == AchievementStatus.Complete)
        {
            return HuntingLogStatus.Complete;
        }

        if (book.Kind == HuntingLogKind.GrandCompany ? slot != playerCompanySlot : !ClassUnlocked(book.OwnerRowId))
        {
            return HuntingLogStatus.Unavailable;
        }

        if (rawRank >= book.RankCount)
        {
            return HuntingLogStatus.Complete;
        }

        return rawRank == book.RankCount - 1 && RankCountsMet(slot, (byte)rawRank) ? HuntingLogStatus.Complete : HuntingLogStatus.InProgress;
    }

    private static bool RankCountsMet(byte slot, byte rank)
    {
        var entries = Rank(slot, rank);
        if (entries.Length == 0)
        {
            return false;
        }

        var offset = slot * CountsPerSlot;
        for (var entryOffset = 0; entryOffset < entries.Length; entryOffset++)
        {
            var entry = entries[entryOffset];
            var targets = Targets(entry);
            for (var targetOffset = 0; targetOffset < targets.Length; targetOffset++)
            {
                var target = targets[targetOffset];
                if (counts[offset + entry.EntryIndex * TargetsPerEntry + target.TargetSlot] < target.Needed)
                {
                    return false;
                }
            }
        }

        return true;
    }

    // A class the character never took up still has an empty record, and its log cannot advance.
    private static bool ClassUnlocked(byte classJobId)
        => Svc.PlayerState.IsLoaded
        && Svc.Data.GetExcelSheet<ClassJob>().TryGetRow(classJobId, out var classJob)
        && Svc.PlayerState.GetClassJobLevel(classJob) > 0;

    private static void MarkAllUnavailable()
    {
        Array.Fill(statuses, HuntingLogStatus.Unavailable);
        Array.Clear(ranks);
        Array.Clear(counts);
    }
}
