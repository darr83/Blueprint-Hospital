using System;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Labels drawn above the world: room name cards, triage markers over patients who are up and
/// about, and small bubbles saying what each busy member of staff is doing.
/// </summary>
public partial class OverlayLayer : Node2D
{
    /// <summary>Below this zoom, staff bubbles are hidden to keep the view readable.</summary>
    private const float BubbleZoom = 0.5f;

    private WorldLayer _world = null!;
    private Font _font = null!;

    public void Initialise(WorldLayer world)
    {
        _world = world;
        _font = ThemeDB.FallbackFont;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_world is null)
        {
            return;
        }

        var sim = _world.Simulation;
        foreach (var space in sim.Spaces.Where(s => s.Kind != CareSpaceKind.Corridor))
        {
            RoomCard(space);
        }

        // Obs monitor screens, always visible above walls and beds.
        foreach (var monitor in _world.Monitors)
        {
            var patient = _world.ConnectedPatient(monitor);
            // The border goes red while a doctor is needed at the bedside, and amber once a Red patient is stabilised.
            var outline = patient is null ? (Color?)null : patient.MonitorColour is not { } colour ? new Color("#F2F4F6") : Looks.TriageRing(colour);
            Figures.ObsScreen(this, monitor.X, monitor.Y, _world.Time, patient?.CurrentObservations(), outline, patient?.LatestObservations?.News2.Total ?? 0);
        }

        foreach (var patient in sim.Patients.Where(p => _world.IsOnMap(p) && !_world.IsLying(p)))
        {
            TriageMarker(patient, _world.FootPosition(patient) + new Vector2(0, _world.IsSeated(patient) ? -64 : -71));
        }

        var zoom = GetViewport().GetCanvasTransform().X.X;
        foreach (var member in sim.Staff)
        {
            if (member.Activity == StaffActivity.Working && (zoom >= BubbleZoom || ReferenceEquals(_world.Selected, member)))
            {
                StaffBubble(member);
            }
        }
    }

    private void RoomCard(CareSpace space)
    {
        var r = space.Rect;
        var anchor = IsoDraw.ToScreen(r.X + (r.Width / 2.0), r.Bottom - 1.1);

        var title = space.Name;
        var detail = $"{r.Width / 2.0:0.#} × {r.Length / 2.0:0.#} m · {space.AreaSquareMetres:0} m²";
        if (space.IsTreatmentSpace)
        {
            detail = space.IsFree ? $"{detail} · free" : $"{detail} · in use";
        }
        else if (space.IsWard)
        {
            detail = $"{detail} · {space.Beds.Count(b => b.OccupantId is not null)}/{space.Beds.Count} beds";
        }

        const int titleSize = 22;
        const int detailSize = 15;
        var width = Math.Max(_font.GetStringSize(title, HorizontalAlignment.Left, -1, titleSize).X + 18, _font.GetStringSize(detail, HorizontalAlignment.Left, -1, detailSize).X) + 16;
        var card = new Rect2(anchor - new Vector2(width / 2, 25), new Vector2(width, 50));

        DrawRect(new Rect2(card.Position + new Vector2(1.5f, 2), card.Size), new Color(0, 0, 0, 0.15f));
        DrawRect(card, new Color(1, 1, 1, 0.94f));
        var accent = DepartmentRenderer.FloorColours[space.Kind].Darkened(0.45f);
        DrawCircle(card.Position + new Vector2(14, 16), 5.5f, accent);
        DrawString(_font, card.Position + new Vector2(26, 23), title, HorizontalAlignment.Left, -1, titleSize, new Color("#1D2B36"));
        DrawString(_font, card.Position + new Vector2(9, 42), detail, HorizontalAlignment.Left, -1, detailSize, new Color("#5B6B78"));
    }

    private void TriageMarker(Patient patient, Vector2 at)
    {
        var colour = Looks.TriageRing(patient.Triage);
        var radius = 6.5f;
        if (patient.Triage == TriageCategory.Red)
        {
            radius += 1.5f * (float)Math.Sin(_world.Time * 6);
        }

        DrawCircle(at, radius + 2, new Color("#1D2B36"));
        DrawCircle(at, radius, patient.Triage is null ? Colors.White : colour);
        if (patient.Triage is null)
        {
            DrawString(_font, at + new Vector2(-3.5f, 5), "?", HorizontalAlignment.Left, -1, 13, new Color("#1D2B36"));
        }
    }

    private void StaffBubble(StaffMember member)
    {
        var task = member.CurrentTaskId is { } id ? _world.Simulation.Tasks.FirstOrDefault(t => t.Id == id) : null;
        if (task is null)
        {
            return;
        }

        var label = task.Label.Length > 26 ? task.Label[..25] + "…" : task.Label;
        var minutes = Math.Max(1, (int)Math.Ceiling(member.WorkRemainingSeconds / 60));
        var text = $"{label} · {minutes} min";
        const int size = 14;
        var width = _font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X + 12;
        var foot = _world.FootPosition(member);
        var top = foot + new Vector2(0, _world.IsSeated(member) ? -78 : -84);
        var box = new Rect2(top - new Vector2(width / 2, 13), new Vector2(width, 21));

        DrawRect(box, new Color(1, 1, 1, 0.95f));
        DrawRect(new Rect2(box.Position, new Vector2(4, box.Size.Y)), new Color("#2BB3A3"));
        DrawColoredPolygon(new[] { top + new Vector2(-5, 8), top + new Vector2(5, 8), top + new Vector2(0, 14) }, new Color(1, 1, 1, 0.95f));
        DrawString(_font, box.Position + new Vector2(9, 16), text, HorizontalAlignment.Left, -1, size, new Color("#1D2B36"));
    }
}
