using System;
using BlueprintHospital.Hospital;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>What a person is doing with their body.</summary>
public enum Pose
{
    Standing,
    Walking,
    Seated,
    Cpr,
}

/// <summary>
/// A person in the "Friendly" style (people style A): a big round head with dot eyes and a smile,
/// a rounded body in their uniform or own clothes, and the kit their job comes with. Built once
/// from a <see cref="PersonLook"/> out of a few merged meshes (body, two arms, two legs), so a
/// crowded department stays cheap to draw; arms and legs swing as they walk, bend as they sit.
/// Proportions follow the 2D figures pixel for pixel, so the two styles read the same.
/// </summary>
public partial class Person3D : Node3D
{
    private static readonly Color Ink = new("#1D2B36");
    private static readonly Color LanyardBlue = new("#005EB8");
    private static readonly Color Metal = new("#AEB9C2");

    private Node3D _body = null!;
    private Node3D _armL = null!;
    private Node3D _armR = null!;
    private Node3D _thighL = null!;
    private Node3D _thighR = null!;
    private Node3D _kneeL = null!;
    private Node3D _kneeR = null!;
    private Node3D? _baby;
    private Node3D? _bag;
    private BabyLook? _babyLook;

    private float _hipY;
    private float _seatDrop;

    public PersonLook Look { get; private set; } = null!;

    /// <summary>Horizontal size in world units of something the 2D art drew <paramref name="px"/> pixels wide.</summary>
    internal static float W(double px) => (float)(px / Kit3D.PxPerUnit);

    /// <summary>Vertical size in world units of something the 2D art drew <paramref name="px"/> pixels tall.</summary>
    internal static float V(double px) => Kit3D.H(px);

    public static Person3D Build(PersonLook look)
    {
        var p = new Person3D { Look = look };
        p.Assemble();
        return p;
    }

    private void Assemble()
    {
        var look = Look;
        var s = look.Scale;
        var kid = s < 0.8f;
        var lw = Mathf.Pow(s, 0.75f);
        var grown = look.Female && !kid;
        var shY = V(41 * s);
        _hipY = V(19 * s);
        var shW = W(9 * s * (kid ? 0.95f : look.Female ? 0.9f : 1.05f) * (0.6f + (0.4f * look.Build)));
        var hipW = W(8.4f * s * (grown ? 1.04f : 1f) * look.Build);
        var legR = W(5.5f * lw * (0.75f + (0.25f * look.Build))) / 2;
        var armR = W(4.6f * lw) / 2;
        var gap = W(3.6f * s * (0.8f + (0.2f * look.Build)));
        var headR = W(7.5f * look.HeadScale);
        var stoop = look.Stoop ? 0.18f : 0f;
        var depth = W(13 * s) * (look.Belly ? 1.25f : 1f) * (0.85f + (0.15f * look.Build));
        _seatDrop = _hipY - V(15);

        _body = new Node3D { Name = "Body" };
        AddChild(_body);
        var body = new MeshBatch();

        // ---- torso and clothes
        var dress = look.Kind == TopKind.Dress;
        var torsoTop = shY + V(2 * s);
        var torsoMid = (torsoTop + _hipY) / 2;
        var top = look.Jacket ?? look.Top;
        body.Pill(new Vector3(0, torsoMid, 0), new Vector3((shW * 2) + W(2), torsoTop - _hipY + V(5 * s), depth), top);
        if (look.Jacket is { } jacket)
        {
            // An open jacket or coat: the shirt shows down the front, the coat tails hang below the hips.
            body.Box(new Vector3(0, torsoMid + V(3), (depth / 2) + 0.003f), new Vector3(shW * 0.55f, torsoTop - _hipY - V(4), 0.02f), look.Top);
            body.Pill(new Vector3(0, _hipY - V(3 * s), 0), new Vector3((hipW * 2) + W(3), V(10 * s), depth * 1.02f), jacket);
        }

        if (look.Kind == TopKind.Stripes && look.Motif is null)
        {
            for (var i = 0; i < 3; i++)
            {
                body.Pill(new Vector3(0, _hipY + V((5 + (i * 6)) * s), 0), new Vector3((shW * 2) + W(2.6f), V(2 * s), depth * 1.03f), look.Top.Darkened(0.25f));
            }
        }

        if (look.Motif is { } motif)
        {
            body.Ball(new Vector3(0, torsoMid + V(2), (depth / 2) - 0.004f), new Vector3(W(6), V(6), 0.03f), motif, small: true);
        }

        if (look.Trim is { } trim)
        {
            // Piping round the collar and down the tunic's edge.
            body.Box(new Vector3(0, torsoTop - V(2), (depth / 2) - 0.01f), new Vector3(W(7), V(2.2f), 0.03f), trim);
            body.Box(new Vector3(0, torsoMid, (depth / 2) + 0.002f), new Vector3(W(1.2f), torsoTop - _hipY - V(4), 0.02f), trim);
        }

        if (look.HiVis)
        {
            // Reflective bands round the body.
            body.Pill(new Vector3(0, _hipY + V(8 * s), 0), new Vector3((shW * 2) + W(3), V(2.2f), depth * 1.05f), new Color("#E9EEF2"));
            body.Pill(new Vector3(0, _hipY + V(14 * s), 0), new Vector3((shW * 2) + W(3), V(2.2f), depth * 1.05f), new Color("#E9EEF2"));
        }

        if (look.Apron is { } apron)
        {
            body.Box(new Vector3(0, _hipY + V(4), (depth / 2) + 0.004f), new Vector3(shW * 1.5f, torsoTop - _hipY + V(2), 0.025f), apron);
        }

        if (look.Tie is { } tie)
        {
            body.Box(new Vector3(0, torsoMid + V(2), (depth / 2) + 0.006f), new Vector3(W(2.6f), torsoTop - _hipY - V(6), 0.02f), tie);
        }

        if (look.Epaulettes is { } ep)
        {
            body.Box(new Vector3(-shW, torsoTop - V(1), 0), new Vector3(W(5), V(1.6f), depth * 0.6f), ep);
            body.Box(new Vector3(shW, torsoTop - V(1), 0), new Vector3(W(5), V(1.6f), depth * 0.6f), ep);
        }

        if (look.Bump)
        {
            body.Ball(new Vector3(0, _hipY + V(8), depth * 0.3f), new Vector3(shW * 1.6f, V(15), depth * 1.1f), dress ? look.Top : top);
        }

        if (dress)
        {
            // A dress flares from the waist to the knee.
            body.Cylinder(new Vector3(0, _hipY - V(4 * s), 0), hipW * 1.25f, V(16 * s), depth * 0.62f, look.Top, cone: true);
        }
        else if (look.Legs == LegKind.Skirt)
        {
            body.Cylinder(new Vector3(0, _hipY - V(3 * s), 0), hipW * 1.25f, V(12 * s), depth * 0.6f, look.Trousers, cone: true);
        }
        else
        {
            // Hips: the trousers' seat.
            body.Pill(new Vector3(0, _hipY + V(1), 0), new Vector3(hipW * 2, V(9 * s), depth * 0.92f), look.Trousers);
        }

        // ---- staff kit
        var chest = new Vector3(0, torsoMid + V(5 * s), depth / 2);
        if (look.Lanyard)
        {
            body.Box(chest + new Vector3(-W(2.2f), V(4), 0.006f), new Vector3(W(1), V(9), 0.01f), LanyardBlue, Basis.FromEuler(new Vector3(0, 0, -0.35f)));
            body.Box(chest + new Vector3(W(2.2f), V(4), 0.006f), new Vector3(W(1), V(9), 0.01f), LanyardBlue, Basis.FromEuler(new Vector3(0, 0, 0.35f)));
            body.Box(chest + new Vector3(0, -V(1.5f), 0.01f), new Vector3(W(4), V(5), 0.015f), Colors.White);
        }

        if (look.Badge is { } badge)
        {
            body.Box(chest + new Vector3(-shW * 0.5f, V(1), 0.01f), new Vector3(W(3.5f), V(2.5f), 0.015f), badge);
        }

        if (look.Pocket)
        {
            body.Box(chest + new Vector3(shW * 0.5f, -V(2), 0.004f), new Vector3(W(4), V(4), 0.012f), top.Darkened(0.15f));
            body.Box(chest + new Vector3(shW * 0.42f, V(0.5f), 0.008f), new Vector3(W(0.8f), V(3), 0.012f), new Color("#2F6FD0"));
        }

        if (look.FobWatch)
        {
            body.Ball(chest + new Vector3(shW * 0.5f, V(3), 0.012f), new Vector3(W(2.4f), V(2.4f), 0.02f), new Color("#E8EDF2"), small: true);
        }

        if (look.Radio)
        {
            body.Box(new Vector3(-hipW * 0.9f, _hipY + V(4), depth * 0.4f), new Vector3(W(2.6f), V(5), 0.05f), new Color("#22262B"));
        }

        // ---- neck and head
        var headY = shY + V(2.2f * s) + V(0.86f * 7.5f * look.HeadScale) + V(1);
        var head = new Vector3(0, headY, stoop * 0.3f);
        body.Cylinder(new Vector3(0, shY + V(2.5f * s), stoop * 0.15f), W(3.2f * s), V(5 * s), W(3.2f * s), look.Skin);
        body.Ball(head, new Vector3(headR * 2, headR * 2.05f, headR * 1.95f), look.Skin);

        Head(body, look, head, headR);

        if (look.Stethoscope)
        {
            body.Ring(new Vector3(0, shY + V(1), 0.01f), shW * 0.62f, new Color("#2B2F33"), Basis.FromEuler(new Vector3(0.35f, 0, 0)), 0.6f);
            body.Ball(new Vector3(-shW * 0.35f, torsoMid, (depth / 2) + 0.01f), new Vector3(W(3), V(3), 0.03f), Metal, small: true);
        }

        var bodyMesh = body.AddTo(_body);
        if (stoop > 0)
        {
            _body.Rotation = new Vector3(stoop, 0, 0);
        }

        // ---- arms (pivot at the shoulder, hanging straight down)
        var hands = look.Gloves ?? look.Skin;
        var sleeves = look.Jacket ?? (look.Kind is TopKind.Dress or TopKind.Tee or TopKind.Polo or TopKind.Scrubs or TopKind.Tunic && !look.LongSleeves ? (Color?)null : look.Top);
        var armLength = shY - _hipY + V(2);
        _armL = Arm(-1, shW + (armR * 0.7f), shY - V(1), armLength, armR, look.Jacket ?? look.Top, sleeves, look.Skin, hands);
        _armR = Arm(1, shW + (armR * 0.7f), shY - V(1), armLength, armR, look.Jacket ?? look.Top, sleeves, look.Skin, hands);

        // ---- legs (pivot at the hip; the shin hangs from the knee)
        var bare = dress || look.Legs == LegKind.Skirt;
        var legColour = bare ? look.Tights ?? look.Skin : look.Trousers;
        var shinColour = bare || look.Legs == LegKind.Shorts ? look.Tights ?? look.Skin : look.Trousers;
        var shoes = look.ShoeColour ?? Figures.Shoes;
        (_thighL, _kneeL) = Leg(-gap, legR, legColour, shinColour, shoes, look.Legs == LegKind.Jeans ? look.Trousers.Lightened(0.25f) : null);
        (_thighR, _kneeR) = Leg(gap, legR, legColour, shinColour, shoes, look.Legs == LegKind.Jeans ? look.Trousers.Lightened(0.25f) : null);

        if (look.Stick)
        {
            var stick = new MeshBatch();
            stick.Rod(new Vector3(0, -armLength, 0.05f), new Vector3(0.03f, -_hipY - V(1) - (shY - _hipY) + armLength - V(2), 0.12f), W(0.9f), new Color("#6B4A2E"), small: true);
            stick.AddTo(_armR);
        }

        if (look.Frame)
        {
            // A walking frame stands in front, its handles at hip height.
            var frame = new MeshBatch();
            var fy = _hipY + V(2);
            foreach (var x in new[] { -W(12), W(12) })
            {
                frame.Rod(new Vector3(x, 0, W(11)), new Vector3(x, fy, W(9)), W(0.8f), Metal, small: true);
                frame.Rod(new Vector3(x, 0, W(2)), new Vector3(x, fy, W(4)), W(0.8f), Metal, small: true);
                frame.Rod(new Vector3(x, fy, W(4)), new Vector3(x, fy, W(9)), W(0.8f), Metal, small: true);
            }

            frame.Rod(new Vector3(-W(12), fy * 0.55f, W(10)), new Vector3(W(12), fy * 0.55f, W(10)), W(0.8f), Metal, small: true);
            frame.AddTo(this);
        }

        if (look.Teddy)
        {
            var teddy = new MeshBatch();
            var brown = new Color("#9A6B43");
            teddy.Ball(new Vector3(0, -armLength - V(1), W(2)), new Vector3(W(4.5f), V(5), W(4)), brown, small: true);
            teddy.Ball(new Vector3(0, -armLength + V(3.5f), W(2)), new Vector3(W(3.6f), V(3.6f), W(3.4f)), brown, small: true);
            teddy.AddTo(_armL);
        }

        _ = bodyMesh;
    }

    /// <summary>The head's features (ears, face, glasses, beard, mask) and hair, on a head ball already drawn at <paramref name="head"/>.</summary>
    public static void Head(MeshBatch b, PersonLook look, Vector3 head, float headR, bool asleep = false)
    {
        // Ears.
        b.Ball(head + new Vector3(-headR * 0.97f, -V(0.5f), 0), new Vector3(W(2.6f), V(3.4f), W(2)), look.Skin.Darkened(0.05f), small: true);
        b.Ball(head + new Vector3(headR * 0.97f, -V(0.5f), 0), new Vector3(W(2.6f), V(3.4f), W(2)), look.Skin.Darkened(0.05f), small: true);

        // Face: dot eyes and a smile, on the front of the head.
        var face = head + new Vector3(0, 0, headR * 0.93f);
        var eyeY = V(0.6f);
        if (asleep)
        {
            // Resting, eyes closed: two little lids.
            b.Box(face + new Vector3(-W(2.6f), eyeY - V(0.3f), -0.004f), new Vector3(W(2.6f), V(0.7f), 0.02f), look.Skin.Darkened(0.5f));
            b.Box(face + new Vector3(W(2.6f), eyeY - V(0.3f), -0.004f), new Vector3(W(2.6f), V(0.7f), 0.02f), look.Skin.Darkened(0.5f));
        }
        else
        {
            b.Ball(face + new Vector3(-W(2.6f), eyeY, 0), new Vector3(W(2), V(2.2f), W(1.2f)), Ink, small: true);
            b.Ball(face + new Vector3(W(2.6f), eyeY, 0), new Vector3(W(2), V(2.2f), W(1.2f)), Ink, small: true);
        }
        var mouthY = -V(3.2f);
        var mouthColour = look.Lips ?? new Color("#7A3B32");
        if (look.Mouth == Mouth.Flat)
        {
            b.Box(face + new Vector3(0, mouthY, -0.005f), new Vector3(W(3.2f), V(0.8f), 0.02f), mouthColour);
        }
        else
        {
            var wide = look.Mouth == Mouth.Grin ? 1.25f : 1f;
            b.Box(face + new Vector3(0, mouthY - V(0.4f), -0.008f), new Vector3(W(2.2f * wide), V(0.9f), 0.02f), mouthColour);
            b.Box(face + new Vector3(-W(1.7f * wide), mouthY + V(0.2f), -0.012f), new Vector3(W(1.4f), V(0.9f), 0.02f), mouthColour, Basis.FromEuler(new Vector3(0, 0, -0.55f)));
            b.Box(face + new Vector3(W(1.7f * wide), mouthY + V(0.2f), -0.012f), new Vector3(W(1.4f), V(0.9f), 0.02f), mouthColour, Basis.FromEuler(new Vector3(0, 0, 0.55f)));
        }

        if (look.Blush)
        {
            var pink = new Color("#F08C8C", 1f).Lerp(look.Skin, 0.4f);
            b.Ball(face + new Vector3(-W(4.4f), -V(1.6f), -0.02f), new Vector3(W(2.6f), V(1.6f), 0.02f), pink, small: true);
            b.Ball(face + new Vector3(W(4.4f), -V(1.6f), -0.02f), new Vector3(W(2.6f), V(1.6f), 0.02f), pink, small: true);
        }

        if (look.Glasses)
        {
            var frame = look.GlassesFrame ?? Ink;
            var facing = Basis.FromEuler(new Vector3(Mathf.Pi / 2, 0, 0));
            b.Ring(face + new Vector3(-W(2.7f), eyeY, 0.012f), W(2.3f), frame, facing, 0.35f);
            b.Ring(face + new Vector3(W(2.7f), eyeY, 0.012f), W(2.3f), frame, facing, 0.35f);
            b.Box(face + new Vector3(0, eyeY + V(0.4f), 0.012f), new Vector3(W(1.4f), V(0.6f), 0.01f), frame);
        }

        if (look.Beard || look.Stubble)
        {
            var colour = look.Stubble && !look.Beard ? look.Skin.Lerp(look.Hair, 0.35f) : look.Hair;
            b.Ball(head + new Vector3(0, -headR * 0.42f, headR * 0.35f), new Vector3(headR * 1.75f, headR * 1.1f, headR * 1.3f), colour);
            b.Box(face + new Vector3(0, mouthY - V(0.4f), 0.01f), new Vector3(W(2.6f), V(0.9f), 0.02f), mouthColour);
        }

        if (look.Moustache)
        {
            b.Box(face + new Vector3(0, mouthY + V(1.4f), 0.006f), new Vector3(W(5), V(1.4f), 0.03f), look.Hair);
        }

        if (look.Mask)
        {
            b.Ball(head + new Vector3(0, -headR * 0.38f, headR * 0.62f), new Vector3(headR * 1.5f, headR * 0.85f, headR * 0.6f), new Color("#9CC9E0"));
        }

        Hair(b, look, head, headR);
    }

    /// <summary>Hair (or a cap, scarf or turban), fitted over the head.</summary>
    private static void Hair(MeshBatch b, PersonLook look, Vector3 head, float r)
    {
        var hair = look.Hair;
        var cap = look.Cap;
        var back = new Vector3(0, 0, -r * 0.18f);

        // The hair's crown: a cap over the top and back of the head.
        void Crown(float lift = 0.32f, float size = 1.06f, float forward = -0.12f) =>
            b.Ball(head + new Vector3(0, r * lift, r * forward), new Vector3(r * 2 * size, r * 1.55f * size, r * 2 * size), hair);

        switch (look.Style)
        {
            case HairStyle.Bald:
                break;
            case HairStyle.Balding:
            case HairStyle.Receding:
                // A ring round the back and sides, the crown bare (or just thin on top).
                b.Ball(head + new Vector3(0, -r * 0.05f, -r * 0.3f), new Vector3(r * 2.08f, r * 1.3f, r * 1.7f), hair);
                if (look.Style == HairStyle.Receding)
                {
                    b.Ball(head + new Vector3(0, r * 0.45f, -r * 0.35f), new Vector3(r * 1.7f, r * 1.1f, r * 1.5f), hair);
                }

                break;
            case HairStyle.Crop:
                Crown(0.38f, 1.02f, -0.15f);
                break;
            case HairStyle.Curly:
            case HairStyle.Perm:
                Crown(0.3f, 1.12f, -0.1f);
                for (var i = 0; i < 6; i++)
                {
                    var a = i * Mathf.Tau / 6;
                    b.Ball(head + new Vector3(Mathf.Cos(a) * r * 0.85f, r * 0.55f, (Mathf.Sin(a) * r * 0.75f) - (r * 0.15f)), new Vector3(r * 0.75f, r * 0.75f, r * 0.75f), hair, small: true);
                }

                break;
            case HairStyle.Afro:
                b.Ball(head + new Vector3(0, r * 0.45f, -r * 0.2f), new Vector3(r * 2.7f, r * 2.3f, r * 2.6f), hair);
                break;
            case HairStyle.Long:
            case HairStyle.Wavy:
                Crown(0.3f, 1.08f, -0.12f);
                b.Pill(head + new Vector3(0, -r * 0.9f, -r * 0.45f), new Vector3(r * 2.1f, r * 2.4f, r * 1.2f), hair);
                break;
            case HairStyle.Bob:
                Crown(0.28f, 1.1f, -0.1f);
                b.Ball(head + new Vector3(0, -r * 0.25f, -r * 0.25f), new Vector3(r * 2.25f, r * 1.5f, r * 1.8f), hair);
                break;
            case HairStyle.Ponytail:
                Crown(0.32f, 1.05f, -0.14f);
                b.Pill(head + new Vector3(0, -r * 0.5f, -r * 1.15f), new Vector3(r * 0.7f, r * 1.9f, r * 0.7f), hair);
                if (look.Bobble is { } bobble)
                {
                    b.Ball(head + new Vector3(0, r * 0.35f, -r * 1.05f), new Vector3(r * 0.4f, r * 0.4f, r * 0.4f), bobble, small: true);
                }

                break;
            case HairStyle.Pigtails:
                Crown(0.32f, 1.05f, -0.12f);
                b.Pill(head + new Vector3(-r * 1.05f, -r * 0.4f, -r * 0.3f), new Vector3(r * 0.6f, r * 1.4f, r * 0.6f), hair);
                b.Pill(head + new Vector3(r * 1.05f, -r * 0.4f, -r * 0.3f), new Vector3(r * 0.6f, r * 1.4f, r * 0.6f), hair);
                break;
            case HairStyle.Bun:
                Crown(0.32f, 1.05f, -0.14f);
                b.Ball(head + new Vector3(0, r * 0.75f, -r * 0.85f), new Vector3(r * 0.95f, r * 0.95f, r * 0.95f), hair);
                break;
            case HairStyle.Braids:
                Crown(0.32f, 1.06f, -0.12f);
                for (var i = -2; i <= 2; i++)
                {
                    b.Pill(head + new Vector3(i * r * 0.32f, -r * 0.9f, -r * 0.75f), new Vector3(r * 0.32f, r * 2.2f, r * 0.32f), hair);
                }

                break;
            case HairStyle.Quiff:
                Crown(0.36f, 1.04f, -0.14f);
                b.Ball(head + new Vector3(0, r * 0.95f, r * 0.45f), new Vector3(r * 1.3f, r * 0.7f, r * 0.9f), hair);
                break;
            case HairStyle.SidePart:
                Crown(0.36f, 1.05f, -0.12f);
                b.Ball(head + new Vector3(-r * 0.4f, r * 0.7f, r * 0.4f), new Vector3(r * 1.3f, r * 0.55f, r * 0.9f), hair);
                break;
            case HairStyle.Hijab:
                // A scarf wrapped over the head and down to the shoulders, the face left open.
                b.Ball(head + new Vector3(0, r * 0.12f, -r * 0.18f), new Vector3(r * 2.3f, r * 2.35f, r * 2.2f), hair);
                b.Cylinder(head + new Vector3(0, -r * 1.05f, -r * 0.1f), r * 1.15f, r * 0.9f, r * 1.0f, hair, cone: true);
                break;
            case HairStyle.Turban:
                b.Ball(head + new Vector3(0, r * 0.55f, -r * 0.05f), new Vector3(r * 2.25f, r * 1.7f, r * 2.25f), hair);
                b.Ball(head + new Vector3(0, r * 0.9f, r * 0.5f), new Vector3(r * 0.7f, r * 0.7f, r * 0.4f), hair.Darkened(0.12f), small: true);
                break;
            default:
                Crown();
                break;
        }

        if (cap is { } c)
        {
            // A theatre cap: a soft dome over the hair.
            b.Ball(head + new Vector3(0, r * 0.38f, -r * 0.05f) + back * 0, new Vector3(r * 2.2f, r * 1.6f, r * 2.2f), c);
        }
    }

    private Node3D Arm(int side, float x, float y, float length, float radius, Color cloth, Color? sleeve, Color skin, Color hand)
    {
        var pivot = new Node3D { Name = side < 0 ? "ArmL" : "ArmR", Position = new Vector3(side * x, y, 0) };
        _body.AddChild(pivot);
        var b = new MeshBatch();
        var elbow = new Vector3(0, -length * 0.45f, 0);
        var wrist = new Vector3(0, -length + radius, 0);
        b.Capsule(Vector3.Zero, elbow, radius * 1.1f, cloth);
        b.Capsule(elbow, wrist, radius, sleeve ?? skin);
        b.Ball(wrist + new Vector3(0, -radius * 0.5f, 0), new Vector3(radius * 2.6f, radius * 2.6f, radius * 2.6f), hand, small: true);
        b.AddTo(pivot);
        return pivot;
    }

    private (Node3D Thigh, Node3D Knee) Leg(float x, float radius, Color thighColour, Color shinColour, Color shoes, Color? hem)
    {
        var thigh = new Node3D { Position = new Vector3(x, _hipY, 0) };
        _body.AddChild(thigh);
        var kneeDrop = _hipY * 0.5f;
        var tb = new MeshBatch();
        tb.Capsule(Vector3.Zero, new Vector3(0, -kneeDrop, 0), radius * 1.05f, thighColour);
        tb.AddTo(thigh);
        var knee = new Node3D { Position = new Vector3(0, -kneeDrop, 0) };
        thigh.AddChild(knee);
        var sb = new MeshBatch();
        var ankle = -(_hipY - kneeDrop) + V(2.4f);
        sb.Capsule(Vector3.Zero, new Vector3(0, ankle, 0), radius, shinColour);
        if (hem is { } h)
        {
            sb.Cylinder(new Vector3(0, ankle + V(1.2f), 0), radius * 1.08f, V(0.8f), radius * 1.08f, h);
        }

        sb.Ball(new Vector3(0, ankle - V(1.2f), W(1.8f)), new Vector3(radius * 2.3f, V(3.4f), radius * 3.4f), shoes, small: true);
        sb.AddTo(knee);
        return (thigh, knee);
    }

    /// <summary>Shows (or clears) the baby in their arms.</summary>
    public void Hold(BabyLook? baby)
    {
        if (baby == _babyLook)
        {
            return;
        }

        _babyLook = baby;
        _baby?.QueueFree();
        _baby = null;
        if (baby is null)
        {
            return;
        }

        _baby = Baby3D.Bundle(baby);
        _baby.Position = new Vector3(0, _hipY + V(12), W(9));
        _baby.Rotation = new Vector3(0, 0, Mathf.Pi / 2);
        _body.AddChild(_baby);
    }

    /// <summary>Shows (or clears) the white pharmacy bag in their hand.</summary>
    public void CarryBag(bool on)
    {
        if (on == (_bag is not null))
        {
            return;
        }

        if (!on)
        {
            _bag!.QueueFree();
            _bag = null;
            return;
        }

        _bag = new Node3D { Name = "Bag" };
        var b = new MeshBatch();
        var drop = -(V(41) - _hipY) - V(3);
        b.Box(new Vector3(0, drop - V(3), W(1)), new Vector3(W(6), V(7), W(3)), new Color("#F7F5EE"));
        b.Box(new Vector3(0, drop - V(3), W(2.6f)), new Vector3(W(1), V(3.5f), 0.01f), new Color("#1E8E3E"));
        b.Box(new Vector3(0, drop - V(3), W(2.6f)), new Vector3(W(3.5f), V(1), 0.01f), new Color("#1E8E3E"));
        b.AddTo(_bag);
        _armR.AddChild(_bag);
    }

    private bool _faded;

    /// <summary>Fades the figure (a patient who has died).</summary>
    public void Modulate(bool faded)
    {
        if (faded == _faded)
        {
            return;
        }

        _faded = faded;
        Fade(this, faded ? 0.5f : 0f);
    }

    private static void Fade(Node node, float transparency)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is GeometryInstance3D g)
            {
                g.Transparency = transparency;
            }

            Fade(child, transparency);
        }
    }

    /// <summary>Sets the pose. <paramref name="stride"/> is the walking phase (radians).</summary>
    public void SetPose(Pose pose, double stride, float lean = 0)
    {
        var holding = _baby is not null;
        var swing = pose == Pose.Walking ? (float)Math.Sin(stride) * 0.55f : 0f;
        var bob = pose == Pose.Walking ? -(float)Math.Abs(Math.Cos(stride)) * V(1.1f) : 0f;
        _body.Position = new Vector3(0, (pose == Pose.Seated ? -_seatDrop : 0) + bob, 0);
        var stoop = Look.Stoop ? 0.18f : 0f;
        _body.Rotation = new Vector3(pose == Pose.Cpr ? 0.45f : stoop, 0, 0);

        switch (pose)
        {
            case Pose.Seated:
                _thighL.Rotation = _thighR.Rotation = new Vector3(-Mathf.Pi / 2, 0, 0);
                _kneeL.Rotation = _kneeR.Rotation = new Vector3(Mathf.Pi / 2, 0, 0);
                _thighL.Position = _thighL.Position with { Z = 0 };
                _armL.Rotation = new Vector3(-0.35f, 0, 0.05f);
                _armR.Rotation = new Vector3(-0.35f, 0, -0.05f);
                break;
            case Pose.Cpr:
                _thighL.Rotation = _thighR.Rotation = new Vector3(-0.45f, 0, 0);
                _kneeL.Rotation = _kneeR.Rotation = Vector3.Zero;
                _armL.Rotation = new Vector3(-1.25f, 0, -0.25f);
                _armR.Rotation = new Vector3(-1.25f, 0, 0.25f);
                break;
            default:
                _thighL.Rotation = new Vector3(swing, 0, 0);
                _thighR.Rotation = new Vector3(-swing, 0, 0);
                _kneeL.Rotation = new Vector3(Math.Max(0, -swing) * 0.6f, 0, 0);
                _kneeR.Rotation = new Vector3(Math.Max(0, swing) * 0.6f, 0, 0);
                _armL.Rotation = new Vector3(-swing * 0.8f, 0, 0.06f);
                _armR.Rotation = new Vector3(swing * 0.8f, 0, -0.06f);
                break;
        }

        if (holding)
        {
            // Cradling the baby: both forearms forward.
            _armL.Rotation = new Vector3(-0.95f, 0, -0.35f);
            _armR.Rotation = new Vector3(-0.95f, 0, 0.35f);
        }

        if (_bag is not null && pose != Pose.Seated)
        {
            _armR.Rotation = new Vector3(swing * 0.3f, 0, -0.12f);
        }

        if (Look.Frame || Look.Stick)
        {
            _armR.Rotation = new Vector3(-0.3f, 0, -0.1f);
            if (Look.Frame)
            {
                _armL.Rotation = new Vector3(-0.3f, 0, 0.1f);
            }
        }

        _ = lean;
    }
}

/// <summary>Babies: a little swaddled bundle with a hat.</summary>
public static class Baby3D
{
    public static Node3D Bundle(BabyLook look, float scale = 1f)
    {
        var node = new Node3D { Name = "Baby" };
        var b = new MeshBatch();
        var r = 0.09f * scale;
        b.Pill(new Vector3(0, -r * 0.4f, 0), new Vector3(r * 2.2f, r * 3.6f, r * 2f), look.Wrap);
        b.Box(new Vector3(0, r * 0.5f, r * 0.95f), new Vector3(r * 1.9f, r * 0.35f, r * 0.2f), look.Trim);
        b.Ball(new Vector3(0, r * 1.55f, 0), new Vector3(r * 1.9f, r * 1.9f, r * 1.9f), look.Skin);
        b.Ball(new Vector3(0, r * 1.95f, -r * 0.15f), new Vector3(r * 1.95f, r * 1.3f, r * 1.9f), look.Hat);
        b.Ball(new Vector3(-r * 0.35f, r * 1.55f, r * 0.9f), new Vector3(r * 0.25f, r * 0.12f, r * 0.1f), new Color("#1D2B36"), small: true);
        b.Ball(new Vector3(r * 0.35f, r * 1.55f, r * 0.9f), new Vector3(r * 0.25f, r * 0.12f, r * 0.1f), new Color("#1D2B36"), small: true);
        b.AddTo(node);
        return node;
    }
}
