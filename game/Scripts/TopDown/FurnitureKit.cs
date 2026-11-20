using System;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.TopDown;

/// <summary>
/// The furniture and equipment, seen from directly above. Every piece is drawn in its own frame:
/// the origin is the middle of its footprint, +y is its front (the side people use), x runs across
/// it, and <c>w</c> × <c>d</c> is its size across and front to back, in tiles.
/// </summary>
public static class FurnitureKit
{
    private static Color C(string hex) => new(hex);

    private static readonly Color Steel = C("#AEB8C2");
    private static readonly Color DarkSteel = C("#6F7D8A");
    private static readonly Color Screen = C("#0E1A14");
    private static readonly Color Casing = C("#2B333C");
    private static readonly Color Paper = C("#FFFFFF");
    private static readonly Color PaperEdge = C("#B8C1CA");
    private static readonly Color Trace = C("#4BE28A");

    /// <summary>The angle that turns a frame so its +y axis points along <paramref name="front"/>.</summary>
    public static double FrontAngle((int X, int Y) front) => Math.Atan2(-front.X, front.Y);

    /// <summary>
    /// Draws the piece with id <paramref name="id"/> on footprint (x, y, w, l), its front facing
    /// <paramref name="front"/>. Returns false if there is no drawing for it (the caller draws a plain box).
    /// </summary>
    public static bool Draw(Pen p, string id, double x, double y, double w, double l, (int X, int Y) front, Color colour, int seed)
    {
        var (fw, fd) = front.X != 0 ? (l, w) : (w, l);
        p.Push(x + (w / 2), y + (l / 2), FrontAngle(front));
        var drawn = Piece(p, id, fw, fd, colour, seed);
        if (!drawn)
        {
            Plain(p, fw, fd, colour);
        }

        p.Pop();
        return drawn;
    }

    /// <summary>A plain box in the item's colour, for anything without its own drawing.</summary>
    public static void Plain(Pen p, double w, double d, Color colour)
    {
        p.Box(-w / 2, -d / 2, w, d, 0.08, colour);
        p.Round((-w / 2) + 0.07, (-d / 2) + 0.07, w - 0.14, d - 0.14, 0.05, null, new Color(1, 1, 1, 0.25f), 0.03);
    }

    private static bool Piece(Pen p, string id, double w, double d, Color colour, int seed)
    {
        var (l, t) = (-w / 2, -d / 2);
        switch (id)
        {
            case "bedside_locker":
                p.Box(l, t, w, d, 0.08, C("#DDCFB8"));
                p.Line(l + 0.1, t + (d * 0.62), l + w - 0.1, t + (d * 0.62), C("#B5A58A"), 0.03);
                p.Circle(l + (w * 0.35), t + (d * 0.33), 0.13, Pen.A("#BFE3F5", 0.95f), C("#6FA6C4"), 0.02);
                p.Round(l + (w * 0.58), t + 0.14, 0.2, 0.26, 0.02, Paper, PaperEdge, 0.02);
                return true;

            case "clinical_cupboard":
                p.Box(l, t, w, d, 0.06, C("#EEF1F4"));
                p.Line(0, t + 0.05, 0, t + d - 0.05, PaperEdge, 0.03);
                if (w > 1.2)
                {
                    p.Round(l + 0.15, t + 0.16, 0.5, Math.Min(0.36, d - 0.3), 0.04, C("#6FA8DC"), C("#3C6FA0"), 0.025);
                    p.Round(l + w - 0.62, t + 0.14, 0.42, Math.Min(0.42, d - 0.26), 0.08, C("#F5C518"), C("#B38F00"), 0.025);
                    p.Circle(l + w - 0.41, t + 0.14 + (Math.Min(0.42, d - 0.26) / 2), 0.08, C("#C0392B"));
                }

                return true;

            case "office_desk":
                p.Box(l, t, w, d, 0.08, C("#CBA677"));
                p.Round(l + 0.06, t + 0.06, w - 0.12, d - 0.12, 0.05, null, new Color(1, 1, 1, 0.25f), 0.03);
                Workstation(p, 0, t, d);
                if (w > 2.4)
                {
                    p.Round(l + 0.2, t + 0.25, 0.45, 0.6, 0.02, Paper, PaperEdge, 0.02);
                }

                return true;

            case "triage_desk":
                p.Box(l, t, w, d, 0.08, C("#E8ECF0"));
                Workstation(p, w > 2 ? -w * 0.15 : 0, t, d);
                if (w > 2)
                {
                    // The BP machine and its cuff.
                    p.Round(l + w - 0.75, t + d - 0.6, 0.55, 0.4, 0.06, C("#3C4A58"));
                    p.Circle(l + w - 0.48, t + d - 0.4, 0.1, C("#7FD1E8"));
                }

                return true;

            case "reception_desk":
                p.Box(l, t, w, d, 0.12, C("#A67F56"));
                p.Round(l + 0.07, t + 0.07, w - 0.14, d - 0.34, 0.08, C("#C4A074"));
                p.Round(l + 0.1, t + d - 0.3, w - 0.2, 0.22, 0.06, C("#2F6FD0"));
                Workstation(p, 0, t, d - 0.3);
                if (w > 2.6)
                {
                    p.Round(l + 0.3, t + 0.2, 0.45, 0.6, 0.02, Paper, PaperEdge, 0.02);
                    p.Round(l + w - 0.75, t + 0.25, 0.4, 0.55, 0.02, C("#FFF4C2"), C("#C9B45A"), 0.02);
                }

                return true;

            case "station_counter":
                p.Box(l, t, w, d, 0.06, C("#B38F68"));
                p.Round(l + 0.05, t + 0.05, w - 0.1, d - 0.1, 0.04, C("#C7A47C"));
                Workstation(p, 0, t, d);
                if (w > 2.6)
                {
                    p.Round(l + 0.2, t + 0.2, 0.42, 0.55, 0.02, Paper, PaperEdge, 0.02);
                    p.Round(l + w - 0.7, t + 0.25, 0.4, 0.5, 0.02, C("#E8F1FA"), C("#9DB3C8"), 0.02);
                }

                return true;

            case "pharmacy_counter":
                p.Box(l, t, w, d, 0.06, C("#7FA88A"));
                p.Round(l + 0.06, t + 0.06, w - 0.12, d - 0.12, 0.04, C("#E9F1EA"));
                Workstation(p, -w * 0.2, t, d);
                p.Round(l + w - 0.9, t + 0.2, 0.5, 0.36, 0.04, Paper, C("#2E7D4F"), 0.03);
                GreenCross(p, l + w - 0.65, t + 0.38, 0.1);
                return true;

            case "staff_table":
                p.Box(l, t, w, d, 0.12, C("#B58B5E"));
                p.Round(l + 0.07, t + 0.07, w - 0.14, d - 0.14, 0.08, C("#C59C70"));
                p.Circle(l + 0.45, t + 0.4, 0.13, Paper, C("#9AA6B2"), 0.025);
                p.Circle(l + 0.45, t + 0.4, 0.08, C("#6B4A2E"));
                p.In(l + w - 0.6, t + d - 0.5, 0.3, () => p.Round(-0.25, -0.33, 0.5, 0.66, 0.02, C("#E9F0F6"), C("#9DB3C8"), 0.02));
                return true;

            case "cafe_table":
                p.Shadow(c => p.Circle(0, 0, Math.Min(w, d) / 2, c));
                p.Dot(0, 0, Math.Min(w, d) / 2 - 0.04, C("#C59463"));
                p.Circle(0, 0, Math.Min(w, d) / 2 - 0.14, null, new Color(1, 1, 1, 0.22f), 0.03);
                if (seed % 3 != 0)
                {
                    p.Round(-0.32, -0.2, 0.5, 0.36, 0.04, C("#D95B43"), C("#9C3B2A"), 0.02);
                    p.Circle(-0.07, -0.02, 0.12, Paper, PaperEdge, 0.02);
                    p.Circle(0.3, 0.2, 0.08, Paper, PaperEdge, 0.02);
                }

                return true;

            case "hand_basin" or "wash_basin" or "plaster_sink" or "cleaners_sink":
                Basin(p, w, d, id is "plaster_sink" or "cleaners_sink");
                return true;

            case "kitchenette":
            {
                p.Box(l, t, w, d, 0.05, C("#DDE2E7"));
                p.Round(l + 0.05, t + 0.05, w - 0.1, d - 0.1, 0.04, C("#EEF1F4"));
                var sink = Math.Min(0.9, w * 0.3);
                p.Round(l + 0.2, t + 0.2, sink, d - 0.4, 0.1, C("#B9C3CC"), DarkSteel, 0.03);
                p.Round(l + 0.28, t + 0.28, sink - 0.16, d - 0.56, 0.08, C("#D5DCE2"));
                p.Circle(l + 0.2 + (sink / 2), t + 0.2, 0.06, DarkSteel);
                if (w > 2)
                {
                    p.Circle(l + sink + 0.65, 0, 0.2, Casing);
                    p.Circle(l + sink + 0.65, 0, 0.12, null, C("#6B7785"), 0.02);
                    p.Circle(l + sink + 1.15, 0, 0.16, C("#F2F4F6"), DarkSteel, 0.03);
                }

                if (w > 2.8)
                {
                    p.Round(l + w - 0.65, t + 0.2, 0.5, d - 0.4, 0.04, C("#3A434D"), C("#1F262D"), 0.02);
                    p.Round(l + w - 0.6, t + 0.26, 0.28, d - 0.52, 0.02, C("#56626E"));
                }

                return true;
            }

            case "drug_fridge":
                p.Box(l, t, w, d, 0.08, C("#F2F5F7"));
                p.Line(l + 0.1, t + d - 0.14, l + w - 0.1, t + d - 0.14, C("#9AA6B2"), 0.05);
                GreenCross(p, 0, -0.05, Math.Min(w, d) * 0.16);
                return true;

            case "medicine_shelves" or "cleaning_shelves":
            {
                p.Box(l, t, w, d, 0.04, id == "cleaning_shelves" ? C("#B0BEC5") : C("#E7E2D4"));
                var colours = id == "cleaning_shelves"
                    ? new[] { "#3B7DD8", "#F2C230", "#E0559A", "#43A047", "#F2F4F7" }
                    : new[] { "#F2F4F7", "#6FA8DC", "#F5C518", "#E58A84", "#93C99A", "#C9B8D8" };
                var n = Math.Max(2, (int)(w / 0.34));
                for (var i = 0; i < n; i++)
                {
                    var bx = l + 0.1 + (i * (w - 0.2) / n);
                    var c = C(colours[(i + seed) % colours.Length]);
                    p.Round(bx, t + 0.1, ((w - 0.2) / n) - 0.05, d - 0.2, 0.03, c, Pen.Outline(c), 0.02);
                }

                return true;
            }

            case "broom_rack":
                p.Box(l, t, w, d, 0.04, C("#546E7A"));
                for (var i = 0; i < 3; i++)
                {
                    var bx = l + (w * (i + 0.5) / 3);
                    p.Line(bx, t + 0.08, bx, t + d + 0.25, C("#C9A26B"), 0.06);
                    p.Round(bx - 0.14, t + d + 0.2, 0.28, 0.14, 0.03, i == 1 ? C("#3B7DD8") : C("#8A5F36"));
                }

                return true;

            case "mop_bucket":
                p.Box(l + 0.08, t + 0.08, w - 0.16, d - 0.16, 0.12, C("#FBC02D"));
                p.Round(l + 0.2, t + 0.2, w - 0.4, (d - 0.4) * 0.6, 0.06, C("#7FB7CF"));
                p.Line(0, 0, w * 0.45, -d * 0.55, C("#C9A26B"), 0.06);
                return true;

            case "instrument_trolley" or "plaster_trolley":
                TrolleyBase(p, w, d, C("#C9D1D8"));
                if (id == "plaster_trolley")
                {
                    for (var i = 0; i < 3; i++)
                    {
                        p.Circle(l + 0.3 + (i * ((w - 0.6) / 2)), -0.05, 0.15, C("#F4F0E6"), C("#B9B19C"), 0.025);
                        p.Circle(l + 0.3 + (i * ((w - 0.6) / 2)), -0.05, 0.05, C("#B9B19C"));
                    }
                }
                else
                {
                    p.Round(l + 0.14, t + 0.14, w - 0.28, d - 0.28, 0.04, C("#3F8F7E"));
                    for (var i = 0; i < 4; i++)
                    {
                        p.Line(l + 0.26 + (i * 0.16), t + 0.24, l + 0.26 + (i * 0.16), t + d - 0.3, C("#E9EEF2"), 0.035);
                    }
                }

                return true;

            case "oxygen_cylinder":
                p.Shadow(c => p.Circle(0, 0, 0.26, c), 0.06, 0.08);
                p.Circle(0, 0, 0.26, C("#22282F"), C("#0F1418"), 0.03);
                p.Circle(0, 0, 0.18, C("#F4F6F8"));
                p.Circle(0, 0, 0.07, C("#3F8F5A"));
                return true;

            case "ventilator":
                Machine(p, w, d, C("#86ADC9"), C("#56B6F0"));
                p.Path(C("#C9D6E0"), 0.08, (w / 2 - 0.1, d / 2 - 0.2), (w / 2 + 0.35, d / 2 + 0.2), (w / 2 + 0.8, d / 2 + 0.25));
                return true;

            case "anaesthetic_machine":
                Machine(p, w, d, C("#4C7D9C"), Trace);
                p.Circle(l + 0.25, t + 0.2, 0.12, C("#F4F6F8"), C("#22282F"), 0.03);
                p.Circle(l + 0.55, t + 0.2, 0.12, C("#3B7DD8"), C("#22282F"), 0.03);
                return true;

            case "ultrasound_machine":
                Machine(p, w, d, C("#7C9DB5"), C("#9FD0E8"));
                p.Path(C("#3A434D"), 0.05, (w / 2 - 0.15, 0.1), (w / 2 + 0.3, 0.25), (w / 2 + 0.35, 0.55));
                return true;

            case "eeg_machine":
                Machine(p, w, d, C("#7A8DA3"), C("#C9A8F0"));
                return true;

            case "endoscopy_stack":
                Machine(p, w, d, C("#5C788C"), C("#F0A8A0"));
                p.Path(C("#22282F"), 0.05, (-w / 2 + 0.2, d / 2 - 0.1), (-w / 2 - 0.2, d / 2 + 0.3), (-w / 2 + 0.1, d / 2 + 0.6));
                return true;

            case "ctg_monitor":
                Machine(p, w, d, C("#7A8C9A"), C("#F5A8C8"));
                return true;

            case "breast_pump":
                Machine(p, w, d, C("#C7D3DC"), C("#F5C518"));
                return true;

            case "resuscitaire":
                p.Box(l, t, w, d, 0.1, C("#E4E9ED"));
                p.Round(l + 0.14, t + 0.3, w - 0.28, d - 0.44, 0.1, C("#FFF6DD"), C("#D9C58A"), 0.03);
                p.Round(l + 0.1, t + 0.06, w - 0.2, 0.2, 0.05, C("#F5A623"));
                return true;

            case "baby_changing_unit":
                p.Box(l, t, w, d, 0.08, C("#E8DCC8"));
                p.Round(l + 0.12, t + 0.12, w - 0.24, d - 0.24, 0.12, C("#BFDDF5"), C("#8DB3D9"), 0.03);
                return true;

            case "lead_screen":
                p.Box(l, t, w, d, 0.05, C("#7F97A9"));
                p.Round(l + (w * 0.2), t + (d * 0.25), w * 0.6, d * 0.5, 0.03, C("#BFE3F5"));
                return true;

            case "bay_curtain":
            {
                // A curtain drawn across: a rail with the fabric gathered in folds along it.
                var fabric = C("#7FB7CF");
                p.Line(l, 0, l + w, 0, DarkSteel, 0.05);
                var n = Math.Max(3, (int)(w / 0.3));
                for (var i = 0; i < n; i++)
                {
                    p.Ellipse(l + ((i + 0.5) * w / n), (i % 2 == 0 ? -0.05 : 0.05), w / n * 0.6, 0.11, fabric, Pen.Outline(fabric), 0.02);
                }

                return true;
            }

            case "toilet_pan" or "accessible_wc":
                Pan(p, 0, 0);
                if (id == "accessible_wc")
                {
                    p.Line(-0.6, t + 0.05, -0.6, t + 0.95, C("#3B7DD8"), 0.07);
                    p.Line(0.6, t + 0.05, 0.6, t + 0.95, C("#3B7DD8"), 0.07);
                }

                return true;

            case "toilet_cubicle":
            {
                var wall = C("#6E9BC4");
                p.Round(l, t, w, d, 0.03, new Color(1, 1, 1, 0.35f));
                p.Line(l, t, l, t + d, wall, 0.12);
                p.Line(l + w, t, l + w, t + d, wall, 0.12);
                p.Line(l, t, l + w, t, wall, 0.12);
                p.Line(l, t + d, l + 0.3, t + d, wall, 0.12);
                p.Line(l + w - 0.3, t + d, l + w, t + d, wall, 0.12);

                // The door stands a little open.
                p.Line(l + 0.3, t + d, l + 0.3 + ((w - 0.6) * 0.8), t + d + ((w - 0.6) * 0.45), C("#8FB5D8"), 0.09);
                Pan(p, 0, t + 0.62);
                return true;
            }

            case "hand_dryer":
                p.Box(l + 0.1, t, w - 0.2, Math.Min(d, 0.34), 0.08, C("#CFD8DC"));
                p.Line(l + 0.25, t + 0.26, l + w - 0.25, t + 0.26, DarkSteel, 0.04);
                return true;

            case "vending_machine":
            {
                var body = new[] { C("#C62828"), C("#1F5FBF"), C("#2E7D4F") }[Math.Abs(seed) % 3];
                p.Box(l, t, w, d, 0.06, Pen.Dark(body, 0.25f));
                p.Round(l + 0.06, t + 0.06, w - 0.12, d - 0.3, 0.04, body);
                p.Round(l + 0.1, t + d - 0.24, w * 0.62, 0.18, 0.03, Pen.A("#BFE3F5", 0.9f), Casing, 0.02);
                p.Round(l + (w * 0.74), t + d - 0.24, w * 0.18, 0.18, 0.02, C("#F5C518"));
                return true;
            }

            case "water_cooler":
                p.Box(l + 0.08, t + 0.08, w - 0.16, d - 0.16, 0.08, C("#E3EEF5"));
                p.Circle(0, -0.02, Math.Min(w, d) * 0.3, Pen.A("#7FC4EE", 0.95f), C("#4A8DB8"), 0.03);
                p.Circle(-0.05, -0.07, Math.Min(w, d) * 0.1, new Color(1, 1, 1, 0.6f));
                return true;

            case "food_counter":
            {
                p.Box(l, t, w, d, 0.06, C("#C98B4E"));
                p.Round(l + 0.06, t + 0.06, w - 0.12, d - 0.4, 0.04, C("#E9EEF2"));
                p.Round(l + 0.06, t + d - 0.3, w - 0.12, 0.24, 0.04, Pen.A("#BFE3F5", 0.75f), C("#7B8794"), 0.02);
                var trays = Math.Max(1, (int)((w - 0.2) / 0.9));
                var food = new[] { C("#E0B04A"), C("#D95B43"), C("#7BB661"), C("#F2E3C2"), C("#B5683F") };
                for (var i = 0; i < trays; i++)
                {
                    var tx = l + 0.14 + (i * (w - 0.28) / trays);
                    p.Round(tx, t + 0.14, ((w - 0.28) / trays) - 0.08, d - 0.58, 0.05, C("#AEB8C2"), DarkSteel, 0.02);
                    p.Round(tx + 0.05, t + 0.19, ((w - 0.28) / trays) - 0.18, d - 0.68, 0.04, food[(i + seed) % food.Length]);
                }

                return true;
            }

            case "c_arm":
                p.Box(l, t + (d * 0.55), w * 0.5, d * 0.45, 0.1, C("#DDE3E8"));
                p.Arc(w * 0.05, 0, Math.Min(w, d) * 0.42, -2.2, 1.2, C("#C9D3DB"), 0.2);
                p.Arc(w * 0.05, 0, Math.Min(w, d) * 0.42, -2.2, 1.2, C("#8E99A5"), 0.03);
                p.Dot(w * 0.05 + (Math.Cos(-2.2) * Math.Min(w, d) * 0.42), Math.Sin(-2.2) * Math.Min(w, d) * 0.42, 0.2, C("#5D6B79"));
                return true;

            case "exercise_mat":
                p.Round(l + 0.05, t + 0.05, w - 0.1, d - 0.1, 0.12, C("#5C8FA8"), C("#3F6E86"), 0.04);
                p.Round(l + 0.2, t + 0.2, w - 0.4, d - 0.4, 0.08, null, new Color(1, 1, 1, 0.25f), 0.03);
                return true;

            case "parallel_bars":
            {
                var along = w >= d;
                p.Round(l + 0.1, t + 0.1, w - 0.2, d - 0.2, 0.08, C("#8FA9B8"));
                var bar = C("#C9A26B");
                if (along)
                {
                    foreach (var by in new[] { t + 0.2, t + d - 0.2 })
                    {
                        p.Line(l, by, l + w, by, bar, 0.09);
                        p.Circle(l + 0.12, by, 0.09, DarkSteel);
                        p.Circle(l + w - 0.12, by, 0.09, DarkSteel);
                    }
                }
                else
                {
                    foreach (var bx in new[] { l + 0.2, l + w - 0.2 })
                    {
                        p.Line(bx, t, bx, t + d, bar, 0.09);
                        p.Circle(bx, t + 0.12, 0.09, DarkSteel);
                        p.Circle(bx, t + d - 0.12, 0.09, DarkSteel);
                    }
                }

                return true;
            }

            case "practice_stairs":
            {
                p.Box(l, t, w, d, 0.05, C("#A8B3BB"));
                var along = w >= d;
                var steps = 4;
                for (var i = 0; i < steps; i++)
                {
                    var shade = Pen.Light(C("#C9A26B"), 0.3f - (i * 0.1f));
                    if (along)
                    {
                        p.Round(l + 0.06 + (i * (w - 0.12) / steps), t + 0.14, ((w - 0.12) / steps) - 0.03, d - 0.28, 0.02, shade);
                    }
                    else
                    {
                        p.Round(l + 0.14, t + 0.06 + (i * (d - 0.12) / steps), w - 0.28, ((d - 0.12) / steps) - 0.03, 0.02, shade);
                    }
                }

                return true;
            }

            case "drip_stand":
                for (var i = 0; i < 5; i++)
                {
                    var a = i * 1.2566;
                    p.Line(0, 0, Math.Cos(a) * 0.3, Math.Sin(a) * 0.3, C("#8E99A5"), 0.06);
                    p.Circle(Math.Cos(a) * 0.3, Math.Sin(a) * 0.3, 0.05, C("#3A434D"));
                }

                p.Line(-0.22, 0, 0.22, 0, Steel, 0.05);
                p.Round(-0.3, -0.12, 0.2, 0.3, 0.06, Pen.A("#BFE3F5", 0.9f), C("#6FA6C4"), 0.02);
                p.Circle(0, 0, 0.07, C("#DDE3E8"), DarkSteel, 0.02);
                return true;

            case "obs_monitor":
                MonitorStand(p, false, null);
                return true;

            case "ct_gantry":
            {
                p.Box(l, t, w, d, 0.3, C("#F1F4F6"));
                p.Round(l + 0.12, t + 0.12, w - 0.24, d - 0.24, 0.22, null, C("#C9D3DB"), 0.04);
                p.Round(-0.95, t - 0.02, 1.9, d + 0.04, 0.1, C("#39434E"));
                p.Round(-0.8, t - 0.02, 1.6, d + 0.04, 0.08, C("#1D242B"));
                p.Circle(l + 0.5, 0, 0.12, C("#56B6F0"));
                return true;
            }

            case "birthing_ball":
                p.Shadow(c => p.Circle(0, 0, Math.Min(w, d) * 0.42, c));
                p.Dot(0, 0, Math.Min(w, d) * 0.42, C("#7E57C2"));
                p.Circle(-0.1, -0.12, Math.Min(w, d) * 0.14, new Color(1, 1, 1, 0.3f));
                return true;

            case "examination_couch":
                Couch(p, w, d, colour);
                return true;

            case "cot":
                Cot(p, w, d, false);
                return true;

            case "incubator":
                Cot(p, w, d, true);
                return true;

            case "parent_bed":
                ParentBed(p, w, d, colour);
                return true;

            case "toy_box":
                ToyBox(p, w, d, colour, seed);
                return true;

            default:
                return false;
        }
    }

    // ------------------------------------------------------------------ shared pieces

    /// <summary>A screen, keyboard and mouse for someone sitting at the back edge (y = <paramref name="back"/>) of a desk <paramref name="depth"/> deep.</summary>
    private static void Workstation(Pen p, double cx, double back, double depth)
    {
        var d = Math.Min(depth, 1.7);
        var far = back + d;
        p.Round(cx - 0.36, far - 0.24, 0.72, 0.12, 0.03, Casing);
        p.Round(cx - 0.1, far - 0.36, 0.2, 0.16, 0.03, C("#454F5A"));
        p.Round(cx - 0.32, far - 0.29, 0.64, 0.05, 0.02, C("#7FD1E8"));
        p.Round(cx - 0.3, back + 0.2, 0.6, 0.22, 0.04, C("#E9EDF1"), C("#8A96A3"), 0.025);
        p.Round(cx + 0.36, back + 0.22, 0.1, 0.16, 0.05, C("#E9EDF1"), C("#8A96A3"), 0.02);
    }

    private static void GreenCross(Pen p, double x, double y, double s)
    {
        var green = C("#2E9E57");
        p.Rect(x - (s * 0.3), y - s, s * 0.6, s * 2, green);
        p.Rect(x - s, y - (s * 0.3), s * 2, s * 0.6, green);
    }

    private static void Basin(Pen p, double w, double d, bool steel)
    {
        var (l, t) = (-w / 2, -d / 2);
        p.Box(l, t, w, d, 0.12, steel ? C("#C9D1D8") : C("#F6F8FA"));
        p.Ellipse(0, 0.04, (w / 2) - 0.16, (d / 2) - 0.18, steel ? C("#AEB8C2") : C("#D4E3EC"), C("#9FB4C2"), 0.025);
        p.Circle(0, 0.04, 0.05, DarkSteel);
        p.Round(-0.05, t + 0.04, 0.1, 0.24, 0.04, C("#9AA6B2"));
    }

    private static void Pan(Pen p, double x, double y)
    {
        p.Shadow(c => p.Ellipse(x, y + 0.32, 0.36, 0.48, c), 0.05, 0.07);
        p.Round(x - 0.42, y - 0.5, 0.84, 0.36, 0.08, C("#F4F6F8"), C("#9AA6B2"), 0.03);
        p.Ellipse(x, y + 0.32, 0.36, 0.5, C("#FBFCFD"), C("#9AA6B2"), 0.03);
        p.Ellipse(x, y + 0.36, 0.2, 0.3, C("#DCE9F0"));
    }

    private static void TrolleyBase(Pen p, double w, double d, Color colour)
    {
        var (l, t) = (-w / 2, -d / 2);
        foreach (var (wx, wy) in new[] { (l + 0.1, t + 0.1), (l + w - 0.1, t + 0.1), (l + 0.1, t + d - 0.1), (l + w - 0.1, t + d - 0.1) })
        {
            p.Circle(wx, wy, 0.08, Casing);
        }

        p.Box(l + 0.04, t + 0.04, w - 0.08, d - 0.08, 0.08, colour);
        p.Round(l + 0.1, t + 0.1, w - 0.2, d - 0.2, 0.05, null, DarkSteel, 0.025);
    }

    /// <summary>A machine on wheels: a coloured body with a screen at the front and a couple of dials.</summary>
    private static void Machine(Pen p, double w, double d, Color body, Color trace)
    {
        var (l, t) = (-w / 2, -d / 2);
        p.Box(l, t, w, d, 0.1, body);
        var sh = Math.Min(0.42, d * 0.42);
        p.Round(l + 0.12, t + d - sh - 0.1, w - 0.24, sh, 0.04, C("#17212B"));
        p.Line(l + 0.2, t + d - 0.1 - (sh / 2), l + w - 0.2, t + d - 0.1 - (sh / 2), trace, 0.03);
        if (d > 0.9)
        {
            p.Circle(l + 0.26, t + 0.24, 0.08, C("#F4F6F8"), C("#5D7387"), 0.02);
            p.Circle(l + w - 0.26, t + 0.24, 0.08, C("#F4F6F8"), C("#5D7387"), 0.02);
        }
    }

    /// <summary>An obs monitor on its stand. <paramref name="alarm"/> is the colour of the patient's NEWS2 (null with nobody connected).</summary>
    public static void MonitorStand(Pen p, bool live, Color? alarm)
    {
        for (var i = 0; i < 5; i++)
        {
            var a = (i * 1.2566) + 0.3;
            p.Line(0, 0, Math.Cos(a) * 0.3, Math.Sin(a) * 0.3, DarkSteel, 0.06);
        }

        p.Shadow(c => p.Round(-0.42, -0.3, 0.84, 0.56, 0.08, c), 0.06, 0.08);
        p.Round(-0.42, -0.3, 0.84, 0.56, 0.08, Casing, C("#141A20"), 0.03);
        p.Round(-0.35, -0.24, 0.7, 0.4, 0.04, Screen);
        if (!live)
        {
            p.Line(-0.3, -0.04, 0.3, -0.04, Pen.A("#4BE28A", 0.35f), 0.03);
            return;
        }

        var colour = alarm ?? Trace;
        p.Path(colour, 0.035, (-0.31, -0.07), (-0.16, -0.07), (-0.11, -0.19), (-0.05, 0.03), (0, -0.07), (0.12, -0.07), (0.17, -0.19), (0.22, 0.03), (0.27, -0.07));
        p.Line(-0.31, 0.08, 0.05, 0.08, C("#56B6F0"), 0.03);
        p.Rect(0.14, 0.03, 0.16, 0.09, colour);
    }

    /// <summary>The crash trolley: red, with the defibrillator on top.</summary>
    public static void ResusTrolley(Pen p, double w, double d, Color colour)
    {
        var (l, t) = (-w / 2, -d / 2);
        TrolleyBase(p, w, d, colour);
        p.Round(l + 0.12, t + 0.12, w - 0.24, Math.Min(0.56, d * 0.42), 0.05, Casing);
        p.Round(l + 0.18, t + 0.18, 0.34, 0.26, 0.02, Screen);
        p.Path(Trace, 0.025, (l + 0.21, t + 0.32), (l + 0.3, t + 0.32), (l + 0.34, t + 0.22), (l + 0.38, t + 0.38), (l + 0.48, t + 0.32));
        p.Round(l + w - 0.5, t + d - 0.5, 0.36, 0.36, 0.04, Paper);
        var red = C("#D8483B");
        p.Rect(l + w - 0.36, t + d - 0.44, 0.08, 0.24, red);
        p.Rect(l + w - 0.44, t + d - 0.36, 0.24, 0.08, red);
    }

    // ------------------------------------------------------------------ chairs

    /// <summary>A chair at (cx, cy), the sitter looking along <paramref name="facing"/>.</summary>
    public static void Chair(Pen p, string id, double cx, double cy, (double X, double Y) facing, Color colour)
    {
        // In the chair's frame the sitter looks along +y and the back rest is at -y.
        p.Push(cx, cy, Math.Atan2(-facing.X, facing.Y));
        switch (id)
        {
            case "staff_chair":
                for (var i = 0; i < 5; i++)
                {
                    var a = (i * 1.2566) + 0.6;
                    p.Line(0, 0, Math.Cos(a) * 0.36, Math.Sin(a) * 0.36, C("#3A434D"), 0.06);
                }

                p.Shadow(c => p.Circle(0, 0, 0.3, c), 0.05, 0.07);
                p.Dot(0, 0.02, 0.3, colour, 0.03);
                p.Solid(-0.3, -0.4, 0.6, 0.16, 0.07, Pen.Dark(colour, 0.2f), 0.03);
                break;
            case "lounge_chair":
                p.Shadow(c => p.Round(-0.5, -0.5, 1, 0.96, 0.16, c));
                p.Round(-0.5, -0.5, 1, 0.96, 0.16, Pen.Dark(colour, 0.12f), Pen.Outline(colour), 0.03);
                p.Round(-0.33, -0.3, 0.66, 0.7, 0.1, Pen.Light(colour, 0.12f));
                p.Round(-0.5, -0.5, 1, 0.24, 0.1, Pen.Dark(colour, 0.05f), Pen.Outline(colour), 0.025);
                break;
            case "waiting_seat":
                p.Shadow(c => p.Round(-0.44, -0.4, 0.88, 0.8, 0.1, c), 0.05, 0.08);
                p.Rect(-0.5, -0.02, 1.0, 0.08, C("#8E99A5"));
                p.Solid(-0.42, -0.28, 0.84, 0.66, 0.12, colour, 0.03);
                p.Round(-0.42, -0.42, 0.84, 0.2, 0.08, Pen.Dark(colour, 0.15f), Pen.Outline(colour), 0.03);
                p.Round(-0.3, -0.14, 0.6, 0.4, 0.08, Pen.Light(colour, 0.14f));
                break;
            default:
                p.Shadow(c => p.Round(-0.36, -0.38, 0.72, 0.74, 0.1, c), 0.05, 0.07);
                p.Solid(-0.36, -0.26, 0.72, 0.62, 0.12, colour, 0.03);
                p.Round(-0.36, -0.4, 0.72, 0.18, 0.07, Pen.Dark(colour, 0.18f), Pen.Outline(colour), 0.03);
                p.Round(-0.26, -0.14, 0.52, 0.4, 0.08, Pen.Light(colour, 0.14f));
                break;
        }

        p.Pop();
    }

    // ------------------------------------------------------------------ beds, couches and tables

    public const double BedWidth = 3.0;
    public const double BedLength = 5.0;

    /// <summary>How wide the mattress of a bed is, for whoever lies on it.</summary>
    public const double MattressWidth = BedWidth - 0.92;

    /// <summary>
    /// A hospital bed in its own frame (the middle of the mattress, head towards -y): wheels, frame,
    /// head board, mattress and pillow. <paramref name="made"/> adds the blanket of an empty bed.
    /// </summary>
    public static void Bed(Pen p, Color? madeBlanket, bool rails = true, bool birthing = false)
    {
        var (w, l) = (BedWidth, BedLength);
        var (x, y) = (-w / 2, -l / 2);
        p.Shadow(c => p.Round(x + 0.3, y + 0.02, w - 0.6, l - 0.04, 0.2, c), 0.12, 0.16);
        foreach (var (wx, wy) in new[] { (x + 0.42, y + 0.3), (x + w - 0.42, y + 0.3), (x + 0.42, y + l - 0.3), (x + w - 0.42, y + l - 0.3) })
        {
            p.Circle(wx, wy, 0.11, Casing);
        }

        var frame = birthing ? C("#C9B8D8") : C("#C5CED6");
        p.Solid(x + 0.36, y + 0.05, w - 0.72, l - 0.1, 0.18, frame);
        p.Solid(x + 0.36, y + 0.02, w - 0.72, 0.2, 0.08, birthing ? C("#8E77A8") : C("#8795A3"), 0.03);
        p.Round(x + 0.46, y + 0.24, w - 0.92, l - 0.42, 0.14, C("#F7F9FB"), C("#B5C0CA"), 0.025);
        p.Round(x + 0.62, y + 0.34, w - 1.24, 0.82, 0.2, Paper, C("#AEBAC5"), 0.03);
        if (madeBlanket is { } blanket)
        {
            p.Solid(x + 0.46, y + 1.5, w - 0.92, l - 1.72, 0.14, blanket, 0.03);
            p.Round(x + 0.46, y + 1.5, w - 0.92, 0.34, 0.1, Pen.Light(blanket, 0.5f));
            p.Line(x + 0.75, y + 2.6, x + w - 0.8, y + 2.75, new Color(1, 1, 1, 0.25f), 0.04);
        }

        if (rails)
        {
            BedRails(p);
        }
    }

    /// <summary>The side rails of a bed, drawn over whoever lies in it.</summary>
    public static void BedRails(Pen p)
    {
        var (w, l) = (BedWidth, BedLength);
        p.Line((-w / 2) + 0.36, (-l / 2) + 1.7, (-w / 2) + 0.36, (l / 2) - 0.8, DarkSteel, 0.07);
        p.Line((w / 2) - 0.36, (-l / 2) + 1.7, (w / 2) - 0.36, (l / 2) - 0.8, DarkSteel, 0.07);
    }

    /// <summary>An imaging, operating or treatment table in the bed frame (head towards -y).</summary>
    public static void Table(Pen p, string id, Color colour)
    {
        var (w, l) = (BedWidth, BedLength);
        var (x, y) = (-w / 2, -l / 2);
        switch (id)
        {
            case "xray_table":
                p.Box(x + 0.1, y + 0.1, w - 0.2, l - 0.2, 0.14, C("#DDE3E8"));
                p.Round(x + 0.3, y + 0.3, w - 0.6, l - 0.6, 0.1, C("#39434E"));
                p.Round(x + 0.45, y + 1.2, w - 0.9, 1.5, 0.05, null, new Color(1, 1, 1, 0.5f), 0.03);
                p.Line(0, y + 1.2, 0, y + 2.7, new Color(1, 1, 1, 0.4f), 0.02);
                p.Line(x + 0.45, y + 1.95, x + w - 0.45, y + 1.95, new Color(1, 1, 1, 0.4f), 0.02);
                break;
            case "ct_table" or "ct_base":
                p.Box(x + 0.25, y + 0.5, w - 0.5, l - 0.6, 0.14, C("#C9D3DB"));
                p.Round(x + 0.36, y + 0.1, w - 0.72, l - 0.3, 0.3, C("#E4E9ED"), C("#9AA8B5"), 0.03);
                p.Round(x + 0.48, y + 0.3, w - 0.96, l - 0.8, 0.2, C("#4A5662"));
                break;
            case "operating_table":
                p.Shadow(c => p.Round(-0.8, y + 0.3, 1.6, l - 0.6, 0.2, c), 0.12, 0.16);
                p.Round(-0.35, -0.6, 0.7, 1.2, 0.12, C("#5D6B79"));
                p.Solid(-0.62, y + 0.3, 1.24, 0.95, 0.2, C("#3F5361"), 0.03);
                p.Solid(-0.75, y + 1.3, 1.5, 1.6, 0.1, C("#3F5361"), 0.03);
                p.Solid(-0.7, y + 2.95, 1.4, l - 3.3, 0.14, C("#3F5361"), 0.03);
                p.Line(-1.25, y + 1.7, -0.75, y + 1.7, C("#3F5361"), 0.26);
                p.Line(0.75, y + 1.7, 1.25, y + 1.7, C("#3F5361"), 0.26);
                break;
            case "cath_lab_table":
                p.Box(-1.0, y + 0.2, 2.0, l - 0.4, 0.16, C("#E4E9ED"));
                p.Round(-0.84, y + 0.4, 1.68, l - 0.9, 0.12, C("#4A5662"));
                break;
            case "birthing_bed":
                Bed(p, null, rails: true, birthing: true);
                break;
            default:
                // Scan and treatment couches: a padded top in the item's colour with a paper roll over it.
                p.Box(-0.95, y + 0.25, 1.9, l - 0.5, 0.2, colour);
                p.Round(-0.83, y + 0.37, 1.66, 0.9, 0.14, Pen.Light(colour, 0.25f));
                p.Round(-0.81, y + 1.35, 1.62, l - 1.9, 0.08, new Color(1, 1, 1, 0.72f));
                break;
        }
    }

    /// <summary>How wide the top of a table is, for whoever lies on it.</summary>
    public static double TableWidth(string id) => id switch
    {
        "xray_table" => 2.2,
        "ct_table" or "ct_base" => 1.9,
        "operating_table" => 1.4,
        "cath_lab_table" => 1.6,
        _ => 1.6,
    };

    /// <summary>The X-ray tube: its column at (x, y) and an arm reaching towards the table at (tx, ty), with the tube head at the end.</summary>
    public static void XrayTube(Pen p, double x, double y, double tx, double ty)
    {
        var (dx, dy) = (tx - x, ty - y);
        var length = Math.Max(0.01, Math.Sqrt((dx * dx) + (dy * dy)));
        var reach = Math.Max(0.6, length - 1.7);
        var (hx, hy) = (x + (dx / length * reach), y + (dy / length * reach));
        p.Line(x, y, hx, hy, C("#B4C0CA"), 0.16);
        p.Circle(x, y, 0.22, C("#9AA8B5"), C("#5D6B79"), 0.03);
        p.Shadow(c => p.Round(hx - 0.45, hy - 0.4, 0.9, 0.8, 0.12, c));
        p.Round(hx - 0.45, hy - 0.4, 0.9, 0.8, 0.12, C("#EEF2F5"), DarkSteel, 0.03);
        p.Circle(hx, hy, 0.2, C("#F5C518"), C("#8A6F00"), 0.03);
    }

    /// <summary>An examination couch in a frame w across and d long, head towards -y.</summary>
    public static void Couch(Pen p, double w, double d, Color colour)
    {
        var (l, t) = (-w / 2, -d / 2);
        var blue = colour.S < 0.05f ? C("#4F86B5") : colour;
        p.Box(l, t, w, d, 0.2, blue);
        p.Round(l + 0.12, t + 0.12, w - 0.24, d * 0.26, 0.14, Pen.Light(blue, 0.22f));
        p.Round(l + 0.14, t + (d * 0.3), w - 0.28, d * 0.66, 0.08, new Color(1, 1, 1, 0.72f));
    }

    /// <summary>A baby's cot (clear sides) or an incubator (a clear hood with port holes), in a frame w across and d long.</summary>
    public static void Cot(Pen p, double w, double d, bool incubator)
    {
        var (l, t) = (-w / 2, -d / 2);
        foreach (var (wx, wy) in new[] { (l + 0.12, t + 0.12), (l + w - 0.12, t + 0.12), (l + 0.12, t + d - 0.12), (l + w - 0.12, t + d - 0.12) })
        {
            p.Circle(wx, wy, 0.07, Casing);
        }

        p.Box(l + 0.05, t + 0.05, w - 0.1, d - 0.1, 0.14, incubator ? C("#5C8FB8") : C("#DDE9F2"));
        p.Round(l + 0.14, t + 0.14, w - 0.28, d - 0.28, 0.1, C("#FAFBFC"), C("#B5C0CA"), 0.025);
        if (incubator)
        {
            p.Round(l + 0.1, t + 0.1, w - 0.2, d - 0.2, 0.14, Pen.A("#BFE3F5", 0.35f), Pen.A("#5A8DB8", 0.9f), 0.03);
            p.Circle(l + 0.12, 0, 0.09, Pen.A("#FFFFFF", 0.7f), C("#5A8DB8"), 0.02);
            p.Circle(l + w - 0.12, 0, 0.09, Pen.A("#FFFFFF", 0.7f), C("#5A8DB8"), 0.02);
        }
    }

    /// <summary>A parent's fold-out bed, opened flat: a padded frame with a raised back at the head and a folded blanket.</summary>
    public static void ParentBed(Pen p, double w, double d, Color colour)
    {
        var (l, t) = (-w / 2, -d / 2);
        p.Box(l, t, w, d, 0.12, Pen.Dark(colour, 0.15f));
        if (d >= w)
        {
            p.Round(l + 0.08, t + 0.08, w - 0.16, d * 0.18, 0.08, Pen.Dark(colour, 0.05f));
            p.Round(l + 0.1, t + (d * 0.22), w - 0.2, d * 0.72, 0.08, colour);
            p.Round(l + (w * 0.2), t + (d * 0.24), w * 0.6, d * 0.12, 0.06, C("#F4F6F8"));
            p.Round(l + 0.14, t + (d * 0.55), w - 0.28, d * 0.22, 0.05, C("#C5CAE9"));
        }
        else
        {
            p.Round(l + 0.08, t + 0.08, w * 0.18, d - 0.16, 0.08, Pen.Dark(colour, 0.05f));
            p.Round(l + (w * 0.22), t + 0.1, w * 0.72, d - 0.2, 0.08, colour);
            p.Round(l + (w * 0.24), t + (d * 0.2), w * 0.12, d * 0.6, 0.06, C("#F4F6F8"));
            p.Round(l + (w * 0.55), t + 0.14, w * 0.22, d - 0.28, 0.05, C("#C5CAE9"));
        }
    }

    /// <summary>A bright toy box, open, with toys inside.</summary>
    public static void ToyBox(Pen p, double w, double d, Color colour, int seed)
    {
        var (l, t) = (-w / 2, -d / 2);
        p.Box(l, t, w, d, 0.08, colour);
        p.Round(l + 0.12, t + 0.12, w - 0.24, d - 0.24, 0.05, Pen.Dark(colour, 0.35f));
        var toys = new[] { C("#FFD54F"), C("#4FC3F7"), C("#81C784"), C("#BA68C8") };
        p.Dot(l + (w * 0.3), t + (d * 0.45), Math.Min(w, d) * 0.16, toys[seed % 4]);
        p.Round(l + (w * 0.52), t + (d * 0.3), w * 0.22, d * 0.36, 0.04, toys[(seed + 1) % 4]);
        p.Dot(l + (w * 0.42), t + (d * 0.68), Math.Min(w, d) * 0.12, C("#C9925A"));
    }

    // ------------------------------------------------------------------ props

    /// <summary>A pot plant.</summary>
    public static void Plant(Pen p, double x, double y, double s = 1)
    {
        p.Shadow(c => p.Circle(x, y, 0.5 * s, c), 0.1, 0.14);
        p.Circle(x, y, 0.34 * s, C("#B5683F"), C("#7E4526"), 0.03);
        p.Circle(x, y, 0.27 * s, C("#4A3324"));
        var seed = (int)Math.Abs((x * 31) + (y * 17));
        for (var i = 0; i < 9; i++)
        {
            var a = (i * 0.698) + (((seed + (i * 7)) % 10) * 0.03);
            var r = (0.3 + (((seed + (i * 13)) % 10) * 0.022)) * s;
            p.Ellipse(x + (Math.Cos(a) * r * 0.6), y + (Math.Sin(a) * r * 0.6), r * 0.62, r * 0.26, i % 2 == 1 ? C("#3E9B4F") : C("#57B565"), C("#2C7A3B"), 0.02, a);
        }

        p.Circle(x, y, 0.1 * s, C("#6CC47A"));
    }

    /// <summary>One of the room decorations standing on the floor (see <see cref="Decorations.FloorPiece"/>), its front along (dx, dy).</summary>
    public static bool Prop(Pen p, string kind, double x, double y, double s, int dx, int dy)
    {
        if (kind == "Plant")
        {
            Plant(p, x, y, Math.Clamp(s, 0.7, 1.2));
            return true;
        }

        p.Push(x, y, FrontAngle((dx, dy)), Math.Clamp(s, 0.7, 1.15));
        var drawn = true;
        switch (kind)
        {
            case "WaterCooler":
                Piece(p, "water_cooler", 0.8, 0.8, default, 0);
                break;
            case "LeafletRack":
                p.Box(-0.4, -0.18, 0.8, 0.36, 0.04, C("#8E99A5"));
                for (var i = 0; i < 4; i++)
                {
                    p.Rect(-0.34 + (i * 0.18), -0.12, 0.14, 0.24, new[] { C("#6FA8DC"), C("#F5C518"), C("#E58A84"), C("#93C99A") }[i]);
                }

                break;
            case "Scales":
                p.Box(-0.3, -0.34, 0.6, 0.68, 0.08, C("#E9EEF2"));
                p.Round(-0.2, -0.28, 0.4, 0.16, 0.03, Casing);
                break;
            case "Bookshelf" or "ScopeCabinet" or "PlasterShelf":
                Piece(p, "medicine_shelves", 1.4, 0.5, default, (int)(x + y));
                break;
            case "Fridge":
                Piece(p, "drug_fridge", 0.9, 0.8, default, 0);
                break;
            case "Skeleton":
                p.Shadow(c => p.Circle(0, 0, 0.32, c));
                p.Circle(0, 0, 0.3, C("#3A434D"));
                p.Ellipse(0, 0, 0.26, 0.14, C("#F2EEDF"), C("#B9B19C"), 0.02);
                p.Circle(0, -0.04, 0.11, C("#F7F4E8"), C("#B9B19C"), 0.02);
                break;
            case "HeartModel" or "BrainModel" or "GutModel" or "PelvisModel" or "MannequinHead":
                p.Box(-0.24, -0.24, 0.48, 0.48, 0.06, C("#8A6A45"));
                p.Dot(0, 0, 0.16, kind == "HeartModel" ? C("#D8483B") : kind == "BrainModel" ? C("#E8B4BC") : C("#F2E3C2"), 0.02);
                break;
            case "PrivacyScreen":
                p.Path(C("#7FB7CF"), 0.09, (-0.7, 0.1), (-0.25, -0.1), (0.25, 0.1), (0.7, -0.1));
                break;
            case "Kiosk":
                p.Box(-0.3, -0.25, 0.6, 0.5, 0.06, C("#3C4A58"));
                p.Round(-0.22, -0.05, 0.44, 0.24, 0.03, C("#7FD1E8"));
                break;
            case "FloorLamp":
                p.Shadow(c => p.Circle(0, 0, 0.3, c));
                p.Dot(0, 0, 0.28, C("#FFF1C2"), 0.02);
                p.Circle(0, 0, 0.08, C("#8A6A45"));
                break;
            case "BlanketStack":
                p.Box(-0.34, -0.26, 0.68, 0.52, 0.08, C("#8DB3D9"));
                p.Line(-0.3, -0.08, 0.3, -0.08, Pen.Dark(C("#8DB3D9"), 0.2f), 0.03);
                p.Line(-0.3, 0.08, 0.3, 0.08, Pen.Dark(C("#8DB3D9"), 0.2f), 0.03);
                break;
            case "ScrubSink":
                Basin(p, 1.2, 0.7, true);
                break;
            case "Bucket" or "Bins":
                p.Shadow(c => p.Circle(0, 0, 0.26, c));
                p.Dot(0, 0, 0.25, kind == "Bins" ? C("#F5C518") : C("#E9E4D6"), 0.03);
                p.Circle(0, 0, 0.17, kind == "Bins" ? C("#3A434D") : C("#C9C2AE"));
                break;
            case "GymBall":
                Piece(p, "birthing_ball", 0.9, 0.9, default, 0);
                break;
            case "Dumbbells":
                p.Line(-0.2, -0.1, 0.2, -0.1, DarkSteel, 0.05);
                p.Line(-0.2, 0.12, 0.2, 0.12, DarkSteel, 0.05);
                foreach (var (ex, ey) in new[] { (-0.22, -0.1), (0.22, -0.1), (-0.22, 0.12), (0.22, 0.12) })
                {
                    p.Circle(ex, ey, 0.08, C("#3B7DD8"));
                }

                break;
            case "OxygenSpare":
                Piece(p, "oxygen_cylinder", 0.6, 0.6, default, 0);
                break;
            case "SlingBox" or "AirwayBag":
                p.Box(-0.3, -0.22, 0.6, 0.44, 0.08, kind == "AirwayBag" ? C("#D8483B") : C("#5C8FA8"));
                p.Line(-0.2, 0, 0.2, 0, new Color(1, 1, 1, 0.6f), 0.04);
                break;
            default:
                drawn = false;
                break;
        }

        p.Pop();
        return drawn;
    }

    /// <summary>A small thing on a desk or counter (see <see cref="Decorations.TopPiece"/>). Computers are drawn with the desk itself.</summary>
    public static void OnTop(Pen p, string kind, double x, double y)
    {
        switch (kind)
        {
            case "Phone":
                p.Round(x - 0.14, y - 0.1, 0.28, 0.2, 0.04, C("#3A434D"));
                p.Round(x - 0.12, y - 0.08, 0.08, 0.16, 0.03, C("#5E6A76"));
                break;
            case "Folders":
                p.Round(x - 0.16, y - 0.12, 0.3, 0.22, 0.02, C("#6FA8DC"), C("#3C6FA0"), 0.02);
                p.Round(x - 0.1, y - 0.06, 0.3, 0.22, 0.02, C("#F5C518"), C("#B38F00"), 0.02);
                break;
            case "KettleMugs":
                p.Circle(x - 0.08, y, 0.12, C("#DDE3E8"), DarkSteel, 0.02);
                p.Circle(x + 0.14, y - 0.06, 0.06, Paper, PaperEdge, 0.02);
                p.Circle(x + 0.16, y + 0.1, 0.06, C("#E58A84"), C("#A8554F"), 0.02);
                break;
            case "PillTray":
                p.Round(x - 0.18, y - 0.1, 0.36, 0.2, 0.03, C("#E9EEF2"), DarkSteel, 0.02);
                p.Circle(x - 0.08, y, 0.035, C("#D8483B"));
                p.Circle(x + 0.02, y - 0.02, 0.035, C("#3B7DD8"));
                p.Circle(x + 0.1, y + 0.03, 0.035, C("#F5C518"));
                break;
            case "ReflexHammer" or "GelWarmer":
                p.Round(x - 0.1, y - 0.1, 0.2, 0.2, 0.05, C("#C9D3DB"), DarkSteel, 0.02);
                break;
        }
    }

    /// <summary>A wet floor: a puddle with a yellow warning sign beside it.</summary>
    public static void Puddle(Pen p, double x, double y, int seed)
    {
        var a = (seed % 7) * 0.5;
        p.Ellipse(x, y, 0.55, 0.36, Pen.A("#9CC7E0", 0.55f), Pen.A("#6FA6C4", 0.7f), 0.025, a);
        p.Ellipse(x + 0.2, y - 0.1, 0.2, 0.12, new Color(1, 1, 1, 0.35f), null, 0, a);
        p.Poly(C("#F5C518"), C("#8A6F00"), 0.025, (x + 0.45, y + 0.5), (x + 0.75, y + 0.5), (x + 0.6, y + 0.2));
    }
}
