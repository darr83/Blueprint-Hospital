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
/// The plan view (V or the 2D button): the 3D hospital seen straight down, like an architect's
/// plan. This layer adds room names over it and marks everyone with a circle in the colour of their
/// uniform, so people are easy to find and pick from above. Patients are a circle in their own
/// clothes with a ring in their triage colour. It only reads the game.
/// </summary>
public partial class PlanLayer : Node2D
{
    /// <summary>A person's circle, in tiles (a tile is half a metre).</summary>
    public const double PersonRadius = 0.42;

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

    public override void _Draw()
    {
        if (_world is null)
        {
            return;
        }

        // The 3D world below draws the rooms, furniture and vehicles from above; this layer marks the people.
        var beds = new Dictionary<GridCoord, bool>();
        foreach (var space in Sim.Spaces.Where(s => IsoDraw.OnViewFloor(s.Rect.Y)))
        {
            foreach (var bed in space.Beds)
            {
                beds[bed.PatientSpot] = bed.HeadSouth;
            }

            if (space.PatientSpot is var spot && !space.HasBeds)
            {
                beds.TryAdd(spot, false);
            }

            if (RoomSigns.Named(space))
            {
                RoomLabel(space);
            }
        }

        foreach (var agent in _world.AgentsInView())
        {
            switch (agent)
            {
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

    // ------------------------------------------------------------------ rooms

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
            at = P(x, y - 1.0);
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
