using AutoHuntGrinder.Core.External;
using AutoHuntGrinder.Core.Hunts;
using clib.Services;

namespace AutoHuntGrinder.Core.Tasks;

internal sealed partial class AutoHuntController
{
    private readonly HuntProgress progress = new();

    private AutoHuntSession? session;
    private HuntBill[] activeBills = [];
    private AutoCommon? currentTask;

    public bool Running => Svc.Automation.Running || Paused;

    public string Status => PauseReason switch
    {
        PauseReason.InContent => "Paused while you are in content",
        PauseReason.Manual    => "Paused",
        _                     => Svc.Automation.CurrentTask?.Status ?? "Idle",
    };

    public HuntPhase Phase => progress.Phase;

    public HuntProgress Progress => progress;

    public AutoHuntSession? SessionSnapshot => session;

    public IReadOnlyList<HuntBill> ActiveBills => activeBills;

    private static void Diag(string message)
        => ECommons.DalamudServices.Svc.Log.Info($"{AhgConstants.LogPrefix} {message}");

    public void Start(IReadOnlyList<HuntBill> bills)
    {
        if (bills.Count == 0)
        {
            Diag("Start aborted: no bills selected.");
            return;
        }

        if (!ExternalPlugins.AllRequiredInstalled())
        {
            var missing = ExternalPlugins.MissingRequiredNames();
            Diag($"Start aborted: required plugins missing ({missing}).");
            ECommons.DalamudServices.Svc.Chat.PrintError($"{AhgConstants.LogPrefix} Cannot start: install all required plugins first ({missing}).");
            return;
        }

        activeBills = [.. bills];
        PauseReason = PauseReason.None;
        ResetFaultBudget();
        session = new AutoHuntSession(activeBills);
        Diag($"Run starting: {activeBills.Length} bill(s), job {session.JobAbbreviation}.");
        StartHunt(session);
    }

    public void Stop()
    {
        var ending = session;
        currentTask = null;
        PauseReason = PauseReason.None;
        Svc.Automation.Stop();
        FinalizeRun(ending);
        session = null;
        activeBills = [];
        progress.Reset();
        if (ending is not null)
        {
            Diag("Stop requested; session cleared.");
        }
    }

    private void StartHunt(AutoHuntSession owningSession)
    {
        progress.Reset();
        progress.SetPhase(HuntPhase.Reading);
        RunTask(new AutoHunt(activeBills, owningSession, progress), () => OnHuntEnded(owningSession));
    }

    private void RunTask(AutoCommon task, Action onCompleted)
    {
        currentTask = task;
        Svc.Automation.Start(task, OnCompleted: () =>
        {
            if (!ReferenceEquals(currentTask, task))
            {
                Diag($"{task.GetType().Name} finished but is no longer the current task (stopped, paused, or superseded); skipping hand-off.");
                return;
            }

            currentTask = null;
            onCompleted();
        });
    }
}

internal enum HuntPhase { Idle, Reading, PickingUp, Travelling, Searching, Fighting, Upkeep, Finishing, Paused }
