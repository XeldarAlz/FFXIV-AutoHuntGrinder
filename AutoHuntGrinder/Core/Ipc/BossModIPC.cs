using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;

namespace AutoHuntGrinder.Core.Ipc;

// Both editions of the combat plugin register the same call gates, so either one answers here.
internal sealed class BossModIPC
{
    private const string SetActiveFailed = AhgConstants.LogPrefix + " BossMod SetActive failed";
    private const string ClearActiveFailed = AhgConstants.LogPrefix + " BossMod ClearActive failed";
    private const string GetActiveFailed = AhgConstants.LogPrefix + " BossMod GetActive failed";
    private const string GetPresetFailed = AhgConstants.LogPrefix + " BossMod GetPreset failed";
    private const string AddTransientFailed = AhgConstants.LogPrefix + " BossMod AddTransientStrategy failed";
    private const string ClearTransientFailed = AhgConstants.LogPrefix + " BossMod ClearTransientStrategy failed";
    private const string CreatePresetFailed = AhgConstants.LogPrefix + " BossMod CreatePreset failed";

    private static BossModIPC? instance;

    private readonly ICallGateSubscriber<string, bool> setActive;
    private readonly ICallGateSubscriber<bool> clearActive;
    private readonly ICallGateSubscriber<string> getActive;
    private readonly ICallGateSubscriber<string, string?> getPreset;
    private readonly ICallGateSubscriber<string, string, string, string, bool> addTransient;
    private readonly ICallGateSubscriber<string, string, string, bool> clearTransient;
    private readonly ICallGateSubscriber<string, bool, bool> createPreset;

    // Cached once so the fight loop's per-tick checks do not allocate a delegate on every call.
    private readonly Func<bool> clearActiveCall;
    private readonly Func<string?> getActiveCall;

    private BossModIPC()
    {
        var pluginInterface = Svc.PluginInterface;
        setActive = pluginInterface.GetIpcSubscriber<string, bool>("BossMod.Presets.SetActive");
        clearActive = pluginInterface.GetIpcSubscriber<bool>("BossMod.Presets.ClearActive");
        getActive = pluginInterface.GetIpcSubscriber<string>("BossMod.Presets.GetActive");
        getPreset = pluginInterface.GetIpcSubscriber<string, string?>("BossMod.Presets.Get");
        addTransient = pluginInterface.GetIpcSubscriber<string, string, string, string, bool>("BossMod.Presets.AddTransientStrategy");
        clearTransient = pluginInterface.GetIpcSubscriber<string, string, string, bool>("BossMod.Presets.ClearTransientStrategy");
        createPreset = pluginInterface.GetIpcSubscriber<string, bool, bool>("BossMod.Presets.Create");

        clearActiveCall = clearActive.InvokeFunc;
        getActiveCall = getActive.InvokeFunc;
    }

    public static BossModIPC Instance => instance ??= new BossModIPC();

    public bool IsAvailable => setActive.HasFunction;

    public bool CanClearTransientStrategy => clearTransient.HasFunction;

    public bool SetActive(string presetName)
        => IpcGate.Invoke(setActive.HasFunction, () => setActive.InvokeFunc(presetName), false, SetActiveFailed);

    public bool ClearActive()
        => IpcGate.Invoke(clearActive.HasFunction, clearActiveCall, false, ClearActiveFailed);

    public string? GetActive()
        => IpcGate.Invoke(getActive.HasFunction, getActiveCall, null, GetActiveFailed);

    public string? GetPreset(string name)
        => IpcGate.Invoke(getPreset.HasFunction, () => getPreset.InvokeFunc(name), null, GetPresetFailed);

    public bool AddTransientStrategy(string preset, string module, string track, string option)
        => IpcGate.Invoke(addTransient.HasFunction, () => addTransient.InvokeFunc(preset, module, track, option), false, AddTransientFailed);

    public bool ClearTransientStrategy(string preset, string module, string track)
        => IpcGate.Invoke(clearTransient.HasFunction, () => clearTransient.InvokeFunc(preset, module, track), false, ClearTransientFailed);

    public bool CreatePreset(string serialized, bool overwrite)
        => IpcGate.Invoke(createPreset.HasFunction, () => createPreset.InvokeFunc(serialized, overwrite), false, CreatePresetFailed);
}
