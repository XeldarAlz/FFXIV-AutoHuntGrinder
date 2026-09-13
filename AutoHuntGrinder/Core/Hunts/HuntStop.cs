namespace AutoHuntGrinder.Core.Hunts;

public readonly record struct HuntStop(HuntBill Bill, HuntTarget Target, uint TerritoryId, uint FateId)
{
    public bool IsFateBound => FateId != 0;
}
