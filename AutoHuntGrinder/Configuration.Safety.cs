using AutoHuntGrinder.Core.Travel;
using Newtonsoft.Json;

namespace AutoHuntGrinder;

public sealed partial class Configuration
{
    public bool AutoRepair { get; set; } = false;
    public int AutoRepairThresholdPct { get; set; } = 20;
    public RepairMode RepairMode { get; set; } = RepairMode.SelfThenNpc;
    // Null sends NPC repairs to the Grand Company mender.
    public RepairNpc? PreferredRepairNpc { get; set; }

    public bool AutoConsume { get; set; } = false;
    // 0 re-eats only once the buff has fully worn off.
    public int AutoConsumeMinMinutes { get; set; } = 3;
    public List<ConsumableEntry> AutoConsumeItems { get; set; } = [];

    public bool HumanizerEnabled { get; set; } = false;
    public int HumanizerMarksBeforeBreak { get; set; } = 30;
    public int HumanizerBreakMinMinutes { get; set; } = 5;
    public int HumanizerBreakMaxMinutes { get; set; } = 10;
    public int HumanizerPauseMinSec { get; set; } = 3;
    public int HumanizerPauseMaxSec { get; set; } = 8;
    public int HumanizerWanderMinMeters { get; set; } = 25;
    public int HumanizerWanderMaxMeters { get; set; } = 80;

    // Replace, because by default the loader adds the saved cities into this pre-filled set and every unticked city would come back.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public HashSet<uint> HumanizerCities { get; set; } = BreakCities.NewDefaultSelection();

    public bool DeclinePartyInvites { get; set; } = false;
    public int DeclineInviteDelayMinSec { get; set; } = 2;
    public int DeclineInviteDelayMaxSec { get; set; } = 6;
    public bool DeclineInviteReply { get; set; } = false;
    public PartyInviteReplyChannel DeclineInviteReplyChannel { get; set; } = PartyInviteReplyChannel.Tell;
    public string DeclineInviteReplyMessage { get; set; } = "";

    public bool GmAlertStopRun { get; set; } = true;
    public bool GmAlertToast { get; set; } = false;
    public bool GmAlertChat { get; set; } = false;
    public bool GmAlertSound { get; set; } = false;
    public int GmAlertBeepCount { get; set; } = 3;
    public int GmAlertBeepDurationMs { get; set; } = 250;
    public int GmAlertBeepFrequencyHz { get; set; } = 900;
    public bool GmAlertKillGame { get; set; } = false;
    public List<string> GmAlertCommands { get; set; } = [];
}
