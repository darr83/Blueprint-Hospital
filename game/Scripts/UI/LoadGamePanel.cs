using System;
using System.Collections.Generic;
using BlueprintHospital.Core;
using BlueprintHospital.Simulation.Core;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// Lists saved games and loads or deletes the chosen one. The ready-built example hospital is always first in the
/// list; it can be loaded but not deleted.
/// </summary>
public partial class LoadGamePanel : ModalPanel
{
    private readonly ItemList _list = new()
    {
        CustomMinimumSize = new Vector2(520, 260),
        SizeFlagsVertical = SizeFlags.ExpandFill,
    };

    private readonly Label _status = UiStyle.Body(string.Empty, muted: true);
    private readonly Button _load;
    private readonly Button _delete;
    private IReadOnlyList<SaveSlotInfo> _slots = Array.Empty<SaveSlotInfo>();

    /// <summary>The first row of the list: the example hospital that ships with the game.</summary>
    public const string ExampleRowText = "Example hospital  —  3 floors, every department, fully staffed (ready-made)";

    public LoadGamePanel()
    {
        Content.AddChild(UiStyle.Heading("Load Game"));
        Content.AddChild(_list);
        Content.AddChild(_status);

        _load = UiStyle.Button("Load", LoadSelected, 120);
        _delete = UiStyle.SecondaryButton("Delete", DeleteSelected, 120);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        buttons.AddChild(_delete);
        buttons.AddChild(UiStyle.SecondaryButton("Back", Close, 120));
        buttons.AddChild(_load);
        Content.AddChild(buttons);

        _list.ItemSelected += _ => UpdateButtons();
        _list.ItemActivated += _ => LoadSelected();
        Refresh();
    }

    private void Refresh()
    {
        _slots = SaveManager.ListSlots();
        _list.Clear();
        _list.AddItem(ExampleRowText);
        _list.SetItemTooltip(0, "A finished hospital to explore: A&E, wards, ICU and HDU, theatres, maternity, clinics, scans, pharmacy, café and more over three floors, with every post filled.");
        foreach (var slot in _slots)
        {
            var saved = slot.SavedAtUtc.ToLocalTime();
            _list.AddItem($"{slot.HospitalName}  —  {slot.GameTime:ddd d MMM yyyy HH:mm}   (saved {saved:d MMM HH:mm})");
        }

        _status.Text = _slots.Count == 0 ? "No saved games yet. Save from the pause menu (Esc) while playing." : string.Empty;
        _list.Select(_slots.Count > 0 ? 1 : 0);

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var hasSelection = _list.GetSelectedItems().Length > 0;
        _load.Disabled = !hasSelection;
        _delete.Disabled = Selected() is null;
    }

    private SaveSlotInfo? Selected()
    {
        var selected = _list.GetSelectedItems();
        return selected.Length == 0 || selected[0] == 0 ? null : _slots[selected[0] - 1];
    }

    private void LoadSelected()
    {
        var selected = _list.GetSelectedItems();
        if (selected.Length == 0)
        {
            return;
        }

        try
        {
            if (selected[0] == 0)
            {
                GameManager.Instance.LoadExampleHospital();
                return;
            }

            GameManager.Instance.LoadGame(_slots[selected[0] - 1].SlotName);
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not load this save: {ex.Message}";
        }
    }

    private void DeleteSelected()
    {
        if (Selected() is not { } slot)
        {
            return;
        }

        if (!SaveManager.Delete(slot.SlotName))
        {
            _status.Text = "Could not delete this save.";
            return;
        }

        Refresh();
    }
}
