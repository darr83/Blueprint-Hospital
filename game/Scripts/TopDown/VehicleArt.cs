using System;
using Godot;

namespace BlueprintHospital.TopDown;

/// <summary>Cars, taxis and ambulances from above. Each is drawn about its centre with its nose along the way it is heading.</summary>
public static class VehicleArt
{
    private static Color C(string hex) => new(hex);

    public static readonly Color[] CarColours =
    {
        C("#C8473D"), C("#3F6FB5"), C("#E8EAED"), C("#4B5560"), C("#2E8C6A"), C("#D9A534"), C("#7A5AA8"), C("#8FA3B3"),
    };

    private static readonly Color Glass = C("#243746");
    private static readonly Color Tyre = C("#1B2026");

    /// <summary>A visitor's car, or a black taxi with its yellow roof light.</summary>
    public static void Car(Pen p, double x, double y, double hx, double hy, Color colour, bool taxi)
    {
        const double w = 3.5, l = 8.2;
        if (taxi)
        {
            colour = C("#22262B");
        }

        p.Push(x, y, Pen.Facing(hx, hy));
        p.Shadow(c => p.Round(-w / 2, -l / 2, w, l, 0.9, c), 0.2, 0.28, 1.4f);
        foreach (var (wx, wy) in new[] { (-w / 2, -2.5), ((w / 2) - 0.22, -2.5), (-w / 2, 2.2), ((w / 2) - 0.22, 2.2) })
        {
            p.Round(wx, wy - 0.6, 0.22, 1.2, 0.08, Tyre);
        }

        p.Solid((-w / 2) + 0.08, -l / 2, w - 0.16, l, 0.95, colour, 0.05);
        p.Round((-w / 2) + 0.3, (-l / 2) + 0.12, w - 0.6, 1.7, 0.6, Pen.Light(colour, 0.12f));
        p.Round((-w / 2) + 0.34, (-l / 2) + 1.9, w - 0.68, 4.3, 0.5, Pen.Dark(colour, 0.08f), Pen.Outline(colour), 0.03);

        // Windscreen and rear window.
        p.Poly(Glass, C("#162430"), 0.03, ((-w / 2) + 0.42, (-l / 2) + 2.9), ((-w / 2) + 0.2, (-l / 2) + 1.85), (0, (-l / 2) + 1.62), ((w / 2) - 0.2, (-l / 2) + 1.85), ((w / 2) - 0.42, (-l / 2) + 2.9));
        p.Poly(Glass, null, 0, ((-w / 2) + 0.45, (l / 2) - 2.25), ((w / 2) - 0.45, (l / 2) - 2.25), ((w / 2) - 0.3, (l / 2) - 1.3), (0, (l / 2) - 1.12), ((-w / 2) + 0.3, (l / 2) - 1.3));
        p.Rect((-w / 2) + 0.7, (-l / 2) + 2.05, 0.9, 0.5, Pen.A("#9FD0E8", 0.35f));

        // Lights and mirrors.
        p.Round((-w / 2) + 0.3, (-l / 2) + 0.06, 0.7, 0.24, 0.1, C("#FFF6C9"));
        p.Round((w / 2) - 1.0, (-l / 2) + 0.06, 0.7, 0.24, 0.1, C("#FFF6C9"));
        p.Round((-w / 2) + 0.3, (l / 2) - 0.26, 0.7, 0.2, 0.08, C("#D8483B"));
        p.Round((w / 2) - 1.0, (l / 2) - 0.26, 0.7, 0.2, 0.08, C("#D8483B"));
        p.Solid((-w / 2) - 0.12, (-l / 2) + 2.3, 0.3, 0.2, 0.06, colour, 0.02);
        p.Solid((w / 2) - 0.18, (-l / 2) + 2.3, 0.3, 0.2, 0.06, colour, 0.02);
        if (taxi)
        {
            p.Round(-0.6, -0.75, 1.2, 0.5, 0.08, C("#F5C518"), C("#8A6F00"), 0.03);
            p.Text("TAXI", 0, -0.5, 10, Tyre);
        }

        p.Pop();
    }

    /// <summary>
    /// An ambulance in UK yellow with green and yellow blocks down its sides. On an emergency run its
    /// blue lights flash left and right with <paramref name="flash"/> (seconds); null leaves them off.
    /// </summary>
    public static void Ambulance(Pen p, double x, double y, double hx, double hy, string callSign, double? flash)
    {
        const double w = 4.4, l = 11.2;
        var yellow = C("#F2D21B");
        var green = C("#2E9E57");
        p.Push(x, y, Pen.Facing(hx, hy));
        p.Shadow(c => p.Round(-w / 2, -l / 2, w, l, 0.5, c), 0.25, 0.34, 1.5f);
        foreach (var (wx, wy) in new[] { ((-w / 2) - 0.05, -3.4), ((w / 2) - 0.2, -3.4), ((-w / 2) - 0.05, 3.2), ((w / 2) - 0.2, 3.2) })
        {
            p.Round(wx, wy - 0.7, 0.25, 1.4, 0.08, Tyre);
        }

        // Cab, then the box body.
        p.Solid((-w / 2) + 0.25, -l / 2, w - 0.5, 3.3, 0.7, yellow, 0.05);
        p.Round(-w / 2, (-l / 2) + 2.9, w, l - 2.9, 0.3, C("#F7F5E8"), Pen.Outline(yellow), 0.05);
        for (var i = 0; i < 8; i++)
        {
            var by = (-l / 2) + 3.1 + (i * 0.94);
            p.Rect((-w / 2) + 0.06, by, 0.4, 0.94, i % 2 == 1 ? yellow : green);
            p.Rect((w / 2) - 0.46, by, 0.4, 0.94, i % 2 == 1 ? green : yellow);
        }

        // Rear chevrons.
        p.Rect((-w / 2) + 0.2, (l / 2) - 0.45, w - 0.4, 0.38, yellow);
        for (var i = 0; i < 6; i++)
        {
            var cx = (-w / 2) + 0.3 + (i * 0.66);
            p.Poly(C("#D8483B"), null, 0, (cx, (l / 2) - 0.07), (cx + 0.3, (l / 2) - 0.45), (cx + 0.6, (l / 2) - 0.45), (cx + 0.3, (l / 2) - 0.07));
        }

        p.Poly(Glass, C("#162430"), 0.03, ((-w / 2) + 0.5, (-l / 2) + 2.75), ((-w / 2) + 0.55, (-l / 2) + 1.5), (0, (-l / 2) + 1.2), ((w / 2) - 0.55, (-l / 2) + 1.5), ((w / 2) - 0.5, (-l / 2) + 2.75));
        p.Rect((-w / 2) + 0.9, (-l / 2) + 1.6, 1.0, 0.6, Pen.A("#9FD0E8", 0.35f));

        // Light bars: front and rear.
        var blue = C("#3D8BF2");
        var lit = C("#BFE0FF");
        var left = flash is { } t && Mathf.PosMod((float)t, 0.5f) < 0.25f;
        var right = flash is not null && !left;
        p.Round((-w / 2) + 0.5, (-l / 2) + 2.95, w - 1.0, 0.5, 0.12, C("#2B333C"));
        p.Round((-w / 2) + 0.58, (-l / 2) + 3.03, 1.2, 0.34, 0.08, left ? lit : blue);
        p.Round((w / 2) - 1.78, (-l / 2) + 3.03, 1.2, 0.34, 0.08, right ? lit : blue);
        p.Round((-w / 2) + 0.7, (l / 2) - 1.1, w - 1.4, 0.4, 0.1, C("#2B333C"));
        p.Round((-w / 2) + 0.78, (l / 2) - 1.03, 0.9, 0.26, 0.06, right ? lit : blue);
        p.Round((w / 2) - 1.68, (l / 2) - 1.03, 0.9, 0.26, 0.06, left ? lit : blue);
        if (flash is not null)
        {
            p.Circle(left ? (-w / 2) + 1.2 : (w / 2) - 1.2, (-l / 2) + 3.2, 1.5, Pen.A("#7FB8FF", 0.22f));
        }

        p.Round(-0.9, -1.5, 1.8, 1.8, 0.2, C("#C9D1D8"), C("#8E99A5"), 0.03);
        p.Text(callSign, 0, 1.35, 15, C("#D8483B"));
        p.Rect(-0.16, 2.6, 0.32, 1.1, green);
        p.Rect(-0.55, 2.99, 1.1, 0.32, green);
        p.Round((-w / 2) + 0.45, (-l / 2) + 0.05, 0.8, 0.24, 0.1, C("#FFF6C9"));
        p.Round((w / 2) - 1.25, (-l / 2) + 0.05, 0.8, 0.24, 0.1, C("#FFF6C9"));
        p.Pop();
    }

    /// <summary>
    /// The air ambulance from above, nose along (<paramref name="hx"/>, <paramref name="hy"/>): a red pod with a
    /// yellow band, a long tail boom with its shrouded tail rotor, skids, and the four main rotor blades turned to
    /// <paramref name="rotorAngle"/> (a blurred disc once they are spinning fast). Its shadow falls further off
    /// and it is drawn a little bigger the higher it is (<paramref name="altitude"/>, metres).
    /// </summary>
    public static void Helicopter(Pen p, double x, double y, double hx, double hy, double altitude, double rotorAngle, double rotorSpeed)
    {
        var red = C("#C8202B");
        var yellow = C("#F2D21B");
        var lift = Math.Clamp(altitude / HelicopterCruiseMetres, 0, 1);
        var facing = Pen.Facing(hx, hy);

        // The shadow on the ground, falling away as it climbs.
        var shadow = new Color(0, 0, 0, (float)(0.28 - (lift * 0.14)));
        p.Push(x + (altitude * 0.12), y + (altitude * 0.16), facing);
        p.Round(-1.6, -4.3, 3.2, 8.6, 1.4, shadow);
        p.Round(-0.45, 3, 0.9, 8.5, 0.4, shadow);
        if (rotorSpeed > 0.05)
        {
            p.Circle(0, -0.5, 10, new Color(0, 0, 0, (float)(0.08 * rotorSpeed)));
        }

        p.Pop();

        p.Push(x, y, facing, 1 + (lift * 0.25));

        // Skids.
        foreach (var sx in new[] { -1.9, 1.9 })
        {
            p.Round(sx - 0.18, -3.4, 0.36, 6.8, 0.18, C("#3A3F45"));
            p.Line(sx, -1.8, sx * 0.55, -1.8, C("#3A3F45"), 0.22);
            p.Line(sx, 1.6, sx * 0.55, 1.6, C("#3A3F45"), 0.22);
        }

        // Tail boom, fin and the shrouded tail rotor.
        p.Poly(red, Pen.Outline(red), 0.04, (-0.6, 2.6), (0.6, 2.6), (0.32, 10.6), (-0.32, 10.6));
        p.Round(-1.7, 8.0, 3.4, 0.7, 0.3, red, Pen.Outline(red), 0.04);
        p.Round(-0.35, 9.9, 0.7, 2.2, 0.3, Pen.Dark(red, 0.15f), Pen.Outline(red), 0.04);
        p.Rect(-0.12, 3.5, 0.24, 4.2, yellow);

        // The pod: red with a yellow band, dark glazing at the nose, the engine cowl on top.
        p.Shadow(c => p.Round(-1.6, -4.3, 3.2, 8.6, 1.4, c), 0.08, 0.1, 1.2f);
        p.Round(-1.6, -4.3, 3.2, 8.6, 1.4, red, Pen.Outline(red), 0.05);
        p.Rect(-1.55, -0.2, 3.1, 0.9, yellow);
        p.Round(-1.3, -4.15, 2.6, 2.4, 1.1, C("#22303C"), C("#111A22"), 0.04);
        p.Round(-0.95, -3.85, 0.8, 1.2, 0.4, Pen.A("#9FD0E8", 0.35f));
        p.Round(-0.8, 0.9, 1.6, 2.6, 0.5, Pen.Dark(red, 0.12f), Pen.Outline(red), 0.04);
        p.Text("AIR AMBULANCE", 0, 2.4, 9, C("#FFFFFF"), rot: Math.PI / 2);

        // Main rotor: four blades on the hub, a pale blur when spinning fast.
        if (rotorSpeed > 0.6)
        {
            p.Circle(0, -0.5, 10, new Color(0.15f, 0.17f, 0.2f, (float)(0.16 * rotorSpeed)), new Color(0.15f, 0.17f, 0.2f, 0.25f), 0.05);
        }

        var blade = new Color(0.16f, 0.18f, 0.21f, rotorSpeed > 0.6 ? 0.45f : 1f);
        for (var i = 0; i < 4; i++)
        {
            var a = rotorAngle + (i * Math.PI / 2);
            p.Line(Math.Sin(a) * 0.6, -0.5 - (Math.Cos(a) * 0.6), Math.Sin(a) * 10, -0.5 - (Math.Cos(a) * 10), blade, 0.55);
        }

        p.Circle(0, -0.5, 0.65, C("#5A6068"), C("#2A2E33"), 0.04);
        p.Pop();
    }

    /// <summary>Cruising height the helicopter's look scales with (matches the simulation).</summary>
    private const double HelicopterCruiseMetres = 40;
}
