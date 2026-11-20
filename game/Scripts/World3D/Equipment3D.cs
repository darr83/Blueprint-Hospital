using System;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>Medical equipment, maternity kit, toilets and the café, in the furniture kit's frame (+z is the front).</summary>
public static partial class Furnish3D
{
    private static readonly Color Tubing = new("#5C9BD6");
    private static readonly Color Porcelain = new("#F7F9FA");

    private static bool Equipment(MeshBatch b, MeshBatch glass, string id, float w, float d, float h, Color colour, int seed)
    {
        switch (id)
        {
            case "ventilator": Ventilator(b, h, colour); return true;
            case "anaesthetic_machine": Anaesthetic(b, w, d, h, colour); return true;
            case "ultrasound_machine": Ultrasound(b, h, colour); return true;
            case "eeg_machine": Eeg(b, w, d, h, colour); return true;
            case "endoscopy_stack": Endoscopy(b, w, d, h, colour); return true;
            case "c_arm": CArm(b, w, d, h, colour); return true;
            case "xray_tube": XRayTube(b, h, colour); return true;
            case "ct_gantry": CtGantry(b, w, d, h, colour); return true;
            case "lead_screen": LeadScreen(b, glass, w, d, h, colour); return true;
            case "ctg_monitor": Ctg(b, h, colour); return true;
            case "resuscitaire": Resuscitaire(b, w, d, h, colour); return true;
            case "birthing_ball":
                b.Ball(new Vector3(0, Math.Min(w, d) * 0.45f, 0), new Vector3(Math.Min(w, d) * 0.9f, Math.Min(w, d) * 0.88f, Math.Min(w, d) * 0.9f), colour);
                return true;
            case "baby_changing_unit": ChangingUnit(b, w, d, h, colour); return true;
            case "breast_pump": BreastPump(b, glass, h, colour); return true;
            case "toilet_pan": Toilet(b, w, d, false); return true;
            case "accessible_wc": Toilet(b, w, d, true); return true;
            case "wash_basin": WallBasin(b, w, d); return true;
            case "hand_dryer": HandDryer(b, colour); return true;
            case "toilet_cubicle": Cubicle(b, w, d, h, colour); return true;
            case "vending_machine": Vending(b, glass, w, d, h, seed); return true;
            case "water_cooler": WaterCooler(b, glass, h); return true;
            case "food_counter": FoodCounter(b, glass, w, d, h, colour, seed); return true;
            case "cafe_table": CafeTable(b, w, d, h, colour); return true;
            case "cleaners_sink": CleanersSink(b, w, d, h); return true;
            case "cleaning_shelves": CleaningShelves(b, w, d, h, colour, seed); return true;
            case "broom_rack": BroomRack(b, w, d, h, colour); return true;
            case "mop_bucket": MopBucket(b, w, d, h, colour); return true;
            default: return false;
        }
    }

    // ------------------------------------------------------------------ critical care and theatres

    private static void Ventilator(MeshBatch b, float h, Color colour)
    {
        Spokes(b, 0.26f, new Color("#5A6670"));
        Bar(b, new Vector3(0, H(4), 0), new Vector3(0, h * 0.45f, 0), 0.04f, new Color("#AEB9C2"));
        Blk(b, 0, 0, 0.5f, 0.42f, h * 0.42f, h * 0.3f, colour.Lightened(0.35f));
        Blk(b, 0, 0.212f, 0.44f, 0.01f, h * 0.48f, h * 0.18f, colour);
        ScreenAt(b, new Vector3(0, h * 0.86f, 0.05f), 0.52f, h * 0.24f, new Color("#14202B"), new Basis(Vector3.Right, -0.25f));

        // The humidifier and the blue breathing circuit looping round to the patient.
        b.Cylinder(new Vector3(0.3f, h * 0.5f, 0.12f), 0.07f, H(5), 0.07f, new Color("#B3E5FC"));
        var path = new[] { new Vector3(0.22f, h * 0.65f, 0.2f), new Vector3(0.4f, h * 0.7f, 0.4f), new Vector3(0.3f, h * 0.8f, 0.7f), new Vector3(0.05f, h * 0.72f, 0.9f) };
        for (var i = 1; i < path.Length; i++)
        {
            b.Capsule(path[i - 1], path[i], 0.035f, Tubing);
        }
    }

    private static void Anaesthetic(MeshBatch b, float w, float d, float h, Color colour)
    {
        Castors(b, w, d);
        Blk(b, 0, 0, w, d, H(4), h * 0.45f, colour);
        Blk(b, 0, 0.05f, w + 0.04f, d + 0.06f, H(4) + (h * 0.45f), H(2), new Color("#D7DEE3"));
        for (var i = 1; i < 3; i++)
        {
            Blk(b, 0, d / 2, w - 0.08f, 0.012f, H(4) + (h * 0.45f * i / 3f), H(0.6f), colour.Darkened(0.35f));
        }

        // The tower: vaporisers on the front rail, the bellows, gas cylinders behind, the monitor on top.
        Blk(b, 0, -0.08f, w * 0.85f, d * 0.6f, H(6) + (h * 0.45f), h * 0.3f, colour.Lightened(0.1f));
        var vapour = new[] { new Color("#9C27B0"), new Color("#FDD835"), new Color("#1E88E5") };
        for (var i = 0; i < 3; i++)
        {
            Blk(b, -0.2f + (i * 0.14f), 0.24f, 0.1f, 0.1f, H(7) + (h * 0.45f) + H(4), H(6), vapour[i]);
        }

        b.Cylinder(new Vector3(0.27f, H(6) + (h * 0.45f) + H(4), 0.2f), 0.07f, H(8), 0.07f, new Color("#DCEFF7"));
        foreach (var x in new[] { -0.15f, 0.15f })
        {
            b.Cylinder(new Vector3(x, h * 0.55f, -(d / 2) - 0.08f), 0.08f, h * 0.5f, 0.08f, new Color("#F4F6F7"));
            b.Cylinder(new Vector3(x, h * 0.81f, -(d / 2) - 0.08f), 0.05f, H(2), 0.05f, new Color("#7D8790"));
        }

        ScreenAt(b, new Vector3(0, h * 0.93f, -0.05f), w * 0.8f, h * 0.18f, new Color("#14202B"), new Basis(Vector3.Right, -0.12f));
        b.Capsule(new Vector3(0.3f, h * 0.55f, 0.3f), new Vector3(0.5f, h * 0.5f, 0.6f), 0.03f, Tubing);
    }

    private static void Ultrasound(MeshBatch b, float h, Color colour)
    {
        Castors(b, 0.55f, 0.55f);
        Blk(b, 0, 0, 0.5f, 0.5f, H(4), H(10), colour.Darkened(0.1f));
        Blk(b, 0, -0.05f, 0.2f, 0.2f, H(14), h * 0.35f, new Color("#B0BEC5"));
        b.Box(new Vector3(0, h * 0.62f, 0.06f), new Vector3(0.6f, H(3), 0.42f), new Basis(Vector3.Right, 0.25f), colour.Lightened(0.25f));
        b.Box(new Vector3(0, h * 0.62f + H(1.6f), 0.08f), new Vector3(0.38f, H(0.5f), 0.22f), new Basis(Vector3.Right, 0.25f), new Color("#37474F"));
        foreach (var x in new[] { -0.33f, 0.33f })
        {
            b.Ball(new Vector3(x, h * 0.64f, 0.15f), new Vector3(0.06f, H(5), 0.06f), new Color("#ECEFF1"), small: true);
        }

        Bar(b, new Vector3(0, h * 0.64f, -0.18f), new Vector3(0, h * 0.82f, -0.18f), 0.02f, Ink);
        ScreenAt(b, new Vector3(0, h * 0.9f, -0.14f), 0.5f, h * 0.2f, new Color("#101418"));
        b.Ball(new Vector3(0.05f, h * 0.9f, -0.105f), new Vector3(0.18f, h * 0.1f, 0.01f), new Color("#6D6D6D"), small: true);
    }

    private static void Eeg(MeshBatch b, float w, float d, float h, Color colour)
    {
        Castors(b, w, d);
        Blk(b, 0, 0, w, d, H(4), H(3), colour);
        Bar(b, new Vector3(0, H(7), 0), new Vector3(0, h * 0.62f, 0), 0.035f, new Color("#AEB9C2"));
        Blk(b, 0, 0, w, d, h * 0.62f, H(2), colour.Lightened(0.2f));

        // The laptop, open, and the head box with its rainbow of electrode leads.
        Blk(b, -0.05f, 0.04f, 0.42f, 0.28f, h * 0.62f + H(2), H(0.6f), new Color("#3A4148"));
        ScreenAt(b, new Vector3(-0.05f, h * 0.62f + H(6), -0.1f), 0.42f, H(8), new Color("#0F1A24"), new Basis(Vector3.Right, -0.2f));
        Blk(b, 0.2f, 0.15f, 0.14f, 0.1f, h * 0.62f + H(2), H(3), new Color("#ECEFF1"));
        var leads = new[] { new Color("#E53935"), new Color("#FDD835"), new Color("#43A047"), new Color("#1E88E5"), new Color("#8E24AA") };
        for (var i = 0; i < leads.Length; i++)
        {
            b.Capsule(new Vector3(0.2f, h * 0.62f + H(3), 0.18f), new Vector3(0.1f + (i * 0.06f), h * 0.35f, 0.3f), 0.008f, leads[i]);
        }
    }

    private static void Endoscopy(MeshBatch b, float w, float d, float h, Color colour)
    {
        Castors(b, w, d);
        foreach (var (x, z) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            Bar(b, new Vector3(x * ((w / 2) - 0.05f), H(4), z * ((d / 2) - 0.05f)), new Vector3(x * ((w / 2) - 0.05f), h * 0.75f, z * ((d / 2) - 0.05f)), 0.022f, Chrome);
        }

        var units = new[] { new Color("#ECEFF1"), new Color("#CFD8DC"), new Color("#ECEFF1"), colour.Lightened(0.3f) };
        for (var i = 0; i < 4; i++)
        {
            var y = H(5) + (i * h * 0.17f);
            Blk(b, 0, 0, w - 0.06f, d - 0.04f, y, H(1), Chrome);
            Blk(b, 0, -0.02f, w - 0.16f, d - 0.14f, y + H(1), h * 0.12f, units[i]);
            Blk(b, -0.1f, (d / 2) - 0.08f, 0.2f, 0.01f, y + H(3), H(1.2f), new Color("#43A047"));
        }

        ScreenAt(b, new Vector3(0, h * 0.88f, 0), w * 1.1f, h * 0.22f, new Color("#7A2E2A"));
        Bar(b, new Vector3((w / 2) + 0.02f, h * 0.7f, 0), new Vector3((w / 2) + 0.1f, h * 0.75f, 0), 0.02f, Chrome);
        b.Ring(new Vector3((w / 2) + 0.12f, h * 0.55f, 0), 0.14f, Ink, new Basis(Vector3.Forward, Mathf.Pi / 2), 0.6f);
    }

    private static void CArm(MeshBatch b, float w, float d, float h, Color colour)
    {
        // The base unit on wheels, at the back, and the big C reaching forward over the table.
        Castors(b, w * 0.6f, d * 0.8f);
        Blk(b, 0, -d * 0.2f, w * 0.6f, d * 0.55f, H(4), h * 0.4f, colour);
        Blk(b, 0, -d * 0.2f, w * 0.5f, d * 0.4f, H(4) + (h * 0.4f), H(10), colour.Lightened(0.05f));
        var c = new Vector3(0, h * 0.55f, d * 0.12f);
        var r = h * 0.38f;
        for (var i = 0; i < 10; i++)
        {
            var a0 = -Mathf.Pi * 0.55f + (i * Mathf.Pi * 1.1f / 10);
            var a1 = -Mathf.Pi * 0.55f + ((i + 1) * Mathf.Pi * 1.1f / 10);
            b.Capsule(c + new Vector3(0, Mathf.Sin(a0) * r, Mathf.Cos(a0) * r * 0.9f), c + new Vector3(0, Mathf.Sin(a1) * r, Mathf.Cos(a1) * r * 0.9f), 0.07f, colour.Lightened(0.1f));
        }

        Blk(b, 0, c.Z + (Mathf.Cos(Mathf.Pi * 0.55f) * r * 0.9f), 0.42f, 0.42f, c.Y + (Mathf.Sin(Mathf.Pi * 0.55f) * r) - H(2), H(5), new Color("#5C6670"));
        Blk(b, 0, c.Z + (Mathf.Cos(-Mathf.Pi * 0.55f) * r * 0.9f), 0.3f, 0.3f, c.Y - r - H(2), H(6), new Color("#5C6670"));
    }

    private static void XRayTube(MeshBatch b, float h, Color colour) =>
        b.Kit(Equipment3D, "xray_tube", new Vector3(1, Kit3D.M(2.1), 1), new Vector3(1, h, 1), colour);

    /// <summary>A deep white ring with the bore open right through, an LED ring on each face and a small control panel.</summary>
    private static void CtGantry(MeshBatch b, float w, float d, float h, Color colour) =>
        b.Kit(Equipment3D, "ct_gantry", new Vector3(4.0f, Kit3D.M(1.9), 1.4f), new Vector3(w, h, d), colour);

    private static void LeadScreen(MeshBatch b, MeshBatch glass, float w, float d, float h, Color colour)
    {
        Castors(b, w, d);
        Blk(b, 0, 0, w, 0.12f, H(4), H(3), Plinth);
        Blk(b, 0, 0, w, 0.06f, H(7), h - H(7), colour);
        glass.Box(new Vector3(0, h * 0.72f, 0.035f), new Vector3(w * 0.6f, h * 0.25f, 0.02f), new Color("#C8E6C9", 0.55f));
        b.Cylinder(new Vector3(0, h * 0.4f, 0.035f), 0.12f, 0.01f, 0.12f, new Color("#FDD835"), new Basis(Vector3.Right, Mathf.Pi / 2));
        b.Cylinder(new Vector3(0, h * 0.4f, 0.042f), 0.03f, 0.01f, 0.03f, Ink, new Basis(Vector3.Right, Mathf.Pi / 2));
    }

    // ------------------------------------------------------------------ maternity

    private static void Ctg(MeshBatch b, float h, Color colour)
    {
        Spokes(b, 0.2f, new Color("#5A6670"));
        Bar(b, new Vector3(0, H(4), 0), new Vector3(0, h * 0.6f, 0), 0.025f, new Color("#AEB9C2"));
        Blk(b, 0, 0, 0.4f, 0.3f, h * 0.6f, h * 0.3f, colour.Lightened(0.15f));
        ScreenAt(b, new Vector3(0, h * 0.8f, 0.16f), 0.32f, h * 0.14f, new Color("#101A14"));
        Blk(b, 0, 0.17f, 0.3f, 0.01f, h * 0.62f, H(2), Colors.White);
        b.Box(new Vector3(0, h * 0.6f - H(1), 0.24f), new Vector3(0.24f, 0.01f, 0.16f), new Basis(Vector3.Right, 0.6f), new Color("#FBFBF7"));
    }

    private static void Resuscitaire(MeshBatch b, float w, float d, float h, Color colour)
    {
        Castors(b, w, d);
        Blk(b, 0, -0.05f, w * 0.85f, d * 0.8f, H(4), h * 0.25f, colour.Darkened(0.05f));
        Blk(b, 0, 0, w, d, h * 0.25f + H(4), H(6), colour);
        Blk(b, 0, 0.02f, w * 0.75f, d * 0.65f, h * 0.25f + H(10), H(1), new Color("#90CAF9"));

        // The column at the back and the radiant heater hanging over the cot.
        Blk(b, 0, -(d / 2) + 0.08f, 0.16f, 0.12f, h * 0.25f + H(10), h * 0.55f, new Color("#CFD8DC"));
        Blk(b, 0, 0, w * 0.85f, d * 0.6f, h * 0.88f, H(5), colour.Lightened(0.05f));
        Blk(b, 0, 0, w * 0.7f, d * 0.45f, h * 0.88f - H(0.6f), H(0.6f), new Color("#FF8A65"));
        ScreenAt(b, new Vector3(0, h * 0.62f, -(d / 2) + 0.16f), 0.3f, H(7), new Color("#101A14"));
    }

    private static void ChangingUnit(MeshBatch b, float w, float d, float h, Color colour)
    {
        Cupboard(b, w, d, h, colour);
        Blk(b, 0, 0, w * 0.85f, d * 0.75f, h, H(2), new Color("#80CBC4"));
        Blk(b, -w * 0.32f, -d * 0.2f, 0.18f, 0.12f, h, H(4), new Color("#FFF9C4"));
        Blk(b, w * 0.32f, -d * 0.2f, 0.16f, 0.1f, h, H(2), new Color("#B3E5FC"));
    }

    private static void BreastPump(MeshBatch b, MeshBatch glass, float h, Color colour)
    {
        Spokes(b, 0.18f, new Color("#5A6670"));
        Bar(b, new Vector3(0, H(4), 0), new Vector3(0, h * 0.7f, 0), 0.025f, new Color("#AEB9C2"));
        Blk(b, 0, 0, 0.3f, 0.24f, h * 0.7f, h * 0.22f, colour);
        Blk(b, 0, 0.125f, 0.16f, 0.01f, h * 0.8f, H(3), new Color("#26A69A"));
        foreach (var x in new[] { -0.08f, 0.08f })
        {
            glass.Box(new Vector3(x, h * 0.62f, 0.14f), new Vector3(0.06f, H(5), 0.06f), new Color("#FFFDE7", 0.7f));
        }
    }

    /// <summary>A cot: a clear plastic tub on a wheeled trolley with a shelf. Returns the mattress height.</summary>
    public static float Cot(MeshBatch b, MeshBatch glass, float w, float d, float h)
    {
        // The Blender model "cot" is 0.5 m by 0.8 m and 0.9 m tall, stretched to this cot's footprint.
        var fit = new Transform3D(Basis.FromScale(new Vector3(w / 0.5f, h / 0.9f, d / 0.8f)), Vector3.Zero);
        b.Model("cot", "Main", fit);
        glass.Model("cot", "Glass", fit, new Color("#DDEFFA", 0.5f));
        return (h * 0.62f) + H(3);
    }

    /// <summary>An incubator: a cabinet on wheels, a clear hood with port holes, a display. Returns the mattress height.</summary>
    public static float Incubator(MeshBatch b, MeshBatch glass, float w, float d, float h, Color colour)
    {
        Castors(b, w, d);
        Blk(b, 0, 0, w, d, H(4), h * 0.5f, colour.Lightened(0.35f));
        Blk(b, 0, 0, w + 0.04f, d + 0.04f, H(4) + (h * 0.5f), H(3), colour);
        var mattress = H(7) + (h * 0.5f) + H(2);
        Blk(b, 0, 0, w * 0.8f, d * 0.8f, H(7) + (h * 0.5f), H(2), Colors.White);
        var hood = new Color("#DDEFFA", 0.42f);
        var hoodH = h * 0.38f;
        var y = H(7) + (h * 0.5f) + (hoodH / 2);
        glass.Box(new Vector3(0, y, 0), new Vector3(w * 0.95f, hoodH, d * 0.95f), hood);
        foreach (var x in new[] { -1f, 1f })
        {
            foreach (var z in new[] { -0.2f, 0.2f })
            {
                b.Ring(new Vector3(x * w * 0.48f, y, z * d), 0.09f, new Color("#B0BEC5"), new Basis(Vector3.Forward, Mathf.Pi / 2), 0.5f);
            }
        }

        ScreenAt(b, new Vector3(0, h * 0.4f, (d / 2) + 0.03f), w * 0.5f, H(6), new Color("#101A14"));
        return mattress;
    }

    // ------------------------------------------------------------------ toilets

    /// <summary>A toilet with its back to the wall (-z): cistern, pan, seat and flush; grab rails on an accessible one.</summary>
    private static void Toilet(MeshBatch b, float w, float d, bool accessible)
    {
        var back = -(d / 2);
        Blk(b, 0, back + 0.1f, w * 0.85f, 0.18f, H(10), H(9), Porcelain);
        Blk(b, 0, back + 0.1f, w * 0.88f, 0.2f, H(19), H(1), new Color("#E3E9EE"));
        b.Cylinder(new Vector3(0, H(4), back + 0.42f), w * 0.3f, H(8), w * 0.28f, Porcelain);
        b.Ball(new Vector3(0, H(9), back + 0.48f), new Vector3(w * 0.75f, H(4), 0.55f), Porcelain);
        b.Ring(new Vector3(0, H(11.3f), back + 0.5f), w * 0.3f, new Color("#E9EEF2"), Basis.FromScale(new Vector3(1, 1, 1.3f)), 1.3f);
        Blk(b, 0, back + 0.03f, 0.1f, 0.02f, H(16), H(2), Chrome);
        if (accessible)
        {
            var red = new Color("#C62828");
            Bar(b, new Vector3((w / 2) + 0.1f, H(20), back + 0.1f), new Vector3((w / 2) + 0.1f, H(20), back + 0.75f), 0.03f, red);
            Bar(b, new Vector3((w / 2) + 0.1f, H(20), back + 0.1f), new Vector3((w / 2) + 0.1f, H(20), back + 0.05f), 0.03f, red);
            Bar(b, new Vector3(-(w / 2) - 0.1f, H(20), back + 0.3f), new Vector3(-(w / 2) - 0.1f, H(20), back + 0.75f), 0.03f, red);
            Bar(b, new Vector3(-(w / 2) - 0.1f, H(20), back + 0.3f), new Vector3(-(w / 2) - 0.1f, H(9), back + 0.3f), 0.02f, Chrome);
        }
    }

    /// <summary>A wall-hung basin with a mixer tap, under a mirror, with the soap dispenser beside it.</summary>
    private static void WallBasin(MeshBatch b, float w, float d)
    {
        var back = -(d / 2);
        b.Ball(new Vector3(0, H(22), back + 0.26f), new Vector3(w * 0.85f, H(5), d * 0.95f), Porcelain);
        b.Cylinder(new Vector3(0, H(23.6f), back + 0.28f), w * 0.28f, H(1), d * 0.3f, new Color("#C9D6DF"));
        Bar(b, new Vector3(0, H(24), back + 0.06f), new Vector3(0, H(27), back + 0.06f), 0.02f, Chrome);
        Bar(b, new Vector3(0, H(27), back + 0.06f), new Vector3(0, H(26.5f), back + 0.16f), 0.018f, Chrome);
        Bar(b, new Vector3(0, H(10), back + 0.08f), new Vector3(0, H(20), back + 0.1f), 0.03f, Porcelain);
        Blk(b, 0, back + 0.012f, w * 0.85f, 0.02f, H(32), H(16), new Color("#B0BEC5"));
        Blk(b, 0, back + 0.024f, w * 0.75f, 0.01f, H(33), H(14), new Color("#DDEBF2"));
        Blk(b, (w / 2) - 0.02f, back + 0.04f, 0.08f, 0.06f, H(28), H(6), Colors.White);
    }

    private static void HandDryer(MeshBatch b, Color colour)
    {
        Blk(b, 0, -0.06f, 0.28f, 0.14f, H(34), H(8), colour);
        Blk(b, 0, 0.012f, 0.2f, 0.02f, H(34), H(1), Ink);
        b.Ball(new Vector3(0.08f, H(40), 0.015f), new Vector3(0.025f, 0.025f, 0.01f), new Color("#3EE07A"), small: true);
    }

    /// <summary>A cubicle: side panels and a front with its door, the engaged sign, on short feet.</summary>
    private static void Cubicle(MeshBatch b, float w, float d, float h, Color colour)
    {
        var foot = H(4);
        foreach (var side in new[] { -1f, 1f })
        {
            Blk(b, side * ((w / 2) - 0.02f), 0, 0.04f, d, foot, h - foot, colour);
            foreach (var z in new[] { -(d / 2) + 0.05f, (d / 2) - 0.05f })
            {
                Bar(b, new Vector3(side * ((w / 2) - 0.02f), 0, z), new Vector3(side * ((w / 2) - 0.02f), foot, z), 0.02f, Chrome);
            }
        }

        // The front: a fixed strip and the door, slightly open, with its lock.
        Blk(b, -(w / 2) + 0.08f, (d / 2) - 0.02f, 0.12f, 0.04f, foot, h - foot, colour);
        Blk(b, 0.06f, (d / 2) - 0.02f, w - 0.24f, 0.04f, foot, h - foot, colour.Lightened(0.08f));
        Blk(b, 0.06f, (d / 2) + 0.005f, w - 0.32f, 0.01f, foot + H(2), h - foot - H(4), colour.Lightened(0.15f));
        Blk(b, -(w / 2) + 0.24f, (d / 2) + 0.012f, 0.06f, 0.02f, h * 0.52f, H(2), Chrome);
        Blk(b, -(w / 2) + 0.24f, (d / 2) + 0.022f, 0.04f, 0.005f, h * 0.52f + H(0.5f), H(1), new Color("#43A047"));
        // A head rail along the top of the front; no roof, so you can see in from above.
        Blk(b, 0, (d / 2) - 0.02f, w, 0.06f, h, H(1), Chrome);
    }

    // ------------------------------------------------------------------ café

    private static readonly Color[] Snacks =
    {
        new("#E53935"), new("#FDD835"), new("#43A047"), new("#1E88E5"), new("#FB8C00"), new("#8E24AA"), new("#F5F5F5"), new("#6D4C41"),
    };

    /// <summary>A vending machine: <paramref name="kind"/> 0 snacks, 1 cold drinks, 2 hot drinks.</summary>
    private static void Vending(MeshBatch b, MeshBatch glass, float w, float d, float h, int kind)
    {
        var cabinet = kind switch { 0 => new Color("#C8402F"), 1 => new Color("#1F6FC4"), _ => new Color("#4A342B") };
        Blk(b, 0, 0, w, d, H(2), h - H(2), cabinet);
        Blk(b, 0, 0, w - 0.02f, d - 0.02f, 0, H(2), Ink);
        var front = d / 2;
        if (kind == 2)
        {
            // Hot drinks: a big picture of a steaming cup, the buttons and the cup bay.
            Blk(b, -0.08f, front + 0.005f, w * 0.6f, 0.01f, h * 0.45f, h * 0.45f, new Color("#7B5B4B"));
            b.Cylinder(new Vector3(-0.08f, h * 0.62f, front + 0.012f), 0.16f, 0.01f, 0.16f, new Color("#F5F0E8"), new Basis(Vector3.Right, Mathf.Pi / 2));
            b.Cylinder(new Vector3(-0.08f, h * 0.62f, front + 0.018f), 0.12f, 0.01f, 0.12f, new Color("#6D4C41"), new Basis(Vector3.Right, Mathf.Pi / 2));
            Blk(b, -0.08f, front - 0.04f, 0.3f, 0.08f, h * 0.2f, H(8), Ink);
        }
        else
        {
            // The glass front with rows of snacks or bottles, and the delivery flap.
            Blk(b, -0.08f, front - 0.06f, w * 0.68f, 0.04f, h * 0.22f, h * 0.7f, new Color("#1A2128"));
            for (var row = 0; row < 5; row++)
            {
                for (var col = 0; col < 4; col++)
                {
                    var c = Snacks[(row * 3 + col + kind) % Snacks.Length];
                    var x = -0.08f - (w * 0.3f) + 0.06f + (col * w * 0.16f);
                    var y = h * 0.26f + (row * h * 0.13f);
                    if (kind == 1)
                    {
                        b.Cylinder(new Vector3(x, y + H(3), front - 0.03f), 0.035f, H(6), 0.035f, c);
                    }
                    else
                    {
                        Blk(b, x, front - 0.03f, 0.09f, 0.03f, y, H(5), c);
                    }
                }

                Blk(b, -0.08f, front - 0.04f, w * 0.66f, 0.05f, h * 0.24f + (row * h * 0.13f), H(0.5f), Chrome);
            }

            glass.Box(new Vector3(-0.08f, h * 0.57f, front + 0.005f), new Vector3(w * 0.68f, h * 0.7f, 0.01f), new Color("#CFE8F5", 0.25f));
            Blk(b, -0.08f, front + 0.005f, w * 0.5f, 0.01f, H(6), h * 0.08f, Ink);
        }

        // The payment panel down the right: display, keypad and card reader; the name across the top.
        Blk(b, w * 0.36f, front + 0.005f, w * 0.2f, 0.01f, h * 0.35f, h * 0.45f, new Color("#2B2F33"));
        Blk(b, w * 0.36f, front + 0.012f, w * 0.14f, 0.005f, h * 0.7f, H(3), new Color("#4FC3F7"));
        for (var i = 0; i < 3; i++)
        {
            Blk(b, w * 0.36f, front + 0.012f, w * 0.14f, 0.005f, h * 0.5f + (i * H(3)), H(1.5f), new Color("#B0BEC5"));
        }

        Blk(b, 0, front + 0.005f, w * 0.9f, 0.01f, h - H(8), H(6), cabinet.Lightened(0.3f));
        Blk(b, 0, 0, w + 0.01f, d + 0.01f, h - H(1), H(1), cabinet.Lightened(0.15f));
    }

    // ------------------------------------------------------------------ cleaning room

    /// <summary>UK hospital cleaning colour code: red for toilets, blue for general areas, green for kitchens, yellow for isolation.</summary>
    private static readonly Color[] CleanCodes = { new("#D32F2F"), new("#1E6FD9"), new("#2E9E4A"), new("#F2C230") };

    /// <summary>A deep low sink on a cupboard, for filling and emptying mop buckets, with tall taps and a tiled splashback.</summary>
    private static void CleanersSink(MeshBatch b, float w, float d, float h)
    {
        var top = h * 0.62f;
        Blk(b, 0, 0, w, d, 0, top, new Color("#E3E8EB"));
        Blk(b, 0, (d / 2) + 0.004f, 0.012f, 0.01f, H(3), top - H(6), new Color("#B7C1C8"));
        foreach (var x in new[] { -(w / 4), w / 4 })
        {
            Blk(b, x, (d / 2) + 0.01f, 0.08f, 0.02f, top - H(9), H(1.2f), Steel);
        }

        // The deep steel sink, its dark inside, and the grille the bucket stands on.
        Blk(b, 0, 0, w + 0.04f, d + 0.02f, top, H(7), Steel);
        Blk(b, 0, 0.02f, w - 0.12f, d - 0.12f, top + H(7) - 0.004f, 0.006f, new Color("#4D5A63"));
        for (var i = -2; i <= 2; i++)
        {
            Blk(b, i * (w - 0.2f) / 5, 0.02f, 0.02f, d - 0.16f, top + H(7), 0.01f, new Color("#9AA6AE"));
        }

        Blk(b, 0, -(d / 2) + 0.015f, w + 0.04f, 0.03f, top + H(7), H(22), new Color("#F4F7F8"));
        for (var row = 0; row < 4; row++)
        {
            Blk(b, 0, -(d / 2) + 0.032f, w + 0.04f, 0.004f, top + H(7) + H(5.5f * (row + 1)), H(0.4f), new Color("#C9D3D9"));
        }

        foreach (var (x, c) in new[] { (-(w / 4), new Color("#1E88E5")), (w / 4, new Color("#E53935")) })
        {
            Bar(b, new Vector3(x, top + H(16), -(d / 2) + 0.07f), new Vector3(x, top + H(16), -(d / 2) + 0.2f), 0.022f, Chrome);
            Bar(b, new Vector3(x, top + H(16), -(d / 2) + 0.2f), new Vector3(x, top + H(12), -(d / 2) + 0.22f), 0.018f, Chrome);
            b.Ball(new Vector3(x, top + H(17.5f), -(d / 2) + 0.07f), new Vector3(0.06f, 0.03f, 0.06f), c, small: true);
        }

        // A red hazard notice and a bottle of disinfectant on the side.
        Blk(b, 0, -(d / 2) + 0.035f, 0.22f, 0.005f, top + H(22), H(7), new Color("#FFFFFF"));
        Blk(b, 0, -(d / 2) + 0.038f, 0.18f, 0.004f, top + H(26), H(1.5f), new Color("#D32F2F"));
        b.Cylinder(new Vector3((w / 2) - 0.06f, top + H(7) + H(4), -(d / 2) + 0.1f), 0.035f, H(8), 0.035f, new Color("#F2C230"));
    }

    /// <summary>Wire shelving stacked with colour-coded spray bottles, toilet rolls, blue paper and bin bags.</summary>
    private static void CleaningShelves(MeshBatch b, float w, float d, float h, Color colour, int seed)
    {
        foreach (var (x, z) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            Bar(b, new Vector3(x * ((w / 2) - 0.02f), 0, z * ((d / 2) - 0.02f)), new Vector3(x * ((w / 2) - 0.02f), h, z * ((d / 2) - 0.02f)), 0.014f, colour.Darkened(0.2f));
        }

        for (var i = 0; i < 4; i++)
        {
            var y = H(3) + (i * (h - H(4)) / 3.2f);
            Blk(b, 0, 0, w, d, y, H(0.8f), colour);
            for (var k = 0; k < 6; k++)
            {
                var x = -(w / 2) + 0.1f + (k * (w - 0.2f) / 5.5f);
                var pick = (k + (i * 2) + seed) % 5;
                switch (i == 0 ? 4 : pick)
                {
                    case 0:
                    case 1:
                        // A trigger spray bottle.
                        var c = CleanCodes[(k + i + seed) % CleanCodes.Length];
                        b.Cylinder(new Vector3(x, y + H(5), 0.02f), 0.035f, H(8), 0.035f, c.Lightened(0.15f));
                        Blk(b, x, 0.02f, 0.03f, 0.05f, y + H(9), H(2.5f), Colors.White);
                        break;
                    case 2:
                        b.Cylinder(new Vector3(x, y + H(4), 0), 0.05f, H(7), 0.05f, new Color("#F7F7F2"));
                        b.Cylinder(new Vector3(x, y + H(11), 0), 0.05f, H(7), 0.05f, new Color("#F7F7F2"));
                        break;
                    case 3:
                        b.Cylinder(new Vector3(x, y + H(5), 0), 0.07f, H(9), 0.07f, new Color("#4F8FD8"));
                        break;
                    default:
                        // A carton of bin bags or a stack of cloths.
                        Blk(b, x, 0, 0.13f, d * 0.7f, y + H(0.8f), H(5), (k % 2 == 0 ? new Color("#3A3F44") : new Color("#E8C547")));
                        break;
                }
            }
        }
    }

    /// <summary>A rail on the wall with colour-coded mops and a brush hanging from it, heads down.</summary>
    private static void BroomRack(MeshBatch b, float w, float d, float h, Color colour)
    {
        var back = -(d / 2);
        Blk(b, 0, back + 0.02f, w, 0.03f, h * 0.8f, H(2.5f), colour);
        var tools = 4;
        for (var i = 0; i < tools; i++)
        {
            var x = -(w / 2) + ((i + 0.5f) * w / tools);
            var code = CleanCodes[i % CleanCodes.Length];
            var brush = i == tools - 1;
            Bar(b, new Vector3(x, H(6), back + 0.09f), new Vector3(x, h * 0.83f, back + 0.05f), 0.018f, brush ? new Color("#C62828") : code);
            b.Ball(new Vector3(x, h * 0.81f, back + 0.06f), new Vector3(0.045f, 0.045f, 0.045f), colour.Darkened(0.2f), small: true);
            if (brush)
            {
                Blk(b, x, back + 0.1f, 0.3f, 0.06f, H(2), H(2.5f), new Color("#8D6E4E"));
                Blk(b, x, back + 0.1f, 0.3f, 0.05f, 0, H(2), new Color("#37474F"));
            }
            else
            {
                b.Ball(new Vector3(x, H(3), back + 0.1f), new Vector3(0.12f, H(6), 0.1f), new Color("#ECEFF1"));
                Blk(b, x, back + 0.1f, 0.06f, 0.05f, H(6), H(2), code);
            }
        }
    }

    /// <summary>The yellow wheeled mop bucket with its wringer, a mop standing in it and a wet floor sign folded beside.</summary>
    private static void MopBucket(MeshBatch b, float w, float d, float h, Color colour)
    {
        var bw = w * 0.8f;
        var bd = d * 0.8f;
        foreach (var (x, z) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            b.Ball(new Vector3(x * ((bw / 2) - 0.04f), H(1.2f), z * ((bd / 2) - 0.04f)), new Vector3(0.06f, H(2.4f), 0.06f), new Color("#2B2F33"), small: true);
        }

        Blk(b, 0, 0, bw, bd, H(2), h * 0.36f, colour);
        Blk(b, 0, 0.03f, bw - 0.06f, bd * 0.55f, H(2) + (h * 0.36f) - 0.004f, 0.006f, new Color("#6E8C99"));
        Blk(b, 0, -(bd / 2) + (bd * 0.2f), bw * 0.9f, bd * 0.35f, H(2) + (h * 0.36f), H(5), new Color("#7D8790"));
        Blk(b, 0, -(bd / 2) + (bd * 0.2f), 0.06f, bd * 0.5f, H(2) + (h * 0.36f) + H(5), H(1.2f), new Color("#C62828"));
        Bar(b, new Vector3(-(bw / 2), H(2) + (h * 0.36f), bd / 2), new Vector3(bw / 2, H(2) + (h * 0.36f), bd / 2), 0.014f, colour.Darkened(0.15f));

        // The mop in the wringer, its handle leaning back against the wall.
        b.Ball(new Vector3(0, H(2) + (h * 0.3f), -(bd / 2) + (bd * 0.2f)), new Vector3(0.14f, H(5), 0.1f), new Color("#ECEFF1"));
        Bar(b, new Vector3(0, H(2) + (h * 0.36f), -(bd / 2) + (bd * 0.2f)), new Vector3(0.02f, h, -(d / 2) + 0.02f), 0.018f, new Color("#3F7FD9"));
    }

    private static void WaterCooler(MeshBatch b, MeshBatch glass, float h)
    {
        Blk(b, 0, 0, 0.32f, 0.32f, 0, h * 0.65f, new Color("#ECEFF1"));
        Blk(b, 0, 0.12f, 0.2f, 0.1f, h * 0.38f, H(3), new Color("#90A4AE"));
        b.Cylinder(new Vector3(-0.05f, h * 0.48f, 0.17f), 0.02f, H(1.5f), 0.02f, new Color("#1E88E5"));
        b.Cylinder(new Vector3(0.05f, h * 0.48f, 0.17f), 0.02f, H(1.5f), 0.02f, new Color("#E53935"));
        glass.Cylinder(new Vector3(0, h * 0.65f + (h * 0.17f), 0), 0.14f, h * 0.34f, 0.14f, new Color("#81D4FA", 0.6f));
    }

    private static void FoodCounter(MeshBatch b, MeshBatch glass, float w, float d, float h, Color colour, int seed)
    {
        Blk(b, 0, 0, w - 0.04f, d - 0.04f, 0, H(3), colour.Darkened(0.5f));
        Blk(b, 0, 0, w, d, H(3), h * 0.8f - H(3), colour);
        Blk(b, 0, 0, w + 0.06f, d + 0.06f, h * 0.8f, H(2), Steel);

        // Trays of food under the sneeze guard, and the till at one end.
        var trays = Math.Max(2, (int)(w / 0.5f));
        for (var i = 0; i < trays - 1; i++)
        {
            var x = -(w / 2) + 0.3f + (i * (w - 0.8f) / Math.Max(1, trays - 2));
            Blk(b, x, 0.05f, 0.36f, d * 0.5f, h * 0.8f + H(2), H(1.5f), Snacks[(i + seed) % Snacks.Length].Lerp(new Color("#D7B98E"), 0.4f));
        }

        glass.Box(new Vector3(-0.2f, h * 0.8f + H(9), (d / 2) - 0.12f), new Vector3(w - 0.6f, H(12), 0.02f), new Color("#DDEFFA", 0.35f));
        foreach (var x in new[] { -(w / 2) + 0.12f, (w / 2) - 0.52f })
        {
            Bar(b, new Vector3(x, h * 0.8f, (d / 2) - 0.12f), new Vector3(x, h * 0.8f + H(15), (d / 2) - 0.12f), 0.015f, Chrome);
        }

        Blk(b, (w / 2) - 0.25f, -0.05f, 0.3f, 0.25f, h * 0.8f + H(2), H(4), Ink);
        ScreenAt(b, new Vector3((w / 2) - 0.25f, h * 0.8f + H(9), -0.08f), 0.28f, H(5), null, new Basis(Vector3.Up, Mathf.Pi));
    }

    private static void CafeTable(MeshBatch b, float w, float d, float h, Color colour)
    {
        b.Cylinder(new Vector3(0, H(1), 0), Math.Min(w, d) * 0.3f, H(2), Math.Min(w, d) * 0.3f, Ink);
        Bar(b, new Vector3(0, H(2), 0), new Vector3(0, h - H(2), 0), 0.04f, Chrome);
        b.Cylinder(new Vector3(0, h - H(1), 0), Math.Min(w, d) * 0.48f, H(2), Math.Min(w, d) * 0.48f, colour);
        b.Cylinder(new Vector3(0.1f, h + H(0.6f), -0.05f), 0.08f, H(0.3f), 0.08f, Colors.White);
    }
}
