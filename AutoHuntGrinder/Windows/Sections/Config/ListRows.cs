using AutoHuntGrinder.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AutoHuntGrinder.Windows.Sections.Config;

internal static class ListRows
{
    private static string[] ordinals = [];

    // Built once per position, so a list drawn every frame does not format its numbers again.
    public static void Ordinal(int index)
    {
        if (index >= ordinals.Length)
        {
            GrowOrdinals(index + 1);
        }

        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
        {
            ImGui.TextUnformatted(ordinals[index]);
        }
    }

    // Right-aligned to the card edge, and scoped per list and row so no two buttons share an id.
    public static bool RemoveButton(string listId, int index, string tooltip)
    {
        var size = ImGui.GetFrameHeight();
        ImGui.SameLine(SettingsGroup.InnerRightLocalX() - size);
        ImGui.PushID(listId);
        ImGui.PushID(index);
        var clicked = IconButton.Draw(FontAwesomeIcon.Times, "##remove", size, Styling.AccentRose, tooltip);
        ImGui.PopID();
        ImGui.PopID();
        return clicked;
    }

    private static void GrowOrdinals(int count)
    {
        var grown = new string[Math.Max(count, ordinals.Length * 2)];
        Array.Copy(ordinals, grown, ordinals.Length);
        for (var index = ordinals.Length; index < grown.Length; index++)
        {
            grown[index] = $"{index + 1}.";
        }

        ordinals = grown;
    }
}
