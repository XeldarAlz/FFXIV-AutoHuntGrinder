using AutoHuntGrinder.Core;
using AutoHuntGrinder.Core.Achievements;
using AutoHuntGrinder.Core.Custom;
using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Marks;
using AutoHuntGrinder.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using ECommons.DalamudServices;
using System.Numerics;

namespace AutoHuntGrinder.Windows.Sections;

internal static class MarkAchievementBoard
{
    private const float Gap = 8f;
    private const float GroupSpace = 14f;
    private const float GroupLabelBelow = 2f;
    private const float HeaderHeight = 58f;
    private const float PadX = 14f;
    private const float IconSize = 36f;
    private const float IconRounding = 6f;
    private const float IconDimAlpha = 0.55f;
    private const float ChevronGap = 10f;
    private const float ButtonHeight = 28f;
    private const float BodyPadY = 10f;
    private const float CellHeight = 44f;
    private const float CellMinWidth = 250f;
    private const float CellPadX = 10f;
    private const float CellAddSize = 26f;
    private const float CellLineGap = 2f;
    private const float CellBadgeGap = 6f;
    private const float FooterHeight = 30f;
    private const float ProgressBarHeight = 3f;
    private const float BodyRevealMs = 180f;
    private const int CountShift = 16;
    private const long CountMask = 0xFFFF;

    private static Fold[] folds = [];
    private static ProgressReading[] readings = [];
    private static CachedText[] markCounts = [];
    private static CachedText[] progressTexts = [];
    private static CachedText[] inListTexts = [];
    private static CachedText summaryText;
    private static uint pendingAchievementId;
    private static uint refusedAchievementId;
    private static uint unansweredAchievementId;

    private enum Fold : byte { Auto, Open, Closed }

    private enum ActionKind : byte { None, Check, Progress }

    private readonly record struct ProgressReading(uint Current, uint Max);

    private readonly record struct CardAction(ActionKind Kind, string Label, FontAwesomeIcon Icon, bool Enabled, string Tooltip);

    public static void Draw(bool running)
    {
        var achievements = MarkAchievements.All;
        if (achievements.IsEmpty)
        {
            TextDraw.Hint(Loc.T(L.HuntMarks.NoMarks));
            return;
        }

        EnsureState(achievements.Length);
        PollProgress(achievements);
        DrawSummary(achievements);

        var previousExpansion = -1;
        ImGui.PushID("##ahg_mark_achievements");
        for (var index = 0; index < achievements.Length; index++)
        {
            var achievement = achievements[index];
            if ((int)achievement.Expansion != previousExpansion)
            {
                GroupLabel.Draw(ExpansionLabels.Name(achievement.Expansion), previousExpansion >= 0 ? GroupSpace : 0f, GroupLabelBelow);
                previousExpansion = (int)achievement.Expansion;
            }

            DrawCard(index, achievement, running);
        }

        ImGui.PopID();
    }

    private static void EnsureState(int count)
    {
        if (folds.Length == count)
        {
            return;
        }

        folds = new Fold[count];
        readings = new ProgressReading[count];
        markCounts = new CachedText[count];
        progressTexts = new CachedText[count];
        inListTexts = new CachedText[count];
    }

    // One progress request is in flight at a time; its answer lands on the card that asked, and the reader decides when
    // an answer is overdue.
    private static void PollProgress(ReadOnlySpan<MarkAchievement> achievements)
    {
        if (pendingAchievementId == 0)
        {
            return;
        }

        if (AchievementReader.TryGetProgress(pendingAchievementId, out var current, out var max))
        {
            var slot = SlotOf(achievements, pendingAchievementId);
            if (slot >= 0)
            {
                readings[slot] = new ProgressReading(current, max);
            }

            RunLog.Info($"Mark achievements: achievement {pendingAchievementId} progress {current}/{max}");
            pendingAchievementId = 0;
            return;
        }

        if (AchievementReader.AwaitedProgress() == pendingAchievementId)
        {
            return;
        }

        unansweredAchievementId = pendingAchievementId;
        pendingAchievementId = 0;
    }

    private static int SlotOf(ReadOnlySpan<MarkAchievement> achievements, uint achievementId)
    {
        for (var index = 0; index < achievements.Length; index++)
        {
            if (achievements[index].AchievementId == achievementId)
            {
                return index;
            }
        }

        return -1;
    }

    private static void DrawSummary(ReadOnlySpan<MarkAchievement> achievements)
    {
        string text;
        if (AchievementReader.IsLoaded)
        {
            var earned = 0;
            for (var index = 0; index < achievements.Length; index++)
            {
                if (AchievementReader.Status(achievements[index].AchievementId) == AchievementStatus.Complete)
                {
                    earned++;
                }
            }

            text = summaryText.Get((long)earned << CountShift | (uint)achievements.Length,
                static key => Loc.T(L.HuntMarks.EarnedSummary, (int)(key >> CountShift), (int)(key & CountMask)));
        }
        else
        {
            text = Loc.T(AchievementReader.LoadState() is null ? L.Common.PlayerNotLoaded : L.HuntMarks.NotLoaded);
        }

        var origin = ImGui.GetCursorScreenPos();
        using (Fonts.PushCaption())
        {
            TextDraw.At(text, new Vector2(origin.X + 2f * ImGuiHelpers.GlobalScale, origin.Y), Styling.TextDim);
            ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight()));
        }

        Styling.VSpace(4f);
    }

    private static void DrawCard(int index, in MarkAchievement achievement, bool running)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var padX = PadX * scale;
        var gap = Gap * scale;
        var status = AchievementReader.Status(achievement.AchievementId);
        var marks = MarkAchievements.Marks(achievement);
        var open = IsOpen(index, status);
        var columns = Math.Clamp((int)MathF.Floor((width - padX * 2f + gap) / (CellMinWidth * scale + gap)), 1, Math.Max(1, marks.Length));
        var rows = (marks.Length + columns - 1) / columns;
        var headerHeight = HeaderHeight * scale;
        var size = new Vector2(width, headerHeight + (open ? BodyHeight(rows) : 0f));
        var end = origin + size;
        if (!ImGui.IsRectVisible(origin, end))
        {
            ImGui.Dummy(size);
            return;
        }

        ImGui.PushID((int)achievement.AchievementId);
        var reveal = Motion.Transition(Motion.Key("##body"), open, BodyRevealMs);
        var action = ActionFor(index, achievement, status);
        var actionWidth = action.Kind == ActionKind.None ? 0f : PillButton.Width(action.Label, action.Icon) + 10f * scale;
        var headerWidth = MathF.Max(1f, width - padX - actionWidth);
        var hit = Hit.Area("##header", new Vector2(headerWidth, headerHeight));
        var hover = Motion.Hover(Motion.Key("##header"), hit.Hovered);
        if (hit.Clicked)
        {
            folds[index] = open ? Fold.Closed : Fold.Open;
        }

        var drawList = ImGui.GetWindowDrawList();
        var complete = status == AchievementStatus.Complete;
        var accent = complete ? Styling.AccentMint : MarkBadges.RankColor(achievement.Rank);
        Paint.Glass(drawList, origin, end, Styling.CardRounding * scale, accent, complete ? 0.06f : 0.03f, hover * 0.5f);

        var midY = origin.Y + headerHeight * 0.5f;
        if (action.Kind != ActionKind.None)
        {
            DrawAction(achievement, action, end.X - padX, midY);
        }

        DrawHeader(index, achievement, status, marks.Length, origin, headerWidth, midY, open, hover);
        if (open)
        {
            DrawBody(index, achievement, marks, new Vector2(origin.X, origin.Y + headerHeight), width, columns, rows, reveal, running);
        }

        ImGui.PopID();
        DrawProgressBar(index, achievement, complete, origin, end);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    // Earned cards start folded; once a card is clicked it keeps the reader's choice for the session.
    private static bool IsOpen(int index, AchievementStatus status) => folds[index] switch
    {
        Fold.Open => true,
        Fold.Closed => false,
        _ => status != AchievementStatus.Complete,
    };

    private static float BodyHeight(int rows)
        => (BodyPadY * 2f + rows * (CellHeight + Gap) + FooterHeight) * ImGuiHelpers.GlobalScale;

    private static CardAction ActionFor(int index, in MarkAchievement achievement, AchievementStatus status)
    {
        if (status == AchievementStatus.Complete)
        {
            return default;
        }

        if (status == AchievementStatus.Unknown)
        {
            var check = AchievementCheck.Read();
            return new CardAction(ActionKind.Check, check.Label, AchievementCheck.Icon, check.Enabled, check.Tooltip);
        }

        var achievementId = achievement.AchievementId;
        var asking = pendingAchievementId == achievementId;
        var reading = readings[index];
        var known = reading.Max > 0;
        var label = asking ? Loc.T(L.HuntMarks.Asking) : known ? ProgressText(index, reading) : Loc.T(L.HuntMarks.Progress);
        var tooltip = (pendingAchievementId != 0 && !asking) || refusedAchievementId == achievementId ? L.HuntMarks.ProgressBusy
            : unansweredAchievementId == achievementId ? L.HuntMarks.ProgressNoAnswer
            : L.HuntMarks.ProgressHelp;
        return new CardAction(ActionKind.Progress, label, known ? FontAwesomeIcon.Sync : FontAwesomeIcon.ChartBar, pendingAchievementId == 0, Loc.T(tooltip));
    }

    private static string ProgressText(int index, ProgressReading reading)
        => progressTexts[index].Get((long)(reading.Current & CountMask) << CountShift | (reading.Max & CountMask),
            static key => Loc.T(L.HuntMarks.ProgressOf, (int)(key >> CountShift), (int)(key & CountMask)));

    private static void DrawAction(in MarkAchievement achievement, in CardAction action, float rightX, float midY)
    {
        var width = PillButton.Width(action.Label, action.Icon);
        ImGui.SetCursorScreenPos(new Vector2(rightX - width, midY - ButtonHeight * ImGuiHelpers.GlobalScale * 0.5f));
        if (!PillButton.Draw("##action", action.Label, Styling.AccentGlow, PillButton.Emphasis.Tinted, action.Icon, action.Enabled, ButtonHeight, action.Tooltip))
        {
            return;
        }

        if (action.Kind == ActionKind.Check)
        {
            AchievementCheck.Press();
            return;
        }

        RequestProgress(achievement.AchievementId);
    }

    private static void RequestProgress(uint achievementId)
    {
        unansweredAchievementId = 0;
        if (!AchievementReader.RequestProgress(achievementId))
        {
            refusedAchievementId = achievementId;
            return;
        }

        refusedAchievementId = 0;
        pendingAchievementId = achievementId;
    }

    private static void DrawHeader(int index, in MarkAchievement achievement, AchievementStatus status, int markCount, Vector2 origin, float headerWidth,
        float midY, bool open, float hover)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var padX = PadX * scale;
        var chevronWidth = TextDraw.IconSize(FontAwesomeIcon.ChevronDown).X;
        TextDraw.IconCentered(open ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight, new Vector2(origin.X + padX + chevronWidth * 0.5f, midY),
            Vector4.Lerp(Styling.TextMuted, Styling.TextSecondary, hover));

        var iconSize = IconSize * scale;
        var iconMin = new Vector2(origin.X + padX + chevronWidth + ChevronGap * scale, midY - iconSize * 0.5f);
        DrawIcon(drawList, achievement.IconId, iconMin, iconMin + new Vector2(iconSize, iconSize), status == AchievementStatus.Complete);

        var textX = iconMin.X + iconSize + 12f * scale;
        var textRight = origin.X + headerWidth - 10f * scale;
        var lineHeight = ImGui.GetTextLineHeight();
        float captionHeight;
        using (Fonts.PushCaption())
        {
            captionHeight = ImGui.GetTextLineHeight();
        }

        var top = midY - (lineHeight + 3f * scale + captionHeight) * 0.5f;
        var name = TextDraw.Truncate(achievement.Name, textRight - textX - MarkBadges.RankWidth - 8f * scale);
        var nameSize = TextDraw.Measure(name);
        TextDraw.At(name, new Vector2(textX, top), Styling.TextStrong);
        MarkBadges.DrawRank(drawList, achievement.Rank, textX + nameSize.X + 8f * scale, top + lineHeight * 0.5f);

        var (statusText, statusColor) = status switch
        {
            AchievementStatus.Complete => (Loc.T(L.HuntingLog.Earned), Styling.AccentMint),
            AchievementStatus.Incomplete => (Loc.T(L.HuntingLog.NotYet), Styling.AccentAmber),
            _ => (Loc.T(L.HuntingLog.Unknown), Styling.TextMuted),
        };

        using (Fonts.PushCaption())
        {
            var captionY = top + lineHeight + 3f * scale;
            var statusSize = TextDraw.Measure(statusText);
            TextDraw.At(statusText, new Vector2(textX, captionY), statusColor);
            var count = markCounts[index].Get(markCount, static key => Loc.Plural(L.HuntMarks.UniqueMarks, (int)key));
            TextDraw.Trailing(count, textX + statusSize.X, textRight, captionY, Styling.TextDim);
        }
    }

    private static void DrawIcon(ImDrawListPtr drawList, uint iconId, Vector2 min, Vector2 max, bool earned)
    {
        var rounding = IconRounding * ImGuiHelpers.GlobalScale;
        if (iconId == 0)
        {
            Paint.Fill(drawList, min, max, Styling.WithAlpha(Styling.Surface3, 0.8f), rounding);
            TextDraw.IconCentered(FontAwesomeIcon.Trophy, (min + max) * 0.5f, earned ? Styling.AccentMint : Styling.TextMuted);
            return;
        }

        var texture = Svc.Texture.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
        drawList.AddImageRounded(texture.Handle, min, max, Vector2.Zero, Vector2.One,
            Paint.Col(new Vector4(1f, 1f, 1f, earned ? 1f : IconDimAlpha)), rounding, ImDrawFlags.RoundCornersAll);
    }

    private static void DrawBody(int index, in MarkAchievement achievement, ReadOnlySpan<HuntMark> marks, Vector2 origin, float width, int columns, int rows,
        float reveal, bool running)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var padX = PadX * scale;
        var gap = Gap * scale;
        using var fade = Motion.PushAlpha(reveal);
        Paint.Hairline(ImGui.GetWindowDrawList(), new Vector2(origin.X + padX, origin.Y), new Vector2(origin.X + width - padX, origin.Y));

        var innerWidth = width - padX * 2f;
        var cellSize = new Vector2((innerWidth - gap * (columns - 1)) / columns, CellHeight * scale);
        var top = origin.Y + BodyPadY * scale;
        var slot = MathF.Max(MarkBadges.InListWidth(), CellAddSize * scale);
        var listed = 0;
        for (var markOffset = 0; markOffset < marks.Length; markOffset++)
        {
            var column = markOffset % columns;
            var row = markOffset / columns;
            var min = new Vector2(origin.X + padX + column * (cellSize.X + gap), top + row * (cellSize.Y + gap));
            if (DrawCell(marks[markOffset], min, cellSize, slot, running))
            {
                listed++;
            }
        }

        DrawFooter(index, achievement, marks.Length, listed, new Vector2(origin.X + padX, top + rows * (cellSize.Y + gap)), innerWidth, running);
    }

    private static bool DrawCell(in HuntMark mark, Vector2 min, Vector2 size, float slot, bool running)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var end = min + size;
        var listed = CustomMobList.Contains(mark.NameId);
        Paint.Fill(drawList, min, end, listed ? Styling.WithAlpha(Styling.AccentMint, 0.08f) : Styling.WithAlpha(Styling.Surface2, 0.45f), 8f * scale);

        var padX = CellPadX * scale;
        var midY = min.Y + size.Y * 0.5f;
        var rightX = end.X - padX;
        var addHovered = false;
        if (listed)
        {
            MarkBadges.DrawInList(rightX, midY);
        }
        else
        {
            var button = CellAddSize * scale;
            ImGui.SetCursorScreenPos(new Vector2(rightX - button, midY - button * 0.5f));
            ImGui.PushID((int)mark.NameId);
            if (IconButton.Draw(FontAwesomeIcon.Plus, "##add", button, Styling.AccentGlow, Loc.T(L.HuntMarks.AddOne), !running))
            {
                CustomMobList.Add(mark.NameId);
            }

            addHovered = ImGui.IsItemHovered();
            ImGui.PopID();
        }

        MarkBadges.DrawNameAndZone(HuntMarkRegistry.IndexOf(mark.NameId), mark.Rank, min.X + padX, rightX - slot - 8f * scale, midY, CellLineGap, CellBadgeGap,
            listed, !addHovered, min, end);
        return listed;
    }

    private static void DrawFooter(int index, in MarkAchievement achievement, int total, int listed, Vector2 origin, float width, bool running)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var midY = origin.Y + FooterHeight * scale * 0.5f;
        var allListed = listed >= total;
        var label = Loc.T(allListed ? L.HuntMarks.AllInList : L.HuntMarks.AddAll);
        var icon = allListed ? FontAwesomeIcon.Check : FontAwesomeIcon.Plus;
        var buttonWidth = PillButton.Width(label, icon);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - buttonWidth, midY - ButtonHeight * scale * 0.5f));
        if (PillButton.Draw("##add_all", label, Styling.AccentGlow, PillButton.Emphasis.Tinted, icon, !allListed && !running, ButtonHeight,
                Loc.T(L.HuntMarks.AddAllHelp)))
        {
            AddAll(achievement);
        }

        using (Fonts.PushCaption())
        {
            var text = inListTexts[index].Get((long)listed << CountShift | (uint)total,
                static key => Loc.T(L.HuntMarks.InListCount, (int)(key >> CountShift), (int)(key & CountMask)));
            var textSize = TextDraw.Measure(text);
            TextDraw.At(TextDraw.Truncate(text, width - buttonWidth - 12f * scale), new Vector2(origin.X + 2f * scale, midY - textSize.Y * 0.5f), Styling.TextDim);
        }
    }

    private static void AddAll(in MarkAchievement achievement)
    {
        var marks = MarkAchievements.Marks(achievement);
        var added = 0;
        for (var index = 0; index < marks.Length; index++)
        {
            if (CustomMobList.Add(marks[index].NameId))
            {
                added++;
            }
        }

        RunLog.Info($"Mark achievements: added {added} of {marks.Length} marks from {achievement.Name} ({achievement.AchievementId}) to the custom list");
    }

    private static void DrawProgressBar(int index, in MarkAchievement achievement, bool complete, Vector2 origin, Vector2 end)
    {
        var reading = readings[index];
        if (!complete && reading.Max == 0)
        {
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var inset = Styling.CardRounding * scale * 0.9f;
        var barHeight = ProgressBarHeight * scale;
        var fraction = complete ? 1f : (float)reading.Current / reading.Max;
        Paint.Bar(ImGui.GetWindowDrawList(), new Vector2(origin.X + inset, end.Y - barHeight - 4f * scale), end.X - origin.X - inset * 2f, barHeight, fraction,
            complete ? Styling.AccentMint : MarkBadges.RankColor(achievement.Rank));
    }
}
