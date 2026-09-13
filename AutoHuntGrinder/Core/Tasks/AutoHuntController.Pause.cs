using clib.Services;

namespace AutoHuntGrinder.Core.Tasks;

internal enum PauseReason { None, Manual, InContent }

internal sealed partial class AutoHuntController
{
    public PauseReason PauseReason { get; private set; } = PauseReason.None;

    public bool Paused => PauseReason != PauseReason.None;

    public bool CanPause => session is not null && Phase is not (HuntPhase.Idle or HuntPhase.Paused or HuntPhase.Finishing);

    public void Pause(PauseReason reason)
    {
        if (reason == PauseReason.None)
        {
            return;
        }

        if (Paused)
        {
            if (reason == PauseReason.Manual && PauseReason == PauseReason.InContent)
            {
                PauseReason = PauseReason.Manual;
                Diag("Auto-pause promoted to a manual pause; leaving content will no longer resume.");
            }

            return;
        }

        if (!CanPause)
        {
            if (reason == PauseReason.Manual)
            {
                ECommons.DalamudServices.Svc.Chat.Print($"{AhgConstants.LogPrefix} Nothing to pause.");
            }

            return;
        }

        PauseReason = reason;
        Phase = HuntPhase.Paused;
        session!.BeginPause();
        currentTask = null;
        Svc.Automation.Stop();

        Diag($"Run paused ({reason}); session kept.");
        ECommons.DalamudServices.Svc.Chat.Print(reason == PauseReason.InContent
            ? $"{AhgConstants.LogPrefix} Paused: you are in instanced content. The hunt resumes once you are back outside."
            : $"{AhgConstants.LogPrefix} Paused. Your bills and session stats are kept until you resume or stop.");
    }

    public void Resume()
    {
        if (!Paused)
        {
            return;
        }

        var resuming = session;
        if (resuming is null || activeBills.Length == 0)
        {
            Diag("Resume requested with no session or no bills; stopping instead.");
            Stop();
            return;
        }

        PauseReason = PauseReason.None;
        resuming.EndPause();
        Diag("Resuming the hunt.");
        ECommons.DalamudServices.Svc.Chat.Print($"{AhgConstants.LogPrefix} Resuming the hunt.");
        StartHunt(resuming);
    }

    public void TogglePause()
    {
        if (Paused)
        {
            Resume();
            return;
        }

        Pause(PauseReason.Manual);
    }
}
