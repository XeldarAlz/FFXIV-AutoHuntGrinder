using ECommons.DalamudServices;

namespace AutoHuntGrinder.Core.Ipc;

internal static class IpcGate
{
    public static T Invoke<T>(bool hasFunction, Func<T> call, T fallback, string label)
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
            Svc.Log.Warning(exception, label);
            return fallback;
        }
    }

    public static void Run(bool hasFunction, Action call, string label)
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
            Svc.Log.Warning(exception, label);
        }
    }
}
