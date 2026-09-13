using AutoHuntGrinder.Core.Game.Ops;
using AutoHuntGrinder.Core.Localization;
using AutoHuntGrinder.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoHuntGrinder.Windows.Sections.Config;

internal static class ConsumableSettings
{
    private const int RefreshMinutesMax = 29;
    private const float PickerWidth = 340f;
    private const float AddButtonWidth = 96f;
    private const float BuffGap = 12f;
    private const string ItemListId = "##ahg_consumables_items";
    // Bag contents change slowly next to the frame rate, so the picker reads them about once a second.
    private const long PickerRefreshMs = 1_000;

    private static readonly List<ConsumableEntry> pickerItems = [];
    private static string[] pickerLabels = [];
    private static long pickerRefreshAtMs;
    private static int pickerSelection;

    private static LanguageInfo? buffLabelLanguage;
    private static string wellFedMissing = string.Empty;
    private static string medicatedMissing = string.Empty;

    public static void Draw(Configuration configuration)
    {
        DrawConsumableGroup(configuration);
        using var items = Motion.PushSection("##ahg_consumables_items_section", configuration.AutoConsume);
        if (items is null)
        {
            return;
        }

        DrawItemsGroup(configuration);
    }

    private static void DrawConsumableGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Safety.ConsumablesGroup));

        SettingsRow.Draw(Loc.T(L.Safety.AutoConsume),
            Loc.T(L.Safety.AutoConsumeHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.AutoConsume, value => configuration.AutoConsume = value, "##ahg_consumables_on"),
            SettingsRow.ToggleHeight);

        using var body = Motion.PushSwitch("##ahg_consumables_body", configuration.AutoConsume);
        if (!configuration.AutoConsume)
        {
            SettingsRow.Note(Loc.T(L.Safety.AutoConsumeOff));
            return;
        }

        var format = configuration.AutoConsumeMinMinutes == 0 ? Loc.T(L.Safety.RefreshWornOff) : Loc.T(L.Safety.RefreshFormat);
        SettingsRow.Draw(Loc.T(L.Safety.RefreshUnder),
            Loc.T(L.Safety.RefreshUnderHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(configuration, "##ahg_consumables_minutes",
                () => configuration.AutoConsumeMinMinutes, value => configuration.AutoConsumeMinMinutes = value,
                0, RefreshMinutesMax, format));
    }

    private static void DrawItemsGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Safety.ConsumablesItems));

        SettingsRow.DrawBlock(Loc.T(L.Safety.AddItem),
            Loc.T(L.Safety.AddItemHelp),
            () => DrawAddRow(configuration));

        SettingsRow.DrawBlock(Loc.T(L.Safety.ActiveItems),
            Loc.T(L.Safety.ActiveItemsHelp),
            () => DrawItemList(configuration));
    }

    // Only what is in the bag right now: a session uses one or two items, so the whole game list is noise.
    private static void DrawAddRow(Configuration configuration)
    {
        RefreshPicker(configuration);
        if (pickerItems.Count == 0)
        {
            SettingsRow.Note(Loc.T(L.Safety.NoneInBag));
            return;
        }

        pickerSelection = Math.Clamp(pickerSelection, 0, pickerItems.Count - 1);
        SettingsControls.DrawSearchableCombo("##ahg_consumables_picker", pickerLabels, ref pickerSelection, PickerWidth);

        var picked = pickerItems[pickerSelection];
        var duplicate = IsQueued(configuration, picked.ItemId);

        ImGui.SameLine();
        var buttonSize = new Vector2(AddButtonWidth * ImGuiHelpers.GlobalScale, ImGui.GetFrameHeight());
        bool clicked;
        ImGui.PushID("##ahg_consumables_add");
        using (ImRaii.Disabled(duplicate))
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
        {
            clicked = ImGui.Button(Loc.T(L.Safety.Add), buttonSize);
        }

        ImGui.PopID();

        if (clicked)
        {
            configuration.AutoConsumeItems.Add(new ConsumableEntry
            {
                ItemId = picked.ItemId,
                Name = picked.Name,
                StatusId = picked.StatusId,
                CanBeHq = picked.CanBeHq,
            });
            configuration.SaveDebounced();
            pickerRefreshAtMs = 0;
        }

        if (!duplicate)
        {
            return;
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
        {
            ImGui.TextUnformatted(Loc.T(L.Safety.AlreadyAdded));
        }
    }

    private static void DrawItemList(Configuration configuration)
    {
        var items = configuration.AutoConsumeItems;
        if (items.Count == 0)
        {
            SettingsRow.Note(Loc.T(L.Safety.NoItemsAdded));
            return;
        }

        RefreshBuffLabels();
        var gap = BuffGap * ImGuiHelpers.GlobalScale;
        var removeIndex = -1;
        for (var index = 0; index < items.Count; index++)
        {
            var entry = items[index];
            ListRows.Ordinal(index);
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
            {
                ImGui.TextUnformatted(entry.Name);
            }

            ImGui.SameLine(0f, gap);
            var inBag = FoodOps.IsAvailable(entry);
            using (ImRaii.PushColor(ImGuiCol.Text, inBag ? Styling.TextMuted : Styling.AccentRose))
            {
                ImGui.TextUnformatted(inBag ? BuffName(entry.StatusId) : MissingLabel(entry.StatusId));
            }

            if (ListRows.RemoveButton(ItemListId, index, Loc.T(L.Safety.Remove)))
            {
                removeIndex = index;
            }
        }

        if (removeIndex < 0)
        {
            return;
        }

        items.RemoveAt(removeIndex);
        configuration.SaveDebounced();
        pickerRefreshAtMs = 0;
    }

    private static void RefreshPicker(Configuration configuration)
    {
        var now = Environment.TickCount64;
        if (now < pickerRefreshAtMs)
        {
            return;
        }

        pickerRefreshAtMs = now + PickerRefreshMs;
        FoodOps.FillAvailable(pickerItems);
        if (pickerLabels.Length != pickerItems.Count)
        {
            pickerLabels = new string[pickerItems.Count];
        }

        var addedSuffix = string.Concat("  ", Loc.T(L.Safety.Added));
        for (var index = 0; index < pickerItems.Count; index++)
        {
            var entry = pickerItems[index];
            var kind = entry.StatusId == FoodOps.WellFedStatusId ? Loc.T(L.Safety.KindFood) : Loc.T(L.Safety.KindMedicine);
            var suffix = IsQueued(configuration, entry.ItemId) ? addedSuffix : string.Empty;
            pickerLabels[index] = Loc.T(L.Safety.ItemLabel, entry.Name, kind, suffix);
        }
    }

    private static bool IsQueued(Configuration configuration, uint itemId)
    {
        var items = configuration.AutoConsumeItems;
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].ItemId == itemId)
            {
                return true;
            }
        }

        return false;
    }

    private static string BuffName(uint statusId)
        => statusId == FoodOps.WellFedStatusId ? Loc.T(L.Safety.WellFed) : Loc.T(L.Safety.Medicated);

    private static string MissingLabel(uint statusId)
        => statusId == FoodOps.WellFedStatusId ? wellFedMissing : medicatedMissing;

    // Formatted once per language instead of on every frame.
    private static void RefreshBuffLabels()
    {
        if (ReferenceEquals(buffLabelLanguage, Loc.Current))
        {
            return;
        }

        buffLabelLanguage = Loc.Current;
        wellFedMissing = Loc.T(L.Safety.NoneInBagShort, Loc.T(L.Safety.WellFed));
        medicatedMissing = Loc.T(L.Safety.NoneInBagShort, Loc.T(L.Safety.Medicated));
    }
}
