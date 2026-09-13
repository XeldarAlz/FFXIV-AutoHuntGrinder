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
    private static readonly Comparison<QueueEntry> byZone = (left, right) => left.Target.TerritoryId.CompareTo(right.Target.TerritoryId);

    private static uint cachedTerritoryId = uint.MaxValue;
    private static string cachedZoneName = string.Empty;

    private readonly record struct QueueEntry(byte MarkIndex, HuntTarget Target, bool Stale);

    public static void Draw(AutoHuntController controller)
    {
        var paused = controller.Paused;
        var (accent, accentSoft, label) = PhasePalette(controller);
        var bills = controller.ActiveBills;
        var workload = BillSelection.Measure(bills);

        DrawHeaderStrip(bills.Count, accent, accentSoft, paused);
        Styling.VSpace(6f);
        DrawHeroCard(controller, workload, accent, accentSoft, label);

        Styling.VSpace(10f);
        DrawStatTiles(controller);

        Styling.VSpace(10f);
        DrawQueue(controller, bills, workload);
    }

    private static void DrawHeaderStrip(int billCount, Vector4 accent, Vector4 accentSoft, bool paused)
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

        var footer = Loc.Plural(L.Run.InPlay, billCount, CurrentZoneName());
        using (Fonts.PushCaption())
        {
            var footerSize = TextDraw.Measure(footer);
            TextDraw.At(footer, new Vector2(origin.X + avail - footerSize.X, midY - footerSize.Y * 0.5f), Styling.TextMuted);
        }

        ImGui.Dummy(new Vector2(avail, lineHeight));
    }

    private static void DrawHeroCard(AutoHuntController controller, BillSelection.Workload workload, Vector4 accent, Vector4 accentSoft, string label)
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
            TextDraw.At(Remaining(workload), new Vector2(columnX, y), Styling.WithAlpha(accentSoft, 0.9f));
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

    private static string Remaining(BillSelection.Workload workload)
    {
        if (workload.KillsLeft > 0)
        {
            return Loc.Plural(L.Run.KillsToGo, workload.KillsLeft);
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

    private static void DrawStatTiles(AutoHuntController controller)
    {
        var session = controller.SessionSnapshot;
        var scale = ImGuiHelpers.GlobalScale;
        var gap = 8f * scale;
        var tileWidth = (ImGui.GetContentRegionAvail().X - gap * 3f) / 4f;
        var nuts = session?.Nuts ?? 0;

        StatTile.Draw(Loc.T(L.Run.TileMarks), (session?.MarksKilled ?? 0).ToString(Loc.Culture), null, Styling.AccentGlow, tileWidth);
        ImGui.SameLine(0, gap);
        StatTile.Draw(Loc.T(L.Run.TileBills), (session?.BillsCompleted ?? 0).ToString(Loc.Culture), null, Styling.AccentMint, tileWidth);
        ImGui.SameLine(0, gap);
        StatTile.Draw(Loc.T(L.Run.TileSeals), (session?.Seals ?? 0).ToString("N0", Loc.Culture), nuts > 0 ? Loc.T(L.Run.NutsSub, nuts) : null, Styling.AccentAmber, tileWidth);
        ImGui.SameLine(0, gap);
        StatTile.Draw(Loc.T(L.Run.TileElapsed), Formatting.Elapsed(session?.Elapsed ?? TimeSpan.Zero), null, Styling.AccentBlue, tileWidth);
    }

    private static void DrawQueue(AutoHuntController controller, IReadOnlyList<HuntBill> bills, BillSelection.Workload workload)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var heading = Loc.T(L.Run.UpNext);
        var labelSize = TextDraw.SectionTitleSize(heading);
        TextDraw.SectionTitle(heading, origin, Styling.TextStrong);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, labelSize.Y + 8f * scale));

        queue.Clear();
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
                if (target.Done || !RoutePlanner.CanHunt(target))
                {
                    continue;
                }

                queue.Add(new QueueEntry(markIndex, target, status == BillStatus.Stale));
            }
        }

        if (queue.Count == 0)
        {
            EmptyHint(workload.PickUps > 0 ? Loc.T(L.Run.PickUpFirst) : Loc.T(L.Run.NoMarksLeft));
            return;
        }

        queue.Sort(byZone);
        BringCurrentForward(controller.Progress);
        var shown = Math.Min(QueueLength, queue.Count);
        for (var index = 0; index < shown; index++)
        {
            DrawQueueRow(queue[index], index == 0);
        }
    }

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

    private static void DrawQueueRow(QueueEntry entry, bool emphasize)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, Layout.QueueRowHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        var target = entry.Target;

        var accent = entry.Stale ? Styling.AccentAmber : Styling.AccentGlow;
        Paint.Glass(drawList, origin, end, Styling.CardRounding * scale, accent, emphasize ? 0.10f : 0.03f);

        var padX = 13f * scale;
        var topY = origin.Y + 9f * scale;
        var iconSize = TextDraw.IconSize(FontAwesomeIcon.Crosshairs);
        TextDraw.Icon(FontAwesomeIcon.Crosshairs, new Vector2(origin.X + padX, topY + (ImGui.GetTextLineHeight() - iconSize.Y) * 0.5f), emphasize ? accent : Styling.TextDim);

        var meta = Loc.T(L.Run.TargetMeta, target.ZoneName, target.Killed, target.Needed);
        Vector2 metaSize;
        using (Fonts.PushCaption())
        {
            metaSize = TextDraw.Measure(meta);
            TextDraw.At(meta, new Vector2(end.X - padX - metaSize.X, topY + (ImGui.GetTextLineHeight() - metaSize.Y) * 0.5f), Styling.TextDim);
        }

        var nameX = origin.X + padX + iconSize.X + 10f * scale;
        var name = TextDraw.Truncate(target.Name, end.X - padX - metaSize.X - 12f * scale - nameX);
        TextDraw.At(name, new Vector2(nameX, topY), emphasize ? Styling.TextStrong : Styling.TextSecondary);

        var fraction = target.Needed > 0 ? target.Killed / (float)target.Needed : 0f;
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
