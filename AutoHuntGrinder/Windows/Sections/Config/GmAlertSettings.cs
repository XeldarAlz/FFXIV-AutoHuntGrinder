using AutoHuntGrinder.Core.Game.Watchers;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace AutoHuntGrinder.Windows.Sections.Config;

internal static class GmAlertSettings
{
    private const int BeepCountMin = 1;
    private const int BeepCountMax = 20;
    private const int BeepLengthMinMs = 50;
    private const int BeepLengthMaxMs = 1000;
    private const int BeepPitchMinHz = 100;
    private const int BeepPitchMaxHz = 5000;
    private const float CommandInputWidth = 360f;
    private const int CommandMaxLength = 200;
    private const string CommandHint = "/logout";
    private const string CommandListId = "##ahg_gm_commands";

    private static string commandDraft = string.Empty;

    public static void Draw(Configuration configuration)
    {
        DrawAlertsGroup(configuration);
        DrawActionsGroup(configuration);
    }

    private static void DrawAlertsGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Safety.GmAlerts));

        SettingsRow.Draw(Loc.T(L.Safety.GmStopRun),
            Loc.T(L.Safety.GmStopRunHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.GmAlertStopRun, value => configuration.GmAlertStopRun = value, "##ahg_gm_stop"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Safety.GmToast),
            Loc.T(L.Safety.GmToastHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.GmAlertToast, value => configuration.GmAlertToast = value, "##ahg_gm_toast"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Safety.GmChat),
            Loc.T(L.Safety.GmChatHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.GmAlertChat, value => configuration.GmAlertChat = value, "##ahg_gm_chat"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Safety.GmSound),
            Loc.T(L.Safety.GmSoundHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.GmAlertSound, value => configuration.GmAlertSound = value, "##ahg_gm_sound"),
            SettingsRow.ToggleHeight);

        using var beep = Motion.PushSection("##ahg_gm_beep", configuration.GmAlertSound);
        if (beep is null)
        {
            return;
        }

        DrawBeepRows(configuration);
    }

    private static void DrawBeepRows(Configuration configuration)
    {
        SettingsRow.Draw(Loc.T(L.Safety.BeepCount),
            Loc.T(L.Safety.BeepCountHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(configuration, "##ahg_gm_beep_count",
                () => configuration.GmAlertBeepCount, value => configuration.GmAlertBeepCount = value,
                BeepCountMin, BeepCountMax, Loc.T(L.Safety.BeepCountFormat)));

        SettingsRow.Draw(Loc.T(L.Safety.BeepLength),
            Loc.T(L.Safety.BeepLengthHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(configuration, "##ahg_gm_beep_length",
                () => configuration.GmAlertBeepDurationMs, value => configuration.GmAlertBeepDurationMs = value,
                BeepLengthMinMs, BeepLengthMaxMs, Loc.T(L.Safety.BeepLengthFormat)));

        SettingsRow.Draw(Loc.T(L.Safety.BeepPitch),
            Loc.T(L.Safety.BeepPitchHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(configuration, "##ahg_gm_beep_pitch",
                () => configuration.GmAlertBeepFrequencyHz, value => configuration.GmAlertBeepFrequencyHz = value,
                BeepPitchMinHz, BeepPitchMaxHz, Loc.T(L.Safety.BeepPitchFormat)));

        SettingsRow.DrawBlock(Loc.T(L.Safety.Test), null, () => DrawBeepPreview(configuration));
    }

    private static void DrawBeepPreview(Configuration configuration)
    {
        bool clicked;
        ImGui.PushID("##ahg_gm_beep_preview");
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentAmber))
        {
            clicked = ImGui.SmallButton(Loc.T(L.Safety.Preview));
        }

        ImGui.PopID();
        if (clicked)
        {
            GmAlertWatcher.PlayBeeps(configuration.GmAlertBeepCount, configuration.GmAlertBeepFrequencyHz, configuration.GmAlertBeepDurationMs);
        }
    }

    private static void DrawActionsGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Safety.GmActions));

        SettingsRow.DrawBlock(Loc.T(L.Safety.GmCommands),
            Loc.T(L.Safety.GmCommandsHelp),
            () => DrawCommandList(configuration));

        SettingsRow.Draw(Loc.T(L.Safety.GmKill),
            Loc.T(L.Safety.GmKillHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.GmAlertKillGame, value => configuration.GmAlertKillGame = value, "##ahg_gm_kill"),
            SettingsRow.ToggleHeight);
    }

    private static void DrawCommandList(Configuration configuration)
    {
        DrawCommandInput(configuration);

        var commands = configuration.GmAlertCommands;
        if (commands.Count == 0)
        {
            SettingsRow.Note(Loc.T(L.Safety.NoCommands));
            return;
        }

        var removeIndex = -1;
        for (var index = 0; index < commands.Count; index++)
        {
            ListRows.Ordinal(index);
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
            {
                ImGui.TextUnformatted(commands[index]);
            }

            if (ListRows.RemoveButton(CommandListId, index, Loc.T(L.Safety.Remove)))
            {
                removeIndex = index;
            }
        }

        if (removeIndex < 0)
        {
            return;
        }

        commands.RemoveAt(removeIndex);
        configuration.SaveDebounced();
    }

    private static void DrawCommandInput(Configuration configuration)
    {
        var input = commandDraft;
        bool entered;
        using (SettingsControls.PushFrameColors())
        {
            ImGui.SetNextItemWidth(CommandInputWidth * ImGuiHelpers.GlobalScale);
            entered = ImGui.InputTextWithHint("##ahg_gm_command_input", CommandHint, ref input, CommandMaxLength, ImGuiInputTextFlags.EnterReturnsTrue);
        }

        commandDraft = input;
        ImGui.SameLine();
        bool added;
        ImGui.PushID("##ahg_gm_command_add");
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
        {
            added = ImGui.SmallButton(Loc.T(L.Safety.Add));
        }

        ImGui.PopID();
        if (!entered && !added)
        {
            return;
        }

        AddCommand(configuration, commandDraft);
        commandDraft = string.Empty;
    }

    private static void AddCommand(Configuration configuration, string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        var command = trimmed.StartsWith('/') ? trimmed : string.Concat("/", trimmed);
        if (configuration.GmAlertCommands.Contains(command))
        {
            return;
        }

        configuration.GmAlertCommands.Add(command);
        configuration.SaveDebounced();
    }
}
