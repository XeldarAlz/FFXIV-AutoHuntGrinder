namespace AutoHuntGrinder.Core;

internal static class AhgConstants
{
    public const string PrimaryCommand = "/ahg";
    public const string AliasCommand = "/huntgrinder";

    public const string LogPrefix = "[AHG]";

    public const int SaveThrottleMs = 500;

    internal static class ThrottleKeys
    {
        public const string Save = "AutoHuntGrinder.Save";
    }
}
