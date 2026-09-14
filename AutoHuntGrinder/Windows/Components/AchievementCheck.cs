using AutoHuntGrinder.Core.Achievements;
using AutoHuntGrinder.Core.Localization;
using Dalamud.Interface;
using ClientAchievementState = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement.AchievementState;

namespace AutoHuntGrinder.Windows.Components;

// The completion list is a server round trip that the reader rations, so every Check button shares one refusal.
internal static class AchievementCheck
{
    public const FontAwesomeIcon Icon = FontAwesomeIcon.Sync;

    private static bool refused;

    public readonly record struct Button(string Label, bool Enabled, string Tooltip);

    // A list that is loading or loaded clears an earlier refusal.
    public static Button Read()
    {
        var loadState = AchievementReader.LoadState();
        if (loadState != ClientAchievementState.Invalid)
        {
            refused = false;
        }

        return new Button(
            Loc.T(loadState == ClientAchievementState.Requested ? L.HuntingLog.Checking : L.HuntingLog.Check),
            loadState == ClientAchievementState.Invalid,
            Loc.T(refused ? L.HuntingLog.CheckWait : L.HuntingLog.CheckHelp));
    }

    public static void Press() => refused = !AchievementReader.RequestLoad();
}
