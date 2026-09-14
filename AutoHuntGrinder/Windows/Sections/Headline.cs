using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Stats;
using AutoHuntGrinder.Core.Tasks;
using AutoHuntGrinder.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoHuntGrinder.Windows.Sections;

internal static class Headline
{
    private const float GreetingGap = 8f;
    private const float DetailGap = 6f;
    private const float RightGap = 18f;
    private const float PluginsButtonHeight = 30f;

    public static bool Draw(Configuration configuration, AutoHuntController controller, RunHistory history)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var info = ReadyState.Resolve(configuration, controller);
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var y = origin.Y;

        var (icon, color, greeting) = Greeting();
        using (Fonts.PushCaption())
        {
            var greetingSize = TextDraw.Measure(greeting);
            var iconSize = TextDraw.IconSize(icon);
            TextDraw.Icon(icon, new Vector2(origin.X + 1f * scale, y + (greetingSize.Y - iconSize.Y) * 0.5f), color);
            TextDraw.At(greeting, new Vector2(origin.X + iconSize.X + 8f * scale, y), Styling.TextDim);
            y += greetingSize.Y + GreetingGap * scale;
        }

        float titleHeight;
        using (Fonts.PushTitle())
        {
            titleHeight = ImGui.GetTextLineHeight();
        }

        var detailHeight = ImGui.GetTextLineHeight();
        var blockHeight = titleHeight + DetailGap * scale + detailHeight;
        var blockMidY = y + blockHeight * 0.5f;

        var rightWidth = DrawRightColumn(info, history, origin.X + width, blockMidY, out var openPlugins);
        var maxTextWidth = width - rightWidth - RightGap * scale;

        using (Fonts.PushTitle())
        {
            TextDraw.At(TextDraw.Truncate(info.Title + Loc.T(L.Hunt.SentenceEnd), maxTextWidth), new Vector2(origin.X, y), Styling.TextStrong);
        }

        y += titleHeight + DetailGap * scale;
        TextDraw.At(TextDraw.Truncate(info.Detail, maxTextWidth), new Vector2(origin.X, y), Styling.TextDim);
        y += detailHeight;

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, y - origin.Y));
        return openPlugins;
    }

    private static (FontAwesomeIcon Icon, Vector4 Color, string Greeting) Greeting() => DateTime.Now.Hour switch
    {
        >= 5 and < 12  => (FontAwesomeIcon.Sun,       Styling.AccentAmber,     Loc.T(L.Shell.GreetingMorning)),
        >= 12 and < 17 => (FontAwesomeIcon.Sun,       Styling.AccentAmber,     Loc.T(L.Shell.GreetingAfternoon)),
        >= 17 and < 22 => (FontAwesomeIcon.CloudMoon, Styling.AccentGlowSoft, Loc.T(L.Shell.GreetingEvening)),
        _              => (FontAwesomeIcon.Moon,      Styling.AccentBlue,      Loc.T(L.Shell.GreetingNight)),
    };

    private static float DrawRightColumn(ReadyState.Info info, RunHistory history, float rightX, float midY, out bool openPlugins)
    {
        var scale = ImGuiHelpers.GlobalScale;
        openPlugins = false;

        if (info.Kind == ReadyState.Kind.SetupNeeded)
        {
            var label = Loc.T(L.Hunt.OpenPlugins);
            var buttonWidth = PillButton.Width(label, FontAwesomeIcon.Plug);
            ImGui.SetCursorScreenPos(new Vector2(rightX - buttonWidth, midY - PluginsButtonHeight * scale * 0.5f));
            openPlugins = PillButton.Draw("##ahg_open_plugins", label, Styling.AccentRose, PillButton.Emphasis.Tinted, FontAwesomeIcon.Plug, height: PluginsButtonHeight);
            return buttonWidth;
        }

        var (title, detail) = LastRun(history);
        using (Fonts.PushCaption())
        {
            var titleSize = TextDraw.Measure(title);
            var detailSize = TextDraw.Measure(detail);
            var gap = 3f * scale;
            var top = midY - (titleSize.Y + gap + detailSize.Y) * 0.5f;
            TextDraw.At(title, new Vector2(rightX - titleSize.X, top), Styling.TextSecondary);
            TextDraw.At(detail, new Vector2(rightX - detailSize.X, top + titleSize.Y + gap), Styling.TextDim);
            return MathF.Max(titleSize.X, detailSize.X);
        }
    }

    // Only a bill run completes bills; the other modes count kills.
    private static (string Title, string Detail) LastRun(RunHistory history)
    {
        var records = history.Records;
        if (records.Count == 0)
        {
            return (Loc.T(L.Hunt.NoRunsYet), Loc.T(L.Hunt.StatsAppearHere));
        }

        var record = records[0];
        var elapsed = Formatting.Elapsed(record.Duration);
        return record.Mode == HuntMode.MarkBills
            ? (Loc.T(L.Hunt.LastRun, record.MarksKilled), Loc.T(L.Hunt.LastRunDetail, elapsed, record.BillsCompleted))
            : (Loc.T(L.Hunt.LastRunKills, record.MarksKilled), elapsed);
    }
}
