using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// The department's floors: a tint per kind of room, the ambulance bay markings and coloured
/// coloured wayfinding bands outside clinical rooms.
/// Everything that stands up from the floor is drawn by <see cref="WorldLayer"/>.
/// </summary>
public partial class DepartmentRenderer : Node2D
{
    public static readonly Dictionary<CareSpaceKind, Color> FloorColours = new()
    {
        [CareSpaceKind.Resus] = new Color("#F3D6D3"),
        [CareSpaceKind.Majors] = new Color("#F4E7C6"),
        [CareSpaceKind.Minors] = new Color("#D8EBD3"),
        [CareSpaceKind.Triage] = new Color("#E2DCF0"),
        [CareSpaceKind.Waiting] = new Color("#D6E4F2"),
        [CareSpaceKind.NursesStation] = new Color("#DDE3E8"),
        [CareSpaceKind.DoctorsOffice] = new Color("#DDE3E8"),
        [CareSpaceKind.StaffRoom] = new Color("#EADFCF"),
        [CareSpaceKind.Corridor] = new Color("#ECEAE5"),
        [CareSpaceKind.Ward] = new Color("#DCE8F0"),
        [CareSpaceKind.XRay] = new Color("#DFE3E8"),
        [CareSpaceKind.CT] = new Color("#DFE3E8"),
        [CareSpaceKind.Theatre] = new Color("#D2E9E5"),
        [CareSpaceKind.Pharmacy] = new Color("#E0EDDC"),
        [CareSpaceKind.Clinic] = new Color("#E3E9F3"),
        [CareSpaceKind.OutpatientReception] = new Color("#D6E4F2"),
        [CareSpaceKind.Echo] = new Color("#EBDDE3"),
        [CareSpaceKind.Eeg] = new Color("#E4DDEE"),
        [CareSpaceKind.Ultrasound] = new Color("#DCE7EC"),
        [CareSpaceKind.Endoscopy] = new Color("#DDE6EA"),
        [CareSpaceKind.CathLab] = new Color("#DFE3E8"),
        [CareSpaceKind.PlasterRoom] = new Color("#E6E4DE"),
        [CareSpaceKind.PhysioGym] = new Color("#DDE9E2"),
        [CareSpaceKind.EndoscopyRecovery] = new Color("#E2EAEE"),
        [CareSpaceKind.OutreachOffice] = new Color("#E6DFEE"),
        [CareSpaceKind.Cafeteria] = new Color("#EFE3D2"),
        [CareSpaceKind.VendingPoint] = new Color("#ECE2D6"),
        [CareSpaceKind.Toilets] = new Color("#DCE8EC"),
        [CareSpaceKind.AccessibleToilet] = new Color("#D9E6EE"),
        [CareSpaceKind.StaffToilet] = new Color("#E0E6EA"),
        [CareSpaceKind.Lift] = new Color("#C9CFD5"),
        [CareSpaceKind.Stairs] = new Color("#D8D4CC"),
        [CareSpaceKind.DeliveryRoom] = new Color("#F2DDE8"),
    };

    private HospitalSimulation _sim = null!;
    private static HospitalSimulation? _current;

    private static bool IsCriticalCare(CareSpace space) => _current?.IsCriticalCareUnit(space) == true;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        _current = simulation;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_sim is null)
        {
            return;
        }

        foreach (var space in _sim.Spaces.Where(s => IsoDraw.OnViewFloor(s.Rect.Y)).OrderBy(s => s.Kind == CareSpaceKind.Corridor ? 0 : 1))
        {
            var r = space.Rect;
            var colour = FloorColours.GetValueOrDefault(space.Kind, new Color("#E6E9EC"));
            if (DecorArt.LookOf(_sim, space) is { IsStandard: false } look && look.Floor != FloorFinish.Standard)
            {
                DecorArt.DrawRoomFloor(this, space, look);
                continue;
            }

            IsoDraw.FloorQuad(this, r.X, r.Y, r.Width, r.Length, colour);

            // A subtle half-metre tile pattern.
            for (var x = r.X; x < r.Right; x++)
            {
                for (var y = r.Y; y < r.Bottom; y++)
                {
                    if ((x + y) % 2 == 0)
                    {
                        IsoDraw.FloorQuad(this, x, y, 1, 1, colour.Darkened(0.025f));
                    }
                }
            }
        }

        DrawWayfinding();
        foreach (var space in _sim.Spaces.Where(s => s.IsShaft && IsoDraw.OnViewFloor(s.Rect.Y)))
        {
            if (space.Kind == CareSpaceKind.Lift)
            {
                DrawLift(space);
            }
            else
            {
                DrawStairs(space);
            }
        }

        if (IsoDraw.ViewFloor > 0)
        {
            return;
        }

        // Ambulance bay markings: one box per handover slot.
        foreach (var slot in _sim.AmbulanceBayPatientTiles)
        {
            IsoDraw.FloorOutline(this, slot.X + 3, slot.Y - 1, 12, 3.6, new Color(1, 1, 1, 0.7f), 2f);
        }
    }

    /// <summary>The colour of the band outside a clinical room's door, or null for rooms without one.</summary>
    public static Color? WayfindingColour(CareSpace space) =>
        space.Kind switch
        {
            CareSpaceKind.Resus => new Color("#E53935"),
            CareSpaceKind.Majors => new Color("#F9A825"),
            CareSpaceKind.Minors => new Color("#43A047"),
            CareSpaceKind.Triage => new Color("#7E57C2"),
            CareSpaceKind.Ward when IsCriticalCare(space) => new Color("#5E35B1"),
            CareSpaceKind.Ward when space.DepartmentId == HospitalSimulation.MaternityDepartmentId => new Color("#D46A9F"),
            CareSpaceKind.DeliveryRoom => new Color("#D46A9F"),
            CareSpaceKind.Ward => new Color("#1E88E5"),
            CareSpaceKind.OutreachOffice => new Color("#5E35B1"),
            CareSpaceKind.XRay or CareSpaceKind.CT => new Color("#546E7A"),
            CareSpaceKind.Theatre => new Color("#00897B"),
            CareSpaceKind.Pharmacy => new Color("#7CB342"),
            CareSpaceKind.Clinic => new Color("#5C6BC0"),
            CareSpaceKind.Echo or CareSpaceKind.CathLab => new Color("#AD1457"),
            CareSpaceKind.Eeg => new Color("#6A1B9A"),
            CareSpaceKind.Ultrasound => new Color("#00838F"),
            CareSpaceKind.Endoscopy or CareSpaceKind.EndoscopyRecovery => new Color("#455A64"),
            CareSpaceKind.PlasterRoom => new Color("#8D6E63"),
            CareSpaceKind.PhysioGym => new Color("#2E7D32"),
            CareSpaceKind.Lift or CareSpaceKind.Stairs => new Color("#37474F"),
            _ => (Color?)null,
        };

    /// <summary>
    /// A coloured band on the corridor floor outside each clinical room's door, so the way to
    /// Resus (red), Majors (amber), Minors (green) and Triage (purple) can be seen at a glance.
    /// </summary>
    private void DrawWayfinding()
    {
        foreach (var space in _sim.Spaces.Where(s => s.HasWalls && s.Doors.Count > 0 && IsoDraw.OnViewFloor(s.Rect.Y)))
        {
            var colour = WayfindingColour(space);
            if (colour is not { } c)
            {
                continue;
            }

            var door = space.Doors[0];
            var r = space.Rect;
            const double gap = 0.35;
            ((double X, double Y) A, (double X, double Y) B) ends = door.Side switch
            {
                WallSide.North => ((r.X + door.Offset, r.Y - gap), (r.X + door.Offset + door.Width, r.Y - gap)),
                WallSide.South => ((r.X + door.Offset, r.Bottom + gap), (r.X + door.Offset + door.Width, r.Bottom + gap)),
                WallSide.West => ((r.X - gap, r.Y + door.Offset), (r.X - gap, r.Y + door.Offset + door.Width)),
                _ => ((r.Right + gap, r.Y + door.Offset), (r.Right + gap, r.Y + door.Offset + door.Width)),
            };
            Line(new[] { ends.A, ends.B }, c, 6f);
        }
    }

    /// <summary>A lift car: a brushed steel floor plate with a chequer tread, and the landing doors' sill at the doorway.</summary>
    private void DrawLift(CareSpace space)
    {
        var r = space.Rect;
        const double inset = 0.45;
        var (x, y, w, l) = (r.X + inset, r.Y + inset, r.Width - (2 * inset), r.Length - (2 * inset));
        IsoDraw.FloorQuad(this, x, y, w, l, new Color("#9EA7B0"));
        for (var i = 1; i < 6; i++)
        {
            var t = i / 6.0;
            Line(new[] { (x + (w * t), y), (x + (w * t), y + l) }, new Color("#B9C1C9"), 1.5f);
            Line(new[] { (x, y + (l * t)), (x + w, y + (l * t)) }, new Color("#B9C1C9"), 1.5f);
        }

        IsoDraw.FloorOutline(this, x, y, w, l, new Color("#5E6B77"), 2f);

        // The sill and the two sliding door leaves, drawn open, at the doorway.
        if (space.Doors.Count > 0)
        {
            var door = space.Doors[0];
            var (a, b) = DoorEnds(r, door, -0.15);
            Line(new[] { a, b }, new Color("#6D7883"), 7f);
        }
    }

    /// <summary>A flight of stairs: treads rising away from the door, with a handrail down each side and an arrow up.</summary>
    private void DrawStairs(CareSpace space)
    {
        var r = space.Rect;
        const double inset = 0.45;
        var (x, y, w, l) = (r.X + inset, r.Y + inset, r.Width - (2 * inset), r.Length - (2 * inset));
        var side = space.DoorSide;
        var alongY = side is WallSide.North or WallSide.South;
        var depth = alongY ? l : w;
        var steps = Math.Max(4, (int)Math.Round(depth * 2));
        for (var i = 0; i < steps; i++)
        {
            // Step 0 is at the door; each one further in is a little darker, as if higher and catching less light.
            var from = i * depth / steps;
            var to = (i + 1) * depth / steps;
            var shade = new Color("#E2DDD3").Darkened(0.04f + (0.3f * i / steps));
            var (sx, sy, sw, sl) = side switch
            {
                WallSide.South => (x, y + l - to, w, to - from),
                WallSide.North => (x, y + from, w, to - from),
                WallSide.East => (x + w - to, y, to - from, l),
                _ => (x + from, y, to - from, l),
            };
            IsoDraw.FloorQuad(this, sx, sy, sw, sl, shade);
            IsoDraw.FloorOutline(this, sx, sy, sw, sl, new Color("#8C857A"), 1f);
        }

        // Handrails along both long sides.
        var rail = new Color("#5D4E3C");
        if (alongY)
        {
            Line(new[] { (x + 0.15, y), (x + 0.15, y + l) }, rail, 3f);
            Line(new[] { (x + w - 0.15, y), (x + w - 0.15, y + l) }, rail, 3f);
        }
        else
        {
            Line(new[] { (x, y + 0.15), (x + w, y + 0.15) }, rail, 3f);
            Line(new[] { (x, y + l - 0.15), (x + w, y + l - 0.15) }, rail, 3f);
        }

        // An arrow pointing up the flight, away from the door.
        var (cx, cy) = (x + (w / 2), y + (l / 2));
        var (dx, dy) = side switch
        {
            WallSide.South => (0.0, -1.0),
            WallSide.North => (0.0, 1.0),
            WallSide.East => (-1.0, 0.0),
            _ => (1.0, 0.0),
        };
        var half = depth * 0.3;
        var tip = (cx + (dx * half), cy + (dy * half));
        Line(new[] { (cx - (dx * half), cy - (dy * half)), tip }, new Color("#37474F"), 4f);
        Line(new[] { (tip.Item1 - (dx * 0.6) - (dy * 0.5), tip.Item2 - (dy * 0.6) + (dx * 0.5)), tip, (tip.Item1 - (dx * 0.6) + (dy * 0.5), tip.Item2 - (dy * 0.6) - (dx * 0.5)) }, new Color("#37474F"), 4f);
    }

    /// <summary>The two ends of a doorway, <paramref name="gap"/> tiles outside the wall (negative: inside).</summary>
    private static ((double X, double Y) A, (double X, double Y) B) DoorEnds(GridRect r, Doorway door, double gap) => door.Side switch
    {
        WallSide.North => ((r.X + door.Offset, r.Y - gap), (r.X + door.Offset + door.Width, r.Y - gap)),
        WallSide.South => ((r.X + door.Offset, r.Bottom + gap), (r.X + door.Offset + door.Width, r.Bottom + gap)),
        WallSide.West => ((r.X - gap, r.Y + door.Offset), (r.X - gap, r.Y + door.Offset + door.Width)),
        _ => ((r.Right + gap, r.Y + door.Offset), (r.Right + gap, r.Y + door.Offset + door.Width)),
    };

    private void Line((double X, double Y)[] points, Color colour, float width = 3f)
    {
        var screen = points.Select(p => IsoDraw.ToScreen(p.X, p.Y)).ToArray();
        DrawPolyline(screen, new Color(colour, 0.75f), width, true);
    }
}
