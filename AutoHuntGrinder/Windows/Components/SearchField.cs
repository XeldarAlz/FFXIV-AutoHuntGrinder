using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoHuntGrinder.Windows.Components;

internal static class SearchField
{
    public const int DefaultMaxLength = 64;

    private const float Height = 36f;
    private const float PadX = 12f;
    private const float IconGap = 8f;

    // The frame is painted before the input exists, so its focus glow follows the caller's flag from the previous frame.
    public static void Draw(string id, string hint, ref string text, ref bool focused, float width, int maxLength = DefaultMaxLength)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var height = Height * scale;
        var end = origin + new Vector2(width, height);
        var padX = PadX * scale;
        var drawList = ImGui.GetWindowDrawList();
        var rounding = Styling.FrameRounding * scale;
        var focus = Motion.Approach(Motion.Key(id, 1), focused ? 1f : 0f, 16f);

        Paint.Fill(drawList, origin, end, Styling.WithAlpha(Styling.Surface0, 0.9f), rounding);
        Paint.Stroke(drawList, origin, end,
            Vector4.Lerp(Styling.WithAlpha(Styling.BorderDim, 0.75f), Styling.WithAlpha(Styling.AccentGlowSoft, 0.85f), focus), rounding);

        var iconSize = TextDraw.IconSize(FontAwesomeIcon.Search);
        TextDraw.Icon(FontAwesomeIcon.Search, new Vector2(origin.X + padX, origin.Y + (height - iconSize.Y) * 0.5f),
            Vector4.Lerp(Styling.TextMuted, Styling.AccentGlowSoft, focus));

        var fieldX = origin.X + padX + iconSize.X + IconGap * scale;
        ImGui.SetCursorScreenPos(new Vector2(fieldX, origin.Y + (height - ImGui.GetFrameHeight()) * 0.5f));
        ImGui.SetNextItemWidth(end.X - fieldX - padX);
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Vector4.Zero)
            .Push(ImGuiCol.FrameBgHovered, Vector4.Zero)
            .Push(ImGuiCol.FrameBgActive, Vector4.Zero))
        {
            ImGui.InputTextWithHint(id, hint, ref text, maxLength);
        }

        focused = ImGui.IsItemActive();
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }
}
