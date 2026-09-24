using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoHuntGrinder.Windows.Components;

internal static class SearchField
{
    public const int DefaultMaxLength = 64;
    public const float DefaultHeight = 36f;

    private const float PadX = 12f;
    private const float IconGap = 8f;
    private const float ClearButtonSize = 22f;

    // The frame is painted before the input exists, so its focus glow follows the caller's flag from the previous frame.
    public static bool Draw(string id, string hint, ref string text, ref bool focused, float width, float height = DefaultHeight,
        bool requestFocus = false, int maxLength = DefaultMaxLength)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var scaledHeight = height * scale;
        var end = origin + new Vector2(width, scaledHeight);
        var padX = PadX * scale;
        var drawList = ImGui.GetWindowDrawList();
        var rounding = Styling.FrameRounding * scale;
        var focus = Motion.Approach(Motion.Key(id, 1), focused ? 1f : 0f, 16f);

        Paint.Fill(drawList, origin, end, Styling.WithAlpha(Styling.Surface0, 0.9f), rounding);
        Paint.Stroke(drawList, origin, end,
            Vector4.Lerp(Styling.WithAlpha(Styling.BorderDim, 0.75f), Styling.WithAlpha(Styling.AccentGlowSoft, 0.85f), focus), rounding);

        var iconSize = TextDraw.IconSize(FontAwesomeIcon.Search);
        TextDraw.Icon(FontAwesomeIcon.Search, new Vector2(origin.X + padX, origin.Y + (scaledHeight - iconSize.Y) * 0.5f),
            Vector4.Lerp(Styling.TextMuted, Styling.AccentGlowSoft, focus));

        var clearSize = ClearButtonSize * scale;
        var hasText = text.Length > 0;
        var fieldX = origin.X + padX + iconSize.X + IconGap * scale;
        var fieldRight = end.X - padX - (hasText ? clearSize : 0f);
        ImGui.SetCursorScreenPos(new Vector2(fieldX, origin.Y + (scaledHeight - ImGui.GetFrameHeight()) * 0.5f));
        ImGui.SetNextItemWidth(MathF.Max(1f, fieldRight - fieldX));
        if (requestFocus)
        {
            ImGui.SetKeyboardFocusHere();
        }

        bool changed;
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Vector4.Zero)
            .Push(ImGuiCol.FrameBgHovered, Vector4.Zero)
            .Push(ImGuiCol.FrameBgActive, Vector4.Zero))
        {
            changed = ImGui.InputTextWithHint(id, hint, ref text, maxLength);
        }

        focused = ImGui.IsItemActive();
        if (hasText)
        {
            ImGui.SetCursorScreenPos(new Vector2(end.X - padX * 0.5f - clearSize, origin.Y + (scaledHeight - clearSize) * 0.5f));
            if (IconButton.Draw(FontAwesomeIcon.Times, string.Concat(id, "_clear"), clearSize, Styling.TextDim))
            {
                text = string.Empty;
                changed = true;
            }
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, scaledHeight));
        return changed;
    }
}
