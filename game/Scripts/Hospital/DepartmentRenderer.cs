using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Draws the emergency department's rooms: tinted floors, low cut-away walls with door gaps,
/// furniture (beds, chairs, desks) and room names. Occupancy is shown by tinting the bed.
/// </summary>
public partial class DepartmentRenderer : Node2D
{
    /// <summary>Interior walls are drawn low so the player can see into every room.</summary>
    public const float InteriorWallHeight = 22f;

    private static readonly Dictionary<CareSpaceKind, Color> FloorColours = new()
    {
        [CareSpaceKind.Resus] = new Color("#F3D6D3"),
        [CareSpaceKind.Majors] = new Color("#F4E7C6"),
        [CareSpaceKind.Minors] = new Color("#D8EBD3"),
        [CareSpaceKind.Triage] = new Color("#E2DCF0"),
        [CareSpaceKind.Waiting] = new Color("#D6E4F2"),
        [CareSpaceKind.NursesStation] = new Color("#DDE3E8"),
        [CareSpaceKind.DoctorsOffice] = new Color("#DDE3E8"),
        [CareSpaceKind.StaffRoom] = new Color("#EADFCF"),
        [CareSpaceKind.Corridor] = new Color("#E9E7E1"),
    };

    private HospitalSimulation _sim = null!;
    private Font _font = null!;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        _font = ThemeDB.FallbackFont;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        // Bed tints change with occupancy, so redraw a few times a second.
        _redrawTimer -= delta;
        if (_redrawTimer <= 0)
        {
            _redrawTimer = 0.25;
            QueueRedraw();
        }
    }

    private double _redrawTimer;

    public override void _Draw()
    {
        if (_sim is null)
        {
            return;
        }

        var spaces = _sim.Spaces.OrderBy(s => s.Kind == CareSpaceKind.Corridor ? 0 : 1).ToList();

        foreach (var space in spaces)
        {
            var r = space.Rect;
            IsoDraw.FloorQuad(this, r.X, r.Y, r.Width, r.Length, FloorColours[space.Kind]);
        }

        // Ambulance bay markings: one box per handover slot.
        foreach (var slot in _sim.AmbulanceBayPatientTiles)
        {
            IsoDraw.FloorOutline(this, slot.X + 3, slot.Y - 1, 12, 3.6, new Color(1, 1, 1, 0.7f), 2f);
        }

        // Furniture and walls back to front, so nearer objects overlap further ones.
        foreach (var space in spaces.OrderBy(s => s.Rect.X + s.Rect.Y))
        {
            DrawFurniture(space);
            if (space.HasWalls)
            {
                DrawWalls(space);
            }
        }

        foreach (var space in spaces.Where(s => s.Kind != CareSpaceKind.Corridor))
        {
            DrawRoomLabel(space);
        }
    }

    private void DrawWalls(CareSpace space)
    {
        var r = space.Rect;
        var back = new Color("#C9D2DA");
        var front = new Color("#A7B3BE");
        var cap = new Color("#5D6873");

        for (var x = r.X; x < r.Right; x++)
        {
            if (!space.IsDoor(new GridCoord(x, r.Y), WallSide.North))
            {
                Wall(IsoDraw.ToScreen(x, r.Y), IsoDraw.ToScreen(x + 1, r.Y), back, cap);
            }
        }

        for (var y = r.Y; y < r.Bottom; y++)
        {
            if (!space.IsDoor(new GridCoord(r.X, y), WallSide.West))
            {
                Wall(IsoDraw.ToScreen(r.X, y), IsoDraw.ToScreen(r.X, y + 1), back.Darkened(0.08f), cap);
            }
        }

        for (var x = r.X; x < r.Right; x++)
        {
            if (!space.IsDoor(new GridCoord(x, r.Bottom - 1), WallSide.South))
            {
                Wall(IsoDraw.ToScreen(x, r.Bottom), IsoDraw.ToScreen(x + 1, r.Bottom), front, cap);
            }
        }

        for (var y = r.Y; y < r.Bottom; y++)
        {
            if (!space.IsDoor(new GridCoord(r.Right - 1, y), WallSide.East))
            {
                Wall(IsoDraw.ToScreen(r.Right, y), IsoDraw.ToScreen(r.Right, y + 1), front.Darkened(0.08f), cap);
            }
        }
    }

    private void Wall(Vector2 a, Vector2 b, Color face, Color cap)
    {
        var up = new Vector2(0, -InteriorWallHeight);
        DrawColoredPolygon(new[] { a, b, b + up, a + up }, face);
        DrawLine(a + up, b + up, cap, 2f);
    }

    private void DrawFurniture(CareSpace space)
    {
        switch (space.Kind)
        {
            case CareSpaceKind.Resus:
            case CareSpaceKind.Majors:
            case CareSpaceKind.Minors:
                DrawTreatmentBay(space);
                break;

            case CareSpaceKind.Triage:
                Chair(space.PatientSpot, new Color("#7E6BB5"));
                Chair(space.StaffSpot, new Color("#56606B"));
                IsoDraw.Box(this, space.StaffSpot.X - 1.2, space.StaffSpot.Y - 1, 1, 2, 22, new Color("#E8E8E8"));
                Monitor(space.PatientSpot.X + 1.4, space.PatientSpot.Y - 0.6);
                break;

            case CareSpaceKind.Waiting:
                foreach (var seat in space.Seats)
                {
                    Chair(seat, new Color("#4F7FB0"));
                }

                // Reception desk between the receptionist and the queue.
                IsoDraw.Box(this, space.StaffSpot.X - 2, space.StaffSpot.Y + 1, 5, 0.8, 30, new Color("#8C6D4F"));
                IsoDraw.Box(this, space.StaffSpot.X - 2, space.StaffSpot.Y + 1.5, 5, 0.3, 32, new Color("#D9CBB8"));
                break;

            case CareSpaceKind.NursesStation:
                IsoDraw.Box(this, space.Rect.X + 1.5, space.Rect.Y + 4.1, space.Rect.Width - 3, 0.9, 28, new Color("#8C6D4F"));
                foreach (var seat in space.Seats)
                {
                    Chair(seat, new Color("#56606B"));
                }

                break;

            case CareSpaceKind.DoctorsOffice:
                IsoDraw.Box(this, space.Rect.X + 1.6, space.Rect.Y + 4.1, space.Rect.Width - 3.2, 1.6, 22, new Color("#A9865F"));
                foreach (var seat in space.Seats)
                {
                    Chair(seat, new Color("#56606B"));
                }

                break;

            case CareSpaceKind.StaffRoom:
                IsoDraw.Box(this, space.Rect.X + 1.8, space.Rect.Y + 3.1, 4.4, 1.6, 20, new Color("#A9865F"));
                foreach (var seat in space.Seats)
                {
                    Chair(seat, new Color("#C0703A"));
                }

                IsoDraw.Box(this, space.Rect.Right - 1.2, space.Rect.Y + 0.2, 1, 3, 26, new Color("#E8E8E8"));
                break;
        }
    }

    private void DrawTreatmentBay(CareSpace space)
    {
        var spot = space.PatientSpot;
        var occupied = space.OccupantId is { } id && _sim.FindPatient(id) is { } p && p.Tile == spot;

        // Trolley/bed: about 0.8 m by 2 m, head against the back wall.
        var frame = new Color("#9AA5AF");
        IsoDraw.Box(this, spot.X - 0.3, spot.Y - 2.2, 1.6, 4.2, 14, frame);
        IsoDraw.Box(this, spot.X - 0.3, spot.Y - 2.2, 1.6, 4.2, 4, occupied ? new Color("#FFFFFF") : new Color("#E9F0F5"), lift: 14);
        IsoDraw.Box(this, spot.X - 0.2, spot.Y - 2.1, 1.4, 0.9, 4, new Color("#FFFFFF"), lift: 18);

        Monitor(spot.X + 1.6, spot.Y - 2.4);
        if (space.Kind == CareSpaceKind.Resus)
        {
            // Resuscitation trolley and a second monitor.
            IsoDraw.Box(this, spot.X - 2.4, spot.Y - 2.6, 1.2, 1.6, 26, new Color("#D84B3E"));
            Monitor(spot.X - 1.6, spot.Y + 1.4);
        }
    }

    private void Chair(GridCoord tile, Color colour)
    {
        IsoDraw.Box(this, tile.X + 0.15, tile.Y + 0.15, 0.7, 0.7, 12, colour);
    }

    private void Monitor(double x, double y)
    {
        IsoDraw.Box(this, x, y, 0.5, 0.5, 30, new Color("#5A6570"));
        IsoDraw.Box(this, x - 0.1, y - 0.1, 0.7, 0.4, 12, new Color("#1E2A33"), lift: 30);
    }

    private void DrawRoomLabel(CareSpace space)
    {
        var r = space.Rect;
        var position = IsoDraw.ToScreen(r.X + (r.Width / 2.0), r.Y + (r.Length / 2.0));
        if (space.Kind is not CareSpaceKind.Waiting)
        {
            // Along the front edge of the room, clear of the furniture.
            position = IsoDraw.ToScreen(r.X + (r.Width / 2.0), r.Bottom - 0.9);
        }
        else
        {
            position = IsoDraw.ToScreen(r.X + 15, r.Y + 11);
        }

        const int size = 22;
        var text = space.Name.ToUpperInvariant();
        var width = _font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        var origin = position - new Vector2(width / 2, -size / 2f);
        DrawString(_font, origin + new Vector2(1, 1), text, HorizontalAlignment.Left, -1, size, new Color(1, 1, 1, 0.8f));
        DrawString(_font, origin, text, HorizontalAlignment.Left, -1, size, new Color("#3A4A57"));
    }
}
