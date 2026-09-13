using AutoHuntGrinder.Core.External;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoHuntGrinder.Windows.Pages;

internal sealed class PluginsPage
{
    private const float PadX = 16f;
    private const float DiscRadius = 17f;
    private const float InstallButtonHeight = 30f;

    public void Draw()
    {
        var plugins = ExternalPlugins.All;
        var missing = 0;
        for (var index = 0; index < plugins.Count; index++)
        {
            if (ExternalPlugins.Catalog[plugins[index]].Required && !ExternalPlugins.IsInstalled(plugins[index]))
            {
                missing++;
            }
        }

        var status = missing == 0 ? Loc.T(L.Plugins.AllInstalled) : Loc.Plural(L.Plugins.Missing, missing);
        PageHeader.Draw(Loc.T(L.Plugins.Title), status, missing == 0 ? Styling.AccentMint : Styling.AccentRose);

        for (var index = 0; index < plugins.Count; index++)
        {
            DrawCard(plugins[index]);
            Styling.VSpace(4f);
        }

        Styling.VSpace(6f);
        using (Fonts.PushCaption())
        {
            var origin = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var footer = Loc.T(L.Plugins.Footer);
            TextDraw.Wrapped(footer, origin, width, Styling.TextMuted);
            ImGui.Dummy(new Vector2(width, TextDraw.MeasureWrapped(footer, width).Y));
        }
    }

    private static string Purpose(ExternalPlugin plugin) => plugin switch
    {
        ExternalPlugin.Vnavmesh => Loc.T(L.Plugins.PurposeVnavmesh),
        ExternalPlugin.BossMod  => Loc.T(L.Plugins.PurposeBossMod),
        _                       => ExternalPlugins.Catalog[plugin].Purpose,
    };

    private static void DrawCard(ExternalPlugin plugin)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var info = ExternalPlugins.Catalog[plugin];
        var installed = ExternalPlugins.IsInstalled(plugin);
        var installing = PluginInstaller.IsInstalling(plugin);

        var (icon, accent) = (installed, info.Required) switch
        {
            (true, _)     => (FontAwesomeIcon.CheckCircle, Styling.AccentMint),
            (false, true) => (FontAwesomeIcon.TimesCircle, Styling.AccentRose),
            _             => (FontAwesomeIcon.Circle, Styling.TextDim),
        };

        var size = new Vector2(ImGui.GetContentRegionAvail().X, Layout.PluginCardHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        Paint.Glass(drawList, origin, end, Styling.CardRounding * scale, accent, 0.06f);

        var padX = PadX * scale;
        var midY = origin.Y + size.Y * 0.5f;
        var discRadius = DiscRadius * scale;
        var discCenter = new Vector2(origin.X + padX + discRadius, midY);
        ProgressRing.Disc(discCenter, discRadius, Styling.Tint(Styling.Surface1, accent, 0.3f));
        ProgressRing.Track(discCenter, discRadius, 1.2f * scale, Styling.WithAlpha(accent, 0.7f));
        ProgressRing.CenterIcon(discCenter, icon, accent, discRadius * 0.95f);

        var rightWidth = DrawAction(plugin, installed, installing, end, midY);

        var purpose = Purpose(plugin);
        var textX = discCenter.X + discRadius + 16f * scale;
        var maxTextWidth = end.X - padX - rightWidth - textX;
        float nameHeight;
        using (Fonts.PushHeadline())
        {
            nameHeight = TextDraw.Measure(info.DisplayName).Y;
        }

        float purposeHeight;
        using (Fonts.PushCaption())
        {
            purposeHeight = TextDraw.Measure(purpose).Y;
        }

        var top = midY - (nameHeight + 3f * scale + purposeHeight) * 0.5f;

        Vector2 nameSize;
        using (Fonts.PushHeadline())
        {
            nameSize = TextDraw.Measure(info.DisplayName);
            TextDraw.At(info.DisplayName, new Vector2(textX, top), Styling.TextStrong);
        }

        DrawRequirementTag(drawList, info.Required, textX + nameSize.X + 10f * scale, top + nameHeight * 0.5f);

        using (Fonts.PushCaption())
        {
            TextDraw.At(TextDraw.Truncate(purpose, maxTextWidth), new Vector2(textX, top + nameHeight + 3f * scale), Styling.TextDim);
        }

        var nameMin = new Vector2(textX, top);
        if (Hit.HoveringRect(nameMin, nameMin + nameSize))
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            Tooltip.Show(Loc.T(L.Plugins.RepoHint, info.RepoUrl));
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                UrlActions.OpenInBrowser(info.RepoUrl);
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                ImGui.SetClipboardText(info.RepoUrl);
            }
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    private static void DrawRequirementTag(ImDrawListPtr drawList, bool required, float x, float midY)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var label = Loc.Upper(required ? Loc.T(L.Plugins.Required) : Loc.T(L.Plugins.Optional));
        using (Fonts.PushCaption())
        {
            var labelSize = TextDraw.Measure(label);
            var tagMin = new Vector2(x, midY - labelSize.Y * 0.5f - 3f * scale);
            var tagMax = tagMin + labelSize + new Vector2(14f * scale, 6f * scale);
            var accent = required ? Styling.AccentGlow : Styling.TextDim;
            Paint.Pill(drawList, tagMin, tagMax, Styling.WithAlpha(accent, 0.18f), Styling.WithAlpha(accent, 0.45f));
            TextDraw.Middle(label, tagMin, tagMax, required ? Styling.AccentGlowSoft : Styling.TextSecondary);
        }
    }

    private static float DrawAction(ExternalPlugin plugin, bool installed, bool installing, Vector2 end, float midY)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var padX = PadX * scale;

        if (installed)
        {
            var label = Loc.T(L.Plugins.Installed);
            var labelSize = TextDraw.Measure(label);
            var iconSize = TextDraw.IconSize(FontAwesomeIcon.Check);
            var labelX = end.X - padX - labelSize.X;
            TextDraw.At(label, new Vector2(labelX, midY - labelSize.Y * 0.5f), Styling.AccentMint);
            var iconX = labelX - 6f * scale - iconSize.X;
            TextDraw.Icon(FontAwesomeIcon.Check, new Vector2(iconX, midY - iconSize.Y * 0.5f), Styling.AccentMint);
            return end.X - padX - iconX + 12f * scale;
        }

        var text = installing ? Loc.T(L.Plugins.Installing) : Loc.T(L.Plugins.Install);
        var width = PillButton.Width(text, FontAwesomeIcon.Download);
        ImGui.SetCursorScreenPos(new Vector2(end.X - padX - width, midY - InstallButtonHeight * scale * 0.5f));
        ImGui.PushID((nint)((int)plugin + 1));
        if (PillButton.Draw("##install", text, Styling.AccentGlow, PillButton.Emphasis.Filled, FontAwesomeIcon.Download, enabled: !installing, height: InstallButtonHeight))
        {
            _ = PluginInstaller.Install(plugin);
        }

        ImGui.PopID();
        return width + 12f * scale;
    }
}
