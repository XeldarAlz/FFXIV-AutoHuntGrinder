namespace AutoHuntGrinder.Core.Hunts;

public readonly record struct HuntTarget(
    uint TargetRowId,
    uint NameId,
    string Name,
    uint TerritoryId,
    string ZoneName,
    byte Needed,
    byte Killed)
{
    public bool Done => Killed >= Needed;

    public int Remaining => Needed > Killed ? Needed - Killed : 0;
}
