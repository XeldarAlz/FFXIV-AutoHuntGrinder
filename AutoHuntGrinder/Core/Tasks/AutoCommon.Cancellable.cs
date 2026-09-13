using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

public abstract partial class AutoCommon
{
    protected const int SubTaskUnwindGraceMs = 5_000;

    private const int CancellablePollFrames = 4;

    // Runs one library operation as its own task under a wall-clock cap. On timeout, or when abortIf trips, it
    // cancels that operation, whose cleanups stop navigation, so the operation genuinely unwinds and cannot fight
    // the next move. True when the operation finished on its own.
    internal async Task<bool> RunCancellable(MoveOp operation, int timeoutMs, string label, Func<bool>? abortIf = null)
    {
        var completion = new TaskCompletionSource();
        operation.Run(() => completion.TrySetResult());
        var work = completion.Task;

        // The operation has its own cancellation source, so a Stop of the whole run must be forwarded to it.
        using var registration = CancelToken.Register(() => TryCancel(operation, label));

        var deadline = Environment.TickCount64 + timeoutMs;
        var aborted = false;
        var abortThrew = false;
        while (!work.IsCompleted && Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested)
            {
                break;
            }

            if (abortIf is not null && ShouldAbort(abortIf, label, ref abortThrew))
            {
                aborted = true;
                break;
            }

            await NextFrame(CancellablePollFrames);
        }

        if (work.IsCompleted)
        {
            return true;
        }

        if (CancelToken.IsCancellationRequested)
        {
            TryCancel(operation, label);
            return false;
        }

        Diag(aborted
            ? $"RunCancellable '{label}' aborting sub-task (abort condition met)"
            : $"WATCHDOG: '{label}' exceeded {timeoutMs / 1000}s; cancelling sub-task");
        TryCancel(operation, label);

        var grace = Environment.TickCount64 + SubTaskUnwindGraceMs;
        while (!work.IsCompleted && Environment.TickCount64 < grace)
        {
            await NextFrame(CancellablePollFrames);
        }

        if (!work.IsCompleted)
        {
            Diag($"WATCHDOG: '{label}' did not unwind {SubTaskUnwindGraceMs / 1000}s after Cancel (unexpected)");
        }

        return false;
    }

    private bool ShouldAbort(Func<bool> abortIf, string label, ref bool abortThrew)
    {
        try
        {
            return abortIf();
        }
        catch (Exception exception)
        {
            if (!abortThrew)
            {
                Warn($"RunCancellable '{label}' abortIf threw (abort disabled, watchdog still active): {exception.Message}");
                abortThrew = true;
            }

            return false;
        }
    }

    private void TryCancel(MoveOp operation, string label)
    {
        // The operation can finish and dispose its cancellation source between our check and here; a finished one needs no cancelling.
        try
        {
            operation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            Warn($"RunCancellable '{label}' Cancel threw: {exception.Message}");
        }
    }
}
