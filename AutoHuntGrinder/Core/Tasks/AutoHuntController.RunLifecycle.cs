using AutoHuntGrinder.Core.Stats;

namespace AutoHuntGrinder.Core.Tasks;

internal sealed partial class AutoHuntController
{
    private const int MaxFaultResumes = 3;
    private const long FaultResumeWindowMs = 5 * 60_000;
    private const long MillisecondsPerMinute = 60_000;

    private int faultResumeCount;
    private long faultWindowStartedAtMs;

    private void OnHuntEnded(AutoHuntSession owningSession)
    {
        if (ReferenceEquals(session, owningSession)
            && owningSession.EndedWithFault
            && !owningSession.CompletedByStopCondition
            && TryAutoResumeAfterFault(owningSession))
        {
            return;
        }

        EndRun(owningSession);
    }

    private void EndRun(AutoHuntSession owningSession)
    {
        FinalizeRun(owningSession);
        if (!ReferenceEquals(session, owningSession))
        {
            Diag("A run that is no longer live ended; it was recorded and the current run is left alone.");
            return;
        }

        session = null;
        activeBills = [];
        Phase = HuntPhase.Idle;
        MaybeRunAfterAction(owningSession);
    }

    private void MaybeRunAfterAction(AutoHuntSession ending)
    {
        if (ending.AfterActionDispatched)
        {
            return;
        }

        if (!ending.CompletedByStopCondition || ending.EndedWithFault)
        {
            Diag($"Run ended without meeting its stop condition (fault {ending.EndedWithFault}); no after-run action.");
            return;
        }

        ending.AfterActionDispatched = true;
        var action = Plugin.Instance.Configuration.AfterRun;
        if (action == AfterRunAction.StayLoggedIn)
        {
            Diag("Run completed by its stop condition; the after-run action is StayLoggedIn, nothing to do.");
            return;
        }

        if (ending.DidNothing)
        {
            Diag($"Run ended by its stop condition without doing any work; skipping after-run action {action}.");
            return;
        }

        Diag($"Run completed by its stop condition; starting after-run action {action}.");
        Phase = HuntPhase.Finishing;
        AutoCommon task = action == AfterRunAction.ReturnToInn ? new AutoReturnToInn() : new AutoAfterRun(action);
        RunTask(task, () =>
        {
            Diag($"After-run action {action} finished.");
            Phase = HuntPhase.Idle;
        });
    }

    // Idempotent through Recorded, so an explicit Stop and a finished task can both call it.
    private void FinalizeRun(AutoHuntSession? ending)
    {
        if (ending is null || ending.Recorded)
        {
            return;
        }

        ending.Recorded = true;
        try
        {
            ending.Sample();
            if (ending.DidNothing)
            {
                Diag("Run did no work; nothing recorded to history.");
                return;
            }

            var record = new RunRecord
            {
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
            Diag($"Run recorded to history: {record.BillsCompleted} bills, {record.MarksKilled} marks, {record.AlliedSeals} allied seals, {record.CenturioSeals} centurio seals, {record.Nuts} nuts over {record.Duration} as {record.JobAbbreviation}.");
        }
        catch (Exception exception)
        {
            Diag($"FinalizeRun failed to record history: {exception.Message}");
        }
    }

    private void ResetFaultBudget()
    {
        faultResumeCount = 0;
        faultWindowStartedAtMs = 0;
    }

    // The window restarts once it lapses, so sparse faults over a long run each get a fresh budget; only a burst, a wedge
    // that faults again straight away, spends it and lets the run end for real.
    private bool TryAutoResumeAfterFault(AutoHuntSession owningSession)
    {
        if (!Plugin.Instance.Configuration.AutoResumeOnFault)
        {
            Diag("Hunt task faulted and auto-resume on fault is off; the run ends.");
            return false;
        }

        if (activeBills.Length == 0)
        {
            Diag("Hunt task faulted with no bills to resume; the run ends.");
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
            Diag($"Hunt task faulted {faultResumeCount} times within {FaultResumeWindowMs / MillisecondsPerMinute} minutes; not resuming. The run ends.");
            return false;
        }

        faultResumeCount++;
        owningSession.ClearFault();
        Diag($"Hunt task ended on an unexpected fault; auto-resuming (resume {faultResumeCount}/{MaxFaultResumes} in this {FaultResumeWindowMs / MillisecondsPerMinute} minute window).");
        ECommons.DalamudServices.Svc.Chat.Print($"{AhgConstants.LogPrefix} The hunt stopped on an unexpected error; restarting it ({faultResumeCount}/{MaxFaultResumes}).");
        StartHunt(owningSession);
        return true;
    }
}
