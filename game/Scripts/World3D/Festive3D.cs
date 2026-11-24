using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// The seasonal decorations in 3D (see <see cref="SeasonalDecor"/>): a Christmas tree in a pot with
/// baubles and a star, tinsel and bunting looped along the walls, carved pumpkins, a basket of Easter
/// eggs. Built as one mesh, again only when the decorations, the layout, the floor or the cutaway change.
/// </summary>
public partial class Festive3D : Node3D
{
    private HospitalSimulation _sim = null!;
    private WorldLayer _world = null!;
    private (string Kind, int Version, int Floor, bool Cutaway, string Outbreaks, string NotInUse)? _built;

    /// <summary>How many red "Not in use" signs were built last time (for checks).</summary>
    public int NotInUseSignCount { get; private set; }

    /// <summary>How many norovirus signs were built last time (for checks).</summary>
    public int OutbreakSignCount { get; private set; }

    /// <summary>How many trees, pumpkins and baskets were built last time, and strings of bunting (for checks).</summary>
    public int PieceCount { get; private set; }

    public int RunCount { get; private set; }

    public void Initialise(HospitalSimulation simulation, WorldLayer world)
    {
        _sim = simulation;
        _world = world;
    }

    private static float Tiles(double metres) => (float)(metres / Units.MetresPerTile);

    /// <summary>Rebuilds if anything that changes the decorations has changed.</summary>
    public void Sync(bool cutaway)
    {
        var key = (SeasonalDecor.Current(_sim), _world.StaticVersion, IsoDraw.ViewFloor, cutaway, OutbreakSigns.Key(_sim), NotInUseSigns.Key(_sim));
        if (key == _built)
        {
            return;
        }

        _built = key;
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }

        var (pieces, runs) = SeasonalDecor.Plan(_sim, key.Item1);
        var b = new MeshBatch();

        // Bunting hangs a little under the top of the walls (or of the cut-down walls).
        var height = cutaway ? Kit3D.H(12) * 0.8f : Walls3D.FullHeight * 0.84f;
        RunCount = 0;
        foreach (var run in runs.Where(r => IsoDraw.OnViewFloor(r.Y1)))
        {
            Run(b, run, height);
            RunCount++;
        }

        PieceCount = 0;
        foreach (var piece in pieces.Where(p => IsoDraw.OnViewFloor(p.Y)))
        {
            var at = Kit3D.At(piece.X, piece.Y);
            switch (piece.Kind)
            {
                case "tree":
                    Tree(b, at);
                    break;
                case "pumpkin":
                    Pumpkin(b, at, piece.Seed);
                    break;
                default:
                    Eggs(b, at);
                    break;
            }

            PieceCount++;
        }

        OutbreakSignCount = 0;
        foreach (var sign in OutbreakSigns.Plan(_sim).Where(s => IsoDraw.OnViewFloor(s.Y)))
        {
            OutbreakSign(b, sign);
            OutbreakSignCount++;
        }

        NotInUseSignCount = 0;
        foreach (var sign in NotInUseSigns.Plan(_sim).Where(s => IsoDraw.OnViewFloor(s.Y)))
        {
            NotInUseSign(b, sign);
            NotInUseSignCount++;
        }

        b.AddTo(this);
    }

    /// <summary>
    /// A yellow A-frame floor sign outside a ward with norovirus, about 0.9 m tall, its two leaves leaning
    /// together. The front leaf faces the corridor and carries the words (red-banded once the ward is closed).
    /// </summary>
    private void OutbreakSign(MeshBatch b, OutbreakSign sign)
    {
        var at = Kit3D.At(sign.X, sign.Y);
        var outward = new Vector3((float)sign.Facing.X, 0, (float)sign.Facing.Y);
        var yaw = Mathf.Atan2(outward.X, outward.Z);
        const float Lean = 0.2f;
        var height = Kit3D.M(0.9);
        var width = Tiles(0.6);
        var depth = Tiles(0.02);
        var spread = Tiles(0.9 * Math.Sin(Lean) / 2);
        var yellow = OutbreakSigns.Yellow.Lightened(0.15f);
        foreach (var face in new[] { 1f, -1f })
        {
            // Each leaf leans in towards the other, meeting at the top.
            var basis = Basis.FromEuler(new Vector3(-face * Lean, yaw, 0));
            var centre = at + (outward * (face * spread * 0.5f)) + (Vector3.Up * (height / 2));
            b.Box(centre, new Vector3(width, height, depth), basis, face > 0 ? yellow : yellow.Darkened(0.15f));
        }

        // The hinge along the top.
        b.Box(at + (Vector3.Up * height), new Vector3(width, Kit3D.M(0.03), Tiles(0.04)), Basis.FromEuler(new Vector3(0, yaw, 0)), yellow.Darkened(0.35f));

        // The words on the front leaf, leaning with it.
        var front = Basis.FromEuler(new Vector3(-Lean, yaw, 0));
        var normal = front * Vector3.Back;
        var middle = at + (outward * (spread * 0.5f)) + (Vector3.Up * (height * 0.55f)) + (normal * (depth / 2 + 0.01f));
        if (sign.Closed)
        {
            b.Box(middle + (front * new Vector3(0, Kit3D.M(0.2), 0)), new Vector3(width * 0.9f, Kit3D.M(0.12), 0.004f), front, OutbreakSigns.Red);
        }

        var heading = new Label3D
        {
            Text = OutbreakSigns.Heading(sign),
            FontSize = 34,
            PixelSize = 0.0042f,
            Modulate = sign.Closed ? Colors.White : OutbreakSigns.Ink,
            OutlineSize = 0,
            Position = middle + (front * new Vector3(0, Kit3D.M(0.2), 0)) + (normal * 0.008f),
            Basis = front,
            DoubleSided = false,
        };
        var body = new Label3D
        {
            Text = OutbreakSigns.Body(sign),
            FontSize = 22,
            PixelSize = 0.0042f,
            Modulate = OutbreakSigns.Ink,
            OutlineSize = 0,
            Position = middle - (front * new Vector3(0, Kit3D.M(0.12), 0)) + (normal * 0.008f),
            Basis = front,
            DoubleSided = false,
        };
        AddChild(heading);
        AddChild(body);
    }

    /// <summary>
    /// A red A-frame floor sign in the middle of a room that isn't joined up to a corridor yet, with "NOT IN USE"
    /// on both leaves, and the same words floating above it so they read from any angle and at any zoom.
    /// </summary>
    private void NotInUseSign(MeshBatch b, NotInUseSign sign)
    {
        var at = Kit3D.At(sign.X, sign.Y);
        var outward = new Vector3((float)sign.Facing.X, 0, (float)sign.Facing.Y);
        var yaw = Mathf.Atan2(outward.X, outward.Z);
        const float Lean = 0.2f;
        var height = Kit3D.M(1.0);
        var width = Tiles(0.75);
        var depth = Tiles(0.02);
        var spread = Tiles(1.0 * Math.Sin(Lean) / 2);
        var red = NotInUseSigns.Red;
        foreach (var face in new[] { 1f, -1f })
        {
            // The words face out from each leaf: the back leaf's words are the front leaf's turned round.
            var basis = Basis.FromEuler(new Vector3(-Lean, yaw + (face > 0 ? 0 : Mathf.Pi), 0));
            var leafBasis = Basis.FromEuler(new Vector3(-face * Lean, yaw, 0));
            var centre = at + (outward * (face * spread * 0.5f)) + (Vector3.Up * (height / 2));
            b.Box(centre, new Vector3(width, height, depth), leafBasis, face > 0 ? red : red.Darkened(0.12f));

            // A white band with the words, on the outside of each leaf.
            var normal = (leafBasis * Vector3.Back) * face;
            var middle = centre + (Vector3.Up * Kit3D.M(0.12)) + (normal * (depth / 2 + 0.004f));
            b.Box(middle, new Vector3(width * 0.88f, Kit3D.M(0.2), 0.004f), leafBasis, Colors.White);
            AddChild(new Label3D
            {
                Text = NotInUseSigns.Text,
                FontSize = 30,
                PixelSize = 0.0042f,
                Modulate = red,
                OutlineSize = 0,
                Position = middle + (normal * 0.006f),
                Basis = basis,
                DoubleSided = false,
            });
            AddChild(new Label3D
            {
                Text = NotInUseSigns.Body,
                FontSize = 20,
                PixelSize = 0.0042f,
                Modulate = Colors.White,
                OutlineSize = 0,
                Position = centre - (Vector3.Up * Kit3D.M(0.2)) + (normal * (depth / 2 + 0.008f)),
                Basis = basis,
                DoubleSided = false,
            });
        }

        b.Box(at + (Vector3.Up * height), new Vector3(width, Kit3D.M(0.03), Tiles(0.04)), Basis.FromEuler(new Vector3(0, yaw, 0)), red.Darkened(0.35f));

        // The words again, floating over the room and always turned to the camera.
        AddChild(new Label3D
        {
            Text = NotInUseSigns.Text,
            FontSize = 64,
            PixelSize = 0.013f,
            Modulate = Colors.White,
            OutlineModulate = red,
            OutlineSize = 30,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            RenderPriority = 10,
            OutlineRenderPriority = 9,
            Position = at + (Vector3.Up * (height + Kit3D.M(0.9))),
        });
    }

    private static void Run(MeshBatch b, DecorRun run, float height)
    {
        var from = Kit3D.At(run.X1, run.Y1, height);
        var to = Kit3D.At(run.X2, run.Y2, height);
        var length = (to - from).Length();
        var along = (to - from) / length;

        // Into the room: the run has the room on its left.
        var inward = new Vector3((float)-(run.Y2 - run.Y1), 0, (float)(run.X2 - run.X1)).Normalized();
        var loops = Math.Max(1, (int)Math.Round(length / Tiles(1.6)));
        var sag = Kit3D.M(0.18);

        Vector3 Point(float t)
        {
            // Swags between hooks on the wall.
            var local = (t * loops) % 1f;
            var dip = sag * 4 * local * (1 - local);
            return from + (along * (length * t)) - (Vector3.Up * dip) + (inward * Tiles(0.03));
        }

        var steps = loops * 8;
        for (var i = 0; i < steps; i++)
        {
            var a = Point(i / (float)steps);
            var c = Point((i + 1) / (float)steps);
            var colour = run.Colours[(i / 2) % run.Colours.Count];
            if (run.Tinsel)
            {
                b.Rod(a, c, Tiles(0.05), colour, small: true);
            }
            else
            {
                b.Rod(a, c, Tiles(0.01), new Color("#F4F1EA"), small: true);
                if (i % 2 == 0)
                {
                    // A little triangular flag hanging from the cord.
                    var mid = (a + c) / 2;
                    b.Tri(a, c, mid - (Vector3.Up * Kit3D.M(0.22)) + (inward * Tiles(0.01)), colour);
                }
            }
        }
    }

    private static void Tree(MeshBatch b, Vector3 at)
    {
        var pot = new Color("#B23A3A");
        b.Cylinder(at + (Vector3.Up * Kit3D.M(0.15)), Tiles(0.2), Kit3D.M(0.3), Tiles(0.2), pot);
        b.Cylinder(at + (Vector3.Up * Kit3D.M(0.32)), Tiles(0.22), Kit3D.M(0.04), Tiles(0.22), pot.Darkened(0.15f));
        b.Cylinder(at + (Vector3.Up * Kit3D.M(0.4)), Tiles(0.05), Kit3D.M(0.2), Tiles(0.05), new Color("#6B4A2E"));
        var greens = new[] { new Color("#1E5E33"), new Color("#256C3B"), new Color("#2E7D45") };
        var tiers = new[] { (Base: 0.45, Height: 0.75, Radius: 0.5), (Base: 0.85, Height: 0.65, Radius: 0.4), (Base: 1.2, Height: 0.6, Radius: 0.28) };
        for (var i = 0; i < tiers.Length; i++)
        {
            var (bottom, h, r) = tiers[i];
            b.Cylinder(at + (Vector3.Up * Kit3D.M(bottom + (h / 2))), Tiles(r), Kit3D.M(h), Tiles(r), greens[i], cone: true);
        }

        var baubles = new[] { new Color("#C62828"), new Color("#F2C230"), new Color("#1E63B5"), new Color("#D9DEE3") };
        for (var i = 0; i < 14; i++)
        {
            // Spread round the cone, a few on each tier.
            var t = (i % 7) / 7.0;
            var height = 0.55 + (t * 1.1);
            var radius = 0.46 * (1 - ((height - 0.45) / 1.45)) + 0.03;
            var angle = (i * 2.4) + (t * 1.3);
            var p = at + new Vector3(Mathf.Cos((float)angle) * Tiles(radius), Kit3D.M(height), Mathf.Sin((float)angle) * Tiles(radius));
            b.Ball(p, Vector3.One * Tiles(0.07), baubles[i % baubles.Length], small: true);
        }

        // The star on top.
        var star = at + (Vector3.Up * Kit3D.M(1.86));
        b.Box(star, new Vector3(Tiles(0.14), Kit3D.M(0.14), Tiles(0.03)), Basis.FromEuler(new Vector3(0, 0, Mathf.Pi / 4)), new Color("#F5C518"));
        b.Box(star, new Vector3(Tiles(0.03), Kit3D.M(0.14), Tiles(0.14)), Basis.FromEuler(new Vector3(Mathf.Pi / 4, 0, 0)), new Color("#F5C518"));
    }

    private static void Pumpkin(MeshBatch b, Vector3 at, int seed)
    {
        var size = 0.32 + ((seed % 3) * 0.05);
        var orange = new Color("#F57C00");
        var middle = at + (Vector3.Up * Kit3D.M(size * 0.38));
        b.Ball(middle, new Vector3(Tiles(size), Kit3D.M(size * 0.75), Tiles(size)), orange);
        for (var i = 0; i < 4; i++)
        {
            // Ribs: slightly darker lobes round the middle.
            var a = i * Mathf.Pi / 4;
            b.Ball(middle, new Vector3(Tiles(size * 1.04), Kit3D.M(size * 0.7), Tiles(size * 0.55)), Basis.FromEuler(new Vector3(0, a, 0)), orange.Darkened(0.08f));
        }

        b.Cylinder(middle + (Vector3.Up * Kit3D.M(size * 0.42)), Tiles(0.025), Kit3D.M(0.08), Tiles(0.025), new Color("#5D7A2E"));

        // A carved face towards the room: two eyes and a grin.
        var face = middle + new Vector3(0, 0, Tiles(size * 0.5));
        var dark = new Color("#3B1F05");
        b.Box(face + new Vector3(-Tiles(size * 0.18), Kit3D.M(size * 0.12), 0), new Vector3(Tiles(size * 0.14), Kit3D.M(size * 0.12), Tiles(0.02)), dark);
        b.Box(face + new Vector3(Tiles(size * 0.18), Kit3D.M(size * 0.12), 0), new Vector3(Tiles(size * 0.14), Kit3D.M(size * 0.12), Tiles(0.02)), dark);
        b.Box(face + new Vector3(0, -Kit3D.M(size * 0.12), 0), new Vector3(Tiles(size * 0.45), Kit3D.M(size * 0.08), Tiles(0.02)), dark);
    }

    private static void Eggs(MeshBatch b, Vector3 at)
    {
        var wicker = new Color("#B58450");
        b.Cylinder(at + (Vector3.Up * Kit3D.M(0.08)), Tiles(0.22), Kit3D.M(0.16), Tiles(0.22), wicker);
        b.Ring(at + (Vector3.Up * Kit3D.M(0.16)), Tiles(0.22), wicker.Darkened(0.15f), flatten: 0.6f);
        b.Ring(at + (Vector3.Up * Kit3D.M(0.2)), Tiles(0.18), wicker.Darkened(0.1f), Basis.FromEuler(new Vector3(Mathf.Pi / 2, 0, 0)));
        b.Cylinder(at + (Vector3.Up * Kit3D.M(0.165)), Tiles(0.19), Kit3D.M(0.01), Tiles(0.19), new Color("#C7E3A0"));
        var colours = SeasonalDecor.EasterColours;
        for (var i = 0; i < 5; i++)
        {
            var a = i * Mathf.Tau / 5;
            var p = at + new Vector3(Mathf.Cos(a) * Tiles(0.1), Kit3D.M(0.2), Mathf.Sin(a) * Tiles(0.1));
            b.Ball(p, new Vector3(Tiles(0.07), Kit3D.M(0.09), Tiles(0.07)), colours[i], small: true);
        }
    }
}
