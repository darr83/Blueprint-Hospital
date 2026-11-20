using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.TopDown;

/// <summary>A straight stretch of wall or doorway: <see cref="Length"/> tiles from (X, Y), along x when <see cref="Horizontal"/>.</summary>
public readonly record struct WallRun(int X, int Y, int Length, bool Horizontal, bool Shell);

/// <summary>A doorway: its run, the room it belongs to (null for a way into the building) and the entrance it is, if any.</summary>
public sealed record DoorRun(WallRun Run, CareSpace? Room, Entrance? Entrance)
{
    /// <summary>The middle of the doorway, on the wall line.</summary>
    public (double X, double Y) Middle => Run.Horizontal ? (Run.X + (Run.Length / 2.0), Run.Y) : (Run.X, Run.Y + (Run.Length / 2.0));
}

/// <summary>
/// The walls of the floor in view, worked out once per rebuild from the world's wall and door
/// edges: collinear edges merged into runs, the outer shell told apart from inside walls, and each
/// run turned into the rectangle that is drawn (squared off at corners, cut clean at doorways).
/// </summary>
public sealed class WallPlan
{
    /// <summary>Thickness of an inside wall, in tiles.</summary>
    public const double InnerThickness = 0.34;

    /// <summary>Thickness of the building's outer wall, in tiles.</summary>
    public const double ShellThickness = 0.6;

    public List<WallRun> Walls { get; } = new();

    public List<DoorRun> Doors { get; } = new();

    public List<(double X, double Y, double W, double H)> InnerRects { get; } = new();

    public List<(double X, double Y, double W, double H)> ShellRects { get; } = new();

    public static WallPlan Build(WorldLayer world, HospitalSimulation sim)
    {
        var plan = new WallPlan();
        var site = sim.Site;
        var up = IsoDraw.ViewFloor * site.FloorStride;

        // The outer shell of the floor in view, and where the ways in are.
        var shell = new HashSet<(int, int, bool)>();
        var entrances = new Dictionary<(int, int, bool), Entrance>();
        foreach (var building in site.Buildings)
        {
            foreach (var (inside, _, side) in building.OuterEdges())
            {
                var edge = side switch
                {
                    WallSide.North => (inside.X, inside.Y + up, true),
                    WallSide.South => (inside.X, inside.Y + 1 + up, true),
                    WallSide.West => (inside.X, inside.Y + up, false),
                    _ => (inside.X + 1, inside.Y + up, false),
                };
                shell.Add(edge);
                if (up == 0 && building.Entrances.FirstOrDefault(e => e.Side == side && Along(e, inside)) is { } entrance)
                {
                    entrances[edge] = entrance;
                }
            }
        }

        // Which room each doorway edge belongs to: a named room before a corridor.
        var roomOf = new Dictionary<(int, int, bool), CareSpace>();
        foreach (var space in world.ViewSpaces.Where(s => s.HasWalls).OrderBy(s => s.Kind == CareSpaceKind.Corridor ? 1 : 0))
        {
            foreach (var door in space.Doors)
            {
                foreach (var edge in RoomSigns.DoorwayEdges(space.Rect, door))
                {
                    roomOf.TryAdd(edge, space);
                }
            }
        }

        var doors = world.DoorEdges.ToHashSet();
        foreach (var run in Merge(world.WallEdges, shell, null))
        {
            plan.Walls.Add(run);
            var w = run.Shell ? ShellThickness : InnerThickness;
            double a = -w / 2, b = run.Length + (w / 2);
            if (doors.Contains(run.Horizontal ? (run.X - 1, run.Y, true) : (run.X, run.Y - 1, false)))
            {
                a = 0;
            }

            if (doors.Contains(run.Horizontal ? (run.X + run.Length, run.Y, true) : (run.X, run.Y + run.Length, false)))
            {
                b = run.Length;
            }

            var rect = run.Horizontal ? (run.X + a, run.Y - (w / 2), b - a, w) : (run.X - (w / 2), run.Y + a, w, b - a);
            (run.Shell ? plan.ShellRects : plan.InnerRects).Add(rect);
        }

        // Doorways break where the room (or the entrance) changes, so two doors side by side stay two doors.
        foreach (var run in Merge(world.DoorEdges, shell, e => (roomOf.GetValueOrDefault(e), entrances.GetValueOrDefault(e))))
        {
            var first = (run.X, run.Y, run.Horizontal);
            plan.Doors.Add(new DoorRun(run, roomOf.GetValueOrDefault(first), entrances.GetValueOrDefault(first)));
        }

        return plan;
    }

    private static bool Along(Entrance e, GridCoord inside) => e.Side is WallSide.North or WallSide.South
        ? inside.Y == e.Tile.Y && inside.X >= e.Tile.X && inside.X < e.Tile.X + e.WidthTiles
        : inside.X == e.Tile.X && inside.Y >= e.Tile.Y && inside.Y < e.Tile.Y + e.WidthTiles;

    private static IEnumerable<WallRun> Merge(IReadOnlyCollection<(int X, int Y, bool Horizontal)> edges, HashSet<(int, int, bool)> shell, Func<(int, int, bool), object?>? key)
    {
        var set = edges.ToHashSet();
        var seen = new HashSet<(int, int, bool)>();
        foreach (var edge in edges.OrderBy(e => e.Horizontal).ThenBy(e => e.Horizontal ? e.Y : e.X).ThenBy(e => e.Horizontal ? e.X : e.Y))
        {
            if (!seen.Add(edge))
            {
                continue;
            }

            var isShell = shell.Contains(edge);
            var group = key?.Invoke(edge);
            var length = 1;
            while (true)
            {
                var next = edge.Horizontal ? (edge.X + length, edge.Y, true) : (edge.X, edge.Y + length, false);
                if (!set.Contains(next) || seen.Contains(next) || shell.Contains(next) != isShell || (key is not null && !Equals(key(next), group)))
                {
                    break;
                }

                seen.Add(next);
                length++;
            }

            yield return new WallRun(edge.X, edge.Y, length, edge.Horizontal, isShell);
        }
    }
}
