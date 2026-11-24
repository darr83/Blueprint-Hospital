using System;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// The decorations the player places on corridors and in open areas: plants, benches, sofas,
/// tables, a fish tank and so on. Built in the prop's own frame like <see cref="Props3D"/>: origin
/// on the floor at its middle, +z its front, x across, one unit a tile (half a metre).
/// </summary>
public static class PublicProps3D
{
    private static float H(double px) => Kit3D.H(px);

    private static float M(double metres) => Kit3D.H(metres * 28);

    private static readonly Color Steel = new("#9AA6B0");
    private static readonly Color Dark = new("#2B3036");
    private static readonly Color Oak = new("#C99A6B");
    private static readonly Color OakDark = new("#9A6E45");
    private static readonly Color Leaf = new("#3F8F4A");

    private static void Slab(MeshBatch b, double x, double z, double w, double d, double fromM, double toM, Color colour) =>
        b.Box(new Vector3((float)x, M((fromM + toM) / 2), (float)z), new Vector3((float)w, Math.Max(0.004f, M(toM - fromM)), (float)d), colour);

    private static void Leg(MeshBatch b, double x, double z, double topM, Color colour, float r = 0.035f) =>
        b.Cylinder(new Vector3((float)x, M(topM / 2), (float)z), r, M(topM), r, colour);

    /// <summary>Builds a placed decoration; false if <paramref name="kind"/> isn't one.</summary>
    public static bool Floor(MeshBatch b, MeshBatch glass, string kind, Color tint)
    {
        switch (kind)
        {
            case "Palm":
                b.Cylinder(new Vector3(0, M(0.22), 0), 0.3f, M(0.44), 0.26f, new Color("#E9E4DA"));
                b.Cylinder(new Vector3(0, M(0.45), 0), 0.32f, M(0.03), 0.32f, new Color("#D2CBBE"));
                b.Cylinder(new Vector3(0, M(0.46), 0), 0.27f, M(0.01), 0.27f, new Color("#4A3324"));
                b.Rod(new Vector3(0, M(0.45), 0), new Vector3(0.06f, M(1.55), 0.03f), 0.06f, new Color("#7A5A3C"));
                for (var i = 0; i < 9; i++)
                {
                    var a = i * Mathf.Tau / 9f;
                    var tip = new Vector3(Mathf.Cos(a) * 0.85f, M(1.15 + ((i % 3) * 0.12)), Mathf.Sin(a) * 0.85f);
                    b.Capsule(new Vector3(0.06f, M(1.6), 0.03f), tip, 0.09f, i % 2 == 0 ? Leaf : Leaf.Lightened(0.12f), 0.35f);
                }

                b.Ball(new Vector3(0.06f, M(1.66), 0.03f), new Vector3(0.3f, M(0.2), 0.3f), Leaf.Lightened(0.2f));
                return true;

            case "Bench":
            {
                // A 1.5 m public bench: steel frame, oak slats, a low back.
                var w = 2.9;
                foreach (var x in new[] { -1.25, 1.25 })
                {
                    Slab(b, x, 0.05, 0.08, 0.8, 0, 0.42, Steel);
                    Slab(b, x, -0.34, 0.08, 0.1, 0.42, 0.82, Steel);
                }

                for (var i = 0; i < 4; i++)
                {
                    Slab(b, 0, 0.36 - (i * 0.2), w, 0.15, 0.42, 0.46, i % 2 == 0 ? Oak : Oak.Darkened(0.06f));
                }

                Slab(b, 0, -0.36, w, 0.06, 0.58, 0.7, Oak);
                Slab(b, 0, -0.36, w, 0.06, 0.72, 0.84, Oak.Darkened(0.06f));
                return true;
            }

            case "GelStation":
                Slab(b, 0, 0, 0.5, 0.4, 0, 0.03, Steel.Darkened(0.2f));
                Leg(b, 0, 0, 1.25, Steel, 0.04f);
                Slab(b, 0, 0.06, 0.28, 0.2, 0.95, 1.22, new Color("#F4F6F8"));
                Slab(b, 0, 0.17, 0.2, 0.02, 1.0, 1.18, new Color("#1E88E5"));
                Slab(b, 0, 0.12, 0.08, 0.1, 0.9, 0.95, Dark);
                Slab(b, 0, 0.04, 0.42, 0.04, 1.26, 1.52, new Color("#1E88E5"));
                Slab(b, 0, 0.065, 0.32, 0.01, 1.32, 1.46, new Color("#F4F6F8"));
                return true;

            case "FishTank":
            {
                // A long cabinet with a lit tank on top: water, weed, gravel and a few fish.
                Slab(b, 0, 0, 2.4, 0.9, 0, 0.75, new Color("#3C4A58"));
                Slab(b, 0, 0.46, 2.2, 0.02, 0.08, 0.68, new Color("#4A5A6A"));
                Slab(b, 0, 0, 2.4, 0.9, 0.75, 0.79, Dark);
                Slab(b, 0, 0, 2.3, 0.82, 0.79, 0.86, new Color("#E2D3B0"));
                glass.Box(new Vector3(0, M(1.06), 0), new Vector3(2.32f, M(0.42), 0.84f), new Color("#4FC3F7", 0.55f));
                Slab(b, 0, -0.38, 2.4, 0.14, 1.27, 1.33, Dark);
                Slab(b, 0, 0.42, 2.4, 0.06, 1.27, 1.3, Dark);
                Slab(b, 0, -0.4, 2.3, 0.04, 0.86, 1.27, new Color("#1C6E8C"));
                foreach (var (x, h) in new[] { (-0.8, 0.36), (-0.55, 0.26), (0.7, 0.32), (0.95, 0.22) })
                {
                    b.Capsule(new Vector3((float)x, M(0.86), -0.2f), new Vector3((float)x + 0.04f, M(0.86 + h), -0.18f), 0.05f, new Color("#2E8B57"));
                }

                foreach (var (x, y, z, c) in new[] { (-0.3, 1.02, 0.1, "#FF8A3D"), (0.25, 1.12, -0.1, "#FFD54F"), (0.45, 0.98, 0.2, "#FF8A3D"), (-0.05, 1.18, 0.0, "#E53935") })
                {
                    b.Ball(new Vector3((float)x, M(y), (float)z), new Vector3(0.16f, M(0.07), 0.06f), new Color(c), small: true);
                }

                return true;
            }

            case "Sculpture":
                Slab(b, 0, 0, 0.8, 0.8, 0, 0.6, new Color("#ECEFF1"));
                Slab(b, 0, 0, 0.86, 0.86, 0.6, 0.63, new Color("#CFD8DC"));
                b.Ring(new Vector3(0, M(1.05), 0), 0.34f, tint, new Basis(Vector3.Right, Mathf.Pi / 2), 0.6f);
                b.Ring(new Vector3(0, M(1.05), 0), 0.34f, tint.Lightened(0.25f), new Basis(Vector3.Up, Mathf.Pi / 2) * new Basis(Vector3.Right, Mathf.Pi / 2), 0.6f);
                b.Ball(new Vector3(0, M(1.05), 0), new Vector3(0.16f, 0.16f, 0.16f), new Color("#F5C518"));
                return true;

            case "NoticeStand":
                foreach (var x in new[] { -0.55, 0.55 })
                {
                    Leg(b, x, 0, 1.55, Steel, 0.03f);
                    Slab(b, x, 0, 0.1, 0.5, 0, 0.03, Steel.Darkened(0.2f));
                }

                Slab(b, 0, 0, 1.3, 0.06, 0.7, 1.6, OakDark);
                Slab(b, 0, 0.035, 1.18, 0.01, 0.76, 1.54, new Color("#C9A27A"));
                foreach (var (x, y, c) in new[] { (-0.35, 1.3, "#F4F6F8"), (0.05, 1.36, "#FFF59D"), (0.38, 1.24, "#BBDEFB"), (-0.25, 0.95, "#F8BBD0"), (0.25, 0.98, "#F4F6F8") })
                {
                    Slab(b, x, 0.042, 0.3, 0.005, y - 0.14, y + 0.14, new Color(c));
                }

                return true;

            case "Sofa":
            {
                // A three-seat sofa, 2 m wide, upholstered in the chosen colour.
                var cloth = tint;
                Slab(b, 0, 0, 3.9, 1.7, 0.06, 0.4, cloth.Darkened(0.12f));
                for (var i = 0; i < 3; i++)
                {
                    Slab(b, -1.15 + (i * 1.15), 0.12, 1.1, 1.3, 0.4, 0.52, cloth.Lightened(0.06f));
                    Slab(b, -1.15 + (i * 1.15), -0.62, 1.1, 0.34, 0.52, 0.95, cloth);
                }

                Slab(b, 0, -0.76, 3.9, 0.2, 0.06, 0.98, cloth.Darkened(0.08f));
                foreach (var x in new[] { -1.82, 1.82 })
                {
                    Slab(b, x, 0, 0.26, 1.7, 0.06, 0.66, cloth.Darkened(0.05f));
                }

                foreach (var (x, z) in new[] { (-1.8, -0.7), (1.8, -0.7), (-1.8, 0.7), (1.8, 0.7) })
                {
                    Leg(b, x, z, 0.06, Dark, 0.05f);
                }

                return true;
            }

            case "Armchair":
            {
                var cloth = tint;
                Slab(b, 0, 0, 1.6, 1.6, 0.06, 0.4, cloth.Darkened(0.12f));
                Slab(b, 0, 0.12, 1.1, 1.2, 0.4, 0.52, cloth.Lightened(0.06f));
                Slab(b, 0, -0.68, 1.6, 0.26, 0.06, 0.98, cloth);
                foreach (var x in new[] { -0.68, 0.68 })
                {
                    Slab(b, x, 0, 0.24, 1.6, 0.06, 0.66, cloth.Darkened(0.05f));
                }

                foreach (var (x, z) in new[] { (-0.68, -0.68), (0.68, -0.68), (-0.68, 0.68), (0.68, 0.68) })
                {
                    Leg(b, x, z, 0.06, OakDark, 0.05f);
                }

                return true;
            }

            case "CoffeeTable":
                foreach (var (x, z) in new[] { (-1.0, -0.45), (1.0, -0.45), (-1.0, 0.45), (1.0, 0.45) })
                {
                    Leg(b, x, z, 0.4, OakDark, 0.05f);
                }

                Slab(b, 0, 0, 2.3, 1.1, 0.12, 0.14, OakDark);
                Slab(b, 0, 0, 2.4, 1.2, 0.4, 0.45, Oak);
                Slab(b, -0.5, 0.1, 0.5, 0.36, 0.45, 0.47, new Color("#E57373"));
                Slab(b, -0.42, 0.12, 0.5, 0.36, 0.47, 0.49, new Color("#64B5F6"));
                b.Cylinder(new Vector3(0.55f, M(0.5), -0.1f), 0.1f, M(0.1), 0.1f, new Color("#F4F6F8"));
                return true;

            case "CafeTable":
            {
                // A round table for two with a chair either side.
                b.Cylinder(new Vector3(0, M(0.015), 0), 0.32f, M(0.03), 0.32f, Dark);
                Leg(b, 0, 0, 0.72, Dark, 0.05f);
                b.Cylinder(new Vector3(0, M(0.735), 0), 0.72f, M(0.03), 0.72f, new Color("#F4F6F8"));
                b.Cylinder(new Vector3(0, M(0.715), 0), 0.7f, M(0.012), 0.7f, Oak);
                foreach (var side in new[] { -1, 1 })
                {
                    var x = side * 1.15;
                    foreach (var (lx, lz) in new[] { (-0.3, -0.3), (0.3, -0.3), (-0.3, 0.3), (0.3, 0.3) })
                    {
                        Leg(b, x + lx, lz, 0.45, Steel, 0.025f);
                    }

                    Slab(b, x, 0, 0.8, 0.8, 0.43, 0.48, tint);
                    Slab(b, x + (side * 0.38), 0, 0.06, 0.78, 0.48, 0.88, tint.Darkened(0.08f));
                }

                b.Cylinder(new Vector3(0.2f, M(0.8), 0.1f), 0.07f, M(0.1), 0.07f, new Color("#F4F6F8"));
                return true;
            }

            case "Rug":
            {
                Slab(b, 0, 0, 5.6, 3.8, 0.02, 0.04, tint);
                Slab(b, 0, 0, 5.0, 3.2, 0.04, 0.044, tint.Lightened(0.35f));
                Slab(b, 0, 0, 4.4, 2.6, 0.044, 0.048, tint.Lightened(0.08f));
                for (var i = -2; i <= 2; i++)
                {
                    Slab(b, i * 0.8, 0, 0.12, 2.2, 0.048, 0.05, tint.Lightened(0.45f));
                }

                return true;
            }

            case "Bookcase":
            {
                Slab(b, 0, -0.32, 1.8, 0.06, 0, 1.6, OakDark);
                Slab(b, -0.87, 0, 0.06, 0.7, 0, 1.6, Oak);
                Slab(b, 0.87, 0, 0.06, 0.7, 0, 1.6, Oak);
                Slab(b, 0, 0, 1.8, 0.7, 1.56, 1.6, Oak);
                Slab(b, 0, 0, 1.8, 0.7, 0, 0.08, Oak);
                var colours = new[] { "#C0392B", "#2E86C1", "#F4D03F", "#27AE60", "#8E44AD", "#E67E22", "#ECF0F1" };
                for (var shelf = 0; shelf < 4; shelf++)
                {
                    var y0 = 0.08 + (shelf * 0.38);
                    Slab(b, 0, 0, 1.68, 0.66, y0 + 0.34, y0 + 0.37, Oak);
                    for (var i = 0; i < 9; i++)
                    {
                        if ((i + shelf) % 5 == 4)
                        {
                            continue;
                        }

                        var h = 0.24 + (((i * 7) + (shelf * 3)) % 5 * 0.02);
                        Slab(b, -0.72 + (i * 0.18), 0.0, 0.14, 0.5, y0, y0 + h, new Color(colours[(i + (shelf * 2)) % colours.Length]));
                    }
                }

                return true;
            }

            case "Planter":
            {
                // A long low trough of plants, 1.5 m: good for breaking up a big space.
                Slab(b, 0, 0, 2.9, 0.9, 0, 0.5, new Color("#ECEFF1"));
                Slab(b, 0, 0, 2.96, 0.96, 0.5, 0.53, new Color("#CFD8DC"));
                Slab(b, 0, 0, 2.7, 0.7, 0.5, 0.52, new Color("#4A3324"));
                for (var i = 0; i < 7; i++)
                {
                    var x = -1.15 + (i * 0.38);
                    var h = 0.75 + ((i * 5) % 3 * 0.08);
                    b.Ball(new Vector3((float)x, M(h), (i % 2 == 0 ? 0.1f : -0.1f)), new Vector3(0.5f, M(0.45), 0.5f), i % 2 == 0 ? Leaf : Leaf.Lightened(0.12f));
                }

                return true;
            }

            case "Vending":
                return false;

            default:
                return false;
        }
    }
}
