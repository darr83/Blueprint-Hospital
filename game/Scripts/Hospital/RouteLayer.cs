using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Shows where people are walking: a dotted line along each walker's route to a ring at their
/// destination. Patients' routes are in their triage colour (grey before triage), staff routes in
/// their uniform colour. Turned on with the Routes button (R); the selected person's route always shows.
/// </summary>
public partial class RouteLayer : Node2D
{
    private WorldLayer _world = null!;

    /// <summary>Show every walker's route, not just the selected person's.</summary>
    public bool ShowAll { get; set; }

    public void Initialise(WorldLayer world) => _world = world;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_world is null)
        {
            return;
        }

        var sim = _world.Simulation;
        IEnumerable<Agent> walkers = ShowAll
            ? sim.Patients.Where(p => _world.IsOnMap(p)).Cast<Agent>().Concat(sim.Staff)
            : _world.Selected is { } selected ? new[] { selected } : Enumerable.Empty<Agent>();

        foreach (var agent in walkers.Where(a => a.IsMoving))
        {
            DrawRoute(agent, ColourOf(agent), ReferenceEquals(agent, _world.Selected));
        }
    }

    private void DrawRoute(Agent agent, Color colour, bool selected)
    {
        var points = new List<Vector2> { _world.FootPosition(agent) };
        points.AddRange(agent.RemainingPath.Select(t => IsoDraw.ToScreen(t.X + 0.5, t.Y + 0.5)));
        if (points.Count < 2)
        {
            return;
        }

        var width = selected ? 4.5f : 3f;
        var line = new Color(colour, selected ? 1f : 0.85f);

        // Dashes along the route, so it reads as "going this way" rather than a wall or a band.
        const float dash = 10f, gap = 7f;
        var carry = 0f;
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var length = a.DistanceTo(b);
            var dir = length > 0 ? (b - a) / length : Vector2.Zero;
            var pos = -carry;
            while (pos < length)
            {
                var start = Mathf.Max(pos, 0f);
                var end = Mathf.Min(pos + dash, length);
                if (end > start)
                {
                    DrawLine(a + (dir * start), a + (dir * end), line, width, true);
                }

                pos += dash + gap;
            }

            carry = pos - length;
        }

        // Destination: a ring on the floor.
        var target = points[^1];
        var ring = IsoDraw.FloorEllipse(target, 14, 7);
        DrawColoredPolygon(ring, new Color(colour, 0.25f));
        DrawPolyline(ring.Append(ring[0]).ToArray(), line, width * 0.8f, true);
    }

    /// <summary>Uniform colour, with white uniforms (radiographers, pharmacists) shown in slate so the line is visible.</summary>
    private static Color StaffColour(StaffMember member)
    {
        var top = Looks.Staff(member).Top;
        return top.Luminance > 0.85f ? new Color("#5A6B7A") : top;
    }

    private static Color ColourOf(Agent agent) => agent switch
    {
        Patient { Triage: { } triage } => Looks.Blanket(triage),
        Patient => new Color("#4A5A68"),
        StaffMember member => StaffColour(member),
        _ => Colors.White,
    };
}
