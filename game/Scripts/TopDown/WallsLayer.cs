using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Core;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.TopDown;

/// <summary>
/// The walls seen from above, over everything that walks about: wall tops with a darker edge, the
/// windows in the outer wall, door thresholds in each department's colour, the glass doors and
/// canopies of the ways in with their signs, and each room's name. Drawn once and kept until the
/// layout changes; the door leaves that swing are drawn by the <see cref="DoorsLayer"/>.
/// </summary>
public partial class WallsLayer : Node2D
{
    public static readonly Color WallEdge = new("#5A6877");
    private static readonly Color InnerTop = new("#F4F6F9");
    private static readonly Color ShellTop = new("#E6EAEF");
    private static readonly Color Glass = new("#A9D8EF");
    private const double EdgeWidth = 0.045;

    private HospitalSimulation _sim = null!;
    private WorldLayer _world = null!;

    public bool Shadows { get; set; } = true;

    /// <summary>Room names are left off while the player walks about as a doctor only if they ask; on by default.</summary>
    public bool ShowLabels { get; set; } = true;

    public WallPlan Plan { get; set; } = new();

    /// <summary>The words on the signs over the ways in, as last drawn.</summary>
    public List<string> SignTexts { get; } = new();

    /// <summary>How many room names were drawn.</summary>
    public int LabelCount { get; private set; }

    public static string HospitalName => GameManager.Instance?.Session?.HospitalName is { Length: > 0 } name ? name : "Hospital";

    public void Initialise(HospitalSimulation simulation, WorldLayer world)
    {
        _sim = simulation;
        _world = world;
    }

    public override void _Draw()
    {
        if (_sim is null)
        {
            return;
        }

        var p = new Pen(this, Shadows);
        SignTexts.Clear();
        LabelCount = 0;

        foreach (var (x, y, w, h) in Plan.ShellRects.Concat(Plan.InnerRects))
        {
            p.Rect(x - EdgeWidth, y - EdgeWidth, w + (2 * EdgeWidth), h + (2 * EdgeWidth), WallEdge);
        }

        foreach (var (x, y, w, h) in Plan.InnerRects)
        {
            p.Rect(x, y, w, h, InnerTop);
        }

        foreach (var (x, y, w, h) in Plan.ShellRects)
        {
            p.Rect(x, y, w, h, ShellTop);
        }

        // A soft highlight along one side of every inside wall.
        foreach (var (x, y, w, h) in Plan.InnerRects)
        {
            p.Rect(x, y, w > h ? w : 0.06, w > h ? 0.06 : h, new Color(1, 1, 1, 0.7f));
        }

        Windows(p);
        foreach (var door in Plan.Doors)
        {
            Jambs(p, door);
        }

        foreach (var door in Plan.Doors.Where(d => d.Entrance is not null))
        {
            Canopy(p, door);
        }

        if (ShowLabels)
        {
            foreach (var space in _world.ViewSpaces.Where(RoomSigns.Named))
            {
                Label(p, space);
            }
        }
    }

    private void Windows(Pen p)
    {
        foreach (var run in Plan.Walls.Where(w => w.Shell))
        {
            var n = run.Length / 6;
            for (var i = 0; i < n; i++)
            {
                var a = ((double)run.Length / n * (i + 0.5)) - 1.6;
                if (run.Horizontal)
                {
                    p.Round(run.X + a, run.Y - 0.13, 3.2, 0.26, 0.03, Glass, WallEdge, 0.03);
                    p.Line(run.X + a + 1.6, run.Y - 0.13, run.X + a + 1.6, run.Y + 0.13, WallEdge, 0.03);
                }
                else
                {
                    p.Round(run.X - 0.13, run.Y + a, 0.26, 3.2, 0.03, Glass, WallEdge, 0.03);
                    p.Line(run.X - 0.13, run.Y + a + 1.6, run.X + 0.13, run.Y + a + 1.6, WallEdge, 0.03);
                }
            }
        }
    }

    /// <summary>The colour a doorway is marked in: its room's department colour, or a plain grey.</summary>
    public static Color DoorColour(DoorRun door) => door.Room is { } room ? RoomSigns.Colour(room) : new Color("#90A4AE");

    /// <summary>The strip of floor in a doorway, under the wall line.</summary>
    public static void Threshold(Pen p, DoorRun door)
    {
        var run = door.Run;
        var w = run.Shell ? WallPlan.ShellThickness : WallPlan.InnerThickness;
        if (door.Entrance is not null)
        {
            // A dark mat well under the glass doors.
            Along(p, run, 0, -w / 2, run.Length, w, new Color("#39434E", 0.55f));
            return;
        }

        if (door.Room is { } room && RoomSigns.OpenGap(room))
        {
            return;
        }

        var colour = DoorColour(door);
        Along(p, run, 0, -w / 2, run.Length, w, Pen.Light(colour, 0.55f));
        Along(p, run, 0, -0.06, run.Length, 0.12, colour);
    }

    /// <summary>A rectangle given along and across a run: <paramref name="a"/> tiles from its start, <paramref name="c"/> tiles off the wall line.</summary>
    private static void Along(Pen p, WallRun run, double a, double c, double length, double width, Color colour)
    {
        if (run.Horizontal)
        {
            p.Rect(run.X + a, run.Y + c, length, width, colour);
        }
        else
        {
            p.Rect(run.X + c, run.Y + a, width, length, colour);
        }
    }

    private static void Jambs(Pen p, DoorRun door)
    {
        var run = door.Run;
        var w = (run.Shell ? WallPlan.ShellThickness : WallPlan.InnerThickness) + (2 * EdgeWidth);
        if (door.Room is { } room && RoomSigns.OpenGap(room) && door.Entrance is null)
        {
            return;
        }

        Along(p, run, -0.06, -w / 2, 0.12, w, WallEdge);
        Along(p, run, run.Length - 0.06, -w / 2, 0.12, w, WallEdge);
        if (door.Entrance is not null)
        {
            // Glass sliding doors, standing part open.
            var len = (double)run.Length;
            Along(p, run, 0, -0.09, len * 0.24, 0.18, Glass);
            Along(p, run, len * 0.76, -0.09, len * 0.24, 0.18, Glass);
            Along(p, run, len * 0.2, 0.06, len * 0.2, 0.14, new Color(Glass, 0.8f));
            Along(p, run, len * 0.6, 0.06, len * 0.2, 0.14, new Color(Glass, 0.8f));
        }
        else if (door.Room is { Kind: CareSpaceKind.Lift })
        {
            // A lift's steel landing doors.
            Along(p, run, 0.06, -0.08, run.Length - 0.12, 0.16, new Color("#AEB8C2"));
            Along(p, run, (run.Length / 2.0) - 0.02, -0.08, 0.04, 0.16, new Color("#5D6B79"));
        }
    }

    /// <summary>
    /// The glass canopy over a way in, with its sign along the outer edge: the hospital's name in blue
    /// over the main entrance, Accident and Emergency in red where the ambulances come.
    /// </summary>
    private void Canopy(Pen p, DoorRun door)
    {
        var entrance = door.Entrance!;
        var run = door.Run;
        (int X, int Y) outward = entrance.Side switch { WallSide.North => (0, -1), WallSide.South => (0, 1), WallSide.West => (-1, 0), _ => (1, 0) };
        var (mx, my) = door.Middle;
        var angle = FurnitureKit.FrontAngle(outward);
        var len = run.Length + 2.0;
        var y0 = WallPlan.ShellThickness / 2;

        // Is the frame upside down on screen? Then the two lines of the sign swap, so the name stays on top.
        var onScreen = Mathf.Wrap(angle + (IsoDraw.Rotation * Math.PI / 2), -Math.PI, Math.PI);
        var flipped = Math.Abs(onScreen) > (Math.PI / 2) + 0.01;

        p.Push(mx, my, angle);
        p.Shadow(c => p.Round(-len / 2, y0, len, 3.6, 0.2, c), 0.2, 0.3, 0.6f);
        p.Round(-len / 2, y0, len, 2.5, 0.1, Pen.A("#CFEAF7", 0.38f), new Color(WallEdge, 0.8f), 0.06);
        for (var i = 1; i < 5; i++)
        {
            p.Line((-len / 2) + (len * i / 5), y0, (-len / 2) + (len * i / 5), y0 + 2.5, new Color(WallEdge, 0.45f), 0.04);
        }

        var board = len + 0.6;
        var mid = y0 + 3.05;
        if (entrance.IsAmbulanceEntrance)
        {
            const string text = "Accident & Emergency";
            p.Round(-board / 2, y0 + 2.3, board, 1.5, 0.25, new Color("#C62828"), new Color("#8E1B1B"), 0.05);
            p.Text(text, 0, mid, Fit(text, 18, board - 0.6), Colors.White);
            SignTexts.Add(text);
        }
        else
        {
            var name = HospitalName;
            p.Round(-board / 2, y0 + 2.3, board, 1.5, 0.25, new Color("#1F5FBF"), new Color("#143F80"), 0.05);
            p.Text(name, 0, mid + (flipped ? 0.3 : -0.24), Fit(name, 19, board - 0.6), Colors.White);
            p.Text("MAIN ENTRANCE", 0, mid + (flipped ? -0.36 : 0.38), Fit("MAIN ENTRANCE", 11, board - 0.6), new Color(1, 1, 1, 0.85f), spacing: 1);
            SignTexts.Add(name);
            SignTexts.Add("MAIN ENTRANCE");
        }

        p.Pop();
    }

    /// <summary>The biggest text size up to <paramref name="size"/> that fits in <paramref name="tiles"/>.</summary>
    private static int Fit(string text, int size, double tiles)
    {
        while (size > 7 && Pen.TextWidth(text, size) > tiles * Pen.T)
        {
            size--;
        }

        return size;
    }

    /// <summary>A room's name on a pill just inside its wall on the side nearest the bottom of the screen.</summary>
    private void Label(Pen p, CareSpace space)
    {
        var r = space.Rect;
        if (Math.Min(r.Width, r.Length) < 3)
        {
            return;
        }

        var (cx, cy) = (r.X + (r.Width / 2.0), r.Y + (r.Length / 2.0));
        var inset = space.IsWard ? 1.6 : 0.95;
        var spots = new[]
        {
            (X: cx, Y: r.Bottom - inset, Room: (double)r.Width), (X: cx, Y: r.Y + inset, Room: r.Width),
            (X: r.Right - inset, Y: cy, Room: r.Length), (X: r.X + inset, Y: cy, Room: r.Length),
        };
        var spot = spots.MaxBy(s => IsoDraw.ToScreen(s.X, s.Y).Y);
        var text = space.Name.ToUpperInvariant();
        var size = 14;
        while (size > 9 && Pen.TextWidth(text, size) + 14 > (spot.Room - 0.5) * Pen.T)
        {
            size--;
        }

        var colour = DepartmentRenderer.WayfindingColour(space) ?? new Color("#607D8B");
        p.Pill(text, spot.X, spot.Y, size, new Color(1, 1, 1, 0.84f), new Color(colour, 0.9f), Pen.Dark(colour, 0.45f));
        LabelCount++;
    }
}

/// <summary>
/// The doors themselves: each leaf swings into its room as someone comes up to it and closes behind
/// them, with the dashed arc of its swing on the floor. Redrawn every frame.
/// </summary>
public partial class DoorsLayer : Node2D
{
    private static readonly Color Leaf = new("#D9B88A");

    /// <summary>The widest doorway that still gets door leaves, in tiles.</summary>
    public const int MaxDoorTiles = 4;

    private WorldLayer _world = null!;

    public WallPlan Plan { get; set; } = new();

    /// <summary>How many door leaves were drawn last frame.</summary>
    public int LeafCount { get; private set; }

    public void Initialise(WorldLayer world) => _world = world;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_world is null)
        {
            return;
        }

        var p = new Pen(this, false);
        LeafCount = 0;
        foreach (var door in Plan.Doors)
        {
            if (door.Room is not { } room || door.Entrance is not null || RoomSigns.OpenGap(room))
            {
                continue;
            }

            var kind = RoomSigns.KindOf(room);
            if (kind == RoomSigns.DoorKind.None)
            {
                continue;
            }

            var run = door.Run;
            var r = room.Rect;
            var into = run.Horizontal ? (run.Y == r.Y ? 1 : -1) : (run.X == r.X ? 1 : -1);
            var (mx, my) = door.Middle;
            var open = _world.DoorOpenness(room, mx, my);
            if (run.Length > MaxDoorTiles)
            {
                // A wide opening (a counter front, an open bay) has no door across it.
                continue;
            }

            var leaves = (kind == RoomSigns.DoorKind.Double && run.Length >= 2) || run.Length > 2 ? 2 : 1;
            var length = (double)run.Length / leaves;
            Swing(p, run, run.X, run.Y, 1, into, length, open);
            if (leaves == 2)
            {
                Swing(p, run, run.Horizontal ? run.X + run.Length : run.X, run.Horizontal ? run.Y : run.Y + run.Length, -1, into, length, open);
            }
        }
    }

    /// <summary>One leaf hinged at (hx, hy), closed along the wall in direction <paramref name="along"/>, swinging into the room.</summary>
    private void Swing(Pen p, WallRun run, double hx, double hy, int along, int into, double length, float open)
    {
        var closed = run.Horizontal ? (along > 0 ? 0 : Math.PI) : (along > 0 ? Math.PI / 2 : -Math.PI / 2);
        var turn = (run.Horizontal ? 1 : -1) * along * into;
        var a = closed + (turn * 1.4 * open);
        if (open > 0.02f)
        {
            p.Arc(hx, hy, length, Math.Min(closed, a), Math.Max(closed, a), new Color("#22303C", 0.25f), 1.0 / Pen.T, dashed: true);
        }

        var (ex, ey) = (hx + (Math.Cos(a) * length), hy + (Math.Sin(a) * length));
        p.Line(hx, hy, ex, ey, Pen.Outline(Leaf), 0.17);
        p.Line(hx + (Math.Cos(a) * 0.03), hy + (Math.Sin(a) * 0.03), hx + (Math.Cos(a) * (length - 0.03)), hy + (Math.Sin(a) * (length - 0.03)), Leaf, 0.11);
        p.Circle(hx, hy, 0.07, WallsLayer.WallEdge);
        LeafCount++;
    }
}
