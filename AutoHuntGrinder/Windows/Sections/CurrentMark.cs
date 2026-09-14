using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Marks;
using AutoHuntGrinder.Core.Tasks;
using AutoHuntGrinder.Core.Travel;

namespace AutoHuntGrinder.Windows.Sections;

// The composed texts are rebuilt only when the mark or objective, a count or the language changes, so drawing them every
// frame allocates nothing.
internal static class CurrentMark
{
    private static Identity cachedIdentity;
    private static LanguageInfo? cachedLanguage;
    private static string cachedName = string.Empty;
    private static string cachedZoneName = string.Empty;
    private static string cachedLine = string.Empty;
    private static string cachedKills = string.Empty;
    private static HuntMarkRank? cachedRank;

    // Rank is set only for a hunt mark on the custom list.
    public readonly record struct View(string Name, string ZoneName, string Line, string Kills, HuntMarkRank? Rank);

    // A mark is keyed by its bill and target row, an objective by its source key and the zone it was planned in.
    private readonly record struct Identity(bool Objective, uint Owner, uint Id, int Killed, int Needed);

    public static bool TryGet(AutoHuntController controller, out View view)
    {
        var progress = controller.Progress;
        if (!controller.Running || (!progress.HasMark && !progress.HasObjective))
        {
            view = default;
            return false;
        }

        if (progress.HasMark)
        {
            ComposeMark(progress.Bill.MarkIndex, progress.Target);
        }
        else
        {
            ComposeObjective(progress.Objective);
        }

        view = new View(cachedName, cachedZoneName, cachedLine, cachedKills, cachedRank);
        return true;
    }

    // An objective planned without a zone is hunted wherever its spawns are known.
    public static string ZoneName(uint territoryId) => territoryId == 0 ? Loc.T(L.CustomList.AnyZone) : TerritoryNames.Of(territoryId);

    // A bill that no longer lists the mark was completed by its last kill.
    private static void ComposeMark(byte markIndex, in HuntTarget target)
    {
        MarkBillReader.Refresh();
        var (killed, needed) = MarkBillReader.TryFindTarget(markIndex, target.TargetRowId, out var listed)
            ? (Math.Min(listed.Killed, listed.Needed), listed.Needed)
            : (target.Needed, target.Needed);
        if (Unchanged(new Identity(false, markIndex, target.TargetRowId, killed, needed)))
        {
            return;
        }

        Compose(target.Name, target.ZoneName, killed, needed, null);
    }

    private static void ComposeObjective(in HuntObjective objective)
    {
        ObjectiveProgress.Refresh(objective.Source);
        var needed = ObjectiveProgress.Needed(objective);
        var killed = Math.Min(ObjectiveProgress.Killed(objective), needed);
        if (Unchanged(new Identity(true, ObjectiveProgress.Key(objective), objective.TerritoryId, killed, needed)))
        {
            return;
        }

        Compose(ObjectiveProgress.Name(objective), ZoneName(objective.TerritoryId), killed, needed, HuntMarkRegistry.RankOf(objective));
    }

    private static bool Unchanged(in Identity identity)
    {
        var language = Loc.Current;
        if (identity == cachedIdentity && ReferenceEquals(language, cachedLanguage))
        {
            return true;
        }

        cachedIdentity = identity;
        cachedLanguage = language;
        return false;
    }

    private static void Compose(string name, string zoneName, int killed, int needed, HuntMarkRank? rank)
    {
        cachedName = name;
        cachedZoneName = zoneName;
        cachedRank = rank;
        cachedKills = Loc.T(L.Progress.Kills, killed, needed);
        cachedLine = Loc.T(L.Progress.MarkLine, name, zoneName, killed, needed);
    }
}
