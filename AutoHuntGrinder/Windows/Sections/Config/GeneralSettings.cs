using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Tasks;
using AutoHuntGrinder.Windows.Components;

namespace AutoHuntGrinder.Windows.Sections.Config;

internal static class GeneralSettings
{
    public static void Draw(Configuration configuration)
    {
        DrawLanguageGroup(configuration);
        DrawWindowGroup(configuration);
        DrawBehaviorGroup(configuration);
        DrawFateGroup(configuration);
    }

    private static void DrawLanguageGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.Language));

        SettingsRow.Draw(Loc.T(L.Settings.Language),
            Loc.T(L.Settings.LanguageHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.DrawLanguageCombo(configuration));
    }

    private static void DrawWindowGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.GeneralWindow));

        SettingsRow.Draw(Loc.T(L.Settings.OpenOnLogin),
            Loc.T(L.Settings.OpenOnLoginHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.AutoShowOnLogin, value => configuration.AutoShowOnLogin = value, "##ahg_general_autoshow"),
            SettingsRow.ToggleHeight);
    }

    private static void DrawBehaviorGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.GeneralBehavior));

        SettingsRow.Draw(Loc.T(L.Settings.AutoPause),
            Loc.T(L.Settings.AutoPauseHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.AutoPauseInContent, value => configuration.AutoPauseInContent = value, "##ahg_general_autopause"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Session.AutoResume),
            Loc.T(L.Session.AutoResumeHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.AutoResumeOnFault, value => configuration.AutoResumeOnFault = value, "##ahg_general_autoresume"),
            SettingsRow.ToggleHeight);
    }

    private static void DrawFateGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.FateGroup));

        SettingsRow.Draw(Loc.T(L.Settings.FateHuntOthers),
            Loc.T(L.Settings.FateHuntOthersHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.FateHuntOthersWhileWaiting, value => configuration.FateHuntOthersWhileWaiting = value, "##ahg_fate_others"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Settings.FateBudget),
            Loc.T(L.Settings.FateBudgetHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(configuration, "##ahg_fate_budget",
                () => configuration.FateWaitBudgetMinutes, value => configuration.FateWaitBudgetMinutes = value,
                FateWaitSettings.MinBudgetMinutes, FateWaitSettings.MaxBudgetMinutes, Loc.T(L.Safety.MinutesFormat)));

        using var rotation = Motion.PushSection("##ahg_fate_rotation", configuration.FateHuntOthersWhileWaiting);
        if (rotation is null)
        {
            return;
        }

        SettingsRow.Draw(Loc.T(L.Settings.FateRecheck),
            Loc.T(L.Settings.FateRecheckHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(configuration, "##ahg_fate_recheck",
                () => configuration.FateRecheckMinutes, value => configuration.FateRecheckMinutes = value,
                FateWaitSettings.MinRecheckMinutes, FateWaitSettings.MaxRecheckMinutes, Loc.T(L.Safety.MinutesFormat)));

        SettingsRow.Draw(Loc.T(L.Settings.FateVisit),
            Loc.T(L.Settings.FateVisitHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(configuration, "##ahg_fate_visit",
                () => configuration.FateVisitMinutes, value => configuration.FateVisitMinutes = value,
                FateWaitSettings.MinVisitMinutes, FateWaitSettings.MaxVisitMinutes, Loc.T(L.Safety.MinutesFormat)));
    }
}
