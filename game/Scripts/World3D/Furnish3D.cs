using System;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// The 3D furniture kit. Every builder works in the item's own frame: the origin is the middle of
/// its footprint on the floor, +z is its front (the side people use), x runs across it, and
/// <c>w</c> × <c>d</c> is its size across and front to back, in tiles. Heights are world units.
/// </summary>
public static partial class Furnish3D
{
    private static readonly Color Steel = new("#C3CCD3");
    private static readonly Color Chrome = new("#B4BEC6");
    private static readonly Color Plinth = new("#55616B");
    private static readonly Color White = new("#F4F7F9");
    private static readonly Color Ink = new("#22272B");
    private static readonly Color Screen = new("#24425E");
    private static readonly Color ScreenGlow = new("#4FC3F7");
    private static readonly Color Tyre = new("#22272B");

    private static readonly Color[] BoxColours =
    {
        new("#E57373"), new("#64B5F6"), new("#FFF176"), new("#81C784"), new("#BA68C8"), new("#FFB74D"), new("#F5F5F5"),
    };

    /// <summary>The Blender-made kit of key equipment (tools/Blender/models/equipment_kit.py), in world units.</summary>
    internal const string Equipment3D = "equipment_kit";

    private static float H(double px) => Kit3D.H(px);

    /// <summary>A box by its footprint middle (x, z), size, bottom height and height.</summary>
    private static void Blk(MeshBatch b, float x, float z, float w, float d, float y0, float h, Color c) =>
        b.Box(new Vector3(x, y0 + (h / 2), z), new Vector3(w, h, d), c);

    private static void Bar(MeshBatch b, Vector3 from, Vector3 to, float r, Color c) => b.Rod(from, to, r, c, small: true);

    private static void Castor(MeshBatch b, float x, float z)
    {
        Bar(b, new Vector3(x, H(2), z), new Vector3(x, H(4.5f), z), 0.02f, new Color("#6E7A84"));
        b.Cylinder(new Vector3(x, H(2), z), 0.065f, 0.05f, 0.065f, Tyre, new Basis(Vector3.Forward, Mathf.Pi / 2));
    }

    private static void Castors(MeshBatch b, float w, float d, float inset = 0.08f)
    {
        foreach (var (x, z) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            Castor(b, x * ((w / 2) - inset), z * ((d / 2) - inset));
        }
    }

    /// <summary>A five-spoke wheeled base, like an office chair's or a drip stand's.</summary>
    private static void Spokes(MeshBatch b, float radius, Color colour)
    {
        for (var i = 0; i < 5; i++)
        {
            var a = i * Mathf.Tau / 5;
            var end = new Vector3(Mathf.Sin(a) * radius, H(3), Mathf.Cos(a) * radius);
            Bar(b, new Vector3(0, H(4), 0), end, 0.022f, colour);
            Castor(b, end.X, end.Z);
        }
    }

    /// <summary>A screen on the front face of something: dark bezel and a glowing picture.</summary>
    private static void ScreenAt(MeshBatch b, Vector3 centre, float w, float h, Color? picture = null, Basis? tilt = null)
    {
        var t = tilt ?? Basis.Identity;
        b.Box(centre, new Vector3(w, h, 0.05f), t, new Color("#2B2F33"));
        b.Box(centre + (t * new Vector3(0, 0, 0.027f)), new Vector3(w * 0.86f, h * 0.8f, 0.01f), t, picture ?? Screen);
        b.Box(centre + (t * new Vector3(0, h * 0.05f, 0.033f)), new Vector3(w * 0.7f, h * 0.06f, 0.005f), t, ScreenGlow);
        b.Box(centre + (t * new Vector3(0, -h * 0.15f, 0.033f)), new Vector3(w * 0.7f, h * 0.05f, 0.005f), t, new Color("#8BE38F"));
    }

    // ------------------------------------------------------------------ dispatch

    /// <summary>Builds a piece of furniture by id; false for ids built elsewhere (beds, chairs, cots...).</summary>
    public static bool Build(MeshBatch b, MeshBatch glass, string id, float w, float d, float h, Color colour, int seed)
    {
        switch (id)
        {
            case "office_desk":
            case "triage_desk":
                Desk(b, w, d, h, colour, id == "triage_desk");
                return true;
            case "staff_table":
                Table(b, w, d, h, colour, seed);
                return true;
            case "instrument_trolley":
                InstrumentTrolley(b, w, d, h);
                return true;
            case "reception_desk":
            case "station_counter":
            case "pharmacy_counter":
                Counter(b, id, w, d, h, colour);
                return true;
            case "clinical_cupboard":
                Cupboard(b, w, d, h, colour);
                return true;
            case "kitchenette":
                Kitchenette(b, w, d, h, colour);
                return true;
            case "bedside_locker":
                Locker(b, w, d, h, colour);
                return true;
            case "plaster_trolley":
                Drawers(b, w, d, h, colour, plaster: true);
                return true;
            case "resus_trolley":
                ResusTrolley(b, w, d, h, colour);
                return true;
            case "medicine_shelves":
                Shelves(b, w, d, h, colour, seed);
                return true;
            case "drug_fridge":
                Fridge(b, glass, w, d, h, colour);
                return true;
            case "hand_basin":
            case "plaster_sink":
                BasinUnit(b, w, d, h, colour, id == "plaster_sink");
                return true;
            case "drip_stand":
                DripStand(b, glass, h);
                return true;
            case "oxygen_cylinder":
                OxygenCylinder(b, h);
                return true;
            case "parallel_bars":
                ParallelBars(b, w, d, h);
                return true;
            case "exercise_mat":
                Blk(b, 0, 0, w, d, 0, H(2), colour);
                Blk(b, 0, 0, w - 0.2f, d - 0.2f, H(2), 0.004f, colour.Lightened(0.15f));
                return true;
            case "practice_stairs":
                PracticeStairs(b, w, d, h, colour);
                return true;
            case "obs_monitor":
                MonitorStand(b);
                return true;
            default:
                return Equipment(b, glass, id, w, d, h, colour, seed);
        }
    }

    /// <summary>A plain block for anything without its own model.</summary>
    public static void Plain(MeshBatch b, float w, float d, float h, Color colour)
    {
        Blk(b, 0, 0, w, d, 0, h, colour);
        Blk(b, 0, 0, w + 0.01f, d + 0.01f, 0, H(2), colour.Darkened(0.4f));
    }

    // ------------------------------------------------------------------ desks, tables and counters

    private static void Desk(MeshBatch b, float w, float d, float h, Color colour, bool triage)
    {
        var top = colour.Lightened(0.12f);
        Blk(b, 0, 0, w, d, h - H(2.2f), H(2.2f), top);

        // A pedestal of drawers at one end on the staff side, legs at the other, a modesty panel at the front.
        var ped = Math.Min(0.6f, w * 0.4f);
        Blk(b, (w / 2) - (ped / 2) - 0.04f, -0.03f, ped, d - 0.1f, 0, h - H(2.2f), colour.Darkened(0.08f));
        for (var i = 1; i < 3; i++)
        {
            Blk(b, (w / 2) - (ped / 2) - 0.04f, -(d / 2) + 0.015f, ped - 0.1f, 0.01f, (h - H(2.2f)) * i / 3f, H(0.6f), colour.Darkened(0.35f));
            Blk(b, (w / 2) - (ped / 2) - 0.04f, -(d / 2) + 0.012f, 0.16f, 0.02f, ((h - H(2.2f)) * i / 3f) - H(2), H(0.8f), Chrome);
        }

        foreach (var z in new[] { -1f, 1f })
        {
            Blk(b, -(w / 2) + 0.07f, z * ((d / 2) - 0.07f), 0.08f, 0.08f, 0, h - H(2.2f), colour.Darkened(0.2f));
        }

        Blk(b, -0.1f, (d / 2) - 0.06f, w - ped - 0.25f, 0.03f, H(8), h - H(10), colour.Darkened(0.15f));

        // The computer faces the staff side (the back), with its keyboard and mouse.
        var screenY = h + H(9);
        Blk(b, -0.1f, 0.05f, 0.18f, 0.14f, h, H(1), Ink);
        Bar(b, new Vector3(-0.1f, h, 0.05f), new Vector3(-0.1f, screenY - H(3), 0.05f), 0.02f, Ink);
        ScreenAt(b, new Vector3(-0.1f, screenY, 0.03f), 0.6f, H(9), null, new Basis(Vector3.Up, Mathf.Pi));
        Blk(b, -0.1f, -0.22f, 0.5f, 0.15f, h, H(0.7f), new Color("#3A4148"));
        b.Ball(new Vector3(0.28f, h + H(0.6f), -0.22f), new Vector3(0.06f, H(1.2f), 0.09f), new Color("#3A4148"), small: true);
        if (triage)
        {
            // A tray of obs kit: a thermometer and a pulse oximeter.
            Blk(b, 0.35f, 0.1f, 0.3f, 0.22f, h, H(0.8f), new Color("#DCE3E9"));
            b.Ball(new Vector3(0.3f, h + H(1.6f), 0.1f), new Vector3(0.07f, H(2), 0.05f), new Color("#1E88E5"), small: true);
        }
        else
        {
            // Papers and a pot of pens.
            Blk(b, 0.3f, 0.15f, 0.3f, 0.4f, h, H(0.4f), Colors.White);
            b.Cylinder(new Vector3(0.45f, h + H(1.5f), -0.15f), 0.04f, H(3), 0.04f, new Color("#5C6BC0"));
        }
    }

    private static void Table(MeshBatch b, float w, float d, float h, Color colour, int seed)
    {
        Blk(b, 0, 0, w, d, h - H(2.2f), H(2.2f), colour.Lightened(0.1f));
        foreach (var (x, z) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            Blk(b, x * ((w / 2) - 0.08f), z * ((d / 2) - 0.08f), 0.08f, 0.08f, 0, h - H(2.2f), colour.Darkened(0.25f));
        }

        // A couple of mugs and a newspaper on the staff room table.
        b.Cylinder(new Vector3(-w * 0.2f, h + H(1.3f), d * 0.1f), 0.05f, H(2.6f), 0.05f, BoxColours[seed % BoxColours.Length]);
        b.Cylinder(new Vector3(w * 0.25f, h + H(1.3f), -d * 0.15f), 0.05f, H(2.6f), 0.05f, Colors.White);
        Blk(b, w * 0.05f, d * 0.1f, 0.36f, 0.26f, h, H(0.4f), new Color("#ECEFF1"));
    }

    private static void InstrumentTrolley(MeshBatch b, float w, float d, float h)
    {
        Castors(b, w, d);
        foreach (var (x, z) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            Bar(b, new Vector3(x * ((w / 2) - 0.06f), H(4), z * ((d / 2) - 0.06f)), new Vector3(x * ((w / 2) - 0.06f), h, z * ((d / 2) - 0.06f)), 0.02f, Steel);
        }

        Blk(b, 0, 0, w, d, H(7), H(1), Steel);
        Blk(b, 0, 0, w, d, h - H(1), H(1), Steel);

        // The top laid with a blue drape and the instruments.
        Blk(b, 0, 0, w - 0.06f, d - 0.06f, h, H(0.4f), new Color("#3F7FBF"));
        for (var i = 0; i < 6; i++)
        {
            var x = -(w / 2) + 0.15f + (i * (w - 0.3f) / 5);
            Blk(b, x, 0.02f, 0.025f, d * 0.55f, h + H(0.4f), H(0.5f), new Color("#DDE3E8"));
        }

        Blk(b, w * 0.3f, -d * 0.25f, 0.2f, 0.12f, h + H(0.4f), H(1), new Color("#C9D2D9"));
    }

    private static void Counter(MeshBatch b, string id, float w, float d, float h, Color colour)
    {
        var worktop = id switch { "reception_desk" => h - H(9), "station_counter" => h - H(5), _ => h };
        Blk(b, 0, 0, w - 0.04f, d - 0.04f, 0, H(3), colour.Darkened(0.5f));
        Blk(b, 0, 0, w, d, H(3), worktop - H(5), colour.Darkened(0.04f));
        Blk(b, 0, 0, w + 0.06f, d + 0.06f, worktop - H(2.5f), H(2.5f), colour.Lightened(0.2f));
        if (worktop < h)
        {
            // The raised ledge on the visitors' side, over the staff worktop.
            Blk(b, 0, (d / 2) - 0.09f, w, 0.18f, worktop, h - worktop - H(2), colour.Darkened(0.02f));
            Blk(b, 0, (d / 2) - 0.1f, w + 0.08f, 0.3f, h - H(2), H(2), colour.Lightened(0.28f));
        }

        // A panel of light wood along the front, and computers facing the staff side.
        Blk(b, 0, (d / 2) + 0.005f, w * 0.92f, 0.01f, H(6), h - H(10), colour.Lightened(0.08f));
        var screens = Math.Max(1, (int)Math.Round(w / 1.6f));
        for (var i = 0; i < screens; i++)
        {
            var x = -(w / 2) + ((i + 0.5f) * w / screens);
            Blk(b, x, -0.05f, 0.14f, 0.12f, worktop, H(1), Ink);
            Bar(b, new Vector3(x, worktop, -0.05f), new Vector3(x, worktop + H(6), -0.05f), 0.02f, Ink);
            ScreenAt(b, new Vector3(x, worktop + H(8.5f), -0.07f), 0.55f, H(8), null, new Basis(Vector3.Up, Mathf.Pi));
            Blk(b, x, -0.3f, 0.45f, 0.14f, worktop, H(0.7f), new Color("#3A4148"));
        }

        if (id == "pharmacy_counter")
        {
            // Bags of prescriptions waiting to be collected.
            for (var i = 0; i < 3; i++)
            {
                Blk(b, (w / 2) - 0.25f - (i * 0.22f), 0.1f, 0.16f, 0.12f, h, H(5), new Color("#F7F5EE"));
                Blk(b, (w / 2) - 0.25f - (i * 0.22f), 0.165f, 0.06f, 0.005f, h + H(2), H(1.6f), new Color("#1E8E3E"));
            }
        }
    }

    // ------------------------------------------------------------------ cupboards, shelves, basins

    /// <summary>Pairs of doors with handles across the front of a cabinet.</summary>
    private static void Doors(MeshBatch b, float w, float d, float y0, float h, Color colour)
    {
        var pairs = Math.Max(1, (int)Math.Round(w / 1.2f));
        var line = colour.Darkened(0.4f);
        for (var i = 0; i <= pairs * 2; i++)
        {
            var x = -(w / 2) + (i * w / (pairs * 2));
            if (i > 0 && i < pairs * 2)
            {
                Blk(b, x, d / 2, 0.012f, 0.012f, y0 + H(1), h - H(2), line);
            }
        }

        for (var i = 0; i < pairs; i++)
        {
            var mid = -(w / 2) + ((i + 0.5f) * w / pairs);
            foreach (var side in new[] { -1f, 1f })
            {
                Blk(b, mid + (side * 0.06f), (d / 2) + 0.012f, 0.02f, 0.025f, y0 + (h * 0.6f), h * 0.18f, Chrome);
            }
        }
    }

    private static void Cupboard(MeshBatch b, float w, float d, float h, Color colour)
    {
        Blk(b, 0, 0, w - 0.02f, d - 0.02f, 0, H(2), colour.Darkened(0.45f));
        Blk(b, 0, 0, w, d, H(2), h - H(2), colour);
        Blk(b, 0, 0, w + 0.02f, d + 0.02f, h - H(0.8f), H(0.8f), colour.Lightened(0.3f));
        Doors(b, w, d, H(2), h - H(3), colour);
    }

    private static void Kitchenette(MeshBatch b, float w, float d, float h, Color colour)
    {
        Blk(b, 0, 0, w - 0.02f, d - 0.02f, 0, H(2), colour.Darkened(0.45f));
        Blk(b, 0, 0, w, d, H(2), h - H(4.5f), colour);
        Doors(b, w, d, H(2), h - H(5), colour);
        Blk(b, 0, 0.02f, w + 0.04f, d + 0.04f, h - H(2.5f), H(2.5f), new Color("#6D7F8C"));

        // A sink with its tap, a kettle and a microwave.
        Blk(b, -w * 0.25f, 0.02f, 0.5f, d * 0.6f, h - H(0.6f), H(0.8f), new Color("#C9D6DF"));
        Bar(b, new Vector3(-w * 0.25f, h, -(d / 2) + 0.08f), new Vector3(-w * 0.25f, h + H(5), -(d / 2) + 0.08f), 0.02f, Chrome);
        Bar(b, new Vector3(-w * 0.25f, h + H(5), -(d / 2) + 0.08f), new Vector3(-w * 0.25f, h + H(5), -(d / 2) + 0.2f), 0.02f, Chrome);
        b.Cylinder(new Vector3(w * 0.1f, h + H(2.5f), 0), 0.07f, H(5), 0.07f, new Color("#ECEFF1"));
        Blk(b, w * 0.32f, -0.03f, 0.5f, d * 0.65f, h, H(8), new Color("#E0E4E8"));
        Blk(b, w * 0.29f, (d * 0.29f) + 0.005f, 0.32f, 0.01f, h + H(1.5f), H(5), new Color("#2B2F33"));
    }

    private static void Locker(MeshBatch b, float w, float d, float h, Color colour)
    {
        Blk(b, 0, 0, w, d, 0, h, colour);
        Blk(b, 0, 0, w + 0.02f, d + 0.02f, h - H(0.8f), H(0.8f), colour.Lightened(0.25f));
        Blk(b, 0, d / 2, w - 0.06f, 0.012f, h - H(5), H(0.6f), colour.Darkened(0.35f));
        Blk(b, 0, (d / 2) + 0.012f, 0.14f, 0.02f, h - H(3), H(0.8f), Chrome);
        Blk(b, 0.06f, (d / 2) + 0.012f, 0.02f, 0.02f, h * 0.35f, h * 0.2f, Chrome);

        // A water jug and a cup.
        b.Cylinder(new Vector3(-w * 0.18f, h + H(3), 0), 0.06f, H(6), 0.06f, new Color("#B3E5FC", 1f));
        b.Cylinder(new Vector3(w * 0.2f, h + H(1.5f), 0.05f), 0.04f, H(3), 0.04f, Colors.White);
    }

    private static void Drawers(MeshBatch b, float w, float d, float h, Color colour, bool plaster)
    {
        Castors(b, w, d);
        Blk(b, 0, 0, w, d, H(4), h - H(4), colour);
        for (var i = 1; i < 4; i++)
        {
            var y = H(4) + ((h - H(4)) * i / 4f);
            Blk(b, 0, d / 2, w - 0.06f, 0.012f, y, H(0.6f), colour.Darkened(0.4f));
            Blk(b, 0, (d / 2) + 0.012f, 0.18f, 0.02f, y - ((h - H(4)) / 8f), H(0.9f), colour.Lightened(0.5f));
        }

        Blk(b, 0, 0, w + 0.02f, d + 0.02f, h - H(0.8f), H(0.8f), colour.Lightened(0.3f));
        if (plaster)
        {
            // Rolls of plaster bandage on the top.
            for (var i = 0; i < 4; i++)
            {
                b.Cylinder(new Vector3(-(w / 2) + 0.18f + (i * 0.17f), h + 0.06f, 0), 0.06f, 0.22f, 0.06f, new Color("#F2EFE6"), new Basis(Vector3.Right, Mathf.Pi / 2));
            }
        }
    }

    /// <summary>The resus trolley: red drawers, the defibrillator on top, the push handle at the back.</summary>
    public static void ResusTrolley(MeshBatch b, float w, float d, float h, Color colour) =>
        b.Kit(Equipment3D, "resus_trolley", new Vector3(1.2f, Kit3D.M(1.0), 1.4f), new Vector3(w, h, d), colour);

    private static void Shelves(MeshBatch b, float w, float d, float h, Color colour, int seed)
    {
        Blk(b, 0, -(d / 2) + 0.02f, w, 0.04f, 0, h, colour.Darkened(0.08f));
        foreach (var side in new[] { -1f, 1f })
        {
            Blk(b, side * ((w / 2) - 0.02f), 0, 0.04f, d, 0, h, colour);
        }

        for (var i = 0; i <= 5; i++)
        {
            var y = Math.Min(h - H(1), i * h / 5f);
            Blk(b, 0, 0, w, d, y, H(1), colour.Lightened(0.08f));
            if (i == 5)
            {
                continue;
            }

            var count = Math.Max(3, (int)(w / 0.22f));
            for (var k = 0; k < count; k++)
            {
                var x = -(w / 2) + 0.1f + (k * (w - 0.2f) / count) + 0.06f;
                var box = BoxColours[(i + k + seed) % BoxColours.Length];
                var tall = (h / 5f) * (0.45f + (((i * 3) + k + seed) % 4 * 0.1f));
                Blk(b, x, 0.03f, (w - 0.2f) / count * 0.8f, d * 0.6f, y + H(1), tall, box);
            }
        }
    }

    private static void Fridge(MeshBatch b, MeshBatch glass, float w, float d, float h, Color colour)
    {
        Blk(b, 0, 0, w, d, 0, h, colour);
        Blk(b, 0, 0, w - 0.02f, d - 0.02f, 0, H(2), colour.Darkened(0.45f));
        Blk(b, 0, (d / 2) - 0.03f, w - 0.12f, 0.02f, H(6), h - H(12), new Color("#DDEFF7"));
        for (var i = 1; i < 4; i++)
        {
            Blk(b, 0, (d / 2) - 0.06f, w - 0.16f, 0.06f, H(6) + ((h - H(12)) * i / 4f), H(0.8f), new Color("#B0BEC5"));
            Blk(b, -0.08f, (d / 2) - 0.1f, 0.12f, 0.08f, H(6.8f) + ((h - H(12)) * i / 4f), H(3), BoxColours[i]);
        }

        glass.Box(new Vector3(0, h / 2, (d / 2) + 0.005f), new Vector3(w - 0.1f, h - H(10), 0.01f), new Color("#A9D3E8", 0.45f));
        Blk(b, w * 0.28f, (d / 2) + 0.01f, 0.12f, 0.01f, h - H(4), H(1.6f), new Color("#3EE07A"));
        Blk(b, (w / 2) - 0.06f, (d / 2) + 0.02f, 0.025f, 0.03f, h * 0.35f, h * 0.3f, Chrome);
    }

    private static void BasinUnit(MeshBatch b, float w, float d, float h, Color colour, bool deep)
    {
        Blk(b, 0, 0, w - 0.02f, d - 0.02f, 0, H(2), colour.Darkened(0.45f));
        Blk(b, 0, 0, w, d, H(2), h - H(2), colour);
        Doors(b, w, d, H(2), h - H(4), colour);
        Blk(b, 0, 0, w + 0.03f, d + 0.03f, h - H(1), H(1), colour.Lightened(0.15f));
        Blk(b, 0, 0.03f, w * 0.65f, d * 0.6f, h - H(deep ? 2 : 0.6f), H(deep ? 2.2f : 0.9f), new Color("#C9D6DF"));
        Bar(b, new Vector3(0, h, -(d / 2) + 0.07f), new Vector3(0, h + H(6), -(d / 2) + 0.07f), 0.022f, Chrome);
        Bar(b, new Vector3(0, h + H(6), -(d / 2) + 0.07f), new Vector3(0, h + H(5), -(d / 2) + 0.2f), 0.02f, Chrome);
        Bar(b, new Vector3(0.05f, h + H(4), -(d / 2) + 0.07f), new Vector3(0.18f, h + H(4), -(d / 2) + 0.07f), 0.012f, Chrome);
    }

    // ------------------------------------------------------------------ stands, cylinders, physio

    private static void DripStand(MeshBatch b, MeshBatch glass, float h)
    {
        var made = new Vector3(1, Kit3D.M(1.8), 1);
        var size = new Vector3(1, h, 1);
        b.Kit(Equipment3D, "drip_stand", made, size);
        glass.Kit(Equipment3D, "drip_bag", made, size, new Color("#E3F2FD", 0.75f));
    }

    private static void OxygenCylinder(MeshBatch b, float h)
    {
        b.Cylinder(new Vector3(0, H(2), 0), 0.17f, H(4), 0.17f, Plinth);
        b.Cylinder(new Vector3(0, (h - H(6)) / 2 + H(3), 0), 0.12f, h - H(10), 0.12f, new Color("#F4F6F7"));
        b.Ball(new Vector3(0, h - H(7), 0), new Vector3(0.24f, H(5), 0.24f), new Color("#F4F6F7"));
        b.Cylinder(new Vector3(0, h - H(3), 0), 0.05f, H(5), 0.05f, new Color("#7D8790"));
        b.Cylinder(new Vector3(0.07f, h - H(2.5f), 0), 0.04f, H(2), 0.04f, new Color("#2E7D32"), new Basis(Vector3.Forward, Mathf.Pi / 2));
    }

    private static void ParallelBars(MeshBatch b, float w, float d, float h)
    {
        var wood = new Color("#B98B5E");
        var alongZ = d >= w;
        var (len, wide) = alongZ ? (d, w) : (w, d);
        var turn = alongZ ? Basis.Identity : new Basis(Vector3.Up, Mathf.Pi / 2);
        b.Box(new Vector3(0, H(1.5f), 0), new Vector3(wide, H(3), len), turn, wood);
        foreach (var side in new[] { -1f, 1f })
        {
            var x = side * ((wide / 2) - 0.08f);
            var posts = Math.Max(2, (int)Math.Round(len / 1.4f) + 1);
            for (var i = 0; i < posts; i++)
            {
                var z = -(len / 2) + 0.12f + (i * (len - 0.24f) / (posts - 1));
                Bar(b, turn * new Vector3(x, H(3), z), turn * new Vector3(x, h, z), 0.025f, Chrome);
            }

            Bar(b, turn * new Vector3(x, h, -(len / 2) + 0.1f), turn * new Vector3(x, h, (len / 2) - 0.1f), 0.035f, new Color("#90A4AE"));
        }
    }

    private static void PracticeStairs(MeshBatch b, float w, float d, float h, Color colour)
    {
        var alongZ = d >= w;
        var (len, wide) = alongZ ? (d, w) : (w, d);
        var turn = alongZ ? Basis.Identity : new Basis(Vector3.Up, Mathf.Pi / 2);
        const int steps = 4;
        var step = len * 0.3f / steps;
        for (var i = 0; i < steps; i++)
        {
            var rise = h * (i + 1) / steps;
            foreach (var end in new[] { -1f, 1f })
            {
                var z = end * ((len / 2) - (step * (i + 0.5f)));
                b.Box(turn * new Vector3(0, rise / 2, z), new Vector3(wide - 0.2f, rise, step), turn, i % 2 == 0 ? colour : colour.Lightened(0.08f));
            }
        }

        b.Box(turn * new Vector3(0, h / 2, 0), new Vector3(wide - 0.2f, h, len * 0.4f), turn, colour.Lightened(0.05f));
        foreach (var side in new[] { -1f, 1f })
        {
            var x = side * ((wide / 2) - 0.05f);
            foreach (var z in new[] { -(len / 2) + 0.05f, -0.2f * len, 0.2f * len, (len / 2) - 0.05f })
            {
                var top = Math.Abs(z) < len * 0.25f ? h + H(18) : H(18);
                Bar(b, turn * new Vector3(x, 0, z), turn * new Vector3(x, top, z), 0.02f, Chrome);
            }

            Bar(b, turn * new Vector3(x, H(18), -(len / 2) + 0.05f), turn * new Vector3(x, h + H(18), -0.2f * len), 0.03f, new Color("#90A4AE"));
            Bar(b, turn * new Vector3(x, h + H(18), -0.2f * len), turn * new Vector3(x, h + H(18), 0.2f * len), 0.03f, new Color("#90A4AE"));
            Bar(b, turn * new Vector3(x, h + H(18), 0.2f * len), turn * new Vector3(x, H(18), (len / 2) - 0.05f), 0.03f, new Color("#90A4AE"));
        }
    }

    /// <summary>An obs monitor's stand: a five-spoke wheeled base, the pole and a basket. Its screen floats above, drawn with the overlays.</summary>
    private static void MonitorStand(MeshBatch b) => b.Model(Equipment3D, "monitor_stand", Transform3D.Identity);

    // ------------------------------------------------------------------ chairs

    /// <summary>
    /// A chair facing +z, its seat centred at the origin: legs, a padded seat at sitting height, a back,
    /// and arms on visitor and lounge chairs. Staff chairs are office chairs on a five-spoke base.
    /// </summary>
    public static void Chair(MeshBatch b, string id, Color colour)
    {
        var seat = H(14);
        var frame = new Color("#5E6770");
        const float half = 0.42f;
        const float depth = 0.36f;
        if (id == "staff_chair")
        {
            Spokes(b, 0.3f, Ink);
            Bar(b, new Vector3(0, H(4), 0), new Vector3(0, seat - H(2), 0), 0.03f, frame);
            Blk(b, 0, 0.02f, half * 1.8f, depth * 1.9f, seat - H(2.5f), H(2.5f), colour);
            b.Box(new Vector3(0, seat + H(11), -depth + 0.02f), new Vector3(half * 1.7f, H(14), 0.07f), new Basis(Vector3.Right, -0.08f), colour);
            Bar(b, new Vector3(0, seat - H(2), -depth + 0.02f), new Vector3(0, seat + H(5), -depth + 0.02f), 0.025f, frame);
            return;
        }

        foreach (var (x, z) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            Bar(b, new Vector3(x * (half - 0.05f), 0, z * (depth - 0.05f)), new Vector3(x * (half - 0.05f), seat - H(2), z * (depth - 0.05f)), 0.022f, frame);
        }

        var lounge = id == "lounge_chair";
        Blk(b, 0, 0.02f, half * 2, depth * 2, seat - H(lounge ? 4 : 2.5f), H(lounge ? 4 : 2.5f), colour);
        b.Box(new Vector3(0, seat + H(lounge ? 13 : 10), -depth + 0.03f), new Vector3(half * 2, H(lounge ? 22 : 17), lounge ? 0.14f : 0.07f), new Basis(Vector3.Right, -0.1f), colour.Darkened(0.05f));
        if (id is "visitor_chair" or "lounge_chair")
        {
            foreach (var side in new[] { -1f, 1f })
            {
                var x = side * (half + (lounge ? 0.04f : 0f));
                if (lounge)
                {
                    Blk(b, x, 0.02f, 0.12f, depth * 2, 0, seat + H(7), colour.Darkened(0.08f));
                }
                else
                {
                    Bar(b, new Vector3(x, seat, depth - 0.08f), new Vector3(x, seat + H(7), depth - 0.08f), 0.02f, frame);
                    Blk(b, x, 0, 0.06f, depth * 1.6f, seat + H(7), H(1.6f), Ink);
                }
            }
        }
    }
}
