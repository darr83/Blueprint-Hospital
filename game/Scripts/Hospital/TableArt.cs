using System;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// The tables and couches patients lie on in the service rooms, each drawn for its job: a padded
/// treatment couch, the X-ray table with its film tray, the CT scanner's sliding cradle, the
/// operating table on its column, and the long cath lab table. All lie north to south, head north.
/// </summary>
public static class TableArt
{
    private static readonly Color Steel = new("#AEB8C1");
    private static readonly Color Dark = new("#3A434B");

    /// <summary>Draws the table for a furniture id, centred on (gx, gy); returns the height its top is at.</summary>
    public static float Draw(CanvasItem c, string id, double gx, double gy, Color colour)
    {
        Shadow(c, gx, gy);
        return id switch
        {
            "xray_table" => XRay(c, gx, gy),
            "ct_table" => Ct(c, gx, gy, colour),
            "operating_table" => Operating(c, gx, gy, colour),
            "cath_lab_table" => CathLab(c, gx, gy),
            _ => Couch(c, gx, gy, colour),
        };
    }

    private static void Shadow(CanvasItem c, double gx, double gy) =>
        c.DrawColoredPolygon(new[] { IsoDraw.ToScreen(gx - 0.75, gy - 2.0) + new Vector2(3, 3), IsoDraw.ToScreen(gx + 0.85, gy - 2.0) + new Vector2(3, 3), IsoDraw.ToScreen(gx + 0.85, gy + 2.15) + new Vector2(3, 3), IsoDraw.ToScreen(gx - 0.75, gy + 2.15) + new Vector2(3, 3) }, new Color(0, 0, 0, 0.12f));

    private static void Box(CanvasItem c, double x, double y, double w, double l, float h, Color top, float lift = 0, Color? face = null, Color? rim = null) =>
        IsoDraw.Prism(c, x, y, w, l, h, top, face ?? top.Darkened(0.16f), lift: lift, rim: rim);

    private static void Quad(CanvasItem c, double x, double y, double w, double l, float lift, Color colour)
    {
        var up = new Vector2(0, -lift);
        c.DrawColoredPolygon(new[] { IsoDraw.ToScreen(x, y) + up, IsoDraw.ToScreen(x + w, y) + up, IsoDraw.ToScreen(x + w, y + l) + up, IsoDraw.ToScreen(x, y + l) + up }, colour);
    }

    private static void Line(CanvasItem c, double x0, double y0, double x1, double y1, float lift, Color colour, float width = 1f) =>
        c.DrawLine(IsoDraw.ToScreen(x0, y0) + new Vector2(0, -lift), IsoDraw.ToScreen(x1, y1) + new Vector2(0, -lift), colour, width, true);

    /// <summary>Draws boxes back to front, so the nearer ones cover the ones behind in every view.</summary>
    private static void InOrder(params (double X, double Y, Action Draw)[] parts)
    {
        Array.Sort(parts, (a, b) => IsoDraw.ToScreen(a.X, a.Y).Y.CompareTo(IsoDraw.ToScreen(b.X, b.Y).Y));
        foreach (var part in parts)
        {
            part.Draw();
        }
    }

    private static void Castors(CanvasItem c, double gx, double gy, double halfW, double halfL)
    {
        foreach (var (ox, oy) in new[] { (-halfW, -halfL), (halfW, -halfL), (halfW, halfL), (-halfW, halfL) })
        {
            c.DrawCircle(IsoDraw.ToScreen(gx + ox, gy + oy) + new Vector2(0, -1.6f), 2f, new Color("#22272B"));
        }
    }

    /// <summary>A white pillow at the head end.</summary>
    private static void Pillow(CanvasItem c, double gx, double gy, float lift) =>
        Box(c, gx - 0.5, gy - 1.9, 1.0, 0.62, 2.5f, new Color("#FBFCFD"), lift, new Color("#DDE5EA"), Colors.White);

    /// <summary>A leg at each corner on a castor, joined by a low rail along each long side.</summary>
    private static void Frame(CanvasItem c, double gx, double gy, double halfW, double halfL, float height, Color leg)
    {
        var corners = new[] { (-halfW, -halfL), (halfW, -halfL), (halfW, halfL), (-halfW, halfL) };
        var parts = new (double, double, Action)[6];
        for (var i = 0; i < 4; i++)
        {
            var (ox, oy) = corners[i];
            parts[i] = (gx + ox, gy + oy, () =>
            {
                c.DrawCircle(IsoDraw.ToScreen(gx + ox, gy + oy) + new Vector2(0, -1.8f), 2.2f, new Color("#22272B"));
                Box(c, gx + ox - 0.07, gy + oy - 0.07, 0.14, 0.14, height - 3, leg, 3);
            });
        }

        parts[4] = (gx - halfW, gy, () => Box(c, gx - halfW - 0.04, gy - halfL, 0.08, 2 * halfL, 1.6f, leg, 6));
        parts[5] = (gx + halfW, gy, () => Box(c, gx + halfW - 0.04, gy - halfL, 0.08, 2 * halfL, 1.6f, leg, 6));
        InOrder(parts);
    }

    /// <summary>A treatment or scanning couch: a wheeled steel frame with a foot pedal, a thick padded top with a raised head section, a pillow and couch roll.</summary>
    private static float Couch(CanvasItem c, double gx, double gy, Color colour)
    {
        const float top = 21f;
        Frame(c, gx, gy, 0.6, 1.8, 14, Steel);

        // The lifting motor under the middle, and its foot pedal sticking out at the side.
        InOrder(
            (gx, gy, () => Box(c, gx - 0.3, gy - 0.5, 0.6, 1.0, 7, Dark, 5)),
            (gx + 0.75, gy + 0.4, () => Box(c, gx + 0.62, gy + 0.25, 0.3, 0.3, 1.5f, new Color("#F9C22E"), 1)),
            (gx - 0.75, gy + 0.4, () => Box(c, gx - 0.92, gy + 0.25, 0.3, 0.3, 1.5f, new Color("#F9C22E"), 1)));
        Box(c, gx - 0.68, gy - 1.98, 1.36, 3.96, 2f, new Color("#8C979F"), 13.5f, new Color("#6F7B85"));
        var pad = colour.Lightened(0.06f);
        InOrder(
            (gx, gy + 0.55, () =>
            {
                Box(c, gx - 0.72, gy - 0.9, 1.44, 2.95, 5.5f, pad, 15.5f, colour.Darkened(0.14f), colour.Lightened(0.35f));

                // The paper couch roll pulled down the pad.
                Quad(c, gx - 0.46, gy - 0.9, 0.92, 2.8, top + 0.2f, new Color("#F6F8F4"));
                Line(c, gx - 0.46, gy + 0.9, gx + 0.46, gy + 0.9, top + 0.2f, new Color("#D9DDD6"), 0.7f);
            }),
            (gx, gy - 1.5, () =>
            {
                Box(c, gx - 0.72, gy - 2.05, 1.44, 1.12, 7.5f, pad.Lightened(0.05f), 15.5f, colour.Darkened(0.1f), colour.Lightened(0.35f));
                Pillow(c, gx, gy, 23f);
            }));

        // The roll on its holder at the foot.
        var roll = IsoDraw.ToScreen(gx, gy + 2.1) + new Vector2(0, -17);
        var across = (IsoDraw.ToScreen(gx + 0.45, gy + 2.1) - IsoDraw.ToScreen(gx - 0.45, gy + 2.1)) / 2;
        c.DrawLine(roll - across, roll + across, new Color("#F6F8F4"), 4.4f, true);
        c.DrawLine(roll - across, roll + across, new Color("#D9DDD6"), 1.1f, true);
        return top;
    }

    /// <summary>An X-ray table: a white pedestal with the film tray in its side, and a floating top with a black mat.</summary>
    private static float XRay(CanvasItem c, double gx, double gy)
    {
        const float top = 22f;
        var white = new Color("#EEF2F5");
        IsoDraw.Prism(c, gx - 0.66, gy - 1.6, 1.32, 3.2, 16.5f, white, new Color("#D3DBE2"), skirting: Dark, skirtingHeight: 3f);
        foreach (var face in IsoFace.Of(gx - 0.66, gy - 1.6, 1.32, 3.2, 16.5f))
        {
            if (face.Side is Sides.East or Sides.West)
            {
                // The film (bucky) tray with its handle, a control strip and a yellow foot switch.
                face.Quad(c, 0.25f, 8.5f, 0.75f, 13.5f, new Color("#3A434B"));
                face.Line(c, 0.44f, 11f, 0.56f, 11f, Steel, 1.5f);
                face.Quad(c, 0.08f, 9.5f, 0.18f, 12.5f, new Color("#7FA7C4"));
                face.Quad(c, 0.84f, 3.5f, 0.94f, 5.5f, new Color("#F9C22E"));
            }
            else
            {
                face.Quad(c, 0.2f, 6f, 0.8f, 7.2f, new Color("#7FA7C4"));
            }
        }

        Box(c, gx - 0.8, gy - 2.05, 1.6, 4.1, 3.5f, white, 18.5f, new Color("#C8D2DA"), Colors.White);
        Quad(c, gx - 0.62, gy - 1.88, 1.24, 3.76, top + 0.2f, new Color("#46525D"));
        Quad(c, gx - 0.58, gy - 1.84, 1.16, 3.68, top + 0.25f, new Color("#56636F"));
        Line(c, gx, gy - 1.84, gx, gy + 1.84, top + 0.3f, new Color(1, 1, 1, 0.22f), 0.8f);
        Line(c, gx - 0.58, gy, gx + 0.58, gy, top + 0.3f, new Color(1, 1, 1, 0.22f), 0.8f);
        Line(c, gx - 0.8, gy - 1.6, gx - 0.8, gy + 1.6, top + 1.5f, Steel, 1.3f);
        Line(c, gx + 0.8, gy - 1.6, gx + 0.8, gy + 1.6, top + 1.5f, Steel, 1.3f);
        return top;
    }

    /// <summary>The CT scanner's table: a pedestal carrying a slim dished cradle with a blue pad and a head rest.</summary>
    private static float Ct(CanvasItem c, double gx, double gy, Color colour)
    {
        const float top = 21f;
        IsoDraw.Prism(c, gx - 0.56, gy - 0.6, 1.12, 2.6, 15.5f, colour, colour.Darkened(0.1f), skirting: Dark, skirtingHeight: 3f, rim: Colors.White);
        foreach (var face in IsoFace.Of(gx - 0.56, gy - 0.6, 1.12, 2.6, 15.5f))
        {
            face.Quad(c, 0.08f, 6.5f, 0.92f, 7.8f, new Color("#7FA7C4"));
            face.Quad(c, 0.8f, 10f, 0.9f, 12.5f, new Color("#3A434B"));
        }

        Box(c, gx - 0.64, gy - 2.05, 1.28, 4.1, 4f, colour.Lightened(0.05f), 17f, colour.Darkened(0.14f), Colors.White);
        Quad(c, gx - 0.47, gy - 1.95, 0.94, 3.9, top + 0.2f, new Color("#7FA7C4"));
        Quad(c, gx - 0.3, gy - 1.95, 0.6, 3.9, top + 0.3f, new Color("#8FB5CF"));
        InOrder(
            (gx - 0.4, gy - 1.7, () => Box(c, gx - 0.46, gy - 1.95, 0.14, 0.5, 3f, new Color("#56616B"), top)),
            (gx + 0.4, gy - 1.7, () => Box(c, gx + 0.32, gy - 1.95, 0.14, 0.5, 3f, new Color("#56616B"), top)));
        return top;
    }

    /// <summary>An operating table: a heavy base and column, four padded sections, side rails and an arm board.</summary>
    private static float Operating(CanvasItem c, double gx, double gy, Color colour)
    {
        const float top = 23f;
        var pad = colour.Darkened(0.35f);
        IsoDraw.Prism(c, gx - 0.6, gy - 1.0, 1.2, 2.2, 3.5f, new Color("#56616B"), Dark, rim: new Color("#7D8A95"));
        Box(c, gx - 0.36, gy - 0.5, 0.72, 1.1, 14f, new Color("#C3CCD3"), 3.5f, Steel);
        foreach (var face in IsoFace.Of(gx - 0.36, gy - 0.5, 0.72, 1.1, 14f, 3.5f))
        {
            // The telescopic column's rings.
            face.Line(c, 0, 4.5f, 1, 4.5f, new Color("#8C979F"), 0.8f);
            face.Line(c, 0, 9f, 1, 9f, new Color("#8C979F"), 0.8f);
        }

        Box(c, gx - 0.56, gy - 1.85, 1.12, 3.7, 1.8f, Steel, 17.2f, new Color("#8C979F"));

        // The pad in sections: head, back, seat and legs, with a gap at each joint.
        var sections = new (double From, double To, double Half)[] { (-2.05, -1.5, 0.45), (-1.42, -0.2, 0.62), (-0.12, 0.7, 0.62), (0.78, 2.05, 0.62) };
        var parts = new (double, double, Action)[sections.Length + 1];
        for (var i = 0; i < sections.Length; i++)
        {
            var s = sections[i];
            parts[i] = (gx, gy + ((s.From + s.To) / 2), () => Box(c, gx - s.Half, gy + s.From, 2 * s.Half, s.To - s.From, 4, pad.Lightened(0.08f), 19, pad.Darkened(0.2f), pad.Lightened(0.35f)));
        }

        // The arm board out to one side at the shoulder.
        parts[^1] = (gx + 1.1, gy - 1.0, () => Box(c, gx + 0.62, gy - 1.2, 0.95, 0.38, 2.5f, pad.Lightened(0.08f), 20, pad.Darkened(0.2f), pad.Lightened(0.35f)));
        InOrder(parts);
        Line(c, gx - 0.66, gy - 1.42, gx - 0.66, gy + 2.05, 20f, Steel.Lightened(0.15f), 1.4f);
        Line(c, gx + 0.66, gy - 0.75, gx + 0.66, gy + 2.05, 20f, Steel.Lightened(0.15f), 1.4f);
        return top;
    }

    /// <summary>The cath lab table: a long dark carbon-fibre top reaching out from a pedestal at the foot end, with a thin blue mattress.</summary>
    private static float CathLab(CanvasItem c, double gx, double gy)
    {
        const float top = 21f;
        var white = new Color("#EEF2F5");
        IsoDraw.Prism(c, gx - 0.6, gy + 0.6, 1.2, 1.5, 17.5f, white, new Color("#D3DBE2"), skirting: Dark, skirtingHeight: 2.5f, rim: Colors.White);
        foreach (var face in IsoFace.Of(gx - 0.6, gy + 0.6, 1.2, 1.5, 17.5f))
        {
            face.Quad(c, 0.15f, 6f, 0.85f, 7.2f, new Color("#AD1457"));
        }

        Box(c, gx - 0.64, gy - 2.2, 1.28, 4.3, 3f, new Color("#3F4A54"), 17.5f, new Color("#2B343C"), new Color("#66737E"));
        Quad(c, gx - 0.52, gy - 2.1, 1.04, 4.0, top - 0.3f, new Color("#7FA7C4"));
        Quad(c, gx - 0.52, gy - 2.1, 1.04, 0.7, top - 0.2f, new Color("#9BBCD3"));

        // The control box clipped to the side rail at the foot end.
        Box(c, gx + 0.64, gy + 1.2, 0.22, 0.6, 4, new Color("#2B343C"), 16.5f);
        var buttons = IsoDraw.ToScreen(gx + 0.75, gy + 1.5) + new Vector2(0, -20.8f);
        c.DrawCircle(buttons + new Vector2(-1.6f, 0), 0.9f, new Color("#5CF08A"));
        c.DrawCircle(buttons + new Vector2(1.6f, 0.8f), 0.9f, new Color("#F9C22E"));
        return top;
    }
}
