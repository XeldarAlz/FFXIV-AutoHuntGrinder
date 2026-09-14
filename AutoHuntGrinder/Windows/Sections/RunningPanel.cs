using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Tasks;
using AutoHuntGrinder.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace AutoHuntGrinder.Windows.Sections;

internal static class RunningPanel
{
    private const float PadX = 18f;
    private const int QueueLength = 6;

    private static readonly List<QueueEntry> queue = new(32);
    private static readonly List<HuntObjective> objectiveQueue = new(QueueLength);
    private static readonly Comparison<QueueEntry> byZone = (left, right) => left.Target.TerritoryId.CompareTo(right.Target.TerritoryId);

    private static uint cachedTerritoryId = uint.MaxValue;
    private static string cachedZoneName = string.Empty;

    private readonly record struct QueueEntry(byte MarkIndex, HuntTarget Target, bool Stale);

    public static void Draw(AutoHuntController controller)
    {
        var paused = controller.Paused;
        var (accent, accentSoft, label) = PhasePalette(controller);
        var objectiveRun = controller.Mode != HuntMode.MarkBills;
        var workload = RunWorkload.Measure(controller);

        DrawHeaderStrip(InPlay(controller), accent, accentSoft, paused);
        Styling.VSpace(6f);
        DrawHeroCard(controller, workload, objectiveRun, accent, accentSoft, label);

        Styling.VSpace(10f);
        DrawStatTiles(controller, objectiveRun);

        Styling.VSpace(10f);
        DrawQueueHeading();
        if (objectiveRun)
        {
            DrawObjectiveQueue(controller);
        }
        else
        {
            DrawBillQueue(controller, workload);
        }
    }

    private static string InPlay(AutoHuntController controller)
    {
        var zoneName = CurrentZoneName();
        return controller.Mode switch
        {
            HuntMode.HuntingLog => Loc.Plural(L.Run.LogsInPlay, controller.ActiveHuntingLogSlots.Count, zoneName),
            HuntMode.CustomList => Loc.Plural(L.Run.MobsInPlay, controller.SessionSnapshot?.BillNames.Count ?? 0, zoneName),
            _ => Loc.Plural(L.Run.InPlay, controller.ActiveBills.Count, zoneName),
        };
    }

    private static void DrawHeaderStrip(string footer, Vector4 accent, Vector4 accentSoft, bool paused)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail().X;
        var lineHeight = ImGui.GetTextLineHeight();
        var midY = origin.Y + lineHeight * 0.5f;

        var dot = paused ? accent : Styling.PulseColor(accent, accentSoft, Styling.PulseMedium);
        var radius = 4f * scale;
        Paint.Dot(drawList, new Vector2(origin.X + radius + 3f * scale, midY), radius, dot);

        var status = paused ? Loc.T(L.Shell.StatusPaused) : Loc.T(L.Shell.StatusRunning);
        var statusSize = TextDraw.SmallCapsSize(status);
        TextDraw.SmallCaps(status, new Vector2(origin.X + radius * 2f + 12f * scale, midY - statusSize.Y * 0.5f), Styling.TextSecondary);

        using (Fonts.PushCaption())
        {
            var footerSize = TextDraw.Measure(footer);
            TextDraw.At(footer, new Vector2(origin.X + avail - footerSize.X, midY - footerSize.Y * 0.5f), Styling.TextMuted);
        }

        ImGui.Dummy(new Vector2(avail, lineHeight));
    }

    private static void DrawHeroCard(AutoHuntController controller, BillSelection.Workload workload, bool objectiveRun, Vector4 accent, Vector4 accentSoft, string label)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, Layout.HeroCardHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        var active = controller.Running && !controller.Paused;
        var rounding = Styling.PanelRounding * scale;

        Paint.Glass(drawList, origin, end, rounding, accent, active ? 0.10f : 0.03f, 0f, elevated: true);
        if (active)
        {
            Paint.Stroke(drawList, origin, end, Styling.PulseColor(Styling.WithAlpha(accent, 0.5f), accentSoft, Styling.PulseMedium), rounding, 1.6f);
        }

        var padX = PadX * scale;
        var ringRadius = size.Y * 0.5f - 18f * scale;
        var ringCenter = new Vector2(origin.X + padX + ringRadius, origin.Y + size.Y * 0.5f);
        DrawKillRing(ringCenter, ringRadius, accent, active, workload);

        var columnX = ringCenter.X + ringRadius + 20f * scale;
        var columnWidth = end.X - padX - columnX;
        var y = origin.Y + 16f * scale;

        y += DrawPhaseChip(columnX, y, label, accent, accentSoft) + 10f * scale;
        y = CurrentMark.TryGet(controller, out var mark)
            ? DrawMark(mark, controller.Status, columnX, columnWidth, y, accentSoft)
            : DrawStatus(controller.Status, columnX, columnWidth, y);

        var barHeight = 8f * scale;
        var barOrigin = new Vector2(columnX, y);
        if (active && workload.KillsNeeded > 0)
        {
            var fraction = Motion.Approach(Motion.Key("##ahg_kill_bar"), workload.KillsDone / (float)workload.KillsNeeded, 10f);
            Paint.Bar(drawList, barOrigin, columnWidth, barHeight, fraction, accent);
        }
        else if (active)
        {
            Paint.IndeterminateBar(drawList, barOrigin, columnWidth, barHeight, accent);
        }
        else
        {
            Paint.Bar(drawList, barOrigin, columnWidth, barHeight, 0f, accent);
        }

        y += barHeight + 8f * scale;
        using (Fonts.PushCaption())
        {
            TextDraw.At(Remaining(workload, objectiveRun), new Vector2(columnX, y), Styling.WithAlpha(accentSoft, 0.9f));
        }

        ImGui.Dummy(size);
    }

    private static float DrawStatus(string status, float x, float width, float y)
    {
        var text = TextDraw.Truncate(string.IsNullOrWhiteSpace(status) ? Loc.T(L.Common.Working) : status, width);
        TextDraw.At(text, new Vector2(x, y), Styling.TextSecondary);
        return y + ImGui.GetTextLineHeight() + 12f * ImGuiHelpers.GlobalScale;
    }

    private static float DrawMark(in CurrentMark.View mark, string status, float x, float width, float y, Vector4 accentSoft)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var killsWidth = TextDraw.Measure(mark.Kills).X;
        TextDraw.At(mark.Kills, new Vector2(x + width - killsWidth, y), accentSoft);
        TextDraw.At(TextDraw.Truncate(mark.Name, width - killsWidth - 12f * scale), new Vector2(x, y), Styling.TextStrong);
        y += ImGui.GetTextLineHeight() + 3f * scale;

        using (Fonts.PushCaption())
        {
            var zone = TextDraw.Truncate(mark.ZoneName, width);
            TextDraw.At(zone, new Vector2(x, y), Styling.TextDim);
            TextDraw.Trailing(status, x + TextDraw.Measure(zone).X, x + width, y, Styling.TextMuted);
            y += ImGui.GetTextLineHeight();
        }

        return y + 10f * scale;
    }

    private static void DrawKillRing(Vector2 center, float radius, Vector4 accent, bool active, BillSelection.Workload workload)
    {
        var thickness = 6f * ImGuiHelpers.GlobalScale;
        ProgressRing.Track(center, radius, thickness, Styling.WithAlpha(Styling.BorderDim, 0.7f));

        if (workload.KillsNeeded == 0)
        {
            if (active)
            {
                ProgressRing.Sweep(center, radius, thickness, accent, Styling.PulseOrbit, MathF.PI * 0.6f, 1f);
            }

            ProgressRing.CenterIcon(center, FontAwesomeIcon.Crosshairs, Styling.TextDim, radius * 0.55f);
            return;
        }

        var fraction = Motion.Approach(Motion.Key("##ahg_kill_ring"), workload.KillsDone / (float)workload.KillsNeeded, 6f);
        ProgressRing.Fill(center, radius, thickness, fraction, accent);
        ProgressRing.CenterValue(center, workload.KillsDone.ToString(Loc.Culture), Loc.T(L.Run.GoalOf, workload.KillsNeeded), Styling.TextStrong, Styling.TextDim);
    }

    // Before its first pass is planned an objective run has no workload yet, which is not the same as having finished it.
    private static string Remaining(BillSelection.Workload workload, bool objectiveRun)
    {
        if (workload.KillsLeft > 0)
        {
            return Loc.Plural(L.Run.KillsToGo, workload.KillsLeft);
        }

        if (objectiveRun)
        {
            return workload.KillsNeeded > 0 ? Loc.T(L.Run.AllTargetsDone) : Loc.T(L.Common.Working);
        }

        return workload.PickUps > 0 ? Loc.Plural(L.Hunt.ToPickUp, workload.PickUps) : Loc.T(L.Run.AllKillsDone);
    }

    private static float DrawPhaseChip(float x, float y, string text, Vector4 accent, Vector4 accentSoft)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var padX = 9f * scale;
        var padY = 3f * scale;

        using (Fonts.PushCaption())
        {
            var label = TextDraw.Upper(text);
            var textSize = TextDraw.Measure(label);
            var chipMin = new Vector2(x, y);
            var chipMax = chipMin + new Vector2(padX * 2f + textSize.X, textSize.Y + padY * 2f);
            Paint.Pill(drawList, chipMin, chipMax, Styling.WithAlpha(accent, 0.28f), Styling.WithAlpha(accent, 0.65f));
            TextDraw.At(label, new Vector2(x + padX, y + padY), accentSoft);
            return chipMax.Y - chipMin.Y;
        }
    }

    private static (Vector4 Accent, Vector4 AccentSoft, string Label) PhasePalette(AutoHuntController controller)
    {
        if (!controller.Running)
        {
            return (Styling.TextDim, Styling.TextSecondary, Loc.T(L.Run.PhaseReady));
        }

        if (controller.Paused)
        {
            return controller.PauseReason == PauseReason.InContent
                ? (Styling.AccentAmber, Styling.AccentAmberSoft, Loc.T(L.Run.PhasePausedInContent))
                : (Styling.AccentAmber, Styling.AccentAmberSoft, Loc.T(L.Run.PhasePaused));
        }

        return controller.Phase switch
        {
            HuntPhase.Fighting  => (Styling.AccentGlow, Styling.AccentGlowSoft, Loc.T(L.Run.PhaseFighting)),
            HuntPhase.Finishing => (Styling.AccentMint,  Styling.AccentMintSoft,  Loc.T(L.Run.PhaseFinishing)),
            HuntPhase.Idle      => (Styling.TextDim,     Styling.TextSecondary,   Loc.T(L.Run.PhaseStandingBy)),
            _                   => (Styling.AccentBlue,  Styling.AccentBlueSoft,  ReadyState.PhaseLabel(controller.Phase)),
        };
    }

    private static void DrawStatTiles(AutoHuntController controller, bool objectiveRun)
    {
        var session = controller.SessionSnapshot;
        var scale = ImGuiHelpers.GlobalScale;
        var gap = 8f * scale;
        var tileWidth = (ImGui.GetContentRegionAvail().X - gap * 3f) / 4f;
        var nuts = session?.Nuts ?? 0;

        StatTile.Draw(Loc.T(objectiveRun ? L.Run.TileKills : L.Run.TileMarks), (session?.MarksKilled ?? 0).ToString(Loc.Culture), null, Styling.AccentGlow, tileWidth);
        ImGui.SameLine(0, gap);
        if (objectiveRun)
        {
            var objectives = controller.Objectives;
            StatTile.Draw(Loc.T(L.Run.TileTargets), RunWorkload.CountDone(objectives).ToString(Loc.Culture), Loc.T(L.Run.GoalOf, objectives.Count), Styling.AccentMint, tileWidth);
        }
        else
        {
            StatTile.Draw(Loc.T(L.Run.TileBills), (session?.BillsCompleted ?? 0).ToString(Loc.Culture), null, Styling.AccentMint, tileWidth);
        }

        ImGui.SameLine(0, gap);
        StatTile.Draw(Loc.T(L.Run.TileSeals), (session?.Seals ?? 0).ToString("N0", Loc.Culture), nuts > 0 ? Loc.T(L.Run.NutsSub, nuts) : null, Styling.AccentAmber, tileWidth);
        ImGui.SameLine(0, gap);
        StatTile.Draw(Loc.T(L.Run.TileElapsed), Formatting.Elapsed(session?.Elapsed ?? TimeSpan.Zero), null, Styling.AccentBlue, tileWidth);
    }

    private static void DrawQueueHeading()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var heading = Loc.T(L.Run.UpNext);
        var labelSize = TextDraw.SectionTitleSize(heading);
        TextDraw.SectionTitle(heading, origin, Styling.TextStrong);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, labelSize.Y + 8f * scale));
    }

    private static void DrawBillQueue(AutoHuntController controller, BillSelection.Workload workload)
    {
        queue.Clear();
        var givenUp = controller.SessionSnapshot?.GivenUpNameIds;
        var routeAhead = controller.Progress.RouteAhead;
        if (routeAhead.Length > 0)
        {
            FillFromRoute(routeAhead, givenUp);
        }
        else
        {
            FillByZone(controller.Progress, controller.ActiveBills, givenUp);
        }

        if (queue.Count == 0)
        {
            EmptyHint(workload.PickUps > 0 ? Loc.T(L.Run.PickUpFirst) : Loc.T(L.Run.NoMarksLeft));
            return;
        }

        var shown = Math.Min(QueueLength, queue.Count);
        for (var index = 0; index < shown; index++)
        {
            var target = queue[index].Target;
            DrawQueueRow(target.Name, target.ZoneName, target.Killed, target.Needed, queue[index].Stale, index == 0);
        }
    }

    // The pass is listed from the objective being hunted onward, with live counts, so a target that filled or was given
    // up drops out.
    private static void DrawObjectiveQueue(AutoHuntController controller)
    {
        objectiveQueue.Clear();
        var session = controller.SessionSnapshot;
        var ahead = controller.Progress.ObjectivesAhead;
        for (var index = 0; index < ahead.Length && objectiveQueue.Count < QueueLength; index++)
        {
            var objective = ObjectiveProgress.Live(ahead[index]);
            if (objective.Done || (session is not null && session.IsGivenUp(objective)))
            {
                continue;
            }

            objectiveQueue.Add(objective);
        }

        if (objectiveQueue.Count == 0)
        {
            EmptyHint(controller.Objectives.Count == 0 ? Loc.T(L.Run.RouteFirst) : Loc.T(L.Run.NoTargetsLeft));
            return;
        }

        for (var index = 0; index < objectiveQueue.Count; index++)
        {
            var objective = objectiveQueue[index];
            DrawQueueRow(ObjectiveProgress.Name(objective), CurrentMark.ZoneName(objective.TerritoryId),
                Math.Min(objective.Killed, objective.Needed), objective.Needed, false, index == 0);
        }
    }

    // The planned route is the order the run works through, and the bills are read live, so a mark that fell since
    // drops out.
    private static void FillFromRoute(ReadOnlySpan<HuntStop> routeAhead, HashSet<uint>? givenUp)
    {
        for (var stopIndex = 0; stopIndex < routeAhead.Length && queue.Count < QueueLength; stopIndex++)
        {
            var stop = routeAhead[stopIndex];
            var markIndex = stop.Bill.MarkIndex;
            if (IsGivenUp(givenUp, stop.Target.NameId)
                || !MarkBillReader.TryFindTarget(markIndex, stop.Target.TargetRowId, out var target)
                || target.Done)
            {
                continue;
            }

            queue.Add(new QueueEntry(markIndex, target, MarkBillReader.Status(markIndex) == BillStatus.Stale));
        }
    }

    private static void FillByZone(HuntProgress progress, IReadOnlyList<HuntBill> bills, HashSet<uint>? givenUp)
    {
        for (var billIndex = 0; billIndex < bills.Count; billIndex++)
        {
            var markIndex = bills[billIndex].MarkIndex;
            var status = MarkBillReader.Status(markIndex);
            if (status is not (BillStatus.Held or BillStatus.Stale))
            {
                continue;
            }

            var targets = MarkBillReader.Targets(markIndex);
            for (var targetIndex = 0; targetIndex < targets.Length; targetIndex++)
            {
                var target = targets[targetIndex];
                if (target.Done || !RoutePlanner.CanHunt(target) || IsGivenUp(givenUp, target.NameId))
                {
                    continue;
                }

                queue.Add(new QueueEntry(markIndex, target, status == BillStatus.Stale));
            }
        }

        queue.Sort(byZone);
        BringCurrentForward(progress);
    }

    private static bool IsGivenUp(HashSet<uint>? givenUp, uint nameId) => givenUp is not null && givenUp.Contains(nameId);

    private static void BringCurrentForward(HuntProgress progress)
    {
        if (!progress.HasMark)
        {
            return;
        }

        for (var index = 1; index < queue.Count; index++)
        {
            var entry = queue[index];
            if (entry.MarkIndex != progress.Bill.MarkIndex || entry.Target.TargetRowId != progress.Target.TargetRowId)
            {
                continue;
            }

            queue.RemoveAt(index);
            queue.Insert(0, entry);
            return;
        }
    }

    private static void DrawQueueRow(string name, string zoneName, int killed, int needed, bool stale, bool emphasize)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, Layout.QueueRowHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();

        var accent = stale ? Styling.AccentAmber : Styling.AccentGlow;
        Paint.Glass(drawList, origin, end, Styling.CardRounding * scale, accent, emphasize ? 0.10f : 0.03f);

        var padX = 13f * scale;
        var topY = origin.Y + 9f * scale;
        var iconSize = TextDraw.IconSize(FontAwesomeIcon.Crosshairs);
        TextDraw.Icon(FontAwesomeIcon.Crosshairs, new Vector2(origin.X + padX, topY + (ImGui.GetTextLineHeight() - iconSize.Y) * 0.5f), emphasize ? accent : Styling.TextDim);

        var meta = Loc.T(L.Run.TargetMeta, zoneName, killed, needed);
        Vector2 metaSize;
        using (Fonts.PushCaption())
        {
            metaSize = TextDraw.Measure(meta);
            TextDraw.At(meta, new Vector2(end.X - padX - metaSize.X, topY + (ImGui.GetTextLineHeight() - metaSize.Y) * 0.5f), Styling.TextDim);
        }

        var nameX = origin.X + padX + iconSize.X + 10f * scale;
        var shownName = TextDraw.Truncate(name, end.X - padX - metaSize.X - 12f * scale - nameX);
        TextDraw.At(shownName, new Vector2(nameX, topY), emphasize ? Styling.TextStrong : Styling.TextSecondary);

        var fraction = needed > 0 ? killed / (float)needed : 0f;
        Paint.Bar(drawList, new Vector2(origin.X + padX, end.Y - 13f * scale), size.X - padX * 2f, Layout.QueueBarHeight * scale, fraction, accent);

        ImGui.Dummy(size);
    }

    private static string CurrentZoneName()
    {
        uint territoryId = Svc.ClientState.TerritoryType;
        if (territoryId == cachedTerritoryId)
        {
            return cachedZoneName;
        }

        cachedTerritoryId = territoryId;
        cachedZoneName = Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId)?.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty;
        if (cachedZoneName.Length == 0)
        {
            cachedZoneName = Loc.T(L.Run.SomewhereElse);
        }

        return cachedZoneName;
    }

    private static void EmptyHint(string text)
    {
        var origin = ImGui.GetCursorScreenPos();
        TextDraw.At(text, origin, Styling.TextMuted);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight()));
    }
}
