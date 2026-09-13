namespace AutoHuntGrinder.Core.HuntingLog;

// TargetRowId is the MonsterNoteTarget row and NameId its BNpcName row. TargetSlot is the target's place in its entry
// (0 to 3), the index of its kill count. FirstZone indexes the registry's flat zone table, and LocationPlaceId is the
// PlaceName of the sub-area in the first zone. InDuty marks a target that lives inside a dungeon.
public readonly record struct HuntingLogTarget(
    uint TargetRowId,
    uint NameId,
    byte Needed,
    byte TargetSlot,
    ushort FirstZone,
    byte ZoneCount,
    uint LocationPlaceId,
    bool InDuty);
