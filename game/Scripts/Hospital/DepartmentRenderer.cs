using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// The department's floors: a tint per kind of room, the ambulance bay markings and coloured
/// wayfinding lines along the corridor (red to Resus, green to Minors, purple to Triage).
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

    private void DrawWayfinding()
    {
        var corridor = _sim.Space("corridor_main").Rect;
        var link = _sim.Space("corridor_link").Rect;
        var resus = _sim.Space("resus_1");
        var triage = _sim.Space("triage_1");
        var minors = _sim.Spaces.Where(s => s.Kind == CareSpaceKind.Minors).OrderBy(s => s.Rect.X).ToList();

        // Red: from the ambulance entrance (east end of the corridor) to the Resus doors.
        var resusDoor = resus.Doors[0];
        var resusX = resus.Rect.X + resusDoor.Offset + (resusDoor.Width / 2.0);
        Line(new[] { (corridor.Right - 0.5, corridor.Y + 2.5), (resusX, corridor.Y + 2.5), (resusX, corridor.Y + 0.3) }, new Color("#E53935"));

        // Green: from the waiting room corridor to Minors.
        var linkX = link.X + (link.Width / 2.0) - 1;
        var minorsX = minors[0].Rect.X + minors[0].Doors[0].Offset + 1.5;
        Line(new[] { (linkX, link.Bottom - 0.5), (linkX, corridor.Y + 4.5), (minorsX, corridor.Y + 4.5), (minorsX, corridor.Y + 0.3) }, new Color("#43A047"));

        // Purple: from the waiting room corridor to Triage.
        var triageY = triage.Rect.Y + triage.Doors[0].Offset + 1;
        Line(new[] { (linkX + 2, link.Bottom - 0.5), (linkX + 2, triageY), (triage.Rect.X + 0.3, triageY) }, new Color("#7E57C2"));
    }

    private void Line((double X, double Y)[] points, Color colour)
    {
        var screen = points.Select(p => IsoDraw.ToScreen(p.X, p.Y)).ToArray();
        DrawPolyline(screen, new Color(colour, 0.75f), 3f, true);
    }
}
