using ECommons.DalamudServices;
using ClientAchievement = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement;
using ClientAchievementState = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement.AchievementState;

namespace AutoHuntGrinder.Core.Achievements;

// The completion bitmap stays empty until someone asks the server for it, which the game itself only does when the
// Achievements window opens. Both kinds of request are server round trips, so each one is rationed.
internal static unsafe class AchievementReader
{
    private const long LoadRetryMs = 30_000;
    private const long ProgressTimeoutMs = 5_000;

    private static long loadRequestedAtTick;
    private static uint progressRequestId;
    private static long progressRequestedAtTick;

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

    public static bool RequestProgress(uint achievementId)
    {
        var achievement = Instance();
        if (achievementId == 0 || achievement == null)
        {
            return false;
        }

        var now = Environment.TickCount64;
        if (ProgressInFlight(achievement, now))
        {
            return false;
        }

        progressRequestId = achievementId;
        progressRequestedAtTick = now;
        Svc.Log.Info($"{AhgConstants.LogPrefix} Achievements: requesting progress for achievement {achievementId}");
        achievement->RequestAchievementProgress(achievementId);
        return true;
    }

    public static bool TryGetProgress(uint achievementId, out uint current, out uint max)
    {
        current = 0;
        max = 0;
        var achievement = Instance();
        if (achievementId == 0 || achievement == null)
        {
            return false;
        }

        if (achievement->ProgressRequestState != ClientAchievementState.Loaded || achievement->ProgressAchievementId != achievementId)
        {
            ProgressInFlight(achievement, Environment.TickCount64);
            return false;
        }

        current = achievement->ProgressCurrent;
        max = achievement->ProgressMax;
        if (progressRequestId == achievementId)
        {
            progressRequestId = 0;
        }

        return true;
    }

    public static ClientAchievementState? LoadState()
    {
        var achievement = Instance();
        return achievement == null ? null : achievement->State;
    }

    private static bool ProgressInFlight(ClientAchievement* achievement, long now)
    {
        if (progressRequestId == 0)
        {
            return false;
        }

        if (achievement->ProgressRequestState == ClientAchievementState.Loaded && achievement->ProgressAchievementId == progressRequestId)
        {
            progressRequestId = 0;
            return false;
        }

        if (now - progressRequestedAtTick < ProgressTimeoutMs)
        {
            return true;
        }

        Svc.Log.Info($"{AhgConstants.LogPrefix} Achievements: no progress answer for achievement {progressRequestId} within {ProgressTimeoutMs / TimeUnits.MillisecondsPerSecond}s; dropping the request");
        progressRequestId = 0;
        return false;
    }

    private static ClientAchievement* Instance() => Svc.ClientState.IsLoggedIn ? ClientAchievement.Instance() : null;
}
