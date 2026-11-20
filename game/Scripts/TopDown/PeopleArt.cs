using System;
using BlueprintHospital.Hospital;
using Godot;

namespace BlueprintHospital.TopDown;

/// <summary>How a person is drawn this frame.</summary>
public readonly record struct PersonPose
{
    /// <summary>Sitting: knees and feet stick out in front.</summary>
    public bool Seated { get; init; }

    /// <summary>Walking: feet and arms swing with <see cref="Stride"/>.</summary>
    public bool Walking { get; init; }

    /// <summary>Where they are in their step, in radians.</summary>
    public double Stride { get; init; }

    /// <summary>Both hands forward: pushing a trolley, a wheelchair or a mop bucket.</summary>
    public bool Pushing { get; init; }

    /// <summary>Doing chest compressions: arms straight out in front, pumping with the clock.</summary>
    public double? Cpr { get; init; }

    /// <summary>A ring on the floor under them: the triage colour for patients.</summary>
    public Color? Ring { get; init; }

    /// <summary>The yellow ring of whoever the player has picked.</summary>
    public bool Selected { get; init; }

    /// <summary>The blue ring of whoever is under the mouse in play mode (a click would pick them).</summary>
    public bool Hovered { get; init; }

    /// <summary>A baby carried in their arms.</summary>
    public BabyLook? Baby { get; init; }

    /// <summary>A green pharmacy bag in one hand.</summary>
    public bool Bag { get; init; }

    /// <summary>Greyed out: a patient who has died.</summary>
    public bool Grey { get; init; }

    /// <summary>Sitting in a wheelchair.</summary>
    public bool Wheelchair { get; init; }
}

/// <summary>
/// People seen from directly above: shoulders in their uniform or own clothes, arms and hands, a
/// round head with their hair, and feet that step as they walk. Staff carry what tells them apart
/// from above: a doctor's stethoscope, the piping on a nurse's tunic, a porter's reflective bands,
/// a theatre cap.
/// </summary>
public static class PeopleArt
{
    public static readonly Color SelectedRing = new("#F5C400");

    /// <summary>The ring under whoever the mouse is over in play mode: the interface blue.</summary>
    public static readonly Color HoverRing = new("#1F5FBF");
    private static readonly Color Shoe = new("#2A2F36");

    private static Color Grey(Color c, bool grey) => grey ? c.Lerp(new Color("#9AA3AB"), 0.7f) : c;

    /// <summary>The colour of what someone wears on top, as seen from above.</summary>
    public static Color TopOf(PersonLook look) => look.Jacket ?? look.Top;

    /// <summary>A person standing, walking or sitting at (x, y), facing along (fx, fy).</summary>
    public static void Person(Pen p, double x, double y, double fx, double fy, PersonLook look, PersonPose pose = default)
    {
        var angle = Pen.Facing(fx, fy);
        var k = look.Scale * (0.94 + ((look.Build - 1) * 0.5));
        Rings(p, x, y, pose, 0.62);
        if (pose.Wheelchair)
        {
            p.In(x, y, angle, () => Wheelchair(p));
        }

        p.Shadow(c => p.Ellipse(x, y, 0.46 * k, 0.34 * k, c), 0.1, 0.13, 1.2f);
        var top = Grey(TopOf(look), pose.Grey);
        var legs = Grey(look.Trousers, pose.Grey);
        var skin = Grey(look.Skin, pose.Grey);
        var shoe = Grey(look.ShoeColour ?? Shoe, pose.Grey);
        var swing = pose.Walking ? Math.Sin(pose.Stride) : 0;
        p.Push(x, y, angle, k);

        // Legs and feet.
        if (pose.Seated || pose.Wheelchair)
        {
            p.Solid(-0.33, -0.56, 0.27, 0.56, 0.1, legs, 0.025);
            p.Solid(0.06, -0.56, 0.27, 0.56, 0.1, legs, 0.025);
            p.Ellipse(-0.2, -0.6, 0.11, 0.15, shoe);
            p.Ellipse(0.2, -0.6, 0.11, 0.15, shoe);
        }
        else if (pose.Walking)
        {
            p.Ellipse(-0.16, -0.04 - (swing * 0.26), 0.1, 0.19, shoe);
            p.Ellipse(0.16, -0.04 + (swing * 0.26), 0.1, 0.19, shoe);
        }
        else
        {
            p.Ellipse(-0.15, -0.2, 0.1, 0.16, shoe);
            p.Ellipse(0.15, -0.2, 0.1, 0.16, shoe);
        }

        // Arms and hands.
        var sleeve = look.LongSleeves || look.Jacket is not null || look.Kind is TopKind.Hoodie or TopKind.Jumper or TopKind.Cardigan or TopKind.Shirt ? top : skin;
        if (pose.Cpr is { } beat)
        {
            var reach = 0.5 + (0.07 * Math.Sin(beat * 11));
            p.Solid(-0.24, -reach, 0.17, reach, 0.08, top, 0.025);
            p.Solid(0.07, -reach, 0.17, reach, 0.08, top, 0.025);
            p.Dot(0, -reach - 0.04, 0.12, skin, 0.02);
        }
        else if (pose.Pushing)
        {
            p.Solid(-0.44, -0.5, 0.17, 0.5, 0.08, top, 0.025);
            p.Solid(0.27, -0.5, 0.17, 0.5, 0.08, top, 0.025);
            p.Dot(-0.35, -0.54, 0.085, skin, 0.02);
            p.Dot(0.35, -0.54, 0.085, skin, 0.02);
        }
        else if (pose.Baby is not null)
        {
            p.Ellipse(-0.34, -0.2, 0.1, 0.24, top, Pen.Outline(top), 0.025, 0.7);
            p.Ellipse(0.34, -0.2, 0.1, 0.24, top, Pen.Outline(top), 0.025, -0.7);
        }
        else
        {
            var a = swing * 0.16;
            p.Ellipse(-0.42, -0.04 + a, 0.1, 0.22, top, Pen.Outline(top), 0.025, 0.25);
            p.Ellipse(0.42, -0.04 - a, 0.1, 0.22, top, Pen.Outline(top), 0.025, -0.25);
            if (sleeve != top)
            {
                p.Ellipse(-0.445, -0.15 + a, 0.075, 0.1, skin);
                p.Ellipse(0.445, -0.15 - a, 0.075, 0.1, skin);
            }

            p.Dot(-0.45, -0.22 + a, 0.085, skin, 0.02);
            p.Dot(0.45, -0.22 - a, 0.085, skin, 0.02);
            if (pose.Bag)
            {
                p.Solid(0.36, -0.5, 0.22, 0.26, 0.04, new Color("#2E9E57"), 0.02);
                p.Rect(0.44, -0.42, 0.06, 0.1, Colors.White);
            }
        }

        // A bump, for mums-to-be.
        if (look.Bump)
        {
            p.Ellipse(0, -0.16, 0.26, 0.2, top, Pen.Outline(top), 0.03);
        }

        // Shoulders.
        p.Ellipse(0, 0.02, 0.43, 0.25, top, Pen.Outline(top), 0.035);
        if (look.Apron is { } apron)
        {
            p.Ellipse(0, -0.02, 0.26, 0.2, Grey(apron, pose.Grey));
        }

        if (look.Trim is { } trim)
        {
            p.Line(-0.4, 0.02, -0.25, 0.02, Grey(trim, pose.Grey), 0.07);
            p.Line(0.25, 0.02, 0.4, 0.02, Grey(trim, pose.Grey), 0.07);
        }

        if (look.Epaulettes is { } tabs)
        {
            p.Round(-0.38, -0.07, 0.14, 0.18, 0.03, Grey(tabs, pose.Grey));
            p.Round(0.24, -0.07, 0.14, 0.18, 0.03, Grey(tabs, pose.Grey));
        }

        if (look.HiVis)
        {
            var band = new Color("#D8DDE2");
            p.Rect(-0.3, -0.16, 0.07, 0.38, band);
            p.Rect(0.23, -0.16, 0.07, 0.38, band);
        }

        if (look.Kind == TopKind.Stripes && look.Trim is null)
        {
            p.Line(-0.3, -0.08, 0.3, -0.08, Pen.Light(top, 0.4f), 0.04);
            p.Line(-0.34, 0.08, 0.34, 0.08, Pen.Light(top, 0.4f), 0.04);
        }

        if (look.Tie is { } tie)
        {
            p.Poly(Grey(tie, pose.Grey), null, 0, (-0.05, -0.2), (0.05, -0.2), (0.03, -0.02), (-0.03, -0.02));
        }

        if (look.Stethoscope)
        {
            p.Arc(0, 0, 0.23, 0.2, Math.PI - 0.2, new Color("#2B333C"), 0.035);
            p.Circle(-0.2, -0.14, 0.05, new Color("#AEB8C2"), new Color("#2B333C"), 0.015);
        }
        else if (look.Lanyard)
        {
            var cord = look.Jacket is not null ? new Color("#F3F0FA") : new Color("#3C6FA0");
            p.Line(-0.1, -0.18, 0, -0.05, cord, 0.03);
            p.Line(0.1, -0.18, 0, -0.05, cord, 0.03);
            p.Rect(-0.05, -0.25, 0.1, 0.07, Colors.White);
        }

        if (pose.Baby is { } baby)
        {
            Baby(p, 0.02, -0.34, Math.PI / 2, baby, 0.8);
            p.Dot(-0.2, -0.42, 0.08, skin, 0.02);
            p.Dot(0.24, -0.3, 0.08, skin, 0.02);
        }

        p.Pop();

        // A walking stick or frame goes ahead of them.
        if (look.Frame && !pose.Seated && !pose.Wheelchair)
        {
            p.In(x, y, angle, () =>
            {
                var steel = new Color("#9AA7B1");
                p.Path(steel, 0.05, (-0.34, -0.36), (-0.34, -0.62), (0.34, -0.62), (0.34, -0.36));
                p.Circle(-0.34, -0.62, 0.04, new Color("#4A545D"));
                p.Circle(0.34, -0.62, 0.04, new Color("#4A545D"));
            });
        }
        else if (look.Stick && !pose.Seated && !pose.Wheelchair)
        {
            p.In(x, y, angle, () => p.Circle(0.5, -0.36, 0.05, new Color("#6B4A2E"), new Color("#3A2A1E"), 0.02));
        }

        Head(p, x + (Math.Sin(angle) * 0.03 * k), y - (Math.Cos(angle) * 0.03 * k), 0.2 * k * look.HeadScale, look, angle, pose.Grey);
    }

    /// <summary>The rings on the floor under someone: the yellow selection ring and the triage (or clinic) ring.</summary>
    public static void Rings(Pen p, double x, double y, PersonPose pose, double radius)
    {
        if (pose.Selected)
        {
            p.Circle(x, y, radius + 0.16, Pen.A("#FFEB3B", 0.3f), SelectedRing, 0.09);
        }
        else if (pose.Hovered)
        {
            p.Circle(x, y, radius + 0.16, Pen.A("#FFFFFF", 0.45f), HoverRing, 0.08);
        }

        if (pose.Ring is { } ring)
        {
            p.Circle(x, y, radius, new Color(ring, 0.16f * ring.A), ring, 0.08);
        }
    }

    /// <summary>A head from above, its face pointing along <paramref name="angle"/>: hair in its style, ears and the tip of the nose.</summary>
    public static void Head(Pen p, double x, double y, double r, PersonLook look, double angle, bool grey = false)
    {
        var skin = Grey(look.Skin, grey);
        var hair = Grey(look.Hair, grey);
        var style = look.Style;
        p.Push(x, y, angle);

        // Hair that hangs behind the head.
        switch (style)
        {
            case HairStyle.Ponytail:
                p.Ellipse(0, r * 1.2, r * 0.3, r * 0.5, hair, Pen.Outline(hair), 0.02);
                break;
            case HairStyle.Bun:
                p.Circle(0, r * 1.0, r * 0.42, hair, Pen.Outline(hair), 0.02);
                break;
            case HairStyle.Braids or HairStyle.Pigtails:
                p.Ellipse(-r * 0.6, r * 1.05, r * 0.22, r * 0.45, hair);
                p.Ellipse(r * 0.6, r * 1.05, r * 0.22, r * 0.45, hair);
                break;
            case HairStyle.Long or HairStyle.Wavy or HairStyle.Bob:
                p.Ellipse(0, r * 0.35, r * 1.12, r * 1.0, hair, Pen.Outline(hair), 0.02);
                break;
            case HairStyle.Afro or HairStyle.Perm or HairStyle.Curly:
                p.Circle(0, r * 0.12, r * (style == HairStyle.Afro ? 1.3 : 1.12), hair, Pen.Outline(hair), 0.02);
                break;
        }

        // Ears, then the head.
        p.Circle(-r * 0.98, 0, r * 0.2, skin);
        p.Circle(r * 0.98, 0, r * 0.2, skin);
        p.Circle(0, 0, r, skin, Pen.Outline(skin), 0.03);

        // Hair on top: everything but the face at the front.
        if (look.Cap is { } cap)
        {
            p.Circle(0, r * 0.1, r * 0.98, Grey(cap, grey));
        }
        else
        {
            switch (style)
            {
                case HairStyle.Bald:
                    break;
                case HairStyle.Balding or HairStyle.Receding:
                    p.Arc(0, r * 0.12, r * 0.74, 0.25, Math.PI - 0.25, hair, r * 0.42);
                    break;
                case HairStyle.Turban:
                    p.Circle(0, r * 0.1, r * 1.0, hair);
                    p.Line(-r * 0.7, -r * 0.3, r * 0.5, r * 0.5, Pen.Dark(hair, 0.25f), 0.03);
                    p.Line(-r * 0.3, -r * 0.6, r * 0.8, r * 0.1, Pen.Dark(hair, 0.25f), 0.03);
                    break;
                case HairStyle.Hijab:
                    p.Circle(0, r * 0.1, r * 1.02, hair);
                    break;
                default:
                    p.Ellipse(0, r * 0.2, r * 0.97, r * 0.82, hair);
                    if (style is HairStyle.Quiff or HairStyle.Curly or HairStyle.Perm or HairStyle.Afro)
                    {
                        p.Circle(-r * 0.3, -r * 0.42, r * 0.3, hair);
                        p.Circle(r * 0.25, -r * 0.46, r * 0.28, hair);
                    }

                    if (style == HairStyle.SidePart)
                    {
                        p.Line(-r * 0.3, -r * 0.5, -r * 0.15, r * 0.7, new Color(0, 0, 0, 0.18f), 0.02);
                    }

                    break;
            }
        }

        if (look.Bobble is { } bobble && style is HairStyle.Ponytail or HairStyle.Bun)
        {
            p.Circle(0, r * 0.82, r * 0.14, Grey(bobble, grey));
        }

        // The tip of the nose shows which way they look.
        p.Circle(0, -r * 0.98, r * 0.13, Pen.Dark(skin, 0.08f));
        p.Pop();
    }

    /// <summary>A baby wrapped in a blanket, head towards -y before turning by <paramref name="angle"/>.</summary>
    public static void Baby(Pen p, double x, double y, double angle, BabyLook look, double scale = 1)
    {
        p.Push(x, y, angle, scale);
        p.Round(-0.17, -0.1, 0.34, 0.5, 0.15, look.Wrap, Pen.Outline(look.Trim), 0.025);
        p.Line(-0.15, 0.02, 0.15, 0.1, look.Trim, 0.035);
        p.Circle(0, -0.2, 0.15, look.Skin, Pen.Outline(look.Skin), 0.02);
        p.Arc(0, -0.2, 0.11, Math.PI + 0.2, Mathf.Tau - 0.2, look.Hat, 0.09);
        p.Pop();
    }

    // ------------------------------------------------------------------ lying down

    /// <summary>
    /// A patient lying on their back under a blanket, in a frame whose origin is the middle of the
    /// mattress with the head towards -y. Shoulders show above the blanket and arms lie on top of it.
    /// </summary>
    public static void LyingFigure(Pen p, PersonLook look, Color blanket, Color? band, BabyLook[]? babies, double width, double length, bool grey = false, bool covered = true)
    {
        var top = Grey(TopOf(look), grey);
        var skin = Grey(look.Skin, grey);
        var k = Math.Min(1.0, look.Scale + 0.08);
        var headY = (-length / 2) + 0.56;
        var half = width / 2;
        p.Ellipse(0, headY + (0.62 * k), 0.58 * k, 0.3 * k, top, Pen.Outline(top), 0.03);
        if (covered)
        {
            var from = headY + (0.7 * k);
            var to = Math.Min(length / 2 - 0.12, headY + (3.3 * k));
            p.Solid(-half, from, width, to - from, 0.14, blanket, 0.03);
            p.Ellipse(0, (from + to) / 2 + 0.1, 0.5 * k, (to - from) * 0.42, new Color(1, 1, 1, 0.16f));
            p.Round(-half, from, width, 0.3, 0.1, Pen.Light(blanket, 0.45f));
        }
        else
        {
            // On the floor: legs out straight.
            var legs = Grey(look.Trousers, grey);
            p.Solid(-0.3 * k, headY + (0.8 * k), 0.26 * k, 1.9 * k, 0.1, legs, 0.025);
            p.Solid(0.04 * k, headY + (0.8 * k), 0.26 * k, 1.9 * k, 0.1, legs, 0.025);
        }

        Head(p, 0, headY, 0.27 * k * look.HeadScale, look, 0, grey);

        // Arms on top of the blanket, the wristband on the left one.
        var armY = headY + (1.25 * k);
        p.Ellipse(-0.6 * k, armY, 0.11, 0.36 * k, top, Pen.Outline(top), 0.02, 0.12);
        p.Ellipse(0.6 * k, armY, 0.11, 0.36 * k, top, Pen.Outline(top), 0.02, -0.12);
        p.Circle(-0.64 * k, armY + (0.37 * k), 0.1, skin);
        p.Circle(0.64 * k, armY + (0.37 * k), 0.1, skin);
        if (band is { } wrist)
        {
            p.Line(-0.72 * k, armY + (0.24 * k), -0.54 * k, armY + (0.27 * k), wrist, 0.07);
        }

        if (babies is { Length: > 0 })
        {
            for (var i = 0; i < babies.Length && i < 2; i++)
            {
                Baby(p, babies.Length == 1 ? 0 : -0.2 + (i * 0.4), armY + 0.1, 0, babies[i], 0.85);
            }
        }
    }

    /// <summary>A hospital trolley (or the ambulance crew's stretcher) with a patient on it, at (x, y) with the head along (fx, fy).</summary>
    public static void Trolley(Pen p, double x, double y, double fx, double fy, PersonLook look, Color blanket, Color? band, BabyLook[]? babies, bool stretcher, PersonPose pose = default)
    {
        Rings(p, x, y, pose, 1.5);
        p.Push(x, y, Pen.Facing(fx, fy));
        p.Shadow(c => p.Round(-0.62, -1.9, 1.24, 3.8, 0.2, c), 0.12, 0.16);
        foreach (var (wx, wy) in new[] { (-0.5, -1.6), (0.5, -1.6), (-0.5, 1.6), (0.5, 1.6) })
        {
            p.Circle(wx, wy, 0.1, new Color("#2B333C"));
        }

        var frame = stretcher ? new Color("#E3C534") : new Color("#AEB8C2");
        p.Solid(-0.62, -1.9, 1.24, 3.8, 0.2, frame);
        p.Round(-0.52, -1.78, 1.04, 3.56, 0.16, new Color("#2F3A46"));
        p.Round(-0.48, -1.74, 0.96, 3.48, 0.14, new Color("#F7F9FB"));
        p.Round(-0.36, -1.62, 0.72, 0.7, 0.18, Colors.White, new Color("#AEBAC5"), 0.03);
        LyingFigure(p, look, blanket, band, babies, 0.96, 3.48, pose.Grey);
        if (stretcher)
        {
            // Straps across the chest and the knees.
            p.Rect(-0.5, -0.2, 1.0, 0.1, new Color("#2B333C"));
            p.Rect(-0.5, 0.9, 1.0, 0.1, new Color("#2B333C"));
        }

        p.Line(-0.62, -0.5, -0.62, 1.2, new Color("#6F7D8A"), 0.07);
        p.Line(0.62, -0.5, 0.62, 1.2, new Color("#6F7D8A"), 0.07);
        p.Pop();
    }

    /// <summary>A patient lying on the floor (after a fall, or in arrest away from a bed).</summary>
    public static void OnFloor(Pen p, double x, double y, double fx, double fy, PersonLook look, PersonPose pose = default)
    {
        Rings(p, x, y, pose, 1.3);
        p.Push(x, y, Pen.Facing(fx, fy));
        p.Shadow(c => p.Ellipse(0, 0.1, 0.6, 1.5, c), 0.08, 0.1);
        LyingFigure(p, look, look.Top, null, null, 0.9, 3.3, pose.Grey, covered: false);
        p.Pop();
    }

    /// <summary>A wheelchair in the sitter's frame (they face -y): big wheels at the sides, the push handles behind.</summary>
    public static void Wheelchair(Pen p)
    {
        var steel = new Color("#6F7D8A");
        p.Shadow(c => p.Round(-0.56, -0.5, 1.12, 1.1, 0.15, c), 0.08, 0.1);
        p.Round(-0.62, -0.28, 0.12, 0.84, 0.05, new Color("#22282F"));
        p.Round(0.5, -0.28, 0.12, 0.84, 0.05, new Color("#22282F"));
        p.Solid(-0.42, -0.4, 0.84, 0.86, 0.1, new Color("#3A4652"), 0.03);
        p.Line(-0.42, 0.46, -0.42, 0.7, steel, 0.07);
        p.Line(0.42, 0.46, 0.42, 0.7, steel, 0.07);
        p.Round(-0.36, -0.8, 0.26, 0.2, 0.05, steel);
        p.Round(0.1, -0.8, 0.26, 0.2, 0.05, steel);
    }

    /// <summary>A cleaner's mop bucket on wheels, pushed ahead of them.</summary>
    public static void MopBucket(Pen p, double x, double y, double angle)
    {
        p.Push(x, y, angle);
        p.Shadow(c => p.Round(-0.3, -1.05, 0.6, 0.5, 0.1, c), 0.06, 0.08);
        p.Solid(-0.3, -1.05, 0.6, 0.5, 0.1, new Color("#FBC02D"), 0.03);
        p.Round(-0.22, -0.98, 0.44, 0.3, 0.06, new Color("#7FB7CF"));
        p.Line(0.12, -0.9, 0.12, -0.5, new Color("#8E99A5"), 0.05);
        p.Pop();
    }
}
