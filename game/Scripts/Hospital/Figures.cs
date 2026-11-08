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
        var gown = new Color("#9CC3E6");

        // Lying flat on their back: head on the pillow, shoulders in a hospital gown, and the
        // rest of the body under the blanket from the chest down to the feet.
        IsoDraw.Box(c, gx - 0.55, gy - 1.2, 1.1, 0.5, 5, gown, lift);
        IsoDraw.Box(c, gx - 0.66, gy - 0.75, 1.32, 2.75, 5, blanket, lift);

        // The shape of the body and feet under the blanket.
        IsoDraw.Box(c, gx - 0.4, gy - 0.7, 0.8, 1.6, 3, blanket.Lightened(0.06f), lift + 5);
        IsoDraw.Box(c, gx - 0.4, gy + 1.45, 0.8, 0.45, 4, blanket.Lightened(0.06f), lift + 5);

        // Turned-down white sheet across the chest.
        IsoDraw.Box(c, gx - 0.68, gy - 0.82, 1.36, 0.22, 6, new Color("#F7F9FA"), lift);

        // Head on the pillow, drawn last so nothing covers the face.
        var head = IsoDraw.ToScreen(gx, gy - 1.5) + up + new Vector2(0, -9);
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

    /// <summary>Height of the obs monitor screen's bottom edge above the floor, in pixels.</summary>
    public const float MonitorHeight = 52f;

    /// <summary>An observations monitor's stand: weighted base and pole. The screen is drawn by <see cref="ObsScreen"/>.</summary>
    public static void MonitorStand(CanvasItem c, double gx, double gy)
    {
        IsoDraw.Box(c, gx - 0.3, gy - 0.3, 0.6, 0.6, 3, new Color("#4A545D"));
        var basePoint = IsoDraw.ToScreen(gx, gy);
        c.DrawLine(basePoint, basePoint + new Vector2(0, -MonitorHeight), new Color("#7D8891"), 2.5f);
    }

    /// <summary>
    /// The monitor screen, facing the viewer at the top of its pole. It cycles every five seconds
    /// between heart rate (with an ECG trace), oxygen saturation (with a pleth wave) and blood
    /// pressure. The frame turns amber at NEWS2 5 and flashes red at 7 or more.
    /// </summary>
    public static void ObsScreen(CanvasItem c, double gx, double gy, double time, int? heartRate, int? spo2, string? bloodPressure, int news2)
    {
        var top = IsoDraw.ToScreen(gx, gy) + new Vector2(0, -MonitorHeight);
        var screen = new Rect2(top + new Vector2(-24, -32), new Vector2(48, 32));
        var alarm = news2 >= 7 ? ((int)(time * 3) % 2 == 0 ? new Color("#E53935") : new Color("#5A1A1A"))
            : news2 >= 5 ? new Color("#F9A825")
            : new Color("#3A4650");
        c.DrawRect(new Rect2(screen.Position + new Vector2(2, 3), screen.Size), new Color(0, 0, 0, 0.2f));
        c.DrawRect(screen.Grow(2.5f), alarm);
        c.DrawRect(screen, new Color("#0E1A22"));

        if (heartRate is not { } hr)
        {
            // Standby: a flat line.
            c.DrawLine(screen.Position + new Vector2(4, 18), screen.Position + new Vector2(44, 18), new Color("#2C5E44"), 1f);
            return;
        }

        var font = ThemeDB.FallbackFont;
        var page = (int)(time / 5) % 3;
        var (label, value, colour) = page switch
        {
            0 => ("HR", hr.ToString(), new Color("#3EE07A")),
            1 => ("SpO₂", spo2 is { } s ? $"{s}%" : "--", new Color("#4FC3F7")),
            _ => ("BP", bloodPressure ?? "--", new Color("#F2F2F2")),
        };

        // Waveform across the top half: ECG on the heart rate page, pleth on the SpO2 page.
        if (page < 2)
        {
            var beat = 60.0 / Math.Max(30, hr);
            var points = new Vector2[40];
            for (var i = 0; i < points.Length; i++)
            {
                var t = time - ((points.Length - i) * 0.03);
                var phase = (t % beat) / beat;
                var y = page == 0
                    ? phase switch
                    {
                        < 0.04 => -1.5,
                        < 0.08 => 2.5,
                        < 0.12 => -7.0,
                        < 0.16 => 3.5,
                        > 0.45 and < 0.6 => -1.5,
                        _ => 0.0,
                    }
                    : -5 * Math.Max(0, Math.Sin(phase * Math.PI * 1.6)) + 1;
                points[i] = screen.Position + new Vector2(3 + (i * 1.05f), 11 + (float)y);
            }

            c.DrawPolyline(points, colour, 1.2f);
        }

        c.DrawString(font, screen.Position + new Vector2(3, 29), label, HorizontalAlignment.Left, -1, 8, new Color(colour, 0.8f));
        var size = value.Length > 4 ? 11 : 13;
        var width = font.GetStringSize(value, HorizontalAlignment.Left, -1, size).X;
        c.DrawString(font, screen.Position + new Vector2(45 - width, 30), value, HorizontalAlignment.Left, -1, size, colour);
    }

    private static Color A(Color colour, float alpha) => new(colour, colour.A * alpha);
}
