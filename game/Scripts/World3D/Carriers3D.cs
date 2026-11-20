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

    /// <summary>
    /// How a bed's, trolley's or stretcher's backrest (the model's part "Back": the head end of the mattress
    /// and its pillow) hinges up: where the hinge is along it (z, in its own frame), how far under the
    /// mattress top, how far it tips up when someone sits up in it, and how high its pillow is above the mattress.
    /// </summary>
    public readonly record struct Backrest(float HingeZ, float Drop, float Raised, float Pillow)
    {
        /// <summary>The backrest's turn at <paramref name="angle"/> (radians), about its hinge under a mattress whose top is at <paramref name="top"/>.</summary>
        public Transform3D Turn(float top, float angle)
        {
            var hinge = new Vector3(0, top - Drop, HingeZ);
            return new Transform3D(Basis.Identity, hinge) * new Transform3D(new Basis(Vector3.Right, angle), Vector3.Zero) * new Transform3D(Basis.Identity, -hinge);
        }
    }

    /// <summary>The ward bed's backrest: 0.2 m from the middle towards the head (see tools/Blender/models/bed.py).</summary>
    public static readonly Backrest BedBack = new(-0.4f, Kit3D.M(0.16), Mathf.DegToRad(45), 0.1f);

    /// <summary>The A&amp;E trolley's backrest (see tools/Blender/models/trolley.py).</summary>
    public static readonly Backrest TrolleyBack = new(-0.5f, Kit3D.M(0.15), Mathf.DegToRad(40), 0.06f);

    /// <summary>The ambulance stretcher's backrest (see tools/Blender/models/stretcher.py).</summary>
    public static readonly Backrest StretcherBack = new(-0.4f, Kit3D.M(0.09), Mathf.DegToRad(40), 0.05f);

    /// <summary>
    /// A carrier's backrest at <paramref name="angle"/> (0 flat), with the gas ram that holds it up showing
    /// under it once it is raised.
    /// </summary>
    public static void Back(MeshBatch b, string model, Backrest rest, float top, float angle)
    {
        var turn = rest.Turn(top, angle);
        b.Model(model, "Back", turn * new Transform3D(Basis.FromScale(Models3D.MetreScale), Vector3.Zero));
        if (angle > 0.05f)
        {
            // The ram: from the frame under the hinge up to the underside of the backrest.
            var under = turn * new Vector3(0, top - (rest.Drop * 1.3f), rest.HingeZ - 0.45f);
            b.Rod(new Vector3(0, top - (rest.Drop * 1.6f), rest.HingeZ + 0.1f), under, 0.045f, Dark, small: true);
            foreach (var x in new[] { -0.45f, 0.45f })
            {
                b.Rod(new Vector3(x, top - rest.Drop, rest.HingeZ), turn * new Vector3(x, top - rest.Drop, rest.HingeZ - 0.9f), 0.025f, Steel, small: true);
            }
        }
    }

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
    /// A hospital bed, about 0.8 m by 2.1 m (1.6 by 4.2 tiles), from the Blender model "bed": castors, the
    /// chassis, a blue mattress with a fitted sheet, moulded head and foot boards with the obs chart,
    /// tube side rails, a handset and a pillow. A <paramref name="made"/> bed has its blanket on.
    /// </summary>
    /// <remarks>
    /// The backrest is drawn at <paramref name="back"/> radians (flat by default); null leaves it out, for a
    /// bed that draws it on its own node so it can go up and down (see <see cref="Back"/>).
    /// </remarks>
    public static float Bed(MeshBatch b, bool made, float? back = 0)
    {
        b.Model("bed");
        if (back is { } angle)
        {
            Back(b, "bed", BedBack, H(18), angle);
        }

        if (made)
        {
            BedMade(b);
        }

        return H(18);
    }

    /// <summary>The blanket and turned-down sheet of a made-up, empty bed.</summary>
    public static void BedMade(MeshBatch b) => b.Model("bed", "Made");

    // ------------------------------------------------------------------ tables and couches

    /// <summary>An imaging or operating table, or a treatment couch; returns the height of its top.</summary>
    public static float Table(MeshBatch b, string id, Color colour) => id switch
    {
        "xray_table" => XRay(b),
        "ct_table" => Ct(b, colour),
        "ct_base" => CtBase(b, colour),
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
        b.Model(Furnish3D.Equipment3D, "xray_table", Transform3D.Identity);
        return H(22);
    }

    private static float Ct(MeshBatch b, Color colour)
    {
        CtBase(b, colour);
        return CtTop(b, colour);
    }

    private static float CtBase(MeshBatch b, Color colour)
    {
        b.Model(Furnish3D.Equipment3D, "ct_base", Transform3D.Identity, colour);
        return H(21);
    }

    /// <summary>The CT table's top, which slides into the scanner and back: drawn on its own so it can move.</summary>
    public static float CtTop(MeshBatch b, Color colour)
    {
        b.Model(Furnish3D.Equipment3D, "ct_top", Transform3D.Identity, colour);
        return H(21);
    }

    private static float Operating(MeshBatch b, Color colour)
    {
        b.Model(Furnish3D.Equipment3D, "operating_table", Transform3D.Identity, colour.Darkened(0.35f).Lightened(0.08f));
        return H(23);
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
    /// An A&amp;E trolley, from the Blender model "trolley": castors, a dark base with an oxygen cylinder,
    /// a blue mattress with a white sheet, side rails up, a push bar at the head and a drip pole.
    /// +z is the way it is pushed (feet first).
    /// </summary>
    public static float Trolley(MeshBatch b, float back = 0)
    {
        b.Model("trolley");
        Back(b, "trolley", TrolleyBack, H(16), back);
        return H(16);
    }

    /// <summary>The ambulance crew's stretcher, from the Blender model "stretcher": a scissor undercarriage, a black mattress and a yellow head bar.</summary>
    public static float Stretcher(MeshBatch b, float back = 0)
    {
        b.Model("stretcher");
        Back(b, "stretcher", StretcherBack, H(16), back);
        return H(16);
    }

    /// <summary>The stretcher's two safety straps, buckled over the blanket.</summary>
    public static void Straps(MeshBatch b, float mattress)
    {
        // Over the lap and the shins, clear of the backrest when it is raised.
        foreach (var z in new[] { 0.05f, 1.2f })
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
    /// <summary>Where a wheelchair's big wheel turns on its axle (side -1 left, +1 right).</summary>
    public static Vector3 WheelchairHub(float side) => new(side * W(13), Person3D.V(10), -0.04f);

    /// <summary>
    /// One big wheel of a wheelchair, built round its own hub so it can turn as the chair is pushed:
    /// the tyre, the spokes and the push rim.
    /// </summary>
    public static void WheelchairWheel(MeshBatch b, float side) =>
        b.Model("wheelchair", "Wheel", new Transform3D(new Basis(Vector3.Up, side < 0 ? Mathf.Pi : 0), Vector3.Zero));

    public static void Wheelchair(MeshBatch b, float scale = 1f, bool wheels = true)
    {
        // The model is made in world units round the seated figure (see tools/Blender/models/wheelchair.py).
        b.Model("wheelchair", "Frame", Transform3D.Identity);
        b.Model("wheelchair", "Seat", new Transform3D(Basis.Identity, new Vector3(0, Person3D.V(13) * (scale - 1), 0)));
        if (!wheels)
        {
            return;
        }

        foreach (var side in new[] { -1f, 1f })
        {
            var at = b.Frame;
            b.Frame = at * new Transform3D(Basis.Identity, WheelchairHub(side));
            WheelchairWheel(b, side);
            b.Frame = at;
        }
    }
}
