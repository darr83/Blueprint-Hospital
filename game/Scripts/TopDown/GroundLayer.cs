using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.TopDown;

/// <summary>
/// The grounds from above: grass, paving, roads with kerbs and centre lines, the marked car park,
/// the taxi rank, the ambulance bays, trees and bushes, and the building's floor slab. Drawn once
/// and kept until the site changes. On an upper floor the grounds fade under a pale wash and the
/// slab is that storey's outline.
/// </summary>
public partial class GroundLayer : Node2D
{
    private static Color C(string hex) => new(hex);

    public static readonly Color Grass = C("#86BC6B");
    private static readonly Color GrassDark = C("#7AB260");
    private static readonly Color GrassLight = C("#93C678");
    private static readonly Color Paving = C("#D8D3C6");
    private static readonly Color PavingJoint = C("#C4BEAF");
    private static readonly Color Asphalt = C("#555C64");
    private static readonly Color ParkingTar = C("#626971");
    private static readonly Color Kerb = C("#C9CED3");
    private static readonly Color Marking = C("#F4F4EE");
    private static readonly Color Yellow = C("#F5C518");
    private static readonly Color BayTar = C("#7A4A45");
    private static readonly Color Slab = C("#EDE9DF");

    private HospitalSimulation _sim = null!;

    /// <summary>Texture on the grass, paving and slab (the "Surface textures" setting).</summary>
    public bool Detailed { get; set; } = true;

    /// <summary>Shadows under trees and bushes.</summary>
    public bool Shadows { get; set; } = true;

    /// <summary>How many trees and bushes were drawn last time, for checks.</summary>
    public int PlantCount { get; private set; }

    /// <summary>The words painted on the ground (AMBULANCE, TAXI), for checks.</summary>
    public List<string> Lettering { get; } = new();

    public void Initialise(HospitalSimulation simulation) => _sim = simulation;

    private static float Hash(int x, int y, int salt = 0)
    {
        unchecked
        {
            var h = (uint)((x * 73856093) ^ (y * 19349663) ^ (salt * 83492791));
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
        }
    }

    public override void _Draw()
    {
        if (_sim is null)
        {
            return;
        }

        var site = _sim.Site;
        var p = new Pen(this, Shadows);
        Lettering.Clear();
        PlantCount = 0;
        TileKind At(int x, int y) => x >= 0 && y >= 0 && x < site.Width && y < site.Length ? site[new GridCoord(x, y)] : TileKind.Grass;

        // Grass, well past the edge of the site so the screen never shows bare background.
        const int margin = 60;
        p.Rect(-margin, -margin, site.Width + (2 * margin), site.Length + (2 * margin), Grass);
        if (Detailed)
        {
            for (var i = 0; i < 700; i++)
            {
                var x = (Hash(i, 1, 5) * (site.Width + 40)) - 20;
                var y = (Hash(i, 2, 5) * (site.Length + 40)) - 20;
                var r = 1.5 + (Hash(i, 3, 5) * 4);
                p.Ellipse(x, y, r, r * (0.5 + (Hash(i, 4, 5) * 0.4)), new Color(i % 2 == 0 ? GrassDark : GrassLight, 0.5f), null, 0, Hash(i, 5, 5) * 3);
            }
        }

        // Everything laid on the ground, a row of tiles at a time.
        Runs(site, At, k => k is TileKind.Paving, (x, y, n) => p.Rect(x - 0.01, y - 0.01, n + 0.02, 1.02, Paving));
        Runs(site, At, k => k is TileKind.Road, (x, y, n) => p.Rect(x - 0.01, y - 0.01, n + 0.02, 1.02, Asphalt));
        Runs(site, At, k => k is TileKind.Parking, (x, y, n) => p.Rect(x - 0.01, y - 0.01, n + 0.02, 1.02, ParkingTar));
        Runs(site, At, k => k is TileKind.TaxiRank, (x, y, n) => p.Rect(x - 0.01, y - 0.01, n + 0.02, 1.02, C("#5B626A")));
        Runs(site, At, k => k is TileKind.AmbulanceBay, (x, y, n) => p.Rect(x - 0.01, y - 0.01, n + 0.02, 1.02, BayTar));

        if (Detailed)
        {
            // Paving slabs: two tiles square, every other course staggered.
            Runs(site, At, k => k is TileKind.Paving, (x, y, n) =>
            {
                if (y % 2 == 0)
                {
                    p.Line(x, y, x + n, y, PavingJoint, 0.05);
                }

                var stagger = (y / 2) % 2;
                for (var jx = x; jx < x + n; jx++)
                {
                    if ((jx + stagger) % 2 == 0)
                    {
                        p.Line(jx, y, jx, y + 1, PavingJoint, 0.05);
                    }
                }
            });

            // Blades of grass, only where there is grass.
            for (var i = 0; i < 2600; i++)
            {
                var x = Hash(i, 11, 3) * site.Width;
                var y = Hash(i, 12, 3) * site.Length;
                if (At((int)x, (int)y) == TileKind.Grass)
                {
                    p.Line(x, y, x + 0.06, y - 0.18, new Color(i % 2 == 0 ? Pen.Dark(Grass, 0.18f) : Pen.Light(Grass, 0.25f), 0.7f), 0.045);
                }
            }
        }

        Edges(site, At, p);
        if (site.Layout is { } layout)
        {
            RoadLines(p, layout);
            CarPark(p, site, layout, At);
            if (layout.TaxiRank is { } rank && At(rank.X, rank.Y) == TileKind.TaxiRank)
            {
                p.Round(rank.X + 0.2, rank.Y + 0.2, rank.Width - 0.4, rank.Length - 0.4, 0.1, null, Yellow, 0.16);
                var along = rank.Width >= rank.Length;
                p.Text("TAXI", rank.X + (rank.Width / 2.0), rank.Y + (rank.Length / 2.0), 40, new Color(Yellow, 0.9f), rot: along ? 0 : Math.PI / 2, spacing: 8);
                Lettering.Add("TAXI");
            }
        }

        AmbulanceBays(p);

        // The building's floor slab.
        foreach (var area in site.Buildings.SelectMany(b => b.Areas))
        {
            p.Rect(area.X, area.Y, area.Width, area.Length, Slab);
            if (!Detailed)
            {
                continue;
            }

            for (var y = area.Y; y < area.Bottom; y += 2)
            {
                for (var x = area.X; x < area.Right; x += 2)
                {
                    if (((x + y) / 2) % 2 == 0)
                    {
                        p.Rect(x, y, Math.Min(2, area.Right - x), Math.Min(2, area.Bottom - y), new Color(1, 1, 1, 0.32f));
                    }
                }
            }
        }

        // Entrance mats: blue at the front doors, red at the ambulance doors.
        foreach (var entrance in site.Buildings.SelectMany(b => b.Entrances))
        {
            var colour = entrance.IsAmbulanceEntrance ? C("#E53935") : C("#005EB8");
            var (x, y, w, l) = entrance.Side is WallSide.North or WallSide.South
                ? (entrance.Tile.X, entrance.Tile.Y, entrance.WidthTiles, 1)
                : (entrance.Tile.X, entrance.Tile.Y, 1, entrance.WidthTiles);
            p.Round(x + 0.15, y + 0.15, w - 0.3, l - 0.3, 0.1, colour.Lerp(Slab, 0.45f), colour.Lerp(Slab, 0.2f), 0.04);
        }

        Planting(p, site, At);

        if (IsoDraw.ViewFloor > 0)
        {
            // Upstairs: the grounds below fade, and the floor slab is the building's outline.
            p.Rect(-margin, -margin, site.Width + (2 * margin), site.Length + (2 * margin), SiteRenderer.UpperFloorGround);
            foreach (var r in site.Buildings.SelectMany(b => b.Areas))
            {
                p.Rect(r.X, r.Y, r.Width, r.Length, SiteRenderer.UpperFloorSlab);
            }
        }
    }

    private static void Runs(SiteMap site, Func<int, int, TileKind> at, Func<TileKind, bool> wanted, Action<int, int, int> run)
    {
        for (var y = 0; y < site.Length; y++)
        {
            var start = -1;
            for (var x = 0; x <= site.Width; x++)
            {
                var inside = x < site.Width && wanted(at(x, y));
                if (inside && start < 0)
                {
                    start = x;
                }
                else if (!inside && start >= 0)
                {
                    run(start, y, x - start);
                    start = -1;
                }
            }
        }
    }

    private static bool Hard(TileKind kind) => kind is TileKind.Road or TileKind.Parking or TileKind.TaxiRank or TileKind.AmbulanceBay;

    /// <summary>Kerbs where tarmac meets grass or paving, and a darker edge where paving meets grass.</summary>
    private static void Edges(SiteMap site, Func<int, int, TileKind> at, Pen p)
    {
        void Pass(Func<TileKind, TileKind, bool> boundary, Color colour, double width)
        {
            // Horizontal edges (between rows), merged into runs.
            for (var y = 0; y <= site.Length; y++)
            {
                var start = -1;
                for (var x = 0; x <= site.Width; x++)
                {
                    var edge = x < site.Width && (boundary(at(x, y - 1), at(x, y)) || boundary(at(x, y), at(x, y - 1)));
                    if (edge && start < 0)
                    {
                        start = x;
                    }
                    else if (!edge && start >= 0)
                    {
                        p.Line(start, y, x, y, colour, width);
                        start = -1;
                    }
                }
            }

            for (var x = 0; x <= site.Width; x++)
            {
                var start = -1;
                for (var y = 0; y <= site.Length; y++)
                {
                    var edge = y < site.Length && (boundary(at(x - 1, y), at(x, y)) || boundary(at(x, y), at(x - 1, y)));
                    if (edge && start < 0)
                    {
                        start = y;
                    }
                    else if (!edge && start >= 0)
                    {
                        p.Line(x, start, x, y, colour, width);
                        start = -1;
                    }
                }
            }
        }

        Pass((a, b) => a == TileKind.Paving && b == TileKind.Grass, C("#B5AE9C"), 0.09);
        Pass((a, b) => Hard(a) && b is TileKind.Grass or TileKind.Paving, Kerb, 0.22);
    }

    private static void RoadLines(Pen p, SiteLayout layout)
    {
        foreach (var road in layout.Roads)
        {
            var along = road.Width >= road.Length;
            var others = layout.Roads.Where(r => r != road && r.Intersects(road)).ToList();
            var (from, to) = along ? (road.X, road.Right) : (road.Y, road.Bottom);
            var mid = along ? road.Y + (road.Length / 2.0) : road.X + (road.Width / 2.0);
            for (var at = from + 1.0; at + 2.2 <= to; at += 4.4)
            {
                // No centre line across a junction.
                var (a, b) = (at, at + 2.2);
                if (others.Any(o => along ? b > o.X - 1 && a < o.Right + 1 : b > o.Y - 1 && a < o.Bottom + 1))
                {
                    continue;
                }

                if (along)
                {
                    p.Line(a, mid, b, mid, C("#F2F2EC"), 0.2);
                }
                else
                {
                    p.Line(mid, a, mid, b, C("#F2F2EC"), 0.2);
                }
            }
        }
    }

    private static void CarPark(Pen p, SiteMap site, SiteLayout layout, Func<int, int, TileKind> at)
    {
        for (var i = 0; i < layout.ParkingBays.Count; i++)
        {
            var bay = layout.ParkingBays[i];
            if (at(bay.X, bay.Y) != TileKind.Parking)
            {
                continue;
            }

            var upright = bay.Length >= bay.Width;
            if (upright)
            {
                p.Line(bay.X, bay.Y, bay.X, bay.Bottom, Marking, 0.14);
                p.Line(bay.Right, bay.Y, bay.Right, bay.Bottom, Marking, 0.14);
            }
            else
            {
                p.Line(bay.X, bay.Y, bay.Right, bay.Y, Marking, 0.14);
                p.Line(bay.X, bay.Bottom, bay.Right, bay.Bottom, Marking, 0.14);
            }

            if (i < layout.DisabledBays)
            {
                var (cx, cy) = (bay.X + (bay.Width / 2.0), bay.Y + (bay.Length / 2.0));
                p.Round(cx - 1.4, cy - 1.4, 2.8, 2.8, 0.3, C("#2F6FD0"));

                // A wheelchair user, simply: a head, a seat and a wheel.
                p.Circle(cx - 0.1, cy - 0.75, 0.26, Colors.White);
                p.Path(Colors.White, 0.2, (cx - 0.1, cy - 0.4), (cx - 0.1, cy + 0.25), (cx + 0.55, cy + 0.25), (cx + 0.8, cy + 0.85));
                p.Arc(cx - 0.05, cy + 0.4, 0.62, 0.6, 3.6, Colors.White, 0.16);
            }
        }
    }

    private void AmbulanceBays(Pen p)
    {
        var side = _sim.AmbulanceBaySide;
        foreach (var bay in _sim.AmbulanceBays)
        {
            p.Round(bay.X + 0.25, bay.Y + 0.25, bay.Width - 0.5, bay.Length - 0.5, 0.1, null, Yellow, 0.22);
            var rot = side switch
            {
                WallSide.East => 0,
                WallSide.West => 0,
                _ => Math.PI / 2,
            };
            p.Text("AMBULANCE", bay.X + (bay.Width / 2.0), bay.Y + (bay.Length / 2.0), 34, new Color(Yellow, 0.85f), rot: rot, spacing: 3);
            if (!Lettering.Contains("AMBULANCE"))
            {
                Lettering.Add("AMBULANCE");
            }
        }
    }

    // ------------------------------------------------------------------ planting

    private void Planting(Pen p, SiteMap site, Func<int, int, TileKind> at)
    {
        bool Open(int x, int y, int r)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    if (x + dx < 0 || y + dy < 0 || x + dx >= site.Width || y + dy >= site.Length || at(x + dx, y + dy) != TileKind.Grass)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        bool NearBuilding(int x, int y, int within) =>
            site.Buildings.SelectMany(b => b.Areas).Any(r => x >= r.X - within && x < r.Right + within && y >= r.Y - within && y < r.Bottom + within);

        // Trees and bushes on a loose grid, jittered, kept clear of everything that isn't grass.
        const int cell = 9;
        for (var gy = 0; gy < site.Length / cell; gy++)
        {
            for (var gx = 0; gx < site.Width / cell; gx++)
            {
                var pick = Hash(gx, gy, 1);
                if (pick < 0.3f)
                {
                    continue;
                }

                var x = (gx * cell) + 2 + (int)(Hash(gx, gy, 2) * (cell - 4));
                var y = (gy * cell) + 2 + (int)(Hash(gx, gy, 3) * (cell - 4));
                if (!Open(x, y, 3) || NearBuilding(x, y, 6))
                {
                    continue;
                }

                PlantCount++;
                if (pick < 0.5f)
                {
                    Bush(p, x + 0.5, y + 0.5, gx + (gy * 31));
                }
                else
                {
                    Tree(p, x + 0.5, y + 0.5, 0.85 + (Hash(gx, gy, 7) * 0.45), gx + (gy * 31));
                }
            }
        }

        // Low hedges along the building's walls, where there is grass beside them.
        foreach (var area in site.Buildings.SelectMany(b => b.Areas))
        {
            for (var x = area.X + 1; x + 4 < area.Right; x += 6)
            {
                foreach (var y in new[] { area.Y - 2, area.Bottom + 1 })
                {
                    if (Enumerable.Range(x, 4).All(tx => at(tx, y) == TileKind.Grass && at(tx, y + (y < area.Y ? -1 : 1)) == TileKind.Grass))
                    {
                        Hedge(p, x, y - 0.2, 4, 1.3, x + y);
                        PlantCount++;
                    }
                }
            }
        }
    }

    private static void Tree(Pen p, double x, double y, double s, int seed)
    {
        p.Shadow(c => p.Circle(x, y, 2.3 * s, c), 0.5, 0.7, 1.3f);
        var cols = new[] { C("#3F8F45"), C("#4FA356"), C("#64B768"), C("#7CC67C") };
        for (var ring = 0; ring < 4; ring++)
        {
            for (var i = 0; i < 7 - ring; i++)
            {
                var a = Hash(seed, (ring * 10) + i, 21) * Mathf.Tau;
                var r = (2.0 - (ring * 0.55)) * s * (0.5 + (Hash(seed, (ring * 10) + i, 22) * 0.5));
                p.Circle(x + (Math.Cos(a) * r * 0.6) - (ring * 0.12 * s), y + (Math.Sin(a) * r * 0.6) - (ring * 0.16 * s), (1.25 - (ring * 0.2)) * s, cols[ring]);
            }
        }
    }

    private static void Bush(Pen p, double x, double y, int seed)
    {
        var leaf = C("#4C9A52");
        p.Shadow(c => p.Circle(x, y, 1.0, c), 0.14, 0.2);
        p.Dot(x, y, 0.95, leaf, 0.04);
        for (var i = 0; i < 6; i++)
        {
            var a = Hash(seed, i, 31) * Mathf.Tau;
            p.Circle(x + (Math.Cos(a) * 0.45), y + (Math.Sin(a) * 0.45), 0.3 + (Hash(seed, i, 32) * 0.2), i % 2 == 0 ? C("#5DAE62") : C("#3F8B46"));
        }

        if (seed % 3 == 0)
        {
            var flower = new[] { C("#F2C230"), C("#E88AB0"), C("#FFFFFF") }[seed % 3];
            foreach (var (dx, dy) in new[] { (0.2, -0.3), (-0.4, 0.2), (0.5, 0.4) })
            {
                p.Circle(x + dx, y + dy, 0.11, flower);
            }
        }
    }

    private static void Hedge(Pen p, double x, double y, double w, double l, int seed)
    {
        var leaf = C("#4C9A52");
        p.Shadow(c => p.Round(x, y, w, l, 0.55, c), 0.14, 0.2);
        p.Solid(x, y, w, l, 0.55, leaf, 0.04);
        for (var i = 0; i < w * l * 1.6; i++)
        {
            p.Circle(x + 0.4 + (Hash(seed, i, 41) * (w - 0.8)), y + 0.35 + (Hash(seed, i, 42) * (l - 0.7)), 0.3 + (Hash(seed, i, 43) * 0.2), i % 2 == 0 ? C("#5DAE62") : C("#3F8B46"));
        }
    }
}
