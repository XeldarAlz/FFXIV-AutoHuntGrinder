using Dalamud.Configuration;
using ECommons.Throttlers;

namespace AutoHuntGrinder;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; }

    public bool AutoShowOnLogin { get; set; } = false;

    public string Language { get; set; } = "";

    // MobHuntOrderType row ids, which double as the MobHunt mark indices.
    public HashSet<byte> SelectedBills { get; set; } = [];

    // Never fires on a manual Stop or a fault.
    public AfterRunAction AfterRun { get; set; } = AfterRunAction.StayLoggedIn;

    public bool AutoPauseInContent { get; set; } = true;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    public void SaveDebounced()
    {
        if (EzThrottler.Throttle(Core.AhgConstants.ThrottleKeys.Save, Core.AhgConstants.SaveThrottleMs))
        {
            Save();
        }
    }
}
