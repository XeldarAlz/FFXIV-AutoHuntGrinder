using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;
using System.Text;
using AddonSheet = Lumina.Excel.Sheets.Addon;

namespace AutoHuntGrinder.Core.Game.Watchers;

// The game asks "Join X's party?" and, once No is pressed, "Decline X's party invite?", which must be answered Yes.
// Both prompts are matched against their Addon sheet templates, so the check works in every client language and never
// depends on the agent's addon-id bookkeeping. Each click waits a human reaction time, and the addon is looked up again
// by id right before it is clicked.
internal sealed unsafe class PartyInviteWatcher : IDisposable
{
    private enum Stage : byte
    {
        Idle,
        DeclinePending,
        ConfirmPending,
    }

    private const string SelectYesnoAddonName = "SelectYesno";
    private const uint JoinPartyPromptRow = 120;
    private const uint DeclineInvitePromptRow = 121;
    private const int ConfirmDelayMinMs = 400;
    private const int ConfirmDelayMaxMs = 900;
    private const int ConfirmWaitMs = 4_000;
    private const int NotReadyAbandonMs = 15_000;

    private PromptTemplate joinPrompt = PromptTemplate.Invalid;
    private PromptTemplate declinePrompt = PromptTemplate.Invalid;
    private bool templatesLoaded;

    private Stage stage;
    private long actAtTick;
    private long confirmDeadlineTick;
    private ushort inviteAddonId;
    private ushort confirmAddonId;
    private string inviterName = "";
    private string inviterWorld = "";

    public PartyInviteWatcher()
    {
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, SelectYesnoAddonName, OnSelectYesnoSetup);
        Svc.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnUpdate;
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, SelectYesnoAddonName, OnSelectYesnoSetup);
    }

    private static bool DeclineArmed()
    {
        var plugin = Plugin.Instance;
        return plugin.Configuration.DeclinePartyInvites
            && plugin.Controller.Running
            && !plugin.Controller.Paused;
    }

    private void OnSelectYesnoSetup(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon is null || stage == Stage.DeclinePending)
        {
            return;
        }

        if (stage == Stage.Idle && !DeclineArmed())
        {
            return;
        }

        EnsureTemplates();
        var prompt = ReadPrompt(addon);
        if (joinPrompt.Matches(prompt))
        {
            ArmDecline(addon);
            return;
        }

        if (declinePrompt.Matches(prompt))
        {
            ArmConfirm(addon);
            return;
        }

        if (stage == Stage.ConfirmPending || MentionsPendingInviter(prompt))
        {
            Svc.Log.Warning($"{AhgConstants.LogPrefix} Party invite: SelectYesno {addon->Id} looks invite-related but matched neither prompt template: \"{prompt}\" (join '{joinPrompt}', decline '{declinePrompt}').");
            return;
        }

        Svc.Log.Debug($"{AhgConstants.LogPrefix} SelectYesno {addon->Id} is not a party invite: \"{prompt}\"");
    }

    private void ArmDecline(AtkUnitBase* addon)
    {
        CaptureInviter();

        var configuration = Plugin.Instance.Configuration;
        var lowSeconds = Math.Max(0, configuration.DeclineInviteDelayMinSeconds);
        var highSeconds = Math.Max(lowSeconds, configuration.DeclineInviteDelayMaxSeconds);
        var delayMs = Random.Shared.Next(lowSeconds, highSeconds + 1) * TimeUnits.MillisecondsPerSecond;

        stage = Stage.DeclinePending;
        inviteAddonId = addon->Id;
        confirmAddonId = 0;
        actAtTick = Environment.TickCount64 + delayMs;
        Svc.Log.Info($"{AhgConstants.LogPrefix} Party invite from {DisplayName()} detected; declining in ~{delayMs / TimeUnits.MillisecondsPerSecond}s.");
    }

    private void ArmConfirm(AtkUnitBase* addon)
    {
        if (stage == Stage.Idle)
        {
            CaptureInviter();
        }

        stage = Stage.ConfirmPending;
        confirmAddonId = addon->Id;
        actAtTick = Environment.TickCount64 + Random.Shared.Next(ConfirmDelayMinMs, ConfirmDelayMaxMs + 1);
        Svc.Log.Debug($"{AhgConstants.LogPrefix} Party invite: decline confirmation {addon->Id} opened for {DisplayName()}; confirming shortly.");
    }

    private void OnUpdate(IFramework _)
    {
        switch (stage)
        {
            case Stage.DeclinePending:
                UpdateDecline();
                break;
            case Stage.ConfirmPending:
                UpdateConfirm();
                break;
        }
    }

    private void UpdateDecline()
    {
        var addon = FindSelectYesno(inviteAddonId);
        if (addon is null)
        {
            Svc.Log.Debug($"{AhgConstants.LogPrefix} Party invite: the prompt closed before the decline; standing down.");
            stage = Stage.Idle;
            return;
        }

        var now = Environment.TickCount64;
        if (now < actAtTick || !WaitUntilReady(addon, now, "invite prompt"))
        {
            return;
        }

        stage = Stage.ConfirmPending;
        confirmAddonId = 0;
        confirmDeadlineTick = now + ConfirmWaitMs;
        if (!Click(addon, yes: false, "decline click"))
        {
            stage = Stage.Idle;
        }
    }

    private void UpdateConfirm()
    {
        var now = Environment.TickCount64;
        if (confirmAddonId == 0)
        {
            if (now < confirmDeadlineTick)
            {
                return;
            }

            Svc.Log.Info($"{AhgConstants.LogPrefix} Declined the party invite from {DisplayName()} (no confirmation prompt appeared).");
            FinishDecline();
            return;
        }

        var addon = FindSelectYesno(confirmAddonId);
        if (addon is null)
        {
            Svc.Log.Debug($"{AhgConstants.LogPrefix} Party invite: the confirmation prompt closed before the click; standing down.");
            stage = Stage.Idle;
            return;
        }

        if (now < actAtTick || !WaitUntilReady(addon, now, "decline confirmation"))
        {
            return;
        }

        if (!Click(addon, yes: true, "confirm click"))
        {
            stage = Stage.Idle;
            return;
        }

        Svc.Log.Info($"{AhgConstants.LogPrefix} Declined the party invite from {DisplayName()}.");
        FinishDecline();
    }

    private bool WaitUntilReady(AtkUnitBase* addon, long now, string what)
    {
        if (GenericHelpers.IsAddonReady(addon))
        {
            return true;
        }

        if (now < actAtTick + NotReadyAbandonMs)
        {
            return false;
        }

        Svc.Log.Warning($"{AhgConstants.LogPrefix} Party invite: {what} {addon->Id} never became ready; standing down.");
        stage = Stage.Idle;
        return false;
    }

    private void FinishDecline()
    {
        stage = Stage.Idle;
        if (Plugin.Instance.Configuration.DeclineInviteReply)
        {
            SendReply();
        }
    }

    private static AtkUnitBase* FindSelectYesno(ushort addonId)
    {
        if (addonId == 0)
        {
            return null;
        }

        var addon = RaptureAtkUnitManager.Instance()->GetAddonById(addonId);
        if (addon is null)
        {
            return null;
        }

        return addon->NameString == SelectYesnoAddonName ? addon : null;
    }

    private static bool Click(AtkUnitBase* addon, bool yes, string what)
    {
        try
        {
            var master = new AddonMaster.SelectYesno((nint)addon);
            if (yes)
            {
                master.Yes();
            }
            else
            {
                master.No();
            }

            return true;
        }
        catch (Exception exception)
        {
            Svc.Log.Warning(exception, $"{AhgConstants.LogPrefix} Party invite: {what} threw.");
            return false;
        }
    }

    private static string ReadPrompt(AtkUnitBase* addon)
    {
        try
        {
            var master = new AddonMaster.SelectYesno((nint)addon);
            var text = master.Addon->PromptText is not null ? master.Text : master.TextLegacy;
            return PromptTemplate.Normalize(text).Trim();
        }
        catch (Exception exception)
        {
            Svc.Log.Debug($"{AhgConstants.LogPrefix} Party invite: failed to read the SelectYesno prompt: {exception.Message}");
            return "";
        }
    }

    private void EnsureTemplates()
    {
        if (templatesLoaded)
        {
            return;
        }

        templatesLoaded = true;
        try
        {
            joinPrompt = PromptTemplate.FromAddonRow(JoinPartyPromptRow);
            declinePrompt = PromptTemplate.FromAddonRow(DeclineInvitePromptRow);
        }
        catch (Exception exception)
        {
            Svc.Log.Warning(exception, $"{AhgConstants.LogPrefix} Party invite: failed to read the Addon sheet prompt templates.");
        }

        if (!joinPrompt.IsValid || !declinePrompt.IsValid)
        {
            Svc.Log.Warning($"{AhgConstants.LogPrefix} Party invite: prompt templates unavailable; auto-decline cannot identify invites.");
            return;
        }

        Svc.Log.Debug($"{AhgConstants.LogPrefix} Party invite templates: join '{joinPrompt}', decline '{declinePrompt}'.");
    }

    private static bool MentionsPendingInviter(string prompt)
    {
        try
        {
            var proxy = InfoProxyPartyInvite.Instance();
            if (proxy is null)
            {
                return false;
            }

            var name = proxy->InviterName.ToString();
            return name.Length > 0 && prompt.Contains(name, StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void SendReply()
    {
        var body = (Plugin.Instance.Configuration.DeclineInviteReplyMessage ?? "").Trim();
        if (body.Length == 0)
        {
            return;
        }

        body = body.Replace("{name}", inviterName).Replace("{world}", inviterWorld);
        var line = body.StartsWith('/')
            ? body
            : Plugin.Instance.Configuration.DeclineInviteReplyChannel switch
            {
                PartyInviteReplyChannel.Tell => BuildTell(body),
                PartyInviteReplyChannel.Yell => $"/yell {body}",
                _ => $"/say {body}",
            };

        try
        {
            Chat.SendMessage(line);
        }
        catch (Exception exception)
        {
            Svc.Log.Warning(exception, $"{AhgConstants.LogPrefix} Party invite: sending the reply threw for '{line}'.");
        }
    }

    private string BuildTell(string body)
    {
        if (string.IsNullOrEmpty(inviterName))
        {
            return $"/say {body}";
        }

        var target = string.IsNullOrEmpty(inviterWorld) ? inviterName : $"{inviterName}@{inviterWorld}";
        return $"/tell {target} {body}";
    }

    private void CaptureInviter()
    {
        inviterName = "";
        inviterWorld = "";
        try
        {
            var proxy = InfoProxyPartyInvite.Instance();
            if (proxy is null)
            {
                return;
            }

            inviterName = proxy->InviterName.ToString();
            var world = Svc.Data.GetExcelSheet<World>().GetRowOrDefault(proxy->InviterWorldId);
            inviterWorld = world?.Name.ExtractText() ?? "";
        }
        catch (Exception exception)
        {
            Svc.Log.Debug($"{AhgConstants.LogPrefix} Party invite: failed to read the inviter: {exception.Message}");
        }
    }

    private string DisplayName()
        => string.IsNullOrEmpty(inviterName) ? "a player"
         : string.IsNullOrEmpty(inviterWorld) ? inviterName
         : $"{inviterName}@{inviterWorld}";

    // The Addon sheet text with the player-name placeholder cut out: the literal text before and after it. Layout-only
    // macros (line break, non-breaking space, soft hyphen) are dropped from both sides so they cannot break the match.
    private readonly struct PromptTemplate
    {
        public static readonly PromptTemplate Invalid = new("", "");

        private readonly string prefix;
        private readonly string suffix;

        private PromptTemplate(string prefix, string suffix)
        {
            this.prefix = prefix;
            this.suffix = suffix;
        }

        public bool IsValid => prefix.Length + suffix.Length > 0;

        public bool Matches(string prompt)
            => IsValid
            && prompt.Length > prefix.Length + suffix.Length
            && prompt.StartsWith(prefix, StringComparison.Ordinal)
            && prompt.EndsWith(suffix, StringComparison.Ordinal);

        public override string ToString() => $"{prefix}<name>{suffix}";

        public static PromptTemplate FromAddonRow(uint rowId)
        {
            var row = Svc.Data.GetExcelSheet<AddonSheet>().GetRowOrDefault(rowId);
            if (row is null)
            {
                return Invalid;
            }

            var prefix = new StringBuilder();
            var suffix = new StringBuilder();
            var sawPlaceholder = false;
            foreach (var payload in row.Value.Text)
            {
                if (payload.Type == ReadOnlySePayloadType.Text)
                {
                    (sawPlaceholder ? suffix : prefix).Append(Encoding.UTF8.GetString(payload.Body.Span));
                    continue;
                }

                if (IsLayoutMacro(payload))
                {
                    continue;
                }

                sawPlaceholder = true;
                suffix.Clear();
            }

            return sawPlaceholder
                ? new PromptTemplate(Normalize(prefix.ToString()), Normalize(suffix.ToString()))
                : Invalid;
        }

        public static string Normalize(string text)
        {
            var builder = new StringBuilder(text.Length);
            for (var charIndex = 0; charIndex < text.Length; charIndex++)
            {
                var character = text[charIndex];
                if (character is '\r' or '\n' or '\u00A0' or '\u00AD')
                {
                    continue;
                }

                builder.Append(character);
            }

            return builder.ToString();
        }

        private static bool IsLayoutMacro(in ReadOnlySePayload payload)
            => payload.Type == ReadOnlySePayloadType.Macro
            && payload.MacroCode is MacroCode.NewLine or MacroCode.NonBreakingSpace or MacroCode.SoftHyphen;
    }
}
