using System;
using System.Collections.Generic;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Maternity equipment: the clear-sided baby cot on its trolley, the neonatal incubator, the CTG
/// monitor, the resuscitaire (the newborn resuscitation unit with its overhead heater), the birthing
/// ball, the baby changing unit and the breast pump. Each item is drawn whole, once.
/// </summary>
public static class MaternityArt
{
    private static readonly Color Castor = new("#2B3036");
    private static readonly Color Steel = new("#B9C3CB");
    private static readonly Color DarkSteel = new("#6E7A84");
    private static readonly Color Glass = new("#0A1218");
    private static readonly Color Perspex = new("#D9EEF7", 0.32f);
    private static readonly Color PerspexEdge = new("#F4FBFF", 0.85f);

    /// <summary>True for the box items drawn here (the cot and incubator are drawn by the world layer, with their babies).</summary>
    public static bool Draws(string id) => id is "ctg_monitor" or "resuscitaire" or "birthing_ball" or "baby_changing_unit" or "breast_pump";

    public static void Draw(CanvasItem c, string id, (double X, double Y, double W, double L) whole, float height, Color colour)
    {
        switch (id)
        {
            case "ctg_monitor":
                Ctg(c, whole, height, colour);
                break;
            case "resuscitaire":
                Resuscitaire(c, whole, height, colour);
                break;
            case "birthing_ball":
                BirthingBall(c, whole, colour);
                break;
            case "baby_changing_unit":
                ChangingUnit(c, whole, height, colour);
                break;
            case "breast_pump":
                BreastPump(c, whole, height, colour);
                break;
        }
    }

    private static Vector2 S(double x, double y, float h = 0) => IsoDraw.ToScreen(x, y) + new Vector2(0, -h);

    private static void Block(CanvasItem c, double x, double y, double w, double l, float h, Color colour, float lift = 0) =>
        IsoDraw.Prism(c, x, y, w, l, h, colour.Lightened(0.1f), colour.Darkened(0.1f), lift: lift, rim: colour.Lightened(0.32f));

    private static void Shadow(CanvasItem c, double x, double y, double w, double l, float alpha = 0.14f)
    {
        var p = new[] { S(x, y), S(x + w, y), S(x + w, y + l), S(x, y + l) };
        var mid = (p[0] + p[1] + p[2] + p[3]) / 4;
        c.DrawColoredPolygon(Array.ConvertAll(p, q => q + ((q - mid) * 0.1f) + new Vector2(2, 2)), new Color(0, 0, 0, alpha));
    }

    private static void Castors(CanvasItem c, double x, double y, double w, double l)
    {
        foreach (var (px, py) in new[] { (x + 0.1, y + 0.1), (x + w - 0.1, y + 0.1), (x + w - 0.1, y + l - 0.1), (x + 0.1, y + l - 0.1) })
        {
            var p = S(px, py);
            c.DrawLine(p + new Vector2(0, -4), p + new Vector2(0, -2), DarkSteel, 1.2f);
            c.DrawCircle(p + new Vector2(0, -1.6f), 1.8f, Castor);
        }
    }

    private static Rect2 Screen(CanvasItem c, Vector2 bottom, float w, float h, Color casing)
    {
        var box = new Rect2(bottom + new Vector2(-w / 2, -h), new Vector2(w, h));
        Figures.RoundedRect(c, new Rect2(box.Position + new Vector2(1, 1.5f), box.Size), 2, new Color(0, 0, 0, 0.2f));
        Figures.RoundedRect(c, box, 2, casing);
        var glass = box.Grow(-1.6f);
        c.DrawRect(glass, Glass);
        return glass;
    }

    private static (double X, double Y, double W, double L) Centred((double X, double Y, double W, double L) whole, double w, double l)
    {
        var (cx, cy) = (whole.X + (whole.W / 2), whole.Y + (whole.L / 2));
        return (cx - (w / 2), cy - (l / 2), w, l);
    }

    /// <summary>
    /// A hospital baby cot: a steel trolley with a storage shelf, and a clear plastic tub on top with a
    /// little mattress. <paramref name="babies"/> lie in the tub, seen through its clear sides.
    /// </summary>
    public static void Cot(CanvasItem c, (double X, double Y, double W, double L) whole, float height, IReadOnlyList<BabyLook> babies)
    {
        var (x, y, w, l) = Centred(whole, Math.Min(whole.W, 1.1), Math.Min(whole.L, 1.5));
        var (cx, cy) = (x + (w / 2), y + (l / 2));
        var tubLift = height * 0.62f;
        Shadow(c, x, y, w, l);
        Castors(c, x, y, w, l);

        // The frame: four posts, a low shelf with a folded blanket and the cradle under the tub.
        foreach (var (px, py) in new[] { (x + 0.12, y + 0.12), (x + w - 0.12, y + 0.12), (x + w - 0.12, y + l - 0.12), (x + 0.12, y + l - 0.12) })
        {
            c.DrawLine(S(px, py, 3), S(px, py, tubLift), Steel, 1.6f);
        }

        IsoDraw.Prism(c, x + 0.08, y + 0.08, w - 0.16, l - 0.16, 1.5f, new Color("#C9D2DA"), DarkSteel, lift: 6);
        Block(c, x + 0.25, y + 0.3, w - 0.5, (l * 0.45) - 0.2, 2.5f, new Color("#CFE0EE"), 7.5f);
        IsoDraw.Prism(c, x + 0.05, y + 0.05, w - 0.1, l - 0.1, 1.5f, Steel, DarkSteel, lift: tubLift - 1.5f);

        // Inside the tub: the white mattress, the babies, then the clear sides in front of them.
        IsoDraw.Prism(c, x + 0.1, y + 0.1, w - 0.2, l - 0.2, 2.5f, new Color("#F6F8FA"), new Color("#DCE4EA"), lift: tubLift);
        for (var i = 0; i < babies.Count; i++)
        {
            var across = babies.Count == 1 ? 0 : (i - 0.5) * 0.36;
            Figures.Baby(c, S(cx + across, cy + (across * 0.4), tubLift + 4.5f), babies[i], scale: 0.9f);
        }

        IsoDraw.Prism(c, x + 0.05, y + 0.05, w - 0.1, l - 0.1, 9, new Color(0, 0, 0, 0), Perspex, lift: tubLift, rim: PerspexEdge);
    }

    /// <summary>
    /// A neonatal incubator: a cream cabinet with drawers on castors, a mattress tray under a clear
    /// hood with round hand ports, a heater and control panel at the head, and the baby inside.
    /// </summary>
    public static void Incubator(CanvasItem c, (double X, double Y, double W, double L) whole, float height, Color colour, BabyLook? baby, bool headSouth, double time)
    {
        var (x, y, w, l) = whole;
        var (cx, cy) = (x + (w / 2), y + (l / 2));
        var deck = height * 0.48f;
        Shadow(c, x, y, w, l, 0.16f);
        Castors(c, x, y, w, l);

        // Cabinet with two drawers.
        var cabinet = new Color("#E9E6DD");
        IsoDraw.Prism(c, x + 0.06, y + 0.06, w - 0.12, l - 0.12, deck - 4, cabinet, cabinet.Darkened(0.12f), lift: 4, skirting: new Color("#7D8A95"), skirtingHeight: 3, rim: cabinet.Lightened(0.2f));
        foreach (var (a, b, _, _) in IsoDraw.VisibleFaces(x + 0.06, y + 0.06, w - 0.12, l - 0.12))
        {
            if ((b - a).Length() > 20)
            {
                for (var d = 0; d < 2; d++)
                {
                    var h = 8 + (d * 7);
                    c.DrawLine(a.Lerp(b, 0.08f) + new Vector2(0, -h), b.Lerp(a, 0.08f) + new Vector2(0, -h), cabinet.Darkened(0.28f), 0.8f, true);
                    c.DrawLine(a.Lerp(b, 0.42f) + new Vector2(0, -h - 3), a.Lerp(b, 0.58f) + new Vector2(0, -h - 3), DarkSteel, 1.4f, true);
                }
            }
        }

        // The tray and its mattress, with the baby lying on it.
        IsoDraw.Prism(c, x, y, w, l, 2, colour.Lightened(0.15f), colour, lift: deck, rim: colour.Lightened(0.35f));
        IsoDraw.Prism(c, x + 0.15, y + 0.2, w - 0.3, l - 0.4, 2, new Color("#F6F8FA"), new Color("#DCE4EA"), lift: deck + 2);
        if (baby is not null)
        {
            Figures.Baby(c, S(cx, cy, deck + 7), baby, scale: 0.85f);

            // A saturation probe lead and monitoring wires.
            var probe = S(cx + 0.15, cy + (headSouth ? -0.45 : 0.45), deck + 5);
            c.DrawPolyline(new[] { probe, probe + new Vector2(4, 2), S(cx + (w / 2) - 0.05, cy, deck + 6) }, new Color("#E04848"), 0.7f, true);
        }

        // The hood: clear, closed, with a rounded lid and hand ports along the sides.
        var hood = height - deck - 6;
        IsoDraw.Prism(c, x + 0.02, y + 0.02, w - 0.04, l - 0.04, hood, new Color("#E6F4FA", 0.28f), Perspex, lift: deck + 2, rim: PerspexEdge);
        foreach (var (a, b, _, _) in IsoDraw.VisibleFaces(x + 0.02, y + 0.02, w - 0.04, l - 0.04))
        {
            if ((b - a).Length() > 20)
            {
                foreach (var t in new[] { 0.3f, 0.7f })
                {
                    var port = a.Lerp(b, t) + new Vector2(0, -deck - 2 - (hood * 0.5f));
                    c.DrawArc(port, 3.2f, 0, Mathf.Tau, 14, new Color("#9FB6C6"), 1.4f, true);
                    c.DrawCircle(port, 2.4f, new Color("#E9F4FA", 0.25f));
                }
            }
        }

        // The control unit and heater at the head end, with its display.
        var headY = headSouth ? y + l - 0.3 : y;
        Block(c, x + 0.05, headY, w - 0.1, 0.3, 8, new Color("#D9DEE3"), deck - 6);
        var panel = S(cx, headY + 0.15, deck + 2);
        var glass = Screen(c, panel, 12, 7, new Color("#5A6670"));
        c.DrawString(ThemeDB.FallbackFont, glass.Position + new Vector2(1, 5), "36.8", HorizontalAlignment.Left, -1, 5, new Color("#7CE0A0"));
        var blink = (int)(time * 2) % 2 == 0;
        c.DrawCircle(glass.Position + new Vector2(glass.Size.X - 1.5f, 1.5f), 0.8f, blink ? new Color("#3EE07A") : new Color("#1E5A36"));
    }

    /// <summary>A CTG monitor on a trolley: a screen with the baby's heart rate and the contractions, and a paper trace curling out.</summary>
    private static void Ctg(CanvasItem c, (double X, double Y, double W, double L) whole, float height, Color colour)
    {
        var (x, y, w, l) = Centred(whole, Math.Min(whole.W, 0.8), Math.Min(whole.L, 0.8));
        var (cx, cy) = (x + (w / 2), y + (l / 2));
        Shadow(c, x, y, w, l);
        Castors(c, x, y, w, l);
        c.DrawLine(S(cx, cy, 4), S(cx, cy, height * 0.55f), DarkSteel, 2.2f);
        Block(c, x + 0.05, y + 0.05, w - 0.1, l - 0.1, 2, new Color("#C9D2DA"), height * 0.55f);
        Block(c, x + 0.1, y + 0.15, w - 0.2, l - 0.3, 6, colour, (height * 0.55f) + 2);
        var bottom = S(cx, cy, (height * 0.55f) + 8);
        var glass = Screen(c, bottom, 22, 15, new Color("#E4E9ED"));

        // Fetal heart (top, about 140 a minute) and contractions (bottom) in green and orange.
        var top = new Vector2[16];
        var toco = new Vector2[16];
        for (var i = 0; i < top.Length; i++)
        {
            var t = i / 15f;
            top[i] = glass.Position + new Vector2(1 + (t * (glass.Size.X - 2)), 4 + Mathf.Sin(t * 19) * 1.1f);
            toco[i] = glass.Position + new Vector2(1 + (t * (glass.Size.X - 2)), glass.Size.Y - 2 - Mathf.Max(0, Mathf.Sin(t * 6.5f)) * 4.5f);
        }

        c.DrawPolyline(top, new Color("#5BE08A"), 0.8f, true);
        c.DrawPolyline(toco, new Color("#F2A64A"), 0.8f, true);

        // The paper trace spilling from the printer.
        var slot = bottom + new Vector2(-8, 3);
        c.DrawColoredPolygon(new[] { slot, slot + new Vector2(10, 0), slot + new Vector2(11, 6), slot + new Vector2(2, 9), slot + new Vector2(-1, 5) }, new Color("#F7F4E8"));
        c.DrawLine(slot + new Vector2(1, 3), slot + new Vector2(9, 3.5f), new Color("#D97A7A"), 0.6f);

        // Two transducers on their belts hanging off the side.
        foreach (var (dx, belt) in new[] { (12f, new Color("#E58FB4")), (14.5f, new Color("#8FB8E5")) })
        {
            var hook = bottom + new Vector2(dx - 1, -4);
            c.DrawLine(hook, hook + new Vector2(0, 9), belt, 1.6f);
            c.DrawCircle(hook + new Vector2(0, 10), 2.2f, new Color("#EEF1F3"));
        }
    }

    /// <summary>
    /// A resuscitaire: a cart with drawers and a padded open platform, an overhead radiant heater on an arm,
    /// a gas panel with dials and a clock, and a little bag-and-mask hanging at the side.
    /// </summary>
    private static void Resuscitaire(CanvasItem c, (double X, double Y, double W, double L) whole, float height, Color colour)
    {
        var (x, y, w, l) = Centred(whole, Math.Min(whole.W, 1.4), Math.Min(whole.L, 1.2));
        var (cx, cy) = (x + (w / 2), y + (l / 2));
        var deck = height * 0.42f;
        Shadow(c, x, y, w, l);
        Castors(c, x, y, w, l);
        Block(c, x + 0.05, y + 0.05, w - 0.1, l - 0.1, deck - 4, colour, 4);
        foreach (var (a, b, _, _) in IsoDraw.VisibleFaces(x + 0.05, y + 0.05, w - 0.1, l - 0.1))
        {
            if ((b - a).Length() > 18)
            {
                for (var d = 0; d < 3; d++)
                {
                    var h = 9 + (d * 6);
                    c.DrawLine(a.Lerp(b, 0.1f) + new Vector2(0, -h), b.Lerp(a, 0.1f) + new Vector2(0, -h), colour.Darkened(0.25f), 0.8f, true);
                }
            }
        }

        // The padded platform with low clear side panels.
        IsoDraw.Prism(c, x, y, w, l, 3, new Color("#7FB3D5"), new Color("#5E93B8"), lift: deck, rim: new Color("#A9CFE6"));
        IsoDraw.Prism(c, x, y, w, l, 5, new Color(0, 0, 0, 0), Perspex, lift: deck + 3, rim: PerspexEdge);

        // The column at the back with the gas panel, the arm and the heater hood above.
        var back = (X: cx, Y: y + 0.1);
        Block(c, back.X - 0.18, back.Y, 0.36, 0.2, height - deck - 2, new Color("#D5DBE0"), deck);
        var panel = S(back.X, back.Y + 0.1, deck + 18);
        Figures.RoundedRect(c, new Rect2(panel + new Vector2(-8, -12), new Vector2(16, 12)), 1.5f, new Color("#E4E9ED"));
        c.DrawCircle(panel + new Vector2(-4, -8), 2.3f, Colors.White);
        c.DrawArc(panel + new Vector2(-4, -8), 2.3f, 0, Mathf.Tau, 10, DarkSteel, 0.6f, true);
        c.DrawLine(panel + new Vector2(-4, -8), panel + new Vector2(-4, -9.8f), Castor, 0.5f);
        c.DrawCircle(panel + new Vector2(3.5f, -8), 2f, new Color("#2E6FB0"));
        c.DrawCircle(panel + new Vector2(3.5f, -3), 2f, new Color("#2B2F33"));
        c.DrawRect(new Rect2(panel + new Vector2(-7, -4.5f), new Vector2(6, 3)), Glass);

        var top = S(back.X, back.Y + 0.1, height);
        var heater = S(cx, cy, height - 2);
        c.DrawLine(top, heater, DarkSteel, 2.6f);
        IsoDraw.Prism(c, cx - 0.45, cy - 0.3, 0.9, 0.6, 4, new Color("#E4E9ED"), new Color("#B9C3CB"), lift: height - 6, rim: Colors.White);
        c.DrawLine(S(cx - 0.35, cy + 0.3, height - 6.5f), S(cx + 0.35, cy + 0.3, height - 6.5f), new Color("#F28C4A", 0.9f), 1.4f, true);
        c.DrawColoredPolygon(new[] { S(cx - 0.35, cy + 0.3, height - 7), S(cx + 0.35, cy + 0.3, height - 7), S(cx + 0.45, cy + 0.4, deck + 4), S(cx - 0.45, cy + 0.4, deck + 4) }, new Color("#FFB070", 0.06f));

        // A neonatal bag and mask hanging on the side rail.
        var hook = S(x + w, cy, deck + 2);
        c.DrawLine(hook, hook + new Vector2(2, 5), DarkSteel, 0.8f);
        c.DrawColoredPolygon(Ellipse(hook + new Vector2(2.5f, 9), 2.2f, 3.4f), new Color("#F2B134"));
        c.DrawCircle(hook + new Vector2(2.5f, 13.5f), 1.6f, new Color("#9ED0E6", 0.8f));
    }

    /// <summary>A big inflatable birthing ball with a sheen and a seam.</summary>
    private static void BirthingBall(CanvasItem c, (double X, double Y, double W, double L) whole, Color colour)
    {
        var (cx, cy) = (whole.X + (whole.W / 2), whole.Y + (whole.L / 2));
        var foot = S(cx, cy);
        c.DrawColoredPolygon(IsoDraw.FloorEllipse(foot + new Vector2(1, 1), 10), new Color(0, 0, 0, 0.18f));
        var centre = foot + new Vector2(0, -9);
        c.DrawCircle(centre, 9.5f, colour);
        c.DrawCircle(centre + new Vector2(2.5f, 2.5f), 7, colour.Darkened(0.12f));
        c.DrawArc(centre, 9.5f, 0, Mathf.Tau, 24, colour.Darkened(0.35f), 1f, true);
        c.DrawArc(centre + new Vector2(-2, -2.5f), 5.5f, Mathf.Pi * 1.05f, Mathf.Pi * 1.55f, 8, colour.Lightened(0.45f), 1.8f, true);
        c.DrawArc(centre + new Vector2(0, 12), 13, Mathf.Pi * 1.25f, Mathf.Pi * 1.75f, 12, colour.Darkened(0.2f), 0.7f, true);
    }

    /// <summary>A baby changing unit: a cupboard with a padded changing mat on top, nappies and wipes.</summary>
    private static void ChangingUnit(CanvasItem c, (double X, double Y, double W, double L) whole, float height, Color colour)
    {
        var (x, y, w, l) = whole;
        Shadow(c, x, y, w, l);
        IsoDraw.Prism(c, x, y, w, l, height - 4, colour.Lightened(0.08f), colour.Darkened(0.12f), skirting: colour.Darkened(0.45f), skirtingHeight: 3, rim: colour.Lightened(0.3f));
        foreach (var (a, b, _, _) in IsoDraw.VisibleFaces(x, y, w, l))
        {
            if ((b - a).Length() > 20)
            {
                var mid = a.Lerp(b, 0.5f);
                c.DrawLine(mid + new Vector2(0, -4), mid + new Vector2(0, -height + 6), colour.Darkened(0.3f), 0.8f);
                c.DrawCircle(mid + new Vector2(-2, -height / 2), 0.9f, DarkSteel);
                c.DrawCircle(mid + new Vector2(2, -height / 2), 0.9f, DarkSteel);
            }
        }

        // The padded mat with raised edges, in a cheerful print.
        IsoDraw.Prism(c, x + 0.08, y + 0.08, w - 0.16, l - 0.16, 3, new Color("#BFE3D6"), new Color("#93C7B4"), lift: height - 4, rim: new Color("#E2F4ED"));
        for (var i = 0; i < 4; i++)
        {
            var dot = S(x + 0.25 + (i * (w - 0.5) / 3), y + (l / 2), height);
            c.DrawCircle(dot, 1.1f, new Color("#F7E3A1"));
        }

        // A stack of nappies and a pack of wipes at one end.
        var end = S(x + w - 0.25, y + 0.2, height - 1);
        for (var i = 0; i < 3; i++)
        {
            Figures.RoundedRect(c, new Rect2(end + new Vector2(-4, -2 - (i * 2)), new Vector2(7, 2)), 0.8f, i % 2 == 0 ? Colors.White : new Color("#E6EEF5"));
        }

        Figures.RoundedRect(c, new Rect2(end + new Vector2(-12, -3), new Vector2(6, 3)), 1, new Color("#8FB8E5"));
    }

    /// <summary>A hospital breast pump on a wheeled stand, with its collection bottles and tubing.</summary>
    private static void BreastPump(CanvasItem c, (double X, double Y, double W, double L) whole, float height, Color colour)
    {
        var (cx, cy) = (whole.X + (whole.W / 2), whole.Y + (whole.L / 2));
        var foot = S(cx, cy);
        c.DrawColoredPolygon(IsoDraw.FloorEllipse(foot + new Vector2(1, 1), 9), new Color(0, 0, 0, 0.14f));
        for (var i = 0; i < 4; i++)
        {
            var a = (i * Mathf.Tau / 4) + 0.4f;
            var end = foot + new Vector2(Mathf.Cos(a) * 7, Mathf.Sin(a) * 3.5f);
            c.DrawLine(foot + new Vector2(0, -1), end, DarkSteel, 1.8f, true);
            c.DrawCircle(end, 1.4f, Castor);
        }

        var unit = foot + new Vector2(0, -height * 0.7f);
        c.DrawLine(foot, unit, Steel, 1.8f);
        Figures.RoundedRect(c, new Rect2(unit + new Vector2(-6, -6), new Vector2(12, 10)), 2.5f, colour);
        Figures.RoundedRect(c, new Rect2(unit + new Vector2(-4, -4), new Vector2(5, 3)), 0.8f, Glass);
        c.DrawCircle(unit + new Vector2(3.5f, -2.5f), 1.6f, new Color("#E58FB4"));
        foreach (var dx in new[] { -3f, 3f })
        {
            var bottle = unit + new Vector2(dx, 8);
            Figures.RoundedRect(c, new Rect2(bottle + new Vector2(-1.6f, 0), new Vector2(3.2f, 6)), 1, new Color("#F4F6F8", 0.9f));
            c.DrawRect(new Rect2(bottle + new Vector2(-1.6f, 3), new Vector2(3.2f, 3)), new Color("#F7EBC8", 0.9f));
            c.DrawLine(unit + new Vector2(dx * 0.5f, 3), bottle, new Color("#E8F2F8", 0.8f), 0.8f, true);
        }
    }

    private static Vector2[] Ellipse(Vector2 centre, float rx, float ry, int n = 14)
    {
        var points = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            var a = i * Mathf.Tau / n;
            points[i] = centre + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }

        return points;
    }
}
