using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// One piece of furniture or equipment. In build mode it describes the item and the room it is in;
/// in play mode it is a bed: who is in it, with a button to open their record.
/// </summary>
public partial class ItemPanel : PanelContainer
{
    private readonly Label _title = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly RichTextLabel _info = new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Button _action = new() { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38) };
    private HospitalSimulation _sim = null!;
    private double _refresh;

    /// <summary>Raised when the player wants the whole room (build mode).</summary>
    public event Action<CareSpace>? RoomRequested;

    /// <summary>Raised when the player wants the patient in the bed (play mode).</summary>
    public event Action<Patient>? PatientRequested;

    /// <summary>The room the item stands in.</summary>
    public CareSpace? Room { get; private set; }

    public PlacedFurniture? Item { get; private set; }

    /// <summary>True when showing a bed in play mode rather than an item in build mode.</summary>
    public bool AsBed { get; private set; }

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", HudStyle.DarkPanel());

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        var header = new HBoxContainer();
        _title.AddThemeFontSizeOverride("font_size", 18);
        _title.AddThemeColorOverride("font_color", UiStyle.TextDark);
        header.AddChild(_title);
        var close = new Button { Text = "✕", ThemeTypeVariation = UiStyle.Secondary, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(40, 40), TooltipText = "Close (Esc)" };
        close.Pressed += Close;
        header.AddChild(close);
        column.AddChild(header);

        _info.AddThemeColorOverride("default_color", UiStyle.TextDark);
        column.AddChild(_info);
        column.AddChild(_action);
        _action.Pressed += OnAction;
    }

    /// <summary>Shows a piece of furniture or equipment (build mode).</summary>
    public void ShowItem(CareSpace room, PlacedFurniture item) => Open(room, item, asBed: false);

    /// <summary>Shows a bed and who is in it (play mode).</summary>
    public void ShowBed(CareSpace room, PlacedFurniture item) => Open(room, item, asBed: true);

    public void Close()
    {
        Visible = false;
        Room = null;
        Item = null;
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        _refresh -= delta;
        if (_refresh <= 0)
        {
            Refresh();
        }
    }

    /// <summary>The patient in (or on their way to) a bed, if any.</summary>
    public static int? BedOccupant(CareSpace room, PlacedFurniture bed) =>
        room.IsWard ? room.Beds.FirstOrDefault(b => b.PatientSpot == bed.Tile)?.OccupantId : room.OccupantId;

    private void Open(CareSpace room, PlacedFurniture item, bool asBed)
    {
        Room = room;
        Item = item;
        AsBed = asBed;
        Visible = true;
        Refresh();
    }

    private void Refresh()
    {
        _refresh = 0.5;
        if (Room is null || Item is null || _sim.FindSpace(Room.Id) is not { } room || !room.Furniture.Contains(Item))
        {
            Close();
            return;
        }

        var def = _sim.Database.Furniture.GetValueOrDefault(Item.ItemId);
        var muted = "[color=#5B6B79]";
        if (AsBed)
        {
            var number = room.IsWard ? room.Beds.ToList().FindIndex(b => b.PatientSpot == Item.Tile) + 1 : 0;
            _title.Text = number > 0 ? $"Bed {number}" : "Bed";
            var patient = BedOccupant(room, Item) is { } id ? _sim.FindPatient(id) : null;
            var status = patient switch
            {
                null => "Empty",
                _ when patient.Tile == (room.IsWard ? Item.Tile : room.PatientSpot) && !patient.IsMoving => $"{patient.FullName} ({patient.Age}{(patient.Sex == Sex.Female ? "F" : "M")})",
                _ => $"Kept for {patient.FullName}, on their way",
            };
            _info.Text = $"{muted}In[/color]  {room.Name}\n{muted}Patient[/color]  {status}";
            _action.Text = "Open patient record";
            _action.Visible = patient is not null;
            return;
        }

        _title.Text = def?.Name ?? Item.ItemId;
        var kind = def?.IsEquipment == true ? "Medical equipment" : "Furniture";
        var size = def is null ? string.Empty : $"\n{muted}Size[/color]  {def.WidthM:0.0} m × {def.DepthM:0.0} m";
        var cost = def is null ? string.Empty : $"\n{muted}Value[/color]  £{def.Cost:N0}";
        _info.Text = $"{muted}{kind} in[/color]  {room.Name}{size}{cost}\n\n{muted}Placed with the room. Move or resize the room to lay it out again.[/color]";
        _action.Text = "Select the room";
        _action.Visible = true;
    }

    private void OnAction()
    {
        if (Room is not { } room || Item is not { } item)
        {
            return;
        }

        if (AsBed)
        {
            if (BedOccupant(room, item) is { } id && _sim.FindPatient(id) is { } patient)
            {
                PatientRequested?.Invoke(patient);
            }
        }
        else
        {
            RoomRequested?.Invoke(room);
        }
    }
}
