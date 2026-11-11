using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.Transport;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// The 2D plan view (V or the 2D button): the hospital seen straight down, like an architect's
/// plan. Floors come from the usual floor renderers (IsoDraw draws them flat while the plan is on);
/// this layer adds walls, doorways, furniture, beds, monitors and room names, and draws everyone
/// as a simple circle in the colour of their uniform. Patients are a circle in their own clothes
/// with a ring in their triage colour. It only reads the game, like the isometric world layer.
/// </summary>
public partial class PlanLayer : Node2D
{
    /// <summary>A person's circle, in tiles (a tile is half a metre).</summary>
    public const double PersonRadius = 0.42;

    private static readonly Color WallColour = new("#56636F");
    private static readonly Color DoorColour = new("#B9C6D1");
    private static readonly Color Outline = new("#1D2B36");
    private static readonly Color Selected = new("#FFEB3B");
    private static readonly Color LabelColour = new("#33424F");

    private WorldLayer _world = null!;
    private Font _font = null!;

    public void Initialise(WorldLayer world)
    {
        _world = world;
        _font = ThemeDB.FallbackFont;
    }

    public override void _Process(double delta)
    {
        if (Visible)
        {
            QueueRedraw();
        }
    }

    private HospitalSimulation Sim => _world.Simulation;

    private static Vector2 P(double x, double y) => IsoDraw.ToScreen(x, y);

    private static float Px(double tiles) => (float)(tiles * IsoDraw.PlanTile);

    private void Rect(double x, double y, double w, double l, Color fill, Color? edge = null, float width = 1.2f)
    {
        var corners = new[] { P(x, y), P(x + w, y), P(x + w, y + l), P(x, y + l) };
        DrawColoredPolygon(corners, fill);
        if (edge is { } e)
        {
            DrawPolyline(corners.Append(corners[0]).ToArray(), e, width, true);
        }
    }

    public override void _Draw()
    {
        if (_world is null)
        {
            return;
        }

        var spaces = Sim.Spaces.Where(s => IsoDraw.OnViewFloor(s.Rect.Y)).ToList();
        var beds = new Dictionary<GridCoord, bool>();
        foreach (var space in spaces)
        {
            DrawFurniture(space, beds);
        }

        foreach (var chair in Sim.Chairs.Where(c => IsoDraw.OnViewFloor(c.Tile.Y)))
        {
            Chair(chair.Tile.X + 0.5, chair.Tile.Y + 0.5, new Color(chair.Colour));
        }

        foreach (var trolley in Sim.ResusTrolleys)
        {
            var (x, y) = _world.Interpolated(trolley);
            if (IsoDraw.OnViewFloor(y))
            {
                Rect(x - 0.6, y - 0.7, 1.2, 1.4, new Color("#D84B3E"), Outline);
                DrawLine(P(x - 0.3, y), P(x + 0.3, y), Colors.White, 2f);
                DrawLine(P(x, y - 0.3), P(x, y + 0.3), Colors.White, 2f);
            }
        }

        DrawWalls();
        DrawMonitors();
        foreach (var space in spaces.Where(RoomSigns.Named))
        {
            RoomLabel(space);
        }

        foreach (var agent in _world.AgentsInView().OrderBy(a => a is AmbulanceVehicle ? 0 : 1))
        {
            switch (agent)
            {
                case AmbulanceVehicle ambulance:
                    Ambulance(ambulance);
                    break;
                case Patient patient:
                    DrawPatient(patient, beds);
                    break;
                case Outpatient outpatient:
                    Person(_world.FootPosition(outpatient), Looks.Outpatient(outpatient, Sim.FindAppointment(outpatient.AppointmentId)).Top, Looks.OutpatientRing, ReferenceEquals(_world.Selected, outpatient));
                    break;
                case StaffMember member:
                    var (fill, ring) = Colours(member);
                    Person(_world.FootPosition(member), fill, ring, ReferenceEquals(_world.Selected, member));
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ walls

    private void DrawWalls()
    {
        const double h = WorldLayer.WallThickness / 2;
        foreach (var (x, y, horizontal) in _world.DoorEdges)
        {
            // A doorway: a pale threshold across the gap.
            if (horizontal)
            {
                Rect(x, y - 0.06, 1, 0.12, DoorColour);
            }
            else
            {
                Rect(x - 0.06, y, 0.12, 1, DoorColour);
            }
        }

        foreach (var (x, y, horizontal) in _world.WallEdges)
        {
            if (horizontal)
            {
                Rect(x - h, y - h, 1 + (2 * h), 2 * h, WallColour);
            }
            else
            {
                Rect(x - h, y - h, 2 * h, 1 + (2 * h), WallColour);
            }
        }
    }

    // ------------------------------------------------------------------ furniture

    private void DrawFurniture(CareSpace space, Dictionary<GridCoord, bool> beds)
    {
        foreach (var item in space.Furniture)
        {
            if (item.ItemId == HospitalSimulation.ResusTrolleyItem || !Sim.Database.Furniture.TryGetValue(item.ItemId, out var def))
            {
                continue;
            }

            var colour = new Color(def.Colour);
            switch (def.Style)
            {
                case FurnitureStyle.Monitor:
                    // Drawn on top with the walls (see DrawMonitors).
                    break;
                case FurnitureStyle.Bed when def.Id == "incubator":
                    Rect(item.X, item.Y, item.Width, item.Length, new Color("#DDEFF5"), new Color("#7FA9BC"));
                    break;
                case FurnitureStyle.Bed when space.IsServiceRoom:
                    Rect(item.X, item.Y, item.Width, item.Length, colour.Lightened(0.35f), colour.Darkened(0.35f));
                    break;
                case FurnitureStyle.Bed when space.IsWard:
                    if (space.Beds.FirstOrDefault(b => b.PatientSpot == item.Tile) is { } wardBed)
                    {
                        beds[wardBed.PatientSpot] = wardBed.HeadSouth;
                        Bed(wardBed.PatientSpot, wardBed.HeadSouth);
                    }

                    break;
                case FurnitureStyle.Bed:
                    beds[space.PatientSpot] = false;
                    Bed(space.PatientSpot, false);
                    break;
                case FurnitureStyle.Chair:
                    Chair(item.CentreX, item.CentreY, colour);
                    break;
                default:
                    Rect(item.X, item.Y, item.Width, item.Length, colour.Lightened(0.15f), colour.Darkened(0.4f), 1f);
                    break;
            }
        }
    }

    /// <summary>A bed (0.8 m by 2.1 m) seen from above: frame, white sheet and a pillow at the head.</summary>
    private void Bed(GridCoord spot, bool headSouth)
    {
        var (x, y) = (spot.X + 0.5 - 0.8, spot.Y + 0.5 - 2.1);
        Rect(x, y, 1.6, 4.2, new Color("#9DB2C4"), new Color("#6F8396"));
        Rect(x + 0.12, y + 0.12, 1.36, 3.96, new Color("#F4F7F9"));
        var pillowY = headSouth ? y + 4.2 - 0.95 : y + 0.25;
        Rect(x + 0.3, pillowY, 1.0, 0.7, Colors.White, new Color("#C9D3DB"), 1f);
    }

    private void Chair(double cx, double cy, Color colour) =>
        Rect(cx - 0.35, cy - 0.35, 0.7, 0.7, colour, colour.Darkened(0.4f), 1f);

    /// <summary>Obs monitors: a small screen whose border follows the patient's NEWS2 band, as in the 3D view.</summary>
    private void DrawMonitors()
    {
        foreach (var monitor in _world.Monitors)
        {
            if (!IsoDraw.OnViewFloor(monitor.Y))
            {
                continue;
            }

            var patient = _world.ConnectedPatient(monitor);
            var border = patient is null ? new Color("#7C8A96") : patient.MonitorColour is not { } colour ? new Color("#F2F4F6") : Looks.TriageRing(colour);
            Rect(monitor.X - 0.45, monitor.Y - 0.35, 0.9, 0.7, border, Outline, 1f);
            Rect(monitor.X - 0.3, monitor.Y - 0.2, 0.6, 0.4, patient is null ? new Color("#26323C") : new Color("#0E1A22"));
            if (patient is not null)
            {
                // A little trace that moves, so a connected monitor looks alive.
                var t = (float)(_world.Time * 2 % 1);
                var a = P(monitor.X - 0.25, monitor.Y);
                var b = P(monitor.X + 0.25, monitor.Y);
                DrawLine(a, b, new Color("#57E389", 0.6f), 1f);
                DrawCircle(a.Lerp(b, t), 1.6f, new Color("#57E389"));
            }
        }
    }

    private void RoomLabel(CareSpace space)
    {
        var r = space.Rect;
        var centre = P(r.X + (r.Width / 2.0), r.Y + (r.Length / 2.0));
        const int size = 15;
        var text = space.Name;
        var width = _font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        var room = Math.Min(Px(r.Width), Px(r.Length)) > Px(3) ? Math.Max(Px(r.Width), Px(r.Length)) : 0;
        if (room <= 0)
        {
            return;
        }

        DrawString(_font, centre + new Vector2(-width / 2, size / 3f), text, HorizontalAlignment.Left, -1, size, new Color(LabelColour, 0.55f));
    }

    // ------------------------------------------------------------------ people

    /// <summary>The circle colour and ring for a member of staff: the uniform and its piping or trim.</summary>
    public static (Color Fill, Color Ring) Colours(StaffMember member)
    {
        var look = Looks.Staff(member);
        if (member.Role == StaffRole.Doctor && member.Grade.Contains("Consultant", StringComparison.OrdinalIgnoreCase))
        {
            // Consultants wear their own shirt and tie, so they show as a dark suit with the tie as the ring.
            return (look.Trousers, look.Tie ?? Colors.White);
        }

        var fill = look.Jacket ?? look.Top;
        var ring = look.HiVis ? new Color("#D8DDE2") : look.Epaulettes ?? look.Trim ?? fill.Darkened(0.35f);
        return (fill, ring);
    }

    private void Person(Vector2 at, Color fill, Color ring, bool selected, float scale = 1f)
    {
        var r = Px(PersonRadius) * scale;
        if (selected)
        {
            DrawCircle(at, r + 5, Selected);
        }

        DrawCircle(at + new Vector2(1.5f, 2f), r, new Color(0, 0, 0, 0.18f));
        DrawCircle(at, r, Outline);
        DrawCircle(at, r - 1.2f, ring);
        DrawCircle(at, r - 4f, fill);
    }

    private void DrawPatient(Patient patient, Dictionary<GridCoord, bool> beds)
    {
        var at = _world.FootPosition(patient);
        if (_world.InBed(patient) && beds.TryGetValue(patient.Tile, out var headSouth))
        {
            // Head on the pillow.
            at = P(patient.Tile.X + 0.5, patient.Tile.Y + 0.5 + (headSouth ? 1.25 : -1.25));
        }
        else if (_world.OnTrolley(patient) || _world.OnAmbulanceStretcher(patient))
        {
            var (x, y) = _world.Interpolated(patient);
            Rect(x - 0.7, y - 1.9, 1.4, 3.8, new Color("#E8EEF2"), new Color("#7C8A96"));
            at = P(x, y - 1.0);
        }
        else if (_world.InWheelchair(patient))
        {
            var (x, y) = _world.Interpolated(patient);
            Rect(x - 0.55, y - 0.55, 1.1, 1.1, new Color("#3A4652"));
        }

        var ring = patient.Triage is null ? Colors.White : Looks.TriageRing(patient.Triage);
        Person(at, Looks.Patient(patient).Top, ring, ReferenceEquals(_world.Selected, patient), patient.Age < 12 ? 0.8f : 1f);
        if (patient.InArrest)
        {
            var pulse = 0.5f + (0.5f * (float)Math.Sin(_world.Time * 8));
            DrawArc(at, Px(PersonRadius) + 7, 0, Mathf.Tau, 24, new Color(Looks.Red, 0.4f + (0.6f * pulse)), 3f, true);
        }

        foreach (var (baby, i) in _world.BabiesWith(patient).Select((b, i) => (b, i)))
        {
            var off = new Vector2(Px(0.55) + (i * Px(0.35)), Px(0.35));
            DrawCircle(at + off, Px(0.2), Outline);
            DrawCircle(at + off, Px(0.2) - 1.5f, baby.Wrap);
        }
    }

    private void Ambulance(AmbulanceVehicle ambulance)
    {
        var (x, y) = _world.Interpolated(ambulance);
        var dir = ambulance.Facing;
        var len = Math.Sqrt((dir.X * dir.X) + (dir.Y * dir.Y));
        var (fx, fy) = len < 1e-6 ? (1.0, 0.0) : (dir.X / len, dir.Y / len);
        var (nx, ny) = (-fy, fx);
        var (cx, cy) = (x - (fx * 2.9), y - (fy * 2.9));

        Vector2[] Box(double from, double to, double half) => new[]
        {
            P(cx + (fx * to) + (nx * half), cy + (fy * to) + (ny * half)),
            P(cx + (fx * to) - (nx * half), cy + (fy * to) - (ny * half)),
            P(cx + (fx * from) - (nx * half), cy + (fy * from) - (ny * half)),
            P(cx + (fx * from) + (nx * half), cy + (fy * from) + (ny * half)),
        };

        var body = Box(-5.8, 5.8, 2.5);
        DrawColoredPolygon(body, new Color("#F2D22E"));
        DrawColoredPolygon(Box(-5.8, 5.8, 0.5), new Color("#2E8B3E"));
        DrawColoredPolygon(Box(3.6, 5.8, 2.3), new Color("#DDE3E8"));
        DrawPolyline(body.Append(body[0]).ToArray(), Outline, 1.5f, true);
        var flashing = ambulance.State is AmbulanceState.Driving or AmbulanceState.WaitingForBay
                       && Sim.FindPatient(ambulance.PatientId) is { Triage: TriageCategory.Red or null };
        if (flashing && (int)(_world.Time * 6) % 2 == 0)
        {
            DrawCircle(P(cx + (fx * 3.4), cy + (fy * 3.4)), 6, new Color("#4FA3FF"));
        }
    }

    /// <summary>The person under a point on screen, if any: the top-most circle wins.</summary>
    public Agent? HitTest(Vector2 world)
    {
        Agent? best = null;
        var r = Px(PersonRadius) + 3;
        foreach (var agent in _world.AgentsInView())
        {
            if (agent is AmbulanceVehicle)
            {
                continue;
            }

            var at = _world.FootPosition(agent);
            if (agent is Patient p && _world.InBed(p))
            {
                // Clicking anywhere on the bed picks its patient.
                var (x, y) = (p.Tile.X + 0.5, p.Tile.Y + 0.5);
                var bed = new Rect2(P(x - 0.8, y - 2.1), Vector2.Zero).Expand(P(x + 0.8, y + 2.1));
                if (bed.HasPoint(world))
                {
                    best = agent;
                }

                continue;
            }

            if (at.DistanceTo(world) <= r)
            {
                best = agent;
            }
        }

        return best;
    }

    /// <summary>
    /// The plan's key: one row per uniform among the staff in the building, so the circles can be
    /// read without clicking. Grades that share a uniform share a row.
    /// </summary>
    public static List<(string Label, Color Fill, Color Ring)> Key(HospitalSimulation sim)
    {
        var rows = new List<(string Label, Color Fill, Color Ring)>();
        foreach (var member in sim.Staff.Where(s => s.IsInBuilding).OrderBy(s => s.Role).ThenBy(s => s.Grade))
        {
            var (fill, ring) = Colours(member);
            if (rows.Any(r => r.Fill.IsEqualApprox(fill) && r.Ring.IsEqualApprox(ring)))
            {
                continue;
            }

            var label = member.Role is StaffRole.Nurse or StaffRole.Doctor or StaffRole.Midwife ? member.Grade : StaffGrades.RoleName(member.Role);
            rows.Add((label.Length > 0 ? char.ToUpperInvariant(label[0]) + label[1..] : label, fill, ring));
        }

        return rows;
    }
}
