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

    /// <summary>Raised when the player wants to pick up a placed decoration and put it somewhere else.</summary>
    public event Action<int>? MoveDecorationRequested;

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

    private readonly VBoxContainer _decorTools = new();
    private readonly HFlowContainer _decorSwatches = new();

    /// <summary>The placed decoration shown, if the item is one.</summary>
    private PlacedDecoration? Decoration => Item is not null && Hospital.HospitalScene.DecorationId(Item) is { } id ? _sim.FindDecoration(id) : null;

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
        UiStyle.Strong(_title, UiStyle.SizeTitle);
        _title.AddThemeColorOverride("font_color", UiStyle.TextDark);
        header.AddChild(_title);
        var close = UiStyle.CloseButton(Close);
        header.AddChild(close);
        column.AddChild(header);

        _info.AddThemeColorOverride("default_color", UiStyle.TextDark);
        column.AddChild(_info);

        // A placed chair can be turned and recoloured in place.
        var turn = new Button { Text = "Turn ⟳", FocusMode = FocusModeEnum.None, ThemeTypeVariation = UiStyle.Secondary, CustomMinimumSize = new Vector2(0, 34), TooltipText = "Turn the chair a quarter (free)" };
        turn.Pressed += () => Restyle(c => (WaitingRoomLayout.Clockwise(c.Facing), c.Colour, c.Style));
        var styles = new HFlowContainer();
        styles.AddThemeConstantOverride("h_separation", 4);
        styles.AddThemeConstantOverride("v_separation", 4);
        foreach (var (name, style) in ChairStyles.All)
        {
            var button = new Button { Text = name, FocusMode = FocusModeEnum.None, ThemeTypeVariation = UiStyle.Secondary, CustomMinimumSize = new Vector2(0, 30), TooltipText = $"Change to a {name.ToLowerInvariant()} chair (free)" };
            button.Pressed += () => Restyle(c => (c.Facing, c.Colour, style));
            styles.AddChild(button);
        }

        _chairTools.AddChild(styles);
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
            swatch.Pressed += () => Restyle(c => (c.Facing, hex, c.Style));
            _swatches.AddChild(swatch);
        }

        _chairTools.AddChild(_swatches);
        _chairTools.AddChild(turn);
        column.AddChild(_chairTools);

        // A placed decoration can be turned, recoloured and moved for free.
        _decorTools.AddThemeConstantOverride("separation", 8);
        _decorSwatches.AddThemeConstantOverride("h_separation", 4);
        _decorSwatches.AddThemeConstantOverride("v_separation", 4);
        foreach (var (name, hex) in DecorItems.Colours)
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
            swatch.Pressed += () =>
            {
                if (Decoration is { } d)
                {
                    _sim.RecolourDecoration(d.Id, hex);
                    Refresh();
                }
            };
            _decorSwatches.AddChild(swatch);
        }

        _decorTools.AddChild(_decorSwatches);
        var decorButtons = new HBoxContainer();
        decorButtons.AddThemeConstantOverride("separation", 6);
        var decorTurn = new Button { Text = "Turn ⟳", FocusMode = FocusModeEnum.None, ThemeTypeVariation = UiStyle.Secondary, CustomMinimumSize = new Vector2(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "Turn it a quarter where it stands (free)" };
        decorTurn.Pressed += () =>
        {
            if (Decoration is { } d && _sim.NextDecorationFacing(d.Id) is { } facing)
            {
                _sim.MoveDecoration(d.Id, d.Tile, facing);
                Refresh();
            }
        };
        decorButtons.AddChild(decorTurn);
        var decorMove = new Button { Text = "Move", FocusMode = FocusModeEnum.None, ThemeTypeVariation = UiStyle.Secondary, CustomMinimumSize = new Vector2(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "Pick it up and click where it should go (free)" };
        decorMove.Pressed += () =>
        {
            if (Decoration is { } d)
            {
                Close();
                MoveDecorationRequested?.Invoke(d.Id);
            }
        };
        decorButtons.AddChild(decorMove);
        _decorTools.AddChild(decorButtons);
        column.AddChild(_decorTools);
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

    /// <summary>How old a baby is, in hours or days.</summary>
    private string BabyAge(Baby baby)
    {
        var hours = baby.HoursOld(_sim.Now);
        return hours < 48 ? $"{Math.Max(0, (int)hours)} h" : $"{(int)(hours / 24)} days";
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
        RefreshText();
        _info.Text = UiStyle.Facts(_info.Text);
    }

    private void RefreshText()
    {
        _refresh = 0.5;
        if (Room is null || Item is null || _sim.FindSpace(Room.Id) is not { } room || !(room.Furniture.Contains(Item) || IsCorridorChair || Decoration is not null))
        {
            Close();
            return;
        }

        var def = _sim.Database.Furniture.GetValueOrDefault(Item.ItemId);
        var muted = "[color=#5B6B79]";
        _chairTools.Visible = false;
        _decorTools.Visible = false;
        if (AsBed)
        {
            if (Item.ItemId == "incubator")
            {
                var index = HospitalSimulation.IncubatorIndex(room, Item);
                _title.Text = index >= 0 ? $"Incubator {index + 1}" : "Incubator";
                var baby = _sim.BabyInIncubator(room, index);
                var mum = baby is null ? null : _sim.FindPatient(baby.MotherId);
                var booked = baby is null && _sim.BabiesIn(room).Any(b => b.CotIndex == index) ? "Kept for a baby on the way" : "Empty";
                _info.Text = baby is null
                    ? $"{muted}In[/color]  {room.Name}\n{muted}Baby[/color]  {booked}"
                    : $"{muted}In[/color]  {room.Name}\n{muted}Baby[/color]  {baby.FullName} ({(baby.Sex == Sex.Female ? "girl" : "boy")}, {baby.WeightKg:0.00} kg)\n{muted}Born[/color]  {baby.GestationWeeks} weeks, {BabyAge(baby)} old\n{muted}Why[/color]  {baby.NeonatalReason ?? "Needs a closer eye"}\n{muted}Mum[/color]  {mum?.FullName ?? "gone home"}";
                _action.Text = "Open mum's record";
                _action.Visible = mum is not null;
                return;
            }

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

        if (Decoration is { Item: { } decor } placed)
        {
            _decorTools.Visible = true;
            _decorSwatches.Visible = decor.Tintable;
            _title.Text = decor.Name;
            var colour = decor.Tintable ? $" · {ChairColours.NameOf(DecorItems.TintOf(decor, placed.Colour))}" : string.Empty;
            var lines = $"{muted}In[/color]  {room.Name}\n{muted}Faces[/color]  {placed.Facing.ToString().ToLowerInvariant()}{colour}";
            if (decor.SeatSpecs.Count > 0)
            {
                var seats = DecorItems.SeatsOf(placed).Select(s => s.Tile).ToHashSet();
                var sitting = _sim.Patients.Count(p => seats.Contains(p.Tile) && !p.IsMoving && p.IsAlive) + _sim.Outpatients.Count(o => seats.Contains(o.Tile) && !o.IsMoving);
                lines += $"\n{muted}Seats[/color]  {decor.SeatSpecs.Count}, {sitting} in use";
            }

            if (decor.Comfort)
            {
                lines += $"\n{muted}Comfort[/color]  cheers up patients waiting nearby";
            }

            _info.Text = lines;
            _action.Text = $"Sell (refund £{decor.Cost / 2:N0})";
            _action.Visible = true;
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
            _info.Text = $"{muted}In[/color]  {room.Name}\n{muted}For[/color]  {where}\n{muted}Faces[/color]  {chair.Facing.ToString().ToLowerInvariant()} · {ChairStyles.NameOf(chair.Style)} · {ChairColours.NameOf(chair.Colour)}\n{muted}Sitting here[/color]  {who}";
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
    private void Restyle(Func<PlacedChair, (WallSide Facing, string Colour, string? Style)> change)
    {
        if (Chair is not { } chair)
        {
            return;
        }

        var (facing, colour, style) = change(chair);
        _sim.PlaceChair(chair.Tile, facing, colour, style);
        Refresh();
    }

    private void OnAction()
    {
        if (Room is not { } room || Item is not { } item)
        {
            return;
        }

        if (!AsBed && Decoration is { } decoration)
        {
            _sim.RemoveDecoration(decoration.Id);
            Close();
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
            if (item.ItemId == "incubator")
            {
                if (_sim.BabyInIncubator(room, item) is { } baby && _sim.FindPatient(baby.MotherId) is { } mum)
                {
                    PatientRequested?.Invoke(mum);
                }
            }
            else if (BedOccupant(room, item) is { } id && _sim.FindPatient(id) is { } patient)
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
