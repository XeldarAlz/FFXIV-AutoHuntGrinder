using AutoHuntGrinder.Core.Custom;
using AutoHuntGrinder.Core.HuntingLog;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Marks;
using AutoHuntGrinder.Core.Spawns;
using AutoHuntGrinder.Core.Tasks;
using AutoHuntGrinder.Core.Travel;
using AutoHuntGrinder.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoHuntGrinder.Windows.Sections;

internal static class CustomListEditor
{
    private const float Gap = 8f;
    private const float ViewSwitchHeight = 34f;
    private const float ViewSlide = 8f;
    private const int VisibleResults = 8;
    private const float ResultRowHeight = 38f;
    private const float SummaryRowHeight = 32f;
    private const float RowPadX = 12f;
    private const float RowPadY = 10f;
    private const float LineGap = 6f;
    private const float WideRowMinWidth = 720f;
    private const float StepperWidth = 132f;
    private const float ZoneWidth = 190f;
    private const float ToggleWidth = 40f;
    private const float ToggleHeight = 22f;
    private const float ActionSize = 28f;
    private const float ProgressBarHeight = 3f;
    private const int MinNeeded = 1;
    private const int MaxNeeded = 999;
    private const int CountShift = 32;
    private const long CountMask = 0xFFFFFFFF;

    // One slot past the visible rows, so a full buffer tells that more names match.
    private static readonly int[] results = new int[VisibleResults + 1];
    private static readonly string[] resultZones = new string[VisibleResults];
    private static readonly Dictionary<uint, ZoneOptions> zoneOptions = new();
    private static readonly Segmented.Item[] viewItems = new Segmented.Item[ViewCount];

    private static CachedText[] entryKills = new CachedText[16];
    private static CachedText summaryText;
    private static CachedText moreText;
    private static string query = string.Empty;
    private static string searchedQuery = string.Empty;
    private static string noMatchesText = string.Empty;
    private static LanguageInfo? searchedLanguage;
    private static int resultCount;
    private static bool searchFocused;
    private static int currentView;

    private const int ViewCount = 3;

    private enum View : byte { MyList, HuntMarks, MarkAchievements }

    private enum RowAction : byte { None, Reset, Remove }

    private readonly record struct ZoneOptions(string[] Labels, LanguageInfo Language);

    public static void Draw(Configuration configuration, AutoHuntController controller, bool scrollIntoView)
    {
        var running = controller.Running;
        LibraryHeader.Draw(Loc.T(L.CustomList.Library), scrollIntoView);
        Styling.VSpace(10f);
        DrawViewSwitch();
        Styling.VSpace(12f);

        using var reveal = Motion.PushSwitch("##ahg_custom_view", currentView, slide: ViewSlide);
        switch ((View)currentView)
        {
            case View.HuntMarks:
                HuntMarkBrowser.Draw(running);
                break;
            case View.MarkAchievements:
                MarkAchievementBoard.Draw(running);
                break;
            default:
                DrawMyList(configuration, running);
                break;
        }
    }

    private static void DrawViewSwitch()
    {
        viewItems[(int)View.MyList] = new Segmented.Item(FontAwesomeIcon.ListUl, Loc.T(L.HuntMarks.ViewMyList));
        viewItems[(int)View.HuntMarks] = new Segmented.Item(FontAwesomeIcon.Skull, Loc.T(L.HuntMarks.ViewMarks));
        viewItems[(int)View.MarkAchievements] = new Segmented.Item(FontAwesomeIcon.Trophy, Loc.T(L.HuntMarks.ViewAchievements));
        Segmented.Draw("##ahg_custom_views", viewItems, ref currentView, height: ViewSwitchHeight);
    }

    private static void DrawMyList(Configuration configuration, bool running)
    {
        SearchField.Draw("##ahg_custom_search", Loc.T(L.CustomList.SearchHint), ref query, ref searchFocused, ImGui.GetContentRegionAvail().X);
        RefreshResults();
        DrawResults(running);

        Styling.VSpace(14f);
        DrawEntries(configuration, running);
    }

    // The catalog is searched only when the text changes; zone lines follow the plugin language, so a switch rebuilds them.
    private static void RefreshResults()
    {
        var language = Loc.Current;
        if (string.Equals(query, searchedQuery, StringComparison.Ordinal) && ReferenceEquals(language, searchedLanguage))
        {
            return;
        }

        searchedQuery = query;
        searchedLanguage = language;
        resultCount = CustomMobCatalog.Search(query, results);
        var visible = Math.Min(resultCount, VisibleResults);
        var nameIds = CustomMobCatalog.NameIds;
        for (var index = 0; index < visible; index++)
        {
            resultZones[index] = ZoneSummary(nameIds[results[index]]);
        }

        var trimmed = query.Trim();
        noMatchesText = resultCount == 0 && trimmed.Length > 0 ? Loc.T(L.Common.NoMatches, trimmed) : string.Empty;
    }

    // Zones a search can use are listed first, so the first zone reads FATE only when every zone does.
    private static string ZoneSummary(uint nameId)
    {
        var territories = MobSpawns.Territories(nameId);
        if (territories.Length == 0)
        {
            return string.Empty;
        }

        var first = ZoneLabel(nameId, territories[0]);
        return territories.Length == 1 ? first : Loc.T(L.CustomList.ZonesMore, first, territories.Length - 1);
    }

    private static string ZoneLabel(uint nameId, uint territoryId)
    {
        var zone = TerritoryNames.Of(territoryId);
        return MobSpawns.IsFateOnly(nameId, territoryId) ? Loc.T(L.CustomList.ZoneFateOnly, zone) : zone;
    }

    private static void DrawResults(bool running)
    {
        if (resultCount == 0)
        {
            if (noMatchesText.Length > 0)
            {
                Styling.VSpace(6f);
                Caption(noMatchesText, Styling.TextMuted);
            }

            return;
        }

        Styling.VSpace(6f);
        var visible = Math.Min(resultCount, VisibleResults);
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.PushID("##ahg_custom_results");
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(Gap, 2f) * scale))
        {
            for (var index = 0; index < visible; index++)
            {
                DrawResultRow(index, running);
            }
        }

        ImGui.PopID();
        if (resultCount > VisibleResults)
        {
            Styling.VSpace(2f);
            Caption(moreText.Get(VisibleResults, static key => Loc.T(L.CustomList.SearchMore, (int)key)), Styling.TextMuted);
        }
    }

    private static void DrawResultRow(int index, bool running)
    {
        var catalogIndex = results[index];
        var nameId = CustomMobCatalog.NameIds[catalogIndex];
        var name = CustomMobCatalog.Names[catalogIndex];
        var listed = CustomMobList.Contains(nameId);

        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, ResultRowHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;

        ImGui.PushID((int)nameId);
        var hit = Hit.Area("##result", size, !listed && !running);
        var hover = Motion.Hover(Motion.Key("##result"), hit.Hovered);
        ImGui.PopID();

        if (hit.Clicked)
        {
            CustomMobList.Add(nameId);
        }

        var drawList = ImGui.GetWindowDrawList();
        Paint.Fill(drawList, origin, end, Styling.WithAlpha(Styling.Surface2, 0.30f + 0.45f * hover), 8f * scale);

        var padX = RowPadX * scale;
        var midY = origin.Y + size.Y * 0.5f;
        var (icon, label, color) = listed
            ? (FontAwesomeIcon.Check, Loc.T(L.CustomList.Added), Styling.AccentMint)
            : (FontAwesomeIcon.Plus, Loc.T(L.CustomList.Add), Vector4.Lerp(Styling.AccentGlow, Styling.AccentGlowSoft, hover));
        var actionRight = end.X - padX;
        using (Fonts.PushCaption())
        {
            var labelSize = TextDraw.Measure(label);
            TextDraw.At(label, new Vector2(actionRight - labelSize.X, midY - labelSize.Y * 0.5f), color);
            actionRight -= labelSize.X + 5f * scale;
        }

        var iconSize = TextDraw.IconSize(icon);
        TextDraw.Icon(icon, new Vector2(actionRight - iconSize.X, midY - iconSize.Y * 0.5f), color);
        actionRight -= iconSize.X + 14f * scale;

        var textX = origin.X + padX;
        var nameText = TextDraw.Truncate(name, actionRight - textX);
        var nameSize = TextDraw.Measure(nameText);
        TextDraw.At(nameText, new Vector2(textX, midY - nameSize.Y * 0.5f), listed ? Styling.TextDim : Vector4.Lerp(Styling.TextSecondary, Styling.TextStrong, hover));

        using (Fonts.PushCaption())
        {
            var zoneY = midY - ImGui.GetTextLineHeight() * 0.5f;
            TextDraw.Trailing(resultZones[index], textX + nameSize.X, actionRight, zoneY, Styling.TextMuted);
        }
    }

    private static void DrawEntries(Configuration configuration, bool running)
    {
        var entries = configuration.CustomMobs;
        if (entries.Count == 0)
        {
            Caption(Loc.T(L.CustomList.Empty), Styling.TextMuted);
            return;
        }

        DrawSummaryRow(entries, running);
        Styling.VSpace(6f);
        EnsureKillCaches(entries.Count);

        var scale = ImGuiHelpers.GlobalScale;
        var wide = ImGui.GetContentRegionAvail().X >= WideRowMinWidth * scale;
        var resetIndex = -1;
        var removeIndex = -1;
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(Gap, Gap) * scale))
        {
            for (var index = 0; index < entries.Count; index++)
            {
                switch (DrawEntryRow(configuration, entries[index], index, wide, running))
                {
                    case RowAction.Reset:
                        resetIndex = index;
                        break;
                    case RowAction.Remove:
                        removeIndex = index;
                        break;
                }
            }
        }

        if (resetIndex >= 0)
        {
            CustomMobList.Reset(resetIndex);
        }

        if (removeIndex >= 0)
        {
            CustomMobList.Remove(removeIndex);
        }
    }

    private static void DrawSummaryRow(List<CustomMobEntry> entries, bool running)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var available = ImGui.GetContentRegionAvail().X;
        var rowHeight = SummaryRowHeight * scale;
        var enabled = 0;
        var anyKills = false;
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].Enabled)
            {
                enabled++;
            }

            anyKills |= entries[index].Killed > 0;
        }

        using (Fonts.PushCaption())
        {
            var summary = summaryText.Get((long)entries.Count << CountShift | (uint)enabled,
                static key => Loc.Plural(L.CustomList.Summary, (int)(key >> CountShift), (int)(key & CountMask)));
            var summarySize = TextDraw.Measure(summary);
            TextDraw.At(summary, new Vector2(origin.X + 2f * scale, origin.Y + (rowHeight - summarySize.Y) * 0.5f), Styling.TextDim);
        }

        var label = Loc.T(L.CustomList.ResetAll);
        var width = PillButton.Width(label, FontAwesomeIcon.UndoAlt);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + available - width, origin.Y));
        if (PillButton.Draw("##ahg_custom_reset_all", label, Styling.AccentRose, PillButton.Emphasis.Ghost, FontAwesomeIcon.UndoAlt,
                enabled: anyKills && !running, height: SummaryRowHeight, tooltip: Loc.T(L.CustomList.ResetAllHelp)))
        {
            CustomMobList.ResetAll();
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(available, rowHeight));
    }

    private static RowAction DrawEntryRow(Configuration configuration, CustomMobEntry entry, int index, bool wide, bool running)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var frame = ImGui.GetFrameHeight();
        var line = MathF.Max(frame, ActionSize * scale);
        var padX = RowPadX * scale;
        var padY = RowPadY * scale;
        var lineGap = LineGap * scale;
        var barSpace = (ProgressBarHeight + 4f) * scale;
        var height = padY * 2f + barSpace + (wide ? line : line * 2f + lineGap);
        var size = new Vector2(ImGui.GetContentRegionAvail().X, height);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        var rounding = Styling.CardRounding * scale;
        var done = entry.Killed >= entry.Needed;
        var accent = done ? Styling.AccentMint : Styling.AccentGlow;
        Paint.Glass(drawList, origin, end, rounding, accent, !entry.Enabled ? 0f : done ? 0.06f : 0.03f);

        var action = RowAction.None;
        var midY = origin.Y + padY + line * 0.5f;
        var actionSize = ActionSize * scale;
        ImGui.PushID((int)entry.NameId);

        var x = end.X - padX - actionSize;
        ImGui.SetCursorScreenPos(new Vector2(x, midY - actionSize * 0.5f));
        if (IconButton.Draw(FontAwesomeIcon.Times, "##remove", actionSize, Styling.AccentRose, Loc.T(L.CustomList.Remove), enabled: !running))
        {
            action = RowAction.Remove;
        }

        x -= actionSize + 2f * scale;
        ImGui.SetCursorScreenPos(new Vector2(x, midY - actionSize * 0.5f));
        if (IconButton.Draw(FontAwesomeIcon.UndoAlt, "##reset", actionSize, null, Loc.T(L.CustomList.Reset), enabled: !running && entry.Killed > 0))
        {
            action = RowAction.Reset;
        }

        x -= ToggleWidth * scale + 10f * scale;
        ImGui.SetCursorScreenPos(new Vector2(x, midY - ToggleHeight * scale * 0.5f));
        var enabled = entry.Enabled;
        if (ToggleSwitch.Draw("##enabled", ref enabled) && !running)
        {
            entry.Enabled = enabled;
            configuration.Save();
        }

        if (ImGui.IsItemHovered())
        {
            Tooltip.Show(Loc.T(L.CustomList.EnabledHelp));
        }

        x -= 14f * scale;
        var kills = entryKills[index].Kills(entry.Killed, entry.Needed);
        var killsSize = TextDraw.Measure(kills);
        x -= killsSize.X;
        TextDraw.At(kills, new Vector2(x, midY - killsSize.Y * 0.5f), done ? Styling.AccentMint : Styling.TextSecondary);
        x -= 14f * scale;

        var controlsY = wide ? midY - frame * 0.5f : origin.Y + padY + line + lineGap + (line - frame) * 0.5f;
        var controlsX = origin.X + padX;
        if (wide)
        {
            x -= ZoneWidth * scale;
            DrawZone(configuration, entry, new Vector2(x, controlsY), running);
            x -= Gap * scale + StepperWidth * scale;
            DrawNeeded(configuration, entry, new Vector2(x, controlsY), running);
            x -= 14f * scale;
        }
        else
        {
            DrawNeeded(configuration, entry, new Vector2(controlsX, controlsY), running);
            DrawZone(configuration, entry, new Vector2(controlsX + StepperWidth * scale + Gap * scale, controlsY), running);
        }

        var coverage = MarkBadges.CoverageIn(entry.NameId, CustomMobList.SearchTerritory(entry));
        if (coverage != SpawnCoverage.Points)
        {
            x -= DrawSpawnBadge(drawList, coverage, x, midY, line) + 10f * scale;
        }

        var nameX = controlsX;
        var markIndex = HuntMarkRegistry.IndexOf(entry.NameId);
        if (markIndex != HuntMarkRegistry.NotFound)
        {
            nameX += DrawRankBadge(drawList, markIndex, controlsX, midY) + 8f * scale;
        }

        var name = TextDraw.Truncate(CustomMobCatalog.NameOf(entry.NameId), x - nameX);
        var nameSize = TextDraw.Measure(name);
        TextDraw.At(name, new Vector2(nameX, midY - nameSize.Y * 0.5f), entry.Enabled ? Styling.TextStrong : Styling.TextDim);
        ImGui.PopID();

        var inset = rounding * 0.9f;
        var barHeight = ProgressBarHeight * scale;
        var fraction = entry.Needed > 0 ? Math.Min(1f, (float)entry.Killed / entry.Needed) : 0f;
        Paint.Bar(drawList, new Vector2(origin.X + inset, end.Y - barHeight - 4f * scale), size.X - inset * 2f, barHeight, fraction, accent);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
        return action;
    }

    // Catalog mobs always have spawn data, so only a hunt mark can read as having none.
    private static float DrawSpawnBadge(ImDrawListPtr drawList, SpawnCoverage coverage, float rightX, float midY, float line)
    {
        var width = SpawnBadge.Draw(drawList, coverage, rightX, midY);
        if (Hit.HoveringRect(new Vector2(rightX - width, midY - line * 0.5f), new Vector2(rightX, midY + line * 0.5f)))
        {
            Tooltip.Show(Loc.T(coverage == SpawnCoverage.FateOnly ? L.CustomList.FateOnlyHelp : L.HuntMarks.NoSpawnsHelp));
        }

        return width;
    }

    private static float DrawRankBadge(ImDrawListPtr drawList, int markIndex, float leftX, float midY)
    {
        var rank = HuntMarkRegistry.Marks[markIndex].Rank;
        var width = MarkBadges.DrawRank(drawList, rank, leftX, midY);
        if (MarkBadges.RankHelp(markIndex, rank) is { } help
            && Hit.HoveringRect(new Vector2(leftX, midY - width * 0.5f), new Vector2(leftX + width, midY + width * 0.5f)))
        {
            Tooltip.Show(Loc.T(help));
        }

        return width;
    }

    // A drag edits the count on every frame it moves, so its saves are debounced.
    private static void DrawNeeded(Configuration configuration, CustomMobEntry entry, Vector2 position, bool running)
    {
        ImGui.SetCursorScreenPos(position);
        var needed = (int)entry.Needed;
        if (!Stepper.Draw("##needed", ref needed, 1, MinNeeded, MaxNeeded, Loc.T(L.CustomList.NeededFormat), StepperWidth) || running)
        {
            return;
        }

        entry.Needed = (ushort)Math.Clamp(needed, MinNeeded, MaxNeeded);
        configuration.SaveDebounced();
    }

    private static void DrawZone(Configuration configuration, CustomMobEntry entry, Vector2 position, bool running)
    {
        var labels = ZoneLabels(entry.NameId);
        var territories = MobSpawns.Territories(entry.NameId);
        var selected = 0;
        for (var index = 0; index < territories.Length; index++)
        {
            if (territories[index] == entry.PinnedTerritoryId)
            {
                selected = index + 1;
                break;
            }
        }

        ImGui.SetCursorScreenPos(position);
        if (!Dropdown.Draw("##ahg_custom_zone", labels, ref selected, ZoneWidth) || running)
        {
            return;
        }

        entry.PinnedTerritoryId = selected == 0 || selected > territories.Length ? 0u : territories[selected - 1];
        configuration.Save();
    }

    // "Any zone" follows the plugin language and the zone names the client, so a list is rebuilt only on a language switch.
    private static string[] ZoneLabels(uint nameId)
    {
        var language = Loc.Current;
        if (zoneOptions.TryGetValue(nameId, out var options) && ReferenceEquals(options.Language, language))
        {
            return options.Labels;
        }

        var territories = MobSpawns.Territories(nameId);
        var labels = new string[territories.Length + 1];
        labels[0] = Loc.T(L.CustomList.AnyZone);
        for (var index = 0; index < territories.Length; index++)
        {
            labels[index + 1] = ZoneLabel(nameId, territories[index]);
        }

        zoneOptions[nameId] = new ZoneOptions(labels, language);
        return labels;
    }

    private static void EnsureKillCaches(int count)
    {
        if (entryKills.Length >= count)
        {
            return;
        }

        Array.Resize(ref entryKills, Math.Max(count, entryKills.Length * 2));
    }

    private static void Caption(string text, Vector4 color)
    {
        var origin = ImGui.GetCursorScreenPos();
        using (Fonts.PushCaption())
        {
            TextDraw.At(text, new Vector2(origin.X + 2f * ImGuiHelpers.GlobalScale, origin.Y), color);
            ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight()));
        }
    }
}
