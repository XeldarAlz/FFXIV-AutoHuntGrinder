using AutoHuntGrinder.Core.Hunts;

namespace AutoHuntGrinder;

public sealed partial class Configuration
{
    public bool FarmARanks { get; set; } = true;
    public bool FarmSRanks { get; set; }
    public HashSet<ExpansionKind> FarmExpansions { get; set; } = [];
    public bool FarmVisitWorlds { get; set; }
}
