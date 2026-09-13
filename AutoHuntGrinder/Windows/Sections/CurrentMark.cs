using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Tasks;

namespace AutoHuntGrinder.Windows.Sections;

// The composed texts are rebuilt only when the mark, a count or the language changes, so drawing them every frame
// allocates nothing.
internal static class CurrentMark
{
    private static byte cachedMarkIndex = byte.MaxValue;
    private static uint cachedTargetRowId;
    private static int cachedKilled = -1;
    private static int cachedNeeded = -1;
    private static LanguageInfo? cachedLanguage;
    private static string cachedLine = string.Empty;
    private static string cachedKills = string.Empty;

    public readonly record struct View(string Name, string ZoneName, string Line, string Kills);

    public static bool TryGet(AutoHuntController controller, out View view)
    {
        var progress = controller.Progress;
        if (!controller.Running || !progress.HasMark)
        {
            view = default;
            return false;
        }

        var markIndex = progress.Bill.MarkIndex;
        var target = progress.Target;
        var (killed, needed) = LiveKills(markIndex, target);
        Compose(markIndex, target, killed, needed);
        view = new View(target.Name, target.ZoneName, cachedLine, cachedKills);
        return true;
    }

    // A bill that no longer lists the mark was completed by its last kill.
    private static (int Killed, int Needed) LiveKills(byte markIndex, in HuntTarget target)
    {
        MarkBillReader.Refresh();
        return MarkBillReader.TryFindTarget(markIndex, target.TargetRowId, out var listed)
            ? (Math.Min(listed.Killed, listed.Needed), listed.Needed)
            : (target.Needed, target.Needed);
    }

    private static void Compose(byte markIndex, in HuntTarget target, int killed, int needed)
    {
        var language = Loc.Current;
        if (markIndex == cachedMarkIndex
            && target.TargetRowId == cachedTargetRowId
            && killed == cachedKilled
            && needed == cachedNeeded
            && ReferenceEquals(language, cachedLanguage))
        {
            return;
        }

        cachedMarkIndex = markIndex;
        cachedTargetRowId = target.TargetRowId;
        cachedKilled = killed;
        cachedNeeded = needed;
        cachedLanguage = language;
        cachedKills = Loc.T(L.Progress.Kills, killed, needed);
        cachedLine = Loc.T(L.Progress.MarkLine, target.Name, target.ZoneName, killed, needed);
    }
}
