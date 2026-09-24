using ECommons;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoHuntGrinder.Core.Game.Ops;

internal static unsafe class DialogDriver
{
    private const string SelectYesnoAddon = "SelectYesno";
    private const string SelectStringAddon = "SelectString";
    private const string TalkAddon = "Talk";
    private const int ClickThrottleMs = 500;
    private const string SelectStringThrottleKey = "AHG.Dialog.SelectString";
    private const string SelectYesnoThrottleKey = "AHG.Dialog.SelectYesno";
    private const string TalkThrottleKey = "AHG.Dialog.Talk";

    public static bool ConfirmationOpen() => AddonReady(SelectYesnoAddon, out _);

    public static bool Confirm()
    {
        if (!AddonReady(SelectYesnoAddon, out var confirmation))
        {
            return false;
        }

        Diag("Dialog: answering Yes to the confirmation.");
        new AddonMaster.SelectYesno(confirmation).Yes();
        return true;
    }

    public static bool AnyOpen()
        => AddonReady(TalkAddon, out _)
        || AddonReady(SelectYesnoAddon, out _)
        || AddonReady(SelectStringAddon, out _);

    // True while any dialog is up, clicked this frame or throttled, so the caller waits instead of starting a new conversation.
    public static bool AdvanceAccepting()
    {
        if (AddonReady(SelectStringAddon, out var menu))
        {
            if (EzThrottler.Throttle(SelectStringThrottleKey, ClickThrottleMs))
            {
                SelectFirstEntry(menu);
            }

            return true;
        }

        if (AddonReady(SelectYesnoAddon, out var confirmation))
        {
            if (EzThrottler.Throttle(SelectYesnoThrottleKey, ClickThrottleMs))
            {
                Diag("Dialog: answering Yes.");
                new AddonMaster.SelectYesno(confirmation).Yes();
            }

            return true;
        }

        if (AddonReady(TalkAddon, out var talk))
        {
            if (EzThrottler.Throttle(TalkThrottleKey, ClickThrottleMs))
            {
                Diag("Dialog: advancing the talk window.");
                new AddonMaster.Talk(talk).Click();
            }

            return true;
        }

        return false;
    }

    private static void SelectFirstEntry(AtkUnitBase* addon)
    {
        var menu = new AddonMaster.SelectString(addon);
        if (menu.EntryCount == 0)
        {
            return;
        }

        var first = menu.Entries[0];
        Diag($"Dialog: choosing '{first.Text}' from a menu of {menu.EntryCount}.");
        first.Select();
    }

    private static bool AddonReady(string name, out AtkUnitBase* addon)
        => GenericHelpers.TryGetAddonByName(name, out addon) && GenericHelpers.IsAddonReady(addon);

    private static void Diag(string message)
        => RunLog.Info(message);
}
