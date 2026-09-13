using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Core.Tasks;
using AutoHuntGrinder.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoHuntGrinder.Windows.Sections;

internal static class PlanCard
{
    private const float PadX = 18f;
    private const float PadY = 16f;
    private const float TokenHeight = 30f;
    private const float TokenPadX = 11f;
    private const float ChevronGap = 6f;
    private const float WordGap = 8f;
    private const float LineGap = 10f;
    private const float PopoverWidth = 420f;
    private const float PopoverGap = 6f;
    private const float PopoverRevealMs = 220f;
    private const float PopoverSlide = 8f;

    private const string AfterPopup = "##ahg_after_popover";

    private static readonly AfterRunAction[] afterRunOrder =
        [AfterRunAction.StayLoggedIn, AfterRunAction.ReturnToInn, AfterRunAction.Logout, AfterRunAction.CloseGame];

    private static readonly (LocString Token, LocString Name, LocString Detail)[] afterRunChoices =
    [
        (L.Hunt.AfterStayToken, L.Hunt.AfterStayName, L.Hunt.AfterStayDetail),
        (L.Hunt.AfterInnToken, L.Hunt.AfterInnName, L.Hunt.AfterInnDetail),
        (L.Hunt.AfterLogoutToken, L.Hunt.AfterLogoutName, L.Hunt.AfterLogoutDetail),
        (L.Hunt.AfterCloseToken, L.Hunt.AfterCloseName, L.Hunt.AfterCloseDetail),
    ];

    private static readonly Piece[] pieces = new Piece[5];
    private static readonly Vector2 PopoverPadding = new(16f, 16f);
    private static Vector2 afterAnchor;
    private static long afterOpenedTick;

    private enum PieceKind { Word, Bills, After }

    private readonly record struct Piece(PieceKind Kind, string Text);

    public static bool Draw(Configuration configuration, AutoHuntController controller)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var padX = PadX * scale;
        var padY = PadY * scale;
        var drawList = ImGui.GetWindowDrawList();

        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        var y = origin.Y + padY;
        var planLabel = Loc.T(L.Hunt.Plan);
        var labelSize = TextDraw.SectionTitleSize(planLabel);
        TextDraw.SectionTitle(planLabel, new Vector2(origin.X + padX, y), Styling.TextStrong);
        y += labelSize.Y + 10f * scale;

        var focusBills = DrawSentence(configuration, controller, new Vector2(origin.X + padX, y), width - padX * 2f, out var sentenceBottom);
        y = sentenceBottom + 14f * scale;

        ImGui.SetCursorScreenPos(new Vector2(origin.X + padX, y));
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(6f, 6f) * scale))
        {
            ImGui.PushID("##ahg_plan_chips");
            ImGui.BeginGroup();
            BillStrip.Draw(configuration, controller, width - padX * 2f);
            ImGui.EndGroup();
            ImGui.PopID();
        }

        var end = new Vector2(origin.X + width, ImGui.GetItemRectMax().Y + padY);

        drawList.ChannelsSetCurrent(0);
        Paint.Glass(drawList, origin, end, Styling.PanelRounding * scale, Styling.AccentGlow, 0.07f, 0f, elevated: true);
        drawList.ChannelsMerge();

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, end.Y - origin.Y));

        DrawAfterPopover(configuration);
        return focusBills;
    }

    private static bool DrawSentence(Configuration configuration, AutoHuntController controller, Vector2 start, float maxWidth, out float bottom)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var tokenHeight = TokenHeight * scale;
        var wordGap = WordGap * scale;
        var billCount = BillSelection.CountSelected(configuration);
        var end = Loc.T(L.Hunt.SentenceEnd);

        var count = 0;
        pieces[count++] = new Piece(PieceKind.Word, Loc.T(L.Hunt.SentenceHunt));
        pieces[count++] = new Piece(PieceKind.Bills, billCount == 0 ? Loc.T(L.Hunt.BillsNone) : Loc.Plural(L.Hunt.BillsCount, billCount));
        pieces[count++] = new Piece(PieceKind.Word, Loc.T(L.Hunt.SentenceThen));
        pieces[count++] = new Piece(PieceKind.After, Loc.T(afterRunChoices[AfterIndex(configuration)].Token));
        if (end.Length > 0)
        {
            pieces[count++] = new Piece(PieceKind.Word, end);
        }

        var x = start.X;
        var y = start.Y;
        var focusBills = false;
        var editable = !controller.Running;

        for (var index = 0; index < count; index++)
        {
            var piece = pieces[index];
            var isPunctuation = ReferenceEquals(piece.Text, end);
            var pieceWidth = piece.Kind == PieceKind.Word ? TextDraw.Measure(piece.Text).X : TokenWidth(piece.Text);
            var gap = isPunctuation ? 0f : wordGap;
            if (x > start.X && x + pieceWidth > start.X + maxWidth)
            {
                x = start.X;
                y += tokenHeight + LineGap * scale;
            }

            if (piece.Kind == PieceKind.Word)
            {
                var textSize = TextDraw.Measure(piece.Text);
                TextDraw.At(piece.Text, new Vector2(x, y + (tokenHeight - textSize.Y) * 0.5f), Styling.TextSecondary);
                x += pieceWidth + gap;
                continue;
            }

            var accent = piece.Kind == PieceKind.Bills && billCount == 0 ? Styling.AccentAmber : Styling.AccentGlow;
            ImGui.SetCursorScreenPos(new Vector2(x, y));
            var clicked = DrawToken(TokenId(piece.Kind), piece.Text, accent, editable);
            if (piece.Kind == PieceKind.Bills)
            {
                focusBills |= clicked;
            }
            else
            {
                afterAnchor = new Vector2(x, y + tokenHeight + PopoverGap * scale);
                if (clicked)
                {
                    afterOpenedTick = OpenPopover(AfterPopup);
                }
            }

            x += pieceWidth + gap;
        }

        bottom = y + tokenHeight;
        return focusBills;
    }

    private static string TokenId(PieceKind kind) => kind == PieceKind.Bills ? "##ahg_token_bills" : "##ahg_token_after";

    private static float TokenWidth(string label)
    {
        var scale = ImGuiHelpers.GlobalScale;
        return TokenPadX * 2f * scale + TextDraw.Measure(label).X + ChevronGap * scale + TextDraw.IconSize(FontAwesomeIcon.ChevronDown).X;
    }

    private static bool DrawToken(string id, string label, Vector4 accent, bool enabled)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(TokenWidth(label), TokenHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var hit = Hit.Area(id, size, enabled);
        var hover = Motion.Hover(Motion.Key(id), hit.Hovered);
        var drawList = ImGui.GetWindowDrawList();

        var fill = enabled ? Styling.WithAlpha(accent, 0.16f + 0.12f * hover) : Styling.WithAlpha(Styling.Surface2, 0.8f);
        var border = enabled ? Styling.WithAlpha(accent, 0.45f + 0.30f * hover) : Styling.WithAlpha(Styling.BorderDim, 0.6f);
        var text = enabled ? Vector4.Lerp(Styling.Lighten(accent, 0.3f), Styling.TextStrong, hover * 0.5f) : Styling.TextDim;
        if (hit.Held)
        {
            fill = Styling.Darken(fill, 0.12f);
        }

        Paint.Pill(drawList, origin, end, fill, border);

        var midY = origin.Y + size.Y * 0.5f;
        var labelSize = TextDraw.Measure(label);
        TextDraw.At(label, new Vector2(origin.X + TokenPadX * scale, midY - labelSize.Y * 0.5f), text);

        var chevronSize = TextDraw.IconSize(FontAwesomeIcon.ChevronDown);
        TextDraw.Icon(FontAwesomeIcon.ChevronDown, new Vector2(end.X - TokenPadX * scale - chevronSize.X, midY - chevronSize.Y * 0.5f + 1f * scale),
            Styling.WithAlpha(text, 0.75f));

        if (!enabled && Hit.HoveringRect(origin, end))
        {
            Tooltip.Show(Loc.T(L.Hunt.PlanLocked));
        }

        return hit.Clicked;
    }

    private static int AfterIndex(Configuration configuration) => Math.Max(0, Array.IndexOf(afterRunOrder, configuration.AfterRun));

    private static Vector2 PopoverPosition(Vector2 anchor, float contentWidth)
    {
        var viewport = ImGui.GetMainViewport();
        var popupWidth = contentWidth + PopoverPadding.X * 2f * ImGuiHelpers.GlobalScale;
        var maxX = viewport.WorkPos.X + viewport.WorkSize.X - popupWidth;
        return anchor with { X = MathF.Max(viewport.WorkPos.X, MathF.Min(anchor.X, maxX)) };
    }

    private static long OpenPopover(string id)
    {
        ImGui.OpenPopup(id);
        return Environment.TickCount64;
    }

    private ref struct Popover
    {
        private ImRaii.StyleDisposable style;
        private ImRaii.PopupDisposable popup;

        public Popover(string id, Vector2 anchor, float width, long openedTick)
        {
            var scale = ImGuiHelpers.GlobalScale;
            var reveal = Motion.Reveal(openedTick, PopoverRevealMs);
            var position = PopoverPosition(anchor, width) - new Vector2(0f, (1f - reveal) * PopoverSlide * scale);
            ImGui.SetNextWindowPos(position, ImGuiCond.Always);
            style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, PopoverPadding * scale)
                .Push(ImGuiStyleVar.Alpha, MathF.Max(0.05f, reveal));
            popup = ImRaii.Popup(id, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove);
        }

        public bool Open => popup.Alive;

        public void Dispose()
        {
            popup.Dispose();
            style.Dispose();
        }
    }

    private static void DrawAfterPopover(Configuration configuration)
    {
        if (!ImGui.IsPopupOpen(AfterPopup))
        {
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var width = PopoverWidth * scale;
        using var popover = new Popover(AfterPopup, afterAnchor, width, afterOpenedTick);
        if (!popover.Open)
        {
            return;
        }

        var current = AfterIndex(configuration);
        var heading = Loc.T(L.Hunt.WhenDone);
        var labelOrigin = ImGui.GetCursorScreenPos();
        var labelSize = TextDraw.SectionTitleSize(heading);
        TextDraw.SectionTitle(heading, labelOrigin, Styling.TextStrong);
        ImGui.Dummy(new Vector2(width, labelSize.Y + 8f * scale));

        for (var index = 0; index < afterRunChoices.Length; index++)
        {
            if (!DrawChoiceRow(index, Loc.T(afterRunChoices[index].Name), Loc.T(afterRunChoices[index].Detail), index == current, width))
            {
                continue;
            }

            configuration.AfterRun = afterRunOrder[index];
            configuration.SaveDebounced();
            ImGui.CloseCurrentPopup();
        }
    }

    private static bool DrawChoiceRow(int index, string name, string detail, bool selected, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var padX = 10f * scale;
        var padY = 8f * scale;
        var lineHeight = ImGui.GetTextLineHeight();
        float detailHeight;
        using (Fonts.PushCaption())
        {
            detailHeight = TextDraw.Measure(detail).Y;
        }

        var size = new Vector2(width, padY * 2f + lineHeight + 2f * scale + detailHeight);
        var origin = ImGui.GetCursorScreenPos();

        ImGui.PushID((nint)(index + 1));
        var hit = Hit.Area("##choice", size);
        var hover = Motion.Hover(Motion.Key("##choice"), hit.Hovered);
        ImGui.PopID();

        var drawList = ImGui.GetWindowDrawList();
        var fill = selected ? Styling.WithAlpha(Styling.AccentGlow, 0.18f + 0.08f * hover) : Styling.WithAlpha(Styling.Surface2, 0.8f * hover);
        if (fill.W > 0.01f)
        {
            Paint.Fill(drawList, origin, origin + size, fill, 8f * scale);
        }

        var nameColor = selected ? Styling.AccentGlowSoft : Vector4.Lerp(Styling.TextSecondary, Styling.TextStrong, hover);
        TextDraw.At(name, new Vector2(origin.X + padX, origin.Y + padY), nameColor);
        using (Fonts.PushCaption())
        {
            TextDraw.At(detail, new Vector2(origin.X + padX, origin.Y + padY + lineHeight + 2f * scale), Styling.TextMuted);
        }

        if (selected)
        {
            var checkSize = TextDraw.IconSize(FontAwesomeIcon.Check);
            TextDraw.Icon(FontAwesomeIcon.Check, new Vector2(origin.X + size.X - padX - checkSize.X, origin.Y + padY + (lineHeight - checkSize.Y) * 0.5f), Styling.AccentGlowSoft);
        }

        return hit.Clicked;
    }
}
