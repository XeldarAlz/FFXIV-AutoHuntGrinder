using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoHuntGrinder.Windows.Components;

internal static class GroupLabel
{
    public static void Draw(string label, float spaceBefore = 0f, float extraBelow = 0f)
    {
        if (spaceBefore > 0f)
        {
            Styling.VSpace(spaceBefore);
        }

        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var labelSize = TextDraw.SmallCapsSize(label);
        TextDraw.SmallCaps(label, new Vector2(origin.X + 2f * scale, origin.Y), Styling.TextMuted);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, labelSize.Y + extraBelow * scale));
    }
}
