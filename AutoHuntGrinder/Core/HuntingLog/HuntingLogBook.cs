namespace AutoHuntGrinder.Core.HuntingLog;

public readonly record struct HuntingLogBook(
    byte Slot,
    HuntingLogKind Kind,
    byte OwnerRowId,
    uint RowBase,
    byte RankCount,
    uint AchievementId);
