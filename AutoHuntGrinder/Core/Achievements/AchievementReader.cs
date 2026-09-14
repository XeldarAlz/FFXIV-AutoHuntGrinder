using ECommons.DalamudServices;
using ClientAchievement = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement;
using ClientAchievementState = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement.AchievementState;

namespace AutoHuntGrinder.Core.Achievements;

// The completion bitmap stays empty until someone asks the server for it, which the game itself only does when the
// Achievements window opens. The request is a server round trip, so it is rationed.
internal static unsafe class AchievementReader
{
    private const long LoadRetryMs = 30_000;

    private static long loadRequestedAtTick;

    public static bool IsLoaded
    {
        get
        {
            var achievement = Instance();
            return achievement != null && achievement->IsLoaded();
        }
    }

    public static long MillisecondsSinceLoadRequest => loadRequestedAtTick == 0 ? -1 : Environment.TickCount64 - loadRequestedAtTick;

    public static AchievementStatus Status(uint achievementId)
    {
        if (achievementId == 0)
        {
            return AchievementStatus.Unknown;
        }

        var achievement = Instance();
        if (achievement == null || !achievement->IsLoaded())
        {
            return AchievementStatus.Unknown;
        }

        return achievement->IsComplete((int)achievementId) ? AchievementStatus.Complete : AchievementStatus.Incomplete;
    }

    public static bool RequestLoad()
    {
        var achievement = Instance();
        if (achievement == null || achievement->State != ClientAchievementState.Invalid)
        {
            return false;
        }

        var now = Environment.TickCount64;
        if (loadRequestedAtTick != 0 && now - loadRequestedAtTick < LoadRetryMs)
        {
            return false;
        }

        loadRequestedAtTick = now;
        Svc.Log.Info($"{AhgConstants.LogPrefix} Achievements: requesting the completion list from the server");
        achievement->RequestCompletedAchievements();
        return true;
    }

    public static ClientAchievementState? LoadState()
    {
        var achievement = Instance();
        return achievement == null ? null : achievement->State;
    }

    private static ClientAchievement* Instance() => Svc.ClientState.IsLoggedIn ? ClientAchievement.Instance() : null;
}
