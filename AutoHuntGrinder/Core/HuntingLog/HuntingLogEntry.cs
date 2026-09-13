namespace AutoHuntGrinder.Core.HuntingLog;

// RowId is the MonsterNote row, EntryIndex the entry's place in its rank (0 to 9), and FirstTarget indexes the
// registry's flat target table.
public readonly record struct HuntingLogEntry(
    uint RowId,
    byte Slot,
    byte Rank,
    byte EntryIndex,
    uint Reward,
    ushort FirstTarget,
    byte TargetCount);
