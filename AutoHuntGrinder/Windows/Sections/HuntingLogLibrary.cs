using AutoHuntGrinder.Core.Achievements;
using AutoHuntGrinder.Core.HuntingLog;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Tasks;
using AutoHuntGrinder.Core.Travel;
using AutoHuntGrinder.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Numerics;
using ClientAchievementState = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement.AchievementState;

namespace AutoHuntGrinder.Windows.Sections;

internal static class HuntingLogLibrary
{
    private const float Gap = 8f;
    private const float ChipHeight = 50f;
    private const float ChipMinWidth = 150f;
    private const float QueueBadgeRadius = 9f;
    private const float RankPillHeight = 30f;
    private const float RankPillPadX = 12f;
    private const float EntryRowHeight = 56f;
    private const float EntryMinWidth = 320f;
    private const float FooterHeight = 60f;
    private const float CheckButtonHeight = 28f;
    private const float ProgressBarHeight = 3f;
    private const float BadgePadX = 7f;
    private const float ListSlide = 8f;
    private const byte FollowCurrentRank = byte.MaxValue;
    private const int KillsShift = 16;
    private const long KillsMask = 0xFFFF;
    private const int MaxRows = HuntingLogRegistry.EntriesPerRank * HuntingLogRegistry.TargetsPerEntry;

    private static readonly byte[] pickerSlots = new byte[HuntingLogRegistry.SlotCount];
    private static readonly string[] queueNumbers = BuildQueueNumbers(HuntingLogRegistry.SlotCount);
    private static readonly EntryRow[] rows = new EntryRow[MaxRows];
    private static readonly CachedText[] rowKills = new CachedText[MaxRows];
    private static readonly CachedText[] rankLabels = new CachedText[HuntingLogRegistry.MaxRanks];
    private static readonly CachedText[] levelFloorTexts = new CachedText[HuntingLogRegistry.MaxRanks];
    private static readonly CachedText[] footerTitles = new CachedText[HuntingLogRegistry.SlotCount];
    private static readonly string?[] achievementNames = new string?[HuntingLogRegistry.SlotCount];

    private static CachedText headerCaption;
    private static CachedText queuePositionText;
    private static byte viewSlot = HuntingLogRegistry.NoLog;
    private static byte viewRank = FollowCurrentRank;
    private static byte builtSlot = HuntingLogRegistry.NoLog;
    private static byte builtRank = FollowCurrentRank;
    private static LanguageInfo? builtLanguage;
    private static int rowCount;
    private static bool checkRefused;

    private enum RankState : byte { Done, Current, Locked }

    private readonly record struct EntryRow(byte EntryIndex, HuntingLogTarget Target, string Name, string ZoneLine, SpawnCoverage Coverage);

    public static void Draw(Configuration configuration, AutoHuntController controller, bool scrollIntoView)
    {
        HuntingLogReader.Refresh();
        DrawHeader(scrollIntoView);
        Styling.VSpace(10f);

        var count = Svc.ClientState.IsLoggedIn ? CollectPicker() : 0;
        if (count == 0)
        {
            EmptyHint(Loc.T(L.HuntingLog.NotLoggedIn));
            return;
        }

        EnsureViewSlot(configuration.HuntingLogQueue, count, scrollIntoView);
        ImGui.PushID("##ahg_log_picker");
        DrawPicker(configuration.HuntingLogQueue, count);
        ImGui.PopID();

        Styling.VSpace(16f);
        using var reveal = Motion.PushSwitch("##ahg_log_book", viewSlot, slide: ListSlide);
        DrawBook(configuration, controller);
    }

    private static void DrawHeader(bool scrollIntoView)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Layout.LibraryHeaderHeight * scale;
        var label = Loc.T(L.HuntingLog.Library);
        var labelSize = TextDraw.SectionTitleSize(label);
        TextDraw.SectionTitle(label, new Vector2(origin.X, origin.Y + (height - labelSize.Y) * 0.5f), Styling.TextStrong);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
        if (scrollIntoView)
        {
            ImGui.SetScrollHereY(0f);
        }
    }

    // The nine class logs, then the player's own company log; another company's log cannot advance.
    private static int CollectPicker()
    {
        var count = 0;
        var books = HuntingLogRegistry.Books;
        for (var index = 0; index < books.Length && count < pickerSlots.Length; index++)
        {
            if (books[index].Kind == HuntingLogKind.Class)
            {
                pickerSlots[count++] = books[index].Slot;
            }
        }

        var companySlot = HuntingLogReader.PlayerGrandCompanySlot();
        if (companySlot != HuntingLogRegistry.NoLog && count < pickerSlots.Length && HuntingLogRegistry.TryGetBook(companySlot, out _))
        {
            pickerSlots[count++] = companySlot;
        }

        return count;
    }

    private static int PickerIndex(byte slot, int count)
    {
        for (var index = 0; index < count; index++)
        {
            if (pickerSlots[index] == slot)
            {
                return index;
            }
        }

        return -1;
    }

    private static void EnsureViewSlot(List<byte> queue, int count, bool focusQueue)
    {
        if (focusQueue && queue.Count > 0 && PickerIndex(queue[0], count) >= 0)
        {
            Select(queue[0]);
            return;
        }

        if (PickerIndex(viewSlot, count) >= 0)
        {
            return;
        }

        var classSlot = HuntingLogReader.PlayerClassSlot();
        Select(PickerIndex(classSlot, count) >= 0 ? classSlot : pickerSlots[0]);
    }

    private static void Select(byte slot)
    {
        if (slot == viewSlot)
        {
            return;
        }

        viewSlot = slot;
        viewRank = FollowCurrentRank;
    }

    private static void DrawPicker(List<byte> queue, int count)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = Gap * scale;
        var avail = ImGui.GetContentRegionAvail().X;
        var columns = Math.Clamp((int)MathF.Floor((avail + gap) / (ChipMinWidth * scale + gap)), 1, count);
        var lines = (count + columns - 1) / columns;
        columns = (count + lines - 1) / lines;
        var chipWidth = (avail - gap * (columns - 1)) / columns;

        using var itemSpacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap));
        for (var index = 0; index < count; index++)
        {
            if (index % columns != 0)
            {
                ImGui.SameLine(0f, gap);
            }

            DrawChip(queue, pickerSlots[index], chipWidth);
        }
    }

    private static void DrawChip(List<byte> queue, byte slot, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(width, ChipHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var selected = slot == viewSlot;
        var state = HuntLauncher.StateOf(slot);
        var queueIndex = queue.IndexOf(slot);

        ImGui.PushID(slot);
        var hit = Hit.Area("##book", size);
        var hover = Motion.Hover(Motion.Key("##book"), hit.Hovered);
        var active = Motion.Approach(Motion.Key("##book", 1), selected ? 1f : 0f, 14f);
        ImGui.PopID();

        if (hit.Clicked)
        {
            Select(slot);
        }

        var drawList = ImGui.GetWindowDrawList();
        var accent = state == BookState.Complete ? Styling.AccentMint : Styling.AccentGlow;
        Paint.Glass(drawList, origin, end, Styling.CardRounding * scale, accent, 0.02f + 0.16f * active, hover);

        var padX = 12f * scale;
        var midY = origin.Y + size.Y * 0.5f;
        var rightX = end.X - padX;
        if (queueIndex >= 0)
        {
            rightX -= DrawQueueBadge(drawList, queueIndex, rightX, midY) + 8f * scale;
        }

        var lineHeight = ImGui.GetTextLineHeight();
        var textWidth = rightX - origin.X - padX;
        using (Fonts.PushCaption())
        {
            var captionHeight = ImGui.GetTextLineHeight();
            var top = midY - (lineHeight + 2f * scale + captionHeight) * 0.5f;
            TextDraw.At(TextDraw.Truncate(HuntLauncher.StatusText(slot, state), textWidth), new Vector2(origin.X + padX, top + lineHeight + 2f * scale),
                HuntLauncher.StatusColor(state));
            using (Fonts.PushBody())
            {
                var nameColor = Vector4.Lerp(Styling.TextSecondary, Styling.TextStrong, MathF.Max(active, hover));
                TextDraw.At(TextDraw.Truncate(HuntingLogRegistry.BookName(slot), textWidth), new Vector2(origin.X + padX, top), nameColor);
            }
        }

        if (!Hit.HoveringRect(origin, end))
        {
            return;
        }

        DrawChipTooltip(state, queueIndex);
    }

    private static float DrawQueueBadge(ImDrawListPtr drawList, int queueIndex, float rightX, float midY)
    {
        var radius = QueueBadgeRadius * ImGuiHelpers.GlobalScale;
        var center = new Vector2(rightX - radius, midY);
        drawList.AddCircleFilled(center, radius, Paint.Col(Styling.AccentGlow));
        using (Fonts.PushCaption())
        {
            var label = queueIndex < queueNumbers.Length ? queueNumbers[queueIndex] : string.Empty;
            TextDraw.Middle(label, center - new Vector2(radius, radius), center + new Vector2(radius, radius), Styling.ForegroundOn(Styling.AccentGlow));
        }

        return radius * 2f;
    }

    private static void DrawChipTooltip(BookState state, int queueIndex)
    {
        var help = state switch
        {
            BookState.NeedsGearset => Loc.T(L.HuntingLog.NeedsGearsetHelp),
            BookState.NotUnlocked => Loc.T(L.HuntingLog.NotUnlockedHelp),
            _ => string.Empty,
        };

        if (help.Length == 0 && queueIndex < 0)
        {
            return;
        }

        using (Tooltip.Begin())
        {
            if (queueIndex >= 0)
            {
                Tooltip.Text(queuePositionText.Get(queueIndex + 1, static key => Loc.T(L.HuntingLog.QueuePosition, (int)key)), Styling.TextStrong);
            }

            if (help.Length > 0)
            {
                Tooltip.Text(help, HuntLauncher.StatusColor(state));
            }
        }
    }

    private static void DrawBook(Configuration configuration, AutoHuntController controller)
    {
        if (!HuntingLogRegistry.TryGetBook(viewSlot, out var book))
        {
            return;
        }

        var status = HuntingLogReader.Status(viewSlot);
        var current = HuntingLogReader.CurrentRank(viewSlot);
        var shownRank = ShownRank(book, current);

        ImGui.PushID(viewSlot);
        DrawBookHeader(configuration, controller, book, status, current);
        Styling.VSpace(10f);
        DrawRankPills(book, status, current, shownRank);
        ImGui.PopID();

        Styling.VSpace(10f);
        using (Motion.PushSwitch("##ahg_log_rank", viewSlot * HuntingLogRegistry.MaxRanks + shownRank, slide: ListSlide))
        {
            DrawEntries(book.Slot, shownRank);
        }

        Styling.VSpace(12f);
        DrawFooter(book);
    }

    private static byte ShownRank(in HuntingLogBook book, byte current)
    {
        if (book.RankCount == 0)
        {
            return 0;
        }

        var last = (byte)(book.RankCount - 1);
        return Math.Min(viewRank == FollowCurrentRank ? current : viewRank, last);
    }

    private static void DrawBookHeader(Configuration configuration, AutoHuntController controller, in HuntingLogBook book, HuntingLogStatus status, byte current)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        float titleHeight;
        using (Fonts.PushHeadline())
        {
            titleHeight = ImGui.GetTextLineHeight();
        }

        float captionHeight;
        using (Fonts.PushCaption())
        {
            captionHeight = ImGui.GetTextLineHeight();
        }

        var height = titleHeight + 3f * scale + captionHeight;
        var midY = origin.Y + height * 0.5f;
        var queue = configuration.HuntingLogQueue;
        var queued = queue.Contains(book.Slot);

        var toggleSize = new Vector2(40f, 22f) * scale;
        var toggleX = origin.X + width - toggleSize.X;
        ImGui.SetCursorScreenPos(new Vector2(toggleX, midY - toggleSize.Y * 0.5f));
        if (ToggleSwitch.Draw("##ahg_log_queue", ref queued) && !controller.Running)
        {
            SetQueued(configuration, book.Slot, queued);
        }

        if (ImGui.IsItemHovered())
        {
            Tooltip.Show(Loc.T(L.HuntingLog.QueueToggleHelp));
        }

        var label = Loc.T(L.HuntingLog.QueueToggle);
        var labelSize = TextDraw.Measure(label);
        var labelX = toggleX - 10f * scale - labelSize.X;
        TextDraw.At(label, new Vector2(labelX, midY - labelSize.Y * 0.5f), Styling.TextSecondary);

        var textWidth = labelX - 16f * scale - origin.X;
        var state = HuntLauncher.StateOf(book.Slot);
        using (Fonts.PushHeadline())
        {
            TextDraw.At(TextDraw.Truncate(HuntingLogRegistry.BookName(book.Slot), textWidth), origin, Styling.TextStrong);
        }

        using (Fonts.PushCaption())
        {
            var caption = HeaderCaption(book, status, state, current);
            TextDraw.At(TextDraw.Truncate(caption, textWidth), new Vector2(origin.X, origin.Y + titleHeight + 3f * scale), HuntLauncher.StatusColor(state));
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    private static string HeaderCaption(in HuntingLogBook book, HuntingLogStatus status, BookState state, byte current)
    {
        if (status != HuntingLogStatus.InProgress)
        {
            return HuntLauncher.StatusText(book.Slot, state);
        }

        var (killed, needed) = HuntingLogReader.RankProgress(book.Slot, current);
        var key = (long)state << 56 | (long)book.Slot << 48 | (long)current << 40 | (long)(killed & KillsMask) << KillsShift | (needed & KillsMask);
        if (headerCaption.TryGet(key, out var text))
        {
            return text;
        }

        var rankLine = Loc.T(L.HuntingLog.RankOf, current + 1, (int)book.RankCount);
        var progress = Loc.T(L.HuntingLog.RankProgress, killed, needed);
        var caption = state == BookState.NeedsGearset
            ? string.Concat(rankLine, TextDraw.Separator, progress, TextDraw.Separator, Loc.T(L.HuntingLog.NeedsGearset))
            : string.Concat(rankLine, TextDraw.Separator, progress);
        return headerCaption.Set(key, caption);
    }

    private static void SetQueued(Configuration configuration, byte slot, bool queued)
    {
        var queue = configuration.HuntingLogQueue;
        var index = queue.IndexOf(slot);
        if (queued == index >= 0)
        {
            return;
        }

        if (queued)
        {
            queue.Add(slot);
        }
        else
        {
            queue.RemoveAt(index);
        }

        configuration.Save();
    }

    private static void DrawRankPills(in HuntingLogBook book, HuntingLogStatus status, byte current, byte shownRank)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail().X;
        var height = RankPillHeight * scale;
        var gap = Gap * scale;
        var x = origin.X;
        var y = origin.Y;

        for (byte rank = 0; rank < book.RankCount; rank++)
        {
            var state = RankStateOf(status, current, rank);
            var label = RankLabel(rank);
            var icon = state switch
            {
                RankState.Done => FontAwesomeIcon.Check,
                RankState.Locked => FontAwesomeIcon.Lock,
                _ => FontAwesomeIcon.Crosshairs,
            };

            var iconSize = TextDraw.IconSize(icon);
            var labelSize = TextDraw.Measure(label);
            var width = RankPillPadX * 2f * scale + iconSize.X + 6f * scale + labelSize.X;
            if (x > origin.X && x + width > origin.X + avail)
            {
                x = origin.X;
                y += height + gap;
            }

            ImGui.SetCursorScreenPos(new Vector2(x, y));
            ImGui.PushID(rank);
            var hit = Hit.Area("##rank", new Vector2(width, height));
            var hover = Motion.Hover(Motion.Key("##rank"), hit.Hovered);
            ImGui.PopID();

            if (hit.Clicked)
            {
                viewRank = rank == current ? FollowCurrentRank : rank;
            }

            DrawRankPill(new Vector2(x, y), new Vector2(width, height), state, rank == shownRank, hover, icon, iconSize, label, labelSize);
            if (hit.Hovered)
            {
                DrawRankTooltip(book.Slot, rank, label, state);
            }

            x += width + gap;
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(avail, y + height - origin.Y));
    }

    private static void DrawRankPill(Vector2 origin, Vector2 size, RankState state, bool shown, float hover, FontAwesomeIcon icon, Vector2 iconSize, string label, Vector2 labelSize)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var end = origin + size;
        var accent = state switch
        {
            RankState.Done => Styling.AccentMint,
            RankState.Current => Styling.AccentGlow,
            _ => Styling.Surface3,
        };

        Vector4 text;
        if (shown)
        {
            var fill = Vector4.Lerp(accent, Styling.Lighten(accent, 0.12f), hover);
            Paint.Pill(drawList, origin, end, fill, Styling.WithAlpha(Styling.Lighten(accent, 0.4f), 0.6f));
            text = Styling.ForegroundOn(fill);
        }
        else
        {
            var tint = state == RankState.Locked ? Styling.BorderDim : accent;
            Paint.Pill(drawList, origin, end, Styling.WithAlpha(tint, 0.10f + 0.12f * hover), Styling.WithAlpha(tint, 0.40f + 0.30f * hover));
            text = state == RankState.Locked ? Styling.TextMuted : Vector4.Lerp(Styling.Lighten(accent, 0.2f), Styling.TextStrong, hover * 0.5f);
        }

        var midY = origin.Y + size.Y * 0.5f;
        var x = origin.X + RankPillPadX * scale;
        TextDraw.Icon(icon, new Vector2(x, midY - iconSize.Y * 0.5f), text);
        TextDraw.At(label, new Vector2(x + iconSize.X + 6f * scale, midY - labelSize.Y * 0.5f), text);
    }

    private static void DrawRankTooltip(byte slot, byte rank, string label, RankState state)
    {
        using (Tooltip.Begin())
        {
            Tooltip.Text(label, Styling.TextStrong);
            Tooltip.Text(Loc.T(state switch
            {
                RankState.Done => L.HuntingLog.RankDone,
                RankState.Current => L.HuntingLog.RankCurrent,
                _ => L.HuntingLog.RankLocked,
            }), Styling.TextSecondary);

            var floor = HuntingLogRegistry.LevelFloor(slot, rank);
            if (floor > 0)
            {
                Tooltip.Text(levelFloorTexts[rank].Get(floor, static key => Loc.T(L.HuntingLog.RankLevelFloor, (int)key)), Styling.TextDim);
            }
        }
    }

    private static RankState RankStateOf(HuntingLogStatus status, byte current, byte rank) => status switch
    {
        HuntingLogStatus.Complete => RankState.Done,
        HuntingLogStatus.InProgress => rank < current ? RankState.Done : rank == current ? RankState.Current : RankState.Locked,
        _ => RankState.Locked,
    };

    private static string RankLabel(byte rank)
        => rank < rankLabels.Length ? rankLabels[rank].Get(rank, static key => Loc.T(L.HuntingLog.Rank, (int)key + 1)) : string.Empty;

    private static void DrawEntries(byte slot, byte rank)
    {
        EnsureRows(slot, rank);
        if (rowCount == 0)
        {
            EmptyHint(Loc.T(L.HuntingLog.NoTargets));
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var gap = Gap * scale;
        var avail = ImGui.GetContentRegionAvail().X;
        var columns = Math.Max(1, (int)MathF.Floor((avail + gap) / (EntryMinWidth * scale + gap)));
        var rowWidth = (avail - gap * (columns - 1)) / columns;

        using var itemSpacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap));
        for (var index = 0; index < rowCount; index++)
        {
            if (index % columns != 0)
            {
                ImGui.SameLine(0f, gap);
            }

            DrawEntryRow(index, slot, rank, rowWidth);
        }
    }

    // Names, zone lines and spawn coverage never change for a rank, so they are gathered once per shown rank.
    private static void EnsureRows(byte slot, byte rank)
    {
        var language = Loc.Current;
        if (slot == builtSlot && rank == builtRank && ReferenceEquals(language, builtLanguage))
        {
            return;
        }

        builtSlot = slot;
        builtRank = rank;
        builtLanguage = language;
        rowCount = 0;
        var entries = HuntingLogRegistry.Rank(slot, rank);
        for (var entryOffset = 0; entryOffset < entries.Length; entryOffset++)
        {
            var entry = entries[entryOffset];
            var targets = HuntingLogRegistry.Targets(entry);
            for (var targetOffset = 0; targetOffset < targets.Length && rowCount < rows.Length; targetOffset++)
            {
                var target = targets[targetOffset];
                var targetIndex = entry.FirstTarget + targetOffset;
                rows[rowCount++] = new EntryRow(entry.EntryIndex, target, HuntingLogRegistry.TargetName(targetIndex), ZoneLine(target),
                    HuntLauncher.Coverage(targetIndex, target));
            }
        }
    }

    private static string ZoneLine(in HuntingLogTarget target)
    {
        var zones = HuntingLogRegistry.Zones(target);
        var zone = zones.Length > 0 ? TerritoryNames.Of(zones[0]) : string.Empty;
        var subArea = HuntingLogRegistry.SubAreaName(target);
        if (zone.Length == 0)
        {
            return subArea;
        }

        return subArea.Length == 0 ? zone : Loc.T(L.HuntingLog.ZoneLine, zone, subArea);
    }

    private static void DrawEntryRow(int index, byte slot, byte rank, float width)
    {
        var row = rows[index];
        var target = row.Target;
        var killed = Math.Min(HuntingLogReader.Killed(slot, rank, row.EntryIndex, target.TargetSlot), target.Needed);
        var done = killed >= target.Needed;
        var huntable = HuntLauncher.IsHuntable(row.Coverage);

        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(width, EntryRowHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        var rounding = Styling.CardRounding * scale;
        var accent = done ? Styling.AccentMint : Styling.AccentGlow;
        Paint.Glass(drawList, origin, end, rounding, accent, done ? 0.06f : 0.02f);

        var padX = 13f * scale;
        var lineHeight = ImGui.GetTextLineHeight();
        var top = origin.Y + 9f * scale;
        var (icon, iconColor) = done ? (FontAwesomeIcon.Check, Styling.AccentMint)
            : row.Coverage == SpawnCoverage.InDuty ? (FontAwesomeIcon.DoorClosed, Styling.TextMuted)
            : row.Coverage == SpawnCoverage.NoData ? (FontAwesomeIcon.QuestionCircle, Styling.TextMuted)
            : (FontAwesomeIcon.Crosshairs, Styling.TextDim);
        var iconSize = TextDraw.IconSize(icon);
        TextDraw.Icon(icon, new Vector2(origin.X + padX, top + (lineHeight - iconSize.Y) * 0.5f), iconColor);
        var textX = origin.X + padX + TextDraw.IconSize(FontAwesomeIcon.Crosshairs).X + 10f * scale;
        var rightX = end.X - padX;

        var kills = rowKills[index].Get((long)killed << KillsShift | target.Needed,
            static key => Loc.T(L.Progress.Kills, (int)(key >> KillsShift), (int)(key & KillsMask)));
        float captionHeight;
        using (Fonts.PushCaption())
        {
            captionHeight = ImGui.GetTextLineHeight();
            var killsSize = TextDraw.Measure(kills);
            TextDraw.At(kills, new Vector2(rightX - killsSize.X, top + (lineHeight - killsSize.Y) * 0.5f), done ? Styling.AccentMint : Styling.TextSecondary);
            var nameColor = done ? Styling.TextSecondary : huntable ? Styling.TextStrong : Styling.TextDim;
            using (Fonts.PushBody())
            {
                TextDraw.At(TextDraw.Truncate(row.Name, rightX - killsSize.X - 12f * scale - textX), new Vector2(textX, top), nameColor);
            }
        }

        var captionY = top + lineHeight + 3f * scale;
        var zoneRight = rightX;
        var badge = BadgeFor(row.Coverage);
        if (badge.Length > 0)
        {
            zoneRight -= DrawBadge(drawList, badge, BadgeColor(row.Coverage), rightX, captionY + captionHeight * 0.5f) + 8f * scale;
        }

        using (Fonts.PushCaption())
        {
            TextDraw.At(TextDraw.Truncate(row.ZoneLine, zoneRight - textX), new Vector2(textX, captionY), Styling.TextDim);
        }

        var inset = rounding * 0.9f;
        var barHeight = ProgressBarHeight * scale;
        var fraction = target.Needed > 0 ? (float)killed / target.Needed : 0f;
        Paint.Bar(drawList, new Vector2(origin.X + inset, end.Y - barHeight - 4f * scale), size.X - inset * 2f, barHeight, fraction, accent);

        ImGui.Dummy(size);
        if (badge.Length > 0 && Hit.HoveringRect(origin, end))
        {
            Tooltip.Show(BadgeHelp(row.Coverage));
        }
    }

    private static string BadgeFor(SpawnCoverage coverage) => coverage switch
    {
        SpawnCoverage.InDuty => Loc.T(L.HuntingLog.BadgeInDuty),
        SpawnCoverage.AreaOnly => Loc.T(L.HuntingLog.BadgeAreaOnly),
        SpawnCoverage.NoData => Loc.T(L.HuntingLog.BadgeNoSpawns),
        _ => string.Empty,
    };

    private static Vector4 BadgeColor(SpawnCoverage coverage) => coverage switch
    {
        SpawnCoverage.InDuty => Styling.AccentAmber,
        SpawnCoverage.AreaOnly => Styling.AccentBlue,
        _ => Styling.AccentRose,
    };

    private static string BadgeHelp(SpawnCoverage coverage) => coverage switch
    {
        SpawnCoverage.InDuty => Loc.T(L.HuntingLog.InDutyHelp),
        SpawnCoverage.AreaOnly => Loc.T(L.HuntingLog.AreaOnlyHelp),
        _ => Loc.T(L.HuntingLog.NoSpawnsHelp),
    };

    private static float DrawBadge(ImDrawListPtr drawList, string label, Vector4 color, float rightX, float midY)
    {
        var scale = ImGuiHelpers.GlobalScale;
        using (Fonts.PushCaption())
        {
            var labelSize = TextDraw.Measure(label);
            var padX = BadgePadX * scale;
            var padY = 2f * scale;
            var min = new Vector2(rightX - labelSize.X - padX * 2f, midY - labelSize.Y * 0.5f - padY);
            var max = new Vector2(rightX, midY + labelSize.Y * 0.5f + padY);
            Paint.Pill(drawList, min, max, Styling.WithAlpha(color, 0.16f), Styling.WithAlpha(color, 0.50f));
            TextDraw.At(label, new Vector2(min.X + padX, midY - labelSize.Y * 0.5f), Styling.Lighten(color, 0.25f));
            return max.X - min.X;
        }
    }

    private static void DrawFooter(in HuntingLogBook book)
    {
        var name = AchievementName(book.Slot);
        if (name.Length == 0)
        {
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var size = new Vector2(width, FooterHeight * scale);
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        var status = AchievementReader.Status(book.AchievementId);
        var (statusText, statusColor, icon) = status switch
        {
            AchievementStatus.Complete => (Loc.T(L.HuntingLog.Earned), Styling.AccentMint, FontAwesomeIcon.Trophy),
            AchievementStatus.Incomplete => (Loc.T(L.HuntingLog.NotYet), Styling.AccentAmber, FontAwesomeIcon.HourglassHalf),
            _ => (Loc.T(L.HuntingLog.Unknown), Styling.TextMuted, FontAwesomeIcon.QuestionCircle),
        };

        Paint.Glass(drawList, origin, end, Styling.CardRounding * scale, status == AchievementStatus.Complete ? Styling.AccentMint : Styling.AccentGlow, 0.05f);

        var padX = 16f * scale;
        var midY = origin.Y + size.Y * 0.5f;
        var iconSize = TextDraw.IconSize(FontAwesomeIcon.Trophy);
        TextDraw.IconCentered(icon, new Vector2(origin.X + padX + iconSize.X * 0.5f, midY), statusColor);

        var rightX = end.X - padX;
        if (status == AchievementStatus.Unknown)
        {
            rightX -= DrawCheckButton(rightX, midY) + 12f * scale;
        }

        var textX = origin.X + padX + iconSize.X + 14f * scale;
        var lineHeight = ImGui.GetTextLineHeight();
        using (Fonts.PushCaption())
        {
            var captionHeight = ImGui.GetTextLineHeight();
            var top = midY - (lineHeight + 3f * scale + captionHeight) * 0.5f;
            TextDraw.At(TextDraw.Truncate(statusText, rightX - textX), new Vector2(textX, top + lineHeight + 3f * scale), statusColor);
            using (Fonts.PushBody())
            {
                var title = footerTitles[book.Slot].Get(book.Slot, static key => Loc.T(L.HuntingLog.Completes, AchievementName((byte)key)));
                TextDraw.At(TextDraw.Truncate(title, rightX - textX), new Vector2(textX, top), Styling.TextStrong);
            }
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    // The completion list is a server round trip; the reader rations the requests and this button only asks for one.
    private static float DrawCheckButton(float rightX, float midY)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var loadState = AchievementReader.LoadState();
        var requested = loadState == ClientAchievementState.Requested;
        if (loadState != ClientAchievementState.Invalid)
        {
            checkRefused = false;
        }

        var label = Loc.T(requested ? L.HuntingLog.Checking : L.HuntingLog.Check);
        var width = PillButton.Width(label, FontAwesomeIcon.Sync);
        ImGui.SetCursorScreenPos(new Vector2(rightX - width, midY - CheckButtonHeight * scale * 0.5f));
        var tooltip = Loc.T(checkRefused ? L.HuntingLog.CheckWait : L.HuntingLog.CheckHelp);
        if (PillButton.Draw("##ahg_log_check", label, Styling.AccentGlow, PillButton.Emphasis.Tinted, FontAwesomeIcon.Sync,
                enabled: loadState == ClientAchievementState.Invalid, height: CheckButtonHeight, tooltip: tooltip))
        {
            checkRefused = !AchievementReader.RequestLoad();
        }

        return width;
    }

    private static string AchievementName(byte slot)
    {
        if (slot >= achievementNames.Length)
        {
            return string.Empty;
        }

        if (achievementNames[slot] is { } cached)
        {
            return cached;
        }

        var achievementId = HuntingLogRegistry.TryGetBook(slot, out var book) ? book.AchievementId : 0;
        var name = achievementId == 0
            ? string.Empty
            : Svc.Data.GetExcelSheet<Achievement>().GetRowOrDefault(achievementId)?.Name.ExtractText() ?? string.Empty;
        achievementNames[slot] = name;
        return name;
    }

    private static void EmptyHint(string text)
    {
        var origin = ImGui.GetCursorScreenPos();
        TextDraw.At(text, origin, Styling.TextMuted);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight()));
    }

    private static string[] BuildQueueNumbers(int count)
    {
        var numbers = new string[count];
        for (var index = 0; index < count; index++)
        {
            numbers[index] = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return numbers;
    }
}
