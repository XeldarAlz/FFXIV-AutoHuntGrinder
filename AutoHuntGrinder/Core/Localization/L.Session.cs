namespace AutoHuntGrinder.Core.Localization;

internal static partial class L
{
    internal static class Session
    {
        public static readonly LocString AutoResume = new("session.autoResume", "Auto-resume on fault");
        public static readonly LocString AutoResumeHelp = new("session.autoResumeHelp", "If the hunt hits an unexpected error and stops, restart it automatically (up to 3 times in 5 minutes) instead of ending the run. Turn it off if you would rather have faults end the run.");
    }
}
