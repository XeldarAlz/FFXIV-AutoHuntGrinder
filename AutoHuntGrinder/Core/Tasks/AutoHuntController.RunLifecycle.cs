using AutoHuntGrinder.Core.Stats;

namespace AutoHuntGrinder.Core.Tasks;

internal sealed partial class AutoHuntController
{
    private const int MaxFaultResumes = 3;
    private const int MaxFaultResumesPerRun = 6;
    private const long FaultResumeWindowMs = 5 * TimeUnits.MillisecondsPerMinute;

    private int faultResumeCount;
    private int runFaultResumeCount;
    private long faultWindowStartedAtMs;

    private void OnHuntEnded(AutoHuntSession owningSession)
    {
        var faulted = ReferenceEquals(session, owningSession) && owningSession.EndedWithFault;
        if (faulted && !owningSession.CompletedByStopCondition && TryAutoResumeAfterFault(owningSession))
        {
            return;
        }

        if (faulted)
        {
            ECommons.DalamudServices.Svc.Chat.PrintError($"{AhgConstants.LogPrefix} The hunt stopped on an unexpected error. The log has the details.");
        }

        EndRun(owningSession);
    }

    private void EndRun(AutoHuntSession owningSession)
    {
        FinalizeRun(owningSession, sample: true);
        if (!ReferenceEquals(session, owningSession))
        {
            Diag("A run that is no longer live ended; it was recorded and the current run is left alone.");
            return;
        }

        progress.ClearMark();
        progress.ClearRoute();
        progress.ClearObjective();
        progress.ClearObjectives();
        if (!TryRunAfterAction(owningSession))
        {
            ClearRun();
        }
    }

    // The finished run stays on screen while its after-run action runs, and is cleared once that ends.
    private bool TryRunAfterAction(AutoHuntSession ending)
    {
        if (ending.AfterActionDispatched)
        {
            return false;
        }

        if (!ending.CompletedByStopCondition || ending.EndedWithFault)
        {
            Diag($"Run ended without meeting its stop condition (fault {ending.EndedWithFault}); no after-run action.");
            return false;
        }

        ending.AfterActionDispatched = true;
        var action = Plugin.Instance.Configuration.AfterRun;
        if (action == AfterRunAction.StayLoggedIn)
        {
            Diag("Run completed by its stop condition; the after-run action is StayLoggedIn, nothing to do.");
            return false;
        }

        if (ending.DidNothing)
        {
            Diag($"Run ended by its stop condition without doing any work; skipping after-run action {action}.");
            return false;
        }

        Diag($"Run completed by its stop condition; starting after-run action {action}.");
        progress.SetPhase(HuntPhase.Finishing);
        AutoCommon task = action == AfterRunAction.ReturnToInn ? new AutoReturnToInn() : new AutoAfterRun(action);
        RunTask(task, () =>
        {
            Diag($"After-run action {action} finished.");
            ClearRun();
        });
        return true;
    }

    // Idempotent through Recorded, so an explicit Stop and a finished task can both call it.
    private void FinalizeRun(AutoHuntSession? ending, bool sample)
    {
        if (ending is null || ending.Recorded)
        {
            return;
        }

        ending.Recorded = true;
        ending.End();
        try
        {
            if (sample)
            {
                ending.Sample();
            }

            if (ending.DidNothing)
            {
                Diag("Run did no work; nothing recorded to history.");
                return;
            }

            var record = new RunRecord
            {
                Mode = ending.Mode,
                StartedAtUtc = ending.StartedAt,
                EndedAtUtc = DateTime.UtcNow,
                DurationSeconds = ending.Elapsed.TotalSeconds,
                BillsCompleted = ending.BillsCompleted,
                MarksKilled = ending.MarksKilled,
                AlliedSeals = ending.AlliedSeals,
                CenturioSeals = ending.CenturioSeals,
                Nuts = ending.Nuts,
                JobAbbreviation = ending.JobAbbreviation,
                BillNames = [.. ending.BillNames],
            };
            Plugin.Instance.History.Append(record);
            Diag($"Run recorded to history ({record.Mode}): {record.BillsCompleted} bills, {record.MarksKilled} marks, {record.AlliedSeals} allied seals, {record.CenturioSeals} centurio seals, {record.Nuts} nuts over {record.Duration} as {record.JobAbbreviation}.");
        }
        catch (Exception exception)
        {
            Diag($"FinalizeRun failed to record history: {exception.Message}");
        }
    }

    private void ResetFaultBudget()
    {
        faultResumeCount = 0;
        runFaultResumeCount = 0;
        faultWindowStartedAtMs = 0;
    }

    // The window restarts once it lapses, so sparse faults over a long run each get a fresh budget; only a burst, a wedge
    // that faults again straight away, spends it. The per-run cap still ends a run whose faults keep coming, however far
    // apart they are.
    private bool TryAutoResumeAfterFault(AutoHuntSession owningSession)
    {
        if (!Plugin.Instance.Configuration.AutoResumeOnFault)
        {
            Diag("Hunt task faulted and auto-resume on fault is off; the run ends.");
            return false;
        }

        if (!CanRestart(owningSession))
        {
            Diag($"Hunt task faulted with nothing to resume ({owningSession.Mode}); the run ends.");
            return false;
        }

        if (runFaultResumeCount >= MaxFaultResumesPerRun)
        {
            Diag($"Hunt task faulted after {runFaultResumeCount} restarts in this run; not resuming. The run ends.");
            return false;
        }

        var now = Environment.TickCount64;
        if (now - faultWindowStartedAtMs > FaultResumeWindowMs)
        {
            faultResumeCount = 0;
            faultWindowStartedAtMs = now;
        }

        if (faultResumeCount >= MaxFaultResumes)
        {
            Diag($"Hunt task faulted {faultResumeCount} times within {FaultResumeWindowMs / TimeUnits.MillisecondsPerMinute} minutes; not resuming. The run ends.");
            return false;
        }

        faultResumeCount++;
        runFaultResumeCount++;
        owningSession.ClearFault();
        Diag($"Hunt task ended on an unexpected fault; auto-resuming (resume {faultResumeCount}/{MaxFaultResumes} in this {FaultResumeWindowMs / TimeUnits.MillisecondsPerMinute} minute window, {runFaultResumeCount}/{MaxFaultResumesPerRun} in this run).");
        ECommons.DalamudServices.Svc.Chat.Print($"{AhgConstants.LogPrefix} The hunt stopped on an unexpected error; restarting it ({faultResumeCount}/{MaxFaultResumes}).");
        StartHunt(owningSession);
        return true;
    }
}
