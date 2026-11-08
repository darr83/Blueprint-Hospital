using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.Transport;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Draws the people and vehicles in the hospital every frame, smoothly interpolated between
/// simulation ticks and sorted by depth. Patients carry a triage-coloured marker; the selected
/// person has a ring. Also answers "who is under the mouse?".
/// </summary>
public partial class AgentRenderer : Node2D
{
    public static readonly Color Red = new("#E53935");
    public static readonly Color Amber = new("#F9A825");
    public static readonly Color Green = new("#43A047");

    private static readonly Color[] ClothesColours =
    {
        new("#6B4E3D"), new("#3E5C76"), new("#7A3B47"), new("#4D6B3C"), new("#8A6E2F"), new("#55476E"), new("#2F4F4F"), new("#9C5B2E"),
    };

    private static readonly Color[] SkinTones =
    {
        new("#F1C7A5"), new("#E0AC86"), new("#C68C5F"), new("#9C6B43"), new("#6E4A2E"), new("#F5D3BC"),
    };

    private readonly List<(Agent Agent, Vector2 Foot, float Depth)> _visible = new();
    private HospitalSimulation _sim = null!;
    private SimulationClock _clock = null!;
    private double _time;

    /// <summary>The patient or staff member the player has selected, if any.</summary>
    public Agent? Selected { get; set; }

    public void Initialise(HospitalSimulation simulation, SimulationClock clock)
    {
        _sim = simulation;
        _clock = clock;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        QueueRedraw();
    }

    /// <summary>Screen position (world space) of an agent's feet, interpolated to this frame.</summary>
    public Vector2 FootPosition(Agent agent)
    {
        var t = _clock.TickProgress;
        var x = agent.PreviousX + ((agent.X - agent.PreviousX) * t);
        var y = agent.PreviousY + ((agent.Y - agent.PreviousY) * t);
        return IsoDraw.ToScreen(x, y);
    }

    /// <summary>The patient or staff member under a world-space point, nearest the viewer first.</summary>
    public Agent? HitTest(Vector2 world)
    {
        Agent? best = null;
        var bestDepth = float.MinValue;
        foreach (var (agent, foot, depth) in _visible)
        {
            if (agent is AmbulanceVehicle)
            {
                continue;
            }

            var lying = agent is Patient p && IsLying(p);
            var box = lying
                ? new Rect2(foot.X - 34, foot.Y - 50, 68, 56)
                : new Rect2(foot.X - 14, foot.Y - 66, 28, 72);
            if (box.HasPoint(world) && depth > bestDepth)
            {
                best = agent;
                bestDepth = depth;
            }
        }

        return best;
    }

    public override void _Draw()
    {
        if (_sim is null)
        {
            return;
        }

        _visible.Clear();
        var hidden = _sim.Ambulances
            .Where(a => a.Purpose == AmbulancePurpose.Arrival && a.State is AmbulanceState.Driving or AmbulanceState.WaitingForBay)
            .Select(a => a.PatientId)
            .ToHashSet();

        foreach (var ambulance in _sim.Ambulances)
        {
            Add(ambulance);
        }

        foreach (var patient in _sim.Patients)
        {
            if (!hidden.Contains(patient.Id) && patient.Stage != PatientStage.Gone)
            {
                Add(patient);
            }
        }

        foreach (var member in _sim.Staff)
        {
            Add(member);
        }

        foreach (var (agent, foot, _) in _visible.OrderBy(v => v.Depth))
        {
            switch (agent)
            {
                case AmbulanceVehicle ambulance:
                    DrawAmbulance(ambulance);
                    break;
                case Patient patient:
                    DrawPatient(patient, foot);
                    break;
                case StaffMember member:
                    DrawStaff(member, foot);
                    break;
            }
        }
    }

    private void Add(Agent agent)
    {
        var t = _clock.TickProgress;
        var x = agent.PreviousX + ((agent.X - agent.PreviousX) * t);
        var y = agent.PreviousY + ((agent.Y - agent.PreviousY) * t);
        var depth = (float)(x + y);
        _visible.Add((agent, IsoDraw.ToScreen(x, y), depth));
    }

    public bool IsLying(Patient patient)
    {
        if (patient.IsMoving)
        {
            return false;
        }

        if (patient.Stage == PatientStage.InAmbulanceQueue)
        {
            return true;
        }

        return patient.CareSpaceId is { } id
               && _sim.Space(id) is { IsTreatmentSpace: true } space
               && patient.Tile == space.PatientSpot;
    }

    // ------------------------------------------------------------------ patients

    private void DrawPatient(Patient patient, Vector2 foot)
    {
        var clothes = ClothesColours[patient.Id % ClothesColours.Length];
        var skin = SkinTones[(patient.Id * 7) % SkinTones.Length];
        var selected = ReferenceEquals(Selected, patient);
        var alpha = patient.IsAlive ? 1f : 0.55f;

        float markerHeight;
        if (IsLying(patient))
        {
            if (selected)
            {
                DrawSelectionRing(foot, 30);
            }

            // On a trolley: blanket from chest to feet, head on the pillow (towards the back wall).
            var lift = new Vector2(0, -20);
            var head = IsoDraw.ToScreen(patient.X, patient.Y - 1.55) + lift;
            var chest = IsoDraw.ToScreen(patient.X, patient.Y - 0.9) + lift;
            var feet = IsoDraw.ToScreen(patient.X, patient.Y + 1.6) + lift;
            if (patient.Stage == PatientStage.InAmbulanceQueue)
            {
                // Ambulance trolley.
                IsoDraw.Box(this, patient.X - 0.75, patient.Y - 2.1, 1.5, 4.1, 18, new Color("#D5DADF"));
            }

            DrawLine(chest, feet, new Color("#7FA7CF", alpha), 16f);
            DrawCircle(feet, 8, new Color("#7FA7CF", alpha));
            DrawLine(head, chest, new Color(clothes, alpha), 14f);
            DrawCircle(head + new Vector2(0, -4), 7, new Color(skin, alpha));
            markerHeight = 52;
            DrawTriageMarker(patient, head + new Vector2(0, -markerHeight + 30));
        }
        else
        {
            if (selected)
            {
                DrawSelectionRing(foot, 16);
            }

            DrawPerson(foot, clothes, clothes.Darkened(0.35f), skin, alpha, patient.IsMoving, patient.Id);
            markerHeight = 64;
            DrawTriageMarker(patient, foot + new Vector2(0, -markerHeight));
        }
    }

    private void DrawTriageMarker(Patient patient, Vector2 at)
    {
        var colour = patient.Triage switch
        {
            TriageCategory.Red => Red,
            TriageCategory.Amber => Amber,
            TriageCategory.Green => Green,
            _ => new Color("#FFFFFF"),
        };

        // Red patients' markers pulse so they stand out.
        var radius = 6.5f;
        if (patient.Triage == TriageCategory.Red)
        {
            radius += 1.5f * (float)Math.Sin(_time * 6);
        }

        DrawCircle(at, radius + 2, new Color("#1D2B36"));
        DrawCircle(at, radius, colour);
        if (patient.Triage is null)
        {
            DrawString(ThemeDB.FallbackFont, at + new Vector2(-3.5f, 5), "?", HorizontalAlignment.Left, -1, 13, new Color("#1D2B36"));
        }

        if (!patient.IsAlive)
        {
            DrawLine(at + new Vector2(-4, -4), at + new Vector2(4, 4), Colors.Black, 2);
            DrawLine(at + new Vector2(-4, 4), at + new Vector2(4, -4), Colors.Black, 2);
        }
    }

    // ------------------------------------------------------------------ staff

    private void DrawStaff(StaffMember member, Vector2 foot)
    {
        if (ReferenceEquals(Selected, member))
        {
            DrawSelectionRing(foot, 16);
        }

        var (top, trousers) = member.Role switch
        {
            StaffRole.Doctor => (new Color("#1F8A8A"), new Color("#1F8A8A").Darkened(0.2f)),
            StaffRole.Nurse when member.Grade.Contains("Senior", StringComparison.OrdinalIgnoreCase) => (new Color("#003087"), new Color("#003087").Darkened(0.2f)),
            StaffRole.Nurse => (new Color("#005EB8"), new Color("#005EB8").Darkened(0.25f)),
            _ => (new Color("#6E5A8C"), new Color("#2E2E38")),
        };

        var skin = SkinTones[(member.Id * 5 + 2) % SkinTones.Length];
        DrawPerson(foot, top, trousers, skin, 1f, member.IsMoving, member.Id + 100);

        if (member.Role == StaffRole.Doctor)
        {
            // Stethoscope.
            DrawArc(foot + new Vector2(0, -34), 5, 0.2f, MathF.PI - 0.2f, 8, new Color("#20262B"), 1.6f);
        }

        // Activity badge: working (spinner), on break (cup), walking to a task (arrow).
        var badge = foot + new Vector2(11, -58);
        switch (member.Activity)
        {
            case StaffActivity.Working:
                DrawCircle(badge, 6, new Color("#FFFFFF"));
                DrawArc(badge, 4, (float)(_time * 4), (float)(_time * 4) + 4.4f, 10, new Color("#005EB8"), 2f);
                break;
            case StaffActivity.OnBreak:
            case StaffActivity.WalkingToBreak:
                DrawCircle(badge, 6, new Color("#FFFFFF"));
                DrawRect(new Rect2(badge + new Vector2(-3, -2), new Vector2(5, 5)), new Color("#8A5A2B"));
                DrawArc(badge + new Vector2(2.5f, 0.5f), 2, -1.5f, 1.5f, 6, new Color("#8A5A2B"), 1.2f);
                break;
        }
    }

    // ------------------------------------------------------------------ shared figures

    private void DrawPerson(Vector2 foot, Color top, Color trousers, Color skin, float alpha, bool walking, int seed)
    {
        DrawColoredPolygon(IsoDraw.FloorEllipse(foot, 9), new Color(0, 0, 0, 0.18f));

        // Simple walk cycle: legs alternate while moving.
        var swing = walking ? (float)Math.Sin((_time * 9) + seed) * 3f : 0f;
        DrawLine(foot + new Vector2(-3, -16), foot + new Vector2(-3 + swing, -1), new Color(trousers, alpha), 5f);
        DrawLine(foot + new Vector2(3, -16), foot + new Vector2(3 - swing, -1), new Color(trousers, alpha), 5f);

        var torso = new Rect2(foot + new Vector2(-8, -38), new Vector2(16, 23));
        DrawRect(torso, new Color(top, alpha));
        DrawRect(new Rect2(torso.Position, new Vector2(16, 3)), new Color(top.Lightened(0.15f), alpha));
        DrawCircle(foot + new Vector2(0, -45), 7, new Color(skin, alpha));
    }

    private void DrawSelectionRing(Vector2 foot, float radius)
    {
        var ring = IsoDraw.FloorEllipse(foot, radius + 3, 28);
        var closed = ring.Append(ring[0]).ToArray();
        DrawPolyline(closed, new Color("#FFEB3B"), 3f, true);
    }

    // ------------------------------------------------------------------ vehicles

    private void DrawAmbulance(AmbulanceVehicle ambulance)
    {
        var t = _clock.TickProgress;
        var x = ambulance.PreviousX + ((ambulance.X - ambulance.PreviousX) * t);
        var y = ambulance.PreviousY + ((ambulance.Y - ambulance.PreviousY) * t);
        var dir = ambulance.Facing;

        // The vehicle sits behind its position (which is the front of the vehicle) along its heading.
        var body = (X: x - (dir.X * 4.5), Y: y - (dir.Y * 4.5));
        var cab = (X: x + (dir.X * 1.6), Y: y + (dir.Y * 1.6));
        var yellow = new Color("#F2D21B");

        IsoDraw.OrientedBox(this, cab.X, cab.Y, dir, 2.6, 4.2, 38, yellow);
        IsoDraw.OrientedBox(this, cab.X + (dir.X * 0.9), cab.Y + (dir.Y * 0.9), dir, 0.6, 3.8, 12, new Color("#26343F"), lift: 24);
        IsoDraw.OrientedBox(this, body.X, body.Y, dir, 8.4, 4.6, 58, yellow);

        // White roof.
        IsoDraw.OrientedBox(this, body.X, body.Y, dir, 8.4, 4.6, 0, new Color("#E9EEF2"), lift: 58);

        // Blue lights flash while the crew is on an emergency run.
        var flashing = ambulance.State is AmbulanceState.Driving or AmbulanceState.WaitingForBay
                       && _sim.FindPatient(ambulance.PatientId) is { Triage: TriageCategory.Red or null };
        var front = IsoDraw.ToScreen(body.X + (dir.X * 4), body.Y + (dir.Y * 4)) + new Vector2(0, -62);
        var on = flashing && ((int)(_time * 6) % 2 == 0);
        DrawCircle(front + new Vector2(-8, 0), 4, on ? new Color("#3D7BFF") : new Color("#1C3F8A"));
        DrawCircle(front + new Vector2(8, 0), 4, !on && flashing ? new Color("#3D7BFF") : new Color("#1C3F8A"));

        var label = ambulance.CallSign;
        var labelPos = IsoDraw.ToScreen(body.X, body.Y) + new Vector2(-18, -36);
        DrawString(ThemeDB.FallbackFont, labelPos, label, HorizontalAlignment.Left, -1, 12, new Color("#1E5631"));
    }
}
