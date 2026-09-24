using AutoHuntGrinder.Core.HuntingLog;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Threading;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Game.Ops;

internal enum GearsetSwitchResult : byte
{
    AlreadyOnClass,
    Switched,
    NoGearset,
    InCombat,
    Refused,
    TimedOut,
    Cancelled,
}

// A class Hunting Log only advances while a class or job that maps to it is equipped, so a class book starts by
// equipping such a gearset. Gearset indexes here are the API's 0-based ones; the game's list shows them plus one.
// Only the members that touch pointers are unsafe, because an async method cannot await inside an unsafe context.
internal static class GearsetSwitcher
{
    public const int NoGearset = -1;

    // The in-game gearset list holds 100 sets.
    private const int MaxGearsetCount = 100;
    // The class change is one server round trip; this leaves room for a slow one.
    private const int SwitchTimeoutMs = 10_000;
    private const int SwitchPollFrames = 5;
    // 0 keeps the glamour plate linked to the gearset.
    private const byte LinkedGlamourPlate = 0;

    public static bool OnClassFor(byte slot)
    {
        var classJobId = CurrentClassJobId();
        return classJobId != 0 && HuntingLogRegistry.SlotForClassJob(classJobId) == slot;
    }

    public static bool HasGearsetFor(byte slot) => FindBestGearset(slot) != NoGearset;

    // Among the gearsets whose class or job maps to the slot, the highest class level wins, then the highest item level.
    public static unsafe int FindBestGearset(byte slot)
    {
        var module = RaptureGearsetModule.Instance();
        if (slot == HuntingLogRegistry.NoLog || module == null)
        {
            return NoGearset;
        }

        var classJobs = Svc.Data.GetExcelSheet<ClassJob>();
        var best = NoGearset;
        var bestLevel = -1;
        var bestItemLevel = -1;
        for (var gearsetIndex = 0; gearsetIndex < MaxGearsetCount; gearsetIndex++)
        {
            if (!module->IsValidGearset(gearsetIndex))
            {
                continue;
            }

            var gearset = module->GetGearset(gearsetIndex);
            if (gearset == null || HuntingLogRegistry.SlotForClassJob(gearset->ClassJob) != slot)
            {
                continue;
            }

            var level = ClassLevel(classJobs, gearset->ClassJob);
            if (level < bestLevel || (level == bestLevel && gearset->ItemLevel <= bestItemLevel))
            {
                continue;
            }

            best = gearsetIndex;
            bestLevel = level;
            bestItemLevel = gearset->ItemLevel;
        }

        return best;
    }

    // Must run on the framework thread, as the run loop's tasks do; the wait polls between frames like their own waits.
    public static async Task<GearsetSwitchResult> EquipForSlot(byte slot, CancellationToken cancelToken)
    {
        if (OnClassFor(slot))
        {
            return GearsetSwitchResult.AlreadyOnClass;
        }

        var bookName = HuntingLogRegistry.BookName(slot);
        var gearsetIndex = FindBestGearset(slot);
        if (gearsetIndex == NoGearset)
        {
            Log($"Gearset: no gearset maps to the {bookName} log (slot {slot})");
            return GearsetSwitchResult.NoGearset;
        }

        if (Svc.Condition[ConditionFlag.InCombat])
        {
            Log($"Gearset: in combat, so gearset {gearsetIndex + 1} for the {bookName} log waits");
            return GearsetSwitchResult.InCombat;
        }

        if (!TryEquip(gearsetIndex, bookName))
        {
            return GearsetSwitchResult.Refused;
        }

        var deadline = Environment.TickCount64 + SwitchTimeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (cancelToken.IsCancellationRequested)
            {
                return GearsetSwitchResult.Cancelled;
            }

            if (OnClassFor(slot))
            {
                Log($"Gearset: now on {ClassJobLabel(CurrentClassJobId())} for the {bookName} log");
                return GearsetSwitchResult.Switched;
            }

            try
            {
                await Svc.Framework.DelayTicks(SwitchPollFrames, cancelToken);
            }
            catch (OperationCanceledException)
            {
                return GearsetSwitchResult.Cancelled;
            }
        }

        Log($"Gearset: the class did not change within {SwitchTimeoutMs / TimeUnits.MillisecondsPerSecond}s of equipping gearset {gearsetIndex + 1} (still {ClassJobLabel(CurrentClassJobId())})");
        return GearsetSwitchResult.TimedOut;
    }

    private static unsafe bool TryEquip(int gearsetIndex, string bookName)
    {
        var module = RaptureGearsetModule.Instance();
        var gearset = module == null ? null : module->GetGearset(gearsetIndex);
        if (gearset == null)
        {
            return false;
        }

        Log($"Gearset: equipping gearset {gearsetIndex + 1} ({ClassJobLabel(gearset->ClassJob)}) for the {bookName} log, from {ClassJobLabel(CurrentClassJobId())}");
        var result = module->EquipGearset(gearsetIndex, LinkedGlamourPlate);
        if (result == 0)
        {
            return true;
        }

        RunLog.Warning($"Gearset: the game refused gearset {gearsetIndex + 1} (EquipGearset returned {result})");
        return false;
    }

    private static uint CurrentClassJobId() => Svc.Objects.LocalPlayer?.ClassJob.RowId ?? 0;

    private static int ClassLevel(ExcelSheet<ClassJob> classJobs, uint classJobId)
        => classJobs.TryGetRow(classJobId, out var classJob) ? Svc.PlayerState.GetClassJobLevel(classJob) : 0;

    private static string ClassJobLabel(uint classJobId)
    {
        var abbreviation = Svc.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(classJobId)?.Abbreviation.ExtractText();
        return string.IsNullOrEmpty(abbreviation) ? $"class {classJobId}" : abbreviation;
    }

    private static void Log(string message) => RunLog.Info(message);
}
