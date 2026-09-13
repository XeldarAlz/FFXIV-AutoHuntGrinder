using AutoHuntGrinder.Core.External;
using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System.Numerics;

namespace AutoHuntGrinder.Windows.Sections;

internal static class ReadyState
{
    public enum Kind { SetupNeeded, PickBills, AllDone, Ready, Running, Paused }

    public readonly record struct Info(Kind Kind, Vector4 Accent, Vector4 AccentSoft, FontAwesomeIcon Icon, string Title, string Detail);

    private static int cachedFrame = -1;
    private static Info cached;

    public static Info Resolve(Configuration configuration, AutoHuntController controller)
    {
        var frame = ImGui.GetFrameCount();
        if (frame == cachedFrame)
        {
            return cached;
        }

        cached = Compute(configuration, controller);
        cachedFrame = frame;
        return cached;
    }

    private static Info Compute(Configuration configuration, AutoHuntController controller)
    {
        if (controller.Running)
        {
            if (controller.Paused)
            {
                var detail = controller.PauseReason == PauseReason.InContent
                    ? Loc.T(L.Hunt.DetailPausedInContent)
                    : Loc.T(L.Hunt.DetailPausedManual);
                return new Info(Kind.Paused, Styling.AccentAmber, Styling.AccentAmberSoft, FontAwesomeIcon.Pause, Loc.T(L.Hunt.TitlePaused), detail);
            }

            return new Info(Kind.Running, Styling.AccentBlue, Styling.AccentBlueSoft, FontAwesomeIcon.Crosshairs, Loc.T(L.Hunt.TitleRunning), PhaseLabel(controller.Phase));
        }

        if (!ExternalPlugins.AllRequiredInstalled())
        {
            return new Info(Kind.SetupNeeded, Styling.AccentRose, Styling.AccentRoseSoft, FontAwesomeIcon.ExclamationTriangle,
                Loc.T(L.Hunt.TitleSetupNeeded), Loc.T(L.Hunt.DetailSetupNeeded));
        }

        if (BillSelection.CountSelected(configuration) == 0)
        {
            return new Info(Kind.PickBills, Styling.AccentAmber, Styling.AccentAmberSoft, FontAwesomeIcon.ClipboardList,
                Loc.T(L.Hunt.TitlePickBills), Loc.T(L.Hunt.DetailPickBills));
        }

        if (BillSelection.ResolveStartList(configuration).Count == 0)
        {
            return new Info(Kind.AllDone, Styling.AccentMint, Styling.AccentMintSoft, FontAwesomeIcon.CheckDouble,
                Loc.T(L.Hunt.TitleAllDone), Loc.T(L.Hunt.DetailAllDone));
        }

        return new Info(Kind.Ready, Styling.AccentMint, Styling.AccentMintSoft, FontAwesomeIcon.CheckCircle,
            Loc.T(L.Hunt.TitleReady), Loc.T(L.Hunt.DetailReady));
    }

    public static string ShortLabel(Kind kind) => kind switch
    {
        Kind.Running     => Loc.T(L.Shell.StatusRunning),
        Kind.Paused      => Loc.T(L.Shell.StatusPaused),
        Kind.Ready       => Loc.T(L.Shell.StatusReady),
        Kind.PickBills   => Loc.T(L.Shell.StatusPickBills),
        Kind.AllDone     => Loc.T(L.Shell.StatusAllDone),
        Kind.SetupNeeded => Loc.T(L.Shell.StatusSetupNeeded),
        _                => Loc.T(L.Shell.StatusIdle),
    };

    public static string PhaseLabel(HuntPhase phase) => phase switch
    {
        HuntPhase.Reading    => Loc.T(L.Run.PhaseReading),
        HuntPhase.PickingUp  => Loc.T(L.Run.PhasePickingUp),
        HuntPhase.Travelling => Loc.T(L.Run.PhaseTravelling),
        HuntPhase.Searching  => Loc.T(L.Run.PhaseSearching),
        HuntPhase.Fighting   => Loc.T(L.Run.PhaseFighting),
        HuntPhase.Upkeep     => Loc.T(L.Progress.PhaseUpkeep),
        HuntPhase.Finishing  => Loc.T(L.Run.PhaseFinishing),
        _                    => Loc.T(L.Run.PhaseStandingBy),
    };

    public static string PlanSummary(IReadOnlyList<HuntBill> bills)
    {
        var workload = BillSelection.Measure(bills);
        var detail = workload.PickUps > 0
            ? Loc.Plural(L.Hunt.ToPickUp, workload.PickUps)
            : Loc.Plural(L.Hunt.KillsLeft, workload.KillsLeft);
        return Loc.T(L.Hunt.StartSub, Loc.Plural(L.Hunt.BillsCount, bills.Count), detail);
    }
}
