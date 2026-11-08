using System;
using System.Linq;
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
        c.DrawPolyline(torso.Append(torso[0]).ToArray(), A(look.Top.Darkened(0.35f), alpha), 1f, true);

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
        c.DrawArc(head, 7.5f, 0, Mathf.Tau, 24, A(look.Skin.Darkened(0.3f), alpha), 1f, true);
        c.DrawColoredPolygon(HairCap(head), A(look.Hair, alpha));

        // Eyes and a hint of a smile.
        var eye = A(new Color("#2A211C"), alpha);
        c.DrawCircle(head + new Vector2(-2.6f, 1.2f), 0.95f, eye);
        c.DrawCircle(head + new Vector2(2.6f, 1.2f), 0.95f, eye);
        c.DrawLine(head + new Vector2(-1.4f, 4.3f), head + new Vector2(1.4f, 4.3f), A(look.Skin.Darkened(0.35f), alpha), 0.9f, true);

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
    public static void Lying(CanvasItem c, double gx, double gy, PersonLook look, Color blanket, float lift, bool headSouth = false)
    {
        var up = new Vector2(0, -lift - 1);
        LyingFigure(c, (along, across) => IsoDraw.ToScreen(gx + across, headSouth ? gy - along : gy + along) + up, look, blanket);
    }

    /// <summary>
    /// A patient lying under a blanket on a trolley that points along <paramref name="dir"/> (the way it is
    /// being pushed). Patients go feet first, so the head is at the back, by the porter.
    /// </summary>
    public static void LyingAlong(CanvasItem c, double gx, double gy, (double X, double Y) dir, PersonLook look, Color blanket, float lift)
    {
        var len = Math.Sqrt((dir.X * dir.X) + (dir.Y * dir.Y));
        var (fx, fy) = len < 1e-6 ? (0.0, 1.0) : (dir.X / len, dir.Y / len);
        var (nx, ny) = (-fy, fx);
        var up = new Vector2(0, -lift - 1);
        LyingFigure(c, (along, across) => IsoDraw.ToScreen(gx + (fx * along) + (nx * across), gy + (fy * along) + (ny * across)) + up, look, blanket);
    }

    /// <summary>
    /// One flat cover in the triage colour, rounded at the corners, with the white sheet turned down
    /// over it at the top and the head on the pillow. <paramref name="at"/> maps (along, across) to the
    /// screen: along runs from the head (negative) to the feet (positive).
    /// </summary>
    private static void LyingFigure(CanvasItem c, Func<double, double, Vector2> at, PersonLook look, Color blanket)
    {
        const double r = 0.2;
        const double head = -0.95;
        const double feet = 1.75;
        const double half = 0.55;

        // Corners as (across, along, start angle), going round in order.
        var corners = new[] { (-half + r, head + r, Math.PI), (half - r, head + r, 1.5 * Math.PI), (half - r, feet - r, 0.0), (-half + r, feet - r, 0.5 * Math.PI) };
        var cover = new Vector2[corners.Length * 5];
        var i = 0;
        foreach (var (across, along, start) in corners)
        {
            for (var k = 0; k < 5; k++)
            {
                var a = start + (Math.PI / 2 * (k / 4.0));
                cover[i++] = at(along + (Math.Sin(a) * r), across + (Math.Cos(a) * r));
            }
        }

        // A soft shadow where the body raises the cover, then the cover and its edge.
        c.DrawColoredPolygon(Array.ConvertAll(cover, p => p + new Vector2(1, 2)), new Color(0, 0, 0, 0.12f));
        c.DrawColoredPolygon(cover, blanket);
        c.DrawPolyline(cover.Append(cover[0]).ToArray(), blanket.Darkened(0.22f), 1f, true);

        // A gentle ridge down the middle where the legs are.
        c.DrawLine(at(0.35, -0.12), at(1.55, -0.12), new Color(blanket.Lightened(0.18f), 0.7f), 1.5f, true);
        c.DrawLine(at(0.35, 0.12), at(1.55, 0.12), new Color(blanket.Darkened(0.12f), 0.7f), 1.5f, true);

        // The sheet folded back over the top of the cover.
        var fold = head + 0.34;
        var sheet = new Vector2[12];
        for (var k = 0; k < 5; k++)
        {
            var a = Math.PI + (Math.PI / 2 * (k / 4.0));
            sheet[k] = at(head + r + (Math.Sin(a) * r), -half + r + (Math.Cos(a) * r));
        }

        for (var k = 0; k < 5; k++)
        {
            var a = (1.5 * Math.PI) + (Math.PI / 2 * (k / 4.0));
            sheet[5 + k] = at(head + r + (Math.Sin(a) * r), half - r + (Math.Cos(a) * r));
        }

        sheet[10] = at(fold, half);
        sheet[11] = at(fold, -half);
        c.DrawColoredPolygon(sheet, new Color("#E8F0F6"));
        c.DrawLine(at(fold, -half), at(fold, half), new Color("#C9D3DC"), 1.2f, true);

        // Head on the pillow, with a little shadow under it.
        var face = at(-1.45, 0) + new Vector2(0, -3);
        c.DrawColoredPolygon(IsoDraw.FloorEllipse(face + new Vector2(1, 5), 8), new Color(0, 0, 0, 0.1f));
        c.DrawCircle(face, 7.5f, look.Skin);
        c.DrawArc(face, 7.5f, 0, Mathf.Tau, 24, look.Skin.Darkened(0.25f), 1f, true);
        c.DrawColoredPolygon(HairCap(face), look.Hair);
    }

    /// <summary>A patient trolley (stretcher) pointing along <paramref name="dir"/>. Returns the mattress height.</summary>
    public static float Trolley(CanvasItem c, double gx, double gy, (double X, double Y) dir)
    {
        const float frame = 12f;
        var len = Math.Sqrt((dir.X * dir.X) + (dir.Y * dir.Y));
        var (fx, fy) = len < 1e-6 ? (0.0, 1.0) : (dir.X / len, dir.Y / len);
        var (nx, ny) = (-fy, fx);

        // Wheels at the corners, a slim frame, the mattress, and a push bar at the head end.
        foreach (var (along, across) in new[] { (-1.7, -0.55), (-1.7, 0.55), (1.7, -0.55), (1.7, 0.55) })
        {
            IsoDraw.OrientedBox(c, gx + (fx * along) + (nx * across), gy + (fy * along) + (ny * across), dir, 0.2, 0.2, 3, new Color("#2F363C"));
        }

        IsoDraw.OrientedBox(c, gx, gy, dir, 0.3, 0.3, frame - 4, new Color("#8C979F"), 3);
        IsoDraw.OrientedBox(c, gx, gy, dir, 4.0, 1.4, 3, new Color("#AEB8C1"), frame - 4);
        IsoDraw.OrientedBox(c, gx, gy, dir, 3.8, 1.25, 4, new Color("#EEF2F5"), frame - 1);
        IsoDraw.OrientedBox(c, gx - (fx * 2.0), gy - (fy * 2.0), dir, 0.15, 1.4, 12, new Color("#7F8A93"), frame);
        return frame + 3;
    }

    /// <summary>A wheelchair whose seat centre is at <paramref name="foot"/>, drawn behind and beside the person in it.</summary>
    public static void WheelchairBack(CanvasItem c, Vector2 foot)
    {
        c.DrawColoredPolygon(IsoDraw.FloorEllipse(foot + new Vector2(0, 2), 15), new Color(0, 0, 0, 0.2f));
        var frame = new Color("#3A4148");
        c.DrawRect(new Rect2(foot + new Vector2(-10, -38), new Vector2(20, 22)), new Color("#2B4A6B"));
        c.DrawLine(foot + new Vector2(-10, -42), foot + new Vector2(-10, -12), frame, 2f);
        c.DrawLine(foot + new Vector2(10, -42), foot + new Vector2(10, -12), frame, 2f);
        c.DrawRect(new Rect2(foot + new Vector2(-10, -15), new Vector2(20, 5)), new Color("#2B4A6B"));
    }

    /// <summary>The wheelchair's big wheels and footplate, drawn in front of the person.</summary>
    public static void WheelchairFront(CanvasItem c, Vector2 foot)
    {
        var tyre = new Color("#22272B");
        foreach (var side in new[] { -1f, 1f })
        {
            var hub = foot + new Vector2(side * 12, -9);
            c.DrawArc(hub, 9, 0, Mathf.Tau, 20, tyre, 2.5f, true);
            c.DrawArc(hub, 7, 0, Mathf.Tau, 16, new Color("#9AA5AE"), 1f, true);
            c.DrawCircle(hub, 1.6f, new Color("#9AA5AE"));
        }

        c.DrawLine(foot + new Vector2(-6, 3), foot + new Vector2(6, 3), new Color("#3A4148"), 2f);
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
        foreach (var (wx, wy) in new[] { (x + 0.1, y + 0.15), (x + 1.35, y + 0.15), (x + 0.1, y + 3.9), (x + 1.35, y + 3.9) })
        {
            IsoDraw.Box(c, wx, wy, 0.15, 0.15, 4, new Color("#2F353B"), shadow: false);
        }

        c.DrawColoredPolygon(new[] { IsoDraw.ToScreen(x - 0.05, y - 0.05), IsoDraw.ToScreen(x + 1.65, y - 0.05), IsoDraw.ToScreen(x + 1.65, y + 4.25), IsoDraw.ToScreen(x - 0.05, y + 4.25) }, new Color(0, 0, 0, 0.13f));
        IsoDraw.Box(c, x + 0.35, y + 0.4, 0.9, 3.4, 5, new Color("#6F7B85"), 3, shadow: false);
        IsoDraw.Prism(c, x, y, 1.6, 4.2, 4, new Color("#B7C2CB"), new Color("#9AA6B0"), lift: deck - 4, rim: new Color("#CDD5DC"));
        IsoDraw.Prism(c, x + 0.06, y + 0.06, 1.48, 4.08, mattress, new Color("#F4F7F9"), new Color("#D8E0E7"), lift: deck, rim: new Color("#E1E7EC"));

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
            // A half-length cot side at the head end, on posts.
            var rx = west ? x - 0.02 : x + 1.6 - 0.08;
            var (r0, r1) = headSouth ? (y + 2.0, y + 3.8) : (y + 0.4, y + 2.2);
            var lift = top + 5;
            IsoDraw.Prism(c, rx, r0, 0.1, r1 - r0, 2.5f, new Color("#D5DDE3"), new Color("#AEB9C2"), lift: lift);
            foreach (var py in new[] { r0 + 0.2, r1 - 0.2 })
            {
                var p = IsoDraw.ToScreen(rx + 0.05, py);
                c.DrawLine(p + new Vector2(0, -deck), p + new Vector2(0, -lift), new Color("#AEB9C2"), 1.5f);
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
