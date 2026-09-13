using AutoHuntGrinder.Core.Hunts;

namespace AutoHuntGrinder;

public sealed partial class Configuration
{
    public HuntMode Mode { get; set; } = HuntMode.MarkBills;

    // Hunting Log slots (0 to 11, numbered as ClassJob.MonsterNote and GrandCompany.MonsterNote number them), worked in this order.
    public List<byte> HuntingLogQueue { get; set; } = [];
}
