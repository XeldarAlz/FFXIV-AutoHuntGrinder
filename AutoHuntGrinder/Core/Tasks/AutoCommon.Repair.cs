using AutoHuntGrinder.Core.Game.Ops;
using AutoHuntGrinder.Core.Travel;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

public abstract partial class AutoCommon
{
    private const float RepairMenderReachMeters = 4f;
    private const int RepairInteractWaitMs = 15_000;
    private const int RepairWindowWaitMs = 10_000;
    private const int RepairConfirmWaitMs = 5_000;
    private const int RepairFinishWaitMs = 30_000;
    private const int RepairFinishPollFrames = 60;
    // Self-repair counts only when it lifts the gear this far past the threshold; anything less goes on to the mender.
    private const float RepairSelfSuccessMarginPercent = 5f;
    // Above this with the window closed, the repair has landed even if its animation flag was missed.
    private const float RepairDoneConditionPercent = 95f;

    protected async Task<bool> RepairGear()
    {
        var configuration = Plugin.Instance.Configuration;
        var before = RepairOps.LowestEquippedConditionPercent();
        Diag($"Repair: lowest condition {before:F1}%, threshold {configuration.AutoRepairThresholdPercent}%, mode {configuration.RepairMode}");
        Svc.Chat.Print($"{AhgConstants.LogPrefix} Repairing gear (lowest at {before:F0}%).");

        var allowSelf = configuration.RepairMode is RepairMode.SelfThenNpc or RepairMode.SelfOnly;
        var allowNpc = configuration.RepairMode is RepairMode.SelfThenNpc or RepairMode.NpcOnly;
        var repaired = allowSelf && await TrySelfRepair();
        if (!repaired && allowNpc && !CancelToken.IsCancellationRequested)
        {
            repaired = await RepairAtMender();
        }
        else if (!repaired && !allowNpc && !CancelToken.IsCancellationRequested)
        {
            Warn("Repair: self-repair failed and NPC repair is off");
        }

        var after = RepairOps.LowestEquippedConditionPercent();
        if (repaired)
        {
            Svc.Chat.Print($"{AhgConstants.LogPrefix} Repair done. Lowest condition went from {before:F0}% to {after:F0}%.");
        }
        else if (!CancelToken.IsCancellationRequested)
        {
            Svc.Chat.PrintError($"{AhgConstants.LogPrefix} Repair failed; lowest condition is {after:F0}%. The log has the details.");
        }

        return repaired;
    }

    private async Task<bool> TrySelfRepair()
    {
        if (!RepairOps.HasDarkMatterForAllEquipped())
        {
            Diag("Repair: skipping self-repair, the bag lacks the Dark Matter grade some equipped piece needs");
            return false;
        }

        if (Svc.Condition[ConditionFlag.Mounted])
        {
            await SafeDismount("repair-dismount");
        }

        Status = "Opening Repair";
        Diag("Repair: using the Repair action");
        if (!RepairOps.TriggerRepairAction())
        {
            Diag("Repair: the Repair action was not sent (throttled, or no action manager)");
            return false;
        }

        if (!await WaitUntilTimed(RepairOps.RepairWindowOpen, RepairWindowWaitMs, "repair-self-window"))
        {
            return false;
        }

        if (await DriveRepairWindow())
        {
            return true;
        }

        Diag("Repair: self-repair did not lift the gear above the threshold");
        return false;
    }

    private async Task<bool> RepairAtMender()
    {
        if (RepairOps.ResolveMender(Plugin.Instance.Configuration) is not { } mender)
        {
            Warn("Repair: NPC repair needs a Grand Company or a custom repair NPC");
            Svc.Chat.PrintError($"{AhgConstants.LogPrefix} NPC repair needs a Grand Company or a custom repair NPC. Join a Grand Company, set a repair NPC, or switch to Self only.");
            return false;
        }

        var zoneName = TerritoryNames.Of(mender.TerritoryId);
        Diag($"Repair: heading to {mender.Name} in {zoneName} ({mender.TerritoryId}), BaseId {mender.DataId}");
        if (!await TravelTo(mender.TerritoryId, mender.Position, RepairMenderReachMeters))
        {
            Warn($"Repair: could not reach {mender.Name} in {zoneName}");
            return false;
        }

        if (Svc.Condition[ConditionFlag.Mounted])
        {
            await SafeDismount("repair-dismount-mender");
        }

        if (RepairOps.FindNearestObjectByBaseId(mender.DataId) is not { } npc)
        {
            Warn($"Repair: {mender.Name} (BaseId {mender.DataId}) is not in the object table near {mender.Position}");
            return false;
        }

        Status = $"Talking to {mender.Name}";
        Diag($"Repair: interacting with {mender.Name}");
        var interact = new MoveOp(move => move.Interact(npc, RepairOps.MenuOrWindowOpen, UiSkipOptions.Talk));
        await RunCancellable(interact, RepairInteractWaitMs, "repair-interact");
        if (interact.Fault is { } fault)
        {
            Warn($"Repair: interacting with {mender.Name} failed: {fault.Message}");
            return false;
        }

        // Grand Company menders open the Repair window straight away; other repair NPCs first offer a talk menu.
        if (!await WaitUntilTimed(RepairOps.MenuOrWindowOpen, RepairWindowWaitMs, "repair-npc-menu"))
        {
            Warn($"Repair: {mender.Name} opened neither a talk menu nor the Repair window");
            return false;
        }

        if (!RepairOps.RepairWindowOpen() && !await PickRepairMenuEntry(mender.Name))
        {
            return false;
        }

        return await DriveRepairWindow();
    }

    private async Task<bool> PickRepairMenuEntry(string menderName)
    {
        var index = RepairOps.RepairMenuEntryIndex(out var matchedByText);
        Diag($"Repair: talk menu open; picking entry {index} ({(matchedByText ? "matched by its text" : "fallback index")})");
        if (!RepairOps.ClickMenuEntry(index))
        {
            Warn($"Repair: could not pick entry {index} in {menderName}'s menu");
            return false;
        }

        if (await WaitUntilTimed(RepairOps.RepairWindowOpen, RepairWindowWaitMs, "repair-npc-window"))
        {
            return true;
        }

        Warn($"Repair: {menderName} did not open the Repair window after the menu pick");
        return false;
    }

    // A disabled Repair All (nothing to pay with, nothing worn) never raises the confirmation, which reads as a failure here.
    // True when the gear ended above the threshold.
    private async Task<bool> DriveRepairWindow()
    {
        Status = "Repairing gear";
        if (!RepairOps.ClickRepairAll())
        {
            Diag("Repair: Repair All was not clicked; the window closed or the click was throttled");
            RepairOps.HideRepairWindow();
            return false;
        }

        if (!await WaitUntilTimed(RepairOps.SelectYesnoOpen, RepairConfirmWaitMs, "repair-confirm"))
        {
            Diag("Repair: no confirmation appeared; Repair All is likely disabled");
            RepairOps.HideRepairWindow();
            return false;
        }

        if (!RepairOps.ClickSelectYesno())
        {
            Diag("Repair: the confirmation closed before it could be accepted");
            RepairOps.HideRepairWindow();
            return false;
        }

        await WaitForRepairToLand();
        RepairOps.HideRepairWindow();
        var after = RepairOps.LowestEquippedConditionPercent();
        Diag($"Repair: lowest condition now {after:F1}%");
        return after > Plugin.Instance.Configuration.AutoRepairThresholdPercent + RepairSelfSuccessMarginPercent;
    }

    // The repair animation raises Occupied39, and the condition jumps to full once it ends.
    private async Task WaitForRepairToLand()
    {
        var deadline = Environment.TickCount64 + RepairFinishWaitMs;
        var sawAnimation = false;
        while (Environment.TickCount64 < deadline && !CancelToken.IsCancellationRequested)
        {
            if (Svc.Condition[ConditionFlag.Occupied39])
            {
                sawAnimation = true;
            }
            else if (sawAnimation)
            {
                return;
            }

            if (!RepairOps.RepairWindowOpen() && RepairOps.LowestEquippedConditionPercent() > RepairDoneConditionPercent)
            {
                return;
            }

            await NextFrame(RepairFinishPollFrames);
        }
    }
}
