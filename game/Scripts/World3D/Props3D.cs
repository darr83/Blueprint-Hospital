using System;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// The 3D versions of the room decorations: props standing on the floor (plants, a water cooler,
/// a skeleton in fracture clinic, a model heart in cardiology), the small things on desks and
/// counters, and the lights and screens hanging over theatre and cath lab tables. Each is built in
/// its own frame: origin on the floor (or the desk top) at its middle, +z its front, x across.
/// </summary>
public static class Props3D
{
    private static float H(double px) => Kit3D.H(px);

    private static readonly Color Steel = new("#9AA6B0");
    private static readonly Color Dark = new("#2B3036");
    private static readonly Color White = new("#F4F6F8");

    /// <summary>A box standing on the floor (or at <paramref name="lift"/>), w across, d deep, h tall (2D pixels).</summary>
    private static void Cube(MeshBatch b, double w, double d, float hPx, Color colour, float liftPx = 0, double z = 0, double x = 0) =>
        b.Box(new Vector3((float)x, H(liftPx + (hPx / 2)), (float)z), new Vector3((float)w, H(hPx), (float)d), colour);

    /// <summary>A plain plinth for the anatomy models.</summary>
    private static void Pedestal(MeshBatch b)
    {
        Cube(b, 0.6, 0.6, 22, new Color("#E7EBEE"));
        Cube(b, 0.64, 0.64, 1.5f, new Color("#C9D2DA"), 21);
    }

    /// <summary>Builds a floor prop; false if <paramref name="kind"/> isn't one.</summary>
    public static bool Floor(MeshBatch b, MeshBatch glass, string kind)
    {
        switch (kind)
        {
            case "Plant":
            {
                b.Cylinder(new Vector3(0, H(6), 0), 0.17f, H(12), 0.17f, new Color("#B5653A"));
                b.Cylinder(new Vector3(0, H(12.5f), 0), 0.19f, H(3), 0.19f, new Color("#C9774A"));
                b.Cylinder(new Vector3(0, H(13.6f), 0), 0.16f, H(0.6f), 0.16f, new Color("#5A3E2B"));
                var leaf = new Color("#3F8F4A");
                foreach (var (x, y, z, r) in new[] { (-0.16f, 21f, 0.04f, 0.17f), (0.16f, 22f, -0.05f, 0.17f), (0f, 29f, 0f, 0.2f), (-0.05f, 18f, 0.14f, 0.14f), (0.1f, 17f, 0.1f, 0.14f), (0.02f, 24f, -0.15f, 0.16f) })
                {
                    b.Ball(new Vector3(x, H(y), z), new Vector3(r * 2, r * 1.7f, r * 2), x < 0 ? leaf : leaf.Lightened(0.08f));
                }

                b.Ball(new Vector3(-0.05f, H(31), 0.05f), new Vector3(0.18f, 0.14f, 0.18f), leaf.Lightened(0.2f));
                return true;
            }

            case "WaterCooler":
                Cube(b, 0.55, 0.55, 22, new Color("#E9EDF0"));
                Cube(b, 0.12, 0.04, 2, new Color("#1E88E5"), 13, 0.29);
                Cube(b, 0.3, 0.12, 1, Steel, 8, 0.3);
                glass.Cylinder(new Vector3(0, H(31), 0), 0.22f, H(14), 0.22f, new Color("#64B5F6", 0.75f));
                glass.Ball(new Vector3(0, H(38), 0), new Vector3(0.44f, H(5), 0.44f), new Color("#64B5F6", 0.75f));
                return true;

            case "LeafletRack":
            {
                Cube(b, 0.7, 0.2, 30, new Color("#B9C3CB"), 0, -0.05);
                var colours = new[] { "#43A047", "#1F5FBF", "#F9A825", "#E53935" };
                for (var i = 0; i < 4; i++)
                {
                    var y = 26 - (i * 7f);
                    b.Box(new Vector3(-0.16f, H(y), 0.08f), new Vector3(0.27f, H(6), 0.03f), new Basis(Vector3.Right, -0.2f), new Color(colours[i]));
                    b.Box(new Vector3(0.16f, H(y), 0.08f), new Vector3(0.27f, H(6), 0.03f), new Basis(Vector3.Right, -0.2f), White);
                }

                return true;
            }

            case "Scales":
                Cube(b, 0.55, 0.55, 3, new Color("#ECEFF1"));
                b.Rod(new Vector3(0, H(3), -0.2f), new Vector3(0, H(30), -0.2f), 0.03f, Steel, small: true);
                b.Box(new Vector3(0, H(32.5f), -0.18f), new Vector3(0.3f, H(7), 0.08f), new Color("#37474F"));
                b.Box(new Vector3(0, H(32.8f), -0.135f), new Vector3(0.22f, H(4), 0.01f), new Color("#A5D6A7"));
                return true;

            case "SlingBox":
                Cube(b, 0.7, 0.5, 9, new Color("#D7B98E"));
                b.Ball(new Vector3(-0.08f, H(9.5f), 0), new Vector3(0.2f, H(3), 0.2f), new Color("#5C8FC4"), small: true);
                b.Ball(new Vector3(0.12f, H(9.5f), 0.05f), new Vector3(0.16f, H(2.5f), 0.16f), White, small: true);
                return true;

            case "AirwayBag":
                Cube(b, 0.8, 0.5, 9, new Color("#D84B3E"));
                Cube(b, 0.82, 0.06, 1.2f, Dark, 6, 0);
                b.Box(new Vector3(0, H(5), 0.255f), new Vector3(0.14f, H(4), 0.01f), White);
                b.Rod(new Vector3(-0.18f, H(10), 0), new Vector3(0.18f, H(10), 0), 0.025f, Dark, small: true);
                return true;

            case "Bookshelf":
            {
                var wood = new Color("#8C6D4F");
                Cube(b, 0.9, 0.35, 44, wood.Darkened(0.15f), 0, -0.02);
                var colours = new[] { "#1F5FBF", "#E53935", "#43A047", "#F9A825", "#ECEFF1", "#7E57C2" };
                for (var i = 0; i < 4; i++)
                {
                    Cube(b, 0.86, 0.3, 1.2f, wood, 2 + (i * 10.5f), 0.01);
                    for (var k = 0; k < 6; k++)
                    {
                        var hBook = 7 + ((i + k) % 3);
                        Cube(b, 0.11, 0.24, hBook, new Color(colours[(i + (k * 2)) % 6]), 3.2f + (i * 10.5f), 0.04, -0.33 + (k * 0.13));
                    }
                }

                Cube(b, 0.9, 0.35, 1.5f, wood, 43, -0.02);
                return true;
            }

            case "Fridge":
                Cube(b, 0.7, 0.65, 42, new Color("#F2F4F6"));
                Cube(b, 0.68, 0.01, 0.6f, new Color("#AEB8C1"), 26, 0.33);
                b.Box(new Vector3(0.26f, H(32), 0.35f), new Vector3(0.03f, H(8), 0.03f), new Color("#8D99A3"));
                b.Box(new Vector3(0.26f, H(18), 0.35f), new Vector3(0.03f, H(8), 0.03f), new Color("#8D99A3"));
                b.Ball(new Vector3(-0.15f, H(35), 0.335f), new Vector3(0.06f, 0.06f, 0.02f), new Color("#F48FB1"), small: true);
                return true;

            case "Skeleton":
                Skeleton(b);
                return true;

            case "HeartModel":
            {
                Pedestal(b);
                var red = new Color("#C62828");
                b.Ball(new Vector3(-0.05f, H(31), 0), new Vector3(0.3f, H(13), 0.26f), red);
                b.Ball(new Vector3(0.07f, H(29), 0.02f), new Vector3(0.24f, H(12), 0.22f), red.Darkened(0.1f));
                b.Ball(new Vector3(0.0f, H(25.5f), 0.03f), new Vector3(0.14f, H(6), 0.14f), red.Darkened(0.05f));
                b.Capsule(new Vector3(-0.05f, H(36), 0), new Vector3(-0.1f, H(44), 0), 0.045f, new Color("#1E88E5"));
                b.Capsule(new Vector3(0.05f, H(36), 0), new Vector3(0.13f, H(43), 0.02f), 0.05f, new Color("#D32F2F"));
                return true;
            }

            case "BrainModel":
            {
                Pedestal(b);
                var pink = new Color("#F0A8B8");
                b.Ball(new Vector3(-0.09f, H(30), 0), new Vector3(0.2f, H(13), 0.34f), pink);
                b.Ball(new Vector3(0.09f, H(30), 0), new Vector3(0.2f, H(13), 0.34f), pink.Darkened(0.05f));
                b.Ball(new Vector3(0, H(25.5f), -0.12f), new Vector3(0.18f, H(5), 0.12f), pink.Darkened(0.15f));
                for (var i = 0; i < 3; i++)
                {
                    b.Ring(new Vector3(0, H(29 + (i * 1.5f)), -0.08f + (i * 0.08f)), 0.12f, pink.Darkened(0.2f), new Basis(Vector3.Forward, Mathf.Pi / 2), 0.3f);
                }

                return true;
            }

            case "GutModel":
                Pedestal(b);
                Cube(b, 0.32, 0.12, 18, new Color("#F5D3BC"), 22, -0.05);
                b.Capsule(new Vector3(-0.08f, H(26), 0.03f), new Vector3(0.08f, H(26), 0.03f), 0.035f, new Color("#E57373"));
                b.Capsule(new Vector3(0.08f, H(26), 0.03f), new Vector3(0.08f, H(32), 0.03f), 0.035f, new Color("#E57373"));
                b.Capsule(new Vector3(0.08f, H(32), 0.03f), new Vector3(-0.08f, H(32), 0.03f), 0.035f, new Color("#E57373"));
                b.Capsule(new Vector3(-0.08f, H(32), 0.03f), new Vector3(-0.08f, H(37), 0.03f), 0.035f, new Color("#E57373"));
                b.Ball(new Vector3(-0.05f, H(38), 0.04f), new Vector3(0.12f, H(4), 0.08f), new Color("#A1887F"));
                return true;

            case "PelvisModel":
            {
                Pedestal(b);
                var bone = new Color("#ECE7DA");
                b.Ring(new Vector3(0, H(28), 0), 0.13f, bone, new Basis(Vector3.Right, -0.5f), 1.4f);
                b.Ball(new Vector3(-0.17f, H(31), -0.02f), new Vector3(0.12f, H(9), 0.2f), bone);
                b.Ball(new Vector3(0.17f, H(31), -0.02f), new Vector3(0.12f, H(9), 0.2f), bone);
                return true;
            }

            case "PrivacyScreen":
                for (var i = 0; i < 3; i++)
                {
                    var x = -0.3f + (i * 0.3f);
                    var z = i == 1 ? -0.06f : 0.02f;
                    var yaw = i == 0 ? 0.35f : i == 2 ? -0.35f : 0;
                    b.Frame = b.Frame * new Transform3D(new Basis(Vector3.Up, yaw), new Vector3(x, 0, z));
                    b.Box(new Vector3(0, H(19), 0), new Vector3(0.27f, H(30), 0.02f), new Color("#F3E5F5"));
                    b.Box(new Vector3(0, H(34.5f), 0), new Vector3(0.29f, H(2.5f), 0.04f), new Color("#9C8AD0"));
                    b.Rod(new Vector3(-0.14f, 0, 0), new Vector3(-0.14f, H(36), 0), 0.012f, Steel, small: true);
                    b.Rod(new Vector3(0.14f, 0, 0), new Vector3(0.14f, H(36), 0), 0.012f, Steel, small: true);
                    b.Frame = b.Frame * new Transform3D(new Basis(Vector3.Up, yaw), new Vector3(x, 0, z)).AffineInverse();
                }

                return true;

            case "Kiosk":
                Cube(b, 0.45, 0.35, 26, new Color("#E7EBEE"));
                b.Box(new Vector3(0, H(33), 0.04f), new Vector3(0.42f, H(15), 0.08f), new Basis(Vector3.Right, -0.3f), Dark);
                b.Box(new Vector3(0, H(33.2f), 0.085f), new Vector3(0.36f, H(12), 0.01f), new Basis(Vector3.Right, -0.3f), new Color("#1F5FBF"));
                b.Box(new Vector3(0, H(32.5f), 0.095f), new Vector3(0.24f, H(2.5f), 0.01f), new Basis(Vector3.Right, -0.3f), White);
                return true;

            case "FloorLamp":
                b.Cylinder(new Vector3(0, H(0.8f), 0), 0.13f, H(1.6f), 0.13f, new Color("#56616B"));
                b.Rod(new Vector3(0, 0, 0), new Vector3(0, H(43), 0), 0.02f, new Color("#56616B"), small: true);
                b.Cylinder(new Vector3(0, H(46), 0), 0.15f, H(8), 0.15f, new Color("#FFE0A0"), cone: false);
                b.Cylinder(new Vector3(0, H(50.2f), 0), 0.11f, H(0.4f), 0.11f, new Color("#FFF3D0"));
                return true;

            case "MannequinHead":
            {
                Pedestal(b);
                var skin = new Color("#E9DFD4");
                b.Cylinder(new Vector3(0, H(24), 0), 0.07f, H(4), 0.07f, skin.Darkened(0.05f));
                b.Ball(new Vector3(0, H(31), 0), new Vector3(0.3f, H(14), 0.32f), skin);
                b.Ball(new Vector3(0, H(33.5f), -0.02f), new Vector3(0.32f, H(9), 0.33f), new Color("#F5F5F5"));
                foreach (var (x, y, z) in new[] { (-0.12f, 33f, 0.06f), (-0.05f, 37f, 0.05f), (0.05f, 37f, 0.05f), (0.12f, 33f, 0.06f), (0f, 35f, 0.15f) })
                {
                    b.Ball(new Vector3(x, H(y), z), Vector3.One * 0.04f, new Color("#E53935"), small: true);
                }

                b.Capsule(new Vector3(0.14f, H(30), 0.02f), new Vector3(0.24f, H(14), 0.1f), 0.01f, new Color("#455A64"));
                return true;
            }

            case "ScopeCabinet":
                Cube(b, 0.8, 0.4, 46, new Color("#E9EDF0"));
                for (var i = 0; i < 3; i++)
                {
                    var x = -0.2f + (i * 0.2f);
                    b.Rod(new Vector3(x, H(10), 0.12f), new Vector3(x, H(41), 0.12f), 0.018f, new Color("#263238"), small: true);
                    b.Ball(new Vector3(x, H(42), 0.12f), new Vector3(0.07f, 0.07f, 0.07f), new Color("#455A64"), small: true);
                }

                glass.Box(new Vector3(0, H(24), 0.205f), new Vector3(0.74f, H(40), 0.01f), new Color("#BFE0F0", 0.45f));
                return true;

            case "BlanketStack":
                Cube(b, 0.7, 0.5, 14, new Color("#D9CBB5"));
                Cube(b, 0.6, 0.45, 4, new Color("#BFD3E6"), 14);
                Cube(b, 0.58, 0.44, 4, new Color("#F2B8C6"), 18);
                Cube(b, 0.6, 0.45, 4, new Color("#BFD3E6"), 22);
                return true;

            case "ScrubSink":
                Cube(b, 0.9, 0.5, 24, new Color("#DDE5EA"));
                Cube(b, 0.7, 0.32, 1, new Color("#A9BBC8"), 23.6f, 0.02);
                foreach (var x in new[] { -0.2f, 0.2f })
                {
                    b.Rod(new Vector3(x, H(24), -0.18f), new Vector3(x, H(34), -0.18f), 0.02f, Steel, small: true);
                    b.Rod(new Vector3(x, H(34), -0.18f), new Vector3(x, H(33), 0f), 0.018f, Steel, small: true);
                }

                return true;

            case "PlasterShelf":
                Cube(b, 0.9, 0.4, 32, new Color("#AAB3BA"), 0, -0.02);
                for (var i = 0; i < 3; i++)
                {
                    Cube(b, 0.88, 0.36, 1, new Color("#C9CFD4"), 3 + (i * 10f));
                    for (var k = 0; k < 3; k++)
                    {
                        b.Cylinder(new Vector3(-0.27f + (k * 0.27f), H(7 + (i * 10f)), 0.06f), 0.09f, H(6), 0.09f, i == 1 ? new Color("#90CAF9") : White, new Basis(Vector3.Right, Mathf.Pi / 2));
                    }
                }

                return true;

            case "Bucket":
                b.Cylinder(new Vector3(0, H(5.5f), 0), 0.17f, H(11), 0.17f, new Color("#90A4AE"));
                b.Cylinder(new Vector3(0, H(11.05f), 0), 0.15f, H(0.2f), 0.15f, new Color("#D6E4EC"));
                b.Ring(new Vector3(0, H(13), 0), 0.17f, new Color("#607D8B"), new Basis(Vector3.Right, Mathf.Pi / 2), 0.4f);
                return true;

            case "GymBall":
                b.Ball(new Vector3(0, 0.27f, 0), Vector3.One * 0.54f, new Color("#1E88E5"));
                return true;

            case "Dumbbells":
            {
                Cube(b, 0.9, 0.4, 8, new Color("#56616B"));
                var colours = new[] { "#E53935", "#1F5FBF", "#F9A825" };
                for (var i = 0; i < 3; i++)
                {
                    var x = -0.27f + (i * 0.27f);
                    b.Rod(new Vector3(x, H(10), -0.12f), new Vector3(x, H(10), 0.12f), 0.015f, new Color("#B9C3CB"), small: true);
                    b.Cylinder(new Vector3(x, H(10), -0.11f), 0.06f, 0.05f, 0.06f, new Color(colours[i]), new Basis(Vector3.Right, Mathf.Pi / 2));
                    b.Cylinder(new Vector3(x, H(10), 0.11f), 0.06f, 0.05f, 0.06f, new Color(colours[i]), new Basis(Vector3.Right, Mathf.Pi / 2));
                }

                return true;
            }

            case "Bins":
                foreach (var (x, lid, badge) in new[] { (-0.17f, "#2B3036", "#9AA6B0"), (0.17f, "#2E7D32", "#FFFFFF") })
                {
                    b.Cylinder(new Vector3(x, H(6.5f), 0), 0.14f, H(13), 0.14f, new Color("#E4E9ED"));
                    b.Cylinder(new Vector3(x, H(13.6f), 0), 0.15f, H(1.4f), 0.15f, new Color(lid));
                    b.Box(new Vector3(x, H(7.3f), 0.135f), new Vector3(0.1f, H(4.4f), 0.02f), new Color(lid));
                    b.Box(new Vector3(x, H(0.6f), 0.15f), new Vector3(0.1f, H(1.2f), 0.06f), new Color("#55616B"));
                    b.Ball(new Vector3(x, H(7.3f), 0.15f), new Vector3(0.04f, 0.04f, 0.01f), new Color(badge), small: true);
                }

                return true;

            case "OxygenSpare":
                foreach (var x in new[] { -0.12f, 0.12f })
                {
                    b.Cylinder(new Vector3(x, H(13), 0), 0.1f, H(26), 0.1f, new Color("#F2F4F5"));
                    b.Ball(new Vector3(x, H(26), 0), new Vector3(0.2f, H(5), 0.2f), White);
                    b.Cylinder(new Vector3(x, H(30.5f), 0), 0.04f, H(3), 0.04f, new Color("#2C2F33"));
                }

                return true;

            default:
                return false;
        }
    }

    private static void Skeleton(MeshBatch b)
    {
        var bone = new Color("#EFEADF");
        var joint = new Color("#D8D0BE");
        var stand = new Color("#6E7A84");
        b.Cylinder(new Vector3(0, H(0.6f), -0.05f), 0.2f, H(1.2f), 0.2f, stand);
        b.Rod(new Vector3(0, 0, -0.08f), new Vector3(0, H(60), -0.08f), 0.015f, stand, small: true);
        foreach (var s in new[] { -1f, 1f })
        {
            b.Capsule(new Vector3(s * 0.07f, H(26), 0), new Vector3(s * 0.08f, H(14), 0), 0.03f, bone);
            b.Capsule(new Vector3(s * 0.08f, H(14), 0), new Vector3(s * 0.08f, H(3), 0), 0.025f, bone);
            b.Ball(new Vector3(s * 0.08f, H(14), 0), Vector3.One * 0.06f, joint, small: true);
            b.Capsule(new Vector3(s * 0.08f, H(2), 0), new Vector3(s * 0.1f, H(1.5f), 0.08f), 0.02f, bone);
            b.Capsule(new Vector3(s * 0.15f, H(44), 0), new Vector3(s * 0.19f, H(34), 0.02f), 0.022f, bone);
            b.Capsule(new Vector3(s * 0.19f, H(34), 0.02f), new Vector3(s * 0.2f, H(25), 0.05f), 0.02f, bone);
            b.Ball(new Vector3(s * 0.19f, H(34), 0.02f), Vector3.One * 0.05f, joint, small: true);
        }

        b.Ball(new Vector3(0, H(28), 0), new Vector3(0.28f, H(5), 0.14f), bone);
        b.Rod(new Vector3(0, H(29), -0.02f), new Vector3(0, H(46), -0.02f), 0.022f, bone, small: true);
        for (var i = 0; i < 4; i++)
        {
            var w = 0.12f - (i * 0.012f);
            b.Ring(new Vector3(0, H(43 - (i * 3)), 0.0f), w, bone, null, 0.5f);
        }

        b.Capsule(new Vector3(-0.15f, H(45), 0), new Vector3(0.15f, H(45), 0), 0.02f, bone);
        b.Ball(new Vector3(0, H(51.5f), 0.01f), new Vector3(0.22f, H(9), 0.24f), bone);
        b.Ball(new Vector3(0, H(48), 0.04f), new Vector3(0.14f, H(4), 0.14f), bone);
        b.Ball(new Vector3(-0.045f, H(52), 0.11f), new Vector3(0.05f, 0.05f, 0.02f), new Color("#3A3A3A"), small: true);
        b.Ball(new Vector3(0.045f, H(52), 0.11f), new Vector3(0.05f, 0.05f, 0.02f), new Color("#3A3A3A"), small: true);
    }

    /// <summary>Builds a small thing on a desk top (origin on the top); false for kinds the 3D desks already show.</summary>
    public static bool Top(MeshBatch b, string kind)
    {
        switch (kind)
        {
            case "Phone":
                b.Box(new Vector3(0, H(1.2f), 0), new Vector3(0.16f, H(2.4f), 0.12f), Dark);
                b.Capsule(new Vector3(-0.07f, H(3), -0.02f), new Vector3(0.07f, H(3), -0.02f), 0.025f, new Color("#3A4148"));
                return true;

            case "Folders":
            {
                var colours = new[] { "#E53935", "#1F5FBF", "#F9A825" };
                for (var i = 0; i < 3; i++)
                {
                    b.Box(new Vector3(0.01f * i, H(0.5f + (i * 1.1f)), 0.01f * i), new Vector3(0.3f, H(1), 0.22f), new Basis(Vector3.Up, 0.1f * (i - 1)), new Color(colours[i]));
                }

                return true;
            }

            case "KettleMugs":
                b.Cylinder(new Vector3(-0.15f, H(4.5f), 0), 0.08f, H(9), 0.08f, new Color("#ECEFF1"));
                b.Cylinder(new Vector3(-0.15f, H(9.5f), 0), 0.07f, H(1.2f), 0.07f, Dark);
                b.Capsule(new Vector3(-0.07f, H(5), 0), new Vector3(-0.05f, H(7), 0), 0.015f, new Color("#ECEFF1"));
                b.Cylinder(new Vector3(0.05f, H(2.5f), 0.02f), 0.04f, H(5), 0.04f, new Color("#E53935"));
                b.Cylinder(new Vector3(0.15f, H(2.5f), -0.02f), 0.04f, H(5), 0.04f, new Color("#1F5FBF"));
                return true;

            case "ReflexHammer":
                b.Rod(new Vector3(-0.12f, H(0.6f), 0.02f), new Vector3(0.06f, H(0.6f), -0.02f), 0.008f, Steel, small: true);
                b.Box(new Vector3(0.08f, H(1.2f), -0.02f), new Vector3(0.05f, H(2.4f), 0.1f), new Color("#E53935"));
                return true;

            case "PillTray":
                b.Box(new Vector3(-0.04f, H(0.6f), 0), new Vector3(0.24f, H(1.2f), 0.16f), new Color("#ECEFF1"));
                foreach (var x in new[] { -0.1f, -0.04f, 0.02f })
                {
                    b.Ball(new Vector3(x, H(1.5f), 0), Vector3.One * 0.03f, new Color("#FFCC80"), small: true);
                }

                b.Cylinder(new Vector3(0.14f, H(3.5f), 0), 0.04f, H(7), 0.04f, Colors.White);
                b.Cylinder(new Vector3(0.14f, H(7.3f), 0), 0.042f, H(1.2f), 0.042f, new Color("#E53935"));
                return true;

            case "GelWarmer":
                b.Cylinder(new Vector3(0, H(4.5f), 0), 0.05f, H(9), 0.05f, new Color("#81D4FA"));
                b.Cylinder(new Vector3(0, H(10), 0), 0.02f, H(2), 0.02f, Colors.White);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Two theatre lamps on a ceiling arm over the table; origin on the floor below.</summary>
    public static void SurgicalLights(MeshBatch b, MeshBatch glow)
    {
        b.Model(Furnish3D.Equipment3D, "surgical_lights", Transform3D.Identity);
        glow.Model(Furnish3D.Equipment3D, "surgical_glow", Transform3D.Identity);
    }

    /// <summary>The cath lab's bank of four screens on a ceiling arm, facing +z.</summary>
    public static void MonitorBank(MeshBatch b, MeshBatch glow)
    {
        b.Rod(new Vector3(0, H(118), 0), new Vector3(0, H(92), 0), 0.04f, Steel);
        b.Box(new Vector3(0, H(90), -0.05f), new Vector3(0.9f, H(4), 0.06f), Steel);
        for (var i = 0; i < 4; i++)
        {
            var x = -0.23f + ((i % 2) * 0.46f);
            var y = H(83 - ((i / 2) * 12f));
            b.Box(new Vector3(x, y, 0), new Vector3(0.44f, H(11), 0.05f), new Color("#1B2228"));
            glow.Box(new Vector3(x, y, 0.027f), new Vector3(0.4f, H(9.4f), 0.005f), new Color(i == 0 ? "#26323C" : "#0E1A22"));
            if (i == 0)
            {
                glow.Ball(new Vector3(x, y, 0.032f), new Vector3(0.12f, 0.12f, 0.004f), new Color("#9FB0BE"), small: true);
            }
            else
            {
                glow.Box(new Vector3(x, y, 0.032f), new Vector3(0.32f, 0.012f, 0.002f), new Color(i == 1 ? "#3EE07A" : "#4FC3F7"));
            }
        }
    }

    /// <summary>A wet floor: a yellow puddle and an A-frame warning sign beside it.</summary>
    public static void Puddle(MeshBatch b, MeshBatch wet, double cx, double cy, int seed)
    {
        void Blob(double x, double y, double radius, int s, Color colour, float lift)
        {
            const int n = 20;
            var centre = Kit3D.At(x, y, lift);
            for (var i = 0; i < n; i++)
            {
                Vector3 P(int k)
                {
                    var a = k * Math.PI * 2 / n;
                    var r = radius * (0.82 + (0.12 * Math.Sin(s + (a * 2))) + (0.06 * Math.Sin((s * 0.7) + (a * 3))));
                    return Kit3D.At(x + (Math.Cos(a) * r), y + (Math.Sin(a) * r * 0.8), lift);
                }

                wet.Tri(centre, P(i + 1), P(i), colour);
            }
        }

        var puddle = new Color(0.95f, 0.88f, 0.45f, 0.45f);
        Blob(cx, cy, 0.7, seed, puddle, 0.035f);
        Blob(cx + 0.35, cy - 0.25, 0.35, seed + 3, puddle, 0.037f);
        Blob(cx - 0.15, cy - 0.1, 0.18, seed + 7, new Color(1, 1, 1, 0.45f), 0.039f);

        // The A-frame sign: two yellow boards leaning together.
        var yellow = new Color("#F9C80E");
        var h = Kit3D.M(0.6);
        var at = Kit3D.At(cx + 0.9, cy + 0.6);
        foreach (var s in new[] { -1f, 1f })
        {
            b.Box(at + new Vector3(0, h / 2, s * 0.09f), new Vector3(0.36f, h, 0.02f), new Basis(Vector3.Right, s * 0.2f), yellow);
            b.Box(at + new Vector3(0, h * 0.55f, s * 0.1f), new Vector3(0.18f, 0.16f, 0.025f), new Basis(Vector3.Right, s * 0.2f), Dark);
        }
    }
}
