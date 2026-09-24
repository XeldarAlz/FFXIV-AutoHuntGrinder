using System.Runtime.CompilerServices;

namespace AutoHuntGrinder.Core.Ipc;

internal static class IpcGate
{
    public static T Invoke<T>(bool hasFunction, Func<T> call, T fallback, string label, [CallerFilePath] string callerFile = "")
    {
        if (!hasFunction)
        {
            return fallback;
        }

        try
        {
            return call();
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, label, callerFile);
            return fallback;
        }
    }

    public static void Run(bool hasFunction, Action call, string label, [CallerFilePath] string callerFile = "")
    {
        if (!hasFunction)
        {
            return;
        }

        try
        {
            call();
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, label, callerFile);
        }
    }
}
