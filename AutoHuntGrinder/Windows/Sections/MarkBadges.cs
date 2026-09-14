using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Marks;
using AutoHuntGrinder.Core.Spawns;
using AutoHuntGrinder.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoHuntGrinder.Windows.Sections;

internal enum MarkSpawnState : byte
{
    Points,
    FateOnly,
    NoData,
}

internal static class MarkBadges
{
    private const float RankSize = 22f;
    private const float RankRounding = 6f;
    private const float AddHeight = 28f;
    private const float InListGap = 5f;
    private const string LetterB = "B";
    private const string LetterA = "A";
    private const string LetterS = "S";

    private static MarkSpawnState[]? spawnStates;

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

    // Spawn data never changes while the plugin runs, so each mark's state is looked up once.
    public static MarkSpawnState SpawnStateAt(int markIndex)
    {
        var marks = HuntMarkRegistry.Marks;
        if ((uint)markIndex >= (uint)marks.Length)
        {
            return MarkSpawnState.NoData;
        }

        spawnStates ??= BuildSpawnStates(marks);
        return spawnStates[markIndex];
    }

    public static float DrawSpawn(ImDrawListPtr drawList, MarkSpawnState state, float rightX, float midY) => state switch
    {
        MarkSpawnState.FateOnly => Badge.Draw(drawList, Loc.T(L.HuntingLog.BadgeFateOnly), Styling.AccentNebula, rightX, midY),
        MarkSpawnState.NoData => Badge.Draw(drawList, Loc.T(L.HuntingLog.BadgeNoSpawns), Styling.AccentRose, rightX, midY),
        _ => 0f,
    };

    public static bool HasTooltip(int markIndex, HuntMarkRank rank, MarkSpawnState state)
        => rank == HuntMarkRank.S || state != MarkSpawnState.Points || HuntMarkRegistry.IsExpansionWideAt(markIndex);

    public static void DrawTooltip(int markIndex, string name, HuntMarkRank rank, MarkSpawnState state)
    {
        using (Tooltip.Begin())
        {
            Tooltip.Text(name, Styling.TextStrong);
            if (HuntMarkRegistry.IsExpansionWideAt(markIndex))
            {
                Tooltip.Text(Loc.T(L.HuntMarks.ExpansionWideHelp), Styling.Lighten(RankColor(rank), 0.25f));
            }
            else if (rank == HuntMarkRank.S)
            {
                Tooltip.Text(Loc.T(L.HuntMarks.SRankHelp), Styling.Lighten(RankColor(rank), 0.25f));
            }

            if (state == MarkSpawnState.FateOnly)
            {
                Tooltip.Text(Loc.T(L.CustomList.FateOnlyHelp), Styling.TextDim);
            }
            else if (state == MarkSpawnState.NoData)
            {
                Tooltip.Text(Loc.T(L.HuntMarks.NoSpawnsHelp), Styling.TextDim);
            }
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

    public static float DrawInList(float rightX, float midY)
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
            return rightX - iconX;
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

    private static MarkSpawnState[] BuildSpawnStates(ReadOnlySpan<HuntMark> marks)
    {
        var states = new MarkSpawnState[marks.Length];
        for (var index = 0; index < marks.Length; index++)
        {
            states[index] = SpawnStateOf(marks[index].NameId, HuntMarkRegistry.SpawnTerritoryAt(index));
        }

        return states;
    }

    private static MarkSpawnState SpawnStateOf(uint nameId, uint territoryId)
    {
        if (MobSpawns.IsSearchable(nameId, territoryId))
        {
            return MarkSpawnState.Points;
        }

        return MobSpawns.IsFateOnly(nameId, territoryId) ? MarkSpawnState.FateOnly : MarkSpawnState.NoData;
    }
}
