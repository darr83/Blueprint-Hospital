using System;
using System.Collections.Generic;
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
/// <summary>A cleaner's kit, drawn with them: pushing the bucket, or mopping or sweeping with the bucket beside them.</summary>
public enum CleanKit
{
    None,
    Pushing,
    Mopping,
    Sweeping,
}

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
    private readonly List<LegParts> _legs = new();
    private float _kneeDrop;
    private float _ankle;
    private Node3D? _baby;
    private Node3D? _bag;
    private BabyLook? _babyLook;

    private float _hipY;
    private float _armLength;
    private Node3D? _clipboard;
    private Node3D? _phone;
    private Node3D? _magazine;
    private Node3D? _cup;
    private Node3D? _can;
    private Node3D? _pot;
    private Node3D? _keypad;
    private Node3D? _handset;
    private Node3D? _dripBag;
    private Node3D? _box;
    private Node3D? _tray;
    private Node3D? _ambuBag;
    private Node3D? _roll;
    private Node3D? _kit;
    private Node3D _bucket = null!;
    private Node3D _mopHead = null!;
    private Node3D _mopHandle = null!;
    private Node3D _brushHead = null!;
    private Node3D _brushHandle = null!;
    private Node3D _wetSign = null!;

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
        _armLength = armLength;
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

    /// <summary>
    /// One leg's parts. The thigh and shin meshes hang under their own holders, so sitting can stretch them
    /// along their length (the figures' legs are short and stylised: drawn true, they would stop half way down
    /// a chair) without squashing the knee and foot hung below them.
    /// </summary>
    private sealed record LegParts(Node3D ThighMesh, Node3D Knee, Node3D ShinMesh, Node3D Foot);

    private (Node3D Thigh, Node3D Knee) Leg(float x, float radius, Color thighColour, Color shinColour, Color shoes, Color? hem)
    {
        var thigh = new Node3D { Position = new Vector3(x, _hipY, 0) };
        _body.AddChild(thigh);
        var kneeDrop = _hipY * 0.5f;
        _kneeDrop = kneeDrop;
        var thighMesh = new Node3D { Name = "ThighMesh" };
        thigh.AddChild(thighMesh);
        var tb = new MeshBatch();
        tb.Capsule(Vector3.Zero, new Vector3(0, -kneeDrop, 0), radius * 1.05f, thighColour);
        tb.AddTo(thighMesh);
        var knee = new Node3D { Position = new Vector3(0, -kneeDrop, 0) };
        thigh.AddChild(knee);
        var shinMesh = new Node3D { Name = "ShinMesh" };
        knee.AddChild(shinMesh);
        var sb = new MeshBatch();
        var ankle = -(_hipY - kneeDrop) + V(2.4f);
        _ankle = ankle;
        sb.Capsule(Vector3.Zero, new Vector3(0, ankle, 0), radius, shinColour);
        if (hem is { } h)
        {
            sb.Cylinder(new Vector3(0, ankle + V(1.2f), 0), radius * 1.08f, V(0.8f), radius * 1.08f, h);
        }

        sb.AddTo(shinMesh);
        var foot = new Node3D { Name = "Foot", Position = new Vector3(0, ankle, 0) };
        knee.AddChild(foot);
        var fb = new MeshBatch();
        fb.Ball(new Vector3(0, -V(1.2f), W(1.8f)), new Vector3(radius * 2.3f, V(3.4f), radius * 3.4f), shoes, small: true);
        fb.AddTo(foot);
        _legs.Add(new LegParts(thighMesh, knee, shinMesh, foot));
        return (thigh, knee);
    }

    /// <summary>
    /// What the person is sitting on, in their own frame (they face +z): how high the seat's top is, how far
    /// back their hips go, and where the front edge of the seat is. An ordinary chair unless told otherwise.
    /// </summary>
    public readonly record struct SeatShape(float Top, float HipZ, float FrontZ)
    {
        /// <summary>Any chair: the seat's top 14 px up, hips back against the backrest, knees at the front edge.</summary>
        public static SeatShape Chair => new(Kit3D.H(14), -0.13f, 0.33f);

        /// <summary>A wheelchair (see <see cref="Carriers3D.Wheelchair"/>), made smaller for a child.</summary>
        public static SeatShape Wheelchair(float scale) => new(Kit3D.H(13) * Mathf.Clamp(scale, 0.6f, 1f), -0.06f, 0.24f);

        /// <summary>Perched on the side of a bed, feet towards the floor.</summary>
        public static SeatShape BedSide => new(Kit3D.H(18), -0.06f, 0.14f);
    }

    /// <summary>The seat they sit on when <see cref="SetPose"/> is given <see cref="Pose.Seated"/>.</summary>
    public SeatShape Seat { get; set; } = SeatShape.Chair;

    /// <summary>
    /// Sets the legs' lengths: true to life standing, stretched to fit the seat when sitting, so the thighs lie
    /// along it to its front edge and the feet reach the floor. Children's legs keep the same proportions, so
    /// their knees stop short of the edge and their feet swing clear of the floor.
    /// </summary>
    private void FitLegs(bool seated, float hip)
    {
        var adultThigh = V(19) * 0.5f;
        var adultShin = V(19) - adultThigh - V(2.4f);
        var thigh = seated ? Mathf.Clamp((Seat.FrontZ - Seat.HipZ) / adultThigh, 1f, 2.2f) : 1f;
        var shin = seated ? Mathf.Clamp((hip - V(2.9f)) / adultShin, 1f, 2.2f) : 1f;
        foreach (var leg in _legs)
        {
            leg.ThighMesh.Scale = new Vector3(1, thigh, 1);
            leg.Knee.Position = new Vector3(0, -_kneeDrop * thigh, 0);
            leg.ShinMesh.Scale = new Vector3(1, shin, 1);
            leg.Foot.Position = new Vector3(0, _ankle * shin, 0);
        }
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
        // Sitting: hips down on the seat and back against the backrest, the bottom just sinking into the cushion.
        var seatedHip = Seat.Top + V((4.5f * Look.Scale) - 1.5f);
        _body.Position = pose == Pose.Seated ? new Vector3(0, seatedHip - _hipY, Seat.HipZ) : new Vector3(0, bob, 0);
        FitLegs(pose == Pose.Seated, seatedHip);
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

    /// <summary>
    /// Animates what they are doing with their hands on top of the pose set by <see cref="SetPose"/>:
    /// examining, typing, pressing vending machine buttons, reading, and so on (see <see cref="Act"/>).
    /// <paramref name="t"/> is the animation clock in seconds; each person runs it from their own offset.
    /// </summary>
    public void SetAct(Act act, Pose pose, double t)
    {
        var seated = pose == Pose.Seated;
        var f = (float)t;
        Show(act);
        Rock(pose != Pose.Walking, f);
        if (act == Act.None || pose == Pose.Cpr || (pose == Pose.Walking && act != Act.Support))
        {
            return;
        }

        var lean = 0f;
        float S(float speed, float phase = 0) => Mathf.Sin((f * speed) + phase);

        // Arm rotations: x below zero swings the arm forward (-pi/2 points it straight ahead);
        // z turns it in across the body (positive for the left arm, negative for the right).
        Vector3 left;
        Vector3 right;
        switch (act)
        {
            case Act.Examine:
            {
                // Lean in and listen for five seconds, then stand up and jot a note.
                var cycle = Mathf.PosMod(f, 7f);
                if (cycle < 5f)
                {
                    lean = 0.26f;
                    right = new Vector3(-1.15f + (0.08f * S(1.3f)), 0, -0.22f + (0.06f * S(0.9f, 1)));
                    left = new Vector3(-0.55f, 0, 0.05f);
                    Hide(_clipboard);
                }
                else
                {
                    lean = 0.05f;
                    left = new Vector3(-1.05f, 0, 0.35f);
                    right = new Vector3(-1.0f + (0.05f * S(14)), 0, -0.25f + (0.04f * S(9)));
                    _clipboard = Prop(_clipboard, _armL, Clipboard);
                }

                break;
            }

            case Act.Observations:
                // BP cuff: one hand on the patient's arm squeezing the bulb, the chart in the other.
                left = new Vector3(-1.0f, 0, 0.35f);
                right = new Vector3(-1.05f + (0.07f * Mathf.Abs(S(5))), 0, -0.12f);
                lean = 0.08f;
                break;
            case Act.Bloods:
                lean = 0.34f;
                left = new Vector3(-0.85f, 0, 0.3f);
                right = new Vector3(-0.92f + (0.05f * S(2.2f)), 0, -0.32f);
                break;
            case Act.Medicine:
            {
                // Hold out the pot, then a step back.
                var reach = 0.5f + (0.5f * S(0.9f));
                right = new Vector3(-0.8f - (0.6f * reach), 0, -0.1f);
                left = new Vector3(-0.15f, 0, 0.06f);
                lean = 0.1f * reach;
                break;
            }

            case Act.Typing:
                left = new Vector3(-1.05f + (0.06f * Mathf.Max(0, S(11))), 0, 0.28f);
                right = new Vector3(-1.05f + (0.06f * Mathf.Max(0, S(11, 2.2f))), 0, -0.28f);
                lean = 0.1f;
                break;
            case Act.Operate:
                lean = 0.3f;
                left = new Vector3(-0.95f + (0.07f * S(3.1f)), 0, 0.38f + (0.05f * S(2.3f)));
                right = new Vector3(-0.95f + (0.07f * S(2.7f, 1.4f)), 0, -0.38f + (0.05f * S(1.9f, 0.4f)));
                break;
            case Act.Talk:
                right = new Vector3(-0.55f + (0.3f * S(2.1f)), 0, -0.2f + (0.15f * S(1.3f)));
                left = new Vector3(-0.35f + (0.22f * S(1.7f, 1.2f)), 0, 0.15f);
                break;
            case Act.Exercise:
            {
                var up = 0.5f - (0.5f * Mathf.Cos(f * 1.8f));
                left = new Vector3(-0.2f - (2.6f * up), 0, -0.15f);
                right = new Vector3(-0.2f - (2.6f * up), 0, 0.15f);
                break;
            }

            case Act.VendPress:
            {
                // Studying the choices, then tapping in the number; the keypad lights up with each press.
                var cycle = Mathf.PosMod(f, 4f);
                _keypad ??= Keypad();
                _keypad.Visible = cycle >= 1.5f && S(9) > 0.2f;
                right = cycle < 1.5f
                    ? new Vector3(-0.35f, 0, -0.05f)
                    : new Vector3(-1.55f + (0.14f * Mathf.Max(0, S(9))), 0, -0.18f);
                left = cycle < 1.5f ? new Vector3(-1.2f, 0, 0.55f) : new Vector3(-0.1f, 0, 0.06f);
                break;
            }

            case Act.VendCollect:
            {
                // Bend down to the tray at the bottom, and come up with a can.
                var down = Mathf.PosMod(f, 3f) < 1.8f;
                lean = down ? 0.55f : 0.1f;
                right = down ? new Vector3(-0.55f, 0, -0.1f) : new Vector3(-1.2f, 0, -0.25f);
                left = new Vector3(-0.1f, 0, 0.06f);
                if (!down)
                {
                    _can = Prop(_can, _armR, Can);
                }
                else
                {
                    Hide(_can);
                }

                break;
            }

            case Act.Eat:
            case Act.Tea:
            {
                // The cup on the table (or in the lap), lifted for a sip every few seconds.
                var cycle = Mathf.PosMod(f, act == Act.Tea ? 6f : 4.5f);
                var sip = cycle < 1.4f ? Mathf.Sin(cycle / 1.4f * Mathf.Pi) : 0f;
                right = new Vector3(-0.9f - (1.35f * sip), 0, -0.25f - (0.35f * sip));
                left = seated ? new Vector3(-0.75f, 0, 0.2f) : new Vector3(-0.1f, 0, 0.06f);
                _cup = Prop(_cup, _armR, Cup);
                break;
            }

            case Act.Phone:
                left = new Vector3(-1.0f, 0, 0.42f);
                right = new Vector3(-1.05f + (0.05f * Mathf.Max(0, S(6))), 0, -0.4f);
                lean = seated ? 0f : 0.08f;
                _phone = Prop(_phone, _armR, Phone);
                break;
            case Act.Read:
            {
                left = new Vector3(-0.95f, 0, 0.32f);
                var flip = Mathf.PosMod(f, 8f) > 7.2f;
                right = flip ? new Vector3(-1.25f, 0, -0.05f) : new Vector3(-0.95f, 0, -0.32f);
                _magazine = Prop(_magazine, _armL, Magazine);
                break;
            }

            case Act.Watch:
            {
                var look = Mathf.PosMod(f, 6f) < 1.6f;
                left = look ? new Vector3(-1.45f, 0, 0.7f) : seated ? new Vector3(-0.35f, 0, 0.05f) : new Vector3(-0.05f, 0, 0.06f);
                right = seated ? new Vector3(-0.35f, 0, -0.05f) : new Vector3(-0.05f, 0, -0.06f);
                break;
            }

            case Act.Fidget:
                left = new Vector3(-0.35f, 0, 0.05f);
                right = new Vector3(-0.35f, 0, -0.05f);
                if (seated)
                {
                    _kneeL.Rotation = new Vector3((Mathf.Pi / 2) + (0.4f * S(5)), 0, 0);
                    _kneeR.Rotation = new Vector3((Mathf.Pi / 2) - (0.4f * S(5)), 0, 0);
                }

                break;
            case Act.HangDrip:
            {
                // Both hands up at the hook, hanging the new bag, then a check of the drip rate.
                var up = Mathf.PosMod(f, 6f) < 4f;
                left = up ? new Vector3(-2.65f, 0, 0.2f) : new Vector3(-1.1f, 0, 0.3f);
                right = up ? new Vector3(-2.8f + (0.06f * S(3)), 0, -0.2f) : new Vector3(-1.25f + (0.05f * S(4)), 0, -0.2f);
                _dripBag = Prop(_dripBag, _armR, DripBag);
                _dripBag.Visible = up;
                break;
            }

            case Act.PhoneCall:
                // The handset at the ear; the other hand jots on a pad.
                right = new Vector3(-2.75f, 0, 0.55f);
                left = new Vector3(-0.95f + (0.04f * S(12)), 0, 0.3f + (0.03f * S(7)));
                lean = seated ? 0f : 0.06f;
                _handset = Prop(_handset, _armR, Handset);
                break;
            case Act.CheckIn:
            {
                // Tapping their details into the screen on the counter (which lights up as they tap; see Life3D).
                right = new Vector3(-1.2f + (0.12f * Mathf.Max(0, S(7))), 0, -0.2f);
                left = new Vector3(-0.1f, 0, 0.06f);
                lean = 0.08f;
                break;
            }

            case Act.Reach:
            {
                // Up to the shelf, then down again with the box.
                var cycle = Mathf.PosMod(f, 2.5f);
                var got = cycle > 1.2f;
                right = got ? new Vector3(-1.35f, 0, -0.15f) : new Vector3(-2.7f, 0, -0.1f);
                left = got ? new Vector3(-1.2f, 0, 0.3f) : new Vector3(-0.4f, 0, 0.06f);
                _box = Prop(_box, _armR, Box);
                _box.Visible = got;
                break;
            }

            case Act.Count:
                // Tray in one hand, the other sliding tablets along it in fives.
                lean = 0.22f;
                left = new Vector3(-1.0f, 0, 0.32f);
                right = new Vector3(-1.08f + (0.05f * Mathf.Abs(S(5))), 0, -0.12f + (0.12f * S(2.5f)));
                _tray = Prop(_tray, _armL, PillTray);
                break;
            case Act.Bag:
            {
                // One hand holding the mask on, the other squeezing the bag every few seconds.
                var squeeze = Mathf.Max(0, S(1.6f));
                lean = 0.16f;
                left = new Vector3(-1.2f, 0, 0.35f);
                right = new Vector3(-0.95f - (0.08f * squeeze), 0, -0.2f);
                _ambuBag = Prop(_ambuBag, _armR, AmbuBag);
                _ambuBag.Scale = new Vector3(1 - (0.3f * squeeze), 1, 1 - (0.3f * squeeze));
                break;
            }

            case Act.Wrap:
            {
                // Round and round the limb with the roll of plaster.
                var a = f * 4f;
                lean = 0.26f;
                left = new Vector3(-1.0f + (0.18f * Mathf.Sin(a)), 0, 0.3f + (0.14f * Mathf.Cos(a)));
                right = new Vector3(-1.0f - (0.18f * Mathf.Sin(a)), 0, -0.3f + (0.14f * Mathf.Cos(a)));
                _roll = Prop(_roll, _armR, Roll);
                break;
            }

            case Act.Support:
                // An arm out towards the patient, ready to steady them.
                left = new Vector3(-0.75f, 0, -0.25f);
                right = new Vector3(-0.55f, 0, -0.1f);
                break;
            case Act.Mop:
            {
                // Both hands on the handle, swinging the mop from side to side in front of them.
                var m = S(2.2f);
                lean = 0.16f;
                left = new Vector3(-0.95f, 0, 0.25f - (0.22f * m));
                right = new Vector3(-0.7f, 0, -0.2f - (0.22f * m));
                break;
            }

            case Act.Sweep:
            {
                // Push the brush out, draw it back.
                var p = S(3f);
                lean = 0.12f + (0.06f * p);
                left = new Vector3(-0.9f - (0.25f * p), 0, 0.2f);
                right = new Vector3(-0.65f - (0.25f * p), 0, -0.15f);
                break;
            }

            case Act.WashHands:
                lean = 0.18f;
                left = new Vector3(-0.95f + (0.06f * S(10)), 0, 0.42f);
                right = new Vector3(-0.95f - (0.06f * S(10)), 0, -0.42f);
                break;
            default:
                return;
        }

        if (_baby is null)
        {
            _armL.Rotation = left;
            _armR.Rotation = right;
        }

        if (!seated && lean != 0)
        {
            // Bend at the hips: the body tips forward, the legs stay upright under it.
            _body.Rotation = new Vector3((Look.Stoop ? 0.18f : 0f) + lean, 0, 0);
            _thighL.Rotation = new Vector3(-lean, 0, 0);
            _thighR.Rotation = new Vector3(-lean, 0, 0);
            _kneeL.Rotation = _kneeR.Rotation = Vector3.Zero;
        }
    }

    /// <summary>Hides the props that don't belong to <paramref name="act"/>.</summary>
    private void Show(Act act)
    {
        if (act != Act.Examine)
        {
            Hide(_clipboard);
        }

        if (act is not Act.Observations)
        {
            Hide(_pot);
        }
        else
        {
            _clipboard = Prop(_clipboard, _armL, Clipboard);
        }

        if (act != Act.Phone)
        {
            Hide(_phone);
        }

        if (act != Act.Read)
        {
            Hide(_magazine);
        }

        if (act is not (Act.Eat or Act.Tea))
        {
            Hide(_cup);
        }

        if (act != Act.VendCollect)
        {
            Hide(_can);
        }

        if (act != Act.VendPress)
        {
            Hide(_keypad);
        }

        if (act == Act.Medicine)
        {
            _pot = Prop(_pot, _armR, Pot);
        }

        if (act != Act.PhoneCall)
        {
            Hide(_handset);
        }

        if (act != Act.HangDrip)
        {
            Hide(_dripBag);
        }

        if (act != Act.Reach)
        {
            Hide(_box);
        }

        if (act != Act.Count)
        {
            Hide(_tray);
        }

        if (act != Act.Bag)
        {
            Hide(_ambuBag);
        }

        if (act != Act.Wrap)
        {
            Hide(_roll);
        }
    }

    /// <summary>
    /// A cleaner's kit: the yellow mop bucket on castors, the mop and the wide brush. Pushing, the bucket
    /// rolls in front of them with the mop in the wringer and the brush in the caddy; mopping or sweeping,
    /// the bucket stands beside them (with a wet floor sign out while they mop). Call after <see cref="SetAct"/>.
    /// </summary>
    public void Clean(CleanKit kit, double t)
    {
        if (kit == CleanKit.None)
        {
            Hide(_kit);
            return;
        }

        _kit ??= BuildKit();
        _kit.Visible = true;
        var f = (float)t;
        var hands = new Vector3(0, V(29), W(9));
        var bucket = kit == CleanKit.Pushing ? new Vector3(0, 0, 0.5f) : new Vector3(0.42f, 0, 0.16f);
        _bucket.Position = bucket;
        _wetSign.Visible = kit == CleanKit.Mopping;

        // The tools when they're in the bucket: the mop in the wringer, the brush upside down in the caddy.
        var mopInBucket = (Head: bucket + new Vector3(0, V(5), 0), Top: bucket + new Vector3(0, V(40), -0.12f));
        var brushInBucket = (Bottom: bucket + new Vector3(W(6), V(6), -0.1f), Top: bucket + new Vector3(W(7), V(38), -0.14f));

        switch (kit)
        {
            case CleanKit.Pushing:
                // Pushing the bucket along by the mop handle.
                _armL.Rotation = new Vector3(-0.85f, 0, 0.22f);
                _armR.Rotation = new Vector3(-0.8f, 0, -0.22f);
                Tool(_mopHead, _mopHandle, mopInBucket.Head, hands + new Vector3(0, 0, 0.02f), headUp: false);
                Tool(_brushHead, _brushHandle, brushInBucket.Bottom, brushInBucket.Top, headUp: true);
                break;
            case CleanKit.Mopping:
            {
                var m = Mathf.Sin((f * 2.2f) + 0f);
                var head = new Vector3(-0.22f * m, 0, 0.55f + (0.05f * Mathf.Cos(f * 4.4f)));
                Tool(_mopHead, _mopHandle, head + new Vector3(0, V(1.5f), 0), hands + new Vector3(-0.06f * m, 0, 0), headUp: false);
                Tool(_brushHead, _brushHandle, brushInBucket.Bottom, brushInBucket.Top, headUp: true);
                break;
            }

            default:
            {
                var p = Mathf.Sin(f * 3f);
                var head = new Vector3(0, 0, 0.58f + (0.14f * p));
                Tool(_brushHead, _brushHandle, head + new Vector3(0, V(2.5f), 0), hands + new Vector3(0, 0, 0.06f * p), headUp: false);
                Tool(_mopHead, _mopHandle, mopInBucket.Head, mopInBucket.Top, headUp: false);
                break;
            }
        }
    }

    /// <summary>
    /// Runs a tool's handle from <paramref name="bottom"/> up to <paramref name="top"/>, with its head at the
    /// bottom (in use) or upside down at the top (stood in the bucket's caddy).
    /// </summary>
    private static void Tool(Node3D headNode, Node3D handle, Vector3 bottom, Vector3 top, bool headUp)
    {
        var span = top - bottom;
        var dir = span.Normalized();
        handle.Position = bottom;
        handle.Quaternion = dir.IsEqualApprox(Vector3.Up) ? Quaternion.Identity : new Quaternion(Vector3.Up, dir);
        handle.Scale = new Vector3(1, span.Length(), 1);
        headNode.Position = headUp ? top : bottom;
        headNode.Rotation = headUp ? new Vector3(Mathf.Pi, 0, 0) : Vector3.Zero;
    }

    private Node3D BuildKit()
    {
        var kit = new Node3D { Name = "CleaningKit" };
        AddChild(kit);

        Node3D Part(string name, Action<MeshBatch> build)
        {
            var node = new Node3D { Name = name };
            var b = new MeshBatch();
            build(b);
            b.AddTo(node);
            kit.AddChild(node);
            return node;
        }

        var yellow = new Color("#FBC02D");
        _bucket = Part("Bucket", b =>
        {
            b.Box(new Vector3(0, V(7), 0), new Vector3(W(13), V(10), W(10)), yellow);
            b.Box(new Vector3(0, V(12.1f), W(1.5f)), new Vector3(W(11), V(0.3f), W(6)), new Color("#6E8C99"));
            b.Box(new Vector3(0, V(14), -W(3)), new Vector3(W(12), V(4.5f), W(3.5f)), new Color("#7D8790"));
            b.Box(new Vector3(0, V(16.6f), -W(3)), new Vector3(W(3), V(1), W(5)), new Color("#C62828"));
            b.Rod(new Vector3(-W(6.5f), V(12), W(5)), new Vector3(W(6.5f), V(12), W(5)), 0.012f, new Color("#E0A800"), small: true);
            foreach (var (x, z) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                b.Ball(new Vector3(x * W(5.5f), V(1.2f), z * W(4)), new Vector3(W(2.4f), V(2.4f), W(2.4f)), new Color("#2B2F33"), small: true);
            }

            // The yellow wet floor sign, folded on the side, and a spray bottle in the caddy.
            b.Box(new Vector3(-W(7.2f), V(9), 0), new Vector3(W(0.8f), V(13), W(7)), yellow.Lightened(0.1f));
            b.Cylinder(new Vector3(W(5), V(14), W(3.5f)), W(1.3f), V(5), W(1.3f), new Color("#1E88E5"));
        });
        _mopHead = Part("MopHead", b =>
        {
            b.Ball(new Vector3(0, V(0.8f), 0), new Vector3(W(10), V(2.6f), W(7)), new Color("#ECEFF1"));
            b.Box(new Vector3(0, V(2.4f), 0), new Vector3(W(3.5f), V(1.8f), W(3)), new Color("#1E88E5"));
        });
        _mopHandle = Part("MopHandle", b => b.Rod(Vector3.Zero, Vector3.Up, W(0.9f), new Color("#3F7FD9"), small: true));
        _brushHead = Part("BrushHead", b =>
        {
            b.Box(new Vector3(0, V(0.8f), 0), new Vector3(W(16), V(1.6f), W(2.4f)), new Color("#37474F"));
            b.Box(new Vector3(0, V(2.2f), 0), new Vector3(W(16), V(1.4f), W(2.8f)), new Color("#8D6E4E"));
        });
        _brushHandle = Part("BrushHandle", b => b.Rod(Vector3.Zero, Vector3.Up, W(0.9f), new Color("#C62828"), small: true));
        _wetSign = Part("WetFloorSign", b =>
        {
            foreach (var side in new[] { -1f, 1f })
            {
                var tilt = new Basis(Vector3.Right, -side * 0.24f);
                b.Box(new Vector3(0, V(9), side * W(2.2f)), new Vector3(W(9), V(18), W(0.6f)), tilt, new Color("#FDD835"));
                b.Box(new Vector3(0, V(11), side * W(2.6f)), new Vector3(W(5), V(5), W(0.2f)), tilt, new Color("#1D2B36"));
            }
        });
        _wetSign.Position = new Vector3(-0.5f, 0, 0.3f);
        _wetSign.Rotation = new Vector3(0, 0.5f, 0);
        return kit;
    }

    /// <summary>A baby in their arms is rocked gently while they stand or sit.</summary>
    private void Rock(bool still, float t)
    {
        if (_baby is null)
        {
            return;
        }

        var sway = still ? 0.2f * Mathf.Sin(t * 1.9f) : 0f;
        _baby.Rotation = new Vector3(0, 0, (Mathf.Pi / 2) + sway);
        _baby.Position = new Vector3(sway * W(4), _hipY + V(12), W(9));
        _armL.Rotation = new Vector3(-0.95f, 0, -0.35f + (sway * 0.5f));
        _armR.Rotation = new Vector3(-0.95f, 0, 0.35f + (sway * 0.5f));
    }

    private static void Hide(Node3D? prop)
    {
        if (prop is not null)
        {
            prop.Visible = false;
        }
    }

    /// <summary>Shows a prop in a hand, building it the first time it is needed.</summary>
    private Node3D Prop(Node3D? existing, Node3D arm, Action<MeshBatch> build)
    {
        if (existing is not null)
        {
            existing.Visible = true;
            return existing;
        }

        var node = new Node3D { Name = build.Method.Name, Position = new Vector3(0, -_armLength, 0) };
        var b = new MeshBatch();
        build(b);
        b.AddTo(node);
        arm.AddChild(node);
        return node;
    }

    /// <summary>The lit keypad on the vending machine in front of them, flashing as they press it.</summary>
    private Node3D Keypad()
    {
        var b = new MeshBatch();
        var at = new Vector3(W(5), V(44), 0.62f);
        b.Box(at, new Vector3(W(6), V(7), 0.02f), new Color("#9EF0FF"));
        b.Box(at + new Vector3(0, V(9), 0), new Vector3(W(9), V(3), 0.02f), new Color("#B9FFCF"));
        var node = new Node3D { Name = "Keypad" };
        var mesh = b.AddTo(node, shadow: false)!;
        mesh.MaterialOverride = Kit3D.VertexColoursUnshaded;
        AddChild(node);
        return node;
    }

    // Props, drawn at the hand. In the hand's frame -y runs on down the arm and +z is the back of the
    // hand (up, when the arm is held out in front).

    private static void Clipboard(MeshBatch b)
    {
        b.Box(new Vector3(0, -W(4), W(1.5f)), new Vector3(W(9), W(12), W(0.8f)), new Color("#8D6E4E"));
        b.Box(new Vector3(0, -W(4), W(2.1f)), new Vector3(W(7.6f), W(10.5f), W(0.3f)), Colors.White);
        b.Box(new Vector3(0, W(1.6f), W(2.2f)), new Vector3(W(3.5f), W(1.2f), W(0.6f)), new Color("#AEB9C2"));
    }

    private static void Phone(MeshBatch b)
    {
        b.Box(new Vector3(-W(1.5f), -W(2.5f), W(1.6f)), new Vector3(W(3.4f), W(6.2f), W(0.6f)), new Color("#22262B"));
        b.Box(new Vector3(-W(1.5f), -W(2.5f), W(1.95f)), new Vector3(W(2.8f), W(5.2f), W(0.1f)), new Color("#7FC4F0"));
    }

    private static void Magazine(MeshBatch b)
    {
        b.Box(new Vector3(W(4.5f), -W(3), W(1.5f)), new Vector3(W(12), W(8), W(0.5f)), new Color("#E8564A"));
        b.Box(new Vector3(W(4.5f), -W(3), W(1.8f)), new Vector3(W(11), W(7), W(0.2f)), new Color("#FBF7EC"));
        b.Box(new Vector3(W(4.5f), -W(3), W(1.95f)), new Vector3(W(0.4f), W(7), W(0.2f)), new Color("#C9C2B0"));
    }

    private static void Cup(MeshBatch b)
    {
        b.Cylinder(new Vector3(0, -W(1.5f), W(2)), W(1.8f), W(4), W(1.8f), new Color("#F4F1EA"), Basis.FromEuler(new Vector3(Mathf.Pi / 2, 0, 0)));
        b.Cylinder(new Vector3(0, -W(1.5f), W(4.1f)), W(1.5f), W(0.3f), W(1.5f), new Color("#6B4226"), Basis.FromEuler(new Vector3(Mathf.Pi / 2, 0, 0)));
    }

    private static void Can(MeshBatch b)
    {
        b.Cylinder(new Vector3(0, -W(1.5f), W(2)), W(1.6f), W(5), W(1.6f), new Color("#D93A30"), Basis.FromEuler(new Vector3(Mathf.Pi / 2, 0, 0)));
        b.Cylinder(new Vector3(0, -W(1.5f), W(4.6f)), W(1.5f), W(0.3f), W(1.5f), new Color("#C9D2D9"), Basis.FromEuler(new Vector3(Mathf.Pi / 2, 0, 0)));
    }

    private static void Handset(MeshBatch b)
    {
        b.Pill(new Vector3(0, -W(1), W(1.5f)), new Vector3(W(2.4f), W(8), W(2.2f)), new Color("#2B2F33"));
    }

    private static void DripBag(MeshBatch b)
    {
        b.Box(new Vector3(0, -W(2), W(2.5f)), new Vector3(W(5), W(7), W(1.8f)), new Color("#E3F2FD"));
        b.Box(new Vector3(0, -W(1.5f), W(3.5f)), new Vector3(W(3), W(2), W(0.2f)), new Color("#1E88E5"));
    }

    private static void Box(MeshBatch b)
    {
        b.Box(new Vector3(0, -W(1.5f), W(2.5f)), new Vector3(W(6), W(3.5f), W(3)), Colors.White);
        b.Box(new Vector3(0, -W(1.5f), W(4.05f)), new Vector3(W(6.1f), W(1.2f), W(0.2f)), new Color("#00897B"));
    }

    private static void PillTray(MeshBatch b)
    {
        b.Box(new Vector3(W(3), -W(1), W(1.6f)), new Vector3(W(8), W(5), W(0.5f)), new Color("#ECEFF1"));
        for (var i = 0; i < 4; i++)
        {
            b.Ball(new Vector3(W(0.5f + (i * 1.6f)), -W(1), W(2.1f)), new Vector3(W(1), W(1), W(0.6f)), new Color("#F06292"), small: true);
        }
    }

    private static void AmbuBag(MeshBatch b)
    {
        b.Ball(new Vector3(0, -W(4), W(1.5f)), new Vector3(W(5), W(8), W(5)), new Color("#2E7D9A"));
    }

    private static void Roll(MeshBatch b)
    {
        b.Cylinder(new Vector3(0, -W(1), W(2)), W(1.8f), W(4.5f), W(1.8f), new Color("#F5F5F0"), Basis.FromEuler(new Vector3(0, 0, Mathf.Pi / 2)));
    }

    private static void Pot(MeshBatch b)
    {
        b.Cylinder(new Vector3(0, -W(1.5f), W(1.6f)), W(1.4f), W(2.4f), W(1.1f), Colors.White, Basis.FromEuler(new Vector3(Mathf.Pi / 2, 0, 0)), cone: true);
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
