namespace AutoHuntGrinder.Core.HuntingLog;

public readonly record struct HuntingLogTarget(
    uint NameId,
    byte Needed,
    byte TargetSlot,
    ushort FirstZone,
    byte ZoneCount,
    bool InDuty);
