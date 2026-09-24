using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Numerics;
using System.Text;
using PlayerHelpers = ECommons.GameHelpers.Player;

namespace AutoHuntGrinder.Core.Game.Ops;

internal static unsafe class NpcInteraction
{
    public const int NoEntry = -1;

    private const int AddonInteractThrottleMs = 500;
    private const string SelectStringAddon = "SelectString";
    private const string SelectYesnoAddon = "SelectYesno";
    private const string TalkAddon = "Talk";
    private const string SelectStringThrottleKey = "AHG.Npc.SelectString";
    private const string SelectYesnoThrottleKey = "AHG.Npc.SelectYesno";
    private const string TalkThrottleKey = "AHG.Npc.Talk";
    private const string CallbackThrottleKey = "AHG.Npc.Callback";
    private const string CloseThrottleKey = "AHG.Npc.Close";

    private static readonly BlockingFlag[] blockingFlags =
    [
        new(ConditionFlag.Mounted, "mounted"),
        new(ConditionFlag.MountOrOrnamentTransition, "mount transition"),
        new(ConditionFlag.Jumping, "jumping"),
        new(ConditionFlag.Jumping61, "airborne"),
        new(ConditionFlag.InCombat, "in combat"),
        new(ConditionFlag.Casting, "casting"),
    ];

    public static bool PlayerReady()
    {
        if (Svc.Objects.LocalPlayer is null)
        {
            return false;
        }

        for (var flagIndex = 0; flagIndex < blockingFlags.Length; flagIndex++)
        {
            if (Svc.Condition[blockingFlags[flagIndex].Flag])
            {
                return false;
            }
        }

        return !PlayerHelpers.IsAnimationLocked && !GenericHelpers.IsOccupied() && PlayerHelpers.Interactable;
    }

    public static string DescribeBlockers()
    {
        var builder = new StringBuilder();
        for (var flagIndex = 0; flagIndex < blockingFlags.Length; flagIndex++)
        {
            if (Svc.Condition[blockingFlags[flagIndex].Flag])
            {
                AppendBlocker(builder, blockingFlags[flagIndex].Label);
            }
        }

        if (PlayerHelpers.IsAnimationLocked)
        {
            AppendBlocker(builder, "animation lock");
        }

        if (GenericHelpers.IsOccupied())
        {
            AppendBlocker(builder, "occupied");
        }

        if (!PlayerHelpers.Interactable)
        {
            AppendBlocker(builder, "player not interactable");
        }

        return builder.Length == 0 ? "no blocker detected" : builder.ToString();
    }

    public static IGameObject? FindNearest(uint baseId)
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return null;
        }

        var objects = Svc.Objects;
        IGameObject? nearest = null;
        var bestDistance = float.MaxValue;
        for (var index = 0; index < objects.Length; index++)
        {
            var candidate = objects[index];
            if (candidate is null || candidate.BaseId != baseId || !candidate.IsTargetable)
            {
                continue;
            }

            var distance = Vector3.DistanceSquared(player.Position, candidate.Position);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            nearest = candidate;
        }

        return nearest;
    }

    public static bool IsTargeted(IGameObject gameObject)
        => Svc.Targets.Target?.GameObjectId == gameObject.GameObjectId;

    public static void Target(IGameObject gameObject)
        => Svc.Targets.Target = gameObject;

    public static ulong Interact(IGameObject gameObject)
        => TargetSystem.Instance()->InteractWithObject(gameObject.Struct(), false);

    public static bool IsAddonReady(string addonName) => AddonReady(addonName, out _);

    public static bool SelectStringOpen() => AddonReady(SelectStringAddon, out _);

    public static bool SelectYesnoOpen() => AddonReady(SelectYesnoAddon, out _);

    public static bool TalkOpen() => AddonReady(TalkAddon, out _);

    public static bool DialogAddonOpen() => TalkOpen() || SelectYesnoOpen() || SelectStringOpen();

    public static int SelectStringEntryCount()
        => AddonReady(SelectStringAddon, out var addon) ? new AddonMaster.SelectString(addon).EntryCount : 0;

    public static string SelectStringEntryText(int entryIndex)
    {
        if (!AddonReady(SelectStringAddon, out var addon))
        {
            return string.Empty;
        }

        var master = new AddonMaster.SelectString(addon);
        return entryIndex >= 0 && entryIndex < master.EntryCount ? master.Entries[entryIndex].Text : string.Empty;
    }

    public static int FindSelectStringEntry(string text)
    {
        if (text.Length == 0 || !AddonReady(SelectStringAddon, out var addon))
        {
            return NoEntry;
        }

        var entries = new AddonMaster.SelectString(addon).Entries;
        for (var entryIndex = 0; entryIndex < entries.Length; entryIndex++)
        {
            if (string.Equals(entries[entryIndex].Text.Trim(), text, StringComparison.OrdinalIgnoreCase))
            {
                return entryIndex;
            }
        }

        return NoEntry;
    }

    public static bool SelectEntry(int entryIndex)
    {
        if (!AddonReady(SelectStringAddon, out var addon) || entryIndex < 0 || entryIndex >= new AddonMaster.SelectString(addon).EntryCount)
        {
            return false;
        }

        if (!EzThrottler.Throttle(SelectStringThrottleKey, AddonInteractThrottleMs))
        {
            return false;
        }

        RunLog.Debug($"SelectString: choosing entry {entryIndex}");
        Callback.Fire(addon, true, entryIndex);
        return true;
    }

    public static bool SelectLastEntry()
    {
        var entryCount = SelectStringEntryCount();
        return entryCount > 0 && SelectEntry(entryCount - 1);
    }

    public static string SelectYesnoText()
        => AddonReady(SelectYesnoAddon, out var addon) ? new AddonMaster.SelectYesno(addon).Text : string.Empty;

    public static bool AnswerNo()
    {
        if (!AddonReady(SelectYesnoAddon, out var addon) || !EzThrottler.Throttle(SelectYesnoThrottleKey, AddonInteractThrottleMs))
        {
            return false;
        }

        new AddonMaster.SelectYesno(addon).No();
        return true;
    }

    public static bool ProgressTalk()
    {
        if (!AddonReady(TalkAddon, out var addon) || !EzThrottler.Throttle(TalkThrottleKey, AddonInteractThrottleMs))
        {
            return false;
        }

        new AddonMaster.Talk(addon).Click();
        return true;
    }

    public static bool FireCallback(string addonName, int value)
    {
        if (!AddonReady(addonName, out var addon) || !EzThrottler.Throttle(CallbackThrottleKey, AddonInteractThrottleMs))
        {
            return false;
        }

        RunLog.Debug($"{addonName}: firing callback {value}");
        Callback.Fire(addon, true, value);
        return true;
    }

    public static bool Close(string addonName)
    {
        if (!AddonReady(addonName, out var addon) || !EzThrottler.Throttle(CloseThrottleKey, AddonInteractThrottleMs))
        {
            return false;
        }

        addon->Close(true);
        return true;
    }

    private static void AppendBlocker(StringBuilder builder, string label)
    {
        if (builder.Length > 0)
        {
            builder.Append(", ");
        }

        builder.Append(label);
    }

    private static bool AddonReady(string addonName, out AtkUnitBase* addon)
        => GenericHelpers.TryGetAddonByName(addonName, out addon) && GenericHelpers.IsAddonReady(addon);

    private readonly record struct BlockingFlag(ConditionFlag Flag, string Label);
}
