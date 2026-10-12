using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Marks;
using AutoHuntGrinder.Windows.Components;
using AutoHuntGrinder.Windows.Sections.Config;
using Dalamud.Bindings.ImGui;

namespace AutoHuntGrinder.Windows.Sections;

internal static class RankFarmEditor
{
    public static void Draw(Configuration configuration, bool scrollIntoView)
    {
        LibraryHeader.Draw(Loc.T(L.RankFarm.Mode), scrollIntoView);
        ImGui.TextWrapped(Loc.T(L.RankFarm.Hint));
        Styling.VSpace(12f);
        using (SettingsGroup.Begin(Loc.T(L.RankFarm.Ranks)))
        {
            SettingsRow.Draw(Loc.T(L.HuntMarks.RankChip, "A"), string.Empty, SettingsControls.ToggleWidth,
                () => SettingsControls.DrawToggle(configuration, () => configuration.FarmARanks, value => configuration.FarmARanks = value, "##farm_a"), SettingsRow.ToggleHeight);
            SettingsRow.Draw(Loc.T(L.HuntMarks.RankChip, "S"), Loc.T(L.HuntMarks.SRankHelp), SettingsControls.ToggleWidth,
                () => SettingsControls.DrawToggle(configuration, () => configuration.FarmSRanks, value => configuration.FarmSRanks = value, "##farm_s"), SettingsRow.ToggleHeight);
        }

        using (SettingsGroup.Begin(Loc.T(L.RankFarm.Expansions)))
        {
            foreach (var expansion in Enum.GetValues<ExpansionKind>())
            {
                SettingsRow.Draw(ExpansionLabels.Name(expansion), string.Empty, SettingsControls.ToggleWidth,
                    () => SettingsControls.DrawToggle(configuration, () => configuration.FarmExpansions.Contains(expansion), value =>
                    {
                        if (value) configuration.FarmExpansions.Add(expansion);
                        else configuration.FarmExpansions.Remove(expansion);
                    }, $"##farm_expansion_{expansion}"), SettingsRow.ToggleHeight);
            }
        }

        using (SettingsGroup.Begin(Loc.T(L.RankFarm.Worlds)))
        {
            SettingsRow.Draw(Loc.T(L.RankFarm.Visit), Loc.T(L.RankFarm.VisitHelp), SettingsControls.ToggleWidth,
                () => SettingsControls.DrawToggle(configuration, () => configuration.FarmVisitWorlds, value => configuration.FarmVisitWorlds = value, "##farm_worlds"), SettingsRow.ToggleHeight);
            if (configuration.FarmVisitWorlds)
                ImGui.TextWrapped(Loc.T(L.RankFarm.WorldPlan, string.Join(" → ", RankFarmSelection.Worlds(true).Select(RankFarmSelection.WorldName))));
            else
                TextDraw.Hint(Loc.T(L.RankFarm.CurrentOnly));
        }

        ImGui.TextWrapped(Loc.T(L.RankFarm.Limits));
    }
}
