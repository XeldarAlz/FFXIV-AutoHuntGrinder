using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoHuntGrinder.Core.Ipc;

// Both editions of the combat plugin register the same call gates, so either one answers here.
internal sealed class BossModIPC
{
    // Read by reflection so a change to the quality struct cannot break the build.
    private const string QualityIsBadProperty = "IsBad";
    private const string SetActiveFailed = AhgConstants.LogPrefix + " BossMod SetActive failed";
    private const string ClearActiveFailed = AhgConstants.LogPrefix + " BossMod ClearActive failed";
    private const string GetActiveFailed = AhgConstants.LogPrefix + " BossMod GetActive failed";
    private const string GetPresetFailed = AhgConstants.LogPrefix + " BossMod GetPreset failed";
    private const string AddTransientFailed = AhgConstants.LogPrefix + " BossMod AddTransientStrategy failed";
    private const string ClearTransientFailed = AhgConstants.LogPrefix + " BossMod ClearTransientStrategy failed";
    private const string CreatePresetFailed = AhgConstants.LogPrefix + " BossMod CreatePreset failed";
    private const string GenerateObstacleMapFailed = AhgConstants.LogPrefix + " BossMod GenerateObstacleMap failed";
    private const string ObstacleMapStatusFailed = AhgConstants.LogPrefix + " BossMod GetObstacleMapStatus failed";
    private const string HasTempObstacleMapFailed = AhgConstants.LogPrefix + " BossMod HasTempObstacleMap failed";
    private const string ClearTempObstacleMapFailed = AhgConstants.LogPrefix + " BossMod ClearTempObstacleMap failed";
    private const string EvaluateQualityFailed = AhgConstants.LogPrefix + " BossMod EvaluateTempMapQuality failed";

    private static BossModIPC? instance;

    private readonly ICallGateSubscriber<string, bool> setActive;
    private readonly ICallGateSubscriber<bool> clearActive;
    private readonly ICallGateSubscriber<string> getActive;
    private readonly ICallGateSubscriber<string, string?> getPreset;
    private readonly ICallGateSubscriber<string, string, string, string, bool> addTransient;
    private readonly ICallGateSubscriber<string, string, string, bool> clearTransient;
    private readonly ICallGateSubscriber<string, bool, bool> createPreset;
    private readonly ICallGateSubscriber<Vector3, float, bool, bool> obstacleGenerate;
    private readonly ICallGateSubscriber<TaskStatus> obstacleGetStatus;
    private readonly ICallGateSubscriber<bool> obstacleHasTempMap;
    private readonly ICallGateSubscriber<bool> obstacleClearTempMap;
    private readonly ICallGateSubscriber<object?> obstacleEvaluateQuality;

    // Cached once so the fight loop's per-tick checks do not allocate a delegate on every call.
    private readonly Func<bool> clearActiveCall;
    private readonly Func<string?> getActiveCall;
    private readonly Func<TaskStatus?> obstacleGetStatusCall;
    private readonly Func<bool> obstacleHasTempMapCall;
    private readonly Func<bool> obstacleClearTempMapCall;
    private readonly Func<bool> evaluateQualityIsBadCall;

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
        obstacleGenerate = pluginInterface.GetIpcSubscriber<Vector3, float, bool, bool>("BossMod.ObstacleMap.Generate");
        obstacleGetStatus = pluginInterface.GetIpcSubscriber<TaskStatus>("BossMod.ObstacleMap.GetGenerationStatus");
        obstacleHasTempMap = pluginInterface.GetIpcSubscriber<bool>("BossMod.ObstacleMap.HasTempMap");
        obstacleClearTempMap = pluginInterface.GetIpcSubscriber<bool>("BossMod.ObstacleMap.ClearTempMap");
        obstacleEvaluateQuality = pluginInterface.GetIpcSubscriber<object?>("BossMod.ObstacleMap.EvaluateTempMapQuality");

        clearActiveCall = clearActive.InvokeFunc;
        getActiveCall = getActive.InvokeFunc;
        obstacleGetStatusCall = () => obstacleGetStatus.InvokeFunc();
        obstacleHasTempMapCall = obstacleHasTempMap.InvokeFunc;
        obstacleClearTempMapCall = obstacleClearTempMap.InvokeFunc;
        evaluateQualityIsBadCall = ReadQualityIsBad;
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

    public bool GenerateObstacleMap(Vector3 center, float radius, bool writeToFile = false)
        => IpcGate.Invoke(obstacleGenerate.HasFunction, () => obstacleGenerate.InvokeFunc(center, radius, writeToFile), false, GenerateObstacleMapFailed);

    public TaskStatus? GetObstacleMapStatus()
        => IpcGate.Invoke(obstacleGetStatus.HasFunction, obstacleGetStatusCall, null, ObstacleMapStatusFailed);

    public bool HasTempObstacleMap()
        => IpcGate.Invoke(obstacleHasTempMap.HasFunction, obstacleHasTempMapCall, false, HasTempObstacleMapFailed);

    public bool ClearTempObstacleMap()
        => IpcGate.Invoke(obstacleClearTempMap.HasFunction, obstacleClearTempMapCall, false, ClearTempObstacleMapFailed);

    // A missing call gate or property reads as "not bad", so an older combat plugin keeps its map.
    public bool EvaluateTempMapQualityIsBad()
        => IpcGate.Invoke(obstacleEvaluateQuality.HasFunction, evaluateQualityIsBadCall, false, EvaluateQualityFailed);

    private bool ReadQualityIsBad()
    {
        var result = obstacleEvaluateQuality.InvokeFunc();
        if (result is null)
        {
            return false;
        }

        var property = result.GetType().GetProperty(QualityIsBadProperty);
        if (property is null)
        {
            return false;
        }

        return property.GetValue(result) is true;
    }
}
