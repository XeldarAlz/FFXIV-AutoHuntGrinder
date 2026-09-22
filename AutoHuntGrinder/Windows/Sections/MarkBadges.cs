using AutoHuntGrinder.Core.HuntingLog;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Marks;
using AutoHuntGrinder.Core.Spawns;
using AutoHuntGrinder.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoHuntGrinder.Windows.Sections;

internal static class MarkBadges
{
    private const float RankSize = 22f;
    private const float RankRounding = 6f;
    private const float AddHeight = 28f;
    private const float InListGap = 5f;
    private const string LetterB = "B";
    private const string LetterA = "A";
    private const string LetterS = "S";

    private static SpawnCoverage[]? coverages;

    public static Vector4 RankColor(HuntMarkRank rank) => rank switch
    {
        HuntMarkRank.B => Styling.AccentBlue,
        HuntMarkRank.A => Styling.AccentAmber,
        _ => Styling.AccentNebula,
    };

    public static string Letter(HuntMarkRank rank) => rank switch
    {
        HuntMarkRank.B => LetterB,
        HuntMarkRank.A => LetterA,
        _ => LetterS,
    };

    public static float RankWidth => RankSize * ImGuiHelpers.GlobalScale;

    public static float DrawRank(ImDrawListPtr drawList, HuntMarkRank rank, float leftX, float midY, float size = RankSize)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var side = size * scale;
        var rounding = RankRounding * scale;
        var min = new Vector2(leftX, midY - side * 0.5f);
        var max = min + new Vector2(side, side);
        var fill = RankColor(rank);
        Paint.Fill(drawList, min, max, fill, rounding);
        Paint.TopLight(drawList, min, max, rounding, 0.18f);
        using (Fonts.PushCaption())
        {
            TextDraw.Middle(Letter(rank), min, max, Styling.ForegroundOn(fill));
        }

        return side;
    }

    // An expansion-wide mark keeps only the first zone that lists it, which says nothing about where it appears.
    public static string ZoneLabel(int markIndex)
        => HuntMarkRegistry.IsExpansionWideAt(markIndex) ? Loc.T(L.CustomList.AnyZone) : HuntMarkRegistry.ZoneNameAt(markIndex);

    // Spawn data never changes while the plugin runs, so each mark's coverage is looked up once.
    public static SpawnCoverage CoverageAt(int markIndex)
    {
        var marks = HuntMarkRegistry.Marks;
        if ((uint)markIndex >= (uint)marks.Length)
        {
            return SpawnCoverage.NoData;
        }

        coverages ??= BuildCoverages(marks);
        return coverages[markIndex];
    }

    // Points stands for any spawn data a search can use, area labels and the zone's shared hunt spawn points included;
    // territoryId 0 asks about every zone.
    public static SpawnCoverage CoverageIn(uint nameId, uint territoryId)
    {
        if (MobSpawns.IsSearchable(nameId, territoryId) || HuntSpawns.Covers(nameId, territoryId))
        {
            return SpawnCoverage.Points;
        }

        return MobSpawns.IsFateOnly(nameId, territoryId) ? SpawnCoverage.FateOnly : SpawnCoverage.NoData;
    }

    // An expansion-wide mark's note already tells of its trigger, so it stands in for the S rank one.
    public static LocString? RankHelp(int markIndex, HuntMarkRank rank)
    {
        if (HuntMarkRegistry.IsExpansionWideAt(markIndex))
        {
            return L.HuntMarks.ExpansionWideHelp;
        }

        return rank == HuntMarkRank.S ? L.HuntMarks.SRankHelp : null;
    }

    // Gaps are unscaled. The tooltip shows while the pointer is inside the hover rectangle, unless tooltip is false
    // because the pointer is on the card's own button.
    public static void DrawNameAndZone(int markIndex, HuntMarkRank rank, float textX, float textRight, float midY, float lineGap, float badgeGap,
        bool listed, bool tooltip, Vector2 hoverMin, Vector2 hoverMax)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var lineHeight = ImGui.GetTextLineHeight();
        float captionHeight;
        using (Fonts.PushCaption())
        {
            captionHeight = ImGui.GetTextLineHeight();
        }

        var top = midY - (lineHeight + lineGap * scale + captionHeight) * 0.5f;
        var name = HuntMarkRegistry.NameAt(markIndex);
        TextDraw.At(TextDraw.Truncate(name, textRight - textX), new Vector2(textX, top), listed ? Styling.TextSecondary : Styling.TextStrong);

        var captionY = top + lineHeight + lineGap * scale;
        var coverage = CoverageAt(markIndex);
        var zoneRight = textRight;
        var badgeWidth = SpawnBadge.Draw(ImGui.GetWindowDrawList(), coverage, textRight, captionY + captionHeight * 0.5f);
        if (badgeWidth > 0f)
        {
            zoneRight -= badgeWidth + badgeGap * scale;
        }

        using (Fonts.PushCaption())
        {
            TextDraw.At(TextDraw.Truncate(ZoneLabel(markIndex), zoneRight - textX), new Vector2(textX, captionY), Styling.TextDim);
        }

        if (tooltip && HasTooltip(markIndex, rank, coverage) && Hit.HoveringRect(hoverMin, hoverMax))
        {
            DrawTooltip(markIndex, name, rank, coverage);
        }
    }

    public static float AddSlotWidth() => MathF.Max(PillButton.Width(Loc.T(L.HuntMarks.Add), FontAwesomeIcon.Plus), InListWidth());

    public static bool DrawAdd(string id, bool listed, bool enabled, float rightX, float midY, out bool hovered)
    {
        hovered = false;
        if (listed)
        {
            DrawInList(rightX, midY);
            return false;
        }

        var label = Loc.T(L.HuntMarks.Add);
        var width = PillButton.Width(label, FontAwesomeIcon.Plus);
        ImGui.SetCursorScreenPos(new Vector2(rightX - width, midY - AddHeight * ImGuiHelpers.GlobalScale * 0.5f));
        var clicked = PillButton.Draw(id, label, Styling.AccentGlow, PillButton.Emphasis.Tinted, FontAwesomeIcon.Plus,
            enabled, AddHeight, Loc.T(L.HuntMarks.AddHelp));
        hovered = ImGui.IsItemHovered();
        return clicked;
    }

    public static void DrawInList(float rightX, float midY)
    {
        var label = Loc.T(L.HuntMarks.InList);
        var iconSize = TextDraw.IconSize(FontAwesomeIcon.Check);
        using (Fonts.PushCaption())
        {
            var labelSize = TextDraw.Measure(label);
            var labelX = rightX - labelSize.X;
            TextDraw.At(label, new Vector2(labelX, midY - labelSize.Y * 0.5f), Styling.AccentMint);
            var iconX = labelX - InListGap * ImGuiHelpers.GlobalScale - iconSize.X;
            TextDraw.Icon(FontAwesomeIcon.Check, new Vector2(iconX, midY - iconSize.Y * 0.5f), Styling.AccentMint);
        }
    }

    public static float InListWidth()
    {
        var iconWidth = TextDraw.IconSize(FontAwesomeIcon.Check).X + InListGap * ImGuiHelpers.GlobalScale;
        using (Fonts.PushCaption())
        {
            return iconWidth + TextDraw.Measure(Loc.T(L.HuntMarks.InList)).X;
        }
    }

    private static bool HasTooltip(int markIndex, HuntMarkRank rank, SpawnCoverage coverage)
        => coverage != SpawnCoverage.Points || RankHelp(markIndex, rank).HasValue;

    private static void DrawTooltip(int markIndex, string name, HuntMarkRank rank, SpawnCoverage coverage)
    {
        using (Tooltip.Begin())
        {
            Tooltip.Text(name, Styling.TextStrong);
            if (RankHelp(markIndex, rank) is { } help)
            {
                Tooltip.Text(Loc.T(help), Styling.Lighten(RankColor(rank), 0.25f));
            }

            if (coverage == SpawnCoverage.FateOnly)
            {
                Tooltip.Text(Loc.T(L.CustomList.FateOnlyHelp), Styling.TextDim);
            }
            else if (coverage == SpawnCoverage.NoData)
            {
                Tooltip.Text(Loc.T(L.HuntMarks.NoSpawnsHelp), Styling.TextDim);
            }
        }
    }

    private static SpawnCoverage[] BuildCoverages(ReadOnlySpan<HuntMark> marks)
    {
        var built = new SpawnCoverage[marks.Length];
        for (var index = 0; index < marks.Length; index++)
        {
            built[index] = CoverageIn(marks[index].NameId, HuntMarkRegistry.SpawnTerritoryAt(index));
        }

        return built;
    }
}
