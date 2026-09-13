using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Travel;
using AutoHuntGrinder.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AutoHuntGrinder.Windows.Sections.Config;

internal static class HumanizerSettings
{
    private const int MarksMin = 1;
    private const int MarksMax = 200;
    private const int BreakMinutesMin = 1;
    private const int BreakMinutesMax = 60;
    private const int PauseSecondsMax = 60;
    private const int WanderMetersMin = 5;
    private const int WanderMetersMax = 200;
    private const string CityToggleId = "##ahg_humanizer_city";

    public static void Draw(Configuration configuration)
    {
        DrawBreaksGroup(configuration);
        using var more = Motion.PushSection("##ahg_humanizer_more", configuration.HumanizerEnabled);
        if (more is null)
        {
            return;
        }

        DrawWanderingGroup(configuration);
        DrawCitiesGroup(configuration);
    }

    private static void DrawBreaksGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Safety.HumanizerBreaks));

        SettingsRow.Draw(Loc.T(L.Safety.HumanizerEnable),
            Loc.T(L.Safety.HumanizerEnableHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.HumanizerEnabled, value => configuration.HumanizerEnabled = value, "##ahg_humanizer_on"),
            SettingsRow.ToggleHeight);

        using var body = Motion.PushSwitch("##ahg_humanizer_body", configuration.HumanizerEnabled);
        if (!configuration.HumanizerEnabled)
        {
            SettingsRow.Note(Loc.T(L.Safety.HumanizerOff));
            return;
        }

        SettingsRow.Draw(Loc.T(L.Safety.MarksBetween),
            Loc.T(L.Safety.MarksBetweenHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(configuration, "##ahg_humanizer_marks",
                () => configuration.HumanizerMarksBeforeBreak, value => configuration.HumanizerMarksBeforeBreak = value,
                MarksMin, MarksMax, Loc.T(L.Safety.MarksFormat)));

        SettingsRow.Draw(Loc.T(L.Safety.BreakLength),
            Loc.T(L.Safety.BreakLengthHelp),
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawRangeInline(configuration, "##ahg_humanizer_break_min", "##ahg_humanizer_break_max",
                () => configuration.HumanizerBreakMinMinutes, value => configuration.HumanizerBreakMinMinutes = value,
                () => configuration.HumanizerBreakMaxMinutes, value => configuration.HumanizerBreakMaxMinutes = value,
                BreakMinutesMax, BreakMinutesMin, Loc.T(L.Safety.MinutesFormat)));
    }

    private static void DrawWanderingGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Safety.HumanizerWandering));

        SettingsRow.Draw(Loc.T(L.Safety.PauseBetween),
            Loc.T(L.Safety.PauseBetweenHelp),
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawRangeInline(configuration, "##ahg_humanizer_pause_min", "##ahg_humanizer_pause_max",
                () => configuration.HumanizerPauseMinSec, value => configuration.HumanizerPauseMinSec = value,
                () => configuration.HumanizerPauseMaxSec, value => configuration.HumanizerPauseMaxSec = value,
                PauseSecondsMax, 0, Loc.T(L.Safety.SecondsFormat)));

        SettingsRow.Draw(Loc.T(L.Safety.WalkDistance),
            Loc.T(L.Safety.WalkDistanceHelp),
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawRangeInline(configuration, "##ahg_humanizer_wander_min", "##ahg_humanizer_wander_max",
                () => configuration.HumanizerWanderMinMeters, value => configuration.HumanizerWanderMinMeters = value,
                () => configuration.HumanizerWanderMaxMeters, value => configuration.HumanizerWanderMaxMeters = value,
                WanderMetersMax, WanderMetersMin, Loc.T(L.Safety.MetersFormat)));
    }

    private static void DrawCitiesGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Safety.HumanizerCities));

        SettingsRow.DrawBlock(Loc.T(L.Safety.AllowedCities),
            Loc.T(L.Safety.AllowedCitiesHelp),
            () => DrawCityList(configuration));
    }

    // The list is ordered by expansion, so a header goes in wherever the expansion changes.
    private static void DrawCityList(Configuration configuration)
    {
        var cities = BreakCities.All;
        var anySelected = false;
        for (var index = 0; index < cities.Length; index++)
        {
            var city = cities[index];
            if (index == 0 || cities[index - 1].Expansion != city.Expansion)
            {
                if (index > 0)
                {
                    ImGui.Spacing();
                }

                using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
                {
                    ImGui.TextUnformatted(ExpansionLabels.Name(city.Expansion));
                }
            }

            anySelected |= DrawCityToggle(configuration, city.TerritoryId);
        }

        ImGui.Spacing();
        if (anySelected)
        {
            return;
        }

        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentRose))
        {
            ImGui.TextWrapped(Loc.T(L.Safety.NoCities));
        }
    }

    // True when the city ends the frame ticked.
    private static bool DrawCityToggle(Configuration configuration, uint territoryId)
    {
        var selected = configuration.HumanizerCities.Contains(territoryId);
        ImGui.PushID((int)territoryId);
        var changed = ToggleSwitch.Draw(CityToggleId, ref selected);
        ImGui.PopID();
        if (changed)
        {
            if (selected)
            {
                configuration.HumanizerCities.Add(territoryId);
            }
            else
            {
                configuration.HumanizerCities.Remove(territoryId);
            }

            configuration.SaveDebounced();
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
        {
            ImGui.TextUnformatted(TerritoryNames.Of(territoryId));
        }

        return selected;
    }
}
