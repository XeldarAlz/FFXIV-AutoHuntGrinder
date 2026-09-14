using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoHuntGrinder.Windows.Components;

internal static class Badge
{
    private const float PadX = 7f;
    private const float PadY = 2f;

    public static float Draw(ImDrawListPtr drawList, string label, Vector4 color, float rightX, float midY)
    {
        var scale = ImGuiHelpers.GlobalScale;
        using (Fonts.PushCaption())
        {
            var labelSize = TextDraw.Measure(label);
            var padX = PadX * scale;
            var padY = PadY * scale;
            var badgeStart = new Vector2(rightX - labelSize.X - padX * 2f, midY - labelSize.Y * 0.5f - padY);
            var badgeEnd = new Vector2(rightX, midY + labelSize.Y * 0.5f + padY);
            Paint.Pill(drawList, badgeStart, badgeEnd, Styling.WithAlpha(color, 0.16f), Styling.WithAlpha(color, 0.50f));
            TextDraw.At(label, new Vector2(badgeStart.X + padX, midY - labelSize.Y * 0.5f), Styling.Lighten(color, 0.25f));
            return badgeEnd.X - badgeStart.X;
        }
    }
}
