using System;
using System.Linq;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>How someone wears their hair.</summary>
public enum HairStyle
{
    Short,
    Crop,
    Curly,
    Long,
    Ponytail,
    Bun,
    Bald,
}

/// <summary>What a person is wearing and looks like.</summary>
public sealed record PersonLook(
    Color Top,
    Color Trousers,
    Color Skin,
    Color Hair,
    Color? Piping = null,
    bool Tunic = false,
    bool Stethoscope = false,
    bool Lanyard = false,
    bool LongSleeves = false,
    bool LongHair = false,
    HairStyle? Style = null,
    Color? Cap = null,
    bool Mask = false,
    bool Collar = false,
    bool Pocket = false,
    bool FobWatch = false,
    bool Beard = false,
    bool Glasses = false,
    bool Skirt = false,
    Color? Jacket = null,
    Color? ShoeColour = null,
    bool Stick = false,
    bool Bump = false)
{
    /// <summary>The hair style, falling back to long or short hair.</summary>
    public HairStyle Hairdo => Style ?? (LongHair ? HairStyle.Long : HairStyle.Short);
}

/// <summary>A newborn's blanket, its edging, hat and skin.</summary>
public sealed record BabyLook(Color Wrap, Color Trim, Color Hat, Color Skin);

/// <summary>
/// Flat-shaded people and clinical equipment in the game's isometric style. People are drawn
/// front-on at a fixed size (about 1.8 m tall); equipment is drawn with <see cref="IsoDraw"/>.
/// All positions are in world (screen) pixels.
/// </summary>
public static class Figures
{
    public static readonly Color Shoes = new("#2B2F33");
    public static readonly Color Outline = new("#1D2B36");

    /// <summary>
    /// A standing (or walking) person whose feet are at <paramref name="foot"/>. With
    /// <paramref name="away"/> they are seen from behind, walking away from the viewer.
    /// </summary>
    public static void Standing(CanvasItem c, Vector2 foot, PersonLook look, double time, bool walking, int seed, float alpha = 1f, bool away = false, BabyLook? holding = null)
    {
        c.DrawColoredPolygon(IsoDraw.FloorEllipse(foot, 10), new Color(0, 0, 0, 0.2f * alpha));
        var phase = (time * 9) + seed;
        var swing = walking ? (float)Math.Sin(phase) * 3.2f : 0f;
        var bob = walking ? -(float)Math.Abs(Math.Cos(phase)) * 1.1f : 0f;

        // Legs (or a skirt and legs) and shoes.
        var trousers = A(look.Trousers, alpha);
        var shoes = A(look.ShoeColour ?? Shoes, alpha);
        var legs = look.Skirt ? A(look.Skin.Darkened(0.15f), alpha) : trousers;
        foreach (var side in new[] { -1f, 1f })
        {
            var s2 = side * swing * (away ? -1 : 1);
            var shade = side > 0 ? legs.Darkened(0.1f) : legs;
            c.DrawLine(foot + new Vector2(side * 3.5f, -21 + bob), foot + new Vector2((side * 3.5f) + (side < 0 ? s2 : -s2), -3), shade, look.Skirt ? 3.5f : 5.5f);
            var toe = foot + new Vector2((side * 3.5f) + (side < 0 ? s2 : -s2), -2);
            c.DrawColoredPolygon(Ellipse(toe + new Vector2(0, away ? -0.5f : 0.4f), 3.3f, 2.3f, 10), shoes);
        }

        if (look.Skirt)
        {
            c.DrawColoredPolygon(new[] { foot + new Vector2(-8, -20 + bob), foot + new Vector2(8, -20 + bob), foot + new Vector2(9.5f, -10 + bob), foot + new Vector2(-9.5f, -10 + bob) }, trousers);
            c.DrawLine(foot + new Vector2(2, -20 + bob), foot + new Vector2(3, -10 + bob), A(look.Trousers.Darkened(0.15f), alpha), 1f);
        }

        Body(c, foot, look, -swing, alpha, drop: bob, away, holding);

        if (look.Stick && !away && holding is null)
        {
            // A walking stick in the right hand, planted a little ahead.
            var hand = foot + new Vector2(10.5f - (swing * 0.6f), -22 + bob);
            c.DrawLine(hand + new Vector2(-1.5f, -1), hand + new Vector2(1.5f, -1), A(new Color("#5A3B22"), alpha), 2f);
            c.DrawLine(hand, foot + new Vector2(12.5f, 1), A(new Color("#6B4A2E"), alpha), 1.6f);
        }
    }

    /// <summary>A person sitting on a chair whose seat centre is at <paramref name="foot"/>.</summary>
    /// <remarks>
    /// <paramref name="away"/> draws them from behind (their legs are hidden by the chair and their body);
    /// <paramref name="lean"/> turns their knees a little to the left (-1) or right (+1) of the screen.
    /// </remarks>
    public static void Seated(CanvasItem c, Vector2 foot, PersonLook look, float alpha = 1f, BabyLook? holding = null, bool away = false, float lean = 0)
    {
        if (away)
        {
            Body(c, foot, look, 0, alpha, drop: 7, away: true, holding);
            return;
        }

        var k = lean * 2.5f;
        var trousers = A(look.Trousers, alpha);
        var shins = look.Skirt ? A(look.Skin.Darkened(0.15f), alpha) : trousers;

        // Thighs forward (towards the viewer), shins down to the floor.
        c.DrawLine(foot + new Vector2(-3.5f, -15), foot + new Vector2(-4.5f + k, -10), trousers, 5.5f);
        c.DrawLine(foot + new Vector2(3.5f, -15), foot + new Vector2(4.5f + k, -10), trousers.Darkened(0.08f), 5.5f);
        c.DrawLine(foot + new Vector2(-4.5f + k, -10), foot + new Vector2(-4.5f + k, 2), shins, look.Skirt ? 3.5f : 5f);
        c.DrawLine(foot + new Vector2(4.5f + k, -10), foot + new Vector2(4.5f + k, 2), shins.Darkened(0.08f), look.Skirt ? 3.5f : 5f);
        if (look.Skirt)
        {
            c.DrawColoredPolygon(new[] { foot + new Vector2(-8.5f, -16), foot + new Vector2(8.5f, -16), foot + new Vector2(9, -8), foot + new Vector2(-9, -8) }, trousers);
        }

        var shoes = A(look.ShoeColour ?? Shoes, alpha);
        c.DrawColoredPolygon(Ellipse(foot + new Vector2(-4.5f + k, 3.4f), 3.3f, 2.3f, 10), shoes);
        c.DrawColoredPolygon(Ellipse(foot + new Vector2(4.5f + k, 3.4f), 3.3f, 2.3f, 10), shoes);

        Body(c, foot, look, 0, alpha, drop: 7, away: false, holding);
    }

    private static void Body(CanvasItem c, Vector2 foot, PersonLook look, float armSwing, float alpha, float drop, bool away, BabyLook? holding = null)
    {
        Vector2 P(float x, float y) => foot + new Vector2(x, y + drop);
        var outer = look.Jacket ?? look.Top;
        var top = A(outer, alpha);
        var skin = A(look.Skin, alpha);

        if (holding is { } heldBehind && away)
        {
            // Seen from behind, only the end of the baby's blanket shows past the shoulder.
            Baby(c, P(6, -33), heldBehind, alpha, 0.8f);
        }

        // Torso; a tunic or a coat comes down over the hips.
        var hem = look.Tunic || look.Jacket is not null ? -16f : -19f;
        var torso = new[] { P(-9, -37), P(-6.5f, -41), P(6.5f, -41), P(9, -37), P(8.5f, hem), P(-8.5f, hem) };
        c.DrawColoredPolygon(torso, top);
        c.DrawColoredPolygon(new[] { P(1, -41), P(6.5f, -41), P(9, -37), P(8.5f, hem), P(3, hem) }, A(outer.Darkened(0.12f), alpha));
        c.DrawPolyline(torso.Append(torso[0]).ToArray(), A(outer.Darkened(0.35f), alpha), 1f, true);

        if (look.Bump)
        {
            // Heavily pregnant: a round bump under the top, catching the light on the left.
            // Only the lower half rounds out past the torso, so it reads as a belly, not a disc.
            var bump = new Vector2[13];
            for (var i = 0; i < bump.Length; i++)
            {
                var a = -0.35f + (i * (Mathf.Pi + 0.7f) / (bump.Length - 1));
                bump[i] = P(0.6f + (Mathf.Cos(a) * 9.6f), -27 + (Mathf.Sin(a) * 7.5f));
            }

            c.DrawColoredPolygon(bump, A(look.Top, alpha));
            if (!away)
            {
                c.DrawArc(P(3.2f, -26.5f), 5.5f, -0.2f, Mathf.Pi * 0.55f, 8, A(look.Top.Darkened(0.14f), alpha), 2.4f, true);
                c.DrawArc(P(-1.5f, -27.5f), 5f, Mathf.Pi * 0.75f, Mathf.Pi * 1.05f, 6, A(look.Top.Lightened(0.22f), alpha), 1.3f, true);
            }

            c.DrawPolyline(bump, A(look.Top.Darkened(0.35f), alpha), 1f, true);
        }

        if (look.Jacket is not null && !away)
        {
            // Open coat: the top underneath shows down the middle, with the lapels either side.
            c.DrawColoredPolygon(new[] { P(-3.5f, -41), P(3.5f, -41), P(2.5f, hem), P(-2.5f, hem) }, A(look.Top, alpha));
            c.DrawLine(P(-3.5f, -41), P(-2.5f, hem), A(outer.Darkened(0.3f), alpha), 1f);
            c.DrawLine(P(3.5f, -41), P(2.5f, hem), A(outer.Darkened(0.3f), alpha), 1f);
            c.DrawColoredPolygon(new[] { P(-3.5f, -41), P(-6, -41), P(-3, -33) }, A(outer.Lightened(0.12f), alpha));
            c.DrawColoredPolygon(new[] { P(3.5f, -41), P(6, -41), P(3, -33) }, A(outer.Darkened(0.05f), alpha));
        }
        else if (!away && look.Collar)
        {
            // Polo or shirt collar, with a short button placket.
            c.DrawColoredPolygon(new[] { P(-1.2f, -41), P(1.2f, -41), P(0, -38) }, skin);
            c.DrawColoredPolygon(new[] { P(-5, -42), P(-0.8f, -41), P(-2, -37.5f) }, A(look.Top.Lightened(0.15f), alpha));
            c.DrawColoredPolygon(new[] { P(5, -42), P(0.8f, -41), P(2, -37.5f) }, A(look.Top.Lightened(0.05f), alpha));
            c.DrawLine(P(0, -38), P(0, -33), A(look.Top.Darkened(0.25f), alpha), 1f);
        }
        else if (!away)
        {
            // Scrubs and tunics have a V-neck.
            c.DrawColoredPolygon(new[] { P(-3, -41), P(3, -41), P(0, -36) }, skin);
        }
        else
        {
            c.DrawLine(P(-3.5f, -41), P(3.5f, -41), A(outer.Darkened(0.3f), alpha), 1.2f);
        }

        if (look.Piping is { } piping)
        {
            var trim = A(piping, alpha);
            if (!away)
            {
                c.DrawPolyline(new[] { P(-3.5f, -41), P(0, -35.5f), P(3.5f, -41) }, trim, 1.4f);
            }

            c.DrawLine(P(-8.5f, hem), P(8.5f, hem), trim, 1.4f);
            c.DrawLine(P(-8.6f, -30), P(-8.4f, hem), trim, 1f);
            c.DrawLine(P(8.6f, -30), P(8.4f, hem), trim, 1f);
        }

        if (!away)
        {
            if (look.Pocket)
            {
                // Breast pocket with two pens.
                c.DrawRect(new Rect2(P(-7, -35), new Vector2(4.5f, 4)), A(look.Top.Darkened(0.1f), alpha));
                c.DrawLine(P(-6, -35), P(-6, -37), A(new Color("#1E4F9E"), alpha), 1f);
                c.DrawLine(P(-4.5f, -35), P(-4.5f, -36.5f), A(new Color("#202020"), alpha), 1f);
            }

            if (look.FobWatch)
            {
                c.DrawLine(P(5, -38), P(5, -35.5f), A(new Color("#B9C3CB"), alpha), 0.8f);
                c.DrawCircle(P(5, -34), 1.7f, A(new Color("#E9EEF2"), alpha));
                c.DrawArc(P(5, -34), 1.7f, 0, Mathf.Tau, 10, A(new Color("#7D8A95"), alpha), 0.6f, true);
            }
        }

        // Arms: short sleeves and bare forearms (bare below the elbows), unless in long sleeves or a coat.
        var longSleeves = look.LongSleeves || look.Jacket is not null;
        var cradling = holding is not null && !away;
        if (holding is { } held && !away)
        {
            // Holding a baby in front of the chest, its head in the crook of the left arm.
            Baby(c, P(0.5f, -31), held, alpha);
        }

        foreach (var side in new[] { -1f, 1f })
        {
            var shoulder = P(side * 9, -38);
            var elbow = cradling ? P(side * 10.5f, -29) : P(side * 10.5f, -30 + (armSwing * side * 0.3f));
            var hand = cradling ? P(side * -3.5f, -27.5f) : P(side * 10.5f + (armSwing * side * 0.6f), -22);
            var sleeve = side > 0 ? A(outer.Darkened(0.1f), alpha) : top;
            c.DrawLine(shoulder, elbow, sleeve, 5f);
            if (!longSleeves)
            {
                c.DrawLine(elbow + new Vector2(-2.4f, 0), elbow + new Vector2(2.4f, 0), A(outer.Darkened(0.25f), alpha), 0.8f);
            }

            c.DrawLine(elbow, hand, longSleeves ? sleeve : skin, 4f);
            c.DrawCircle(hand, 2.4f, skin);
        }

        // Neck and head.
        c.DrawRect(new Rect2(P(-2.5f, -45), new Vector2(5, 5)), skin);
        var head = P(0, -50.5f);
        if (away)
        {
            HeadFromBehind(c, head, look, alpha);
        }
        else
        {
            HeadFront(c, head, look, alpha, eyesOpen: true);
        }

        if (!away && look.Mask)
        {
            // Surgical mask pulled down under the chin.
            c.DrawColoredPolygon(new[] { P(-4.5f, -44.5f), P(4.5f, -44.5f), P(3.5f, -40.5f), P(-3.5f, -40.5f) }, A(new Color("#8FC3E6"), alpha));
            c.DrawLine(P(-4.5f, -44), P(-7, -47), A(Colors.White, alpha), 0.6f);
            c.DrawLine(P(4.5f, -44), P(7, -47), A(Colors.White, alpha), 0.6f);
        }

        if (look.Stethoscope)
        {
            var steth = A(new Color("#2E3A44"), alpha);
            if (away)
            {
                c.DrawPolyline(new[] { P(-5, -40), P(-2, -42), P(2, -42), P(5, -40) }, steth, 1.5f);
            }
            else
            {
                c.DrawPolyline(new[] { P(-4.5f, -41), P(-3.5f, -35), P(0, -32), P(3.5f, -35), P(4.5f, -41) }, steth, 1.5f);
                c.DrawCircle(P(0, -31), 1.9f, A(new Color("#B9C3CB"), alpha));
            }
        }

        if (look.Lanyard)
        {
            if (away)
            {
                c.DrawLine(P(-2.5f, -41.5f), P(2.5f, -41.5f), A(new Color("#005EB8"), alpha), 1f);
            }
            else
            {
                c.DrawPolyline(new[] { P(-2, -41), P(1.5f, cradling ? -36 : -30), P(2.5f, -41) }, A(new Color("#005EB8"), alpha), 1f);
                if (!cradling)
                {
                    c.DrawRect(new Rect2(P(0, -30), new Vector2(4, 5)), A(Colors.White, alpha));
                    c.DrawRect(new Rect2(P(0.6f, -29.3f), new Vector2(1.4f, 1.6f)), A(new Color("#9AB4CC"), alpha));
                }
            }
        }
    }

    /// <summary>The face and hair from the front: ears, eyes, brows, a hint of a smile, and any beard, glasses or theatre cap.</summary>
    private static void HeadFront(CanvasItem c, Vector2 head, PersonLook look, float alpha, bool eyesOpen)
    {
        var skin = A(look.Skin, alpha);
        var hair = A(look.Hair, alpha);
        var style = look.Cap is null ? look.Hairdo : HairStyle.Crop;
        if (look.Cap is null && style is HairStyle.Long or HairStyle.Ponytail)
        {
            // Hair falling behind the shoulders.
            var length = style == HairStyle.Long ? 10f : 4f;
            c.DrawColoredPolygon(new[] { head + new Vector2(-8, -1), head + new Vector2(8, -1), head + new Vector2(7.5f, length), head + new Vector2(-7.5f, length) }, hair);
        }

        // Ears, then the face.
        c.DrawCircle(head + new Vector2(-7.3f, 1), 1.8f, skin);
        c.DrawCircle(head + new Vector2(7.3f, 1), 1.8f, A(look.Skin.Darkened(0.08f), alpha));
        c.DrawCircle(head, 7.5f, skin);
        c.DrawCircle(head + new Vector2(2.5f, 1), 5.5f, A(new Color(look.Skin.Darkened(0.06f), 0.5f), alpha));
        c.DrawArc(head, 7.5f, 0, Mathf.Tau, 24, A(look.Skin.Darkened(0.3f), alpha), 1f, true);

        if (look.Beard)
        {
            var beard = new Vector2[12];
            for (var i = 0; i < 10; i++)
            {
                var a = Mathf.Pi * i / 9f;
                beard[i] = head + new Vector2(Mathf.Cos(a) * 7.6f, Mathf.Sin(a) * 7.8f + 0.5f);
            }

            beard[10] = head + new Vector2(-6, 2);
            beard[11] = head + new Vector2(6, 2);
            c.DrawColoredPolygon(new[] { beard[11] }.Concat(beard[..10]).Append(beard[10]).ToArray(), hair);
            c.DrawLine(head + new Vector2(-1.6f, 4.4f), head + new Vector2(1.6f, 4.4f), A(look.Skin.Darkened(0.2f), alpha), 1.1f, true);
        }

        if (look.Cap is { } cap)
        {
            // Theatre cap tied at the back, pulled down over the hair.
            var dome = new Vector2[13];
            for (var i = 0; i <= 10; i++)
            {
                var a = Mathf.Pi + (i * Mathf.Pi / 10);
                dome[i] = head + new Vector2(Mathf.Cos(a) * 8.3f, Mathf.Sin(a) * 8.6f - 0.3f);
            }

            dome[11] = head + new Vector2(8, -1.2f);
            dome[12] = head + new Vector2(-8, -1.2f);
            c.DrawColoredPolygon(dome, A(cap, alpha));
            c.DrawLine(head + new Vector2(-8, -1.6f), head + new Vector2(8, -1.6f), A(cap.Darkened(0.25f), alpha), 1.4f);
            c.DrawCircle(head + new Vector2(-3.5f, -5), 0.9f, A(cap.Lightened(0.35f), alpha));
            c.DrawCircle(head + new Vector2(2.5f, -6.2f), 0.9f, A(cap.Lightened(0.35f), alpha));
            c.DrawCircle(head + new Vector2(4.5f, -3.2f), 0.9f, A(cap.Lightened(0.35f), alpha));
        }
        else
        {
            HairFront(c, head, style, hair);
        }

        // Eyes, brows and a hint of a smile.
        var eye = A(new Color("#2A211C"), alpha);
        if (eyesOpen)
        {
            c.DrawCircle(head + new Vector2(-2.6f, 1.2f), 0.95f, eye);
            c.DrawCircle(head + new Vector2(2.6f, 1.2f), 0.95f, eye);
            var brow = A(look.Hair.Darkened(0.2f), alpha);
            c.DrawLine(head + new Vector2(-3.8f, -1.2f), head + new Vector2(-1.6f, -1.5f), brow, 0.8f, true);
            c.DrawLine(head + new Vector2(1.6f, -1.5f), head + new Vector2(3.8f, -1.2f), brow, 0.8f, true);
        }

        if (!look.Beard)
        {
            c.DrawArc(head + new Vector2(0, 2.6f), 1.9f, 0.5f, Mathf.Pi - 0.5f, 6, A(look.Skin.Darkened(0.38f), alpha), 0.9f, true);
        }

        if (look.Glasses)
        {
            var frame = A(new Color("#2B2F33"), alpha);
            c.DrawArc(head + new Vector2(-2.7f, 1.2f), 2f, 0, Mathf.Tau, 10, frame, 0.7f, true);
            c.DrawArc(head + new Vector2(2.7f, 1.2f), 2f, 0, Mathf.Tau, 10, frame, 0.7f, true);
            c.DrawLine(head + new Vector2(-0.7f, 1), head + new Vector2(0.7f, 1), frame, 0.7f);
        }
    }

    /// <summary>Hair as seen from the front, for each style.</summary>
    private static void HairFront(CanvasItem c, Vector2 head, HairStyle style, Color hair)
    {
        switch (style)
        {
            case HairStyle.Bald:
                // A fringe of hair round the sides only.
                c.DrawArc(head + new Vector2(0, 0.5f), 7.6f, Mathf.Pi * 0.95f, Mathf.Pi * 1.18f, 5, hair, 2.2f, true);
                c.DrawArc(head + new Vector2(0, 0.5f), 7.6f, Mathf.Pi * 1.82f, Mathf.Pi * 2.05f, 5, hair, 2.2f, true);
                c.DrawCircle(head + new Vector2(-2.5f, -4.5f), 1.4f, new Color(1, 1, 1, 0.25f));
                return;

            case HairStyle.Curly:
                c.DrawColoredPolygon(HairCap(head, 8.6f, 1.2f), hair);
                for (var i = 0; i < 7; i++)
                {
                    var a = Mathf.Pi + (i * Mathf.Pi / 6);
                    c.DrawCircle(head + new Vector2(Mathf.Cos(a) * 8, (Mathf.Sin(a) * 8) - 0.8f), 2.6f, hair);
                }

                return;

            case HairStyle.Crop:
                c.DrawColoredPolygon(HairCap(head, 7.8f, -0.5f, fringe: false), hair);
                return;

            case HairStyle.Bun:
                c.DrawCircle(head + new Vector2(0, -8.5f), 3.4f, hair);
                c.DrawColoredPolygon(HairCap(head, 7.9f, -0.5f), hair);
                return;

            case HairStyle.Ponytail:
                // The tail shows just past the neck on one side.
                c.DrawColoredPolygon(HairCap(head, 7.9f, -0.5f), hair);
                c.DrawColoredPolygon(new[] { head + new Vector2(5.5f, 2), head + new Vector2(8, 3), head + new Vector2(8.5f, 9), head + new Vector2(6.5f, 8) }, hair.Darkened(0.1f));
                return;

            default:
                c.DrawColoredPolygon(HairCap(head, 7.9f, -0.5f), hair);
                return;
        }
    }

    /// <summary>The back of the head: hair over almost all of it (or the theatre cap's ties), ears showing.</summary>
    private static void HeadFromBehind(CanvasItem c, Vector2 head, PersonLook look, float alpha)
    {
        var skin = A(look.Skin, alpha);
        var hair = A(look.Hair, alpha);
        c.DrawCircle(head + new Vector2(-7.3f, 1), 1.8f, skin);
        c.DrawCircle(head + new Vector2(7.3f, 1), 1.8f, skin);
        c.DrawCircle(head, 7.5f, skin);
        if (look.Cap is { } cap)
        {
            c.DrawColoredPolygon(Ellipse(head + new Vector2(0, -0.8f), 8.2f, 7.8f), A(cap, alpha));
            c.DrawLine(head + new Vector2(-1, 3), head + new Vector2(-3, 8), A(cap.Darkened(0.2f), alpha), 1.2f);
            c.DrawLine(head + new Vector2(1, 3), head + new Vector2(3, 8), A(cap.Darkened(0.2f), alpha), 1.2f);
            return;
        }

        switch (look.Hairdo)
        {
            case HairStyle.Bald:
                c.DrawArc(head + new Vector2(0, 0.5f), 7.4f, Mathf.Pi * -0.05f, Mathf.Pi * 1.05f, 12, hair, 3f, true);
                break;
            case HairStyle.Long:
                c.DrawColoredPolygon(new[] { head + new Vector2(-8, -1), head + new Vector2(8, -1), head + new Vector2(7.5f, 11), head + new Vector2(-7.5f, 11) }, hair);
                c.DrawCircle(head, 8f, hair);
                break;
            case HairStyle.Ponytail:
                c.DrawCircle(head, 7.9f, hair);
                c.DrawColoredPolygon(new[] { head + new Vector2(-2, 5), head + new Vector2(2, 5), head + new Vector2(1.2f, 13), head + new Vector2(-1.2f, 13) }, hair);
                c.DrawLine(head + new Vector2(-1.8f, 6), head + new Vector2(1.8f, 6), A(look.Hair.Darkened(0.45f), alpha), 1.2f);
                break;
            case HairStyle.Bun:
                c.DrawCircle(head, 7.9f, hair);
                c.DrawCircle(head + new Vector2(0, -3), 3.8f, hair.Darkened(0.12f));
                break;
            case HairStyle.Curly:
                c.DrawCircle(head + new Vector2(0, -0.5f), 8.4f, hair);
                for (var i = 0; i < 9; i++)
                {
                    var a = Mathf.Pi * 0.9f + (i * Mathf.Pi * 1.2f / 8);
                    c.DrawCircle(head + new Vector2(Mathf.Cos(a) * 8, Mathf.Sin(a) * 8), 2.6f, hair);
                }

                break;
            default:
                // Short hair, ending above the neck.
                c.DrawColoredPolygon(Ellipse(head + new Vector2(0, -0.6f), 7.9f, 7.4f), hair);
                break;
        }

        c.DrawArc(head + new Vector2(0, -1), 6, Mathf.Pi * 1.15f, Mathf.Pi * 1.45f, 5, A(look.Hair.Lightened(0.25f), alpha), 1f, true);
    }

    private static Vector2[] HairCap(Vector2 head, float radius = 7.9f, float lift = -0.5f, bool fringe = true)
    {
        // Upper part of the head plus a short fringe.
        var points = new Vector2[13];
        for (var i = 0; i <= 10; i++)
        {
            var a = Mathf.Pi + (i * Mathf.Pi / 10);
            points[i] = head + new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius + lift);
        }

        points[11] = head + (fringe ? new Vector2(4, -2) : new Vector2(5, -3.2f));
        points[12] = head + (fringe ? new Vector2(-6, -1.5f) : new Vector2(-5, -3.2f));
        return points;
    }

    /// <summary>
    /// A patient lying on their back under a blanket, head at the north end. (gx, gy) is the
    /// centre of the mattress in grid tiles; <paramref name="lift"/> is the mattress height in pixels.
    /// </summary>
    public static void Lying(CanvasItem c, double gx, double gy, PersonLook look, Color blanket, float lift, bool headSouth = false, Color? band = null, BabyLook[]? onChest = null)
    {
        var up = new Vector2(0, -lift - 1);
        LyingFigure(c, (along, across) => IsoDraw.ToScreen(gx + across, headSouth ? gy - along : gy + along) + up, look, blanket, band, onChest);
    }

    /// <summary>
    /// A patient lying under a blanket on a trolley that points along <paramref name="dir"/> (the way it is
    /// being pushed). Patients go feet first, so the head is at the back, by the porter.
    /// </summary>
    public static void LyingAlong(CanvasItem c, double gx, double gy, (double X, double Y) dir, PersonLook look, Color blanket, float lift, Color? band = null, BabyLook[]? onChest = null)
    {
        var len = Math.Sqrt((dir.X * dir.X) + (dir.Y * dir.Y));
        var (fx, fy) = len < 1e-6 ? (0.0, 1.0) : (dir.X / len, dir.Y / len);
        var (nx, ny) = (-fy, fx);
        var up = new Vector2(0, -lift - 1);
        LyingFigure(c, (along, across) => IsoDraw.ToScreen(gx + (fx * along) + (nx * across), gy + (fy * along) + (ny * across)) + up, look, blanket, band, onChest);
    }

    /// <summary>Hospital gown: pale blue with a small print.</summary>
    private static readonly Color Gown = new("#C3D8EA");

    /// <summary>
    /// A patient in bed. The blanket is tucked in at chest height and follows the body underneath it:
    /// hips, the two legs and the feet tenting it at the end. Above it the patient wears a hospital
    /// gown, arms resting on the covers, with a wristband (in the triage colour when there is one)
    /// and their head on the pillow. <paramref name="at"/> maps (along, across) to the screen: along
    /// runs from the head (negative) to the feet (positive).
    /// </summary>
    private static void LyingFigure(CanvasItem c, Func<double, double, Vector2> at, PersonLook look, Color blanket, Color? band, BabyLook[]? onChest = null)
    {
        Vector2 Up(double along, double across, float h) => at(along, across) + new Vector2(0, -h);

        // An outline traced round the body at a list of (along, half-width) stations.
        Vector2[] Outline((double Along, double Half)[] stations, Func<double, float> height)
        {
            var points = new Vector2[stations.Length * 2];
            for (var i = 0; i < stations.Length; i++)
            {
                var (along, half) = stations[i];
                points[i] = Up(along, -half, height(along));
                points[(stations.Length * 2) - 1 - i] = Up(along, half, height(along));
            }

            return points;
        }

        // The blanket, tucked in down the sides of the mattress, with its thickness showing.
        const double top = -0.5;
        const double end = 1.8;
        const double width = 0.64;
        var cover = RoundedQuad(at, top, end, width, 0.14);
        c.DrawColoredPolygon(Array.ConvertAll(cover, p => p + new Vector2(0, 1.5f)), blanket.Darkened(0.28f));
        c.DrawColoredPolygon(cover, blanket);

        // The body under the covers: a gentle rise over the hips and two legs, then the feet tenting it.
        float Rise(double along) => (float)(along < 0.6 ? 3.2 : 3.2 - ((along - 0.6) * 1.2));
        var body = Outline(new[] { (top + 0.05, 0.47), (0.2, 0.46), (0.55, 0.42), (1.0, 0.34), (1.45, 0.27), (1.62, 0.24) }, Rise);
        c.DrawColoredPolygon(Array.ConvertAll(body, p => p + new Vector2(0.8f, 1.6f)), new Color(blanket.Darkened(0.2f), 0.6f));
        c.DrawColoredPolygon(body, blanket.Lightened(0.1f));
        c.DrawPolyline(new[] { Up(0.55, 0, Rise(0.55) - 0.6f), Up(1.0, 0, Rise(1.0) - 1), Up(1.5, 0, Rise(1.5) - 1) }, blanket.Darkened(0.2f), 1.2f, true);
        if (look.Bump)
        {
            // The pregnant bump lifting the blanket over the tummy.
            var dome = at(0.05, 0) + new Vector2(0, -7);
            c.DrawColoredPolygon(Ellipse(dome + new Vector2(1, 3), 12, 8.5f), new Color(blanket.Darkened(0.25f), 0.5f));
            c.DrawColoredPolygon(Ellipse(dome, 12, 8.5f), blanket.Lightened(0.12f));
            c.DrawArc(dome + new Vector2(-3, -2.5f), 6, Mathf.Pi * 1.05f, Mathf.Pi * 1.6f, 8, blanket.Lightened(0.35f), 1.4f, true);
            c.DrawArc(dome, 12, 0.15f, Mathf.Pi - 0.15f, 14, blanket.Darkened(0.2f), 1f, true);
        }
        foreach (var side in new[] { -1.0, 1.0 })
        {
            // The two feet tenting the blanket.
            c.DrawColoredPolygon(Ellipse(Up(1.6, side * 0.13, 4.2f), 3.6f, 2.8f, 12), blanket.Lightened(0.14f));
            c.DrawArc(Up(1.6, side * 0.13, 4.2f), 3.2f, 0.2f, Mathf.Pi - 0.2f, 6, blanket.Darkened(0.18f), 1f, true);
        }

        // Folds where the blanket drops over the sides of the mattress.
        c.DrawLine(Up(0.3, -0.56, 0.5f), Up(1.5, -0.56, 0.5f), new Color(blanket.Darkened(0.15f), 0.7f), 1f, true);
        c.DrawLine(Up(0.3, 0.56, 0.5f), Up(1.5, 0.56, 0.5f), new Color(blanket.Darkened(0.15f), 0.7f), 1f, true);

        // The white top sheet turned down over the blanket at the chest.
        var sheet = new[] { Up(top - 0.04, -width, 1.2f), Up(top - 0.04, width, 1.2f), Up(top + 0.2, width, 1.4f), Up(top + 0.2, -width, 1.4f) };
        c.DrawColoredPolygon(sheet, new Color("#F3F6F8"));
        c.DrawLine(sheet[2], sheet[3], new Color("#C9D3DC"), 1.1f, true);

        // Shoulders and chest in the gown above the sheet.
        var gown = Outline(new[] { (-1.2, 0.2), (-1.16, 0.34), (-1.08, 0.45), (-0.96, 0.5), (-0.7, 0.5), (-0.46, 0.49) }, _ => 3.8f);
        c.DrawColoredPolygon(Array.ConvertAll(gown, p => p + new Vector2(0.8f, 1.5f)), new Color(0, 0, 0, 0.1f));
        c.DrawColoredPolygon(gown, Gown);
        c.DrawPolyline(gown.Append(gown[0]).ToArray(), Gown.Darkened(0.25f), 0.8f, true);
        foreach (var (a, b) in new[] { (-0.92, -0.25), (-0.75, 0.1), (-0.62, -0.3), (-0.85, 0.3), (-0.6, 0.22) })
        {
            c.DrawCircle(Up(a, b, 3.6f), 0.7f, new Color("#7FA3C6"));
        }

        // Arms on top of the covers, sleeves to the elbow, hands resting by the hips.
        var skin = look.Skin;
        foreach (var side in new[] { -1.0, 1.0 })
        {
            var shoulder = Up(-0.98, side * 0.43, 4.2f);
            var elbow = Up(-0.64, side * 0.5, 4.4f);
            var hand = Up(-0.36, side * 0.2, 5.2f);
            c.DrawLine(shoulder + new Vector2(0.6f, 1.4f), elbow + new Vector2(0.6f, 1.4f), new Color(0, 0, 0, 0.12f), 6.4f);
            c.DrawLine(elbow + new Vector2(0.6f, 1.4f), hand + new Vector2(0.6f, 1.4f), new Color(0, 0, 0, 0.12f), 5.4f);
            c.DrawLine(shoulder, shoulder.Lerp(elbow, 0.65f), Gown.Darkened(0.06f), 6.4f);
            c.DrawCircle(shoulder, 3.2f, Gown.Darkened(0.06f));
            c.DrawLine(shoulder.Lerp(elbow, 0.6f), elbow, skin, 4.8f);
            c.DrawLine(elbow, hand, skin.Darkened(0.04f), 4.6f);
            c.DrawCircle(elbow, 2.3f, skin);
            c.DrawCircle(hand, 2.8f, skin);
            if (side < 0)
            {
                // The patient's wristband.
                var wrist = elbow.Lerp(hand, 0.78f);
                var along = (hand - elbow).Normalized();
                c.DrawLine(wrist - (along * 1.2f), wrist + (along * 1.2f), band ?? Colors.White, 4.4f);
            }
        }

        // Neck, then the head on the pillow with a little shadow under it.
        c.DrawLine(Up(-1.3, 0, 3), Up(-1.08, 0, 3.4f), skin.Darkened(0.06f), 5f);
        c.DrawColoredPolygon(new[] { Up(-1.13, -0.13, 3.6f), Up(-1.13, 0.13, 3.6f), Up(-1.02, 0, 3.6f) }, skin.Darkened(0.04f));
        var face = at(-1.45, 0) + new Vector2(0, -4);
        c.DrawColoredPolygon(IsoDraw.FloorEllipse(face + new Vector2(1, 5), 8), new Color(0, 0, 0, 0.1f));
        if (look.Hairdo == HairStyle.Long)
        {
            // Long hair spread on the pillow.
            c.DrawColoredPolygon(Ellipse(face + new Vector2(0, -1.5f), 10, 7.5f), look.Hair);
        }

        c.DrawCircle(face + new Vector2(-7.2f, 1), 1.7f, skin);
        c.DrawCircle(face + new Vector2(7.2f, 1), 1.7f, skin.Darkened(0.08f));
        c.DrawCircle(face, 7.5f, skin);
        c.DrawCircle(face + new Vector2(2.5f, 1), 5.5f, new Color(skin.Darkened(0.06f), 0.5f));
        c.DrawArc(face, 7.5f, 0, Mathf.Tau, 24, skin.Darkened(0.25f), 1f, true);
        HairFront(c, face, look.Hairdo == HairStyle.Long ? HairStyle.Short : look.Hairdo, look.Hair);
        if (look.Beard)
        {
            c.DrawArc(face + new Vector2(0, 0.5f), 6.4f, 0.25f, Mathf.Pi - 0.25f, 10, look.Hair, 2.6f, true);
        }

        // Resting, eyes closed.
        var lid = skin.Darkened(0.45f);
        c.DrawArc(face + new Vector2(-2.6f, 1), 1.3f, 0.2f, Mathf.Pi - 0.2f, 6, lid, 0.9f, true);
        c.DrawArc(face + new Vector2(2.6f, 1), 1.3f, 0.2f, Mathf.Pi - 0.2f, 6, lid, 0.9f, true);
        c.DrawLine(face + new Vector2(-3.8f, -1.3f), face + new Vector2(-1.6f, -1.5f), look.Hair.Darkened(0.2f), 0.8f, true);
        c.DrawLine(face + new Vector2(1.6f, -1.5f), face + new Vector2(3.8f, -1.3f), look.Hair.Darkened(0.2f), 0.8f, true);
        c.DrawCircle(face + new Vector2(0, 2.8f), 0.9f, skin.Darkened(0.12f));
        if (look.Glasses)
        {
            c.DrawArc(face + new Vector2(-2.7f, 1.2f), 2f, 0, Mathf.Tau, 10, new Color("#2B2F33"), 0.7f, true);
            c.DrawArc(face + new Vector2(2.7f, 1.2f), 2f, 0, Mathf.Tau, 10, new Color("#2B2F33"), 0.7f, true);
        }

        if (onChest is { Length: > 0 })
        {
            // Skin to skin: the newborn (or twins) lying on mum's chest, her hand round them.
            for (var i = 0; i < onChest.Length; i++)
            {
                var spot = Up(-0.78 + (i * 0.32), (i * 0.18) - (0.09 * (onChest.Length - 1)), 7.5f);
                Baby(c, spot, onChest[i]);
            }

            var hold = Up(-0.6, 0.28, 8.5f);
            c.DrawLine(hold + new Vector2(5, 1), hold, skin, 4.2f);
            c.DrawCircle(hold, 2.6f, skin);
        }
    }

    /// <summary>
    /// A newborn swaddled in a cellular blanket with a little knitted hat, lying across the screen
    /// with the head to the left. <paramref name="at"/> is the middle of the bundle; it is about 0.5 m long.
    /// </summary>
    public static void Baby(CanvasItem c, Vector2 at, BabyLook look, float alpha = 1f, float scale = 1f)
    {
        // A touch larger than life, so babies read at normal zoom.
        scale *= 1.3f;
        Vector2 P(float x, float y) => at + (new Vector2(x, y) * scale);
        var wrap = A(look.Wrap, alpha);
        c.DrawColoredPolygon(Ellipse(P(1.5f, 2.2f), 8.2f * scale, 3.6f * scale), new Color(0, 0, 0, 0.12f * alpha));

        // The bundle: rounded, a shade darker underneath, with the blanket's folds and its coloured edge.
        var bundle = Ellipse(P(1.8f, 0), 7.4f * scale, 4.3f * scale, 18);
        c.DrawColoredPolygon(bundle, wrap);
        c.DrawColoredPolygon(Ellipse(P(2.2f, 1.6f), 6.4f * scale, 2.6f * scale, 14), A(look.Wrap.Darkened(0.1f), alpha));
        c.DrawPolyline(bundle.Append(bundle[0]).ToArray(), A(look.Wrap.Darkened(0.32f), alpha), 0.8f, true);
        c.DrawPolyline(new[] { P(-2.4f, -3.2f), P(1.5f, 0.2f), P(-1.8f, 3.6f) }, A(look.Trim, alpha), 1.3f * scale, true);
        c.DrawLine(P(3.5f, -3.4f), P(6.5f, 2.2f), A(look.Wrap.Darkened(0.18f), alpha), 0.7f, true);
        for (var i = 0; i < 3; i++)
        {
            c.DrawCircle(P(4 + (i * 1.6f), -1.2f + (i * 0.9f)), 0.45f * scale, A(look.Wrap.Darkened(0.2f), alpha));
        }

        // A round face peeping out, eyes closed, and a knitted hat with a turned-up brim.
        var head = P(-4.6f, -0.6f);
        var r = 3.5f * scale;
        c.DrawCircle(head, r, A(look.Skin, alpha));
        c.DrawCircle(head + new Vector2(0.9f, 0.9f) * scale, r * 0.75f, A(look.Skin.Darkened(0.05f), 0.6f * alpha));
        c.DrawArc(head, r, 0, Mathf.Tau, 16, A(look.Skin.Darkened(0.28f), alpha), 0.7f, true);
        var lid = A(look.Skin.Darkened(0.45f), alpha);
        c.DrawArc(head + new Vector2(-1.3f, 0.5f) * scale, 0.7f * scale, 0.3f, Mathf.Pi - 0.3f, 5, lid, 0.6f, true);
        c.DrawArc(head + new Vector2(0.9f, 0.5f) * scale, 0.7f * scale, 0.3f, Mathf.Pi - 0.3f, 5, lid, 0.6f, true);
        c.DrawCircle(head + new Vector2(-1.9f, 1.6f) * scale, 0.7f * scale, A(new Color("#F2A5A0"), 0.45f * alpha));
        var hat = new Vector2[12];
        for (var i = 0; i < hat.Length; i++)
        {
            var a = Mathf.Pi + (i * Mathf.Pi / (hat.Length - 1));
            hat[i] = head + (new Vector2(Mathf.Cos(a) * (r + 0.5f), (Mathf.Sin(a) * (r + 0.6f)) - 0.4f));
        }

        c.DrawColoredPolygon(hat, A(look.Hat, alpha));
        c.DrawLine(head + new Vector2(-r - 0.4f, -0.4f), head + new Vector2(r + 0.4f, -0.4f), A(look.Hat.Darkened(0.15f), alpha), 1.6f * scale, true);
        c.DrawCircle(head + new Vector2(0, -r - 0.9f), 1.1f * scale, A(look.Hat.Lightened(0.15f), alpha));
    }

    /// <summary>A rectangle with rounded corners in the (along, across) frame of a bed or trolley.</summary>
    private static Vector2[] RoundedQuad(Func<double, double, Vector2> at, double a0, double a1, double half, double r)
    {
        var corners = new[] { (-half + r, a0 + r, Math.PI), (half - r, a0 + r, 1.5 * Math.PI), (half - r, a1 - r, 0.0), (-half + r, a1 - r, 0.5 * Math.PI) };
        var points = new Vector2[corners.Length * 5];
        var i = 0;
        foreach (var (across, along, start) in corners)
        {
            for (var k = 0; k < 5; k++)
            {
                var a = start + (Math.PI / 2 * (k / 4.0));
                points[i++] = at(along + (Math.Sin(a) * r), across + (Math.Cos(a) * r));
            }
        }

        return points;
    }

    /// <summary>
    /// An A&amp;E trolley pointing along <paramref name="dir"/>: castors, a dark base with its hydraulic
    /// columns, a steel frame, a blue vinyl mattress with a white sheet, side rails up, a push bar at
    /// the head (the back) and a folded drip pole. Returns the mattress height.
    /// </summary>
    public static float Trolley(CanvasItem c, double gx, double gy, (double X, double Y) dir)
    {
        const float frame = 13f;
        var o = Orient(gx, gy, dir);
        var steel = new Color("#B9C3CB");

        o.Shadow(c, -2.0, 2.0, -0.75, 0.75);
        foreach (var (along, across) in new[] { (-1.6, -0.5), (-1.6, 0.5), (1.6, -0.5), (1.6, 0.5) })
        {
            Wheel(c, o.At(along, across));
        }

        // Base, the two hydraulic columns, then the frame and mattress.
        o.Prism(c, -1.75, 1.75, -0.6, 0.6, 4, new Color("#5A6670"), new Color("#3F4A53"), 3);

        // An oxygen cylinder carried in the tray under the foot end.
        o.Prism(c, 0.6, 1.5, -0.12, 0.12, 3, Colors.White, new Color("#D9DFE4"), 7);
        foreach (var along in new[] { -0.9, 0.9 })
        {
            o.Prism(c, along - 0.14, along + 0.14, -0.14, 0.14, frame - 9, new Color("#8C979F"), new Color("#6F7B85"), 6);
        }

        o.Prism(c, -2.0, 2.0, -0.7, 0.7, 2, steel, steel.Darkened(0.2f), frame - 3);
        o.Prism(c, -1.92, 1.92, -0.62, 0.62, 4, new Color("#F2F5F7"), new Color("#3E6A96"), frame - 1);
        o.Rim(c, -1.92, 1.92, -0.62, 0.62, frame + 3, new Color("#DCE3E9"));

        // Side rails up, on short posts.
        foreach (var across in new[] { -0.74, 0.74 })
        {
            foreach (var along in new[] { -1.3, 0.0, 1.3 })
            {
                c.DrawLine(o.At(along, across, frame - 1), o.At(along, across, frame + 7), new Color("#8C979F"), 1.2f);
            }

            c.DrawLine(o.At(-1.5, across, frame + 7), o.At(1.5, across, frame + 7), steel, 1.8f, true);
            c.DrawLine(o.At(-1.5, across, frame + 3.5f), o.At(1.5, across, frame + 3.5f), new Color(steel, 0.8f), 1.1f, true);
        }

        // Push bar across the head end, and the folded drip pole beside it.
        c.DrawLine(o.At(-2.1, -0.62, frame), o.At(-2.1, -0.62, frame + 12), new Color("#7F8A93"), 1.6f);
        c.DrawLine(o.At(-2.1, 0.62, frame), o.At(-2.1, 0.62, frame + 12), new Color("#7F8A93"), 1.6f);
        c.DrawLine(o.At(-2.1, -0.62, frame + 12), o.At(-2.1, 0.62, frame + 12), new Color("#4A545D"), 2.6f, true);
        c.DrawLine(o.At(-1.95, 0.7, frame), o.At(-1.95, 0.7, frame + 26), steel, 1.2f);
        c.DrawLine(o.At(-1.95, 0.7, frame + 26) + new Vector2(-3, 0), o.At(-1.95, 0.7, frame + 26) + new Vector2(3, 0), steel, 1.2f);
        return frame + 3;
    }

    /// <summary>
    /// The ambulance crew's stretcher, pointing along <paramref name="dir"/>, with the patient on it:
    /// a scissor undercarriage on castors, a black mattress, the yellow head bar and the safety straps
    /// buckled over the blanket.
    /// </summary>
    public static void AmbulanceStretcher(CanvasItem c, double gx, double gy, (double X, double Y) dir, PersonLook look, Color blanket, Color? band = null)
    {
        const float frame = 14f;
        var o = Orient(gx, gy, dir);
        var black = new Color("#2A2E33");
        var steel = new Color("#C3CBD2");

        o.Shadow(c, -2.0, 2.0, -0.75, 0.75);
        foreach (var (along, across) in new[] { (-1.7, -0.55), (-1.7, 0.55), (1.7, -0.55), (1.7, 0.55) })
        {
            Wheel(c, o.At(along, across));
        }

        // The base and the X of the scissor legs on each side.
        o.Prism(c, -1.8, 1.8, -0.6, 0.6, 2, new Color("#7F8A93"), new Color("#5E6A74"), 3);
        foreach (var across in new[] { -0.55, 0.55 })
        {
            c.DrawLine(o.At(-1.4, across, 4), o.At(1.4, across, frame - 2), steel, 1.8f, true);
            c.DrawLine(o.At(1.4, across, 4), o.At(-1.4, across, frame - 2), steel, 1.8f, true);
        }

        o.Prism(c, -2.0, 2.0, -0.66, 0.66, 2, steel, steel.Darkened(0.2f), frame - 2);
        o.Prism(c, -1.92, 1.92, -0.6, 0.6, 3, black.Lightened(0.08f), black, frame);

        // Yellow head bar at the back, by the crew, on two short arms.
        foreach (var across in new[] { -0.55, 0.55 })
        {
            c.DrawLine(o.At(-1.9, across, frame), o.At(-2.15, across, frame + 3), steel, 1.4f);
        }

        c.DrawLine(o.At(-2.15, -0.6, frame + 3), o.At(-2.15, 0.6, frame + 3), new Color("#F2C230"), 2.6f, true);

        LyingAlong(c, gx, gy, dir, look, blanket, frame + 3 - 1, band);

        // Two straps over the blanket, black with a buckle each.
        foreach (var (along, rise) in new[] { (-0.7, 6f), (1.2, 6f) })
        {
            var from = o.At(along, -0.64, frame + rise);
            var to = o.At(along, 0.64, frame + rise);
            c.DrawLine(from, to, new Color("#1C1F22"), 2f, true);
            var buckle = from.Lerp(to, 0.5f);
            c.DrawRect(new Rect2(buckle + new Vector2(-1.8f, -1.6f), new Vector2(3.6f, 3.2f)), new Color("#F2C230"));
        }

        // Folded side rails in front of the straps.
        foreach (var across in new[] { -0.7, 0.7 })
        {
            c.DrawLine(o.At(-1.4, across, frame + 3), o.At(1.4, across, frame + 3), steel, 1.4f, true);
        }
    }

    /// <summary>A small castor wheel on the floor.</summary>
    private static void Wheel(CanvasItem c, Vector2 at)
    {
        c.DrawLine(at + new Vector2(0, -5), at + new Vector2(0, -2.5f), new Color("#6E7A84"), 1.3f);
        c.DrawCircle(at + new Vector2(0, -2), 2.2f, new Color("#22272B"));
        c.DrawCircle(at + new Vector2(0, -2), 0.8f, new Color("#7F8A93"));
    }

    private static Oriented Orient(double gx, double gy, (double X, double Y) dir)
    {
        var len = Math.Sqrt((dir.X * dir.X) + (dir.Y * dir.Y));
        var (fx, fy) = len < 1e-6 ? (0.0, 1.0) : (dir.X / len, dir.Y / len);
        return new Oriented(gx, gy, fx, fy);
    }

    /// <summary>A frame of reference for something that points along (fx, fy): positions are given along it and across it, in tiles.</summary>
    private readonly record struct Oriented(double Gx, double Gy, double Fx, double Fy)
    {
        public Vector2 At(double along, double across, float height = 0) =>
            IsoDraw.ToScreen(Gx + (Fx * along) - (Fy * across), Gy + (Fy * along) + (Fx * across)) + new Vector2(0, -height);

        private Vector2[] Corners(double a0, double a1, double b0, double b1, float height) =>
            new[] { At(a0, b0, height), At(a1, b0, height), At(a1, b1, height), At(a0, b1, height) };

        public void Shadow(CanvasItem c, double a0, double a1, double b0, double b1) =>
            c.DrawColoredPolygon(Array.ConvertAll(Corners(a0, a1, b0, b1, 0), p => p + new Vector2(2, 2)), new Color(0, 0, 0, 0.13f));

        /// <summary>A box from a0 to a1 along and b0 to b1 across, standing <paramref name="lift"/> pixels off the floor.</summary>
        public void Prism(CanvasItem c, double a0, double a1, double b0, double b1, float height, Color top, Color side, float lift = 0)
        {
            var low = Corners(a0, a1, b0, b1, lift);
            var high = Corners(a0, a1, b0, b1, lift + height);
            var centre = (low[0] + low[1] + low[2] + low[3]) / 4;
            for (var i = 0; i < 4; i++)
            {
                var (p, q) = (low[i], low[(i + 1) % 4]);
                var mid = (p + q) / 2;
                if (mid.Y > centre.Y + 0.01f)
                {
                    c.DrawColoredPolygon(new[] { p, q, high[(i + 1) % 4], high[i] }, side.Darkened(mid.X < centre.X ? 0f : 0.12f));
                }
            }

            c.DrawColoredPolygon(high, top);
        }

        public void Rim(CanvasItem c, double a0, double a1, double b0, double b1, float height, Color colour)
        {
            var p = Corners(a0, a1, b0, b1, height);
            c.DrawPolyline(new[] { p[0], p[1], p[2], p[3], p[0] }, colour, 1f, true);
        }
    }

    private static readonly Color ChairFrame = new("#8C979F");
    private static readonly Color ChairSeat = new("#2B4A6B");
    private static readonly Color Tyre = new("#22272B");

    /// <summary>
    /// A hospital wheelchair whose seat centre is at <paramref name="foot"/>, the parts behind the
    /// person: the big wheels on their far sides, the push handles and the padded back and seat.
    /// </summary>
    public static void WheelchairBack(CanvasItem c, Vector2 foot)
    {
        c.DrawColoredPolygon(IsoDraw.FloorEllipse(foot + new Vector2(0, 1), 16), new Color(0, 0, 0, 0.2f));

        // Big wheels, seen at an angle; the far half of each is behind the person.
        foreach (var side in new[] { -1f, 1f })
        {
            var hub = foot + new Vector2(side * 13, -10);
            c.DrawColoredPolygon(Ellipse(hub, 5.5f, 10.5f), Tyre);
            c.DrawColoredPolygon(Ellipse(hub, 4f, 8.5f), new Color("#C9D2D9"));
            c.DrawColoredPolygon(Ellipse(hub, 3.1f, 7.4f), new Color("#4A545D"));
            for (var k = 0; k < 4; k++)
            {
                var a = k * Mathf.Pi / 4;
                c.DrawLine(hub + new Vector2(Mathf.Cos(a) * 3, Mathf.Sin(a) * 7.2f), hub - new Vector2(Mathf.Cos(a) * 3, Mathf.Sin(a) * 7.2f), new Color("#9AA5AE"), 0.6f);
            }

            c.DrawCircle(hub, 1.4f, new Color("#C9D2D9"));
        }

        // Frame uprights up to the push handles with black grips.
        foreach (var side in new[] { -1f, 1f })
        {
            var x = side * 9.5f;
            c.DrawLine(foot + new Vector2(x, -10), foot + new Vector2(x, -44), ChairFrame, 2f);
            c.DrawLine(foot + new Vector2(x, -44), foot + new Vector2(x + (side * 2.5f), -44.5f), Figures.Shoes, 2.6f, true);
        }

        // Sling back and seat cushion.
        c.DrawColoredPolygon(new[] { foot + new Vector2(-9.5f, -41), foot + new Vector2(9.5f, -41), foot + new Vector2(9.5f, -18), foot + new Vector2(0, -16.5f), foot + new Vector2(-9.5f, -18) }, ChairSeat);
        c.DrawLine(foot + new Vector2(-9.5f, -38), foot + new Vector2(9.5f, -38), ChairSeat.Lightened(0.15f), 1f);
        c.DrawRect(new Rect2(foot + new Vector2(-10, -16), new Vector2(20, 5)), ChairSeat.Darkened(0.1f));
        c.DrawLine(foot + new Vector2(-10, -11), foot + new Vector2(10, -11), ChairFrame, 1.4f);
    }

    /// <summary>The wheelchair's parts in front of the person: armrests, the near halves of the wheels with their push rims, the front castors and the footplates.</summary>
    public static void WheelchairFront(CanvasItem c, Vector2 foot)
    {
        foreach (var side in new[] { -1f, 1f })
        {
            // Armrest pad on its support.
            c.DrawLine(foot + new Vector2(side * 10.5f, -12), foot + new Vector2(side * 10.5f, -24), ChairFrame, 1.6f);
            Figures.RoundedRect(c, new Rect2(foot + new Vector2((side * 10.5f) - 2.5f, -27), new Vector2(5, 3.5f)), 1.5f, Figures.Shoes);

            // Push rim round the outside of the big wheel.
            var hub = foot + new Vector2(side * 13, -10);
            var rim = new Vector2[11];
            for (var k = 0; k < rim.Length; k++)
            {
                var a = (-Mathf.Pi / 2) + (k * Mathf.Pi / (rim.Length - 1));
                rim[k] = hub + new Vector2(side * Mathf.Cos(a) * 6.8f, Mathf.Sin(a) * 9.6f);
            }

            c.DrawPolyline(rim, new Color("#C9D2D9"), 1.2f, true);
            c.DrawLine(hub + new Vector2(side * 5.2f, 0), hub + new Vector2(side * 6.8f, 0), new Color("#9AA5AE"), 0.8f);

            // Front castor and footplate, with the patient's feet resting on it.
            var castor = foot + new Vector2(side * 7.5f, 5);
            c.DrawLine(foot + new Vector2(side * 9.5f, -10), castor + new Vector2(0, -3), ChairFrame, 1.6f);
            c.DrawCircle(castor + new Vector2(0, -1.5f), 2.2f, Tyre);
            c.DrawColoredPolygon(new[] { castor + new Vector2(-3.5f * side - 2.5f, -1), castor + new Vector2(-3.5f * side + 2.5f, -1), castor + new Vector2(-3.5f * side + 3, 1.5f), castor + new Vector2(-3.5f * side - 3, 1.5f) }, new Color("#3A4148"));
        }
    }

    private static Vector2[] Ellipse(Vector2 centre, float rx, float ry, int n = 18)
    {
        var points = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            var a = i * Mathf.Tau / n;
            points[i] = centre + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }

        return points;
    }

    /// <summary>
    /// A hospital bed (about 0.8 m by 2.1 m) with its head against the north. Returns the mattress
    /// height. <paramref name="onMattress"/> draws whoever is lying on it, before the rail and end
    /// board nearest the viewer, so the bed reads correctly from every view angle.
    /// </summary>
    public static float Bed(CanvasItem c, double gx, double gy, bool made, bool headSouth = false, Action<float>? onMattress = null)
    {
        const float deck = 12f;
        const float mattress = 6f;
        const float top = deck + mattress;
        var x = gx - 0.8;
        var y = gy - 2.1;
        var (headY, footY) = headSouth ? (y + 4.2, y) : (y, y + 4.2);
        var headNear = IsoDraw.ToScreen(gx, headY).Y > IsoDraw.ToScreen(gx, footY).Y;
        var westNear = IsoDraw.ToScreen(x, gy).Y > IsoDraw.ToScreen(x + 1.6, gy).Y;

        // Castors, the dark chassis, the frame and the mattress.
        foreach (var (wx, wy) in new[] { (x + 0.15, y + 0.2), (x + 1.45, y + 0.2), (x + 0.15, y + 4.0), (x + 1.45, y + 4.0) })
        {
            Wheel(c, IsoDraw.ToScreen(wx, wy) + new Vector2(0, 1));
        }

        c.DrawColoredPolygon(new[] { IsoDraw.ToScreen(x - 0.05, y - 0.05), IsoDraw.ToScreen(x + 1.65, y - 0.05), IsoDraw.ToScreen(x + 1.65, y + 4.25), IsoDraw.ToScreen(x - 0.05, y + 4.25) }, new Color(0, 0, 0, 0.13f));
        IsoDraw.Box(c, x + 0.35, y + 0.4, 0.9, 3.4, 5, new Color("#6F7B85"), 3, shadow: false);
        IsoDraw.Prism(c, x, y, 1.6, 4.2, 4, new Color("#B7C2CB"), new Color("#9AA6B0"), lift: deck - 4, rim: new Color("#CDD5DC"));
        IsoDraw.Prism(c, x + 0.06, y + 0.06, 1.48, 4.08, mattress, new Color("#F4F7F9"), new Color("#4F7BA8"), lift: deck, rim: new Color("#E1E7EC"));
        foreach (var (a, b, _, _) in IsoDraw.VisibleFaces(x + 0.06, y + 0.06, 1.48, 4.08))
        {
            // The fitted sheet wraps over the top edge of the blue mattress.
            c.DrawColoredPolygon(new[] { a + new Vector2(0, -top), b + new Vector2(0, -top), b + new Vector2(0, -top + 2.2f), a + new Vector2(0, -top + 2.2f) }, new Color("#E8EEF2"));
        }

        void Board(double at, float height, bool head)
        {
            var by = at == y ? y - 0.12 : y + 4.2 - 0.1;
            IsoDraw.Prism(c, x - 0.05, by, 1.7, 0.22, height, new Color("#9DB2C4"), new Color("#7C93A8"), skirting: new Color("#5E7285"), skirtingHeight: 5, rim: new Color("#B9C9D6"));
            if (!head)
            {
                // The obs chart hanging on the foot board.
                foreach (var (a, b, _, _) in IsoDraw.VisibleFaces(x - 0.05, by, 1.7, 0.22))
                {
                    if ((b - a).Length() > 30)
                    {
                        var m = a.Lerp(b, 0.5f) + new Vector2(0, -height + 3);
                        var d = (b - a).Normalized() * 5;
                        c.DrawColoredPolygon(new[] { m - d, m + d, m + d + new Vector2(0, 9), m - d + new Vector2(0, 9) }, new Color("#F7F4E8"));
                        c.DrawLine(m - (d * 0.6f) + new Vector2(0, 4), m + (d * 0.6f) + new Vector2(0, 4), new Color("#6D8BA5"), 1f);
                    }
                }
            }
        }

        void Rail(bool west)
        {
            // A half-length cot side at the head end: two bars on posts.
            var rx = west ? x - 0.02 : x + 1.6 - 0.08;
            var (r0, r1) = headSouth ? (y + 2.0, y + 3.8) : (y + 0.4, y + 2.2);
            var lift = top + 6;
            foreach (var py in new[] { r0 + 0.2, r1 - 0.2 })
            {
                var p = IsoDraw.ToScreen(rx + 0.05, py);
                c.DrawLine(p + new Vector2(0, -deck), p + new Vector2(0, -lift - 2), new Color("#AEB9C2"), 1.5f);
            }

            IsoDraw.Prism(c, rx, r0, 0.1, r1 - r0, 2, new Color("#E3E9EE"), new Color("#B4BFC8"), lift: lift);
            IsoDraw.Prism(c, rx, r0 + 0.1, 0.1, r1 - r0 - 0.2, 1.5f, new Color("#D5DDE3"), new Color("#A9B4BE"), lift: top + 1.5f);
            if (west == westNear)
            {
                // The bed's control handset clipped to the near rail.
                var h = IsoDraw.ToScreen(rx + 0.05, (r0 + r1) / 2) + new Vector2(0, -lift);
                c.DrawLine(h, h + new Vector2(0, 6), new Color("#3A4148"), 0.8f);
                Figures.RoundedRect(c, new Rect2(h + new Vector2(-1.8f, 5), new Vector2(3.6f, 6)), 1, new Color("#5A6670"));
                c.DrawCircle(h + new Vector2(0, 7), 0.6f, new Color("#3EE07A"));
            }
        }

        // Far end board and far rail first, then the pillow and anything on the mattress.
        if (headNear)
        {
            Board(footY, 16, head: false);
        }
        else
        {
            Board(headY, 26, head: true);
        }

        Rail(!westNear);

        var pillowY = headSouth ? y + 4.2 - 0.25 - 0.8 : y + 0.25;
        IsoDraw.Prism(c, x + 0.25, pillowY, 1.1, 0.8, 4, Colors.White, new Color("#E3E9EE"), lift: top, rim: new Color("#E3E9EE"));
        c.DrawLine(IsoDraw.ToScreen(x + 0.4, pillowY + 0.4) + new Vector2(0, -top - 4.2f), IsoDraw.ToScreen(x + 1.2, pillowY + 0.4) + new Vector2(0, -top - 4.2f), new Color("#DCE3E9"), 1f, true);
        if (made)
        {
            // Empty bed: a folded blanket at the foot.
            IsoDraw.Prism(c, x + 0.15, headSouth ? y + 0.3 : y + 3.2, 1.3, 0.7, 3, new Color("#BFD3E6"), new Color("#A5BED6"), lift: top);
        }

        onMattress?.Invoke(top);

        // Near rail and end board last, in front of the patient.
        Rail(westNear);
        if (headNear)
        {
            Board(headY, 26, head: true);
        }
        else
        {
            Board(footY, 16, head: false);
        }

        return top;
    }

    /// <summary>An X-ray, CT or operating table: a slab on a central pedestal. Returns the height of its top.</summary>
    public static float Table(CanvasItem c, double gx, double gy, Color top)
    {
        const float height = 20f;
        IsoDraw.Box(c, gx - 0.5, gy - 1.0, 1.0, 2.0, 3, new Color("#4A545D"));
        IsoDraw.Box(c, gx - 0.3, gy - 0.8, 0.6, 1.6, height - 5, new Color("#7D8995"));
        IsoDraw.Box(c, gx - 0.75, gy - 2.05, 1.5, 4.1, 5, top, height - 5);
        return height;
    }

    /// <summary>Height of the obs monitor screen's bottom edge above the floor, in pixels.</summary>
    public const float MonitorHeight = 52f;

    /// <summary>An observations monitor's stand: a five-spoke wheeled base, the pole and a basket. The screen is drawn by <see cref="ObsScreen"/>.</summary>
    public static void MonitorStand(CanvasItem c, double gx, double gy)
    {
        var basePoint = IsoDraw.ToScreen(gx, gy);
        c.DrawColoredPolygon(IsoDraw.FloorEllipse(basePoint + new Vector2(1, 1), 12), new Color(0, 0, 0, 0.14f));
        var spoke = new Color("#56616B");
        for (var i = 0; i < 5; i++)
        {
            var a = (i * Mathf.Tau / 5) + 0.3f;
            var end = basePoint + new Vector2(Mathf.Cos(a) * 10, Mathf.Sin(a) * 5);
            c.DrawLine(basePoint + new Vector2(0, -2), end + new Vector2(0, -1), spoke, 2.2f, true);
            c.DrawCircle(end, 1.7f, new Color("#2B3036"));
        }

        c.DrawLine(basePoint + new Vector2(0, -2), basePoint + new Vector2(0, -MonitorHeight), new Color("#8D99A3"), 2.5f);
        c.DrawLine(basePoint + new Vector2(0.8f, -2), basePoint + new Vector2(0.8f, -MonitorHeight), new Color("#B9C3CB"), 0.8f);

        // A small wire basket for the cuff and probes, part way up.
        var basket = basePoint + new Vector2(0, -24);
        c.DrawColoredPolygon(new[] { basket + new Vector2(-6, -3), basket + new Vector2(6, -3), basket + new Vector2(5, 3), basket + new Vector2(-5, 3) }, new Color("#6E7A84"));
        c.DrawLine(basket + new Vector2(-4, -1), basket + new Vector2(3, -1), new Color("#2F5E8C"), 2f);
    }

    /// <summary>
    /// The monitor, facing the viewer at the top of its pole. The casing is outlined in the
    /// patient's triage colour (grey when nobody is connected). The ECG and pleth traces run all the
    /// time; the big reading cycles every four seconds through heart rate, SpO₂, blood pressure and
    /// respiratory rate. An alarm bell shows at NEWS2 5 and flashes red at 7 or more.
    /// </summary>
    public static void ObsScreen(CanvasItem c, double gx, double gy, double time, BlueprintHospital.Simulation.Medical.Observations? obs, Color? triage, int news2)
    {
        var mount = IsoDraw.ToScreen(gx, gy) + new Vector2(0, -MonitorHeight);
        var casing = new Rect2(mount + new Vector2(-31, -42), new Vector2(62, 42));
        var screen = new Rect2(casing.Position + new Vector2(4, 4), new Vector2(54, 31));

        // Shadow, triage outline, casing with a darker lower lip, then the glass.
        RoundedRect(c, new Rect2(casing.Position + new Vector2(2, 3), casing.Size), 5, new Color(0, 0, 0, 0.22f));
        var alarm = obs is not null && news2 >= 7;
        var flash = alarm && (int)(time * 3) % 2 == 0;
        if (triage is { } outline)
        {
            RoundedRect(c, casing.Grow(3f), 7.5f, flash ? outline.Lightened(0.45f) : outline);
        }
        else
        {
            RoundedRect(c, casing.Grow(1.2f), 6, new Color("#7D8A95"));
        }

        RoundedRect(c, casing, 5, new Color("#E4E9ED"));
        RoundedRect(c, new Rect2(casing.Position + new Vector2(0, casing.Size.Y - 7), new Vector2(casing.Size.X, 7)), 4, new Color("#C9D2DA"));
        c.DrawRect(new Rect2(casing.Position + new Vector2(4, casing.Size.Y - 8), new Vector2(casing.Size.X - 8, 2)), new Color("#E4E9ED"));
        RoundedRect(c, screen.Grow(1), 2, new Color("#3B454E"));
        c.DrawRect(screen, new Color("#0A1218"));
        c.DrawRect(new Rect2(screen.Position, new Vector2(screen.Size.X, 9)), new Color("#101C24"));

        // Power light and two buttons on the lip.
        var lip = casing.Position + new Vector2(0, casing.Size.Y - 3.5f);
        c.DrawCircle(lip + new Vector2(8, 0), 1.4f, obs is null ? new Color("#E0A030") : new Color("#3EE07A"));
        c.DrawRect(new Rect2(lip + new Vector2(casing.Size.X - 20, -1), new Vector2(5, 2)), new Color("#8E9AA4"));
        c.DrawRect(new Rect2(lip + new Vector2(casing.Size.X - 12, -1), new Vector2(5, 2)), new Color("#8E9AA4"));

        // Mounting arm from the pole into the back of the casing.
        c.DrawRect(new Rect2(mount + new Vector2(-3, -1), new Vector2(6, 3)), new Color("#6E7A84"));

        if (obs is null)
        {
            // Standby: a dim flat line and a waiting message.
            c.DrawLine(screen.Position + new Vector2(4, 17), screen.Position + new Vector2(screen.Size.X - 4, 17), new Color("#24543C"), 1f);
            c.DrawString(ThemeDB.FallbackFont, screen.Position + new Vector2(4, 28), "STANDBY", HorizontalAlignment.Left, -1, 6, new Color("#4F6B7A"));
            return;
        }

        var font = ThemeDB.FallbackFont;
        var green = new Color("#3EE07A");
        var cyan = new Color("#4FC3F7");

        // Traces on the left: ECG on top, pleth below, sweeping in time with the heart rate.
        var beat = 60.0 / Math.Max(30, obs.HeartRate);
        Trace(c, screen.Position + new Vector2(2, 17), 27, time, beat, green, ecg: true);
        Trace(c, screen.Position + new Vector2(2, 28), 27, time, beat, cyan, ecg: false);

        // Header strip: alarm bell and the patient's NEWS2.
        if (news2 >= 5)
        {
            var bell = alarm ? (flash ? new Color("#FF5A4F") : new Color("#7A2420")) : new Color("#F9A825");
            var b = screen.Position + new Vector2(screen.Size.X - 7, 4.5f);
            c.DrawColoredPolygon(new[] { b + new Vector2(-3, 2), b + new Vector2(-2, -2), b + new Vector2(0, -3), b + new Vector2(2, -2), b + new Vector2(3, 2) }, bell);
            c.DrawCircle(b + new Vector2(0, 3), 0.9f, bell);
        }

        var news = $"NEWS2 {news2}";
        var newsWidth = font.GetStringSize(news, HorizontalAlignment.Left, -1, 6).X;
        c.DrawString(font, screen.Position + new Vector2(screen.Size.X - (news2 >= 5 ? 12 : 3) - newsWidth, 7f), news, HorizontalAlignment.Left, -1, 6, news2 >= 5 ? new Color("#FFCC80") : new Color("#8FA3B1"));

        // The big reading on the right, cycling through the vital signs.
        var page = (int)(time / 4) % 4;
        var (label, value, colour) = page switch
        {
            0 => ("HR", obs.HeartRate.ToString(), green),
            1 => ("SpO₂", $"{obs.OxygenSaturation}", cyan),
            2 => ("NIBP", obs.BloodPressure, new Color("#F2F2F2")),
            _ => ("RR", obs.RespiratoryRate.ToString(), new Color("#FFD54F")),
        };

        var right = screen.Position.X + screen.Size.X - 2;
        c.DrawString(font, new Vector2(right - font.GetStringSize(label, HorizontalAlignment.Left, -1, 6).X, screen.Position.Y + 16), label, HorizontalAlignment.Left, -1, 6, new Color(colour, 0.8f));
        var size = value.Length > 4 ? 9 : 13;
        var width = font.GetStringSize(value, HorizontalAlignment.Left, -1, size).X;
        c.DrawString(font, new Vector2(right - width, screen.Position.Y + 28), value, HorizontalAlignment.Left, -1, size, colour);

        // Page dots in the header, showing which reading is up.
        for (var i = 0; i < 4; i++)
        {
            c.DrawCircle(screen.Position + new Vector2(4 + (i * 4), 4.5f), 1f, i == page ? colour : new Color("#33424C"));
        }
    }

    /// <summary>A sweeping trace: an ECG complex (P, QRS, T) or a pleth pulse, one per heart beat.</summary>
    private static void Trace(CanvasItem c, Vector2 origin, float width, double time, double beat, Color colour, bool ecg)
    {
        const int count = 34;
        var points = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            var t = time - ((count - i) * 0.03);
            var phase = (t % beat) / beat;
            var y = ecg
                ? phase switch
                {
                    > 0.02 and < 0.07 => -1.2,
                    > 0.10 and < 0.12 => 1.5,
                    > 0.12 and < 0.15 => -6.5,
                    > 0.15 and < 0.18 => 2.5,
                    > 0.40 and < 0.52 => -1.6,
                    _ => 0.0,
                }
                : -4.5 * Math.Max(0, Math.Sin(phase * Math.PI * 1.4)) + (phase > 0.72 ? -0.8 * Math.Sin((phase - 0.72) * Math.PI * 3.5) : 0);
            points[i] = origin + new Vector2(i * width / (count - 1), (float)y);
        }

        c.DrawPolyline(points, colour, 1.1f, true);

        // The bright write head at the newest end.
        c.DrawCircle(points[^1], 1.1f, colour.Lightened(0.4f));
    }

    /// <summary>A filled rectangle with rounded corners.</summary>
    public static void RoundedRect(CanvasItem c, Rect2 r, float radius, Color colour)
    {
        radius = Math.Min(radius, Math.Min(r.Size.X, r.Size.Y) / 2);
        var points = new Vector2[20];
        var centres = new[]
        {
            r.Position + new Vector2(r.Size.X - radius, radius),
            r.End - new Vector2(radius, radius),
            r.Position + new Vector2(radius, r.Size.Y - radius),
            r.Position + new Vector2(radius, radius),
        };
        var i = 0;
        for (var corner = 0; corner < 4; corner++)
        {
            for (var k = 0; k < 5; k++)
            {
                var a = (-Mathf.Pi / 2) + (corner * Mathf.Pi / 2) + (k * Mathf.Pi / 8);
                points[i++] = centres[corner] + new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
            }
        }

        c.DrawColoredPolygon(points, colour);
    }

    private static Color A(Color colour, float alpha) => new(colour, colour.A * alpha);
}
