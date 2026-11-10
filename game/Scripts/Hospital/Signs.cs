using System;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>The little pictures used on signs.</summary>
public enum Pictogram
{
    None, Bed, Cross, Heart, Pulse, Pill, Cup, Cutlery, Man, Woman, Wheelchair, Bone, Baby, Brain, Dumbbell, Info, Staff, Chair,
    Lift, Stairs, Scalpel, Radiation, Waves, Clipboard, Scope, Mug, Monitor, Drop,
}

/// <summary>How the name board over a door is styled.</summary>
public enum DoorSignStyle
{
    /// <summary>White board, a stripe and an icon tile in the room's colour, dark lettering.</summary>
    Stripe,

    /// <summary>The whole board in the room's colour with white lettering and a white icon disc.</summary>
    Fascia,

    /// <summary>A dark slate board with a block of the room's colour holding the icon, white lettering.</summary>
    Slate,
}

/// <summary>
/// The hospital's signs, designed flat (see <see cref="Flat"/>): door name boards, and the wall
/// signs: directions, toilets, pharmacy, fire exit, radiation warnings, staff only and so on.
/// One family: the same lettering, the same pictograms, the room's wayfinding colour.
/// </summary>
public static class Signs
{
    public static DoorSignStyle DoorStyle { get; set; } = DoorSignStyle.Fascia;

    public const float BoardHeight = 23f;
    private const int NameSize = 15;

    private static readonly Color Ink = new("#1D2B36");
    private static readonly Color Paper = new("#FBFCFD");
    private static readonly Color Blue = new("#005EB8");
    private static readonly Color Green = new("#1E8E3E");
    private static readonly Color Red = new("#D5281B");
    private static readonly Color Yellow = new("#FFD600");

    /// <summary>The pictogram that goes with a room.</summary>
    public static Pictogram IconFor(CareSpace space) => space.Kind switch
    {
        CareSpaceKind.Waiting => Pictogram.Chair,
        CareSpaceKind.Triage => Pictogram.Clipboard,
        CareSpaceKind.Minors or CareSpaceKind.Majors => Pictogram.Bed,
        CareSpaceKind.Resus => Pictogram.Pulse,
        CareSpaceKind.NursesStation => Pictogram.Cross,
        CareSpaceKind.DoctorsOffice or CareSpaceKind.OutreachOffice => Pictogram.Staff,
        CareSpaceKind.StaffRoom => Pictogram.Mug,
        CareSpaceKind.Ward => space.TemplateId switch
        {
            "intensive_care_unit" or "high_dependency_unit" or "coronary_care_unit" => Pictogram.Monitor,
            "mother_baby_ward" or "neonatal_unit" => Pictogram.Baby,
            _ => Pictogram.Bed,
        },
        CareSpaceKind.XRay or CareSpaceKind.PlasterRoom => Pictogram.Bone,
        CareSpaceKind.CT => Pictogram.Radiation,
        CareSpaceKind.Theatre => Pictogram.Scalpel,
        CareSpaceKind.Pharmacy => Pictogram.Pill,
        CareSpaceKind.Clinic => space.TemplateId switch
        {
            "cardiology_clinic" => Pictogram.Heart,
            "neurology_clinic" => Pictogram.Brain,
            "fracture_clinic" => Pictogram.Bone,
            "antenatal_clinic" => Pictogram.Baby,
            "antenatal_scan_room" => Pictogram.Waves,
            _ => Pictogram.Clipboard,
        },
        CareSpaceKind.OutpatientReception => Pictogram.Info,
        CareSpaceKind.Echo or CareSpaceKind.CathLab => Pictogram.Heart,
        CareSpaceKind.Eeg => Pictogram.Brain,
        CareSpaceKind.Ultrasound => Pictogram.Waves,
        CareSpaceKind.Endoscopy => Pictogram.Scope,
        CareSpaceKind.EndoscopyRecovery => Pictogram.Bed,
        CareSpaceKind.PhysioGym => Pictogram.Dumbbell,
        CareSpaceKind.Cafeteria => Pictogram.Cutlery,
        CareSpaceKind.VendingPoint => Pictogram.Cup,
        CareSpaceKind.Toilets => Pictogram.Man,
        CareSpaceKind.AccessibleToilet => Pictogram.Wheelchair,
        CareSpaceKind.StaffToilet => Pictogram.Staff,
        CareSpaceKind.Lift => Pictogram.Lift,
        CareSpaceKind.Stairs => Pictogram.Stairs,
        CareSpaceKind.DeliveryRoom => Pictogram.Baby,
        _ => Pictogram.None,
    };

    // ------------------------------------------------------------------ door name boards

    /// <summary>How long a name board needs to be for its name, in pixels.</summary>
    public static float BoardLength(string name, Pictogram icon) =>
        Flat.Width(name, NameSize) + 16 + (icon == Pictogram.None ? 0 : BoardHeight);

    /// <summary>The top and face colours of the board's little slab, to suit the style.</summary>
    public static (Color Top, Color Face) BoardColours(DoorSignStyle style, Color colour) => style switch
    {
        DoorSignStyle.Fascia => (colour.Lightened(0.25f), colour),
        DoorSignStyle.Slate => (new Color("#3A4A58"), new Color("#22303C")),
        _ => (new Color("#E9EEF2"), Paper),
    };

    /// <summary>Paints a name board's face: (0, 0) is its top left, it is <paramref name="length"/> by <see cref="BoardHeight"/>.</summary>
    public static void NameBoard(CanvasItem c, DoorSignStyle style, string name, Color colour, Pictogram icon, float length)
    {
        const float h = BoardHeight;
        var hasIcon = icon != Pictogram.None;
        var textWidth = Flat.Width(name, NameSize);
        var iconSpace = hasIcon ? h : 0;
        var textCentre = iconSpace + ((length - iconSpace) / 2);
        switch (style)
        {
            case DoorSignStyle.Fascia:
                Flat.Rect(c, 0, 0, length, h, colour);
                Flat.Rect(c, 0, h - 2, length, 2, colour.Darkened(0.25f));
                Flat.Rect(c, 0, 0, length, 1, colour.Lightened(0.3f));
                if (hasIcon)
                {
                    c.DrawCircle(new Vector2((h / 2) + 1, (h / 2) - 0.5f), (h / 2) - 3.5f, Paper);
                    Draw(c, icon, (h / 2) + 1, (h / 2) - 0.5f, h - 11.5f, colour.Darkened(0.1f), Paper);
                }

                Flat.Text(c, name, textCentre, h - 6.6f, NameSize, Colors.White);
                break;

            case DoorSignStyle.Slate:
                Flat.Rect(c, 0, 0, length, h, new Color("#22303C"));
                Flat.Rect(c, 0, h - 2.5f, length, 2.5f, colour);
                if (hasIcon)
                {
                    Flat.Rect(c, 0, 0, h, h, colour);
                    Draw(c, icon, h / 2, h / 2, h - 8f, Colors.White, colour);
                }

                Flat.Text(c, name, textCentre, h - 7.4f, NameSize, Colors.White);
                break;

            default:
                Flat.Rect(c, 0, 0, length, h, Paper);
                Flat.Rect(c, 0, 0, length, 3.2f, colour);
                Flat.Rect(c, 0, h - 1, length, 1, new Color("#C9D3DC"));
                if (hasIcon)
                {
                    Flat.Round(c, 3.5f, 5.6f, h - 8.6f, h - 8.6f, 3f, colour);
                    Draw(c, icon, 3.5f + ((h - 8.6f) / 2), 5.6f + ((h - 8.6f) / 2), h - 12.5f, Colors.White, colour);
                }

                Flat.Text(c, name, textCentre, h - 5.4f, NameSize, Ink);
                break;
        }
    }

    // ------------------------------------------------------------------ pictograms

    /// <summary>A pictogram in a square of side <paramref name="s"/> centred on (x, y). <paramref name="ground"/> is the colour behind it, for cut-outs.</summary>
    public static void Draw(CanvasItem c, Pictogram icon, float x, float y, float s, Color ink, Color ground)
    {
        Vector2 P(float px, float py) => new(x + (px * s), y + (py * s));
        void R(float px, float py, float w, float h, Color? col = null) => Flat.Rect(c, x + (px * s), y + (py * s), w * s, h * s, col ?? ink);
        void Dot(float px, float py, float r, Color? col = null) => c.DrawCircle(P(px, py), r * s, col ?? ink);
        void L(float x0, float y0, float x1, float y1, float w, Color? col = null) => c.DrawLine(P(x0, y0), P(x1, y1), col ?? ink, Math.Max(0.7f, w * s), true);
        var thin = Math.Max(0.7f, 0.1f * s);
        switch (icon)
        {
            case Pictogram.Bed:
                R(-0.5f, -0.32f, 0.11f, 0.7f);
                R(-0.5f, 0.06f, 1f, 0.17f);
                R(0.39f, -0.06f, 0.11f, 0.44f);
                Dot(-0.22f, -0.12f, 0.13f);
                R(-0.04f, -0.2f, 0.43f, 0.22f);
                break;

            case Pictogram.Cross:
                R(-0.16f, -0.46f, 0.32f, 0.92f);
                R(-0.46f, -0.16f, 0.92f, 0.32f);
                break;

            case Pictogram.Heart:
                Dot(-0.21f, -0.14f, 0.26f);
                Dot(0.21f, -0.14f, 0.26f);
                c.DrawColoredPolygon(new[] { P(-0.45f, -0.03f), P(0.45f, -0.03f), P(0, 0.46f) }, ink);
                break;

            case Pictogram.Pulse:
                c.DrawPolyline(new[] { P(-0.5f, 0.05f), P(-0.22f, 0.05f), P(-0.1f, -0.4f), P(0.06f, 0.42f), P(0.2f, -0.12f), P(0.28f, 0.05f), P(0.5f, 0.05f) }, ink, Math.Max(0.9f, 0.13f * s), true);
                break;

            case Pictogram.Monitor:
                Flat.Round(c, x - (0.48f * s), y - (0.4f * s), 0.96f * s, 0.66f * s, 0.1f * s, ink);
                R(-0.38f, -0.3f, 0.76f, 0.46f, ground);
                c.DrawPolyline(new[] { P(-0.34f, -0.06f), P(-0.14f, -0.06f), P(-0.06f, -0.24f), P(0.04f, 0.1f), P(0.12f, -0.06f), P(0.34f, -0.06f) }, ink, thin, true);
                R(-0.06f, 0.26f, 0.12f, 0.14f);
                R(-0.28f, 0.38f, 0.56f, 0.1f);
                break;

            case Pictogram.Pill:
                Flat.Round(c, x - (0.48f * s), y - (0.2f * s), 0.96f * s, 0.4f * s, 0.2f * s, ink);
                Flat.Round(c, x + (0.02f * s), y - (0.12f * s), 0.38f * s, 0.24f * s, 0.12f * s, ground);
                break;

            case Pictogram.Cup:
                c.DrawColoredPolygon(new[] { P(-0.3f, -0.2f), P(0.3f, -0.2f), P(0.22f, 0.46f), P(-0.22f, 0.46f) }, ink);
                R(-0.36f, -0.32f, 0.72f, 0.12f);
                L(0.02f, -0.34f, 0.2f, -0.52f, 0.08f);
                break;

            case Pictogram.Mug:
                Flat.Round(c, x - (0.36f * s), y - (0.16f * s), 0.58f * s, 0.6f * s, 0.1f * s, ink);
                c.DrawArc(P(0.24f, 0.12f), 0.17f * s, -1.5f, 1.5f, 8, ink, thin, true);
                L(-0.2f, -0.28f, -0.14f, -0.46f, 0.08f);
                L(0f, -0.28f, 0.06f, -0.46f, 0.08f);
                break;

            case Pictogram.Cutlery:
                L(-0.22f, -0.45f, -0.22f, 0.46f, 0.1f);
                L(-0.36f, -0.45f, -0.36f, -0.12f, 0.07f);
                L(-0.08f, -0.45f, -0.08f, -0.12f, 0.07f);
                L(-0.36f, -0.12f, -0.08f, -0.12f, 0.09f);
                L(0.26f, -0.1f, 0.26f, 0.46f, 0.1f);
                c.DrawColoredPolygon(new[] { P(0.2f, -0.46f), P(0.38f, -0.3f), P(0.38f, 0.02f), P(0.2f, 0.02f) }, ink);
                break;

            case Pictogram.Man:
            case Pictogram.Staff:
                Dot(0, -0.36f, 0.13f);
                Flat.Round(c, x - (0.2f * s), y - (0.2f * s), 0.4f * s, 0.4f * s, 0.07f * s, ink);
                R(-0.17f, 0.16f, 0.14f, 0.34f);
                R(0.03f, 0.16f, 0.14f, 0.34f);
                if (icon == Pictogram.Staff)
                {
                    // A lanyard and badge.
                    L(-0.1f, -0.2f, 0, -0.02f, 0.05f, ground);
                    L(0.1f, -0.2f, 0, -0.02f, 0.05f, ground);
                    R(-0.06f, -0.02f, 0.12f, 0.12f, ground);
                }

                break;

            case Pictogram.Woman:
                Dot(0, -0.36f, 0.13f);
                c.DrawColoredPolygon(new[] { P(-0.12f, -0.2f), P(0.12f, -0.2f), P(0.3f, 0.24f), P(-0.3f, 0.24f) }, ink);
                R(-0.14f, 0.22f, 0.1f, 0.28f);
                R(0.04f, 0.22f, 0.1f, 0.28f);
                break;

            case Pictogram.Wheelchair:
                Dot(-0.12f, -0.38f, 0.11f);
                L(-0.12f, -0.24f, -0.1f, 0.1f, 0.12f);
                L(-0.1f, 0.1f, 0.2f, 0.1f, 0.11f);
                L(0.2f, 0.1f, 0.32f, 0.42f, 0.11f);
                L(-0.1f, -0.1f, 0.14f, -0.1f, 0.09f);
                c.DrawArc(P(-0.08f, 0.2f), 0.27f * s, 0.2f, Mathf.Pi + 1.2f, 14, ink, thin, true);
                break;

            case Pictogram.Bone:
                L(-0.26f, 0.26f, 0.26f, -0.26f, 0.15f);
                foreach (var (bx, by) in new[] { (-0.36f, 0.22f), (-0.22f, 0.36f), (0.36f, -0.22f), (0.22f, -0.36f) })
                {
                    Dot(bx, by, 0.12f);
                }

                break;

            case Pictogram.Baby:
                Dot(0, -0.22f, 0.2f);
                Flat.Round(c, x - (0.26f * s), y - (0.04f * s), 0.52f * s, 0.5f * s, 0.2f * s, ink);
                L(-0.12f, 0.12f, 0.12f, 0.34f, 0.06f, ground);
                L(0.12f, 0.12f, -0.12f, 0.34f, 0.06f, ground);
                break;

            case Pictogram.Brain:
                foreach (var (bx, by) in new[] { (-0.22f, -0.12f), (0.02f, -0.22f), (0.24f, -0.08f), (-0.14f, 0.14f), (0.16f, 0.14f) })
                {
                    Dot(bx, by, 0.22f);
                }

                L(0.04f, -0.36f, 0.04f, 0.3f, 0.05f, ground);
                c.DrawArc(P(-0.16f, 0f), 0.14f * s, 0.4f, 2.6f, 6, ground, Math.Max(0.6f, 0.05f * s), true);
                c.DrawArc(P(0.22f, 0f), 0.14f * s, 0.4f, 2.6f, 6, ground, Math.Max(0.6f, 0.05f * s), true);
                break;

            case Pictogram.Dumbbell:
                R(-0.3f, -0.06f, 0.6f, 0.12f);
                R(-0.46f, -0.26f, 0.14f, 0.52f);
                R(-0.32f, -0.18f, 0.08f, 0.36f);
                R(0.32f, -0.26f, 0.14f, 0.52f);
                R(0.24f, -0.18f, 0.08f, 0.36f);
                break;

            case Pictogram.Info:
                Dot(0, -0.34f, 0.12f);
                R(-0.1f, -0.14f, 0.2f, 0.56f);
                R(-0.18f, -0.14f, 0.1f, 0.1f);
                R(-0.2f, 0.34f, 0.4f, 0.1f);
                break;

            case Pictogram.Chair:
                R(-0.3f, -0.44f, 0.12f, 0.62f);
                R(-0.3f, 0.06f, 0.6f, 0.13f);
                R(-0.3f, 0.18f, 0.09f, 0.3f);
                R(0.21f, 0.18f, 0.09f, 0.3f);
                break;

            case Pictogram.Lift:
                c.DrawColoredPolygon(new[] { P(-0.24f, -0.06f), P(0f, -0.44f), P(0.24f, -0.06f) }, ink);
                c.DrawColoredPolygon(new[] { P(-0.24f, 0.08f), P(0.24f, 0.08f), P(0f, 0.46f) }, ink);
                break;

            case Pictogram.Stairs:
                c.DrawColoredPolygon(new[] { P(-0.46f, 0.42f), P(-0.46f, 0.18f), P(-0.16f, 0.18f), P(-0.16f, -0.06f), P(0.14f, -0.06f), P(0.14f, -0.3f), P(0.46f, -0.3f), P(0.46f, 0.42f) }, ink);
                break;

            case Pictogram.Scalpel:
                L(-0.42f, 0.4f, 0.08f, -0.1f, 0.13f);
                c.DrawColoredPolygon(new[] { P(0.04f, -0.14f), P(0.44f, -0.44f), P(0.2f, 0.02f) }, ink);
                break;

            case Pictogram.Radiation:
                Dot(0, 0, 0.1f);
                for (var i = 0; i < 3; i++)
                {
                    var a = (i * Mathf.Tau / 3) - (Mathf.Pi / 2);
                    c.DrawArc(P(0, 0), 0.31f * s, a - 0.5f, a + 0.5f, 6, ink, 0.26f * s);
                }

                break;

            case Pictogram.Waves:
                Dot(-0.36f, 0.3f, 0.1f);
                for (var i = 1; i <= 3; i++)
                {
                    c.DrawArc(P(-0.36f, 0.3f), i * 0.24f * s, -1.45f, -0.12f, 8, ink, thin, true);
                }

                break;

            case Pictogram.Clipboard:
                Flat.Round(c, x - (0.32f * s), y - (0.4f * s), 0.64f * s, 0.86f * s, 0.07f * s, ink);
                R(-0.14f, -0.48f, 0.28f, 0.14f);
                R(-0.2f, -0.16f, 0.4f, 0.07f, ground);
                R(-0.2f, 0.02f, 0.4f, 0.07f, ground);
                R(-0.2f, 0.2f, 0.26f, 0.07f, ground);
                break;

            case Pictogram.Scope:
                c.DrawArc(P(0.04f, 0.04f), 0.3f * s, -0.4f, 4.2f, 14, ink, thin, true);
                L(-0.14f, -0.22f, -0.3f, -0.46f, 0.1f);
                Dot(0.3f, -0.1f, 0.09f);
                break;

            case Pictogram.Drop:
                Dot(0, 0.14f, 0.26f);
                c.DrawColoredPolygon(new[] { P(-0.22f, 0.02f), P(0, -0.46f), P(0.22f, 0.02f) }, ink);
                break;
        }
    }

    // ------------------------------------------------------------------ wall signs (flat, w by h pixels)

    /// <summary>A way-finding panel: a blue header and a row for each place with an arrow tile.</summary>
    public static void Directions(CanvasItem c, float w, float h, string title, params (string Place, int QuarterTurns, Pictogram Icon)[] rows)
    {
        Flat.Round(c, 0, 0, w, h, 2f, new Color("#8C99A5"));
        Flat.Rect(c, 1, 1, w - 2, h - 2, Paper);
        Flat.Rect(c, 1, 1, w - 2, 9.5f, Blue);
        Flat.Text(c, title, w / 2, 8.4f, 7, Colors.White);
        var rowH = (h - 11.5f) / rows.Length;
        for (var i = 0; i < rows.Length; i++)
        {
            var y = 10.5f + (i * rowH);
            var tile = Math.Min(rowH - 1.6f, 9f);
            if (i > 0)
            {
                Flat.Rect(c, 3, y, w - 6, 0.6f, new Color("#C9D3DC"));
            }

            Flat.Round(c, 3, y + ((rowH - tile) / 2), tile, tile, 1.4f, Blue);
            Flat.Arrow(c, 3 + (tile / 2), y + (rowH / 2), tile * 0.72f, rows[i].QuarterTurns, Colors.White);
            Flat.TextLeft(c, rows[i].Place, tile + 6, y + (rowH / 2) + 2.6f, 7, Ink);
            if (rows[i].Icon != Pictogram.None)
            {
                Draw(c, rows[i].Icon, w - 3 - (tile / 2), y + (rowH / 2), tile * 0.82f, Blue, Paper);
            }
        }
    }

    /// <summary>A lit sign: a coloured box with a pictogram tile and one or two lines of white lettering.</summary>
    public static void Banner(CanvasItem c, float w, float h, Color colour, Pictogram icon, string title, string? under = null)
    {
        Flat.Round(c, 0, 0, w, h, 2f, colour.Darkened(0.35f));
        Flat.Round(c, 1, 1, w - 2, h - 2, 1.5f, colour);
        Flat.Rect(c, 1, 1, w - 2, 1.2f, colour.Lightened(0.3f));
        var text = 0f;
        if (icon != Pictogram.None)
        {
            var tile = h - 5;
            Flat.Round(c, 2.5f, 2.5f, tile, tile, 2f, Paper);
            Draw(c, icon, 2.5f + (tile / 2), 2.5f + (tile / 2), tile * 0.74f, colour, Paper);
            text = tile + 3;
        }

        if (under is null)
        {
            Flat.Text(c, title, text + ((w - text) / 2), (h / 2) + 3.2f, 9, Colors.White);
        }
        else
        {
            Flat.Text(c, title, text + ((w - text) / 2), (h / 2) - 0.2f, 8, Colors.White);
            Flat.Text(c, under, text + ((w - text) / 2), (h / 2) + 6.6f, 5, new Color(1, 1, 1, 0.92f), false);
        }
    }

    /// <summary>Toilet door signs: a blue tile for each of the given pictograms, with the word under them.</summary>
    public static void Toilets(CanvasItem c, float w, float h, params Pictogram[] who)
    {
        var tile = Math.Min((w - 2 - ((who.Length - 1) * 2f)) / who.Length, h - 9f);
        var x0 = (w - ((tile * who.Length) + ((who.Length - 1) * 2f))) / 2;
        for (var i = 0; i < who.Length; i++)
        {
            var x = x0 + (i * (tile + 2f));
            Flat.Round(c, x, 0, tile, tile, 2f, Blue);
            Draw(c, who[i], x + (tile / 2), tile / 2, tile * 0.78f, Colors.White, Blue);
        }

        Flat.Round(c, x0, tile + 1.5f, w - (2 * x0), 7f, 1.5f, Paper);
        Flat.Text(c, "Toilets", w / 2, tile + 7.2f, 6, Ink);
    }

    /// <summary>The green running-man fire exit sign.</summary>
    public static void FireExit(CanvasItem c, float w, float h, int quarterTurns)
    {
        Flat.Round(c, 0, 0, w, h, 1.5f, Green.Darkened(0.3f));
        Flat.Rect(c, 1, 1, w - 2, h - 2, Green);
        var s = h - 4;

        // The running figure heading for the door.
        var (mx, my) = (2 + (s / 2), h / 2);
        c.DrawCircle(new Vector2(mx + (s * 0.12f), my - (s * 0.34f)), s * 0.11f, Colors.White);
        c.DrawLine(new Vector2(mx + (s * 0.06f), my - (s * 0.2f)), new Vector2(mx - (s * 0.06f), my + (s * 0.08f)), Colors.White, s * 0.16f, true);
        c.DrawPolyline(new[] { new Vector2(mx - (s * 0.06f), my + (s * 0.08f)), new Vector2(mx + (s * 0.16f), my + (s * 0.2f)), new Vector2(mx + (s * 0.12f), my + (s * 0.44f)) }, Colors.White, s * 0.11f, true);
        c.DrawPolyline(new[] { new Vector2(mx - (s * 0.06f), my + (s * 0.08f)), new Vector2(mx - (s * 0.22f), my + (s * 0.22f)), new Vector2(mx - (s * 0.42f), my + (s * 0.2f)) }, Colors.White, s * 0.11f, true);
        c.DrawPolyline(new[] { new Vector2(mx + (s * 0.3f), my - (s * 0.12f)), new Vector2(mx + (s * 0.1f), my - (s * 0.12f)), new Vector2(mx - (s * 0.22f), my - (s * 0.2f)) }, Colors.White, s * 0.09f, true);
        Flat.Arrow(c, 2 + s + (s * 0.42f), h / 2, s * 0.7f, quarterTurns, Colors.White);
        Flat.Rect(c, w - 3 - (s * 0.5f), 2.5f, s * 0.5f, h - 5, Colors.White);
        Flat.Rect(c, w - 3 - (s * 0.5f) + 1, 3.5f, (s * 0.5f) - 2, h - 7, Green);
        c.DrawCircle(new Vector2(w - 3 - (s * 0.5f) + 2.4f, h / 2), 0.7f, Colors.White);
    }

    /// <summary>A yellow warning triangle with a pictogram, and black lettering on a yellow plate.</summary>
    public static void Warning(CanvasItem c, float w, float h, Pictogram icon, string line1, string line2)
    {
        Flat.Round(c, 0, 0, w, h, 1.5f, new Color("#2B3036"));
        Flat.Rect(c, 1, 1, w - 2, h - 2, Yellow);
        var s = Math.Min(h - 4, w * 0.4f);
        var (tx, ty) = (3 + (s / 2), h / 2);
        c.DrawColoredPolygon(new[] { new Vector2(tx, ty - (s * 0.5f)), new Vector2(tx + (s * 0.56f), ty + (s * 0.46f)), new Vector2(tx - (s * 0.56f), ty + (s * 0.46f)) }, new Color("#1B1F23"));
        c.DrawColoredPolygon(new[] { new Vector2(tx, ty - (s * 0.32f)), new Vector2(tx + (s * 0.4f), ty + (s * 0.37f)), new Vector2(tx - (s * 0.4f), ty + (s * 0.37f)) }, Yellow);
        Draw(c, icon, tx, ty + (s * 0.14f), s * 0.42f, new Color("#1B1F23"), Yellow);
        var cx = 3 + (s * 1.1f) + ((w - 5 - (s * 1.1f)) / 2);
        Flat.Text(c, line1, cx, (h / 2) - 0.5f, 7, new Color("#1B1F23"));
        Flat.Text(c, line2, cx, (h / 2) + 6.5f, 5, new Color("#1B1F23"), false);
    }

    /// <summary>A red prohibition roundel beside a line of lettering: staff only, no smoking.</summary>
    public static void Prohibition(CanvasItem c, float w, float h, string text, bool smoking)
    {
        Flat.Round(c, 0, 0, w, h, 1.5f, new Color("#8C99A5"));
        Flat.Rect(c, 1, 1, w - 2, h - 2, Paper);
        var r = (h / 2) - 2.5f;
        var centre = new Vector2(3 + r, h / 2);
        c.DrawCircle(centre, r, Red);
        c.DrawCircle(centre, r - 1.8f, Paper);
        if (smoking)
        {
            Flat.Rect(c, centre.X - (r * 0.55f), centre.Y - 0.9f, r * 0.8f, 1.8f, Ink);
            Flat.Rect(c, centre.X + (r * 0.3f), centre.Y - 0.9f, r * 0.2f, 1.8f, new Color("#E57373"));
        }
        else
        {
            Draw(c, Pictogram.Man, centre.X, centre.Y, r * 1.25f, Ink, Paper);
        }

        c.DrawLine(centre + new Vector2(-r * 0.68f, -r * 0.68f), centre + new Vector2(r * 0.68f, r * 0.68f), Red, 1.8f, true);
        Flat.Text(c, text, (2 * r) + 5 + ((w - (2 * r) - 7) / 2), (h / 2) + 2.8f, 7, Red);
    }

    /// <summary>The cafe's menu: a chalkboard with today's dishes and their prices.</summary>
    public static void Menu(CanvasItem c, float w, float h)
    {
        Flat.Round(c, 0, 0, w, h, 2f, new Color("#7A5A3A"));
        Flat.Rect(c, 2, 2, w - 4, h - 4, new Color("#26332E"));
        Flat.Text(c, "TODAY", w / 2, 10.5f, 7, new Color("#FFE082"));
        Flat.Rect(c, w * 0.25f, 12.2f, w * 0.5f, 0.7f, new Color("#FFE082"));
        var dishes = new[] { ("Soup", "2.50"), ("Jacket potato", "3.80"), ("Fish & chips", "5.20"), ("Curry & rice", "4.90"), ("Tea / coffee", "1.60") };
        var rowH = (h - 18f) / dishes.Length;
        for (var i = 0; i < dishes.Length; i++)
        {
            var y = 15f + ((i + 0.75f) * rowH);
            Flat.TextLeft(c, dishes[i].Item1, 5, y, 5, new Color("#F1F4F2"), false);
            Flat.TextLeft(c, dishes[i].Item2, w - 5 - Flat.Width(dishes[i].Item2, 5, false), y, 5, new Color("#A5D6A7"), false);
        }
    }

    /// <summary>A white notice with a pictogram tile, a title and a line under it: visiting times, clean your hands.</summary>
    public static void Notice(CanvasItem c, float w, float h, Color colour, Pictogram icon, string title, string under)
    {
        Flat.Round(c, 0, 0, w, h, 1.5f, new Color("#8C99A5"));
        Flat.Rect(c, 1, 1, w - 2, h - 2, Paper);
        Flat.Rect(c, 1, 1, w - 2, 3, colour);
        var tile = h - 9;
        Flat.Round(c, 3, 6, tile, tile, 2f, colour);
        Draw(c, icon, 3 + (tile / 2), 6 + (tile / 2), tile * 0.74f, Colors.White, colour);
        var cx = tile + 5 + ((w - tile - 7) / 2);
        Flat.Text(c, title, cx, (h / 2) + 1.4f, 7, Ink);
        Flat.Text(c, under, cx, (h / 2) + 8f, 5, new Color("#4C5B67"), false);
    }

    /// <summary>A wall cabinet with a public defibrillator: green, with a heart and a lightning flash.</summary>
    public static void Defibrillator(CanvasItem c, float w, float h)
    {
        Flat.Round(c, 0, 0, w, h, 2f, Green.Darkened(0.35f));
        Flat.Round(c, 1, 1, w - 2, h - 2, 1.5f, Green);
        Flat.Round(c, 3, 9, w - 6, h - 12, 1.5f, new Color("#DDEBE0"));
        Flat.Text(c, "AED", w / 2, 7.8f, 6, Colors.White);
        var centre = new Vector2(w / 2, 9 + ((h - 12) / 2));
        Draw(c, Pictogram.Heart, centre.X, centre.Y, Math.Min(w - 9, h - 15), Green, Paper);
        c.DrawColoredPolygon(new[] { centre + new Vector2(1.4f, -4f), centre + new Vector2(-2.4f, 0.6f), centre + new Vector2(-0.2f, 0.6f), centre + new Vector2(-1.4f, 4f), centre + new Vector2(2.4f, -0.8f), centre + new Vector2(0.2f, -0.8f) }, Colors.White);
    }

    /// <summary>A poster: a coloured title band, a picture drawn by <paramref name="picture"/> (given its box), and lines of small print.</summary>
    public static void Poster(CanvasItem c, float w, float h, Color colour, string title, Action<Rect2> picture)
    {
        Flat.Rect(c, 0, 0, w, h, new Color("#8C99A5"));
        Flat.Rect(c, 1, 1, w - 2, h - 2, Paper);
        Flat.Rect(c, 1, 1, w - 2, 9, colour);
        Flat.Text(c, title, w / 2, 8f, Flat.Width(title, 6) > w - 5 ? 5 : 6, Colors.White);
        picture(new Rect2(3, 11.5f, w - 6, h - 21));
        for (var i = 0; i < 2; i++)
        {
            Flat.Rect(c, 4, h - 7.5f + (i * 2.6f), (w - 8) * (i == 0 ? 1f : 0.65f), 1f, new Color("#9AA6B0"));
        }
    }
}
