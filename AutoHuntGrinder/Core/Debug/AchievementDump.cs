using AutoHuntGrinder.Core.Achievements;
using ClientAchievementState = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement.AchievementState;

namespace AutoHuntGrinder.Core.Debug;

internal static class AchievementDump
{
    // A dump that finds the completion list unloaded asks for it, so the next dump can show it.
    public static void LogLoadState(Action<string> log)
    {
        var state = AchievementReader.LoadState();
        log($"achievements: state {state?.ToString() ?? "unreadable"}, last load request {AchievementReader.MillisecondsSinceLoadRequest} ms ago (-1 = never)");
        if (state != ClientAchievementState.Invalid)
        {
            return;
        }

        log(AchievementReader.RequestLoad()
            ? "achievements: requested the completion list; run the dump again in a few seconds to see it load"
            : $"achievements: a load request went out under {AchievementReader.LoadRetryMs / TimeUnits.MillisecondsPerSecond} s ago; not asking again yet");
    }
}
