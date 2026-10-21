namespace AutoHuntGrinder.Core;

internal static class AhgConstants
{
    public const string PrimaryCommand = "/ahg";
    public const string AliasCommand = "/huntgrinder";

    public const string LogPrefix = "[AHG]";

    public const int SaveThrottleMs = 500;

    // Some plugin commands misbehave when several arrive in the same frame, so post-run commands are spaced out.
    public const int PostRunCommandSpacingMs = 500;

    internal static class ThrottleKeys
    {
        public const string Save = "AutoHuntGrinder.Save";
    }
}
