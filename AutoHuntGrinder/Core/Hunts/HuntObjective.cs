namespace AutoHuntGrinder.Core.Hunts;

public enum ObjectiveSource : byte
{
    HuntingLog,
    Custom,
}

// SourceKey locates the objective in its source: a Hunting Log target packs slot * 1000 + rank * 100 + entry * 10 +
// target slot, and a custom mob is its index in the list.
public readonly record struct HuntObjective(ObjectiveSource Source, ushort SourceKey, uint NameId, uint TerritoryId, ushort Needed, ushort Killed)
{
    public bool Done => Killed >= Needed;

    public int Remaining => Math.Max(0, Needed - Killed);
}
