using BlueprintHospital.Hospital;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// Beds, tables, trolleys, stretchers and wheelchairs: the things patients lie or sit on. Each is
/// built in its own frame with the head at the -z end (beds face north when not turned), and
/// returns the height of its mattress top for the lying figure.
/// </summary>
public static class Carriers3D
{
    private static readonly Color Steel = new("#B9C3CB");
    private static readonly Color Tyre = new("#22272B");
    private static readonly Color Dark = new("#3A434B");
    private static readonly Color Sheet = new("#F4F7F9");

    private static float H(double px) => Kit3D.H(px);

    private static float W(double px) => Person3D.W(px);

    /// <summary>A small castor wheel on the floor at (x, z).</summary>
    private static void Castor(MeshBatch b, float x, float z)
    {
        b.Rod(new Vector3(x, H(2), z), new Vector3(x, H(5), z), 0.025f, new Color("#6E7A84"), small: true);
        b.Cylinder(new Vector3(x, H(2.2f), z), 0.08f, 0.06f, 0.08f, Tyre, new Basis(Vector3.Forward, Mathf.Pi / 2));
    }

    /// <summary>A rod between two points, for rails and frames.</summary>
    private static void Bar(MeshBatch b, Vector3 from, Vector3 to, float radius, Color colour) => b.Rod(from, to, radius, colour, small: true);

    // ------------------------------------------------------------------ hospital bed

    /// <summary>
    /// A hospital bed, about 0.8 m by 2.1 m (1.6 by 4.2 tiles): castors, a dark chassis, the frame, a
    /// blue mattress with a fitted sheet, the head and foot boards (the obs chart on the foot board),
    /// half-length cot sides at the head, a handset and a pillow. An empty bed has a folded blanket at the foot.
    /// </summary>
    public static float Bed(MeshBatch b, bool made)
    {
        const float deck = 12f;
        const float top = 18f;
        foreach (var (x, z) in new[] { (-0.65f, -1.9f), (0.65f, -1.9f), (-0.65f, 1.9f), (0.65f, 1.9f) })
        {
            Castor(b, x, z);
        }

        b.Box(new Vector3(0, H(5.5f), 0), new Vector3(0.9f, H(5), 3.4f), new Color("#6F7B85"));
        b.Box(new Vector3(0, H(deck - 2), 0), new Vector3(1.6f, H(4), 4.2f), new Color("#B7C2CB"));
        b.Box(new Vector3(0, H(deck + 2.6f), 0), new Vector3(1.48f, H(5.2f), 4.08f), new Color("#4F7BA8"));
        b.Box(new Vector3(0, H(top - 1.1f), 0), new Vector3(1.5f, H(2.2f), 4.1f), Sheet);

        // Head board (taller) and foot board, with the skirting band and the obs chart.
        var board = new Color("#8FA6BA");
        b.Box(new Vector3(0, H(13), -2.1f + 0.11f), new Vector3(1.7f, H(26), 0.22f), board);
        b.Box(new Vector3(0, H(2.5f), -2.1f + 0.11f), new Vector3(1.71f, H(5), 0.225f), new Color("#5E7285"));
        b.Box(new Vector3(0, H(8), 2.1f - 0.11f), new Vector3(1.7f, H(16), 0.22f), board);
        b.Box(new Vector3(0, H(2.5f), 2.1f - 0.11f), new Vector3(1.71f, H(5), 0.225f), new Color("#5E7285"));
        b.Box(new Vector3(0, H(26.2f), -2.1f + 0.11f), new Vector3(1.72f, H(0.8f), 0.24f), new Color("#B9C9D6"));
        b.Box(new Vector3(0, H(16.2f), 2.1f - 0.11f), new Vector3(1.72f, H(0.8f), 0.24f), new Color("#B9C9D6"));
        b.Box(new Vector3(0, H(9.5f), 2.1f + 0.005f), new Vector3(0.36f, H(9), 0.02f), new Color("#F7F4E8"));
        b.Box(new Vector3(0, H(11), 2.1f + 0.016f), new Vector3(0.24f, H(0.8f), 0.01f), new Color("#6D8BA5"));

        // Cot sides along the head half, two bars on posts, with the handset on one.
        foreach (var side in new[] { -1f, 1f })
        {
            var x = side * 0.79f;
            foreach (var z in new[] { -1.5f, -0.1f })
            {
                Bar(b, new Vector3(x, H(deck), z), new Vector3(x, H(top + 7), z), 0.022f, new Color("#AEB9C2"));
            }

            b.Box(new Vector3(x, H(top + 7), -0.8f), new Vector3(0.05f, H(2), 1.8f), new Color("#E3E9EE"));
            b.Box(new Vector3(x, H(top + 2.2f), -0.8f), new Vector3(0.05f, H(1.5f), 1.6f), new Color("#D5DDE3"));
        }

        b.Box(new Vector3(0.83f, H(top + 3), -0.8f), new Vector3(0.04f, H(6), 0.08f), new Color("#5A6670"));
        b.Ball(new Vector3(0.855f, H(top + 4), -0.8f), new Vector3(0.02f, 0.03f, 0.03f), new Color("#3EE07A"), small: true);

        // The pillow at the head, and on an empty bed a folded blanket at the foot.
        b.Ball(new Vector3(0, H(top + 2.5f), -1.45f), new Vector3(1.1f, H(5.5f), 0.78f), Colors.White);
        if (made)
        {
            b.Box(new Vector3(0, H(top + 1.5f), 1.45f), new Vector3(1.3f, H(3), 0.7f), new Color("#BFD3E6"));
        }

        return H(top);
    }

    // ------------------------------------------------------------------ tables and couches

    /// <summary>An imaging or operating table, or a treatment couch; returns the height of its top.</summary>
    public static float Table(MeshBatch b, string id, Color colour) => id switch
    {
        "xray_table" => XRay(b),
        "ct_table" => Ct(b, colour),
        "operating_table" => Operating(b, colour),
        "cath_lab_table" => CathLab(b),
        _ => Couch(b, colour),
    };

    private static float Couch(MeshBatch b, Color colour)
    {
        const float top = 21f;
        foreach (var (x, z) in new[] { (-0.6f, -1.8f), (0.6f, -1.8f), (0.6f, 1.8f), (-0.6f, 1.8f) })
        {
            Castor(b, x, z);
            b.Box(new Vector3(x, H(8.5f), z), new Vector3(0.14f, H(11), 0.14f), new Color("#AEB8C1"));
        }

        foreach (var side in new[] { -1f, 1f })
        {
            b.Box(new Vector3(side * 0.6f, H(6.8f), 0), new Vector3(0.08f, H(1.6f), 3.6f), new Color("#AEB8C1"));
            b.Box(new Vector3(side * 0.77f, H(1.75f), 0.4f), new Vector3(0.3f, H(1.5f), 0.3f), new Color("#F9C22E"));
        }

        b.Box(new Vector3(0, H(8.5f), 0), new Vector3(0.6f, H(7), 1.0f), Dark);
        b.Box(new Vector3(0, H(14.5f), 0), new Vector3(1.36f, H(2), 3.96f), new Color("#8C979F"));
        var pad = colour.Lightened(0.06f);
        b.Box(new Vector3(0, H(18.25f), 0.575f), new Vector3(1.44f, H(5.5f), 2.95f), pad);
        b.Box(new Vector3(0, H(top + 0.1f), 0.55f), new Vector3(0.92f, H(0.3f), 2.8f), new Color("#F6F8F4"));
        b.Box(new Vector3(0, H(19.25f), -1.49f), new Vector3(1.44f, H(7.5f), 1.12f), pad.Lightened(0.05f));
        b.Ball(new Vector3(0, H(24.5f), -1.6f), new Vector3(1.0f, H(3.5f), 0.62f), Colors.White);
        b.Cylinder(new Vector3(0, H(17), 2.1f), 0.06f, 0.9f, 0.06f, new Color("#F6F8F4"), new Basis(Vector3.Forward, Mathf.Pi / 2));
        return H(top);
    }

    private static float XRay(MeshBatch b)
    {
        const float top = 22f;
        var white = new Color("#EEF2F5");
        b.Box(new Vector3(0, H(8.25f), 0), new Vector3(1.32f, H(16.5f), 3.2f), white);
        b.Box(new Vector3(0, H(1.5f), 0), new Vector3(1.33f, H(3), 3.21f), Dark);
        foreach (var side in new[] { -1f, 1f })
        {
            // The film tray in the side, and a control strip.
            b.Box(new Vector3(side * 0.665f, H(11), 0), new Vector3(0.02f, H(5), 1.6f), Dark);
            b.Box(new Vector3(side * 0.67f, H(11), -1.2f), new Vector3(0.02f, H(3), 0.3f), new Color("#7FA7C4"));
        }

        b.Box(new Vector3(0, H(20.25f), 0), new Vector3(1.6f, H(3.5f), 4.1f), white);
        b.Box(new Vector3(0, H(top + 0.1f), 0), new Vector3(1.24f, H(0.3f), 3.76f), new Color("#4E5B67"));
        foreach (var side in new[] { -1f, 1f })
        {
            Bar(b, new Vector3(side * 0.82f, H(top + 1.5f), -1.6f), new Vector3(side * 0.82f, H(top + 1.5f), 1.6f), 0.025f, new Color("#AEB8C1"));
        }

        return H(top);
    }

    private static float Ct(MeshBatch b, Color colour)
    {
        const float top = 21f;
        b.Box(new Vector3(0, H(7.75f), 0.7f), new Vector3(1.12f, H(15.5f), 2.6f), colour);
        b.Box(new Vector3(0, H(1.5f), 0.7f), new Vector3(1.13f, H(3), 2.61f), Dark);
        b.Box(new Vector3(0, H(7), 0.7f), new Vector3(1.14f, H(1.3f), 2.4f), new Color("#7FA7C4"));
        b.Box(new Vector3(0, H(19), 0), new Vector3(1.28f, H(4), 4.1f), colour.Lightened(0.05f));
        b.Box(new Vector3(0, H(top + 0.1f), 0), new Vector3(0.94f, H(0.3f), 3.9f), new Color("#7FA7C4"));
        foreach (var side in new[] { -1f, 1f })
        {
            b.Box(new Vector3(side * 0.39f, H(top + 1.5f), -1.7f), new Vector3(0.14f, H(3), 0.5f), new Color("#56616B"));
        }

        return H(top);
    }

    private static float Operating(MeshBatch b, Color colour)
    {
        const float top = 23f;
        var pad = colour.Darkened(0.35f);
        b.Box(new Vector3(0, H(1.75f), 0.1f), new Vector3(1.2f, H(3.5f), 2.2f), new Color("#56616B"));
        b.Box(new Vector3(0, H(10.5f), 0.05f), new Vector3(0.72f, H(14), 1.1f), new Color("#C3CCD3"));
        b.Box(new Vector3(0, H(18.1f), 0), new Vector3(1.12f, H(1.8f), 3.7f), new Color("#AEB8C1"));
        foreach (var (from, to, half) in new[] { (-2.05f, -1.5f, 0.45f), (-1.42f, -0.2f, 0.62f), (-0.12f, 0.7f, 0.62f), (0.78f, 2.05f, 0.62f) })
        {
            b.Box(new Vector3(0, H(21), (from + to) / 2), new Vector3(2 * half, H(4), to - from), pad.Lightened(0.08f));
        }

        b.Box(new Vector3(1.095f, H(21.25f), -1.0f), new Vector3(0.95f, H(2.5f), 0.38f), pad.Lightened(0.08f));
        foreach (var side in new[] { -1f, 1f })
        {
            Bar(b, new Vector3(side * 0.66f, H(20), -1.4f), new Vector3(side * 0.66f, H(20), 2.05f), 0.025f, new Color("#C9D2D9"));
        }

        return H(top);
    }

    private static float CathLab(MeshBatch b)
    {
        const float top = 21f;
        var white = new Color("#EEF2F5");
        b.Box(new Vector3(0, H(8.75f), 1.35f), new Vector3(1.2f, H(17.5f), 1.5f), white);
        b.Box(new Vector3(0, H(1.25f), 1.35f), new Vector3(1.21f, H(2.5f), 1.51f), Dark);
        b.Box(new Vector3(0, H(6.6f), 1.35f), new Vector3(1.22f, H(1.2f), 1.1f), new Color("#AD1457"));
        b.Box(new Vector3(0, H(19), -0.05f), new Vector3(1.28f, H(3), 4.3f), new Color("#3F4A54"));
        b.Box(new Vector3(0, H(top - 0.3f), -0.1f), new Vector3(1.04f, H(0.6f), 4.0f), new Color("#7FA7C4"));
        b.Box(new Vector3(0.75f, H(18.5f), 1.5f), new Vector3(0.22f, H(4), 0.6f), new Color("#2B343C"));
        return H(top);
    }

    // ------------------------------------------------------------------ trolleys and stretchers

    /// <summary>
    /// An A&amp;E trolley: castors, a dark base on two hydraulic columns with an oxygen cylinder in the
    /// tray, a steel frame, a blue mattress with a white sheet, side rails up, a push bar at the head
    /// and a folded drip pole. +z is the way it is pushed (feet first).
    /// </summary>
    public static float Trolley(MeshBatch b)
    {
        const float frame = 13f;
        foreach (var (x, z) in new[] { (-0.5f, -1.6f), (0.5f, -1.6f), (-0.5f, 1.6f), (0.5f, 1.6f) })
        {
            Castor(b, x, z);
        }

        b.Box(new Vector3(0, H(5), 0), new Vector3(1.2f, H(4), 3.5f), new Color("#4E5A64"));
        b.Rod(new Vector3(0, H(8.5f), 0.6f), new Vector3(0, H(8.5f), 1.5f), 0.12f, Colors.White, small: true);
        foreach (var z in new[] { -0.9f, 0.9f })
        {
            b.Box(new Vector3(0, H(8), z), new Vector3(0.28f, H(4), 0.28f), new Color("#7F8A93"));
        }

        b.Box(new Vector3(0, H(frame - 2), 0), new Vector3(1.4f, H(2), 4.0f), Steel);
        b.Box(new Vector3(0, H(frame + 1), 0), new Vector3(1.24f, H(4), 3.84f), new Color("#3E6A96"));
        b.Box(new Vector3(0, H(frame + 2.6f), 0), new Vector3(1.25f, H(1), 3.85f), Sheet);
        foreach (var side in new[] { -1f, 1f })
        {
            var x = side * 0.74f;
            foreach (var z in new[] { -1.3f, 0f, 1.3f })
            {
                Bar(b, new Vector3(x, H(frame - 1), z), new Vector3(x, H(frame + 7), z), 0.018f, new Color("#8C979F"));
            }

            Bar(b, new Vector3(x, H(frame + 7), -1.5f), new Vector3(x, H(frame + 7), 1.5f), 0.028f, Steel);
            Bar(b, new Vector3(x, H(frame + 3.5f), -1.5f), new Vector3(x, H(frame + 3.5f), 1.5f), 0.02f, Steel);
        }

        foreach (var side in new[] { -1f, 1f })
        {
            Bar(b, new Vector3(side * 0.62f, H(frame), -2.1f), new Vector3(side * 0.62f, H(frame + 12), -2.1f), 0.025f, new Color("#7F8A93"));
        }

        Bar(b, new Vector3(-0.64f, H(frame + 12), -2.1f), new Vector3(0.64f, H(frame + 12), -2.1f), 0.045f, new Color("#4A545D"));
        Bar(b, new Vector3(0.7f, H(frame), -1.95f), new Vector3(0.7f, H(frame + 26), -1.95f), 0.018f, Steel);
        Bar(b, new Vector3(0.6f, H(frame + 26), -1.95f), new Vector3(0.8f, H(frame + 26), -1.95f), 0.018f, Steel);
        return H(frame + 3);
    }

    /// <summary>The ambulance crew's stretcher: a scissor undercarriage, a black mattress and a yellow head bar.</summary>
    public static float Stretcher(MeshBatch b)
    {
        const float frame = 14f;
        var steel = new Color("#C3CBD2");
        foreach (var (x, z) in new[] { (-0.55f, -1.7f), (0.55f, -1.7f), (-0.55f, 1.7f), (0.55f, 1.7f) })
        {
            Castor(b, x, z);
        }

        b.Box(new Vector3(0, H(4), 0), new Vector3(1.2f, H(2), 3.6f), new Color("#7F8A93"));
        foreach (var side in new[] { -0.55f, 0.55f })
        {
            Bar(b, new Vector3(side, H(4), -1.4f), new Vector3(side, H(frame - 2), 1.4f), 0.03f, steel);
            Bar(b, new Vector3(side, H(4), 1.4f), new Vector3(side, H(frame - 2), -1.4f), 0.03f, steel);
        }

        b.Box(new Vector3(0, H(frame - 1), 0), new Vector3(1.32f, H(2), 4.0f), steel);
        b.Box(new Vector3(0, H(frame + 1.5f), 0), new Vector3(1.2f, H(3), 3.84f), new Color("#2E3338"));
        foreach (var side in new[] { -0.55f, 0.55f })
        {
            Bar(b, new Vector3(side, H(frame), -1.9f), new Vector3(side, H(frame + 3), -2.15f), 0.022f, steel);
            Bar(b, new Vector3(side * 1.27f, H(frame + 3), -1.4f), new Vector3(side * 1.27f, H(frame + 3), 1.4f), 0.022f, steel);
        }

        Bar(b, new Vector3(-0.62f, H(frame + 3), -2.15f), new Vector3(0.62f, H(frame + 3), -2.15f), 0.045f, new Color("#F2C230"));
        return H(frame + 2);
    }

    /// <summary>The stretcher's two safety straps, buckled over the blanket.</summary>
    public static void Straps(MeshBatch b, float mattress)
    {
        foreach (var z in new[] { -0.7f, 1.2f })
        {
            b.Box(new Vector3(0, mattress + H(4), z), new Vector3(1.3f, H(1.2f), 0.1f), new Color("#1C1F22"));
            b.Box(new Vector3(0, mattress + H(4.7f), z), new Vector3(0.12f, H(1.2f), 0.13f), new Color("#F2C230"));
        }
    }

    // ------------------------------------------------------------------ wheelchair

    /// <summary>
    /// A hospital wheelchair round someone sitting in it, facing +z, with its seat under their hips:
    /// big wheels with push rims, a navy sling seat and back, push handles, armrests, castors and footplates.
    /// </summary>
    public static void Wheelchair(MeshBatch b, float scale = 1f)
    {
        var frame = new Color("#8C979F");
        var seat = new Color("#2B4A6B");
        var seatTop = Person3D.V(13) * scale;
        var sideways = new Basis(Vector3.Forward, Mathf.Pi / 2);
        foreach (var side in new[] { -1f, 1f })
        {
            var hub = new Vector3(side * W(13), Person3D.V(10), -0.04f);
            b.Ring(hub, Person3D.V(9.2f), Tyre, sideways, 1.4f);
            b.Cylinder(hub, Person3D.V(8.4f), 0.015f, Person3D.V(8.4f), new Color("#9AA5AE"), sideways);
            b.Ring(hub + new Vector3(side * 0.035f, 0, 0), Person3D.V(7.6f), new Color("#C9D2D9"), sideways, 0.6f);
            b.Cylinder(hub, 0.03f, 0.06f, 0.03f, new Color("#C9D2D9"), sideways);

            // Frame up to the push handles, armrest, front castor and footplate.
            var x = side * W(9.5f);
            Bar(b, new Vector3(x, Person3D.V(8), -0.18f), new Vector3(x, Person3D.V(44), -0.2f), 0.018f, frame);
            Bar(b, new Vector3(x, Person3D.V(44), -0.2f), new Vector3(x, Person3D.V(44), -0.3f), 0.024f, Figures.Shoes);
            Bar(b, new Vector3(side * W(10.5f), seatTop, 0.05f), new Vector3(side * W(10.5f), Person3D.V(24), 0.05f), 0.016f, frame);
            b.Box(new Vector3(side * W(10.5f), Person3D.V(25), 0.03f), new Vector3(0.06f, Person3D.V(2), 0.28f), Figures.Shoes);
            Bar(b, new Vector3(x, seatTop, 0.12f), new Vector3(side * 0.16f, Person3D.V(3), 0.34f), 0.016f, frame);
            Castor(b, side * 0.16f, 0.36f);
            b.Box(new Vector3(side * 0.09f, Person3D.V(2.5f), 0.42f), new Vector3(0.15f, 0.015f, 0.12f), new Color("#3A4148"));
        }

        b.Box(new Vector3(0, seatTop - Person3D.V(1.2f), 0.06f), new Vector3(W(20), Person3D.V(2.4f), 0.4f), seat);
        b.Box(new Vector3(0, Person3D.V(29), -0.19f), new Vector3(W(19), Person3D.V(24), 0.03f), seat);
    }
}
