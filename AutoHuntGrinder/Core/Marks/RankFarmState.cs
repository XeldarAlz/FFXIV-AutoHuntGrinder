using AutoHuntGrinder.Core.Hunts;

namespace AutoHuntGrinder.Core.Marks;

// Kept by the session across Pause/Resume. Each mark gets one attempt on each world; a miss is not a kill.
internal sealed class RankFarmState(HuntMark[] marks, uint[] worlds)
{
    private readonly HashSet<(uint World, uint Name)> attempted = [];
    private readonly HashSet<(uint World, uint Name)> killed = [];

    public HuntMark[] Marks { get; } = marks;
    public uint[] Worlds { get; } = worlds;
    public int WorldIndex { get; private set; }
    public bool Finished => WorldIndex >= Worlds.Length;
    public uint World => Finished ? 0 : Worlds[WorldIndex];
    public int TotalKills => killed.Count;
    public int FailedTargets { get; private set; }

    public static bool Matches(in HuntMark mark, bool rankA, bool rankS, IReadOnlySet<ExpansionKind> expansions)
        => expansions.Contains(mark.Expansion) && (mark.Rank == HuntMarkRank.A && rankA || mark.Rank == HuntMarkRank.S && rankS);

    public bool Tracks(uint world, uint nameId) => !Finished && world == World && Marks.Any(mark => mark.NameId == nameId);
    public ushort Killed(uint nameId) => (ushort)(killed.Contains((World, nameId)) ? 1 : 0);
    public bool Attempted(uint nameId) => attempted.Contains((World, nameId)) || Killed(nameId) != 0;

    public void Credit(uint world, uint nameId)
    {
        if (Tracks(world, nameId)) killed.Add((world, nameId));
    }

    public void CompleteAttempt(uint nameId, bool failed)
    {
        if (attempted.Add((World, nameId)) && failed) FailedTargets++;
    }

    public void AdvanceWorld()
    {
        if (!Finished) WorldIndex++;
    }

    public HuntObjective[] Pending() => Marks
        .Select((mark, index) => new HuntObjective(ObjectiveSource.RankFarm, checked((ushort)(WorldIndex * Marks.Length + index)), mark.NameId, mark.TerritoryId, 1, Killed(mark.NameId)))
        .Where(objective => !Attempted(objective.NameId))
        .ToArray();
}
