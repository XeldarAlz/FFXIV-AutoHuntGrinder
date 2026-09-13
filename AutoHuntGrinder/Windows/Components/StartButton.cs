using AutoHuntGrinder.Core.Localization;
using Dalamud.Interface;

namespace AutoHuntGrinder.Windows.Components;

internal static class StartButton
{
    public static bool Draw(string sublabel, bool enabled, string? disabledReason = null, float width = 0f)
        => HeroButton.Draw(FontAwesomeIcon.Play, Loc.T(L.Hunt.Start), sublabel, Styling.AccentGlow, enabled, disabledReason, width);
}
