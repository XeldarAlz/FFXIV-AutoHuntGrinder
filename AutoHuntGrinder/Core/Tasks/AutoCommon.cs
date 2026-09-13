using clib.TaskSystem;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Tasks;

public abstract class AutoCommon : TaskBase
{
    private const int DelayPollFrames = 2;

    protected void Diag(string message) => Svc.Log.Info($"{AhgConstants.LogPrefix} {message}");

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
}
