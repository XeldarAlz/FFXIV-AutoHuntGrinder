using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;

namespace AutoHuntGrinder.Core.Ipc;

// IPC contract: NightmareXIV/Lifestream, Lifestream/IPC/IPCProvider.cs.
internal sealed class LifestreamIPC
{
    private static LifestreamIPC? instance;
    public static LifestreamIPC Instance => instance ??= new();

    private readonly ICallGateSubscriber<uint, bool> changeWorld = Svc.PluginInterface.GetIpcSubscriber<uint, bool>("Lifestream.ChangeWorldById");
    private readonly ICallGateSubscriber<bool> isBusy = Svc.PluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
    private readonly ICallGateSubscriber<object> abort = Svc.PluginInterface.GetIpcSubscriber<object>("Lifestream.Abort");

    public bool Available => changeWorld.HasFunction && isBusy.HasFunction && abort.HasAction;
    // A missing or failed busy check must never look like a completed journey.
    public bool IsBusy() => IpcGate.Invoke(isBusy.HasFunction, isBusy.InvokeFunc, true, "Lifestream IsBusy failed");
    public bool ChangeWorld(uint world) => IpcGate.Invoke(changeWorld.HasFunction, () => changeWorld.InvokeFunc(world), false, "Lifestream ChangeWorld failed");
    public void Abort() => IpcGate.Run(abort.HasAction, abort.InvokeAction, "Lifestream Abort failed");
}
