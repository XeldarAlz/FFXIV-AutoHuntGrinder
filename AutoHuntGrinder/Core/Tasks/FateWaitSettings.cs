namespace AutoHuntGrinder.Core.Tasks;

// The FATE mark settings as a run reads them, held to the ranges the settings page offers.
internal readonly record struct FateWaitSettings(bool HuntsOthers, int RecheckMs, int VisitMs, int BudgetMs)
{
    public const int MinRecheckMinutes = 1;
    public const int MaxRecheckMinutes = 15;
    public const int MinVisitMinutes = 1;
    public const int MaxVisitMinutes = 10;
    public const int MinBudgetMinutes = 5;
    public const int MaxBudgetMinutes = 60;

    public static FateWaitSettings Read()
    {
        var configuration = Plugin.Instance.Configuration;
        return new FateWaitSettings(
            configuration.FateHuntOthersWhileWaiting,
            Minutes(configuration.FateRecheckMinutes, MinRecheckMinutes, MaxRecheckMinutes),
            Minutes(configuration.FateVisitMinutes, MinVisitMinutes, MaxVisitMinutes),
            Minutes(configuration.FateWaitBudgetMinutes, MinBudgetMinutes, MaxBudgetMinutes));
    }

    private static int Minutes(int minutes, int minimum, int maximum) => Math.Clamp(minutes, minimum, maximum) * TimeUnits.MillisecondsPerMinute;
}
