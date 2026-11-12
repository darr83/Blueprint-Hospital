using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
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

    private readonly VBoxContainer _chairTools = new();
    private readonly HFlowContainer _swatches = new();

    /// <summary>The waiting chair shown, when the item is one the player placed rather than part of a room.</summary>
    private PlacedChair? Chair => Item is not null && Item.ItemId == HospitalSimulation.ChairItem ? _sim.ChairAt(Item.Tile) : null;

    private bool IsCorridorChair => Chair is not null;

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

        // A placed chair can be turned and recoloured in place.
        var turn = new Button { Text = "Turn ⟳", FocusMode = FocusModeEnum.None, ThemeTypeVariation = UiStyle.Secondary, CustomMinimumSize = new Vector2(0, 34), TooltipText = "Turn the chair a quarter (free)" };
        turn.Pressed += () => Restyle(c => (WaitingRoomLayout.Clockwise(c.Facing), c.Colour));
        _chairTools.AddThemeConstantOverride("separation", 8);
        _swatches.AddThemeConstantOverride("h_separation", 4);
        _swatches.AddThemeConstantOverride("v_separation", 4);
        _swatches.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        foreach (var (name, hex) in ChairColours.All)
        {
            var swatch = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(26, 26), TooltipText = $"{name} (free)" };
            var box = new StyleBoxFlat { BgColor = new Color(hex) };
            box.SetCornerRadiusAll(4);
            swatch.AddThemeStyleboxOverride("normal", box);
            var hover = (StyleBoxFlat)box.Duplicate();
            hover.BorderColor = Colors.White;
            hover.SetBorderWidthAll(2);
            swatch.AddThemeStyleboxOverride("hover", hover);
            swatch.AddThemeStyleboxOverride("pressed", hover);
            swatch.Pressed += () => Restyle(c => (c.Facing, hex));
            _swatches.AddChild(swatch);
        }

        _chairTools.AddChild(_swatches);
        _chairTools.AddChild(turn);
        column.AddChild(_chairTools);
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
        room.HasBeds ? room.Beds.FirstOrDefault(b => b.PatientSpot == bed.Tile)?.OccupantId : room.OccupantId;

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
        if (Room is null || Item is null || _sim.FindSpace(Room.Id) is not { } room || !(room.Furniture.Contains(Item) || IsCorridorChair))
        {
            Close();
            return;
        }

        var def = _sim.Database.Furniture.GetValueOrDefault(Item.ItemId);
        var muted = "[color=#5B6B79]";
        _chairTools.Visible = false;
        if (AsBed)
        {
            var number = room.HasBeds ? room.Beds.ToList().FindIndex(b => b.PatientSpot == Item.Tile) + 1 : 0;
            _title.Text = number > 0 ? $"Bed {number}" : "Bed";
            var patient = BedOccupant(room, Item) is { } id ? _sim.FindPatient(id) : null;
            var status = patient switch
            {
                null => "Empty",
                _ when patient.Tile == (room.HasBeds ? Item.Tile : room.PatientSpot) && !patient.IsMoving => $"{patient.FullName} ({patient.Age}{(patient.Sex == Sex.Female ? "F" : "M")})",
                _ => $"Kept for {patient.FullName}, on their way",
            };
            _info.Text = $"{muted}In[/color]  {room.Name}\n{muted}Patient[/color]  {status}";
            _action.Text = "Open patient record";
            _action.Visible = patient is not null;
            return;
        }

        _chairTools.Visible = Chair is not null;
        if (Chair is { } chair)
        {
            var tile = chair.Tile;
            var patient = _sim.Patients.FirstOrDefault(p => p.Tile == tile && !p.IsMoving && p.IsAlive);
            var outpatient = _sim.Outpatients.FirstOrDefault(o => o.Tile == tile && !o.IsMoving);
            var who = patient?.FullName ?? (outpatient is not null && _sim.FindAppointment(outpatient.AppointmentId) is { } a ? a.FullName : "Free");
            var where = room.Kind == CareSpaceKind.Corridor ? "patients waiting for a nearby room or clinic" : "patients waiting for triage";
            _title.Text = "Waiting chair";
            _info.Text = $"{muted}In[/color]  {room.Name}\n{muted}For[/color]  {where}\n{muted}Faces[/color]  {chair.Facing.ToString().ToLowerInvariant()} · {ChairColours.NameOf(chair.Colour)}\n{muted}Sitting here[/color]  {who}";
            _action.Text = $"Remove chair (refund £{_sim.ChairCost / 2:N0})";
            _action.Visible = true;
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

    /// <summary>Turns or recolours the chair shown, for free.</summary>
    private void Restyle(Func<PlacedChair, (WallSide Facing, string Colour)> change)
    {
        if (Chair is not { } chair)
        {
            return;
        }

        var (facing, colour) = change(chair);
        _sim.PlaceChair(chair.Tile, facing, colour);
        Refresh();
    }

    private void OnAction()
    {
        if (Room is not { } room || Item is not { } item)
        {
            return;
        }

        if (!AsBed && IsCorridorChair)
        {
            _sim.RemoveChair(item.Tile);
            Close();
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
