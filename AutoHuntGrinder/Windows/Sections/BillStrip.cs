using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Tasks;
using AutoHuntGrinder.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoHuntGrinder.Windows.Sections;

internal static class BillStrip
{
    private const float Gap = 8f;
    private const float PadX = 11f;
    private const float InnerGap = 6f;

    private readonly record struct ChipMetrics(
        float Total, float BodyWidth, string Tag, float TagWidth, string Name, float NameWidth,
        FontAwesomeIcon StatusIcon, string StatusText, float StatusWidth, Vector4 StatusColor);

    public static void Draw(Configuration configuration, AutoHuntController controller, float maxWidth)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var regionStart = ImGui.GetCursorScreenPos();
        if (BillSelection.CountSelected(configuration) == 0)
        {
            using (Fonts.PushCaption())
            {
                var hint = Loc.T(L.Hunt.PlanHint);
                TextDraw.At(hint, new Vector2(regionStart.X + 2f * scale, regionStart.Y), Styling.TextMuted);
                ImGui.Dummy(new Vector2(maxWidth, TextDraw.Measure(hint).Y));
            }

            return;
        }

        var running = controller.Running;
        var height = Layout.ChipHeight * scale;
        var gap = Gap * scale;
        var x = regionStart.X;
        var y = regionStart.Y;
        var bottom = y + height;
        byte? remove = null;

        var bills = HuntRegistry.Bills;
        for (var index = 0; index < bills.Length; index++)
        {
            var bill = bills[index];
            if (!configuration.SelectedBills.Contains(bill.MarkIndex))
            {
                continue;
            }

            var metrics = Measure(bill);
            if (x > regionStart.X && x + metrics.Total > regionStart.X + maxWidth)
            {
                x = regionStart.X;
                y += height + gap;
            }

            if (DrawChip(new Vector2(x, y), height, metrics, bill, running))
            {
                remove = bill.MarkIndex;
            }

            x += metrics.Total + gap;
            bottom = MathF.Max(bottom, y + height);
        }

        ImGui.SetCursorScreenPos(regionStart);
        ImGui.Dummy(new Vector2(maxWidth, bottom - regionStart.Y));

        if (remove is not { } markIndex)
        {
            return;
        }

        configuration.SelectedBills.Remove(markIndex);
        configuration.SaveDebounced();
    }

    private static ChipMetrics Measure(HuntBill bill)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var padX = PadX * scale;
        var gap = InnerGap * scale;
        var tag = Abbreviation(bill.Expansion);
        var (icon, text, color) = StatusVisual(bill.MarkIndex);
        float tagWidth;
        using (Fonts.PushCaption())
        {
            tagWidth = TextDraw.Measure(tag).X;
        }

        var nameWidth = TextDraw.Measure(bill.Name).X;
        var statusWidth = TextDraw.IconSize(icon).X + (text.Length > 0 ? 4f * scale + TextDraw.Measure(text).X : 0f);
        var bodyWidth = padX + tagWidth + gap + nameWidth + gap + statusWidth + gap;
        var closeWidth = TextDraw.IconSize(FontAwesomeIcon.Times).X + gap * 2f;
        return new ChipMetrics(bodyWidth + closeWidth, bodyWidth, tag, tagWidth, bill.Name, nameWidth, icon, text, statusWidth, color);
    }

    private static bool DrawChip(Vector2 origin, float height, ChipMetrics metrics, HuntBill bill, bool running)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var end = origin + new Vector2(metrics.Total, height);

        ImGui.SetCursorScreenPos(new Vector2(origin.X + metrics.BodyWidth, origin.Y));
        var closeClicked = ImGui.InvisibleButton($"##ahg_chip_close_{bill.MarkIndex}", new Vector2(metrics.Total - metrics.BodyWidth, height));
        var closeHovered = ImGui.IsItemHovered();
        if (!running && closeHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var hover = Motion.Hover(Motion.Key("##ahg_chip", (uint)bill.MarkIndex), !running && Hit.HoveringRect(origin, end));
        var accent = Styling.AccentGlow;
        var rounding = height * 0.5f;
        var tint = running ? 0.06f : 0.24f + 0.14f * hover;
        Paint.Gradient(drawList, origin, end, Styling.Tint(Styling.Surface2, accent, tint), Styling.Tint(Styling.Surface1, accent, tint * 0.8f), rounding);
        Paint.TopLight(drawList, origin, end, rounding, 0.09f);
        Paint.Stroke(drawList, origin, end, running ? Styling.WithAlpha(Styling.BorderDim, 0.6f) : Styling.WithAlpha(accent, 0.45f + 0.35f * hover), rounding);

        var midY = origin.Y + height * 0.5f;
        var cursorX = origin.X + PadX * scale;
        var gap = InnerGap * scale;

        using (Fonts.PushCaption())
        {
            var tagSize = TextDraw.Measure(metrics.Tag);
            TextDraw.At(metrics.Tag, new Vector2(cursorX, midY - tagSize.Y * 0.5f), running ? Styling.TextMuted : Styling.TextDim);
        }

        cursorX += metrics.TagWidth + gap;
        var nameSize = TextDraw.Measure(metrics.Name);
        TextDraw.At(metrics.Name, new Vector2(cursorX, midY - nameSize.Y * 0.5f), running ? Styling.TextDim : Styling.TextStrong);
        cursorX += metrics.NameWidth + gap;

        var iconSize = TextDraw.IconSize(metrics.StatusIcon);
        TextDraw.Icon(metrics.StatusIcon, new Vector2(cursorX, midY - iconSize.Y * 0.5f), metrics.StatusColor);
        if (metrics.StatusText.Length > 0)
        {
            var statusSize = TextDraw.Measure(metrics.StatusText);
            TextDraw.At(metrics.StatusText, new Vector2(cursorX + iconSize.X + 4f * scale, midY - statusSize.Y * 0.5f), metrics.StatusColor);
        }

        var closeColor = running ? Styling.TextMuted : closeHovered ? Styling.AccentRose : Styling.TextDim;
        var closeSize = TextDraw.IconSize(FontAwesomeIcon.Times);
        TextDraw.Icon(FontAwesomeIcon.Times, new Vector2(origin.X + metrics.BodyWidth + gap, midY - closeSize.Y * 0.5f), closeColor);

        if (!running && closeHovered)
        {
            Tooltip.Show(Loc.T(L.Hunt.RemoveFromPlan));
        }

        return !running && closeClicked;
    }

    private static (FontAwesomeIcon Icon, string Text, Vector4 Color) StatusVisual(byte markIndex)
    {
        switch (MarkBillReader.Status(markIndex))
        {
            case BillStatus.Held:
                var (killed, needed) = MarkBillReader.Kills(markIndex);
                return (FontAwesomeIcon.Crosshairs, string.Concat(killed.ToString(Loc.Culture), "/", needed.ToString(Loc.Culture)), Styling.AccentGlowSoft);
            case BillStatus.Stale:
                return (FontAwesomeIcon.History, string.Empty, Styling.AccentAmber);
            case BillStatus.Done:
                return (FontAwesomeIcon.Check, string.Empty, Styling.AccentMint);
            case BillStatus.Available:
                return (FontAwesomeIcon.PlusCircle, string.Empty, Styling.TextDim);
            default:
                return (FontAwesomeIcon.Lock, string.Empty, Styling.TextMuted);
        }
    }

    private static string Abbreviation(ExpansionKind expansion) => expansion switch
    {
        ExpansionKind.ARR => "ARR",
        ExpansionKind.HW  => "HW",
        ExpansionKind.SB  => "SB",
        ExpansionKind.ShB => "ShB",
        ExpansionKind.EW  => "EW",
        _                 => "DT",
    };
}
