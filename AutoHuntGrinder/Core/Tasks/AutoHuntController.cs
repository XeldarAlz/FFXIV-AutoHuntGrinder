using AutoHuntGrinder.Core.Custom;
using AutoHuntGrinder.Core.External;
using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Ipc;
using clib.Services;

namespace AutoHuntGrinder.Core.Tasks;

internal sealed partial class AutoHuntController
{
    private const int SessionSampleIntervalMs = 1_000;

    private readonly HuntProgress progress = new();

    private AutoHuntSession? session;
    private HuntBill[] activeBills = [];
    private byte[] activeLogSlots = [];
    private AutoCommon? currentTask;
    private long nextSessionSampleAtMs;

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

    public HuntMode Mode => session?.Mode ?? HuntMode.MarkBills;

    public IReadOnlyList<HuntBill> ActiveBills => activeBills;

    public IReadOnlyList<byte> ActiveHuntingLogSlots => activeLogSlots;

    public IReadOnlyList<HuntObjective> Objectives => progress.Objectives;

    private static void Diag(string message)
        => ECommons.DalamudServices.Svc.Log.Info($"{AhgConstants.LogPrefix} {message}");

    public void Start(IReadOnlyList<HuntBill> bills)
    {
        if (bills.Count == 0)
        {
            Diag("Start aborted: no bills selected.");
            return;
        }

        if (!RequiredPluginsReady())
        {
            return;
        }

        activeBills = [.. bills];
        activeLogSlots = [];
        BeginRun(new AutoHuntSession(activeBills), $"{activeBills.Length} bill(s)");
    }

    public void StartHuntingLog(IReadOnlyList<byte> slots)
    {
        if (slots.Count == 0)
        {
            Diag("Start aborted: no Hunting Log queued.");
            return;
        }

        if (!RequiredPluginsReady())
        {
            return;
        }

        activeBills = [];
        activeLogSlots = [.. slots];
        BeginRun(new AutoHuntSession(activeLogSlots), $"{activeLogSlots.Length} Hunting Log(s)");
    }

    public void StartCustomList()
    {
        var pending = CustomMobList.CountNeedingKills();
        if (pending == 0)
        {
            Diag("Start aborted: no enabled custom mob needs kills.");
            return;
        }

        if (!RequiredPluginsReady())
        {
            return;
        }

        activeBills = [];
        activeLogSlots = [];
        BeginRun(new AutoHuntSession(Plugin.Instance.Configuration.CustomMobs), $"{pending} custom mob(s)");
    }

    public void Stop()
    {
        var ending = session;
        var wasPaused = Paused;
        currentTask = null;
        PauseReason = PauseReason.None;
        Svc.Automation.Stop();
        if (ending is not null)
        {
            ReleaseHelpers();
        }

        // Pause already credited the run, and anything done since was the player's own play.
        FinalizeRun(ending, sample: !wasPaused);
        ClearRun();
        if (ending is not null)
        {
            Diag("Stop requested; session cleared.");
        }
    }

    // Credits kills as they land, so the stat tiles keep pace with the live kill counts. A paused run is left alone,
    // because Resume makes the game state its new zero point.
    public void Tick()
    {
        if (session is null || session.Recorded || Paused)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now < nextSessionSampleAtMs)
        {
            return;
        }

        nextSessionSampleAtMs = now + SessionSampleIntervalMs;
        session.Sample();
    }

    private static bool RequiredPluginsReady()
    {
        if (ExternalPlugins.AllRequiredInstalled())
        {
            return true;
        }

        var missing = ExternalPlugins.MissingRequiredNames();
        Diag($"Start aborted: required plugins missing ({missing}).");
        ECommons.DalamudServices.Svc.Chat.PrintError($"{AhgConstants.LogPrefix} Cannot start: install all required plugins first ({missing}).");
        return false;
    }

    private void BeginRun(AutoHuntSession newSession, string plan)
    {
        PauseReason = PauseReason.None;
        ResetFaultBudget();
        session = newSession;
        Diag($"Run starting: {plan}, job {newSession.JobAbbreviation}.");
        StartHunt(newSession);
    }

    private void StartHunt(AutoHuntSession owningSession)
    {
        progress.Reset();
        progress.SetPhase(HuntPhase.Reading);
        RunTask(CreateRunTask(owningSession), () => OnHuntEnded(owningSession));
    }

    private AutoCommon CreateRunTask(AutoHuntSession owningSession) => owningSession.Mode switch
    {
        HuntMode.HuntingLog => new AutoHuntingLog(activeLogSlots, owningSession, progress),
        HuntMode.CustomList => new AutoCustomHunt(owningSession, progress),
        _                   => new AutoHunt(activeBills, owningSession, progress),
    };

    // Resume and a fault restart rebuild the task from what the run started with; a custom run rereads the list itself.
    private bool CanRestart(AutoHuntSession run) => run.Mode switch
    {
        HuntMode.HuntingLog => activeLogSlots.Length > 0,
        HuntMode.CustomList => true,
        _                   => activeBills.Length > 0,
    };

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

    private void ClearRun()
    {
        session = null;
        activeBills = [];
        activeLogSlots = [];
        progress.Reset();
    }

    // A stopped task unwinds on a later frame, and after an unload that frame may never come, so the combat preset and
    // the pathfinder are released here as well.
    private static void ReleaseHelpers()
    {
        BossModIPC.Instance.ClearActive();
        BossModIPC.Instance.ReleaseMovement();
        NavmeshIPC.Instance.Stop();
    }
}

internal enum HuntPhase { Idle, Reading, PickingUp, Travelling, Searching, Fighting, Upkeep, Finishing, Paused }
