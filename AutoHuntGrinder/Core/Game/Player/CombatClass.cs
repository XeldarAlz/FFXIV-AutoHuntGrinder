using ECommons.DalamudServices;

namespace AutoHuntGrinder.Core.Game.Player;

// Crafters and gatherers share role 0 in the ClassJob sheet; every combat class and job has a role of its own. An
// unknown player (mid-zone) counts as a combat class, so a transient null never ends a run.
internal static class CombatClass
{
    private const byte CrafterOrGathererRole = 0;

    public static bool Equipped()
        => Svc.Objects.LocalPlayer?.ClassJob.ValueNullable is not { Role: CrafterOrGathererRole };

    public static string CurrentAbbreviation()
        => Svc.Objects.LocalPlayer?.ClassJob.ValueNullable?.Abbreviation.ExtractText() ?? string.Empty;
}
