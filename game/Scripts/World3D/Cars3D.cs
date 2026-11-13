using Godot;

namespace BlueprintHospital.World3D;

/// <summary>What sort of car to build.</summary>
public enum CarKind
{
    Hatchback,
    Saloon,
    Taxi,
}

/// <summary>
/// Visitors' cars and taxis, built from a few rounded boxes in the same friendly style as the people.
/// A car is modelled about its centre with its nose along +Z (grid +y), sizes in tiles (half metres).
/// </summary>
public static class Cars3D
{
    /// <summary>Length of a car, in tiles (4.3 m).</summary>
    public const float Length = 8.6f;

    /// <summary>Width of a car, in tiles (1.8 m).</summary>
    public const float Width = 3.6f;

    private static readonly Color Glass = new("#2F4558");
    private static readonly Color Tyre = new("#1D1F22");
    private static readonly Color Hub = new("#C9CED3");
    private static readonly Color Trim = new("#2A2D31");
    private static readonly Color HeadLamp = new("#FFF6D2");
    private static readonly Color TailLamp = new("#D8322A");
    private static readonly Color FrontPlate = new("#F4F4F0");
    private static readonly Color RearPlate = new("#F2C230");

    /// <summary>The paint colours ordinary cars come in.</summary>
    public static readonly Color[] Paints =
    {
        new("#C8342F"), new("#2F5FA8"), new("#E9E9E6"), new("#8C9399"), new("#2E3338"),
        new("#3F8F62"), new("#D9A441"), new("#7A4EA3"), new("#5FA7C9"), new("#B5532B"),
    };

    public static readonly Color TaxiBlack = new("#1B1C1F");

    /// <summary>The colour of car number <paramref name="id"/>: the same car always gets the same paint.</summary>
    public static Color PaintFor(int id) => Paints[(int)((uint)(id * 2654435761u) >> 8) % Paints.Length];

    /// <summary>Adds a car to <paramref name="m"/>, using whatever <see cref="MeshBatch.Frame"/> is set to place and turn it.</summary>
    public static void Build(MeshBatch m, CarKind kind, Color paint)
    {
        var taxi = kind == CarKind.Taxi;
        var saloon = kind == CarKind.Saloon || taxi;
        var w = Width;
        var half = Length / 2;
        var sill = 0.32f;
        var waist = taxi ? 1.2f : 1.08f;
        var roof = taxi ? 2.05f : 1.78f;
        var dark = paint.Darkened(0.22f);

        // Body: a long lower shell with softened ends, and bumpers.
        m.Box(new Vector3(0, (sill + waist) / 2, 0), new Vector3(w, waist - sill, Length - 0.5f), paint);
        m.Pill(new Vector3(0, (sill + waist) / 2, half - 0.45f), new Vector3(waist - sill, w * 0.98f, 0.9f), paint, Sideways);
        m.Pill(new Vector3(0, (sill + waist) / 2, -half + 0.45f), new Vector3(waist - sill, w * 0.98f, 0.9f), paint, Sideways);
        m.Box(new Vector3(0, sill + 0.06f, half - 0.12f), new Vector3(w * 0.96f, 0.16f, 0.3f), Trim);
        m.Box(new Vector3(0, sill + 0.06f, -half + 0.12f), new Vector3(w * 0.96f, 0.16f, 0.3f), Trim);
        m.Box(new Vector3(0, sill - 0.04f, 0), new Vector3(w * 0.9f, 0.1f, Length - 1.2f), Trim);

        // Cabin: glass all round between pillars, under a painted roof.
        var cabinBack = saloon ? -half + 1.9f : -half + 0.7f;
        var cabinFront = half - 2.6f;
        var cabinLength = cabinFront - cabinBack;
        var cabinMid = (cabinFront + cabinBack) / 2;
        var cw = w * 0.9f;
        var glassH = roof - waist - 0.1f;
        m.Box(new Vector3(0, waist + (glassH / 2), cabinMid), new Vector3(cw - 0.08f, glassH, cabinLength - 0.08f), Glass);
        m.Box(new Vector3(0, roof - 0.05f, cabinMid), new Vector3(cw, 0.1f, cabinLength), paint);
        m.Pill(new Vector3(0, roof - 0.02f, cabinMid), new Vector3(cw * 0.94f, 0.16f, cabinLength * 0.94f), paint.Lightened(0.06f));
        foreach (var z in new[] { cabinBack + 0.09f, cabinMid - 0.1f, cabinFront - 0.09f })
        {
            foreach (var sx in new[] { -1f, 1f })
            {
                m.Box(new Vector3(sx * ((cw / 2) - 0.07f), waist + (glassH / 2), z), new Vector3(0.16f, glassH, 0.18f), dark);
            }
        }

        // Door lines and mirrors.
        foreach (var sx in new[] { -1f, 1f })
        {
            m.Box(new Vector3(sx * ((w / 2) + 0.005f), (sill + waist) / 2, cabinMid - 0.1f), new Vector3(0.02f, (waist - sill) * 0.8f, 0.05f), dark);
            m.Box(new Vector3(sx * ((w / 2) + 0.16f), waist + 0.08f, cabinFront - 0.3f), new Vector3(0.3f, 0.16f, 0.2f), dark);
        }

        // Wheels, with arches.
        foreach (var z in new[] { half - 1.75f, -half + 1.75f })
        {
            foreach (var sx in new[] { -1f, 1f })
            {
                var x = sx * ((w / 2) - 0.2f);
                m.Rod(new Vector3(x - (sx * 0.25f), 0.46f, z), new Vector3(x + (sx * 0.25f), 0.46f, z), 0.46f, Tyre);
                m.Rod(new Vector3(x + (sx * 0.2f), 0.46f, z), new Vector3(x + (sx * 0.27f), 0.46f, z), 0.24f, Hub);
            }
        }

        // Lights and UK number plates (white in front, yellow behind).
        foreach (var sx in new[] { -1f, 1f })
        {
            m.Box(new Vector3(sx * ((w / 2) - 0.55f), waist - 0.2f, half - 0.02f), new Vector3(0.7f, 0.2f, 0.1f), HeadLamp);
            m.Box(new Vector3(sx * ((w / 2) - 0.5f), waist - 0.2f, -half + 0.02f), new Vector3(0.6f, 0.2f, 0.1f), TailLamp);
        }

        m.Box(new Vector3(0, sill + 0.24f, half + 0.03f), new Vector3(1.1f, 0.2f, 0.04f), FrontPlate);
        m.Box(new Vector3(0, sill + 0.24f, -half - 0.03f), new Vector3(1.1f, 0.2f, 0.04f), RearPlate);

        if (taxi)
        {
            // The lit TAXI sign on the roof and a chequered band along the doors.
            m.Box(new Vector3(0, roof + 0.13f, cabinFront - 0.9f), new Vector3(1.3f, 0.26f, 0.4f), new Color("#FFC928"));
            for (var i = 0; i < 12; i++)
            {
                var z = -half + 1.3f + (i * 0.5f);
                foreach (var sx in new[] { -1f, 1f })
                {
                    m.Box(new Vector3(sx * ((w / 2) + 0.006f), waist - 0.3f + ((i % 2) * 0.14f), z), new Vector3(0.02f, 0.14f, 0.5f), new Color("#FFC928"));
                }
            }
        }
    }

    private static readonly Basis Sideways = new(Vector3.Forward, Mathf.Pi / 2);

    /// <summary>The transform that stands a car at a grid point, nose along (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
    public static Transform3D Place(double x, double y, double dx, double dy) =>
        new(new Basis(Vector3.Up, Kit3D.Yaw(dx, dy)), Kit3D.At(x, y));
}
