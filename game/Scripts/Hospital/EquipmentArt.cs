using System;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Detailed drawings of medical equipment that used to be plain boxes: the ventilator, the
/// anaesthetic machine, imaging (C-arm, X-ray tube, CT gantry, lead screen), scanners and stacks
/// on carts. Bodies are isometric blocks; screens and control panels face the viewer, like the obs
/// monitor, so they read from every view angle. Each item is drawn whole, once, by the furniture
/// slice under its centre.
/// </summary>
public static class EquipmentArt
{
    private static readonly Color Castor = new("#2B3036");
    private static readonly Color Glass = new("#0A1218");
    private static readonly Color Casing = new("#39434C");
    private static readonly Color Steel = new("#B9C3CB");
    private static readonly Color DarkSteel = new("#6E7A84");
    private static readonly Color Hose = new("#6FA8DC");

    /// <summary>True for the items drawn here.</summary>
    public static bool Draws(string id) => id is "ventilator" or "anaesthetic_machine" or "ultrasound_machine" or "eeg_machine"
        or "endoscopy_stack" or "c_arm" or "xray_tube" or "ct_gantry" or "lead_screen";

    public static void Draw(CanvasItem c, string id, (double X, double Y, double W, double L) whole, float height, Color colour)
    {
        switch (id)
        {
            case "ventilator":
                Ventilator(c, whole, height, colour);
                break;
            case "anaesthetic_machine":
                AnaestheticMachine(c, whole, height, colour);
                break;
            case "ultrasound_machine":
                Ultrasound(c, whole, height, colour);
                break;
            case "eeg_machine":
                Eeg(c, whole, height, colour);
                break;
            case "endoscopy_stack":
                EndoscopyStack(c, whole, height, colour);
                break;
            case "c_arm":
                CArm(c, whole, height, colour);
                break;
            case "xray_tube":
                XrayTube(c, whole, height, colour);
                break;
            case "ct_gantry":
                CtGantry(c, whole, height, colour);
                break;
            case "lead_screen":
                LeadScreen(c, whole, height, colour);
                break;
        }
    }

    // ------------------------------------------------------------------ helpers

    private static Vector2 S(double x, double y, float h = 0) => IsoDraw.ToScreen(x, y) + new Vector2(0, -h);

    /// <summary>A block with a lighter top, light from the upper left, and a fine rim.</summary>
    private static void Block(CanvasItem c, double x, double y, double w, double l, float h, Color colour, float lift = 0) =>
        IsoDraw.Prism(c, x, y, w, l, h, colour.Lightened(0.1f), colour.Darkened(0.1f), lift: lift, rim: colour.Lightened(0.32f));

    /// <summary>A soft floor shadow under a footprint.</summary>
    private static void Shadow(CanvasItem c, double x, double y, double w, double l, float alpha = 0.14f)
    {
        var p = new[] { S(x, y), S(x + w, y), S(x + w, y + l), S(x, y + l) };
        var mid = (p[0] + p[1] + p[2] + p[3]) / 4;
        c.DrawColoredPolygon(Array.ConvertAll(p, q => q + ((q - mid) * 0.1f) + new Vector2(2, 2)), new Color(0, 0, 0, alpha));
    }

    /// <summary>Four castors at the corners of a footprint.</summary>
    private static void Castors(CanvasItem c, double x, double y, double w, double l)
    {
        foreach (var (px, py) in new[] { (x + 0.1, y + 0.1), (x + w - 0.1, y + 0.1), (x + w - 0.1, y + l - 0.1), (x + 0.1, y + l - 0.1) })
        {
            var p = S(px, py);
            c.DrawLine(p + new Vector2(0, -4), p + new Vector2(0, -2), DarkSteel, 1.2f);
            c.DrawCircle(p + new Vector2(0, -1.6f), 1.8f, Castor);
        }
    }

    /// <summary>A screen facing the viewer, its bottom edge centred on <paramref name="bottom"/>. Returns the glass.</summary>
    private static Rect2 Screen(CanvasItem c, Vector2 bottom, float w, float h, Color? casing = null)
    {
        var box = new Rect2(bottom + new Vector2(-w / 2, -h), new Vector2(w, h));
        Figures.RoundedRect(c, new Rect2(box.Position + new Vector2(1, 1.5f), box.Size), 2, new Color(0, 0, 0, 0.2f));
        Figures.RoundedRect(c, box, 2, casing ?? Casing);
        var glass = box.Grow(-1.6f);
        c.DrawRect(glass, Glass);
        return glass;
    }

    /// <summary>A waveform across a strip of a screen.</summary>
    private static void Wave(CanvasItem c, Vector2 origin, float width, Func<float, float> shape, Color colour, float thickness = 0.9f)
    {
        const int n = 24;
        var points = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)(n - 1);
            points[i] = origin + new Vector2(t * width, shape(t));
        }

        c.DrawPolyline(points, colour, thickness, true);
    }

    /// <summary>Corrugated breathing tubing along a curve, in alternating bands.</summary>
    private static void Tubing(CanvasItem c, Vector2[] path, Color colour, float width = 2.6f)
    {
        for (var i = 0; i < path.Length - 1; i++)
        {
            c.DrawLine(path[i], path[i + 1], i % 2 == 0 ? colour : colour.Darkened(0.18f), width, true);
        }
    }

    private static Vector2[] Curve(Vector2 a, Vector2 control, Vector2 b, int n = 14)
    {
        var points = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)(n - 1);
            points[i] = a.Lerp(control, t).Lerp(control.Lerp(b, t), t);
        }

        return points;
    }

    /// <summary>The item's footprint shrunk or grown to a given size about its centre.</summary>
    private static (double X, double Y, double W, double L) Centred((double X, double Y, double W, double L) whole, double w, double l)
    {
        var (cx, cy) = (whole.X + (whole.W / 2), whole.Y + (whole.L / 2));
        return (cx - (w / 2), cy - (l / 2), w, l);
    }

    /// <summary>A five-spoke wheeled base, like an office chair's.</summary>
    private static void SpokeBase(CanvasItem c, Vector2 at, float radius)
    {
        c.DrawColoredPolygon(IsoDraw.FloorEllipse(at + new Vector2(1, 1), radius + 2), new Color(0, 0, 0, 0.14f));
        for (var i = 0; i < 5; i++)
        {
            var a = (i * Mathf.Tau / 5) + 0.3f;
            var end = at + new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius / 2);
            c.DrawLine(at + new Vector2(0, -2), end + new Vector2(0, -1), new Color("#56616B"), 2.2f, true);
            c.DrawCircle(end, 1.7f, Castor);
        }
    }

    // ------------------------------------------------------------------ items

    /// <summary>An ICU ventilator: a cart on a wheeled base, a touch screen with pressure and flow waves, a humidifier and blue breathing tubing.</summary>
    private static void Ventilator(CanvasItem c, (double X, double Y, double W, double L) whole, float height, Color colour)
    {
        var (cx, cy) = (whole.X + (whole.W / 2), whole.Y + (whole.L / 2));
        var foot = S(cx, cy);
        SpokeBase(c, foot, 10);
        c.DrawLine(foot + new Vector2(0, -2), foot + new Vector2(0, -12), DarkSteel, 3f);

        // The ventilator unit, with a grille and the humidifier chamber on its side.
        var body = new Color("#E3E9EE");
        Block(c, cx - 0.32, cy - 0.32, 0.64, 0.64, 12, body, 12);
        foreach (var (a, b, _, left) in IsoDraw.VisibleFaces(cx - 0.32, cy - 0.32, 0.64, 0.64))
        {
            for (var i = 0; i < 3; i++)
            {
                var v = 15 + (i * 2.5f);
                c.DrawLine(a.Lerp(b, 0.2f) + new Vector2(0, -v), a.Lerp(b, 0.5f) + new Vector2(0, -v), body.Darkened(0.3f), 0.8f);
            }

            if (left)
            {
                // Port where the tubing connects, in the unit's accent colour.
                c.DrawCircle(a.Lerp(b, 0.75f) + new Vector2(0, -18), 1.6f, colour.Darkened(0.2f));
            }
        }

        var humidifier = foot + new Vector2(-12, -15);
        Figures.RoundedRect(c, new Rect2(humidifier + new Vector2(-2.5f, -7), new Vector2(5, 7)), 1.5f, new Color("#D6ECF5", 0.95f));
        c.DrawRect(new Rect2(humidifier + new Vector2(-2, -3), new Vector2(4, 2.6f)), new Color("#7FC3E0"));
        c.DrawRect(new Rect2(humidifier + new Vector2(-3, -9), new Vector2(6, 2)), DarkSteel);

        // Breathing circuit: out of the unit, round in a loop and back, as it would go to the patient.
        // Breathing circuit: from the humidifier, looping down and back up to the holder arm.
        var arm = foot + new Vector2(-17, -30);
        c.DrawLine(foot + new Vector2(-4, -22), arm, DarkSteel, 1.2f);
        Tubing(c, Curve(humidifier + new Vector2(0, -8), humidifier + new Vector2(-14, 18), arm), Hose, 2.2f);
        c.DrawCircle(arm, 1.8f, new Color("#E8EEF2"));

        // Screen on top: pressure and flow waves, with the main settings.
        c.DrawLine(foot + new Vector2(0, -24), foot + new Vector2(0, -height + 14), DarkSteel, 2f);
        var glass = Screen(c, foot + new Vector2(2, -height + 15), 26, 19, new Color("#E3E9EE"));
        c.DrawRect(new Rect2(glass.Position, new Vector2(glass.Size.X, 3)), new Color(colour, 0.7f));
        Wave(c, glass.Position + new Vector2(1.5f, 8.5f), 14, t => -3.2f * Mathf.Max(0, Mathf.Sin(t * Mathf.Tau * 1.5f)), new Color("#FFD54F"));
        Wave(c, glass.Position + new Vector2(1.5f, 13.5f), 14, t => -2.4f * Mathf.Sin(t * Mathf.Tau * 1.5f), new Color("#4FC3F7"));
        for (var i = 0; i < 3; i++)
        {
            c.DrawRect(new Rect2(glass.Position + new Vector2(17.5f, 4.5f + (i * 3.8f)), new Vector2(4.5f, 2.2f)), i == 0 ? new Color("#3EE07A") : new Color("#8FA3B1"));
        }
    }

    /// <summary>
    /// An anaesthetic workstation: a drawer cart with a worktop, the gas flowmeters and a yellow
    /// vaporiser, the bellows in their clear dome, a monitor on top and the blue breathing circuit.
    /// </summary>
    private static void AnaestheticMachine(CanvasItem c, (double X, double Y, double W, double L) whole, float height, Color colour)
    {
        var (x, y, w, l) = Centred(whole, Math.Max(whole.W, 1.1), Math.Max(whole.L, 1.0));
        var (cx, cy) = (x + (w / 2), y + (l / 2));
        Shadow(c, x, y, w, l);
        Castors(c, x, y, w, l);

        // Drawer cart and worktop.
        Block(c, x + 0.05, y + 0.05, w - 0.1, l - 0.1, 19, colour, 3);
        foreach (var (a, b, _, _) in IsoDraw.VisibleFaces(x + 0.05, y + 0.05, w - 0.1, l - 0.1))
        {
            for (var i = 1; i < 3; i++)
            {
                var v = 3 + (19 * i / 3f);
                c.DrawLine(a.Lerp(b, 0.06f) + new Vector2(0, -v), a.Lerp(b, 0.94f) + new Vector2(0, -v), colour.Darkened(0.4f), 0.9f);
                c.DrawLine(a.Lerp(b, 0.4f) + new Vector2(0, -v - 3), a.Lerp(b, 0.6f) + new Vector2(0, -v - 3), colour.Lightened(0.45f), 1.3f);
            }
        }

        Block(c, x - 0.02, y - 0.02, w + 0.04, l + 0.04, 2, new Color("#D9DFE4"), 22);

        // The tower at the back with the flowmeters on its face.
        var top = S(cx, cy, 24);
        Figures.RoundedRect(c, new Rect2(top + new Vector2(-10, -20), new Vector2(20, 20)), 2, colour.Lightened(0.15f));
        Figures.RoundedRect(c, new Rect2(top + new Vector2(-8, -18), new Vector2(11, 13)), 1, new Color("#E9EEF1"));
        var gases = new[] { new Color("#FFFFFF"), new Color("#2F6FD0"), new Color("#1D1D1D") };
        for (var i = 0; i < 3; i++)
        {
            var tube = top + new Vector2(-6.5f + (i * 3.5f), -17);
            c.DrawRect(new Rect2(tube, new Vector2(2, 10)), new Color("#BFE3F2"));
            c.DrawCircle(tube + new Vector2(1, 10 - (2 + (i * 2.5f))), 0.9f, new Color("#3A3A3A"));
            c.DrawCircle(tube + new Vector2(1, 12.5f), 1.4f, gases[i]);
            c.DrawArc(tube + new Vector2(1, 12.5f), 1.4f, 0, Mathf.Tau, 10, new Color("#555F68"), 0.5f);
        }

        // Yellow sevoflurane vaporiser beside the flowmeters.
        Figures.RoundedRect(c, new Rect2(top + new Vector2(4, -16), new Vector2(5, 9)), 1, new Color("#F2C230"));
        c.DrawCircle(top + new Vector2(6.5f, -13), 1.5f, new Color("#8A6C10"));

        // Bellows in their clear dome on the left of the worktop.
        var bellows = S(cx, cy, 24) + new Vector2(-17, 0);
        Figures.RoundedRect(c, new Rect2(bellows + new Vector2(-4, -12), new Vector2(8, 12)), 3, new Color("#DDF0F8", 0.7f));
        for (var i = 0; i < 4; i++)
        {
            c.DrawLine(bellows + new Vector2(-3, -2 - (i * 2)), bellows + new Vector2(3, -2 - (i * 2)), new Color("#5A9FC0"), 1f);
        }

        // Breathing circuit hanging in a loop off the front.
        Tubing(c, Curve(bellows + new Vector2(2, -2), bellows + new Vector2(-6, 20), bellows + new Vector2(10, 6)), Hose);

        // Monitor on top of the tower.
        c.DrawLine(top + new Vector2(0, -20), top + new Vector2(0, -24), DarkSteel, 2f);
        var glass = Screen(c, top + new Vector2(0, -23), 24, 16);
        Wave(c, glass.Position + new Vector2(1.5f, 5), 14, t => t is > 0.3f and < 0.38f ? -3.5f : 0, new Color("#3EE07A"));
        Wave(c, glass.Position + new Vector2(1.5f, 9), 14, t => -2f * Mathf.Max(0, Mathf.Sin(t * Mathf.Tau * 2)), new Color("#4FC3F7"));
        Wave(c, glass.Position + new Vector2(1.5f, 12.5f), 14, t => t % 0.5f < 0.3f ? -2.5f : 0, new Color("#FFD54F"));
        c.DrawRect(new Rect2(glass.Position + new Vector2(16.5f, 2), new Vector2(3.5f, 2.2f)), new Color("#3EE07A"));
        c.DrawRect(new Rect2(glass.Position + new Vector2(16.5f, 6), new Vector2(3.5f, 2.2f)), new Color("#4FC3F7"));
        c.DrawRect(new Rect2(glass.Position + new Vector2(16.5f, 10), new Vector2(3.5f, 2.2f)), new Color("#FFD54F"));
    }

    /// <summary>An ultrasound scanner: a cart with a keyboard console, probes in their holders and a screen showing a scan.</summary>
    private static void Ultrasound(CanvasItem c, (double X, double Y, double W, double L) whole, float height, Color colour)
    {
        var (x, y, w, l) = Centred(whole, Math.Min(whole.W, 1.1), Math.Min(whole.L, 1.1));
        var (cx, cy) = (x + (w / 2), y + (l / 2));
        Shadow(c, x, y, w, l);
        Castors(c, x, y, w, l);
        Block(c, x + 0.08, y + 0.08, w - 0.16, l - 0.16, 7, colour, 3);
        Block(c, cx - 0.17, cy - 0.17, 0.34, 0.34, 12, new Color("#C9D2DA"), 10);

        // Console with the keyboard and trackball.
        Block(c, x + 0.02, y + 0.1, w - 0.04, l - 0.2, 3, new Color("#E4E9ED"), 22);
        var desk = S(cx, cy, 25);
        Figures.RoundedRect(c, new Rect2(desk + new Vector2(-8, -2.5f), new Vector2(12, 4.5f)), 1, new Color("#AAB6C0"));
        for (var i = 0; i < 4; i++)
        {
            c.DrawRect(new Rect2(desk + new Vector2(-7 + (i * 2.8f), -1.6f), new Vector2(2, 1.2f)), new Color("#6E7A84"));
        }

        c.DrawCircle(desk + new Vector2(7, 0), 2f, new Color("#4A545D"));
        c.DrawCircle(desk + new Vector2(6.4f, -0.6f), 0.7f, new Color("#9AA5AE"));

        // Probes in their holders, with coiled grey cables.
        for (var i = 0; i < 3; i++)
        {
            var holder = desk + new Vector2(-12 + (i * 3), 2);
            c.DrawLine(holder, holder + new Vector2(0, -5), new Color("#E8ECEF"), 2f);
            c.DrawPolyline(Curve(holder + new Vector2(0, -5), holder + new Vector2(-6 + (i * 2), 14), holder + new Vector2(4, 8), 8), new Color("#4F5A64"), 0.9f, true);
        }

        // Screen on its arm, showing a grey fan-shaped scan.
        c.DrawLine(desk + new Vector2(0, -2), desk + new Vector2(0, -12), DarkSteel, 2f);
        var glass = Screen(c, desk + new Vector2(0, -11), 26, 18);
        var apex = glass.Position + new Vector2(glass.Size.X / 2 - 3, 1.5f);
        var fan = new Vector2[10];
        fan[0] = apex;
        for (var i = 0; i < 9; i++)
        {
            var a = Mathf.DegToRad(55 + (i * 70 / 8f));
            fan[i + 1] = apex + new Vector2(Mathf.Cos(a) * 13.5f, Mathf.Sin(a) * 13.5f);
        }

        c.DrawColoredPolygon(fan, new Color("#4B5157"));
        c.DrawCircle(apex + new Vector2(-1, 8), 2.5f, new Color("#1A1D20"));
        c.DrawLine(apex + new Vector2(-6, 11), apex + new Vector2(5, 10), new Color("#8E969D"), 0.8f);
        c.DrawRect(new Rect2(glass.Position + new Vector2(glass.Size.X - 4.5f, 2), new Vector2(3, 7)), new Color("#2A3540"));
    }

    /// <summary>An EEG machine: a cart with a laptop showing many brain-wave channels and the head box with its rainbow of electrode leads.</summary>
    private static void Eeg(CanvasItem c, (double X, double Y, double W, double L) whole, float height, Color colour)
    {
        var (x, y, w, l) = Centred(whole, Math.Min(whole.W, 1.1), Math.Min(whole.L, 1.0));
        var (cx, cy) = (x + (w / 2), y + (l / 2));
        Shadow(c, x, y, w, l);
        Castors(c, x, y, w, l);

        // Cart: posts, a lower shelf with the amplifier, and a top shelf.
        foreach (var (px, py) in new[] { (x + 0.08, y + 0.08), (x + w - 0.08, y + 0.08), (x + w - 0.08, y + l - 0.08), (x + 0.08, y + l - 0.08) })
        {
            c.DrawLine(S(px, py, 3), S(px, py, 24), DarkSteel, 1.4f);
        }

        Block(c, x + 0.05, y + 0.05, w - 0.1, l - 0.1, 2, Steel, 4);
        Block(c, cx - 0.3, cy - 0.25, 0.6, 0.5, 6, colour, 6);
        Block(c, x + 0.02, y + 0.02, w - 0.04, l - 0.04, 2, new Color("#D9DFE4"), 24);

        // The head box, with its coloured leads gathered into a bundle.
        var box = S(cx, cy, 26) + new Vector2(-9, 0);
        Figures.RoundedRect(c, new Rect2(box + new Vector2(-4, -5), new Vector2(8, 5)), 1, new Color("#3D4A57"));
        var leads = new[] { "#E53935", "#FBC02D", "#43A047", "#1E88E5", "#8E24AA", "#FB8C00", "#F5F5F5" };
        for (var i = 0; i < leads.Length; i++)
        {
            var from = box + new Vector2(-3 + i, -4.5f);
            c.DrawPolyline(Curve(from, from + new Vector2(-6 - i, 10), from + new Vector2(-4 - (i * 0.6f), 22), 8), new Color(leads[i]), 0.8f, true);
        }

        // The laptop, open, showing eight wavy channels.
        var lap = S(cx, cy, 26) + new Vector2(4, 0);
        c.DrawColoredPolygon(new[] { lap + new Vector2(-10, 0), lap + new Vector2(10, 0), lap + new Vector2(8, -2.5f), lap + new Vector2(-8, -2.5f) }, new Color("#AEB8C1"));
        var glass = Screen(c, lap + new Vector2(0, -2), 20, 14, new Color("#2C3239"));
        for (var i = 0; i < 6; i++)
        {
            var phase = i * 1.7f;
            Wave(c, glass.Position + new Vector2(1, 1.8f + (i * 1.75f)), glass.Size.X - 2, t => 0.6f * Mathf.Sin((t * 22) + phase) * Mathf.Sin((t * 5) + i), i % 2 == 0 ? new Color("#7FE0A0") : new Color("#9AD0F0"), 0.6f);
        }
    }

    /// <summary>An endoscopy stack: a tall trolley of processors and a light source, a monitor showing the scope's view, and the scope hanging on its hook.</summary>
    private static void EndoscopyStack(CanvasItem c, (double X, double Y, double W, double L) whole, float height, Color colour)
    {
        var (x, y, w, l) = Centred(whole, Math.Min(whole.W, 1.2), Math.Min(whole.L, 1.1));
        var (cx, cy) = (x + (w / 2), y + (l / 2));
        Shadow(c, x, y, w, l);
        Castors(c, x, y, w, l);

        // Posts and three stacked units, each with a front panel.
        foreach (var (px, py) in new[] { (x + 0.06, y + 0.06), (x + w - 0.06, y + 0.06), (x + w - 0.06, y + l - 0.06), (x + 0.06, y + l - 0.06) })
        {
            c.DrawLine(S(px, py, 3), S(px, py, 37), DarkSteel, 1.4f);
        }

        var units = new[] { (3f, colour.Darkened(0.15f)), (13f, new Color("#E8ECEF")), (22f, colour), (31f, new Color("#E8ECEF")) };
        foreach (var (lift, unit) in units)
        {
            Block(c, x + 0.1, y + 0.1, w - 0.2, l - 0.2, 7, unit, lift);
            foreach (var (a, b, _, left) in IsoDraw.VisibleFaces(x + 0.1, y + 0.1, w - 0.2, l - 0.2))
            {
                if (!left)
                {
                    continue;
                }

                var p = a.Lerp(b, 0.25f) + new Vector2(0, -lift - 4.5f);
                c.DrawRect(new Rect2(p, new Vector2(4, 2)), new Color("#1F2A33"));
                c.DrawCircle(a.Lerp(b, 0.75f) + new Vector2(0, -lift - 3.5f), 1.1f, unit.Lightened(0.4f).Luminance > 0.8f ? new Color("#3EA6E0") : new Color("#E0E6EA"));
            }
        }

        // Water bottle for the scope on the side.
        var side = S(x + w, cy, 14);
        Figures.RoundedRect(c, new Rect2(side + new Vector2(1, -8), new Vector2(4, 8)), 1.5f, new Color("#DDF0F8", 0.85f));

        // The scope coiled on its hook, black insertion tube with a grey control head.
        var hook = S(x, cy, 40) + new Vector2(-4, 0);
        c.DrawLine(hook + new Vector2(4, 0), hook, DarkSteel, 1.4f);
        c.DrawPolyline(Curve(hook, hook + new Vector2(-14, 34), hook + new Vector2(2, 26), 14), new Color("#1C1F22"), 1.6f, true);
        c.DrawPolyline(Curve(hook + new Vector2(2, 26), hook + new Vector2(10, 18), hook + new Vector2(4, 36), 10), new Color("#1C1F22"), 1.6f, true);
        Figures.RoundedRect(c, new Rect2(hook + new Vector2(-2, -1), new Vector2(4, 7)), 1, new Color("#5C6670"));

        // Monitor showing the pink inside of the stomach.
        c.DrawLine(S(cx, cy, 38), S(cx, cy, 42), DarkSteel, 2f);
        var glass = Screen(c, S(cx, cy, 41), 28, 19);
        var view = glass.GetCenter() + new Vector2(-2, 0);
        c.DrawCircle(view, 7.5f, new Color("#9E3B45"));
        c.DrawCircle(view + new Vector2(-1, -1), 5.5f, new Color("#D86E72"));
        c.DrawCircle(view + new Vector2(1.5f, 1), 2.4f, new Color("#5E1E26"));
        c.DrawCircle(view + new Vector2(-3, -3), 1.2f, new Color("#FFD9D2"));
        c.DrawRect(new Rect2(glass.Position + new Vector2(glass.Size.X - 4.5f, 2), new Vector2(3, 2)), new Color("#3EE07A"));
    }

    /// <summary>A mobile C-arm: the base unit on wheels and the big C, with the detector on the top end and the X-ray tube on the bottom.</summary>
    private static void CArm(CanvasItem c, (double X, double Y, double W, double L) whole, float height, Color colour)
    {
        var alongX = whole.W >= whole.L;
        var (x, y, w, l) = whole;
        var (cx, cy) = (x + (w / 2), y + (l / 2));
        var length = alongX ? w : l;

        // Base unit at one end of the footprint, the C reaching out over the other.
        var (bx, by, bw, bl) = alongX ? (x, cy - 0.45, length * 0.42, 0.9) : (cx - 0.45, y, 0.9, length * 0.42);
        Shadow(c, bx, by, bw, bl);
        Castors(c, bx, by, bw, bl);
        Block(c, bx + 0.04, by + 0.04, bw - 0.08, bl - 0.08, 16, colour, 3);
        foreach (var (a, b, _, _) in IsoDraw.VisibleFaces(bx + 0.04, by + 0.04, bw - 0.08, bl - 0.08))
        {
            c.DrawLine(a.Lerp(b, 0.1f) + new Vector2(0, -15), a.Lerp(b, 0.9f) + new Vector2(0, -15), new Color("#2F6FD0"), 1.2f);
        }

        // Positions along the footprint's length, measured from the base end.
        var (sx, sy) = alongX ? (x, cy) : (cx, y);
        Vector2 Along(double t, float h) => alongX ? S(sx + t, sy, h) : S(sx, sy + t, h);

        // The column, and the arm out to the back of the C.
        var col = length * 0.3;
        var (colX, colY) = alongX ? (sx + col, sy) : (sx, sy + col);
        Block(c, colX - 0.12, colY - 0.12, 0.24, 0.24, 16, colour.Darkened(0.06f), 19);
        const float hub = 33f;
        var back = length * 0.45;
        c.DrawLine(Along(col, hub), Along(back, hub), colour.Darkened(0.2f), 4.5f);
        c.DrawLine(Along(col, hub + 1.2f), Along(back, hub + 1.2f), colour.Lightened(0.2f), 1.5f);

        // The C itself: an arc in the vertical plane along the footprint, opening away from the base.
        const float rise = 25f;
        var reach = length * 0.36;
        Vector2 Arc(float a) => Along(back + reach - (Math.Cos(a) * reach), hub + (rise * Mathf.Sin(a)));
        const int n = 26;
        var points = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            points[i] = Arc(Mathf.DegToRad(-95 + (i * 190f / (n - 1))));
        }

        c.DrawPolyline(points, new Color("#7D8A95"), 7.5f, true);
        c.DrawPolyline(points, new Color("#EEF1F4"), 5f, true);
        c.DrawPolyline(points, new Color("#C9D2DA"), 1.5f, true);

        // Detector on top, X-ray tube below, facing each other across the gap where the patient lies.
        double End(float degrees) => back + reach - (Math.Cos(Mathf.DegToRad(degrees)) * reach);
        var (tx, ty) = alongX ? (sx + End(95), sy) : (sx, sy + End(95));
        Block(c, tx - 0.24, ty - 0.24, 0.48, 0.48, 5, new Color("#E3E8EC"), hub + rise - 5);
        var (ux, uy) = alongX ? (sx + End(-95), sy) : (sx, sy + End(-95));
        Block(c, ux - 0.22, uy - 0.22, 0.44, 0.44, 7, new Color("#C9D2DA"), hub - rise - 2);
        var tube = S(ux, uy, hub - rise + 5);
        c.DrawRect(new Rect2(tube + new Vector2(-2.5f, -1), new Vector2(5, 1.5f)), new Color("#FFE082"));

        // Yellow radiation warning on the base.
        var warn = S(bx + (bw / 2), by + (bl / 2), 8) + new Vector2(0, 4);
        c.DrawColoredPolygon(new[] { warn + new Vector2(0, -3), warn + new Vector2(3, 2), warn + new Vector2(-3, 2) }, new Color("#F9D037"));
    }

    /// <summary>An X-ray tube on a floor-standing column: the column on its base, the tube head and collimator with its light.</summary>
    private static void XrayTube(CanvasItem c, (double X, double Y, double W, double L) whole, float height, Color colour)
    {
        var (cx, cy) = (whole.X + (whole.W / 2), whole.Y + (whole.L / 2));
        Shadow(c, cx - 0.4, cy - 0.4, 0.8, 0.8);
        Block(c, cx - 0.36, cy - 0.36, 0.72, 0.72, 3, new Color("#5A6670"));
        Block(c, cx - 0.13, cy - 0.13, 0.26, 0.26, height - 3, new Color("#9AA6B0"), 3);

        // The tube head across the top of the column, the collimator under it with its light window.
        var (hw, hl) = whole.W >= whole.L ? (0.9, 0.36) : (0.36, 0.9);
        Block(c, cx - (hw / 2), cy - (hl / 2), hw, hl, 9, new Color("#EEF1F4"), height - 24);
        Block(c, cx - 0.2, cy - 0.2, 0.4, 0.4, 6, new Color("#D2D9DF"), height - 31);
        Block(c, cx - 0.16, cy - 0.16, 0.32, 0.32, 3, new Color("#9AA6B0"), height - 3);
        foreach (var (a, b, _, left) in IsoDraw.VisibleFaces(cx - 0.2, cy - 0.2, 0.4, 0.4))
        {
            if (left)
            {
                c.DrawRect(new Rect2(a.Lerp(b, 0.5f) + new Vector2(-2, -height + 34), new Vector2(4, 2)), new Color("#FFE082"));
            }
        }

        // Handles on the tube head, and a soft light field on the floor below.
        var head = S(cx, cy, height - 24);
        c.DrawLine(head + new Vector2(-10, 0), head + new Vector2(-10, 5), new Color("#5A6670"), 1.4f);
        c.DrawLine(head + new Vector2(10, 0), head + new Vector2(10, 5), new Color("#5A6670"), 1.4f);
        var warn = S(cx, cy, height - 18) + new Vector2(0, 0);
        c.DrawColoredPolygon(new[] { warn + new Vector2(0, -3), warn + new Vector2(3, 2), warn + new Vector2(-3, 2) }, new Color("#F9D037"));
    }

    /// <summary>A CT scanner gantry: a deep white ring with the bore open on both faces, an LED ring and a small control panel.</summary>
    private static void CtGantry(CanvasItem c, (double X, double Y, double W, double L) whole, float height, Color colour)
    {
        var (x, y, w, l) = whole;
        var alongX = w >= l;
        Shadow(c, x, y, w, l);
        Block(c, x + 0.1, y + 0.1, w - 0.2, l - 0.2, 4, new Color("#7D8995"));
        IsoDraw.Prism(c, x, y, w, l, height - 4, colour, colour.Darkened(0.08f), lift: 4, rim: Colors.White);

        // The bore on whichever long face can be seen; the table runs through it.
        foreach (var (a, b, side, left) in IsoDraw.VisibleFaces(x, y, w, l))
        {
            var longFace = alongX ? side is Sides.North or Sides.South : side is Sides.East or Sides.West;
            if (!longFace)
            {
                // A small control panel on the end.
                var p = a.Lerp(b, 0.5f) + new Vector2(0, -height * 0.55f);
                Figures.RoundedRect(c, new Rect2(p + new Vector2(-3, -4), new Vector2(6, 8)), 1, new Color("#3A444D"));
                c.DrawCircle(p + new Vector2(0, -1.5f), 1f, new Color("#3EE07A"));
                c.DrawCircle(p + new Vector2(0, 1.5f), 1f, new Color("#E53935"));
                continue;
            }

            var mid = a.Lerp(b, 0.5f) + new Vector2(0, -4 - ((height - 4) * 0.5f));
            var along = (b - a).Normalized() * (b - a).Length() * 0.5f / (float)(alongX ? w : l);
            Vector2[] Ring(float tiles, float rise)
            {
                var points = new Vector2[28];
                for (var i = 0; i < points.Length; i++)
                {
                    var t = i * Mathf.Tau / points.Length;
                    points[i] = mid + (along * Mathf.Cos(t) * tiles * 2) + new Vector2(0, Mathf.Sin(t) * rise);
                }

                return points;
            }

            c.DrawColoredPolygon(Ring(0.95f, 21), colour.Darkened(0.12f));
            c.DrawColoredPolygon(Ring(0.78f, 17), new Color("#55616C"));
            c.DrawColoredPolygon(Array.ConvertAll(Ring(0.7f, 15), p => p + new Vector2(2, -2)), new Color("#2E3740"));
            var led = Ring(0.88f, 19.2f);
            c.DrawPolyline(led[^8..].Concat(led[..6]), new Color("#4FC3F7"), 1.2f, true);

            // Laser alignment lights above the bore.
            var lights = mid + new Vector2(0, -22);
            c.DrawCircle(lights + (along * -0.3f), 1f, new Color("#E53935"));
            c.DrawCircle(lights + (along * 0.3f), 1f, new Color("#3EE07A"));
        }
    }

    private static Vector2[] Concat(this Vector2[] a, Vector2[] b)
    {
        var all = new Vector2[a.Length + b.Length];
        a.CopyTo(all, 0);
        b.CopyTo(all, a.Length);
        return all;
    }

    /// <summary>A mobile lead screen: a grey panel on a wheeled base with a lead-glass window and a radiation sign.</summary>
    private static void LeadScreen(CanvasItem c, (double X, double Y, double W, double L) whole, float height, Color colour)
    {
        var (x, y, w, l) = whole;
        var alongX = w >= l;
        var (cx, cy) = (x + (w / 2), y + (l / 2));
        Shadow(c, x, y, w, l, 0.1f);
        Castors(c, x, y, w, l);
        Block(c, x + 0.04, y + 0.04, w - 0.08, l - 0.08, 3, new Color("#5A6670"), 3);

        var (px, py, pw, pl) = alongX ? (x, cy - 0.1, w, 0.2) : (cx - 0.1, y, 0.2, l);
        IsoDraw.Prism(c, px, py, pw, pl, height - 6, colour.Lightened(0.08f), colour.Darkened(0.06f), lift: 6, rim: colour.Lightened(0.35f));
        foreach (var (a, b, side, _) in IsoDraw.VisibleFaces(px, py, pw, pl))
        {
            var longFace = alongX ? side is Sides.North or Sides.South : side is Sides.East or Sides.West;
            if (!longFace)
            {
                continue;
            }

            Vector2 At(float u, float v) => a.Lerp(b, u) + new Vector2(0, -v);
            c.DrawColoredPolygon(new[] { At(0.2f, height - 22), At(0.8f, height - 22), At(0.8f, height - 7), At(0.2f, height - 7) }, new Color("#9FC9DD", 0.85f));
            c.DrawPolyline(new[] { At(0.2f, height - 22), At(0.8f, height - 22), At(0.8f, height - 7), At(0.2f, height - 7), At(0.2f, height - 22) }, colour.Darkened(0.3f), 1f, true);
            c.DrawLine(At(0.3f, height - 9), At(0.45f, height - 19), new Color(1, 1, 1, 0.5f), 1f, true);

            var sign = At(0.5f, height * 0.4f);
            c.DrawColoredPolygon(new[] { sign + new Vector2(0, -4), sign + new Vector2(4, 3), sign + new Vector2(-4, 3) }, new Color("#F9D037"));
            c.DrawCircle(sign + new Vector2(0, 0.8f), 0.9f, new Color("#1D1D1D"));
        }
    }
}
