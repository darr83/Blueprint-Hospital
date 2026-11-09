using System;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Wet patches on the floor where a patient couldn't get to a toilet in time, each with a yellow
/// "wet floor" sign beside it, until the cleaners have mopped up.
/// </summary>
public partial class AccidentLayer : Node2D
{
    private static readonly Color Puddle = new(0.95f, 0.88f, 0.45f, 0.4f);
    private static readonly Color Shine = new(1f, 1f, 1f, 0.35f);
    private static readonly Color SignYellow = new("#FBC02D");

    private HospitalSimulation _sim = null!;

    public void Initialise(HospitalSimulation simulation) => _sim = simulation;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_sim is null)
        {
            return;
        }

        foreach (var accident in _sim.FloorAccidents)
        {
            if (!IsoDraw.OnViewFloor(accident.Tile.Y))
            {
                continue;
            }

            var (cx, cy) = (accident.Tile.X + 0.5, accident.Tile.Y + 0.5);
            var seed = Math.Abs((accident.Tile.X * 31) + (accident.Tile.Y * 17));
            Blob(cx, cy, 0.7, seed, Puddle);
            Blob(cx + 0.35, cy - 0.25, 0.35, seed + 3, Puddle);
            Blob(cx - 0.15, cy - 0.1, 0.18, seed + 7, Shine);
            Sign(cx + 0.9, cy + 0.6);
        }
    }

    /// <summary>A lumpy flat patch of the given radius (in tiles).</summary>
    private void Blob(double cx, double cy, double radius, int seed, Color colour)
    {
        var points = new Vector2[24];
        for (var i = 0; i < points.Length; i++)
        {
            var a = i * Math.PI * 2 / points.Length;
            var r = radius * (0.82 + (0.12 * Math.Sin(seed + (a * 2))) + (0.06 * Math.Sin((seed * 0.7) + (a * 3))));
            points[i] = IsoDraw.ToScreen(cx + (Math.Cos(a) * r), cy + (Math.Sin(a) * r * 0.8));
        }

        DrawColoredPolygon(points, colour);
    }

    /// <summary>A yellow A-frame "caution: wet floor" sign.</summary>
    private void Sign(double x, double y)
    {
        var foot = IsoDraw.ToScreen(x, y);
        var height = 0.6f * IsoDraw.PixelsPerMetre;
        var half = 5f;
        DrawColoredPolygon(new[] { foot + new Vector2(-half - 2, 2), foot + new Vector2(-half + 4, 3), foot + new Vector2(half + 4, 3), foot + new Vector2(half + 2, 2) }, new Color(0, 0, 0, 0.15f));
        DrawColoredPolygon(new[] { foot + new Vector2(-half, 0), foot + new Vector2(half, 0), foot + new Vector2(half * 0.45f, -height), foot + new Vector2(-half * 0.45f, -height) }, SignYellow);
        DrawColoredPolygon(new[] { foot + new Vector2(-half, 0), foot + new Vector2(-half + 1.5f, 0), foot + new Vector2(-half * 0.45f + 1, -height), foot + new Vector2(-half * 0.45f, -height) }, SignYellow.Darkened(0.2f));

        // The warning triangle and a stick figure slipping, in black.
        var t = foot + new Vector2(0, -height * 0.62f);
        DrawColoredPolygon(new[] { t + new Vector2(-3, 2.5f), t + new Vector2(3, 2.5f), t + new Vector2(0, -3) }, new Color("#212121"));
        DrawColoredPolygon(new[] { t + new Vector2(-1.8f, 1.6f), t + new Vector2(1.8f, 1.6f), t + new Vector2(0, -1.6f) }, SignYellow);
        DrawLine(foot + new Vector2(-3, -height * 0.25f), foot + new Vector2(3, -height * 0.25f), new Color("#212121"), 1f);
    }
}
