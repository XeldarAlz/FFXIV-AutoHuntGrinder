namespace AutoHuntGrinder.Core.HuntingLog;

// OwnerRowId is the ClassJob row of a class log and the GrandCompany row of a company log. MonsterNote row ids run
// RowBase + rank * 10 + entry + 1, and FirstEntry indexes the registry's flat entry table.
public readonly record struct HuntingLogBook(
    byte Slot,
    HuntingLogKind Kind,
    byte OwnerRowId,
    uint RowBase,
    byte RankCount,
    uint IconId,
    uint AchievementId,
    ushort FirstEntry);
