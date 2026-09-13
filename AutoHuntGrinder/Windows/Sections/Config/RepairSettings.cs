using AutoHuntGrinder.Core;
using AutoHuntGrinder.Core.Game.Ops;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Travel;
using AutoHuntGrinder.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using ECommons.DalamudServices;

namespace AutoHuntGrinder.Windows.Sections.Config;

internal static class RepairSettings
{
    private const int ThresholdMinPercent = 5;
    private const int ThresholdMaxPercent = 80;
    private const string ThresholdFormat = "%d%%";
    private const string CustomNpcScope = "##ahg_repair_npc";

    private static readonly RepairMode[] modes = [RepairMode.SelfThenNpc, RepairMode.SelfOnly, RepairMode.NpcOnly];

    private static readonly SettingsControls.Choices.Choice[] modeChoices =
    [
        new(L.Safety.RepairSelfThenNpcName, L.Safety.RepairSelfThenNpcDetail),
        new(L.Safety.RepairSelfOnlyName, L.Safety.RepairSelfOnlyDetail),
        new(L.Safety.RepairNpcOnlyName, L.Safety.RepairNpcOnlyDetail),
    ];

    private static RepairNpc? labelledNpc;
    private static LanguageInfo? labelLanguage;
    private static string npcLabel = string.Empty;

    public static void Draw(Configuration configuration)
    {
        DrawTriggerGroup(configuration);
        using var source = Motion.PushSection("##ahg_repair_source", configuration.AutoRepair);
        if (source is null)
        {
            return;
        }

        DrawSourceGroup(configuration);
    }

    private static void DrawTriggerGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Safety.RepairTrigger));

        SettingsRow.Draw(Loc.T(L.Safety.AutoRepair),
            Loc.T(L.Safety.AutoRepairHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.AutoRepair, value => configuration.AutoRepair = value, "##ahg_repair_on"),
            SettingsRow.ToggleHeight);

        using var body = Motion.PushSwitch("##ahg_repair_body", configuration.AutoRepair);
        if (!configuration.AutoRepair)
        {
            SettingsRow.Note(Loc.T(L.Safety.AutoRepairOff));
            return;
        }

        SettingsRow.Draw(Loc.T(L.Safety.RepairThreshold),
            Loc.T(L.Safety.RepairThresholdHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(configuration, "##ahg_repair_threshold",
                () => configuration.AutoRepairThresholdPercent, value => configuration.AutoRepairThresholdPercent = value,
                ThresholdMinPercent, ThresholdMaxPercent, ThresholdFormat));
    }

    private static void DrawSourceGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Safety.RepairSource));

        var selected = Math.Max(0, Array.IndexOf(modes, configuration.RepairMode));
        SettingsRow.Draw(Loc.T(L.Safety.RepairSource),
            Loc.T(L.Safety.RepairSourceHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##ahg_repair_mode", modeChoices, selected, choice =>
            {
                configuration.RepairMode = modes[choice];
                configuration.SaveDebounced();
            }));
        SettingsRow.Caption(Loc.T(modeChoices[selected].Detail));

        using var npcSection = Motion.PushSection("##ahg_repair_npc_section", configuration.RepairMode != RepairMode.SelfOnly);
        if (npcSection is null)
        {
            return;
        }

        SettingsRow.DrawBlock(Loc.T(L.Safety.CustomNpc),
            Loc.T(L.Safety.CustomNpcHelp),
            () => DrawCustomNpc(configuration));

        SettingsRow.Note(Loc.T(L.Safety.NpcNote));
    }

    private static void DrawCustomNpc(Configuration configuration)
    {
        var npc = configuration.PreferredRepairNpc;
        using (ImRaii.PushColor(ImGuiCol.Text, npc is null ? Styling.TextMuted : Styling.AccentMint))
        {
            ImGui.TextWrapped(npc is null ? Loc.T(L.Safety.NpcNone) : NpcLabel(npc));
        }

        ImGui.PushID(CustomNpcScope);
        if (ImGui.Button(Loc.T(L.Safety.SetFromTarget)))
        {
            CaptureTarget(configuration);
        }

        if (npc is not null)
        {
            ImGui.SameLine();
            if (ImGui.Button(Loc.T(L.Common.Clear)))
            {
                configuration.PreferredRepairNpc = null;
                configuration.SaveDebounced();
            }
        }

        ImGui.PopID();
    }

    private static void CaptureTarget(Configuration configuration)
    {
        var captured = RepairOps.CaptureTargetAsRepairNpc();
        if (captured is null)
        {
            Svc.Chat.PrintError($"{AhgConstants.LogPrefix} {Loc.T(L.Safety.NoTargetChat)}");
            return;
        }

        configuration.PreferredRepairNpc = captured;
        configuration.SaveDebounced();
        Svc.Chat.Print($"{AhgConstants.LogPrefix} {Loc.T(L.Safety.NpcSetChat, captured.Name, TerritoryNames.Of(captured.TerritoryId))}");
    }

    // Formatted once per NPC and language instead of on every frame.
    private static string NpcLabel(RepairNpc npc)
    {
        if (ReferenceEquals(npc, labelledNpc) && ReferenceEquals(Loc.Current, labelLanguage))
        {
            return npcLabel;
        }

        labelledNpc = npc;
        labelLanguage = Loc.Current;
        npcLabel = Loc.T(L.Safety.NpcSet, npc.Name, TerritoryNames.Of(npc.TerritoryId));
        return npcLabel;
    }
}
