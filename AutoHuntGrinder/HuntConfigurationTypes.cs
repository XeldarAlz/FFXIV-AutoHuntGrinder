namespace AutoHuntGrinder;

public sealed class CustomMobEntry
{
    public uint NameId { get; set; }
    public ushort Needed { get; set; } = 1;
    // Kept across reloads so a long list can be worked over several sessions; the list editor resets it.
    public ushort Killed { get; set; }
    // 0 lets a run choose among every zone with known spawns.
    public uint PinnedTerritoryId { get; set; }
    public bool Enabled { get; set; } = true;
}
