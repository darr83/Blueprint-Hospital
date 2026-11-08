using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// Details of one room: its name (which can be changed), size, capacity, furniture and who is
/// using it, with buttons to move, resize, turn or demolish it.
/// </summary>
public partial class RoomPanel : PanelContainer
{
    private readonly LineEdit _name = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly RichTextLabel _info = new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly RichTextLabel _problems = new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false };
    private readonly Button _move = new() { Text = "Move or resize", TooltipText = "Drag out the room again where you want it, at the size you want", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38) };
    private readonly Button _demolish = new() { Text = "Demolish", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38) };
    private HospitalSimulation _sim = null!;
    private string? _roomId;
    private double _refresh;

    /// <summary>Raised when the player wants to move or resize the room.</summary>
    public event Action<CareSpace>? MoveRequested;

    public string? RoomId => _roomId;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", HudStyle.DarkPanel());
        CustomMinimumSize = new Vector2(360, 0);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        var header = new HBoxContainer();
        _name.TooltipText = "Type a new name and press Enter";
        _name.TextSubmitted += text =>
        {
            if (_roomId is not null)
            {
                _sim.RenameRoom(_roomId, text);
            }

            _name.ReleaseFocus();
        };
        header.AddChild(_name);
        var close = new Button { Text = "✕", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(34, 32) };
        close.Pressed += Close;
        header.AddChild(close);
        column.AddChild(header);

        _info.AddThemeColorOverride("default_color", new Color("#E8EDEE"));
        column.AddChild(_info);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 6);
        buttons.AddChild(_move);
        buttons.AddChild(_demolish);
        column.AddChild(buttons);
        column.AddChild(_problems);

        _move.Pressed += () =>
        {
            if (_roomId is not null && _sim.FindSpace(_roomId) is { } room)
            {
                MoveRequested?.Invoke(room);
            }
        };
        _demolish.Pressed += Demolish;
    }

    public void Show(CareSpace room)
    {
        _roomId = room.Id;
        _name.Text = room.Name;
        _problems.Text = string.Empty;
        Visible = true;
        Refresh();
    }

    public void Close()
    {
        Visible = false;
        _roomId = null;
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

    private void Refresh()
    {
        _refresh = 0.5;
        if (_roomId is null || _sim.FindSpace(_roomId) is not { } room)
        {
            Close();
            return;
        }

        var template = _sim.Database.RoomTemplates.GetValueOrDefault(room.TemplateId);
        var text = template is null ? string.Empty : BuildPanel.Describe(template, room, _sim);
        text = text.Replace($"[color=#B8C4CE]Cost[/color]  £{room.BuildCost:N0}", $"[color=#B8C4CE]Value[/color]  £{room.BuildCost:N0}");
        var header = $"[color=#B8C4CE]{template?.Name ?? room.Kind.ToString()} · door faces {Facing(room.DoorSide)}[/color]";
        _info.Text = $"{header}\n{text}\n[color=#B8C4CE]In use[/color]  {Usage(room)}";

        var check = _sim.CheckDemolish(room.Id);
        _demolish.Text = $"Demolish (+£{-check.Cost:N0})";
        _move.Visible = room.Kind != CareSpaceKind.Corridor;
    }

    private string Usage(CareSpace room)
    {
        if (room.OccupantId is { } id && _sim.FindPatient(id) is { } patient)
        {
            return $"{patient.FullName} ({patient.Age}{(patient.Sex == Simulation.Patients.Sex.Female ? "F" : "M")})";
        }

        if (room.ReservedByStaffId is { } staffId && _sim.FindStaff(staffId) is { } member)
        {
            return $"{member.Name}";
        }

        var inside = _sim.Patients.Count(p => room.Rect.Contains(p.Tile) && !p.IsMoving) + _sim.Staff.Count(s => room.Rect.Contains(s.Tile));
        return room.Kind switch
        {
            CareSpaceKind.Corridor => "Corridor",
            _ when inside > 0 => $"{inside} {(inside == 1 ? "person" : "people")} here",
            _ when room.IsTreatmentSpace => "Free",
            _ => "Empty",
        };
    }

    private void Demolish()
    {
        if (_roomId is null)
        {
            return;
        }

        var result = _sim.DemolishRoom(_roomId);
        if (result.CanBuild)
        {
            Close();
            return;
        }

        _problems.Text = $"[color=#FFB4AE]✗ {string.Join("\n✗ ", result.Problems)}[/color]";
    }

    private static string Facing(WallSide side) => side switch
    {
        WallSide.North => "north-east",
        WallSide.East => "south-east",
        WallSide.South => "south-west",
        _ => "north-west",
    };
}
