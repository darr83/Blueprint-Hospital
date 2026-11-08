using System;
using Godot;

namespace BlueprintHospital.Hospital;

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
    bool LongHair = false);

/// <summary>
/// Flat-shaded people and clinical equipment in the game's isometric style. People are drawn
/// front-on at a fixed size (about 1.8 m tall); equipment is drawn with <see cref="IsoDraw"/>.
/// All positions are in world (screen) pixels.
/// </summary>
public static class Figures
{
    public static readonly Color Shoes = new("#2B2F33");
    public static readonly Color Outline = new("#1D2B36");

    /// <summary>A standing (or walking) person whose feet are at <paramref name="foot"/>.</summary>
    public static void Standing(CanvasItem c, Vector2 foot, PersonLook look, double time, bool walking, int seed, float alpha = 1f)
    {
        c.DrawColoredPolygon(IsoDraw.FloorEllipse(foot, 10), new Color(0, 0, 0, 0.2f * alpha));
        var swing = walking ? (float)Math.Sin((time * 9) + seed) * 3.2f : 0f;

        // Legs and shoes.
        var trousers = A(look.Trousers, alpha);
        c.DrawLine(foot + new Vector2(-3.5f, -21), foot + new Vector2(-3.5f + swing, -3), trousers, 5.5f);
        c.DrawLine(foot + new Vector2(3.5f, -21), foot + new Vector2(3.5f - swing, -3), trousers, 5.5f);
        c.DrawCircle(foot + new Vector2(-3.5f + swing, -2), 3f, A(Shoes, alpha));
        c.DrawCircle(foot + new Vector2(3.5f - swing, -2), 3f, A(Shoes, alpha));

        Body(c, foot, look, -swing, alpha, drop: 0);
    }

    /// <summary>A person sitting on a chair whose seat centre is at <paramref name="foot"/>.</summary>
    public static void Seated(CanvasItem c, Vector2 foot, PersonLook look, float alpha = 1f)
    {
        var trousers = A(look.Trousers, alpha);

        // Thighs forward (towards the viewer), shins down to the floor.
        c.DrawLine(foot + new Vector2(-3.5f, -15), foot + new Vector2(-4.5f, -10), trousers, 5.5f);
        c.DrawLine(foot + new Vector2(3.5f, -15), foot + new Vector2(4.5f, -10), trousers, 5.5f);
        c.DrawLine(foot + new Vector2(-4.5f, -10), foot + new Vector2(-4.5f, 2), trousers, 5f);
        c.DrawLine(foot + new Vector2(4.5f, -10), foot + new Vector2(4.5f, 2), trousers, 5f);
        c.DrawCircle(foot + new Vector2(-4.5f, 3), 3f, A(Shoes, alpha));
        c.DrawCircle(foot + new Vector2(4.5f, 3), 3f, A(Shoes, alpha));

        Body(c, foot, look, 0, alpha, drop: 7);
    }

    private static void Body(CanvasItem c, Vector2 foot, PersonLook look, float armSwing, float alpha, float drop)
    {
        Vector2 P(float x, float y) => foot + new Vector2(x, y + drop);
        var top = A(look.Top, alpha);
        var skin = A(look.Skin, alpha);

        // Torso; a tunic comes down over the hips.
        var hem = look.Tunic ? -16f : -19f;
        var torso = new[] { P(-9, -37), P(-6.5f, -41), P(6.5f, -41), P(9, -37), P(8.5f, hem), P(-8.5f, hem) };
        c.DrawColoredPolygon(torso, top);
        c.DrawColoredPolygon(new[] { P(1, -41), P(6.5f, -41), P(9, -37), P(8.5f, hem), P(3, hem) }, A(look.Top.Darkened(0.12f), alpha));

        // Neckline: scrubs and tunics have a V-neck.
        c.DrawColoredPolygon(new[] { P(-3, -41), P(3, -41), P(0, -36) }, skin);
        if (look.Piping is { } piping)
        {
            var trim = A(piping, alpha);
            c.DrawPolyline(new[] { P(-3.5f, -41), P(0, -35.5f), P(3.5f, -41) }, trim, 1.4f);
            c.DrawLine(P(-8.5f, hem), P(8.5f, hem), trim, 1.4f);
            c.DrawLine(P(-8.6f, -30), P(-8.4f, hem), trim, 1f);
            c.DrawLine(P(8.6f, -30), P(8.4f, hem), trim, 1f);
        }

        // Arms: short sleeves and bare forearms (bare below the elbows), unless in own clothes.
        foreach (var side in new[] { -1f, 1f })
        {
            var shoulder = P(side * 9, -38);
            var elbow = P(side * 10.5f, -30 + (armSwing * side * 0.3f));
            var hand = P(side * 10.5f + (armSwing * side * 0.6f), -22);
            c.DrawLine(shoulder, elbow, top, 5f);
            c.DrawLine(elbow, hand, look.LongSleeves ? top : skin, 4f);
            c.DrawCircle(hand, 2.4f, skin);
        }

        // Neck and head.
        c.DrawRect(new Rect2(P(-2.5f, -45), new Vector2(5, 5)), skin);
        var head = P(0, -50.5f);
        if (look.LongHair)
        {
            c.DrawRect(new Rect2(head + new Vector2(-7.5f, -1), new Vector2(15, 9)), A(look.Hair, alpha));
        }

        c.DrawCircle(head, 7.5f, skin);
        c.DrawColoredPolygon(HairCap(head), A(look.Hair, alpha));

        if (look.Stethoscope)
        {
            var steth = A(new Color("#2E3A44"), alpha);
            c.DrawPolyline(new[] { P(-4.5f, -41), P(-3.5f, -35), P(0, -32), P(3.5f, -35), P(4.5f, -41) }, steth, 1.5f);
            c.DrawCircle(P(0, -31), 1.9f, A(new Color("#B9C3CB"), alpha));
        }

        if (look.Lanyard)
        {
            c.DrawPolyline(new[] { P(-2, -41), P(1.5f, -30), P(2.5f, -41) }, A(new Color("#005EB8"), alpha), 1f);
            c.DrawRect(new Rect2(P(0, -30), new Vector2(4, 5)), A(Colors.White, alpha));
        }
    }

    private static Vector2[] HairCap(Vector2 head)
    {
        // Upper part of the head plus a short fringe.
        var points = new Vector2[13];
        for (var i = 0; i <= 10; i++)
        {
            var a = Mathf.Pi + (i * Mathf.Pi / 10);
            points[i] = head + new Vector2(Mathf.Cos(a) * 7.9f, Mathf.Sin(a) * 7.9f - 0.5f);
        }

        points[11] = head + new Vector2(4, -2);
        points[12] = head + new Vector2(-6, -1.5f);
        return points;
    }

    /// <summary>
    /// A patient lying on their back under a blanket, head at the north end. (gx, gy) is the
    /// centre of the mattress in grid tiles; <paramref name="lift"/> is the mattress height in pixels.
    /// </summary>
    public static void Lying(CanvasItem c, double gx, double gy, PersonLook look, Color blanket, float lift)
    {
        var up = new Vector2(0, -lift);

        // Shoulders, then the blanket over the body with a turned-down white edge and the shape of the feet.
        var shoulders = IsoDraw.ToScreen(gx, gy - 0.9) + up;
        c.DrawLine(shoulders + new Vector2(-10, -5), shoulders + new Vector2(10, -5), look.Top, 7f);
        IsoDraw.Box(c, gx - 0.62, gy - 0.75, 1.24, 2.65, 7, blanket, lift);
        IsoDraw.Box(c, gx - 0.64, gy - 0.8, 1.28, 0.25, 8, new Color("#F7F9FA"), lift);
        IsoDraw.Box(c, gx - 0.45, gy + 1.3, 0.9, 0.5, 4, blanket.Lightened(0.08f), lift + 7);

        // An arm resting on top of the blanket, along the body.
        var armTop = new Vector2(0, -lift - 9);
        var shoulder = IsoDraw.ToScreen(gx + 0.42, gy - 0.6) + armTop;
        var elbow = IsoDraw.ToScreen(gx + 0.46, gy + 0.05) + armTop;
        var hand = IsoDraw.ToScreen(gx + 0.4, gy + 0.6) + armTop;
        c.DrawLine(shoulder, elbow, look.Top, 4.5f);
        c.DrawLine(elbow, hand, look.Skin, 3.5f);
        c.DrawCircle(hand, 2.3f, look.Skin);

        // Head on the pillow, drawn last so it sits above the sheet.
        var head = IsoDraw.ToScreen(gx, gy - 1.5) + up + new Vector2(0, -8);
        if (look.LongHair)
        {
            c.DrawCircle(head + new Vector2(0, -3), 8.5f, look.Hair);
        }

        c.DrawCircle(head, 7f, look.Skin);
        c.DrawColoredPolygon(HairCap(head), look.Hair);
    }

    /// <summary>A hospital bed (about 0.8 m by 2.1 m) with its head against the north. Returns the mattress height.</summary>
    public static float Bed(CanvasItem c, double gx, double gy, bool made)
    {
        const float frameHeight = 12f;
        const float mattress = 6f;
        var x = gx - 0.8;
        var y = gy - 2.1;

        // Wheels, frame, mattress, pillow and headboard.
        foreach (var (wx, wy) in new[] { (x + 0.1, y + 0.1), (x + 1.4, y + 0.1), (x + 0.1, y + 4), (x + 1.4, y + 4) })
        {
            IsoDraw.Box(c, wx, wy, 0.15, 0.15, 4, new Color("#3A4148"));
        }

        IsoDraw.Box(c, x, y, 1.6, 4.2, frameHeight - 4, new Color("#AEB8C1"), 4);
        IsoDraw.Box(c, x + 0.05, y + 0.05, 1.5, 4.1, mattress, new Color("#EEF2F5"), frameHeight);
        IsoDraw.Box(c, x - 0.05, y - 0.1, 1.7, 0.25, 26, new Color("#8F9BA6"));
        IsoDraw.Box(c, x + 0.25, y + 0.25, 1.1, 0.8, 4, Colors.White, frameHeight + mattress);

        if (made)
        {
            // Empty bed: a folded blanket at the foot.
            IsoDraw.Box(c, x + 0.15, y + 3.2, 1.3, 0.7, 3, new Color("#BFD3E6"), frameHeight + mattress);
        }

        return frameHeight + mattress;
    }

    /// <summary>
    /// An observations monitor on a stand, screen facing the viewer. Shows a live ECG trace,
    /// heart rate and SpO₂ when connected; the frame turns amber or flashes red for high NEWS2.
    /// </summary>
    public static void ObsMonitor(CanvasItem c, double gx, double gy, double time, int? heartRate, int? spo2, int news2)
    {
        IsoDraw.Box(c, gx - 0.3, gy - 0.3, 0.6, 0.6, 3, new Color("#4A545D"));
        var basePoint = IsoDraw.ToScreen(gx, gy);
        c.DrawLine(basePoint, basePoint + new Vector2(0, -46), new Color("#7D8891"), 2.5f);

        var screen = new Rect2(basePoint + new Vector2(-15, -66), new Vector2(30, 21));
        var alarm = news2 >= 7 ? ((int)(time * 3) % 2 == 0 ? new Color("#E53935") : new Color("#5A1A1A"))
            : news2 >= 5 ? new Color("#F9A825")
            : new Color("#3A4650");
        c.DrawRect(screen.Grow(2), alarm);
        c.DrawRect(screen, new Color("#0E1A22"));

        if (heartRate is not { } hr)
        {
            // Standby: a flat line.
            c.DrawLine(screen.Position + new Vector2(3, 12), screen.Position + new Vector2(27, 12), new Color("#2C5E44"), 1f);
            return;
        }

        // ECG: a QRS spike every beat, scrolling left to right.
        var beat = 60.0 / Math.Max(30, hr);
        var points = new Vector2[24];
        for (var i = 0; i < points.Length; i++)
        {
            var t = time - ((points.Length - i) * 0.04);
            var phase = (t % beat) / beat;
            var yOffset = phase switch
            {
                < 0.04 => -1.5,
                < 0.08 => 3.0,
                < 0.12 => -7.5,
                < 0.16 => 4.0,
                > 0.45 and < 0.6 => -1.5,
                _ => 0.0,
            };
            points[i] = screen.Position + new Vector2(2 + (i * 0.75f), 9 + (float)yOffset);
        }

        c.DrawPolyline(points, new Color("#3EE07A"), 1.2f);
        var font = ThemeDB.FallbackFont;
        c.DrawString(font, screen.Position + new Vector2(20, 9), hr.ToString(), HorizontalAlignment.Left, -1, 8, new Color("#3EE07A"));
        if (spo2 is { } sat)
        {
            c.DrawString(font, screen.Position + new Vector2(20, 19), sat.ToString(), HorizontalAlignment.Left, -1, 8, new Color("#4FC3F7"));
        }
    }

    private static Color A(Color colour, float alpha) => new(colour, colour.A * alpha);
}
