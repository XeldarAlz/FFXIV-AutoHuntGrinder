using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Tasks;
using Dalamud.Interface;

namespace AutoHuntGrinder.Windows.Components;

internal static class PauseButton
{
    public static bool Draw(PauseReason reason, float width = 0f) => reason switch
    {
        PauseReason.InContent => HeroButton.Draw(FontAwesomeIcon.Play, Loc.T(L.Hunt.ResumeCaps), null, Styling.AccentMint, false, Loc.T(L.Hunt.InContent), width),
        PauseReason.Manual    => HeroButton.Draw(FontAwesomeIcon.Play, Loc.T(L.Hunt.ResumeCaps), null, Styling.AccentMint, true, null, width),
        _                     => HeroButton.Draw(FontAwesomeIcon.Pause, Loc.T(L.Hunt.PauseCaps), null, Styling.AccentAmber, true, null, width),
    };
}
