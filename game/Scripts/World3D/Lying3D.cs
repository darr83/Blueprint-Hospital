using System;
using BlueprintHospital.Hospital;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// A patient lying on their back under a blanket, as on a bed, trolley or stretcher: the 3D
/// version of the 2D lying figure (bed option C, the banded arm across the tummy). Built in its
/// own frame: the origin is the middle of the mattress at its top, +z runs to the feet, the head
/// rests on the pillow at the -z end. Children are smaller, lying at the head end.
/// </summary>
public static class Lying3D
{
    private static readonly Color Gown = new("#C3D8EA");
    private static readonly Color ChildGown = new("#F7E3A1");
    private static readonly Color Sheet = new("#F3F6F8");

    private static float W(double px) => Person3D.W(px);

    private static float V(double px) => Person3D.V(px);

    /// <summary>Builds the figure. <paramref name="band"/> is the wristband's triage colour (white band if null).</summary>
    public static Node3D Build(PersonLook look, Color blanket, Color? band, BabyLook[]? onChest = null)
    {
        var node = new Node3D { Name = "Lying" };
        var b = new MeshBatch();
        var sz = Mathf.Clamp(look.Scale, 0.5f, 1f);
        const float anchor = -1.45f;
        float U(double along) => (float)((along * sz) + ((1 - sz) * anchor));
        var gown = sz < 0.8f ? ChildGown : Gown;
        var skin = look.Skin;

        // Shoulders and chest in the gown, showing above the turned-down sheet.
        b.Ball(new Vector3(0, V(2.2f), U(-0.86)), new Vector3(0.95f * sz, V(7.5f), 0.72f * sz), gown);

        // The blanket tucked in down the sides of the mattress, with the body's rise and the feet under it.
        var edge = U(-0.6);
        const float end = 1.82f;
        b.Box(new Vector3(0, V(0.4f), (edge + end) / 2), new Vector3(1.32f, V(2.8f), end - edge), blanket);
        foreach (var side in new[] { -1f, 1f })
        {
            b.Box(new Vector3(side * 0.67f, -V(1.6f), (edge + end) / 2), new Vector3(0.035f, V(6.8f), end - edge), blanket.Darkened(0.12f));
        }

        b.Box(new Vector3(0, -V(1.6f), end + 0.015f), new Vector3(1.37f, V(6.8f), 0.035f), blanket.Darkened(0.12f));
        b.Ball(new Vector3(0, V(1.5f), U(0.45)), new Vector3(0.94f * sz, V(5.4f), 2.3f * sz), blanket.Lightened(0.08f));
        foreach (var side in new[] { -1f, 1f })
        {
            b.Ball(new Vector3(side * 0.14f * sz, V(2.4f), U(1.58)), new Vector3(0.2f * sz, V(5.6f), 0.26f * sz), blanket.Lightened(0.12f), small: true);
        }

        if (look.Bump)
        {
            b.Ball(new Vector3(0, V(4.5f), U(0.05)), new Vector3(0.66f, V(11), 0.62f), blanket.Lightened(0.12f));
        }

        // The white top sheet turned down over the blanket at the chest.
        b.Box(new Vector3(0, V(2.0f), edge + 0.09f), new Vector3(1.34f, V(1.4f), 0.26f), Sheet);

        // Arms on top of the covers, sleeves to the elbow.
        void Arm(float side, (double Along, double Across, float H)[] path, bool banded)
        {
            var p = Array.ConvertAll(path, q => new Vector3(side * (float)q.Across * sz, V(q.H) - V(1.5f), U(q.Along)));
            var skinR = W(5) / 2 * sz;
            b.Capsule(p[0], p[0].Lerp(p[1], 0.75f), W(6.6f) / 2 * sz, gown.Darkened(0.07f));
            b.Capsule(p[1], p[2], skinR, skin);
            b.Capsule(p[2], p[3], skinR, skin);
            b.Ball(p[3], Vector3.One * W(6.4f) * sz, skin, small: true);
            if (banded)
            {
                // The ID band: a white strap with a block of the triage colour.
                var dir = (p[3] - p[2]).Normalized();
                var wrist = p[2] + (dir * (p[3] - p[2]).Length() * 0.45f);
                b.Capsule(wrist - (dir * 0.045f), wrist + (dir * 0.045f), skinR * 1.35f, Colors.White);
                if (band is { } colour)
                {
                    b.Capsule(wrist - (dir * 0.022f), wrist + (dir * 0.022f), skinR * 1.45f, colour);
                }
            }
        }

        var straight = new[] { (-1.0, 0.42, 3.6f), (-0.55, 0.46, 4.4f), (-0.16, 0.42, 4.6f), (-0.02, 0.4, 4.6f) };
        var across = new[] { (-1.0, 0.43, 3.6f), (-0.5, 0.54, 4.6f), (-0.22, 0.2, 7f), (-0.16, 0.04, 7.3f) };
        var cuddling = onChest is { Length: > 0 };
        Arm(-1, straight, false);
        if (!cuddling)
        {
            Arm(1, look.Bump ? straight : across, true);
        }

        // Neck, then the head on the pillow, face turned up to the ceiling, eyes closed.
        var r = W(7.8f * (0.72f + (0.28f * sz)) * Mathf.Lerp(1f, look.HeadScale, 0.5f));
        var head = new Vector3(0, V(4.5f) + (r * 0.8f), U(-1.5));
        b.Capsule(new Vector3(0, V(3), U(-1.2)), head + new Vector3(0, -r * 0.4f, r * 0.5f), W(2.6f) * sz, skin.Darkened(0.06f));
        b.Frame = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2), head);
        b.Ball(Vector3.Zero, new Vector3(r * 2, r * 2.05f, r * 1.95f), skin);
        Person3D.Head(b, look, Vector3.Zero, r, asleep: true);
        b.Frame = Transform3D.Identity;
        b.AddTo(node);

        if (cuddling)
        {
            // Baby skin to skin on mum's chest, held by both arms.
            var baby = Baby3D.Bundle(onChest![0], 0.9f);
            baby.Position = new Vector3(0, V(9), U(-0.7));
            baby.Rotation = new Vector3(-Mathf.Pi / 2, Mathf.Pi / 2, 0);
            node.AddChild(baby);
            var arms = new MeshBatch();
            foreach (var side in new[] { -1f, 1f })
            {
                arms.Capsule(new Vector3(side * 0.42f * sz, V(3), U(-0.95)), new Vector3(side * 0.3f, V(8), U(-0.6)), W(2.5f) * sz, skin);
            }

            arms.AddTo(node);
        }

        return node;
    }
}
