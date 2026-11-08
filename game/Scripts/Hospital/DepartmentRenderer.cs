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
    };

    private HospitalSimulation _sim = null!;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_sim is null)
        {
            return;
        }

        foreach (var space in _sim.Spaces.OrderBy(s => s.Kind == CareSpaceKind.Corridor ? 0 : 1))
        {
            var r = space.Rect;
            var colour = FloorColours[space.Kind];
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

        // Ambulance bay markings: one box per handover slot.
        foreach (var slot in _sim.AmbulanceBayPatientTiles)
        {
            IsoDraw.FloorOutline(this, slot.X + 3, slot.Y - 1, 12, 3.6, new Color(1, 1, 1, 0.7f), 2f);
        }
    }

    /// <summary>
    /// A coloured band on the corridor floor outside each clinical room's door, so the way to
    /// Resus (red), Majors (amber), Minors (green) and Triage (purple) can be seen at a glance.
    /// </summary>
    private void DrawWayfinding()
    {
        foreach (var space in _sim.Spaces.Where(s => s.HasWalls && s.Doors.Count > 0))
        {
            var colour = space.Kind switch
            {
                CareSpaceKind.Resus => new Color("#E53935"),
                CareSpaceKind.Majors => new Color("#F9A825"),
                CareSpaceKind.Minors => new Color("#43A047"),
                CareSpaceKind.Triage => new Color("#7E57C2"),
                CareSpaceKind.Ward => new Color("#1E88E5"),
                CareSpaceKind.XRay or CareSpaceKind.CT => new Color("#546E7A"),
                CareSpaceKind.Theatre => new Color("#00897B"),
                CareSpaceKind.Pharmacy => new Color("#7CB342"),
                _ => (Color?)null,
            };
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

    private void Line((double X, double Y)[] points, Color colour, float width = 3f)
    {
        var screen = points.Select(p => IsoDraw.ToScreen(p.X, p.Y)).ToArray();
        DrawPolyline(screen, new Color(colour, 0.75f), width, true);
    }
}
