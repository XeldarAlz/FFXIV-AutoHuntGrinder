using AutoHuntGrinder.Core;
using AutoHuntGrinder.Core.Game.Ops;
using AutoHuntGrinder.Core.HuntingLog;
using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Spawns;
using AutoHuntGrinder.Windows.Sections;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using System.Numerics;

namespace AutoHuntGrinder.Windows;

internal enum SpawnCoverage : byte
{
    Points,
    AreaOnly,
    InDuty,
    NoData,
}

internal enum BookState : byte
{
    Unavailable,
    NotUnlocked,
    NeedsGearset,
    InProgress,
    Complete,
}

// Decides, per mode, what a press of Start would hunt, so the ready state, the plan card, the dock and the libraries
// all read the same answer.
internal static class HuntLauncher
{
    public enum Readiness : byte
    {
        NothingPicked,
        Blocked,
        AllDone,
        Ready,
    }

    // Picked counts what the plan covers: selected bills, queued logs or enabled mobs. FirstSlot is the first queued log
    // that can advance, or HuntingLogRegistry.NoLog.
    public readonly record struct Plan(HuntMode Mode, Readiness Readiness, int Picked, int Workable, int KillsLeft, byte FirstSlot);

    // Finding a gearset walks the whole gearset list, so the answer is reused for this long.
    private const long GearsetRefreshMs = 1_000;
    private const byte UnresolvedCoverage = 0;
    private const int RankBits = 8;
    private const long ByteMask = 0xFF;
    private const long CountMask = 0xFFFFFF;

    private static readonly bool[] gearsetBySlot = new bool[HuntingLogRegistry.SlotCount];
    private static readonly CachedText[] bookLabelTexts = new CachedText[HuntingLogRegistry.SlotCount];
    private static readonly CachedText[] bookStatusTexts = new CachedText[HuntingLogRegistry.SlotCount];

    private static byte[]? coverageByTarget;
    private static long gearsetCheckedAtTick = -GearsetRefreshMs;
    private static int cachedFrame = -1;
    private static HuntMode cachedMode;
    private static Plan cachedPlan;
    private static CachedText subjectText;
    private static CachedText sublabelText;
    private static CachedText captionText;

    internal static void Start(HuntMode mode)
    {
        var plugin = Plugin.Instance;
        if (mode == HuntMode.MarkBills)
        {
            plugin.Controller.Start(BillSelection.ResolveStartList(plugin.Configuration));
            return;
        }

        var plan = Assess(plugin.Configuration, mode);
        Svc.Log.Info($"{AhgConstants.LogPrefix} Start: {mode} runs are not wired to the controller yet ({plan.Workable} of {plan.Picked} ready, {plan.KillsLeft} kills left, first log slot {plan.FirstSlot})");
    }

    public static Plan Assess(Configuration configuration, HuntMode mode)
    {
        var frame = ImGui.GetFrameCount();
        if (frame == cachedFrame && mode == cachedMode)
        {
            return cachedPlan;
        }

        cachedPlan = mode switch
        {
            HuntMode.HuntingLog => AssessLogs(configuration.HuntingLogQueue),
            HuntMode.CustomList => AssessCustom(configuration.CustomMobs),
            _ => AssessBills(configuration),
        };
        cachedFrame = frame;
        cachedMode = mode;
        return cachedPlan;
    }

    public static string SubjectToken(Configuration configuration, in Plan plan)
    {
        switch (plan.Mode)
        {
            case HuntMode.HuntingLog:
                var queue = configuration.HuntingLogQueue;
                if (queue.Count == 0)
                {
                    return Loc.T(L.HuntingLog.LogsNone);
                }

                return queue.Count == 1
                    ? BookLabel(queue[0])
                    : subjectText.Get(CountKey(plan.Mode, queue.Count), static key => Loc.Plural(L.HuntingLog.LogsCount, (int)(key & CountMask)));
            case HuntMode.CustomList:
                return configuration.CustomMobs.Count == 0
                    ? Loc.T(L.CustomList.MobsNone)
                    : subjectText.Get(CountKey(plan.Mode, plan.Picked), static key => Loc.Plural(L.CustomList.MobsCount, (int)(key & CountMask)));
            default:
                return plan.Picked == 0
                    ? Loc.T(L.Hunt.BillsNone)
                    : subjectText.Get(CountKey(plan.Mode, plan.Picked), static key => Loc.Plural(L.Hunt.BillsCount, (int)(key & CountMask)));
        }
    }

    public static string Sublabel(Configuration configuration, in Plan plan)
    {
        if (plan.Mode == HuntMode.MarkBills)
        {
            var startList = BillSelection.ResolveStartList(configuration);
            return startList.Count > 0 ? ReadyState.PlanSummary(startList) : Loc.T(L.Hunt.BillsNone);
        }

        var subject = SubjectToken(configuration, plan);
        if (plan.Readiness != Readiness.Ready)
        {
            return subject;
        }

        var key = PlanKey(plan, RankOf(plan.FirstSlot), plan.KillsLeft);
        if (sublabelText.TryGet(key, out var text))
        {
            return text;
        }

        return sublabelText.Set(key, Loc.T(L.Hunt.StartSub, subject, Loc.Plural(L.Hunt.KillsLeft, plan.KillsLeft)));
    }

    public static string Reason(in Plan plan) => plan.Readiness switch
    {
        Readiness.Ready => string.Empty,
        Readiness.NothingPicked => Loc.T(plan.Mode switch
        {
            HuntMode.HuntingLog => L.HuntingLog.ReasonPick,
            HuntMode.CustomList => L.CustomList.ReasonPick,
            _ => L.Hunt.ReasonPickBill,
        }),
        Readiness.Blocked => Loc.T(plan.Mode == HuntMode.CustomList ? L.CustomList.ReasonBlocked : L.HuntingLog.ReasonBlocked),
        _ => Loc.T(plan.Mode switch
        {
            HuntMode.HuntingLog => L.HuntingLog.ReasonAllDone,
            HuntMode.CustomList => L.CustomList.ReasonAllDone,
            _ => L.Hunt.ReasonAllDone,
        }),
    };

    // The line under the plan sentence in the two modes that have no chip strip.
    public static string Caption(in Plan plan)
    {
        if (plan.Mode == HuntMode.CustomList)
        {
            return plan.Readiness switch
            {
                Readiness.NothingPicked => Loc.T(L.CustomList.PlanHint),
                Readiness.Blocked => Loc.T(L.CustomList.DetailBlocked),
                Readiness.AllDone => Loc.T(L.CustomList.DetailAllDone),
                _ => captionText.Get(PlanKey(plan, 0, plan.KillsLeft), static key => Loc.Plural(L.CustomList.PlanKillsLeft, (int)(key & CountMask))),
            };
        }

        return plan.Readiness switch
        {
            Readiness.NothingPicked => Loc.T(L.HuntingLog.PlanHint),
            Readiness.Blocked => Loc.T(L.HuntingLog.PlanBlocked),
            Readiness.AllDone => Loc.T(L.HuntingLog.PlanAllDone),
            _ => UpFirst(plan),
        };
    }

    public static string BookLabel(byte slot)
    {
        var name = HuntingLogRegistry.BookName(slot);
        if (slot >= HuntingLogRegistry.SlotCount || HuntingLogReader.Status(slot) != HuntingLogStatus.InProgress)
        {
            return name;
        }

        return bookLabelTexts[slot].Get((long)slot << RankBits | HuntingLogReader.CurrentRank(slot),
            static key => Loc.T(L.HuntingLog.BookRank, HuntingLogRegistry.BookName((byte)(key >> RankBits)), (int)(key & ByteMask) + 1));
    }

    public static BookState StateOf(byte slot)
    {
        if (!HuntingLogRegistry.TryGetBook(slot, out var book))
        {
            return BookState.Unavailable;
        }

        switch (HuntingLogReader.Status(slot))
        {
            case HuntingLogStatus.Complete:
                return BookState.Complete;
            case HuntingLogStatus.InProgress:
                return book.Kind == HuntingLogKind.Class && !HasGearset(slot) ? BookState.NeedsGearset : BookState.InProgress;
            default:
                return book.Kind == HuntingLogKind.Class && Svc.ClientState.IsLoggedIn ? BookState.NotUnlocked : BookState.Unavailable;
        }
    }

    public static string StatusText(byte slot, BookState state)
    {
        switch (state)
        {
            case BookState.Complete:
                return Loc.T(L.HuntingLog.Complete);
            case BookState.NeedsGearset:
                return Loc.T(L.HuntingLog.NeedsGearset);
            case BookState.NotUnlocked:
                return Loc.T(L.HuntingLog.NotUnlocked);
            case BookState.InProgress when HuntingLogRegistry.TryGetBook(slot, out var book):
                return bookStatusTexts[slot].Get((long)book.RankCount << RankBits | HuntingLogReader.CurrentRank(slot),
                    static key => Loc.T(L.HuntingLog.RankOf, (int)(key & ByteMask) + 1, (int)(key >> RankBits)));
            default:
                return Loc.T(L.HuntingLog.Unavailable);
        }
    }

    public static Vector4 StatusColor(BookState state) => state switch
    {
        BookState.Complete => Styling.AccentMint,
        BookState.NeedsGearset => Styling.AccentAmber,
        BookState.InProgress => Styling.TextDim,
        _ => Styling.TextMuted,
    };

    public static bool HasGearset(byte slot)
    {
        if (slot >= HuntingLogRegistry.SlotCount)
        {
            return false;
        }

        var now = Environment.TickCount64;
        if (now - gearsetCheckedAtTick >= GearsetRefreshMs)
        {
            gearsetCheckedAtTick = now;
            var books = HuntingLogRegistry.Books;
            for (var index = 0; index < books.Length; index++)
            {
                var book = books[index];
                gearsetBySlot[book.Slot] = book.Kind == HuntingLogKind.Class && GearsetSwitcher.HasGearsetFor(book.Slot);
            }
        }

        return gearsetBySlot[slot];
    }

    // targetIndex counts through the registry's whole target table (entry.FirstTarget plus the offset in the entry).
    // Spawn data never changes, so each target is looked up once.
    public static SpawnCoverage Coverage(int targetIndex, in HuntingLogTarget target)
    {
        var table = coverageByTarget ??= new byte[TargetTableSize()];
        if ((uint)targetIndex >= (uint)table.Length)
        {
            return Classify(target);
        }

        var stored = table[targetIndex];
        if (stored == UnresolvedCoverage)
        {
            stored = (byte)(Classify(target) + 1);
            table[targetIndex] = stored;
        }

        return (SpawnCoverage)(stored - 1);
    }

    public static bool IsHuntable(SpawnCoverage coverage) => coverage is SpawnCoverage.Points or SpawnCoverage.AreaOnly;

    private static Plan AssessBills(Configuration configuration)
    {
        var picked = BillSelection.CountSelected(configuration);
        var workable = BillSelection.ResolveStartList(configuration).Count;
        var readiness = picked == 0 ? Readiness.NothingPicked : workable == 0 ? Readiness.AllDone : Readiness.Ready;
        return new Plan(HuntMode.MarkBills, readiness, picked, workable, 0, HuntingLogRegistry.NoLog);
    }

    private static Plan AssessLogs(List<byte> queue)
    {
        HuntingLogReader.Refresh();
        var workable = 0;
        var complete = 0;
        var killsLeft = 0;
        var firstSlot = HuntingLogRegistry.NoLog;
        for (var index = 0; index < queue.Count; index++)
        {
            var slot = queue[index];
            var state = StateOf(slot);
            if (state == BookState.Complete)
            {
                complete++;
                continue;
            }

            var left = CanAdvance(slot, state) ? HuntableKillsLeft(slot, HuntingLogReader.CurrentRank(slot)) : 0;
            if (left == 0)
            {
                continue;
            }

            workable++;
            killsLeft += left;
            if (firstSlot == HuntingLogRegistry.NoLog)
            {
                firstSlot = slot;
            }
        }

        var readiness = queue.Count == 0 ? Readiness.NothingPicked
            : workable > 0 ? Readiness.Ready
            : complete == queue.Count ? Readiness.AllDone
            : Readiness.Blocked;
        return new Plan(HuntMode.HuntingLog, readiness, queue.Count, workable, killsLeft, firstSlot);
    }

    private static Plan AssessCustom(List<CustomMobEntry> entries)
    {
        var enabled = 0;
        var workable = 0;
        var killsLeft = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (!entry.Enabled || entry.NameId == 0)
            {
                continue;
            }

            enabled++;
            if (entry.Killed >= entry.Needed)
            {
                continue;
            }

            workable++;
            killsLeft += entry.Needed - entry.Killed;
        }

        var readiness = entries.Count == 0 ? Readiness.NothingPicked
            : workable > 0 ? Readiness.Ready
            : enabled > 0 ? Readiness.AllDone
            : Readiness.Blocked;
        return new Plan(HuntMode.CustomList, readiness, enabled, workable, killsLeft, HuntingLogRegistry.NoLog);
    }

    // A class log advances only on its class, which the run reaches through a gearset unless the player is on it now.
    private static bool CanAdvance(byte slot, BookState state)
        => state == BookState.InProgress || (state == BookState.NeedsGearset && GearsetSwitcher.OnClassFor(slot));

    private static int HuntableKillsLeft(byte slot, byte rank)
    {
        var entries = HuntingLogRegistry.Rank(slot, rank);
        var left = 0;
        for (var entryOffset = 0; entryOffset < entries.Length; entryOffset++)
        {
            var entry = entries[entryOffset];
            var targets = HuntingLogRegistry.Targets(entry);
            for (var targetOffset = 0; targetOffset < targets.Length; targetOffset++)
            {
                var target = targets[targetOffset];
                if (!IsHuntable(Coverage(entry.FirstTarget + targetOffset, target)))
                {
                    continue;
                }

                left += Math.Max(0, target.Needed - HuntingLogReader.Killed(slot, entry.EntryIndex, target.TargetSlot));
            }
        }

        return left;
    }

    private static string UpFirst(in Plan plan)
    {
        var slot = plan.FirstSlot;
        var rank = RankOf(slot);
        var left = HuntableKillsLeft(slot, rank);
        var key = PlanKey(plan, rank, left);
        if (captionText.TryGet(key, out var text))
        {
            return text;
        }

        return captionText.Set(key, Loc.T(L.HuntingLog.PlanNext, BookLabel(slot), Loc.Plural(L.HuntingLog.KillsLeftInRank, left)));
    }

    private static byte RankOf(byte slot) => slot == HuntingLogRegistry.NoLog ? (byte)0 : HuntingLogReader.CurrentRank(slot);

    private static long CountKey(HuntMode mode, int count) => (long)mode << 32 | (uint)count;

    private static long PlanKey(in Plan plan, byte rank, int count)
        => (long)plan.Readiness << 58 | (long)plan.Mode << 56 | (long)rank << 48 | (long)plan.FirstSlot << 40
        | (long)(ushort)plan.Picked << 24 | (count & CountMask);

    private static SpawnCoverage Classify(in HuntingLogTarget target)
    {
        if (target.InDuty)
        {
            return SpawnCoverage.InDuty;
        }

        var zones = HuntingLogRegistry.Zones(target);
        var areaFound = false;
        for (var index = 0; index < zones.Length; index++)
        {
            if (!MobSpawns.TryGet(target.NameId, zones[index], out var points) || points.Length == 0)
            {
                continue;
            }

            if (points[0].Kind == SpawnKind.Point)
            {
                return SpawnCoverage.Points;
            }

            areaFound = true;
        }

        if (areaFound)
        {
            return SpawnCoverage.AreaOnly;
        }

        var territories = MobSpawns.Territories(target.NameId);
        if (territories.Length == 0 || !MobSpawns.TryGet(target.NameId, territories[0], out var fallback) || fallback.Length == 0)
        {
            return SpawnCoverage.NoData;
        }

        return fallback[0].Kind == SpawnKind.Point ? SpawnCoverage.Points : SpawnCoverage.AreaOnly;
    }

    private static int TargetTableSize()
    {
        var size = 0;
        var books = HuntingLogRegistry.Books;
        for (var bookIndex = 0; bookIndex < books.Length; bookIndex++)
        {
            var book = books[bookIndex];
            for (byte rank = 0; rank < book.RankCount; rank++)
            {
                var entries = HuntingLogRegistry.Rank(book.Slot, rank);
                for (var entryIndex = 0; entryIndex < entries.Length; entryIndex++)
                {
                    size = Math.Max(size, entries[entryIndex].FirstTarget + entries[entryIndex].TargetCount);
                }
            }
        }

        return size;
    }
}
