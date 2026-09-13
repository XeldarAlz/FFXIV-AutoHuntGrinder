namespace AutoHuntGrinder.Core.Hunts;

public readonly record struct HuntBill(
    byte MarkIndex,
    ExpansionKind Expansion,
    BillCadence Cadence,
    string Name,
    uint IconId,
    uint UnlockQuestId,
    string UnlockQuestName,
    uint OrderStart,
    byte OrderAmount);
