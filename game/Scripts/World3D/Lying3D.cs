using System;
using BlueprintHospital.Hospital;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>What a patient lying in bed is doing with their hands by day.</summary>
public enum InBed
{
    None,
    Book,
    Phone,
    Meal,
}

/// <summary>
/// A patient in bed, on a trolley or a stretcher, or lying on a table or the floor, in the "Friendly" style
/// (people style A) with the bed option Darren picked on 2026-11-20 (option B, "sat up"): a body under the
/// blanket with its shoulders, tummy, legs and toes, the head sunk into the pillow, and the arms on top of
/// the covers with the ID band on the wrist. On a carrier with a backrest (<see cref="Carriers3D.Backrest"/>)
/// raised, everything above the hinge sits up with it, so their face looks out at the camera.
/// Built in its own frame: the origin is the middle of the mattress at its top, +z runs to the feet, the head
/// rests at the -z end. Children are smaller; sat up, they slide down so their hips are at the hinge.
/// </summary>
public static class Lying3D
{
    private static readonly Color Gown = new("#C3D8EA");
    private static readonly Color GownPrint = new("#8FB3D4");
    private static readonly Color ChildGown = new("#F7E3A1");
    private static readonly Color ChildPrint = new("#E5C46A");
    private static readonly Color Sheet = new("#F7F9FA");
    private static readonly Color Plaster = new("#F5F5F0");

    private static float W(double px) => Person3D.W(px);

    private static float V(double px) => Person3D.V(px);

    /// <summary>
    /// Builds the figure. <paramref name="band"/> is the wristband's triage colour (white band if null); eyes are
    /// closed unless <paramref name="awake"/>. <paramref name="doing"/> raises a hand (or both) to hold a book, a
    /// phone or a fork over the tray; those hands are a node named "Holding" pivoting at the elbow, so the bed can
    /// move them. <paramref name="cast"/> (0 to 1) is how much of the forearm is plastered (negative: the lower leg).
    /// <paramref name="rest"/> is the carrier's backrest and <paramref name="angle"/> how far it is raised; with no
    /// backrest (a table, the floor) they lie flat with no pillow.
    /// </summary>
    public static Node3D Build(PersonLook look, Color blanket, Color? band, BabyLook[]? onChest = null, bool awake = false, InBed doing = InBed.None, float cast = 0,
        Carriers3D.Backrest? rest = null, float angle = 0)
    {
        var node = new Node3D { Name = "Lying" };
        var sz = Mathf.Clamp(look.Scale, 0.5f, 1f);
        var kid = sz < 0.8f;
        var gown = kid ? ChildGown : Gown;
        var skin = look.Skin;
        var raised = rest is not null && angle > 0.05f;
        var cuddling = onChest is { Length: > 0 };
        if (cuddling)
        {
            doing = InBed.None;
        }

        // ---- the body plan along the bed, from the head (t = 0) to the toes (t = 1)
        var headR = 0.215f * Mathf.Lerp(1f, look.HeadScale, 0.5f) * (0.75f + (0.25f * sz));
        var length = 2.55f * sz;
        var hinge = rest?.HingeZ ?? 0f;
        var headZ = -1.45f;
        if (raised)
        {
            // Sat up, a child slides down the bed until their bottom is at the hinge.
            headZ = Mathf.Max(headZ, hinge + 0.12f - (0.52f * length));
        }

        float Z(float t) => headZ + (t * length);
        var shZ = Z(0.15f);
        var chestEdge = Z(0.3f);
        var hipZ = Z(0.52f);
        var kneeZ = Z(0.76f);
        var footZ = Z(0.97f);
        var shW = 0.46f * Mathf.Lerp(sz, 1f, 0.3f) * (0.85f + (0.15f * look.Build));
        const float coverTop = 0.24f;

        // Everything above the hinge turns up with the backrest.
        var up = raised ? rest!.Value.Turn(0, angle) : Transform3D.Identity;
        var down = up.AffineInverse();
        var upper = new Node3D { Name = "Upper", Transform = up };
        node.AddChild(upper);
        var u = new MeshBatch();

        // ---- shoulders and chest in the gown, the neck tucked in
        var torsoLen = (hipZ - shZ) + 0.25f;
        u.Shape("torso", new Vector3(0, 0.12f, shZ + (torsoLen / 2) - 0.08f), new Vector3(shW * 2.15f, torsoLen, 0.36f * Mathf.Lerp(sz, 1f, 0.4f)), gown, new Basis(Vector3.Right, -Mathf.Pi / 2));
        for (var i = 0; i < 3; i++)
        {
            // A little print on the gown, so it reads as a hospital gown.
            u.Ball(new Vector3((-0.18f + (i * 0.18f)) * sz, 0.3f * Mathf.Lerp(sz, 1f, 0.4f), shZ + 0.14f), new Vector3(0.05f, 0.012f, 0.05f), kid ? ChildPrint : GownPrint, small: true);
        }

        // On the pillow if their head reaches it (a child sat up may have slid off it).
        var pillow = (rest?.Pillow ?? 0f) * Mathf.Clamp(1f - ((headZ + 1.45f) / 0.3f), 0f, 1f);
        var head = new Vector3(0, pillow + (headR * 0.72f), headZ);
        u.Capsule(new Vector3(0, 0.2f, shZ - 0.03f), head + new Vector3(0, -headR * 0.3f, headR * 0.55f), headR * 0.42f, skin.Darkened(0.06f));

        // ---- the head on the pillow, sunk in a little, face to the ceiling (to the camera when sat up)
        u.Frame = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2), head);
        Person3D.Head(u, look, Vector3.Zero, headR, asleep: !awake);
        u.Frame = Transform3D.Identity;

        // ---- the blanket over the chest when sat up, and the white top sheet turned down over it
        if (raised)
        {
            u.Shape("cover", new Vector3(0, -0.04f, (chestEdge + hinge + 0.06f) / 2), new Vector3(1.4f, coverTop + 0.1f, hinge + 0.06f - chestEdge), blanket);
        }

        u.Shape("cover", new Vector3(0, coverTop * 0.55f, chestEdge + 0.08f), new Vector3(1.43f, coverTop * 0.75f, 0.24f), Sheet);

        // ---- arms on top of the covers: short sleeve, forearm, hand, the ID band on the right wrist
        var armR = 0.095f * Mathf.Lerp(sz, 1f, 0.3f);
        var foreR = armR * 0.88f;
        var sy = coverTop * 0.9f;
        var shoulderL = new Vector3(-shW, 0.2f, shZ + 0.05f);
        var shoulderR = new Vector3(shW, 0.2f, shZ + 0.05f);
        Vector3 Elbow(float side) => raised
            ? new Vector3(side * (shW + 0.04f), sy + 0.04f, Mathf.Min(hinge - 0.12f, Mathf.Lerp(shZ, hipZ, 0.6f)))
            : new Vector3(side * (shW + 0.05f), sy + (side > 0 ? 0.02f : 0f), Mathf.Lerp(shZ, hipZ, 0.5f));

        void UpperArm(Vector3 shoulder, Vector3 elbow)
        {
            u.Capsule(shoulder, shoulder.Lerp(elbow, 0.6f), armR * 1.12f, gown.Darkened(0.06f));
            u.Capsule(shoulder.Lerp(elbow, 0.5f), elbow, armR, skin);
        }

        void Forearm(float side, Vector3 elbow, Vector3 hand, bool banded)
        {
            u.Capsule(elbow, hand, foreR, skin);
            u.Shape("hand", hand, new Vector3(foreR * 2.6f, foreR * 1.6f, foreR * 3f), skin, new Basis(Vector3.Right, -Mathf.Pi / 2) * new Basis(Vector3.Up, side * 0.3f));
            if (banded)
            {
                var dir = (hand - elbow).Normalized();
                var wrist = hand - (dir * foreR * 2.2f);
                u.Capsule(wrist - (dir * 0.03f), wrist + (dir * 0.03f), foreR * 1.3f, Colors.White);
                if (band is { } colour)
                {
                    u.Capsule(wrist - (dir * 0.014f), wrist + (dir * 0.014f), foreR * 1.38f, colour);
                }
            }
        }

        // Where the hands rest: on the lap when sat up, or by their side and across the tummy lying flat.
        var restL = raised ? down * new Vector3(-0.14f * sz, sy + 0.12f, hinge + 0.2f) : new Vector3(-shW + 0.04f, sy + 0.04f, hipZ - 0.05f);
        var restR = raised || look.Bump
            ? (raised ? down * new Vector3(0.1f * sz, sy + 0.17f, hinge + 0.16f) : new Vector3(shW - 0.04f, sy + 0.04f, hipZ - 0.05f))
            : new Vector3(0.06f, coverTop + 0.08f, Mathf.Lerp(shZ, hipZ, 0.75f));

        UpperArm(shoulderL, Elbow(-1));
        UpperArm(shoulderR, Elbow(1));
        if (cuddling)
        {
            // Baby skin to skin on their chest, held by both arms.
            var baby = Baby3D.Bundle(onChest![0], 0.9f);
            baby.Position = new Vector3(0, 0.42f, shZ + 0.32f);
            baby.Rotation = new Vector3(-Mathf.Pi / 2, Mathf.Pi / 2, 0);
            upper.AddChild(baby);
            foreach (var side in new[] { -1f, 1f })
            {
                u.Capsule(Elbow(side), new Vector3(side * 0.2f, 0.42f, shZ + 0.4f), foreR, skin);
            }
        }
        else
        {
            if (doing != InBed.Book)
            {
                Forearm(-1, Elbow(-1), restL, false);
            }

            if (doing == InBed.None)
            {
                Forearm(1, Elbow(1), restR, true);
                if (cast > 0.01f)
                {
                    // A fresh plaster cast, wrapped on from the wrist up the forearm.
                    var e = Elbow(1);
                    u.Capsule(restR, restR.Lerp(e, Mathf.Clamp(cast, 0.15f, 1f) * 0.85f), foreR * 1.45f, Plaster);
                }
            }
        }

        u.AddTo(upper);
        if (doing != InBed.None)
        {
            upper.AddChild(Hold(doing, look, Elbow, sz, foreR, down));
        }

        // ---- the blanket over the lap and legs: soft hips, the legs, and the toes showing through
        var b = new MeshBatch();
        const float end = 1.88f;
        var lapFrom = raised ? hinge - 0.06f : chestEdge;
        var lap = blanket.Lightened(0.05f);
        b.Shape("cover", new Vector3(0, -0.04f, (lapFrom + end) / 2), new Vector3(1.4f, 0.16f, end - lapFrom), blanket);
        b.Shape("cover", new Vector3(0, -0.12f, (lapFrom + end) / 2), new Vector3(1.44f, 0.1f, end - lapFrom + 0.02f), blanket.Darkened(0.1f));
        b.Ball(new Vector3(0, 0.06f, Mathf.Lerp(raised ? hinge : chestEdge, hipZ, 0.5f) + 0.1f), new Vector3(shW * 2.1f, coverTop * 0.6f, Mathf.Max(0.3f, hipZ - chestEdge) * 1.4f), lap);
        var legW = shW * 0.42f;
        const float thick = 0.3f;
        b.Ball(new Vector3(0, 0.05f, (hipZ + kneeZ) / 2), new Vector3((legW * 2) + (0.46f * sz), thick * 1.3f * Mathf.Lerp(sz, 1f, 0.3f), (kneeZ - hipZ) * 1.6f), lap);
        b.Ball(new Vector3(0, 0.03f, (kneeZ + footZ) / 2), new Vector3((legW * 2) + (0.34f * sz), thick * 1.05f * Mathf.Lerp(sz, 1f, 0.3f), (footZ - kneeZ) * 1.35f), lap);
        foreach (var side in new[] { -1f, 1f })
        {
            b.Ball(new Vector3(side * legW * 0.75f, 0.15f * Mathf.Sqrt(sz), footZ), new Vector3(0.24f * sz, 0.32f * Mathf.Sqrt(sz), 0.22f * sz), lap.Lightened(0.05f));
        }

        if (look.Bump)
        {
            b.Ball(new Vector3(0, 0.22f, hipZ - 0.15f), new Vector3(0.7f, 0.36f, 0.62f), lap.Lightened(0.06f));
        }

        if (cast < -0.01f)
        {
            // A backslab on the lower leg, over the covers, wrapped on from the foot up.
            var ankle = new Vector3(legW * 0.75f, 0.2f, footZ - 0.12f);
            var knee = new Vector3(legW * 0.85f, 0.22f, kneeZ);
            b.Capsule(ankle, ankle.Lerp(knee, Mathf.Clamp(-cast, 0.15f, 1f)), W(9f) / 2 * sz, Plaster);
        }

        b.AddTo(node);
        return node;
    }

    /// <summary>
    /// The raised forearm(s) and whatever they hold, as a node pivoting at the right elbow, in the frame of the
    /// upper body. <paramref name="down"/> takes a point in the bed's frame (the tray on the table) into that frame.
    /// </summary>
    private static Node3D Hold(InBed doing, PersonLook look, Func<float, Vector3> elbow, float sz, float foreR, Transform3D down)
    {
        var right = elbow(1);
        var node = new Node3D { Name = "Holding", Position = right };
        var b = new MeshBatch();
        void Forearm(Vector3 from, Vector3 hand)
        {
            b.Capsule(from, hand, foreR, look.Skin);
            b.Shape("hand", hand, new Vector3(foreR * 2.6f, foreR * 1.6f, foreR * 3f), look.Skin, new Basis(Vector3.Right, -Mathf.Pi / 2));
        }

        switch (doing)
        {
            case InBed.Book:
            {
                // Both hands up holding an open book over the chest, tipped towards the face.
                var left = elbow(-1) - right;
                var (hl, hr) = (new Vector3(-right.X - (0.2f * sz), 0.3f, -0.12f), new Vector3(-right.X + (0.2f * sz), 0.3f, -0.12f));
                Forearm(left, hl);
                Forearm(Vector3.Zero, hr);
                var mid = (hl + hr) / 2;
                var tilt = new Basis(Vector3.Right, -0.9f);
                var blue = new Color("#3F6FB5");
                var paper = new Color("#FBF7EC");
                b.Box(mid + new Vector3(-0.13f * sz, 0.04f, 0), new Vector3(0.26f * sz, 0.02f, 0.34f * sz), tilt * new Basis(Vector3.Forward, 0.18f), blue);
                b.Box(mid + new Vector3(0.13f * sz, 0.04f, 0), new Vector3(0.26f * sz, 0.02f, 0.34f * sz), tilt * new Basis(Vector3.Forward, -0.18f), blue);
                b.Box(mid + new Vector3(-0.12f * sz, 0.055f, -0.01f), new Vector3(0.23f * sz, 0.01f, 0.3f * sz), tilt * new Basis(Vector3.Forward, 0.18f), paper);
                b.Box(mid + new Vector3(0.12f * sz, 0.055f, -0.01f), new Vector3(0.23f * sz, 0.01f, 0.3f * sz), tilt * new Basis(Vector3.Forward, -0.18f), paper);
                break;
            }

            case InBed.Phone:
            {
                var hand = new Vector3(-0.3f * sz, 0.32f, -0.2f);
                Forearm(Vector3.Zero, hand);
                b.Box(hand + new Vector3(0, 0.05f, -0.03f), new Vector3(0.1f, 0.015f, 0.18f), new Basis(Vector3.Right, -0.9f), new Color("#22262B"));
                b.Box(hand + new Vector3(0, 0.059f, -0.03f), new Vector3(0.085f, 0.004f, 0.15f), new Basis(Vector3.Right, -0.9f), new Color("#7FC4F0"));
                break;
            }

            case InBed.Meal:
            {
                // A hand out to the plate on the table across the bed (see Beds3D's meal table), holding a fork.
                var plate = (down * new Vector3(0.05f, 0.38f, -0.3f)) - right;
                var hand = Vector3.Zero.Lerp(plate, Mathf.Min(1f, 0.55f / Mathf.Max(0.01f, plate.Length())));
                Forearm(Vector3.Zero, hand);
                b.Rod(hand, hand + new Vector3(-0.12f, 0.02f, 0.05f), 0.01f, new Color("#B0BEC5"), small: true);
                break;
            }
        }

        b.AddTo(node);
        return node;
    }
}
