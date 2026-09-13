namespace AutoHuntGrinder;

public enum RepairMode
{
    SelfThenNpc,
    SelfOnly,
    NpcOnly,
}

public enum PartyInviteReplyChannel
{
    Tell,
    Say,
    Yell,
}

// Coordinates are scalars so the configuration serializes without a vector converter.
public sealed class RepairNpc
{
    public uint TerritoryId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public uint DataId { get; set; }
    public string Name { get; set; } = "";
}

public sealed class ConsumableEntry
{
    public uint ItemId { get; set; }
    public string Name { get; set; } = "";
    public uint StatusId { get; set; }
    public bool CanBeHq { get; set; }
}
