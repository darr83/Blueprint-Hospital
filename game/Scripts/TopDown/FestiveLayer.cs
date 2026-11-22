using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.TopDown;

/// <summary>
/// The seasonal decorations from above (see <see cref="SeasonalDecor"/>): a Christmas tree with baubles
/// and a star, tinsel round the walls, pumpkins, bunting and a basket of Easter eggs. Drawn over the
/// furniture and under the people, and only redrawn when the decorations or the layout change.
/// </summary>
public partial class FestiveLayer : Node2D
{
    private HospitalSimulation _sim = null!;
    private WorldLayer _world = null!;
    private (string Kind, int Version, int Floor, int Rotation, string Outbreaks)? _drawn;

    /// <summary>How many norovirus signs were drawn last time (for checks).</summary>
    public int OutbreakSignCount { get; private set; }

    public bool Shadows { get; set; } = true;

    /// <summary>How many trees, pumpkins and baskets were drawn last time, and strings of bunting (for checks).</summary>
    public int PieceCount { get; private set; }

    public int RunCount { get; private set; }

    public string Kind { get; private set; } = string.Empty;

    public void Initialise(HospitalSimulation simulation, WorldLayer world)
    {
        _sim = simulation;
        _world = world;
    }

    public override void _Process(double delta)
    {
        var key = (SeasonalDecor.Current(_sim), _world.StaticVersion, IsoDraw.ViewFloor, IsoDraw.Rotation, OutbreakSigns.Key(_sim));
        if (key != _drawn)
        {
            _drawn = key;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_sim is null)
        {
            return;
        }

        Kind = SeasonalDecor.Current(_sim);
        var (pieces, runs) = SeasonalDecor.Plan(_sim, Kind);
        var p = new Pen(this, Shadows);
        RunCount = 0;
        foreach (var run in runs.Where(r => IsoDraw.OnViewFloor(r.Y1)))
        {
            Run(p, run);
            RunCount++;
        }

        PieceCount = 0;
        foreach (var piece in pieces.Where(x => IsoDraw.OnViewFloor(x.Y)))
        {
            switch (piece.Kind)
            {
                case "tree":
                    Tree(p, piece.X, piece.Y);
                    break;
                case "pumpkin":
                    Pumpkin(p, piece.X, piece.Y, piece.Seed);
                    break;
                default:
                    Eggs(p, piece.X, piece.Y);
                    break;
            }

            PieceCount++;
        }

        OutbreakSignCount = 0;
        foreach (var sign in OutbreakSigns.Plan(_sim).Where(s => IsoDraw.OnViewFloor(s.Y)))
        {
            OutbreakSign(p, sign);
            OutbreakSignCount++;
        }
    }

    /// <summary>A yellow A-frame floor sign from above: its two leaves meeting at the top, with a warning mark and the words on the face.</summary>
    private static void OutbreakSign(Pen p, OutbreakSign sign)
    {
        // About 0.6 m wide and 0.4 m deep where it stands (a tile is half a metre).
        var (fx, fy) = sign.Facing;
        var (ax, ay) = (Math.Abs(fy), Math.Abs(fx));
        const double W = 0.62;
        const double D = 0.42;
        (double, double) At(double along, double outward) => (sign.X + (ax * along) + (fx * outward), sign.Y + (ay * along) + (fy * outward));
        p.Shadow(c => p.Poly(c, null, 0, At(-W, -D), At(W, -D), At(W, D), At(-W, D)), 0.08, 0.1, 1f);
        var yellow = OutbreakSigns.Yellow;
        p.Poly(Pen.Dark(yellow, 0.12f), Pen.Dark(yellow, 0.45f), 0.03, At(-W, -D), At(W, -D), At(W, 0), At(-W, 0));
        p.Poly(yellow, Pen.Dark(yellow, 0.45f), 0.03, At(-W, 0), At(W, 0), At(W, D), At(-W, D));
        var (rx, ry) = At(-W, 0);
        var (sx, sy) = At(W, 0);
        p.Line(rx, ry, sx, sy, Pen.Dark(yellow, 0.5f), 0.04);

        // A red band across the face when the ward is closed, and a warning triangle.
        if (sign.Closed)
        {
            p.Poly(OutbreakSigns.Red, null, 0, At(-W + 0.06, D * 0.55), At(W - 0.06, D * 0.55), At(W - 0.06, D * 0.85), At(-W + 0.06, D * 0.85));
        }

        p.Poly(OutbreakSigns.Ink, null, 0, At(-0.14, D * 0.12), At(0.14, D * 0.12), At(0, D * 0.42));
        var (tx, ty) = At(0, D * 0.25);
        p.Circle(tx, ty, 0.035, yellow);
    }

    private static void Run(Pen p, DecorRun run)
    {
        var length = Math.Sqrt(Math.Pow(run.X2 - run.X1, 2) + Math.Pow(run.Y2 - run.Y1, 2));
        var (dx, dy) = ((run.X2 - run.X1) / length, (run.Y2 - run.Y1) / length);

        // Inward from the wall: the side of the line the room is on.
        var (nx, ny) = (-dy, dx);
        if (run.Tinsel)
        {
            // Tinsel: short, thick, sparkly lengths in turn.
            var n = (int)Math.Ceiling(length / 0.25);
            for (var i = 0; i < n; i++)
            {
                var a = i * 0.25;
                var b = Math.Min(length, a + 0.25);
                var c = run.Colours[i % run.Colours.Count];
                p.Line(run.X1 + (dx * a), run.Y1 + (dy * a), run.X1 + (dx * b), run.Y1 + (dy * b), c, 0.09);
                p.Circle(run.X1 + (dx * (a + 0.12)), run.Y1 + (dy * (a + 0.12)), 0.035, Pen.Light(c, 0.5f));
            }

            return;
        }

        // Bunting: a cord with little triangular flags hanging off it.
        p.Line(run.X1, run.Y1, run.X2, run.Y2, new Color("#F4F1EA"), 0.025);
        var flags = (int)Math.Floor(length / 0.3);
        for (var i = 0; i < flags; i++)
        {
            var a = (i * 0.3) + 0.04;
            var (x, y) = (run.X1 + (dx * a), run.Y1 + (dy * a));
            var c = run.Colours[i % run.Colours.Count];
            p.Poly(c, null, 0, (x, y), (x + (dx * 0.22), y + (dy * 0.22)), (x + (dx * 0.11) + (nx * 0.2), y + (dy * 0.11) + (ny * 0.2)));
        }
    }

    /// <summary>A Christmas tree from above: tiers of branches, baubles and a gold star on top.</summary>
    private static void Tree(Pen p, double x, double y)
    {
        // About 1.1 metres across (a tile is half a metre).
        const double K = 2.3;
        p.Shadow(c => p.Circle(x, y, 0.46 * K, c), 0.14 * K, 0.18 * K, 1.2f);
        var greens = new[] { new Color("#1E5E33"), new Color("#2A7A43"), new Color("#379152") };
        var radii = new[] { 0.46, 0.33, 0.2 };
        for (var t = 0; t < 3; t++)
        {
            var points = Enumerable.Range(0, 16).Select(i =>
            {
                var a = i * Math.PI / 8;
                var r = radii[t] * K * (i % 2 == 0 ? 1 : 0.8);
                return (x + (Math.Cos(a) * r), y + (Math.Sin(a) * r));
            }).ToArray();
            p.Poly(greens[t], Pen.Dark(greens[t], 0.25f), 0.02 * K, points);
        }

        var baubles = new[] { new Color("#C62828"), new Color("#F2C230"), new Color("#1E63B5"), new Color("#C0C7CE") };
        for (var i = 0; i < 9; i++)
        {
            var a = i * 2.4;
            var r = (0.14 + ((i % 3) * 0.1)) * K;
            p.Circle(x + (Math.Cos(a) * r), y + (Math.Sin(a) * r), 0.045 * K, baubles[i % baubles.Length], Pen.Dark(baubles[i % baubles.Length], 0.3f), 0.01);
        }

        // The star.
        var star = Enumerable.Range(0, 10).Select(i =>
        {
            var a = (i * Math.PI / 5) - (Math.PI / 2);
            var r = (i % 2 == 0 ? 0.11 : 0.045) * K;
            return (x + (Math.Cos(a) * r), y + (Math.Sin(a) * r));
        }).ToArray();
        p.Poly(new Color("#F5C518"), new Color("#B8901A"), 0.01, star);
    }

    /// <summary>A carved pumpkin from above: ribbed orange with a green stalk.</summary>
    private static void Pumpkin(Pen p, double x, double y, int seed)
    {
        var r = (0.22 + ((seed % 3) * 0.03)) * 1.6;
        p.Shadow(c => p.Circle(x, y, r, c), 0.08, 0.1);
        var orange = new Color("#F57C00");
        p.Circle(x, y, r, orange, Pen.Dark(orange, 0.3f), 0.02);
        for (var i = 0; i < 6; i++)
        {
            var a = i * Math.PI / 3;
            p.Line(x + (Math.Cos(a) * r * 0.25), y + (Math.Sin(a) * r * 0.25), x + (Math.Cos(a) * r * 0.95), y + (Math.Sin(a) * r * 0.95), Pen.Dark(orange, 0.2f), 0.02);
        }

        p.Circle(x, y, r * 0.22, new Color("#5D7A2E"), new Color("#3E5320"), 0.01);
    }

    /// <summary>A wicker basket of Easter eggs from above.</summary>
    private static void Eggs(Pen p, double x, double y)
    {
        const double K = 1.4;
        p.Shadow(c => p.Circle(x, y, 0.3 * K, c), 0.08, 0.1);
        p.Circle(x, y, 0.3 * K, new Color("#B58450"), new Color("#7A5530"), 0.03);
        p.Circle(x, y, 0.24 * K, new Color("#C7E3A0"));
        var colours = SeasonalDecor.EasterColours;
        for (var i = 0; i < 5; i++)
        {
            var a = i * Math.PI * 2 / 5;
            p.Ellipse(x + (Math.Cos(a) * 0.12 * K), y + (Math.Sin(a) * 0.12 * K), 0.07 * K, 0.095 * K, colours[i], Pen.Dark(colours[i], 0.25f), 0.01, a);
        }
    }
}
