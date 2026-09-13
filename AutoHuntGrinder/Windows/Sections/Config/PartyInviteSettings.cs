using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace AutoHuntGrinder.Windows.Sections.Config;

internal static class PartyInviteSettings
{
    private const int DelaySecondsMax = 30;
    private const float MessageInputWidth = 360f;
    private const int MessageMaxLength = 480;

    private static readonly SettingsControls.Choices.Choice[] channelChoices =
    [
        new(L.Safety.ChannelTellName, L.Safety.ChannelTellDetail),
        new(L.Safety.ChannelSayName, L.Safety.ChannelSayDetail),
        new(L.Safety.ChannelYellName, L.Safety.ChannelYellDetail),
    ];

    public static void Draw(Configuration configuration)
    {
        DrawDeclineGroup(configuration);
        using var reply = Motion.PushSection("##ahg_invites_reply", configuration.DeclinePartyInvites);
        if (reply is null)
        {
            return;
        }

        DrawReplyGroup(configuration);
    }

    private static void DrawDeclineGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Safety.InvitesDecline));

        SettingsRow.Draw(Loc.T(L.Safety.AutoDecline),
            Loc.T(L.Safety.AutoDeclineHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.DeclinePartyInvites, value => configuration.DeclinePartyInvites = value, "##ahg_invites_on"),
            SettingsRow.ToggleHeight);

        using var body = Motion.PushSwitch("##ahg_invites_body", configuration.DeclinePartyInvites);
        if (!configuration.DeclinePartyInvites)
        {
            SettingsRow.Note(Loc.T(L.Safety.AutoDeclineOff));
            return;
        }

        SettingsRow.Draw(Loc.T(L.Safety.DeclineDelay),
            Loc.T(L.Safety.DeclineDelayHelp),
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawRangeInline(configuration, "##ahg_invites_delay_min", "##ahg_invites_delay_max",
                () => configuration.DeclineInviteDelayMinSeconds, value => configuration.DeclineInviteDelayMinSeconds = value,
                () => configuration.DeclineInviteDelayMaxSeconds, value => configuration.DeclineInviteDelayMaxSeconds = value,
                DelaySecondsMax, 0, Loc.T(L.Safety.SecondsFormat)));
    }

    private static void DrawReplyGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Safety.InvitesReply));

        SettingsRow.Draw(Loc.T(L.Safety.SendReply),
            Loc.T(L.Safety.SendReplyHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.DeclineInviteReply, value => configuration.DeclineInviteReply = value, "##ahg_invites_reply_on"),
            SettingsRow.ToggleHeight);

        using var channel = Motion.PushSection("##ahg_invites_channel_section", configuration.DeclineInviteReply);
        if (channel is null)
        {
            return;
        }

        var selected = Math.Clamp((int)configuration.DeclineInviteReplyChannel, 0, channelChoices.Length - 1);
        SettingsRow.Draw(Loc.T(L.Safety.ReplyChannel),
            Loc.T(L.Safety.ReplyChannelHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##ahg_invites_channel", channelChoices, selected, choice =>
            {
                configuration.DeclineInviteReplyChannel = (PartyInviteReplyChannel)choice;
                configuration.SaveDebounced();
            }));
        SettingsRow.Caption(Loc.T(channelChoices[selected].Detail));

        SettingsRow.DrawBlock(Loc.T(L.Safety.ReplyMessage),
            Loc.T(L.Safety.ReplyMessageHelp),
            () => DrawMessageInput(configuration));
    }

    private static void DrawMessageInput(Configuration configuration)
    {
        var message = configuration.DeclineInviteReplyMessage;
        bool edited;
        ImGui.SetNextItemWidth(MessageInputWidth * ImGuiHelpers.GlobalScale);
        using (SettingsControls.PushFrameColors())
        {
            edited = ImGui.InputTextWithHint("##ahg_invites_message", Loc.T(L.Safety.ReplyMessageHint), ref message, MessageMaxLength);
        }

        if (!edited)
        {
            return;
        }

        configuration.DeclineInviteReplyMessage = message;
        configuration.SaveDebounced();
    }
}
