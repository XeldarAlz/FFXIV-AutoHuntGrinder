using AutoHuntGrinder.Core.Custom;
using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Marks;
using AutoHuntGrinder.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoHuntGrinder.Windows.Sections;

internal static class HuntMarkBrowser
{
    private const float Gap = 8f;
    private const float FilterGap = 10f;
    private const float ExpansionWidth = 200f;
    private const float SearchMinWidth = 200f;
    private const float ChipPadX = 12f;
    private const float ChipMarkRadius = 7f;
    private const float ChipLabelGap = 8f;
    private const float RowHeight = 56f;
    private const float RowMinWidth = 300f;
    private const float RowPadX = 12f;
    private const float RowLineGap = 3f;
    private const float RowBadgeGap = 8f;
    private const float ExpansionHeaderSpace = 10f;
    private const float ZoneHeaderSpace = 4f;
    private const int AllExpansions = 0;
    private const int NotFiltered = -1;
    // A territory id is a ushort, so the expansion sits above it in a zone group's key.
    private const int GroupExpansionShift = 16;
    private const string SearchId = "##ahg_marks_search";
    private const string ExpansionId = "##ahg_marks_expansion";

    private static readonly HuntMarkRank[] ranks = [HuntMarkRank.B, HuntMarkRank.A, HuntMarkRank.S];

    private static readonly ExpansionKind[] expansions =
        [ExpansionKind.ARR, ExpansionKind.HW, ExpansionKind.SB, ExpansionKind.ShB, ExpansionKind.EW, ExpansionKind.DT];

    private static readonly CachedText[] chipLabels = new CachedText[ranks.Length];
    private static readonly CachedText[] chipHelps = new CachedText[ranks.Length];

    private static int[] results = [];
    private static string[] expansionOptions = [];
    private static LanguageInfo? optionsLanguage;
    private static LanguageInfo? filteredLanguage;
    private static CachedText countText;
    private static string query = string.Empty;
    private static string filteredQuery = string.Empty;
    private static string noMatchesText = string.Empty;
    private static int rankMask = HuntMarkRegistry.RankBit(HuntMarkRank.B) | HuntMarkRegistry.RankBit(HuntMarkRank.A) | HuntMarkRegistry.RankBit(HuntMarkRank.S);
    private static int filteredRankMask = NotFiltered;
    private static int expansionIndex = AllExpansions;
    private static int filteredExpansionIndex = NotFiltered;
    private static int resultCount;
    private static bool searchFocused;

    public static void Draw(bool running)
    {
        DrawSearchRow();
        Styling.VSpace(8f);
        DrawRankRow();
        Styling.VSpace(12f);
        DrawResults(running);
    }

    // A narrow page stacks the dropdown under the search box instead of squeezing the box beside it.
    private static void DrawSearchRow()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var available = ImGui.GetContentRegionAvail().X;
        var dropdownWidth = ExpansionWidth * scale;
        var searchWidth = available - dropdownWidth - FilterGap * scale;
        var hint = Loc.T(L.HuntMarks.SearchHint);
        if (searchWidth < SearchMinWidth * scale)
        {
            SearchField.Draw(SearchId, hint, ref query, ref searchFocused, available);
            Dropdown.Draw(ExpansionId, ExpansionOptions(), ref expansionIndex, MathF.Min(ExpansionWidth, available / scale));
            return;
        }

        var origin = ImGui.GetCursorScreenPos();
        SearchField.Draw(SearchId, hint, ref query, ref searchFocused, searchWidth);
        var height = ImGui.GetItemRectSize().Y;

        ImGui.SetCursorScreenPos(new Vector2(origin.X + available - dropdownWidth, origin.Y + (height - ImGui.GetFrameHeight()) * 0.5f));
        Dropdown.Draw(ExpansionId, ExpansionOptions(), ref expansionIndex, ExpansionWidth);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(available, height));
    }

    // Chips wrap on a narrow page, and the count drops under them once it no longer fits beside the last one.
    private static void DrawRankRow()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var available = ImGui.GetContentRegionAvail().X;
        var right = origin.X + available;
        var height = Layout.ChipHeight * scale;
        var gap = Gap * scale;
        var x = origin.X;
        var y = origin.Y;
        for (var index = 0; index < ranks.Length; index++)
        {
            var width = ChipWidth(index);
            if (x > origin.X && x + width > right)
            {
                x = origin.X;
                y += height + gap;
            }

            ImGui.SetCursorScreenPos(new Vector2(x, y));
            if (DrawChip(index, new Vector2(width, height)))
            {
                rankMask ^= HuntMarkRegistry.RankBit(ranks[index]);
            }

            x += width + gap;
        }

        Refresh();
        var bottom = y + height;
        using (Fonts.PushCaption())
        {
            var count = countText.Get(resultCount, static key => Loc.Plural(L.HuntMarks.Count, (int)key));
            var countSize = TextDraw.Measure(count);
            var besideChips = x - gap + FilterGap * scale + countSize.X <= right;
            var countPosition = besideChips
                ? new Vector2(right - countSize.X, y + (height - countSize.Y) * 0.5f)
                : new Vector2(origin.X + 2f * scale, bottom + gap);
            TextDraw.At(count, countPosition, Styling.TextDim);
            bottom = MathF.Max(bottom, countPosition.Y + countSize.Y);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(available, bottom - origin.Y));
    }

    private static float ChipWidth(int index)
    {
        var scale = ImGuiHelpers.GlobalScale;
        return ChipPadX * 2f * scale + ChipMarkRadius * 2f * scale + ChipLabelGap * scale + TextDraw.Measure(ChipLabel(index)).X;
    }

    private static bool DrawChip(int index, Vector2 size)
    {
        var rank = ranks[index];
        var on = (rankMask & HuntMarkRegistry.RankBit(rank)) != 0;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;

        ImGui.PushID(index);
        var hit = Hit.Area("##rank", size);
        var hover = Motion.Hover(Motion.Key("##rank"), hit.Hovered);
        var active = Motion.Approach(Motion.Key("##rank", 1), on ? 1f : 0f, 14f);
        ImGui.PopID();

        var scale = ImGuiHelpers.GlobalScale;
        var color = MarkBadges.RankColor(rank);
        var drawList = ImGui.GetWindowDrawList();
        var fill = Vector4.Lerp(Styling.WithAlpha(Styling.Surface2, 0.35f + 0.45f * hover), Styling.WithAlpha(color, 0.16f + 0.10f * hover), active);
        var border = Vector4.Lerp(Styling.WithAlpha(Styling.BorderDim, 0.6f + 0.3f * hover), Styling.WithAlpha(color, 0.55f + 0.30f * hover), active);
        Paint.Pill(drawList, origin, end, fill, border);

        var midY = origin.Y + size.Y * 0.5f;
        var radius = ChipMarkRadius * scale;
        var center = new Vector2(origin.X + ChipPadX * scale + radius, midY);
        DrawToggleMark(drawList, center, radius, color, active);

        var label = ChipLabel(index);
        var labelSize = TextDraw.Measure(label);
        var offColor = Vector4.Lerp(Styling.TextDim, Styling.TextSecondary, hover);
        var onColor = Vector4.Lerp(Styling.Lighten(color, 0.35f), Styling.TextStrong, hover * 0.5f);
        TextDraw.At(label, new Vector2(center.X + radius + ChipLabelGap * scale, midY - labelSize.Y * 0.5f), Vector4.Lerp(offColor, onColor, active));

        if (hit.Hovered)
        {
            Tooltip.Show(chipHelps[index].Get(index, static key => Loc.T(L.HuntMarks.RankChipHelp, MarkBadges.Letter(ranks[(int)key]))));
        }

        return hit.Clicked;
    }

    private static void DrawToggleMark(ImDrawListPtr drawList, Vector2 center, float radius, Vector4 color, float active)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var ring = Vector4.Lerp(Styling.WithAlpha(Styling.BorderDim, 0.9f), color, active);
        drawList.AddCircle(center, radius, Paint.Col(ring), 0, 1.4f * scale);
        if (active <= 0.01f)
        {
            return;
        }

        drawList.AddCircleFilled(center, radius * active, Paint.Col(color));
        if (active > 0.5f)
        {
            Paint.Check(drawList, center, radius * 1.1f, Styling.WithAlpha(Styling.ForegroundOn(color), (active - 0.5f) * 2f), 1.6f * scale);
        }
    }

    private static string ChipLabel(int index)
        => chipLabels[index].Get(index, static key => Loc.T(L.HuntMarks.RankChip, MarkBadges.Letter(ranks[(int)key])));

    // The registry is filtered only when a filter input changes; the no-match line follows the plugin language.
    private static void Refresh()
    {
        var language = Loc.Current;
        if (expansionIndex == filteredExpansionIndex && rankMask == filteredRankMask && ReferenceEquals(language, filteredLanguage)
            && string.Equals(query, filteredQuery, StringComparison.Ordinal))
        {
            return;
        }

        filteredQuery = query;
        filteredRankMask = rankMask;
        filteredExpansionIndex = expansionIndex;
        filteredLanguage = language;

        var markCount = HuntMarkRegistry.Marks.Length;
        if (results.Length < markCount)
        {
            results = new int[markCount];
        }

        ExpansionKind? expansion = expansionIndex == AllExpansions ? null : expansions[expansionIndex - 1];
        resultCount = HuntMarkRegistry.Filter(rankMask, expansion, query, results);
        var trimmed = query.Trim();
        noMatchesText = resultCount == 0 && trimmed.Length > 0 ? Loc.T(L.Common.NoMatches, trimmed) : string.Empty;
    }

    private static void DrawResults(bool running)
    {
        if (resultCount == 0)
        {
            TextDraw.Hint(rankMask == 0 ? Loc.T(L.HuntMarks.NoRanks) : noMatchesText.Length > 0 ? noMatchesText : Loc.T(L.HuntMarks.NoMarks));
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var gap = Gap * scale;
        var available = ImGui.GetContentRegionAvail().X;
        var columns = Math.Max(1, (int)MathF.Floor((available + gap) / (RowMinWidth * scale + gap)));
        var rowWidth = (available - gap * (columns - 1)) / columns;
        var addSlot = MarkBadges.AddSlotWidth();
        var marks = HuntMarkRegistry.Marks;
        var groupByExpansion = expansionIndex == AllExpansions;
        var previousExpansion = NotFiltered;
        var previousGroup = NotFiltered;
        var column = 0;

        ImGui.PushID("##ahg_marks_results");
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap)))
        {
            for (var position = 0; position < resultCount; position++)
            {
                var markIndex = results[position];
                var mark = marks[markIndex];
                if (groupByExpansion && (int)mark.Expansion != previousExpansion)
                {
                    GroupLabel.Draw(ExpansionLabels.Name(mark.Expansion), previousExpansion != NotFiltered ? ExpansionHeaderSpace : 0f);
                    previousExpansion = (int)mark.Expansion;
                }

                var group = GroupKey(markIndex, mark);
                if (group != previousGroup)
                {
                    DrawZoneHeader(markIndex, previousGroup != NotFiltered);
                    previousGroup = group;
                    column = 0;
                }

                if (column % columns != 0)
                {
                    ImGui.SameLine(0f, gap);
                }

                DrawRow(markIndex, mark, rowWidth, addSlot, running);
                column++;
            }
        }

        ImGui.PopID();
    }

    private static void DrawZoneHeader(int markIndex, bool spaced)
    {
        if (spaced)
        {
            Styling.VSpace(ZoneHeaderSpace);
        }

        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();
        var iconSize = TextDraw.IconSize(FontAwesomeIcon.MapMarkerAlt);
        var iconX = origin.X + 2f * scale;
        TextDraw.Icon(FontAwesomeIcon.MapMarkerAlt, new Vector2(iconX, origin.Y + (lineHeight - iconSize.Y) * 0.5f), Styling.TextMuted);
        TextDraw.At(MarkBadges.ZoneLabel(markIndex), new Vector2(iconX + iconSize.X + 8f * scale, origin.Y), Styling.TextSecondary);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, lineHeight));
    }

    private static void DrawRow(int markIndex, in HuntMark mark, float width, float addSlot, bool running)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(width, RowHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        if (!ImGui.IsRectVisible(origin, end))
        {
            ImGui.Dummy(size);
            return;
        }

        var listed = CustomMobList.Contains(mark.NameId);
        var drawList = ImGui.GetWindowDrawList();
        Paint.Glass(drawList, origin, end, Styling.CardRounding * scale, listed ? Styling.AccentMint : MarkBadges.RankColor(mark.Rank), listed ? 0.05f : 0.02f);

        var padX = RowPadX * scale;
        var midY = origin.Y + size.Y * 0.5f;
        var textX = origin.X + padX + MarkBadges.DrawRank(drawList, mark.Rank, origin.X + padX, midY) + 10f * scale;
        var rightX = end.X - padX;

        ImGui.PushID((int)mark.NameId);
        var clicked = MarkBadges.DrawAdd("##add", listed, !running, rightX, midY, out var addHovered);
        ImGui.PopID();
        if (clicked)
        {
            CustomMobList.Add(mark.NameId);
        }

        MarkBadges.DrawNameAndZone(markIndex, mark.Rank, textX, rightX - addSlot - 10f * scale, midY, RowLineGap, RowBadgeGap, listed, !addHovered,
            origin, end);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    // The expansion-wide marks form one group after the zones of their expansion, whatever zone first listed them.
    private static int GroupKey(int markIndex, in HuntMark mark)
        => (int)mark.Expansion << GroupExpansionShift | (HuntMarkRegistry.IsExpansionWideAt(markIndex) ? 0 : mark.TerritoryId);

    // "All expansions" follows the plugin language, so the options are rebuilt only on a language switch.
    private static string[] ExpansionOptions()
    {
        var language = Loc.Current;
        if (expansionOptions.Length > 0 && ReferenceEquals(language, optionsLanguage))
        {
            return expansionOptions;
        }

        var options = new string[expansions.Length + 1];
        options[AllExpansions] = Loc.T(L.HuntMarks.AllExpansions);
        for (var index = 0; index < expansions.Length; index++)
        {
            options[index + 1] = ExpansionLabels.Name(expansions[index]);
        }

        expansionOptions = options;
        optionsLanguage = language;
        return options;
    }
}
