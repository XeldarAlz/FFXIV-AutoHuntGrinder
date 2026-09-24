using AutoHuntGrinder.Core.Ipc;
using clib.TaskSystem;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

public abstract partial class AutoCommon : TaskBase
{
    private const int DelayPollFrames = 2;
    private const int WaitPollFrames = 30;
    private const int NavmeshPollFrames = 120;

    protected void Diag(string message, [CallerFilePath] string callerFile = "") => RunLog.Info(message, callerFile);

    protected void Warn(string message, [CallerFilePath] string callerFile = "") => RunLog.Warning(message, callerFile);

    protected new async Task DelayMs(int milliseconds)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return;
            }

            await NextFrame(DelayPollFrames);
        }
    }

    // Pinned every frame because the movement library overwrites Status with raw coordinates mid-teleport.
    protected async Task RunWithStatusPinned(string label, Func<Task> work)
    {
        Status = label;
        IFramework.OnUpdateDelegate pin = _ => Status = label;
        Svc.Framework.Update += pin;
        try
        {
            await work();
        }
        finally
        {
            Svc.Framework.Update -= pin;
        }
    }

    protected async Task<bool> WaitUntilTimed(Func<bool> condition, int timeoutMs, string scope, int checkFrames = WaitPollFrames)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var threw = false;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            if (ConditionHolds(condition, scope, ref threw))
            {
                return true;
            }

            await NextFrame(checkFrames);
        }

        Diag($"WAIT TIMEOUT: '{scope}' not satisfied within {timeoutMs / TimeUnits.MillisecondsPerSecond}s");
        return false;
    }

    // After a teleport the destination mesh is still building, and pathfind queries issued now race it and fault.
    protected async Task WaitForNavmeshReady(int timeoutMs, int pollFrames = NavmeshPollFrames)
    {
        var navmesh = NavmeshIPC.Instance;
        if (navmesh.IsReady())
        {
            return;
        }

        var deadline = Environment.TickCount64 + timeoutMs;
        while (!navmesh.IsReady())
        {
            if (CancelToken.IsCancellationRequested)
            {
                return;
            }

            if (Environment.TickCount64 >= deadline)
            {
                Diag($"WAIT TIMEOUT: navmesh not ready within {timeoutMs / TimeUnits.MillisecondsPerSecond}s; proceeding anyway");
                return;
            }

            var progress = navmesh.BuildProgress();
            Status = progress is >= 0f and <= 1f
                ? $"Please wait, the navmesh is loading ({progress * 100f:F0}%)"
                : "Please wait, the navmesh is loading…";
            await NextFrame(pollFrames);
        }
    }

    private bool ConditionHolds(Func<bool> condition, string scope, ref bool threw)
    {
        try
        {
            return condition();
        }
        catch (Exception exception)
        {
            if (!threw)
            {
                Warn($"WaitUntilTimed '{scope}' condition threw (treating as unsatisfied; will retry until timeout): {exception.Message}");
                threw = true;
            }

            return false;
        }
    }
}
