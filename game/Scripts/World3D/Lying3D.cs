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
    private static readonly Color Gel = new("#9AD8F2");
    private static readonly Color Drape = new("#3F8FD6");

    /// <summary>
    /// Where the middle of the chest is, in the bed's frame above the mattress, for someone of this size with the
    /// backrest at <paramref name="angle"/>: where a baby is laid, a probe rests or a stethoscope goes.
    /// </summary>
    public static Vector3 Chest(PersonLook look, Carriers3D.Backrest? rest, float angle, float out_ = 0.42f)
    {
        var sz = Mathf.Clamp(look.Scale, 0.5f, 1f);
        var length = 2.55f * sz;
        var raised = rest is not null && angle > 0.05f;
        var headZ = raised ? Mathf.Max(-1.45f, rest!.Value.HingeZ + 0.12f - (0.52f * length)) : -1.45f;
        var at = new Vector3(0, out_, headZ + (0.15f * length) + 0.32f);
        return raised ? rest!.Value.Turn(0, angle) * at : at;
    }

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
        Carriers3D.Backrest? rest = null, float angle = 0, Care care = Care.None, float careSide = 1)
    {
        var node = new Node3D { Name = "Lying" };
        var sz = Mathf.Clamp(look.Scale, 0.5f, 1f);
        var kid = sz < 0.8f;
        var gown = kid ? ChildGown : Gown;
        var skin = look.Skin;
        var raised = rest is not null && angle > 0.05f;
        var cuddling = onChest is { Length: > 0 };
        if (cuddling || care != Care.None)
        {
            doing = InBed.None;
        }

        var ecg = care == Care.Ecg;
        var birth = care == Care.Birth && rest is not null;
        var lumbar = care == Care.LumbarPuncture;
        var scan = Procedure3D.OnScanner(care) || care == Care.Endoscopy || lumbar;
        var chestBare = ecg || care == Care.Echo;
        var tummyBare = care is Care.Ultrasound or Care.AsciticTap;

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
        if (raised && !chestBare && !tummyBare && !birth)
        {
            u.Shape("cover", new Vector3(0, -0.04f, (chestEdge + hinge + 0.06f) / 2), new Vector3(1.4f, coverTop + 0.1f, hinge + 0.06f - chestEdge), blanket);
        }

        if (!chestBare && !tummyBare && !birth && !scan)
        {
            u.Shape("cover", new Vector3(0, coverTop * 0.55f, chestEdge + 0.08f), new Vector3(1.43f, coverTop * 0.75f, 0.24f), Sheet);
        }

        if (ecg)
        {
            Ecg(u, skin, shW, shZ, raised ? Mathf.Min(hipZ, hinge - 0.02f) : hipZ, Mathf.Lerp(sz, 1f, 0.4f), careSide, down);
        }

        if (care == Care.Echo)
        {
            // The gown open on the chest, with a blob of gel where the probe goes.
            var front = 0.12f + (0.18f * Mathf.Lerp(sz, 1f, 0.4f));
            var to = raised ? Mathf.Min(hipZ, hinge - 0.02f) : hipZ;
            u.Ball(new Vector3(0, front - 0.05f, Mathf.Lerp(shZ, to, 0.5f)), new Vector3(shW * 1.5f, 0.14f, (to - shZ) * 0.95f), skin);
            u.Ball(new Vector3(-0.1f, front + 0.02f, shZ + 0.3f), new Vector3(0.2f, 0.04f, 0.16f), Gel, small: true);
        }

        if (tummyBare)
        {
            // The gown lifted off the tummy, with gel on it for the probe; a paper towel tucked in below.
            var front = 0.12f + (0.18f * Mathf.Lerp(sz, 1f, 0.4f));
            var mid = Mathf.Lerp(shZ, hipZ, 0.78f);
            u.Ball(new Vector3(0, front - 0.03f + (look.Bump ? 0.12f : 0f), mid), new Vector3(shW * 1.7f, look.Bump ? 0.46f : 0.16f, (hipZ - shZ) * 0.62f), skin);
            if (care == Care.AsciticTap)
            {
                // A sterile drape on the side of the tummy with a window where the needle goes in.
                var at = new Vector3(careSide * shW * 0.62f, front + 0.02f, mid + 0.04f);
                u.Box(at, new Vector3(0.36f, 0.03f, 0.36f), new Basis(Vector3.Back, -careSide * 0.35f), Drape);
                u.Ball(at + new Vector3(careSide * 0.01f, 0.014f, 0), new Vector3(0.12f, 0.03f, 0.12f), skin, small: true);
            }
            else
            {
                u.Ball(new Vector3(0.04f, front + 0.04f + (look.Bump ? 0.3f : 0f), mid), new Vector3(0.24f, 0.04f, 0.2f), Gel, small: true);
            }
        }

        if (lumbar)
        {
            // The gown open at the back, a sterile drape low on the spine with a window in it.
            var at = new Vector3(0, -0.07f, Mathf.Lerp(shZ, hipZ, 0.82f));
            u.Ball(new Vector3(0, -0.03f, Mathf.Lerp(shZ, hipZ, 0.55f)), new Vector3(shW * 1.5f, 0.1f, (hipZ - shZ) * 0.9f), skin);
            u.Box(at, new Vector3(shW * 1.9f, 0.03f, 0.46f), Drape);
            u.Ball(at - new Vector3(0, 0.012f, 0), new Vector3(0.12f, 0.03f, 0.12f), skin, small: true);
        }

        if (care == Care.Eeg)
        {
            // The electrode cap: a net of little coloured discs over the top of the head, its wires gathered at the crown.
            var cap = new[] { "#E53935", "#FDD835", "#43A047", "#1E88E5", "#8E24AA", "#F4511E", "#00ACC1" };
            for (var i = 0; i < cap.Length; i++)
            {
                var a = (i / (float)(cap.Length - 1) * 2.2f) - 1.1f;
                foreach (var row in new[] { 0f, 1f })
                {
                    var at = head + new Vector3(Mathf.Sin(a) * headR * 0.9f, (Mathf.Cos(a) * headR * (0.62f - (0.3f * row))) + (headR * 0.25f), -headR * (0.25f + (0.5f * row)));
                    u.Ball(at, new Vector3(0.06f, 0.06f, 0.06f), new Color(cap[(i + (int)row * 3) % cap.Length]), small: true);
                }
            }

            u.Ball(head + new Vector3(0, headR * 0.1f, -headR * 0.98f), new Vector3(0.11f, 0.11f, 0.08f), new Color("#ECEFF1"), small: true);
        }

        if (care == Care.Endoscopy)
        {
            // The mouth guard that the scope goes through.
            u.Ball(head + new Vector3(0, headR * 0.92f, headR * 0.42f), new Vector3(0.1f, 0.06f, 0.08f), new Color("#4FC3F7"), small: true);
        }

        if (birth && look.Bump)
        {
            u.Ball(down * new Vector3(0, 0.36f, hinge - 0.02f), new Vector3(0.74f, 0.52f, 0.68f), gown.Lightened(0.03f));
        }

        if (care == Care.CtHead)
        {
            // The head cradle of the scanner table.
            u.Ball(head - new Vector3(0, headR * 0.75f, 0), new Vector3(headR * 2.9f, headR * 1.1f, headR * 2.7f), new Color("#3F4A54"));
        }

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

        var (elbowL, elbowR) = (Elbow(-1), Elbow(1));
        if (chestBare || tummyBare)
        {
            // Arms relaxed by their sides, out of the way of the leads or the probe.
            restL = down * new Vector3(-(shW + 0.14f), raised ? 0.16f : sy, (raised ? hinge : hipZ) + 0.22f);
            restR = down * new Vector3(shW + 0.14f, raised ? 0.16f : sy, (raised ? hinge : hipZ) + 0.22f);
        }
        else if (birth)
        {
            // Holding on behind her knees.
            var hb = hinge + 0.15f;
            restL = down * new Vector3(-0.5f, 0.56f, hb + 0.3f);
            restR = down * new Vector3(0.5f, 0.56f, hb + 0.3f);
        }
        else if (care == Care.CtBody)
        {
            // Arms up above the head, out of the way of the scan.
            elbowL = new Vector3(-(shW + 0.1f), 0.14f, headZ + 0.02f);
            elbowR = new Vector3(shW + 0.1f, 0.14f, headZ + 0.02f);
            restL = new Vector3(-0.2f, 0.16f, headZ - 0.36f);
            restR = new Vector3(0.2f, 0.16f, headZ - 0.36f);
        }
        else if (lumbar)
        {
            // Curled up, hugging their knees.
            elbowL = new Vector3(-(shW + 0.02f), 0.3f, Mathf.Lerp(shZ, hipZ, 0.6f));
            elbowR = new Vector3(shW + 0.02f, 0.3f, Mathf.Lerp(shZ, hipZ, 0.6f));
            restL = new Vector3(-0.14f, 0.5f, hipZ + 0.12f);
            restR = new Vector3(0.14f, 0.5f, hipZ + 0.12f);
        }
        else if (care is Care.Bloods or Care.FingerPrick)
        {
            // The arm on the nurse's side out straight beside them, palm up.
            var z0 = raised ? hinge : hipZ;
            var e = down * new Vector3(careSide * (shW + 0.17f), raised ? 0.2f : sy + 0.02f, z0 - 0.08f);
            var h = down * new Vector3(careSide * (shW + 0.3f), raised ? 0.2f : sy + 0.02f, z0 + 0.3f);
            if (careSide > 0)
            {
                (elbowR, restR) = (e, h);
            }
            else
            {
                (elbowL, restL) = (e, h);
            }
        }
        else if (care == Care.PeakFlow)
        {
            // One hand up holding the meter to their mouth.
            restR = head + new Vector3(0.05f, (headR * 0.95f) + 0.2f, (headR * 0.42f) + 0.07f);
            elbowR = new Vector3(shW + 0.06f, 0.36f, shZ + 0.3f);
        }
        else if (scan)
        {
            restL = new Vector3(-(shW + 0.06f), 0.1f, hipZ - 0.02f);
            restR = care == Care.XrayArm ? new Vector3(shW + 0.12f, 0.12f, hipZ - 0.3f) : new Vector3(shW + 0.06f, 0.1f, hipZ - 0.02f);
            elbowL = new Vector3(-(shW + 0.07f), 0.12f, Mathf.Lerp(shZ, hipZ, 0.5f));
            elbowR = new Vector3(shW + (care == Care.XrayArm ? 0.2f : 0.07f), 0.12f, Mathf.Lerp(shZ, hipZ, 0.42f));
        }

        UpperArm(shoulderL, elbowL);
        UpperArm(shoulderR, elbowR);
        var (nearShoulder, nearElbow, nearHand) = careSide > 0 ? (shoulderR, elbowR, restR) : (shoulderL, elbowL, restL);
        if (care == Care.Obs)
        {
            // The blood pressure cuff round the upper arm, its tube going to the machine; the probe on a finger.
            var navy = new Color("#1F3A5F");
            var tube = new Color("#607D8B");
            var top = nearShoulder.Lerp(nearElbow, 0.42f);
            var bottom = nearShoulder.Lerp(nearElbow, 0.88f);
            u.Capsule(top, bottom, armR * 1.45f, navy);
            u.Ball(top.Lerp(bottom, 0.5f) + new Vector3(0, armR * 1.3f, 0), new Vector3(0.07f, 0.04f, 0.1f), Colors.White, small: true);
            var socket = down * Procedure3D.ObsSocket(careSide);
            var sag = bottom.Lerp(socket, 0.5f) + (down.Basis * new Vector3(0, -0.16f, 0));
            u.Rod(bottom + new Vector3(careSide * armR, 0, 0), sag, 0.013f, tube, small: true);
            u.Rod(sag, socket, 0.013f, tube, small: true);
            var tip = nearHand + ((nearHand - nearElbow).Normalized() * foreR * 2.4f);
            u.Ball(tip, new Vector3(0.075f, 0.07f, 0.1f), new Color("#ECEFF1"), small: true);
            u.Ball(tip + new Vector3(0, 0.03f, 0), new Vector3(0.035f, 0.03f, 0.035f), new Color("#FF5252"), small: true);
            var loop = tip.Lerp(socket, 0.5f) + (down.Basis * new Vector3(0, -0.2f, 0));
            u.Rod(tip, loop, 0.008f, tube, small: true);
            u.Rod(loop, socket + (down.Basis * new Vector3(0, -0.03f, 0)), 0.008f, tube, small: true);
        }
        else if (care == Care.Bloods)
        {
            // The tourniquet above the elbow.
            var at = nearShoulder.Lerp(nearElbow, 0.8f);
            var dir = (nearElbow - nearShoulder).Normalized();
            u.Capsule(at - (dir * 0.025f), at + (dir * 0.025f), armR * 1.35f, new Color("#1E88E5"));
        }
        else if (care == Care.PeakFlow)
        {
            // The peak flow meter: a white tube with a blue mouthpiece and the red marker on its scale.
            var mouth = head + new Vector3(0, headR * 0.95f, headR * 0.42f);
            u.Box(mouth + new Vector3(0, 0.05f, 0), new Vector3(0.07f, 0.08f, 0.06f), new Color("#4FC3F7"));
            u.Box(mouth + new Vector3(0, 0.24f, 0), new Vector3(0.09f, 0.32f, 0.07f), new Color("#F7F9FA"));
            u.Box(mouth + new Vector3(0, 0.26f, 0.037f), new Vector3(0.03f, 0.24f, 0.006f), new Color("#FDD835"));
            u.Ball(mouth + new Vector3(0, 0.2f, 0.045f), new Vector3(0.035f, 0.035f, 0.02f), new Color("#E53935"), small: true);
        }
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
                Forearm(-1, elbowL, restL, false);
            }

            if (doing == InBed.None)
            {
                Forearm(1, elbowR, restR, true);
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

        if (birth || scan)
        {
            var bare = new MeshBatch();
            if (birth)
            {
                BirthLegs(bare, skin, gown, hinge + 0.15f);
            }
            else if (lumbar)
            {
                // Knees drawn up towards the chest.
                foreach (var s in new[] { -1f, 1f })
                {
                    var hip = new Vector3(s * 0.14f, 0.12f, hipZ);
                    var knee = new Vector3(s * 0.15f, 0.56f, hipZ + 0.3f);
                    var foot = new Vector3(s * 0.15f, 0.3f, hipZ + 0.85f);
                    bare.Capsule(hip, knee, 0.14f * Mathf.Lerp(sz, 1f, 0.3f), gown);
                    bare.Capsule(knee, foot, 0.085f * Mathf.Lerp(sz, 1f, 0.3f), skin);
                    bare.Ball(foot + new Vector3(0, 0.08f, 0.06f), new Vector3(0.15f, 0.28f, 0.17f) * Mathf.Lerp(sz, 1f, 0.3f), skin);
                }

                bare.Ball(new Vector3(0, 0.1f, hipZ - 0.02f), new Vector3(shW * 2.1f, 0.34f * Mathf.Lerp(sz, 1f, 0.4f), 0.5f), gown);
            }
            else
            {
                ScanLegs(bare, skin, gown, care, shW, chestEdge, Z(0.76f), Z(0.97f), sz);
            }

            bare.AddTo(node);
            return node;
        }

        // ---- the blanket over the lap and legs: soft hips, the legs, and the toes showing through
        var b = new MeshBatch();
        const float end = 1.88f;
        var lapFrom = raised ? hinge - 0.06f : chestEdge;
        if (chestBare || tummyBare)
        {
            // The covers folded down to the waist (to the hips for a scan of the tummy).
            lapFrom = raised ? lapFrom : hipZ - (tummyBare ? -0.08f : 0.1f);
        }
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

        if (care is Care.Bloods or Care.FingerPrick or Care.AsciticTap or Care.JointAspiration)
        {
            // A cardboard tray on the bed with what is needed: tubes with coloured tops, a wipe.
            var tray = new Vector3(careSide * 0.2f, 0.27f, Mathf.Min(1.5f, kneeZ + 0.34f));
            b.Box(tray, new Vector3(0.3f, 0.05f, 0.18f), new Color("#B9B1A0"));
            b.Box(tray + new Vector3(0, 0.02f, 0), new Vector3(0.25f, 0.03f, 0.13f), new Color("#D9D2C3"));
            var caps = new[] { "#8E24AA", "#FDD835", "#1E88E5" };
            for (var i = 0; i < (care == Care.FingerPrick ? 1 : caps.Length); i++)
            {
                var at = tray + new Vector3(-0.08f + (i * 0.08f), 0.045f, 0);
                b.Capsule(at - new Vector3(0, 0, 0.04f), at + new Vector3(0, 0, 0.03f), 0.016f, new Color("#F4F6F7"));
                b.Ball(at + new Vector3(0, 0, 0.045f), new Vector3(0.04f, 0.04f, 0.03f), new Color(caps[i]), small: true);
            }
        }

        if (care == Care.JointAspiration)
        {
            // The covers off the knee, a sterile drape round it.
            var knee = new Vector3(careSide * legW * 0.9f, 0.25f, kneeZ - 0.05f);
            b.Box(knee, new Vector3(0.44f, 0.03f, 0.5f), Drape);
            b.Ball(knee + new Vector3(0, 0.03f, 0), new Vector3(0.2f, 0.13f, 0.24f), skin);
        }

        if (chestBare || tummyBare)
        {
            b.Capsule(new Vector3(-0.66f, 0.1f, lapFrom + 0.02f), new Vector3(0.66f, 0.1f, lapFrom + 0.02f), 0.1f, Sheet);
        }

        b.AddTo(node);
        return node;
    }

    /// <summary>Where the ECG machine's lead socket is, in the bed's frame above the mattress, on the nurse's side.</summary>
    public static Vector3 EcgSocket(float side) => new(side * 1.42f, 0.42f, 0.55f);

    /// <summary>
    /// The gown open on the chest with the ten electrodes of a 12-lead ECG stuck on: six across the chest, one on
    /// each shoulder and each side of the tummy, each with its own coloured wire running to the cable that goes
    /// off to the machine.
    /// </summary>
    private static void Ecg(MeshBatch u, Color skin, float shW, float shZ, float hipZ, float deep, float side, Transform3D down)
    {
        var front = 0.12f + (0.18f * deep);
        // The bare chest between the open edges of the gown.
        u.Ball(new Vector3(0, front - 0.05f, Mathf.Lerp(shZ, hipZ, 0.5f)), new Vector3(shW * 1.5f, 0.14f, (hipZ - shZ) * 0.95f), skin);
        var lower = Mathf.Lerp(shZ, hipZ, 0.82f);
        var c = shZ + 0.24f;
        var leads = new (Vector3 At, string Colour)[]
        {
            (new Vector3(0.07f, front + 0.02f, c), "#E53935"),
            (new Vector3(-0.07f, front + 0.02f, c), "#FDD835"),
            (new Vector3(-0.15f, front + 0.02f, c + 0.07f), "#43A047"),
            (new Vector3(-0.23f, front + 0.01f, c + 0.14f), "#8D6E63"),
            (new Vector3(-0.31f, front - 0.01f, c + 0.15f), "#263238"),
            (new Vector3(-0.38f, front - 0.04f, c + 0.16f), "#8E24AA"),
            (new Vector3(shW * 0.78f, front - 0.03f, shZ + 0.06f), "#E53935"),
            (new Vector3(-shW * 0.78f, front - 0.03f, shZ + 0.06f), "#FDD835"),
            (new Vector3(shW * 0.55f, front - 0.01f, lower), "#263238"),
            (new Vector3(-shW * 0.55f, front - 0.01f, lower), "#43A047"),
        };

        // The wires gather at a block resting on the tummy, and one cable goes from there to the machine.
        var block = new Vector3(side * 0.12f, front + 0.06f, Mathf.Lerp(shZ, hipZ, 0.62f));
        foreach (var (at, colour) in leads)
        {
            u.Ball(at, new Vector3(0.085f, 0.03f, 0.085f), new Color("#F4F6F7"), small: true);
            u.Ball(at + new Vector3(0, 0.014f, 0), new Vector3(0.042f, 0.03f, 0.042f), new Color(colour), small: true);
            var mid = at.Lerp(block, 0.5f) + new Vector3(0, 0.07f, 0);
            u.Rod(at + new Vector3(0, 0.02f, 0), mid, 0.009f, new Color(colour), small: true);
            u.Rod(mid, block, 0.009f, new Color(colour), small: true);
        }

        u.Box(block, new Vector3(0.13f, 0.045f, 0.09f), new Color("#ECEFF1"));
        var socket = down * EcgSocket(side);
        var sag = block.Lerp(socket, 0.55f) + (down.Basis * new Vector3(0, -0.2f, 0));
        var cable = new Color("#546E7A");
        u.Rod(block, block.Lerp(sag, 0.5f) + (down.Basis * new Vector3(0, 0.05f, 0)), 0.016f, cable, small: true);
        u.Rod(block.Lerp(sag, 0.5f) + (down.Basis * new Vector3(0, 0.05f, 0)), sag, 0.016f, cable, small: true);
        u.Rod(sag, socket, 0.016f, cable, small: true);
    }

    /// <summary>
    /// Giving birth: thighs up under a drape, knees apart, calves in the padded leg supports that swing up from
    /// the sides of the bed, on a clean pad.
    /// </summary>
    private static void BirthLegs(MeshBatch b, Color skin, Color gown, float hb)
    {
        var steel = new Color("#B0BEC5");
        var pad = new Color("#37474F");
        b.Box(new Vector3(0, 0.012f, hb + 0.62f), new Vector3(1.05f, 0.02f, 1.0f), new Color("#BFE3F2"));
        foreach (var s in new[] { -1f, 1f })
        {
            var hip = new Vector3(s * 0.17f, 0.15f, hb);
            var knee = new Vector3(s * 0.46f, 0.76f, hb + 0.42f);
            var foot = new Vector3(s * 0.58f, 0.7f, hb + 0.98f);
            b.Capsule(hip, knee, 0.15f, gown.Lightened(0.04f));
            b.Capsule(knee, foot, 0.1f, skin);
            b.Ball(foot + new Vector3(0, 0.1f, 0.05f), new Vector3(0.17f, 0.34f, 0.19f), new Color("#F4F6F7"));

            // The leg support: a post up from the bed frame to a padded cradle under the calf.
            var calf = knee.Lerp(foot, 0.55f);
            var cradle = calf - new Vector3(0, 0.13f, 0);
            b.Rod(new Vector3(s * 0.74f, -0.12f, hb + 0.62f), cradle + new Vector3(s * 0.06f, -0.03f, 0), 0.026f, steel);
            b.Box(cradle, new Vector3(0.26f, 0.06f, 0.5f), new Basis(Vector3.Right, 0.1f), pad);
            b.Box(cradle + new Vector3(s * 0.13f, 0.06f, 0), new Vector3(0.04f, 0.14f, 0.5f), new Basis(Vector3.Right, 0.1f), pad);
        }

        // A drape over her tummy and thighs.
        b.Ball(new Vector3(0, 0.3f, hb + 0.14f), new Vector3(1.06f, 0.6f, 0.8f), new Color("#F7F9FA"));
        b.Ball(new Vector3(0, 0.52f, hb + 0.36f), new Vector3(0.98f, 0.42f, 0.4f), new Color("#F7F9FA"));
    }

    /// <summary>On the X-ray or CT table: no covers, the gown down to the knees, bare lower legs and feet.</summary>
    private static void ScanLegs(MeshBatch b, Color skin, Color gown, Care care, float shW, float chestEdge, float kneeZ, float footZ, float sz)
    {
        b.Ball(new Vector3(0, 0.1f, Mathf.Lerp(chestEdge, kneeZ, 0.5f)), new Vector3(shW * 2.15f, 0.32f * Mathf.Lerp(sz, 1f, 0.4f), (kneeZ - chestEdge) * 1.25f), gown);
        foreach (var s in new[] { -1f, 1f })
        {
            var lift = care == Care.XrayLeg && s > 0 ? 0.05f : 0f;
            var x = s * 0.15f * Mathf.Lerp(sz, 1f, 0.3f);
            b.Capsule(new Vector3(x, 0.1f + lift, kneeZ - 0.08f), new Vector3(x, 0.09f + lift, footZ - 0.08f), 0.085f * Mathf.Lerp(sz, 1f, 0.3f), skin);
            b.Ball(new Vector3(x, 0.17f + lift, footZ), new Vector3(0.15f, 0.3f, 0.17f) * Mathf.Lerp(sz, 1f, 0.3f), skin);
        }

        var plate = new Color("#2E3A44");
        var edge = new Color("#CFD8DC");
        if (care == Care.XrayLeg)
        {
            // The detector plate under the lower leg.
            b.Box(new Vector3(0.17f, 0.02f, Mathf.Lerp(kneeZ, footZ, 0.5f)), new Vector3(0.5f, 0.045f, 0.85f), edge);
            b.Box(new Vector3(0.17f, 0.03f, Mathf.Lerp(kneeZ, footZ, 0.5f)), new Vector3(0.44f, 0.045f, 0.79f), plate);
        }
        else if (care == Care.XrayArm)
        {
            b.Box(new Vector3(shW + 0.13f, 0.02f, chestEdge + 0.32f), new Vector3(0.36f, 0.045f, 0.62f), edge);
            b.Box(new Vector3(shW + 0.13f, 0.03f, chestEdge + 0.32f), new Vector3(0.3f, 0.045f, 0.56f), plate);
        }
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
