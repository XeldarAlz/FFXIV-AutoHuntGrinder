using AutoHuntGrinder.Core.Stats;

namespace AutoHuntGrinder.Core.Tasks;

internal sealed partial class AutoHuntController
{
    private void EndRun(AutoHuntSession owningSession)
    {
        FinalizeRun(owningSession);
        if (ReferenceEquals(session, owningSession))
        {
            session = null;
            activeBills = [];
        }

        Phase = HuntPhase.Idle;
    }

    // Idempotent through Recorded, so an explicit Stop and a finished task can both call it.
    private void FinalizeRun(AutoHuntSession? ending)
    {
        if (ending is null || ending.Recorded)
        {
            return;
        }

        ending.Recorded = true;
        if (ending.DidNothing)
        {
            return;
        }

        try
        {
            var billNames = new List<string>(activeBills.Length);
            for (var index = 0; index < activeBills.Length; index++)
            {
                billNames.Add(activeBills[index].Name);
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
                BillNames = billNames,
            };
            Plugin.Instance.History.Append(record);
            Diag($"Run recorded to history: {record.BillsCompleted} bills, {record.MarksKilled} marks over {record.Duration}.");
        }
        catch (Exception exception)
        {
            Diag($"FinalizeRun failed to record history: {exception.Message}");
        }
    }
}
