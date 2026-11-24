using System;
using Godot;

namespace BlueprintHospital.TopDown;

/// <summary>
/// The 2D (top-down) drawings of the decorations the player places on corridors and in open areas,
/// matching <see cref="World3D.PublicProps3D"/>. Drawn in the prop's own frame: origin at its middle,
/// +y its front, one unit a tile.
/// </summary>
public static class PublicPropsKit
{
    private static Color C(string hex) => new(hex);

    private static readonly Color Oak = C("#C99A6B");
    private static readonly Color OakDark = C("#9A6E45");
    private static readonly Color Steel = C("#9AA6B0");
    private static readonly Color Dark = C("#2B3036");

    public static bool Prop(Pen p, string kind, double x, double y, int dx, int dy, Color tint)
    {
        p.Push(x, y, FurnitureKit.FrontAngle((dx, dy)));
        var drawn = true;
        switch (kind)
        {
            case "Palm":
                p.Shadow(c => p.Circle(0, 0, 0.85, c), 0.14, 0.2);
                p.Circle(0, 0, 0.3, C("#E9E4DA"), C("#B9B19C"), 0.03);
                for (var i = 0; i < 9; i++)
                {
                    var a = i * Math.Tau / 9;
                    p.Ellipse(Math.Cos(a) * 0.45, Math.Sin(a) * 0.45, 0.42, 0.11, i % 2 == 0 ? C("#3F8F4A") : C("#57B565"), C("#2C7A3B"), 0.02, a);
                }

                p.Circle(0, 0, 0.12, C("#6CC47A"));
                break;
            case "Bench":
                p.Shadow(c => p.Round(-1.45, -0.42, 2.9, 0.84, 0.08, c));
                p.Rect(-1.3, -0.4, 0.08, 0.8, Steel);
                p.Rect(1.22, -0.4, 0.08, 0.8, Steel);
                for (var i = 0; i < 4; i++)
                {
                    p.Solid(-1.45, 0.28 - (i * 0.2), 2.9, 0.15, 0.03, i % 2 == 0 ? Oak : Pen.Dark(Oak, 0.06f), 0.02);
                }

                p.Solid(-1.45, -0.42, 2.9, 0.1, 0.03, OakDark, 0.02);
                break;
            case "GelStation":
                p.Shadow(c => p.Circle(0, 0, 0.26, c));
                p.Round(-0.25, -0.2, 0.5, 0.4, 0.05, C("#7C8893"));
                p.Round(-0.15, -0.05, 0.3, 0.22, 0.04, C("#F4F6F8"), C("#8E99A5"), 0.02);
                p.Rect(-0.21, -0.02, 0.42, 0.05, C("#1E88E5"));
                break;
            case "FishTank":
                p.Shadow(c => p.Round(-1.2, -0.45, 2.4, 0.9, 0.05, c));
                p.Solid(-1.2, -0.45, 2.4, 0.9, 0.04, Dark, 0.03);
                p.Round(-1.12, -0.38, 2.24, 0.76, 0.03, C("#4FC3F7"));
                p.Round(-1.12, 0.12, 2.24, 0.26, 0.03, C("#E2D3B0"));
                foreach (var (fx, fy, c) in new[] { (-0.4, -0.1, "#FF8A3D"), (0.3, 0.05, "#FFD54F"), (0.6, -0.2, "#E53935") })
                {
                    p.Ellipse(fx, fy, 0.09, 0.04, C(c), null);
                }

                p.Dot(-0.85, -0.15, 0.08, C("#2E8B57"));
                p.Dot(0.9, 0.0, 0.07, C("#2E8B57"));
                break;
            case "Sculpture":
                p.Shadow(c => p.Round(-0.43, -0.43, 0.86, 0.86, 0.04, c));
                p.Solid(-0.43, -0.43, 0.86, 0.86, 0.04, C("#ECEFF1"));
                p.Circle(0, 0, 0.32, null, tint, 0.07);
                p.Ellipse(0, 0, 0.32, 0.1, null, Pen.Light(tint, 0.25f), 0.06);
                p.Dot(0, 0, 0.09, C("#F5C518"));
                break;
            case "NoticeStand":
                p.Shadow(c => p.Round(-0.68, -0.08, 1.36, 0.16, 0.03, c));
                p.Rect(-0.6, -0.25, 0.1, 0.5, Steel);
                p.Rect(0.5, -0.25, 0.1, 0.5, Steel);
                p.Solid(-0.65, -0.05, 1.3, 0.1, 0.02, OakDark);
                p.Rect(-0.5, 0.03, 0.25, 0.05, C("#FFF59D"));
                p.Rect(-0.1, 0.03, 0.25, 0.05, C("#BBDEFB"));
                p.Rect(0.25, 0.03, 0.2, 0.05, C("#F8BBD0"));
                break;
            case "Sofa":
                p.Shadow(c => p.Round(-1.95, -0.85, 3.9, 1.7, 0.2, c));
                p.Solid(-1.95, -0.85, 3.9, 1.7, 0.2, Pen.Dark(tint, 0.12f));
                for (var i = 0; i < 3; i++)
                {
                    p.Round(-1.7 + (i * 1.15), -0.45, 1.1, 1.18, 0.12, Pen.Light(tint, 0.08f), Pen.Outline(tint), 0.02);
                }

                p.Round(-1.95, -0.85, 3.9, 0.4, 0.14, tint, Pen.Outline(tint), 0.025);
                break;
            case "Armchair":
                p.Shadow(c => p.Round(-0.8, -0.8, 1.6, 1.6, 0.2, c));
                p.Solid(-0.8, -0.8, 1.6, 1.6, 0.2, Pen.Dark(tint, 0.12f));
                p.Round(-0.55, -0.45, 1.1, 1.18, 0.12, Pen.Light(tint, 0.08f), Pen.Outline(tint), 0.02);
                p.Round(-0.8, -0.8, 1.6, 0.36, 0.14, tint, Pen.Outline(tint), 0.025);
                break;
            case "CoffeeTable":
                p.Shadow(c => p.Round(-1.2, -0.6, 2.4, 1.2, 0.08, c));
                p.Solid(-1.2, -0.6, 2.4, 1.2, 0.08, Oak);
                p.Rect(-0.75, -0.08, 0.5, 0.36, C("#E57373"));
                p.Rect(-0.67, -0.06, 0.5, 0.36, C("#64B5F6"));
                p.Circle(0.55, -0.1, 0.1, C("#F4F6F8"), Steel, 0.02);
                break;
            case "CafeTable":
                foreach (var side in new[] { -1, 1 })
                {
                    p.Shadow(c => p.Round((side * 1.15) - 0.4, -0.4, 0.8, 0.8, 0.12, c));
                    p.Solid((side * 1.15) - 0.4, -0.4, 0.8, 0.8, 0.12, tint);
                    p.Round(side < 0 ? -1.55 : 1.47, -0.4, 0.08, 0.8, 0.03, Pen.Dark(tint, 0.2f));
                }

                p.Shadow(c => p.Circle(0, 0, 0.72, c));
                p.Circle(0, 0, 0.72, C("#F4F6F8"), Steel, 0.03);
                p.Circle(0.2, 0.1, 0.08, C("#FFFFFF"), Steel, 0.02);
                break;
            case "Rug":
                p.Round(-2.8, -1.9, 5.6, 3.8, 0.05, tint);
                p.Round(-2.5, -1.6, 5.0, 3.2, 0.04, Pen.Light(tint, 0.35f));
                p.Round(-2.2, -1.3, 4.4, 2.6, 0.04, Pen.Light(tint, 0.08f));
                for (var i = -2; i <= 2; i++)
                {
                    p.Rect((i * 0.8) - 0.06, -1.1, 0.12, 2.2, Pen.Light(tint, 0.45f));
                }

                break;
            case "Bookcase":
                p.Shadow(c => p.Round(-0.9, -0.35, 1.8, 0.7, 0.03, c));
                p.Solid(-0.9, -0.35, 1.8, 0.7, 0.03, Oak);
                var colours = new[] { "#C0392B", "#2E86C1", "#F4D03F", "#27AE60", "#8E44AD", "#E67E22" };
                for (var i = 0; i < 9; i++)
                {
                    p.Rect(-0.8 + (i * 0.18), -0.25, 0.14, 0.42, C(colours[i % colours.Length]));
                }

                break;
            case "Planter":
                p.Shadow(c => p.Round(-1.48, -0.48, 2.96, 0.96, 0.05, c));
                p.Solid(-1.48, -0.48, 2.96, 0.96, 0.05, C("#ECEFF1"));
                for (var i = 0; i < 7; i++)
                {
                    p.Dot(-1.15 + (i * 0.38), i % 2 == 0 ? 0.08 : -0.08, 0.27, i % 2 == 0 ? C("#3F8F4A") : C("#57B565"));
                }

                break;
            default:
                drawn = false;
                break;
        }

        p.Pop();
        return drawn;
    }

    /// <summary>
    /// A wall item seen from above: a thin strip (a circle for a clock) standing off the wall behind the tile,
    /// centred on (x, y) with its front facing (dx, dy).
    /// </summary>
    public static void WallItem(Pen p, string kind, double x, double y, double length, int dx, int dy)
    {
        p.Push(x, y, FurnitureKit.FrontAngle((dx, dy)));
        var (face, edge) = kind switch
        {
            "Tv" => (C("#1B1F24"), C("#0D0F12")),
            "Clock" => (C("#FFFFFF"), C("#37474F")),
            "Noticeboard" => (C("#C9A27A"), C("#7A5A3C")),
            _ => (C("#F3E5D8"), C("#8D6E63")),
        };
        const double back = -0.32;
        if (kind == "Clock")
        {
            p.Circle(0, back + 0.18, 0.2, face, edge, 0.03);
        }
        else
        {
            p.Shadow(c => p.Round(-length / 2, back, length, 0.12, 0.02, c));
            p.Round(-length / 2, back, length, 0.12, 0.02, face, edge, 0.025);
        }

        p.Pop();
    }
}
