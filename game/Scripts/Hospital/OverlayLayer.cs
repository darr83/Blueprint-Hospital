using System;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.UI;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Labels drawn above the world: small bubbles saying what each busy member of staff is doing, and
/// in the 3D game the obs monitor screens and the triage markers over patients who are up and about.
/// (In the 2D game the triage colours are the rings under patients, and the monitors show their own screens.)
/// </summary>
public partial class OverlayLayer : Node2D
{
    /// <summary>Below this zoom, staff bubbles are hidden to keep the view readable.</summary>
    private static float BubbleZoom => Core.GameView.ThreeD ? 0.5f : 0.65f;

    /// <summary>At most this many patients show a thought at once, so a busy waiting room doesn't turn into a wall of bubbles.</summary>
    private const int MaxThoughts = 12;

    /// <summary>Each patient's thought comes up for this many seconds...</summary>
    private const double ThoughtShows = 7;

    /// <summary>...once in this many (sooner when they are unhappy or want something).</summary>
    private const double ThoughtEvery = 40, UrgentThoughtEvery = 22;

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
        if (Core.GameView.ThreeD)
        {
            // Obs monitor screens, always visible above walls and beds.
            foreach (var monitor in _world.Monitors)
            {
                var patient = _world.ConnectedPatient(monitor);
                if (patient is null && _world.ConnectedBaby(monitor) is { } baby)
                {
                    // A baby on the neonatal unit: newborn vital signs, outlined by how poorly they are (no crash alarm: the unit looks after them).
                    var level = baby.MonitorColour;
                    Figures.ObsScreen(this, monitor.X, monitor.Y, _world.Time, baby.CurrentObservations(), Looks.TriageRing(level), level == TriageCategory.Red ? 6 : level == TriageCategory.Amber ? 5 : 0, header: baby.FirstName.ToUpperInvariant());
                    continue;
                }

                // The border follows the NEWS2 the screen shows (green 0-4, amber 5-6 or any single 3, red 7+), not the triage category.
                var outline = patient is null ? (Color?)null : patient.MonitorColour is not { } colour ? new Color("#F2F4F6") : Looks.TriageRing(colour);
                Figures.ObsScreen(this, monitor.X, monitor.Y, _world.Time, patient?.CurrentObservations(), outline, patient?.LiveNews2 ?? 0);
            }

            foreach (var patient in sim.Patients.Where(p => _world.IsOnMap(p) && !_world.IsLying(p) && _world.OnViewFloor(p)))
            {
                TriageMarker(patient, _world.FootPosition(patient) + new Vector2(0, _world.IsSeated(patient) ? -64 : -71));
            }
        }

        var zoom = GetViewport().GetCanvasTransform().X.X;
        Thoughts(zoom);
        foreach (var member in sim.Staff)
        {
            if (member.Activity == StaffActivity.Working && _world.OnViewFloor(member) && (zoom >= BubbleZoom || ReferenceEquals(_world.Selected, member)))
            {
                StaffBubble(member);
            }
        }
    }

    /// <summary>What patients are thinking, doing and wanting: a few at a time, each for a few seconds, and always the one selected.</summary>
    private void Thoughts(float zoom)
    {
        var sim = _world.Simulation;
        if (!sim.FeedbackOn)
        {
            return;
        }

        var shown = 0;
        var near = zoom >= BubbleZoom;
        foreach (var patient in sim.Patients)
        {
            var selected = ReferenceEquals(_world.Selected, patient);
            if ((!near && !selected) || !_world.IsOnMap(patient) || !_world.OnViewFloor(patient) || (shown >= MaxThoughts && !selected))
            {
                continue;
            }

            if (sim.ThoughtOf(patient) is { } thought && (selected || Showing(patient.Id, thought)))
            {
                var lift = !Core.GameView.ThreeD ? -40 : _world.IsLying(patient) ? -62 : _world.IsSeated(patient) ? -86 : -94;
                ThoughtBubble(_world.FootPosition(patient) + new Vector2(0, lift), thought);
                shown++;
            }
        }

        foreach (var outpatient in sim.Outpatients)
        {
            var selected = ReferenceEquals(_world.Selected, outpatient);
            if ((!near && !selected) || !_world.OnViewFloor(outpatient) || (shown >= MaxThoughts && !selected))
            {
                continue;
            }

            if (sim.ThoughtOf(outpatient) is { } thought && (selected || Showing(100_000 + outpatient.Id, thought)))
            {
                var lift = !Core.GameView.ThreeD ? -40 : _world.IsSeated(outpatient) ? -86 : -94;
                ThoughtBubble(_world.FootPosition(outpatient) + new Vector2(0, lift), thought);
                shown++;
            }
        }
    }

    /// <summary>Whether this person's turn to show their thought has come round (staggered by id so they don't all pop up together).</summary>
    private bool Showing(int id, Thought thought)
    {
        var every = thought.Kind is ThoughtKind.Unhappy or ThoughtKind.Wanting ? UrgentThoughtEvery : ThoughtEvery;
        var phase = (_world.Time + (id * 7.37)) % every;
        return phase < ThoughtShows;
    }

    public static Color ThoughtColour(ThoughtKind kind) => kind switch
    {
        ThoughtKind.Doing => new Color("#2F6FD0"),
        ThoughtKind.Wanting => new Color("#E08A1E"),
        ThoughtKind.Happy => new Color("#3A9B4A"),
        ThoughtKind.Unhappy => new Color("#D24B45"),
        _ => new Color("#7A8794"),
    };

    /// <summary>A rounded thought bubble with a trail of little circles down to the person, edged in the colour of the kind of thought.</summary>
    private void ThoughtBubble(Vector2 at, Thought thought)
    {
        const int size = 13;
        var font = UiStyle.Medium;
        var colour = ThoughtColour(thought.Kind);
        var text = thought.Text;
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X + 24;
        var box = new Rect2(at - new Vector2(width / 2, 24), new Vector2(width, 22));
        var fill = new Color(1, 1, 1, 0.96f);
        var style = new StyleBoxFlat
        {
            BgColor = fill, BorderColor = colour, CornerDetail = 6,
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 11, CornerRadiusTopRight = 11, CornerRadiusBottomLeft = 11, CornerRadiusBottomRight = 11,
        };
        DrawStyleBox(style, box);
        DrawCircle(box.Position + new Vector2(11, 11), 4, colour);
        DrawCircle(at + new Vector2(-3, 4), 3.2f, colour);
        DrawCircle(at + new Vector2(-3, 4), 2, fill);
        DrawCircle(at + new Vector2(-6, 10), 2, colour);
        DrawString(font, box.Position + new Vector2(19, 16), text, HorizontalAlignment.Left, -1, size, new Color("#1D2B36"));
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
        var top = foot + new Vector2(0, !Core.GameView.ThreeD ? -34 : _world.IsSeated(member) ? -78 : -84);
        var box = new Rect2(top - new Vector2(width / 2, 13), new Vector2(width, 21));

        DrawRect(box, new Color(1, 1, 1, 0.95f));
        DrawRect(new Rect2(box.Position, new Vector2(4, box.Size.Y)), new Color("#2BB3A3"));
        DrawColoredPolygon(new[] { top + new Vector2(-5, 8), top + new Vector2(5, 8), top + new Vector2(0, 14) }, new Color(1, 1, 1, 0.95f));
        DrawString(_font, box.Position + new Vector2(9, 16), text, HorizontalAlignment.Left, -1, size, new Color("#1D2B36"));
    }
}
