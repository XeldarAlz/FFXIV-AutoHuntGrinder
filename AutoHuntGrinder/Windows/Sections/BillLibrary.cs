using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Tasks;
using AutoHuntGrinder.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoHuntGrinder.Windows.Sections;

internal static class BillLibrary
{
    private const float Gap = 8f;
    private const float SummaryRowHeight = 32f;
    private const float GroupGap = 14f;
    private const float ListSlide = 8f;
    private const float ProgressBarHeight = 3f;

    private static readonly ExpansionKind[] expansions =
        [ExpansionKind.DT, ExpansionKind.EW, ExpansionKind.ShB, ExpansionKind.SB, ExpansionKind.HW, ExpansionKind.ARR];

    private static readonly Segmented.Item[] segments = new Segmented.Item[expansions.Length];
    private static readonly List<HuntBill> dailyBills = new(4);
    private static readonly List<HuntBill> weeklyBills = new(2);

    private static int currentExpansion;

    public static void Draw(Configuration configuration, AutoHuntController controller, bool scrollIntoView)
    {
        DrawHeader(scrollIntoView);
        Styling.VSpace(10f);
        DrawExpansionPicker();

        using var reveal = Motion.PushSwitch("##ahg_bill_list", currentExpansion, slide: ListSlide);
        Collect(expansions[currentExpansion]);
        if (dailyBills.Count + weeklyBills.Count == 0)
        {
            EmptyHint(Loc.T(L.Hunt.NoBillsInData));
            return;
        }

        DrawSummaryRow(configuration, controller);
        DrawGroup(Loc.T(L.Hunt.Daily), dailyBills, configuration, controller);
        if (dailyBills.Count > 0 && weeklyBills.Count > 0)
        {
            Styling.VSpace(GroupGap);
        }

        DrawGroup(Loc.T(L.Hunt.Weekly), weeklyBills, configuration, controller);
    }

    private static void Collect(ExpansionKind expansion)
    {
        dailyBills.Clear();
        weeklyBills.Clear();
        var bills = HuntRegistry.Bills;
        for (var index = 0; index < bills.Length; index++)
        {
            var bill = bills[index];
            if (bill.Expansion != expansion)
            {
                continue;
            }

            if (bill.Cadence == BillCadence.Weekly)
            {
                weeklyBills.Add(bill);
                continue;
            }

            dailyBills.Add(bill);
        }
    }

    private static void DrawHeader(bool scrollIntoView)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Layout.LibraryHeaderHeight * scale;
        var label = Loc.T(L.Hunt.Bills);
        var labelSize = TextDraw.SectionTitleSize(label);
        TextDraw.SectionTitle(label, new Vector2(origin.X, origin.Y + (height - labelSize.Y) * 0.5f), Styling.TextStrong);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
        if (scrollIntoView)
        {
            ImGui.SetScrollHereY(0f);
        }
    }

    private static void DrawExpansionPicker()
    {
        for (var index = 0; index < expansions.Length; index++)
        {
            segments[index] = new Segmented.Item(null, ExpansionLabels.Name(expansions[index]));
        }

        Segmented.Draw("##ahg_expansions", segments, ref currentExpansion);
        Styling.VSpace(8f);
    }

    private static void DrawSummaryRow(Configuration configuration, AutoHuntController controller)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail().X;
        var rowHeight = SummaryRowHeight * scale;
        var total = dailyBills.Count + weeklyBills.Count;
        var selected = CountSelected(configuration, dailyBills) + CountSelected(configuration, weeklyBills);

        using (Fonts.PushCaption())
        {
            var summary = Loc.T(L.Hunt.SelectedSummary, selected, total);
            var summarySize = TextDraw.Measure(summary);
            TextDraw.At(summary, new Vector2(origin.X + 2f * scale, origin.Y + (rowHeight - summarySize.Y) * 0.5f), Styling.TextDim);
        }

        var allSelected = AllUnlockedSelected(configuration, dailyBills) && AllUnlockedSelected(configuration, weeklyBills)
            && CountUnlocked(dailyBills) + CountUnlocked(weeklyBills) > 0;
        var label = allSelected ? Loc.T(L.Common.Clear) : Loc.T(L.Common.SelectAll);
        var width = PillButton.Width(label);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + avail - width, origin.Y));
        if (PillButton.Draw("##ahg_bill_bulk", label, allSelected ? Styling.AccentRose : Styling.AccentGlow,
                PillButton.Emphasis.Ghost, enabled: !controller.Running, height: SummaryRowHeight))
        {
            SetAll(configuration, dailyBills, !allSelected);
            SetAll(configuration, weeklyBills, !allSelected);
            configuration.SaveDebounced();
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(avail, rowHeight));
    }

    private static void DrawGroup(string label, List<HuntBill> bills, Configuration configuration, AutoHuntController controller)
    {
        if (bills.Count == 0)
        {
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var labelSize = TextDraw.SmallCapsSize(label);
        TextDraw.SmallCaps(label, new Vector2(origin.X + 2f * scale, origin.Y), Styling.TextMuted);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, labelSize.Y + 6f * scale));

        var gap = Gap * scale;
        var avail = ImGui.GetContentRegionAvail().X;
        var columns = Math.Max(1, (int)MathF.Floor((avail + gap) / (Layout.BillCardMinWidth * scale + gap)));
        var cardWidth = (avail - gap * (columns - 1)) / columns;

        using var itemSpacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap));
        for (var index = 0; index < bills.Count; index++)
        {
            if (index % columns != 0)
            {
                ImGui.SameLine(0f, gap);
            }

            DrawCard(bills[index], configuration, controller, cardWidth);
        }
    }

    private static void DrawCard(HuntBill bill, Configuration configuration, AutoHuntController controller, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(width, Layout.BillCardHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var status = MarkBillReader.Status(bill.MarkIndex);
        var locked = status == BillStatus.Locked;
        var selected = configuration.SelectedBills.Contains(bill.MarkIndex);
        var running = controller.Running;
        var interactive = !locked && !running;

        ImGui.PushID(bill.MarkIndex);
        var hit = Hit.Area("##bill", size, interactive);
        var hover = Motion.Hover(Motion.Key("##bill"), hit.Hovered);
        var active = Motion.Approach(Motion.Key("##bill", 1), selected ? 1f : 0f, 14f);
        ImGui.PopID();

        if (hit.Clicked)
        {
            SetSelected(configuration, bill.MarkIndex, !selected);
            configuration.SaveDebounced();
        }

        var drawList = ImGui.GetWindowDrawList();
        var rounding = Styling.CardRounding * scale;
        Paint.Glass(drawList, origin, end, rounding, Styling.AccentGlow, 0.02f + 0.16f * active, hover);

        var midY = origin.Y + size.Y * 0.5f;
        var discRadius = 9f * scale;
        var discCenter = new Vector2(origin.X + 14f * scale + discRadius, midY);
        DrawSelector(drawList, discCenter, discRadius, locked, active);

        var rightX = end.X - 12f * scale;
        rightX -= DrawStatus(bill, status, rightX, midY) + 10f * scale;

        var textX = discCenter.X + discRadius + 12f * scale;
        var nameColor = locked ? Styling.TextMuted : Vector4.Lerp(Styling.TextSecondary, Styling.TextStrong, MathF.Max(active, hover));
        var name = TextDraw.Truncate(bill.Name, rightX - textX);
        var nameSize = TextDraw.Measure(name);
        TextDraw.At(name, new Vector2(textX, midY - nameSize.Y * 0.5f), nameColor);

        if (status is BillStatus.Held or BillStatus.Stale)
        {
            var (killed, needed) = MarkBillReader.Kills(bill.MarkIndex);
            var inset = rounding * 0.9f;
            var barHeight = ProgressBarHeight * scale;
            var fraction = needed > 0 ? (float)killed / needed : 0f;
            Paint.Bar(drawList, new Vector2(origin.X + inset, end.Y - barHeight - 4f * scale), size.X - inset * 2f, barHeight, fraction,
                status == BillStatus.Stale ? Styling.AccentAmber : Styling.AccentGlow);
        }

        if (!Hit.HoveringRect(origin, end))
        {
            return;
        }

        if (locked)
        {
            Tooltip.Show(LockedTooltip(bill));
            return;
        }

        if (running)
        {
            Tooltip.Show(Loc.T(L.Hunt.BillsLockedRunning));
            return;
        }

        DrawTooltip(bill, status);
    }

    private static void DrawSelector(ImDrawListPtr drawList, Vector2 center, float radius, bool locked, float active)
    {
        var scale = ImGuiHelpers.GlobalScale;
        if (locked)
        {
            TextDraw.IconCentered(FontAwesomeIcon.Lock, center, Styling.TextMuted);
            return;
        }

        var ring = Vector4.Lerp(Styling.WithAlpha(Styling.BorderDim, 0.9f), Styling.AccentGlowSoft, active);
        drawList.AddCircle(center, radius, Paint.Col(ring), 0, 1.4f * scale);
        if (active <= 0.01f)
        {
            return;
        }

        drawList.AddCircleFilled(center, radius * active, Paint.Col(Styling.AccentGlow));
        if (active > 0.5f)
        {
            Paint.Check(drawList, center, radius * 1.1f, Styling.WithAlpha(Styling.InkOnGlow, (active - 0.5f) * 2f), 1.8f * scale);
        }
    }

    private static float DrawStatus(HuntBill bill, BillStatus status, float rightX, float midY)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var (icon, text, color) = status switch
        {
            BillStatus.Held      => (FontAwesomeIcon.Crosshairs, KillsLabel(bill.MarkIndex), Styling.AccentGlowSoft),
            BillStatus.Stale     => (FontAwesomeIcon.History, Loc.T(L.Hunt.StatusOld), Styling.AccentAmber),
            BillStatus.Done      => (FontAwesomeIcon.Check, Loc.T(L.Hunt.StatusDone), Styling.AccentMint),
            BillStatus.Available => (FontAwesomeIcon.PlusCircle, Loc.T(L.Hunt.StatusNotTaken), Styling.TextDim),
            _                    => ((FontAwesomeIcon?)null, string.Empty, Styling.TextMuted),
        };

        if (icon is null)
        {
            return 0f;
        }

        using (Fonts.PushCaption())
        {
            var textSize = TextDraw.Measure(text);
            var textX = rightX - textSize.X;
            TextDraw.At(text, new Vector2(textX, midY - textSize.Y * 0.5f), color);
            var iconSize = TextDraw.IconSize(icon.Value);
            var iconX = textX - 5f * scale - iconSize.X;
            TextDraw.Icon(icon.Value, new Vector2(iconX, midY - iconSize.Y * 0.5f), color);
            return rightX - iconX;
        }
    }

    private static string KillsLabel(byte markIndex)
    {
        var (killed, needed) = MarkBillReader.Kills(markIndex);
        return string.Concat(killed.ToString(Loc.Culture), "/", needed.ToString(Loc.Culture));
    }

    private static void DrawTooltip(HuntBill bill, BillStatus status)
    {
        var targets = MarkBillReader.Targets(bill.MarkIndex);
        using (Tooltip.Begin())
        {
            Tooltip.Text(bill.Name, Styling.TextStrong);
            switch (status)
            {
                case BillStatus.Available:
                    Tooltip.Text(Loc.T(L.Hunt.TooltipNotTaken), Styling.TextDim);
                    return;
                case BillStatus.Stale:
                    Tooltip.Text(Loc.T(L.Hunt.TooltipOld), Styling.AccentAmber);
                    break;
                case BillStatus.Done:
                    Tooltip.Text(Loc.T(L.Hunt.TooltipDone), Styling.AccentMint);
                    break;
            }

            for (var index = 0; index < targets.Length; index++)
            {
                var target = targets[index];
                var line = Loc.T(L.Hunt.TooltipTarget, target.Name, target.Killed, target.Needed, target.ZoneName);
                Tooltip.Text(line, target.Done ? Styling.AccentMint : Styling.TextSecondary);
            }
        }
    }

    private static string LockedTooltip(HuntBill bill)
        => bill.UnlockQuestName.Length > 0
            ? Loc.T(L.Hunt.LockedQuest, bill.UnlockQuestName)
            : Loc.T(L.Hunt.LockedRank);

    private static void EmptyHint(string text)
    {
        var origin = ImGui.GetCursorScreenPos();
        TextDraw.At(text, origin, Styling.TextMuted);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight()));
    }

    private static int CountSelected(Configuration configuration, List<HuntBill> bills)
    {
        var count = 0;
        for (var index = 0; index < bills.Count; index++)
        {
            if (configuration.SelectedBills.Contains(bills[index].MarkIndex))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountUnlocked(List<HuntBill> bills)
    {
        var count = 0;
        for (var index = 0; index < bills.Count; index++)
        {
            if (MarkBillReader.Status(bills[index].MarkIndex) != BillStatus.Locked)
            {
                count++;
            }
        }

        return count;
    }

    private static bool AllUnlockedSelected(Configuration configuration, List<HuntBill> bills)
    {
        for (var index = 0; index < bills.Count; index++)
        {
            var markIndex = bills[index].MarkIndex;
            if (MarkBillReader.Status(markIndex) == BillStatus.Locked)
            {
                continue;
            }

            if (!configuration.SelectedBills.Contains(markIndex))
            {
                return false;
            }
        }

        return true;
    }

    private static void SetSelected(Configuration configuration, byte markIndex, bool selected)
    {
        if (selected)
        {
            configuration.SelectedBills.Add(markIndex);
            return;
        }

        configuration.SelectedBills.Remove(markIndex);
    }

    private static void SetAll(Configuration configuration, List<HuntBill> bills, bool selected)
    {
        for (var index = 0; index < bills.Count; index++)
        {
            var markIndex = bills[index].MarkIndex;
            if (selected && MarkBillReader.Status(markIndex) == BillStatus.Locked)
            {
                continue;
            }

            SetSelected(configuration, markIndex, selected);
        }
    }
}
