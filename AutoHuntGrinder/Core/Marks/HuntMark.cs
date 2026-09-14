using AutoHuntGrinder.Core.Hunts;

namespace AutoHuntGrinder.Core.Marks;

public readonly record struct HuntMark(
    uint NameId,
    HuntMarkRank Rank,
    ushort TerritoryId,
    ExpansionKind Expansion);
