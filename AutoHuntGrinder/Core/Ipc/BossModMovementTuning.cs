namespace AutoHuntGrinder.Core.Ipc;

// BossMod dodges the instant an AoE is telegraphed and hugs its edge, so AHG characters on the same mark step out on
// the same frame to the same spot. Rolling NormalMovement's delay and cushion levels per mark breaks that (issue #17).
internal static class BossModMovementTuning
{
    public const string Module = "BossMod.Autorotation.MiscAI.NormalMovement";

    private const string SeparateDodgeDelayTrack = "SeparateDodgeDelay";
    private const string DodgeDelayTrack = "DodgeDelayMovement";
    private const string MoveDelayTrack = "DelayMovement";
    private const string CushionTrack = "ForbiddenZoneCushion";
    private const string EnabledOption = "Enabled";

    // Option names in BossMod's order; the config stores indexes into these.
    public static readonly string[] DelayOptions = ["None", "Short", "Long"];
    public static readonly string[] CushionOptions = ["None", "Small", "Medium", "Large"];

    private static readonly string[] tracks = [SeparateDodgeDelayTrack, DodgeDelayTrack, MoveDelayTrack, CushionTrack];

    private static string? overridePreset;
    private static bool failureLogged;

    public static void Apply(string preset, string markName)
    {
        var configuration = Plugin.Instance.Configuration;
        if (!configuration.CombatMovementEnabled || !BossModIPC.Instance.CanClearTransientStrategy)
        {
            ClearOverrides();
            return;
        }

        if (overridePreset != preset)
        {
            ClearOverrides();
        }

        var dodgeDelay = Roll(DelayOptions, configuration.CombatDodgeDelayMin, configuration.CombatDodgeDelayMax);
        var moveDelay = Roll(DelayOptions, configuration.CombatMoveDelayMin, configuration.CombatMoveDelayMax);
        var cushion = Roll(CushionOptions, configuration.CombatCushionMin, configuration.CombatCushionMax);

        var ipc = BossModIPC.Instance;
        var applied = ipc.AddTransientStrategy(preset, Module, SeparateDodgeDelayTrack, EnabledOption)
                   && ipc.AddTransientStrategy(preset, Module, DodgeDelayTrack, dodgeDelay)
                   && ipc.AddTransientStrategy(preset, Module, MoveDelayTrack, moveDelay)
                   && ipc.AddTransientStrategy(preset, Module, CushionTrack, cushion);
        if (!applied)
        {
            // A partial set would leave the dodge delay split without the rolled values; take back whatever landed.
            for (var trackIndex = 0; trackIndex < tracks.Length; trackIndex++)
            {
                ipc.ClearTransientStrategy(preset, Module, tracks[trackIndex]);
            }

            overridePreset = null;
            if (!failureLogged)
            {
                failureLogged = true;
                RunLog.Warning($"Combat movement variation not applied to '{preset}' (no NormalMovement module in the preset, or a BossMod build without these options).");
            }

            return;
        }

        overridePreset = preset;
        RunLog.Info($"Combat movement for {markName} on '{preset}': dodge delay {dodgeDelay}, movement delay {moveDelay}, cushion {cushion}.");
    }

    // Transients live on BossMod's in-memory preset and outlast AHG, so a run end or unload has to take them back.
    public static void Release()
    {
        ClearOverrides();
        failureLogged = false;
    }

    private static void ClearOverrides()
    {
        if (overridePreset is null)
        {
            return;
        }

        for (var trackIndex = 0; trackIndex < tracks.Length; trackIndex++)
        {
            BossModIPC.Instance.ClearTransientStrategy(overridePreset, Module, tracks[trackIndex]);
        }

        RunLog.Info($"Combat movement overrides cleared from '{overridePreset}'.");
        overridePreset = null;
    }

    private static string Roll(string[] options, int minimum, int maximum)
    {
        var last = options.Length - 1;
        var low = Math.Clamp(minimum, 0, last);
        var high = Math.Clamp(maximum, 0, last);
        return options[Random.Shared.Next(Math.Min(low, high), Math.Max(low, high) + 1)];
    }
}
