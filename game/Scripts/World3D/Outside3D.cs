using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Core;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>How the outside of the building is finished.</summary>
public enum ExteriorStyle
{
    /// <summary>Red brick with a stone band and white window frames.</summary>
    Brick,

    /// <summary>White render with a blue band and grey window frames.</summary>
    Modern,
}

/// <summary>
/// Everything that makes the grounds look like a hospital's: the building's outside walls and windows,
/// the entrance canopies with the hospital's name and the Accident and Emergency sign, road markings and
/// kerbs, the marked car park, the taxi rank and its shelter, the sign by the road, street lamps, trees,
/// hedges, benches and planters. Looks only: the simulation knows nothing of any of it.
/// </summary>
public partial class Outside3D : Node3D
{
    /// <summary>Darren chose the modern white and blue finish (2026-11-13).</summary>
    public static ExteriorStyle Style { get; set; } = ExteriorStyle.Modern;

    private static readonly Color HospitalBlue = new("#005EB8");
    private static readonly Color EmergencyRed = new("#D5281B");
    private static readonly Color White = new("#F7F7F4");
    private static readonly Color Line = new("#ECECE6");
    private static readonly Color Yellow = new("#F2C230");
    private static readonly Color Kerb = new("#C9C6BD");
    private static readonly Color Steel = new("#8E969D");
    private static readonly Color DarkSteel = new("#4A4F55");
    private static readonly Color Timber = new("#9A6B3F");
    private static readonly Color Trunk = new("#6B4A2E");
    private static readonly Color Hedge = new("#3F7A3A");
    private static readonly Color[] Leaves = { new("#4E9443"), new("#5BA24C"), new("#3F8440"), new("#6DAE52") };
    private static readonly Color[] Flowers = { new("#E8536B"), new("#F2C230"), new("#F5F5F0"), new("#B56BD6") };
    private static readonly Color GlassRoof = new(0.72f, 0.86f, 0.94f, 0.42f);
    private static readonly Color WindowGlass = new("#8FB8D4");

    private View3D _view = null!;
    private HospitalSimulation _sim = null!;
    private StandardMaterial3D? _lampGlow;

    public void Initialise(View3D view, HospitalSimulation simulation)
    {
        _view = view;
        _sim = simulation;
        if (OS.GetCmdlineUserArgs().Contains("--outside-brick"))
        {
            Style = ExteriorStyle.Brick;
        }
    }

    /// <summary>The name over the door.</summary>
    private static string HospitalName => GameManager.Instance?.Session?.HospitalName is { Length: > 0 } name ? name : "Hospital";

    public void Rebuild(bool cutaway)
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }

        _lampGlow = null;
        if (IsoDraw.ViewFloor != 0)
        {
            return;
        }

        var site = _sim.Site;
        var layout = site.Layout;
        var solid = new MeshBatch();
        var flat = new MeshBatch();
        var glass = new MeshBatch();
        var glow = new MeshBatch();

        Cladding(solid, site, cutaway);
        if (layout is not null)
        {
            Roads(solid, flat, site, layout);
            CarPark(solid, flat, site, layout);
            TaxiRank(solid, flat, glass, site, layout);
            FrontPath(solid, site, layout);
            RoadSign(solid, site, layout);
            Lamps(solid, glow, site, layout);
        }

        foreach (var entrance in site.Buildings.SelectMany(b => b.Entrances))
        {
            Canopy(solid, glass, site, entrance);
        }

        Planting(solid, site);
        solid.AddTo(this);
        flat.AddTo(this, shadow: false);
        glass.AddTo(this, alpha: true, shadow: false);
        if (glow.Build() is { } lit)
        {
            _lampGlow = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                EmissionEnabled = true,
                Emission = new Color("#FFE9B0"),
                EmissionEnergyMultiplier = 0f,
            };
            AddChild(new MeshInstance3D { Mesh = lit, MaterialOverride = _lampGlow, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
    }

    /// <summary>The words on the signs outside (the hospital's name, Accident &amp; Emergency, TAXI...), for checks.</summary>
    public System.Collections.Generic.IEnumerable<string> SignTexts => GetChildren().OfType<Label3D>().Select(l => l.Text);

    /// <summary>Street lamps come on as it gets dark.</summary>
    public void Sync()
    {
        if (_lampGlow is not null)
        {
            _lampGlow.EmissionEnergyMultiplier = _view.Night * 2.2f;
        }
    }

    // ------------------------------------------------------------------ helpers

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

    private static bool Is(SiteMap site, int x, int y, TileKind kind) =>
        x >= 0 && y >= 0 && x < site.Width && y < site.Length && site[new GridCoord(x, y)] == kind;

    private static bool Tarmac(SiteMap site, int x, int y) =>
        x >= 0 && y >= 0 && x < site.Width && y < site.Length
        && site[new GridCoord(x, y)] is TileKind.Road or TileKind.Parking or TileKind.TaxiRank or TileKind.AmbulanceBay;

    private static bool Soft(SiteMap site, int x, int y) =>
        x >= 0 && y >= 0 && x < site.Width && y < site.Length && site[new GridCoord(x, y)] is TileKind.Grass or TileKind.Paving;

    /// <summary>Unit vectors along a wall and straight out from it.</summary>
    private static ((int X, int Y) Along, (int X, int Y) Out) Axes(WallSide side) => side switch
    {
        WallSide.North => ((1, 0), (0, -1)),
        WallSide.South => ((1, 0), (0, 1)),
        WallSide.West => ((0, 1), (-1, 0)),
        _ => ((0, 1), (1, 0)),
    };

    /// <summary>The yaw that turns something facing +Z (grid +y) to face (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
    private static Basis Facing(double dx, double dy) => new(Vector3.Up, Kit3D.Yaw(dx, dy));

    /// <summary>
    /// Flat lettering on an upright board: centred at <paramref name="centre"/>, read from the side
    /// (<paramref name="nx"/>, <paramref name="ny"/>) points to, shrunk to fit the given width and height.
    /// </summary>
    private void Lettering(string text, Vector3 centre, double nx, double ny, float maxWidth, float maxHeight, Color colour)
    {
        const int size = 96;
        var font = ThemeDB.FallbackFont;
        var measured = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        var pixel = Math.Min(maxWidth / Math.Max(1f, measured.X), maxHeight / Math.Max(1f, measured.Y));
        AddChild(new Label3D
        {
            Text = text,
            Font = font,
            FontSize = size,
            PixelSize = pixel,
            Modulate = colour,
            OutlineSize = 0,
            DoubleSided = false,
            Shaded = false,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Transform = new Transform3D(Facing(nx, ny), centre + new Vector3((float)nx * 0.03f, 0, (float)ny * 0.03f)),
        });
    }

    /// <summary>Lettering painted on the ground, its top towards (<paramref name="ux"/>, <paramref name="uy"/>).</summary>
    private void GroundLettering(string text, double x, double y, double ux, double uy, float maxWidth, float maxHeight, Color colour)
    {
        const int size = 96;
        var font = ThemeDB.FallbackFont;
        var measured = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        var pixel = Math.Min(maxWidth / Math.Max(1f, measured.X), maxHeight / Math.Max(1f, measured.Y));
        var basis = Facing(-ux, -uy) * new Basis(Vector3.Right, -Mathf.Pi / 2);
        AddChild(new Label3D
        {
            Text = text,
            Font = font,
            FontSize = size,
            PixelSize = pixel,
            Modulate = colour,
            OutlineSize = 0,
            DoubleSided = false,
            Shaded = false,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Transform = new Transform3D(basis, Kit3D.At(x, y, 0.02f)),
        });
    }

    // ------------------------------------------------------------------ the building's outside

    /// <summary>
    /// The outer face of every outside wall: a plinth, the wall finish, windows and a coping. Follows the
    /// walls as built, so it grows with extensions and stops at doorways.
    /// </summary>
    private void Cladding(MeshBatch m, SiteMap site, bool cutaway)
    {
        var brick = Style == ExteriorStyle.Brick;
        var face = brick ? new Color("#A9553F") : new Color("#EEF0EE");
        var faceAlt = brick ? new Color("#9D4C38") : new Color("#E4E8E8");
        var plinth = brick ? new Color("#6E6A66") : new Color("#5C6B78");
        var band = brick ? new Color("#E4DDCF") : HospitalBlue;
        var frame = brick ? White : new Color("#56606A");
        const float t = (float)WorldLayer.WallThickness;
        const float skin = 0.07f;
        var h = cutaway ? Kit3D.H(12) : Walls3D.FullHeight;
        var plinthH = cutaway ? h * 0.4f : 0.26f;
        var bandH = cutaway ? 0f : 0.2f;

        void Panel(double x, double y, bool horizontal, int outward, int index)
        {
            // A slab hugging the outer face of one wall tile; "outward" is -1 or +1 along the other axis.
            (double X, double Y, double W, double L) At(float proud, double inset, double grow) => horizontal
                ? (x + inset - grow, y + (outward * (t / 2)) + (outward < 0 ? -proud : 0), 1 - (inset * 2) + (grow * 2), proud)
                : (x + (outward * (t / 2)) + (outward < 0 ? -proud : 0), y + inset - grow, proud, 1 - (inset * 2) + (grow * 2));

            var grow = (t / 2) + skin;
            var wall = At(skin, 0, grow);
            m.Block(wall.X, wall.Y, wall.W, wall.L, h - plinthH - bandH, index % 2 == 0 ? face : faceAlt, plinthH);
            var foot = At(skin + 0.03f, 0, grow + 0.03);
            m.Block(foot.X, foot.Y, foot.W, foot.L, plinthH, plinth);
            if (cutaway)
            {
                return;
            }

            var top = At(skin + 0.04f, 0, grow + 0.04);
            m.Block(top.X, top.Y, top.W, top.L, bandH, band, h - bandH);

            // Windows in pairs, two tiles of wall between each pair.
            var slot = ((index % 5) + 5) % 5;
            if (slot is 1 or 2)
            {
                var outer = slot == 1 ? 0.12 : 0.0;
                var surround = At(skin + 0.03f, 0, 0);
                m.Block(surround.X, surround.Y, surround.W, surround.L, 0.86f, frame, 0.62f);
                var pane = At(skin + 0.05f, 0.07, 0);
                m.Block(pane.X, pane.Y, pane.W, pane.L, 0.7f, WindowGlass, 0.7f);
                _ = outer;
            }
        }

        bool Inside(int x, int y) => Is(site, x, y, TileKind.BuildingFloor);
        foreach (var (x, y, horizontal) in _view.World.WallEdges)
        {
            if (horizontal)
            {
                var below = Inside(x, y);
                var above = Inside(x, y - 1);
                if (below != above && y <= site.Length)
                {
                    Panel(x, y, true, below ? -1 : 1, x);
                }
            }
            else
            {
                var right = Inside(x, y);
                var left = Inside(x - 1, y);
                if (right != left && y < site.Length)
                {
                    Panel(x, y, false, right ? -1 : 1, y);
                }
            }
        }
    }

    /// <summary>
    /// A canopy over an entrance: steel posts, a glass roof, and a deep fascia with the sign. The main
    /// entrance carries the hospital's name in blue; the ambulance entrance says Accident and Emergency in red.
    /// </summary>
    private void Canopy(MeshBatch m, MeshBatch glass, SiteMap site, Entrance e)
    {
        var (a, o) = Axes(e.Side);
        // The doorway's first outside corner: the wall line at the start of the doorway.
        double wx = e.Side == WallSide.East ? e.Tile.X + 1 : e.Tile.X;
        double wy = e.Side == WallSide.South ? e.Tile.Y + 1 : e.Tile.Y;
        Vector3 P(double along, double outward, float height) => Kit3D.At(wx + (a.X * along) + (o.X * outward), wy + (a.Y * along) + (o.Y * outward), height);

        var colour = e.IsAmbulanceEntrance ? EmergencyRed : HospitalBlue;
        var width = e.WidthTiles + 4.0;
        var depth = e.IsAmbulanceEntrance ? 2.6 : 3.4;
        var roofH = Walls3D.FullHeight + 0.25f;
        var start = -2.0;

        // Posts at the outer corners and a slim frame round the roof.
        foreach (var along in new[] { start + 0.2, start + width - 0.2 })
        {
            m.Cylinder(P(along, depth - 0.2, roofH / 2), 0.09f, roofH, 0.09f, Steel);
            m.Cylinder(P(along, depth - 0.2, 0.06f), 0.16f, 0.12f, 0.16f, DarkSteel);
        }

        void Slab(MeshBatch batch, double along, double length, double from, double to, float bottom, float height, Color c)
        {
            var p0 = P(along, from, 0);
            var p1 = P(along + length, to, 0);
            batch.Block(Math.Min(p0.X, p1.X), Math.Min(p0.Z, p1.Z), Math.Abs(p1.X - p0.X), Math.Abs(p1.Z - p0.Z), height, c, bottom);
        }

        Slab(glass, start, width, 0.2, depth, roofH, 0.05f, GlassRoof);
        Slab(m, start, width, depth - 0.14, depth, roofH - 0.08f, 0.16f, Steel);
        Slab(m, start, 0.14, 0.2, depth, roofH - 0.08f, 0.16f, Steel);
        Slab(m, start + width - 0.14, 0.14, 0.2, depth, roofH - 0.08f, 0.16f, Steel);
        for (var i = 1; i < 4; i++)
        {
            Slab(m, start + (width * i / 4) - 0.04, 0.08, 0.2, depth, roofH - 0.04f, 0.08f, Steel);
        }

        // The sign: a tall board standing on the front edge of the canopy.
        var boardH = e.IsAmbulanceEntrance ? 1.25f : 1.5f;
        Slab(m, start, width, depth - 0.1, depth + 0.08, roofH + 0.08f, boardH, colour);
        Slab(m, start - 0.05, width + 0.1, depth - 0.12, depth + 0.1, roofH + 0.08f + boardH, 0.07f, White);
        var mid = P(start + (width / 2), depth + 0.08, roofH + 0.08f + (boardH / 2));
        if (e.IsAmbulanceEntrance)
        {
            Lettering("Accident & Emergency", mid, o.X, o.Y, (float)width - 0.6f, boardH * 0.8f, White);
        }
        else
        {
            Lettering(HospitalName, mid + new Vector3(0, boardH * 0.14f, 0), o.X, o.Y, (float)width - 0.6f, boardH * 0.62f, White);
            Lettering("Main Entrance", mid - new Vector3(0, boardH * 0.32f, 0), o.X, o.Y, (float)width - 0.8f, boardH * 0.3f, new Color("#CFE4F7"));
        }

        if (e.IsAmbulanceEntrance)
        {
            return;
        }

        // Planters either side of the front doors.
        foreach (var along in new[] { start - 0.2, start + width - 1.3 })
        {
            var p = P(along, 0.5, 0);
            var q = P(along + 1.5, 1.6, 0);
            var (x, y, w, l) = (Math.Min(p.X, q.X), Math.Min(p.Z, q.Z), Math.Abs(q.X - p.X), Math.Abs(q.Z - p.Z));
            if (!Enumerable.Range(0, 2).All(i => Is(site, (int)Math.Floor(x + (w / 2)), (int)Math.Floor(y + (l / 2)), TileKind.Paving)))
            {
                continue;
            }

            Planter(m, x, y, w, l, (int)x + (int)y);
        }
    }

    private static void Planter(MeshBatch m, double x, double y, double w, double l, int seed)
    {
        m.Block(x, y, w, l, 0.42f, new Color("#7D8288"));
        m.Block(x + 0.08, y + 0.08, w - 0.16, l - 0.16, 0.05f, new Color("#5A4636"), 0.4f);
        var n = 0;
        for (var fx = 0.3; fx < w - 0.1; fx += 0.42)
        {
            for (var fy = 0.3; fy < l - 0.1; fy += 0.42)
            {
                n++;
                var r = Hash((int)(fx * 10) + seed, (int)(fy * 10), n);
                m.Ball(Kit3D.At(x + fx, y + fy, 0.56f), new Vector3(0.44f, 0.36f, 0.44f), r < 0.45f ? Leaves[n % Leaves.Length] : Flowers[(n + seed) % Flowers.Length], small: true);
            }
        }
    }

    // ------------------------------------------------------------------ roads

    private void Roads(MeshBatch m, MeshBatch flat, SiteMap site, SiteLayout layout)
    {
        // Kerbs wherever tarmac meets grass or paving, and double yellow lines along the roads.
        for (var y = 0; y < site.Length; y++)
        {
            for (var x = 0; x < site.Width; x++)
            {
                if (!Tarmac(site, x, y))
                {
                    continue;
                }

                var road = Is(site, x, y, TileKind.Road);
                if (Soft(site, x, y - 1))
                {
                    m.Block(x, y - 0.16, 1, 0.16, 0.07f, Kerb);
                    if (road)
                    {
                        flat.Floor(x, y + 0.22, 1, 0.09, 0.004f, Yellow);
                        flat.Floor(x, y + 0.4, 1, 0.09, 0.004f, Yellow);
                    }
                }

                if (Soft(site, x, y + 1))
                {
                    m.Block(x, y + 1, 1, 0.16, 0.07f, Kerb);
                    if (road)
                    {
                        flat.Floor(x, y + 1 - 0.31, 1, 0.09, 0.004f, Yellow);
                        flat.Floor(x, y + 1 - 0.49, 1, 0.09, 0.004f, Yellow);
                    }
                }

                if (Soft(site, x - 1, y))
                {
                    m.Block(x - 0.16, y, 0.16, 1, 0.07f, Kerb);
                    if (road)
                    {
                        flat.Floor(x + 0.22, y, 0.09, 1, 0.004f, Yellow);
                        flat.Floor(x + 0.4, y, 0.09, 1, 0.004f, Yellow);
                    }
                }

                if (Soft(site, x + 1, y))
                {
                    m.Block(x + 1, y, 0.16, 1, 0.07f, Kerb);
                    if (road)
                    {
                        flat.Floor(x + 1 - 0.31, y, 0.09, 1, 0.004f, Yellow);
                        flat.Floor(x + 1 - 0.49, y, 0.09, 1, 0.004f, Yellow);
                    }
                }
            }
        }

        // Centre lines: dashes down the middle of each road, broken at junctions.
        foreach (var road in layout.Roads)
        {
            var others = layout.Roads.Where(r => r != road).ToList();
            var horizontal = road.Width > road.Length;
            var length = horizontal ? road.Width : road.Length;
            for (var d = 1; d + 3 <= length; d += 6)
            {
                (double x, double y, double w, double l) = horizontal
                    ? ((double)road.X + d, road.Y + (road.Length / 2.0) - 0.1, 3.0, 0.2)
                    : (road.X + (road.Width / 2.0) - 0.1, (double)road.Y + d, 0.2, 3.0);
                var probe = new GridRect((int)x - 2, (int)y - 2, (int)w + 4, (int)l + 4);
                if (!others.Any(r => r.Intersects(probe)))
                {
                    flat.Floor(x, y, w, l, 0.004f, Line);
                }
            }
        }

        // A zebra crossing where the front path meets the road, with a beacon either side.
        var (a, o) = Axes(layout.Plan.EntranceSide);
        var path = layout.FrontPath;
        var front = layout.Roads.FirstOrDefault(r => r.Contains(layout.WalkInSpawn));
        if (front.Area > 0)
        {
            var across = a.X != 0;
            var from = across ? path.X : path.Y;
            var span = across ? path.Width : path.Length;
            var roadWidth = across ? front.Length : front.Width;
            for (var i = 0.5; i + 1 <= roadWidth; i += 2)
            {
                if (across)
                {
                    flat.Floor(from + 0.5, front.Y + i, span - 1, 1, 0.005f, Line);
                }
                else
                {
                    flat.Floor(front.X + i, from + 0.5, 1, span - 1, 0.005f, Line);
                }
            }

            foreach (var end in new[] { -0.9, span + 0.9 })
            {
                foreach (var far in new[] { false, true })
                {
                    var (bx, by) = across
                        ? (from + end, far == (o.Y > 0) ? front.Bottom + 0.6 : front.Y - 0.6)
                        : (far == (o.X > 0) ? front.Right + 0.6 : front.X - 0.6, from + end);
                    if (!Soft(site, (int)Math.Floor(bx), (int)Math.Floor(by)))
                    {
                        continue;
                    }

                    for (var band = 0; band < 6; band++)
                    {
                        m.Cylinder(Kit3D.At(bx, by, 0.18f + (band * 0.36f)), 0.07f, 0.36f, 0.07f, band % 2 == 0 ? new Color("#22262A") : White);
                    }

                    m.Ball(Kit3D.At(bx, by, 2.34f), new Vector3(0.4f, 0.4f, 0.4f), new Color("#F6A21C"));
                }
            }
        }
    }

    // ------------------------------------------------------------------ car park

    private void CarPark(MeshBatch m, MeshBatch flat, SiteMap site, SiteLayout layout)
    {
        var (a, o) = Axes(layout.Plan.EntranceSide);
        var across = a.X != 0;
        var bays = layout.ParkingBays.Where(b => b.Tiles().All(t => site[t] == TileKind.Parking)).ToList();
        for (var i = 0; i < layout.ParkingBays.Count; i++)
        {
            var bay = layout.ParkingBays[i];
            if (!bays.Contains(bay))
            {
                continue;
            }

            // White lines down both long sides, stopping short of the road.
            if (across)
            {
                flat.Floor(bay.X - 0.08, bay.Y + 0.3, 0.16, bay.Length - 0.6, 0.004f, Line);
                flat.Floor(bay.Right - 0.08, bay.Y + 0.3, 0.16, bay.Length - 0.6, 0.004f, Line);
            }
            else
            {
                flat.Floor(bay.X + 0.3, bay.Y - 0.08, bay.Width - 0.6, 0.16, 0.004f, Line);
                flat.Floor(bay.X + 0.3, bay.Bottom - 0.08, bay.Width - 0.6, 0.16, 0.004f, Line);
            }

            // A wheel stop at the building end.
            var cx = bay.X + (bay.Width / 2.0);
            var cy = bay.Y + (bay.Length / 2.0);
            var half = (across ? bay.Length : bay.Width) / 2.0;
            var away = layout.MainCarPark is { } over && over.Intersects(bay) ? -1 : 1;
            var sx = cx - (o.X * away * (half - 0.7));
            var sy = cy - (o.Y * away * (half - 0.7));
            m.Block(sx - (across ? 1.5 : 0.15), sy - (across ? 0.15 : 1.5), across ? 3 : 0.3, across ? 0.3 : 3, 0.14f, new Color("#D8D5CC"));

            if (i < layout.DisabledBays)
            {
                // Blue badge space: a blue square with the wheelchair symbol, and yellow hatching at the road end.
                var bx = cx + (o.X * away * 0.4);
                var by = cy + (o.Y * away * 0.4);
                flat.Floor(bx - 1.5, by - 1.5, 3, 3, 0.005f, HospitalBlue);
                Wheelchair(flat, bx, by, o.X, o.Y);
                for (var s = 0; s < 4; s++)
                {
                    var d = half - 0.5 - (s * 0.45);
                    flat.Floor(cx + (o.X * away * d) - (across ? 2.0 : 0.1), cy + (o.Y * away * d) - (across ? 0.1 : 2.0), across ? 4 : 0.2, across ? 0.2 : 4, 0.005f, Yellow);
                }
            }
        }

        void HedgeRun(double x, double y, double w, double l)
        {
            var tiles = new GridRect((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Ceiling(w), (int)Math.Ceiling(l)).Tiles();
            if (tiles.All(t => Is(site, t.X, t.Y, TileKind.Grass)))
            {
                HedgeBlock(m, x, y, w, l);
            }
        }

        // A hedge round the back and ends of the main car park across the road, and its own P sign.
        if (layout.MainCarPark is { } main)
        {
            if (across)
            {
                HedgeRun(main.X - 1.4, o.Y > 0 ? main.Bottom + 0.4 : main.Y - 1.4, main.Width + 2.8, 1);
                HedgeRun(main.X - 1.4, main.Y + 1, 1, main.Length - 1);
                HedgeRun(main.Right + 0.4, main.Y + 1, 1, main.Length - 1);
                PSign(m, site, main.Right + 0.9, o.Y > 0 ? main.Y + 0.5 : main.Bottom - 0.5, o, across);
            }
            else
            {
                HedgeRun(o.X > 0 ? main.Right + 0.4 : main.X - 1.4, main.Y - 1.4, 1, main.Length + 2.8);
                HedgeRun(main.X + 1, main.Y - 1.4, main.Width - 1, 1);
                HedgeRun(main.X + 1, main.Bottom + 0.4, main.Width - 1, 1);
                PSign(m, site, o.X > 0 ? main.X + 0.5 : main.Right - 0.5, main.Bottom + 0.9, o, across);
            }
        }

        if (layout.CarPark is not { } park)
        {
            return;
        }

        // The P sign on the grass at the far end from the path, and a pay machine on the paving behind the spaces.
        var far = layout.ParkingBays.Count > 0 ? layout.ParkingBays[^1] : park;
        var near = layout.ParkingBays.Count > 0 ? layout.ParkingBays[0] : park;
        var pathSide = across ? Math.Sign(layout.FrontPath.X - near.X) : Math.Sign(layout.FrontPath.Y - near.Y);
        if (pathSide == 0)
        {
            pathSide = 1;
        }

        var end = across
            ? (X: pathSide > 0 ? park.X - 1.0 : park.Right + 1.0, Y: park.Y + (park.Length / 2.0) + (o.Y * ((park.Length / 2.0) - 1.2)))
            : (X: park.X + (park.Width / 2.0) + (o.X * ((park.Width / 2.0) - 1.2)), Y: pathSide > 0 ? park.Y - 1.0 : park.Bottom + 1.0);
        _ = far;
        PSign(m, site, end.X, end.Y, o, across);

        // Pay machine: on the paving just behind the space nearest the path.
        var mx = near.X + (near.Width / 2.0) - (o.X * ((across ? near.Length : near.Width) / 2.0 + 0.9));
        var my = near.Y + (near.Length / 2.0) - (o.Y * ((across ? near.Length : near.Width) / 2.0 + 0.9));
        if (Is(site, (int)Math.Floor(mx), (int)Math.Floor(my), TileKind.Paving))
        {
            m.Block(mx - 0.3, my - 0.3, 0.6, 0.6, 1.25f, new Color("#2F5FA8"));
            m.Block(mx - 0.34, my - 0.34, 0.68, 0.68, 0.1f, Yellow, 1.25f);
            m.Box(Kit3D.At(mx + (o.X * 0.31), my + (o.Y * 0.31), 0.95f), new Vector3(across ? 0.36f : 0.04f, 0.24f, across ? 0.04f : 0.36f), new Color("#BFE3F2"));
        }

        // A hedge closing the gap between the car park and the path, and one along its far end.

        if (across)
        {
            HedgeRun(pathSide > 0 ? park.Right + 0.4 : park.X - 1.4, park.Y + 0.5, 1, park.Length - 4);
            HedgeRun(pathSide > 0 ? park.X - 2.4 : park.Right + 1.4, park.Y + 0.5, 1, park.Length - 4);
        }
        else
        {
            HedgeRun(park.X + 0.5, pathSide > 0 ? park.Bottom + 0.4 : park.Y - 1.4, park.Width - 4, 1);
            HedgeRun(park.X + 0.5, pathSide > 0 ? park.Y - 2.4 : park.Bottom + 1.4, park.Width - 4, 1);
        }
    }

    /// <summary>A blue P on a pole, readable from the road and from the hospital.</summary>
    private void PSign(MeshBatch m, SiteMap site, double x, double y, (int X, int Y) o, bool across)
    {
        if (!Is(site, (int)Math.Floor(x), (int)Math.Floor(y), TileKind.Grass))
        {
            return;
        }

        m.Cylinder(Kit3D.At(x, y, 1.1f), 0.06f, 2.2f, 0.06f, Steel);
        m.Box(Kit3D.At(x, y, 2.25f), new Vector3(across ? 1.1f : 0.1f, 1.1f, across ? 0.1f : 1.1f), HospitalBlue);
        Lettering("P", Kit3D.At(x + (o.X * 0.06), y + (o.Y * 0.06), 2.25f), o.X, o.Y, 0.9f, 1.0f, White);
        Lettering("P", Kit3D.At(x - (o.X * 0.06), y - (o.Y * 0.06), 2.25f), -o.X, -o.Y, 0.9f, 1.0f, White);
    }

    private static void HedgeBlock(MeshBatch m, double x, double y, double w, double l)
    {
        m.Block(x, y, w, l, 0.62f, Hedge);
        m.Block(x + 0.08, y + 0.08, w - 0.16, l - 0.16, 0.1f, Hedge.Lightened(0.08f), 0.62f);
    }

    /// <summary>The white wheelchair symbol, about 2.4 tiles across, its top towards the building (away from the road).</summary>
    private static void Wheelchair(MeshBatch flat, double x, double y, int ox, int oy)
    {
        // Drawn in a little frame: u to the right, v towards the road.
        var (ux, uy) = (-oy, ox);
        void Quad(double u, double v, double w, double h)
        {
            var (x0, y0) = (x + (ux * u) + (ox * v), y + (uy * u) + (oy * v));
            var (x1, y1) = (x + (ux * (u + w)) + (ox * (v + h)), y + (uy * (u + w)) + (oy * (v + h)));
            flat.Floor(Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0), Math.Abs(y1 - y0), 0.007f, White);
        }

        Quad(-0.35, -1.0, 0.36, 0.36);   // head
        Quad(-0.3, -0.55, 0.26, 0.85);   // back
        Quad(-0.3, 0.1, 0.8, 0.24);      // seat
        Quad(0.3, 0.1, 0.24, 0.7);       // legs
        Quad(0.3, 0.7, 0.5, 0.2);        // feet
        Quad(-0.2, -0.3, 0.6, 0.2);      // arm
        Quad(-0.85, 0.45, 0.22, 0.5);    // wheel, left
        Quad(-0.85, 0.85, 0.9, 0.22);    // wheel, bottom
        Quad(-0.05, 0.5, 0.2, 0.5);      // wheel, right
    }

    // ------------------------------------------------------------------ taxi rank

    private void TaxiRank(MeshBatch m, MeshBatch flat, MeshBatch glass, SiteMap site, SiteLayout layout)
    {
        if (layout.TaxiRank is not { } rank || !rank.Tiles().All(t => site[t] == TileKind.TaxiRank))
        {
            return;
        }

        var (a, o) = Axes(layout.Plan.EntranceSide);
        var across = a.X != 0;

        // A yellow box round the rank, broken along the road side, and TAXI painted in it.
        const double w = 0.14;
        if (across)
        {
            flat.Floor(rank.X + 0.2, o.Y > 0 ? rank.Y + 0.25 : rank.Bottom - 0.25 - w, rank.Width - 0.4, w, 0.005f, Yellow);
            flat.Floor(rank.X + 0.2, rank.Y + 0.25, w, rank.Length - 0.3, 0.005f, Yellow);
            flat.Floor(rank.Right - 0.2 - w, rank.Y + 0.25, w, rank.Length - 0.3, 0.005f, Yellow);
            for (var d = 0.6; d + 1 < rank.Width; d += 2)
            {
                flat.Floor(rank.X + d, o.Y > 0 ? rank.Bottom - 0.1 : rank.Y - 0.04, 1.1, w, 0.005f, Yellow);
            }
        }
        else
        {
            flat.Floor(o.X > 0 ? rank.X + 0.25 : rank.Right - 0.25 - w, rank.Y + 0.2, w, rank.Length - 0.4, 0.005f, Yellow);
            flat.Floor(rank.X + 0.25, rank.Y + 0.2, rank.Width - 0.3, w, 0.005f, Yellow);
            flat.Floor(rank.X + 0.25, rank.Bottom - 0.2 - w, rank.Width - 0.3, w, 0.005f, Yellow);
            for (var d = 0.6; d + 1 < rank.Length; d += 2)
            {
                flat.Floor(o.X > 0 ? rank.Right - 0.1 : rank.X - 0.04, rank.Y + d, w, 1.1, 0.005f, Yellow);
            }
        }

        var cx = rank.X + (rank.Width / 2.0);
        var cy = rank.Y + (rank.Length / 2.0);
        GroundLettering("TAXI", cx, cy, -o.X, -o.Y, 6.5f, 2.6f, Yellow);

        // On the pavement behind: a glass shelter with a bench, and the TAXI sign on a pole.
        var back = (across ? rank.Length : rank.Width) / 2.0;
        var sx = cx - (o.X * (back + 2.2));
        var sy = cy - (o.Y * (back + 2.2));
        if (!Is(site, (int)Math.Floor(sx), (int)Math.Floor(sy), TileKind.Paving))
        {
            return;
        }

        Vector3 S(double along, double outward, float h) => Kit3D.At(sx + (a.X * along) + (o.X * outward), sy + (a.Y * along) + (o.Y * outward), h);
        void Slab(MeshBatch batch, double along, double length, double from, double to, float bottom, float height, Color c)
        {
            var p0 = S(along, from, 0);
            var p1 = S(along + length, to, 0);
            batch.Block(Math.Min(p0.X, p1.X), Math.Min(p0.Z, p1.Z), Math.Abs(p1.X - p0.X), Math.Abs(p1.Z - p0.Z), height, c, bottom);
        }

        const float roof = 1.75f;
        foreach (var along in new[] { -3.0, 0.0, 3.0 })
        {
            m.Cylinder(S(along, -0.9, roof / 2), 0.06f, roof, 0.06f, DarkSteel);
        }

        foreach (var along in new[] { -3.0, 3.0 })
        {
            m.Cylinder(S(along, 0.8, roof / 2), 0.06f, roof, 0.06f, DarkSteel);
        }

        Slab(m, -3.3, 6.6, -1.1, 1.0, roof, 0.1f, new Color("#3D4349"));
        Slab(m, -3.3, 6.6, 0.9, 1.02, roof - 0.34f, 0.34f, Yellow);
        Slab(glass, -3.0, 6.0, -0.94, -0.88, 0.25f, roof - 0.3f, GlassRoof);
        Slab(m, -2.4, 4.8, -0.7, -0.1, 0.5f, 0.08f, Timber);
        foreach (var along in new[] { -2.2, 2.0 })
        {
            Slab(m, along, 0.14, -0.6, -0.2, 0, 0.5f, DarkSteel);
        }

        Lettering("TAXI", S(0, 1.02, roof - 0.17f), o.X, o.Y, 3.2f, 0.28f, new Color("#1B1C1F"));

        var pole = S(-4.6, 1.2, 0);
        m.Cylinder(pole + new Vector3(0, 1.2f, 0), 0.05f, 2.4f, 0.05f, Steel);
        m.Box(pole + new Vector3(0, 2.3f, 0), new Vector3(across ? 1.5f : 0.1f, 0.6f, across ? 0.1f : 1.5f), Yellow);
        Lettering("TAXI", pole + new Vector3(o.X * 0.06f, 2.3f, o.Y * 0.06f), o.X, o.Y, 1.3f, 0.5f, new Color("#1B1C1F"));
        Lettering("TAXI", pole + new Vector3(-o.X * 0.06f, 2.3f, -o.Y * 0.06f), -o.X, -o.Y, 1.3f, 0.5f, new Color("#1B1C1F"));
    }

    // ------------------------------------------------------------------ front path and sign

    private void FrontPath(MeshBatch m, SiteMap site, SiteLayout layout)
    {
        var (a, o) = Axes(layout.Plan.EntranceSide);
        var path = layout.FrontPath;
        var across = a.X != 0;

        // Bollards across the road end of the path, wide enough apart for a trolley.
        var roadEnd = across ? (o.Y > 0 ? path.Bottom - 0.6 : path.Y + 0.6) : (o.X > 0 ? path.Right - 0.6 : path.X + 0.6);
        var span = across ? path.Width : path.Length;
        for (var i = 0; i <= 2; i++)
        {
            var along = (across ? path.X : path.Y) + 0.5 + (i * (span - 1) / 2.0);
            var (bx, by) = across ? (along, roadEnd) : (roadEnd, along);
            m.Cylinder(Kit3D.At(bx, by, 0.35f), 0.11f, 0.7f, 0.11f, DarkSteel);
            m.Cylinder(Kit3D.At(bx, by, 0.6f), 0.115f, 0.08f, 0.115f, White);
        }

        // A bench and a bin on the grass (or pavement) beside the path, on the side away from the car park.
        var side = layout.CarPark is { } park
            ? (across ? Math.Sign(path.X - park.X) : Math.Sign(path.Y - park.Y))
            : 1;
        var edge = side > 0 ? (across ? path.Right : path.Bottom) + 0.3 : (across ? path.X : path.Y) - 1.1;
        var mid = across ? path.Y + 1.2 : path.X + 1.2;
        var (x, y, w, l) = across ? (edge, mid, 0.8, 3.0) : (mid, edge, 3.0, 0.8);
        if (Soft(site, (int)Math.Floor(x), (int)Math.Floor(y)) && Soft(site, (int)Math.Floor(x + w - 0.01), (int)Math.Floor(y + l - 0.01)))
        {
            Bench(m, x, y, w, l, across ? (side > 0 ? -1 : 1, 0) : (0, side > 0 ? -1 : 1));
            var (binX, binY) = across ? (x + 0.4, y + l + 0.7) : (x + w + 0.7, y + 0.4);
            m.Cylinder(Kit3D.At(binX, binY, 0.4f), 0.26f, 0.8f, 0.26f, new Color("#2F3F36"));
            m.Cylinder(Kit3D.At(binX, binY, 0.83f), 0.29f, 0.07f, 0.29f, new Color("#1F2A24"));
        }
    }

    /// <summary>A park bench filling the given footprint, its seat facing (<paramref name="face"/>).</summary>
    private static void Bench(MeshBatch m, double x, double y, double w, double l, (int X, int Y) face)
    {
        m.Block(x, y, w, l, 0.07f, Timber, 0.42f);
        var back = face.X != 0
            ? (X: face.X > 0 ? x : x + w - 0.1, Y: y, W: 0.1, L: l)
            : (X: x, Y: face.Y > 0 ? y : y + l - 0.1, W: w, L: 0.1);
        m.Block(back.X, back.Y, back.W, back.L, 0.42f, Timber.Darkened(0.08f), 0.49f);
        foreach (var f in new[] { 0.12, 0.88 })
        {
            var (lx, ly, lw, ll) = face.X != 0 ? (x + 0.05, y + (l * f) - 0.06, w - 0.1, 0.12) : (x + (w * f) - 0.06, y + 0.05, 0.12, l - 0.1);
            m.Block(lx, ly, lw, ll, 0.42f, DarkSteel);
        }
    }

    /// <summary>The sign by the road: the hospital's name and what is where.</summary>
    private void RoadSign(MeshBatch m, SiteMap site, SiteLayout layout)
    {
        var (a, o) = Axes(layout.Plan.EntranceSide);
        var across = a.X != 0;
        var path = layout.FrontPath;

        // In the gap between the path and the car park (or just beside the path), near the road.
        var side = layout.CarPark is { } park
            ? (across ? Math.Sign(park.X - path.X) : Math.Sign(park.Y - path.Y))
            : -1;
        if (side == 0)
        {
            side = -1;
        }

        var along = side > 0 ? (across ? path.Right : path.Bottom) + 1.0 : (across ? path.X : path.Y) - 1.0;
        var outEdge = across ? (o.Y > 0 ? path.Bottom - 1.6 : path.Y + 1.6) : (o.X > 0 ? path.Right - 1.6 : path.X + 1.6);
        var (x, y) = across ? (along, outEdge) : (outEdge, along);
        if (!Is(site, (int)Math.Floor(x), (int)Math.Floor(y), TileKind.Grass))
        {
            return;
        }

        const float width = 2.6f;
        const float height = 3.8f;
        var size = across ? new Vector3(width, height, 0.3f) : new Vector3(0.3f, height, width);
        m.Box(Kit3D.At(x, y, 0.1f), new Vector3(size.X + 0.3f, 0.2f, size.Z + 0.3f), new Color("#6E6A66"));
        m.Box(Kit3D.At(x, y, 0.2f + (height / 2)), size, HospitalBlue);
        var redAt = 0.2f + (height * 0.2f);
        m.Box(Kit3D.At(x, y, redAt), new Vector3(size.X + 0.02f, height * 0.2f, size.Z + 0.02f), EmergencyRed);
        m.Box(Kit3D.At(x, y, 0.2f + height - 0.05f), new Vector3(size.X + 0.04f, 0.1f, size.Z + 0.04f), White);

        foreach (var s in new[] { 1, -1 })
        {
            var (nx, ny) = (o.X * s, o.Y * s);
            Vector3 At(float h) => Kit3D.At(x + (nx * 0.16), y + (ny * 0.16), h);
            var words = HospitalName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var top = 0.2f + height - 0.45f;
            var step = Math.Min(0.5f, 1.5f / Math.Max(1, words.Length));
            for (var i = 0; i < words.Length; i++)
            {
                Lettering(words[i], At(top - (i * step)), nx, ny, width - 0.2f, step * 0.9f, White);
            }

            Lettering("Main Entrance", At(0.2f + (height * 0.42f)), nx, ny, width - 0.3f, 0.36f, new Color("#CFE4F7"));
            Lettering("A&E", At(redAt), nx, ny, width - 0.3f, 0.66f, White);
        }
    }

    // ------------------------------------------------------------------ lamps

    private static void Lamps(MeshBatch m, MeshBatch glow, SiteMap site, SiteLayout layout)
    {
        // Along the far kerb of every road, leaning over it; skipped where there is no grass to stand on.
        foreach (var road in layout.Roads)
        {
            var horizontal = road.Width > road.Length;
            var length = horizontal ? road.Width : road.Length;
            for (var d = 8; d < length; d += 18)
            {
                foreach (var nearSide in new[] { false, true })
                {
                    if (nearSide != ((d / 18) % 2 == 0))
                    {
                        continue;
                    }

                    var (x, y, dx, dy) = horizontal
                        ? (road.X + d + 0.5, nearSide ? road.Y - 0.7 : road.Bottom + 0.7, 0, nearSide ? 1 : -1)
                        : (nearSide ? road.X - 0.7 : road.Right + 0.7, road.Y + d + 0.5, nearSide ? 1 : -1, 0);
                    var path = layout.FrontPath;
                    var nearCrossing = horizontal ? x > path.X - 4 && x < path.Right + 4 : y > path.Y - 4 && y < path.Bottom + 4;
                    if (!Is(site, (int)Math.Floor(x), (int)Math.Floor(y), TileKind.Grass) || nearCrossing)
                    {
                        continue;
                    }

                    const float h = 3.3f;
                    m.Cylinder(Kit3D.At(x, y, 0.15f), 0.13f, 0.3f, 0.13f, DarkSteel);
                    m.Cylinder(Kit3D.At(x, y, h / 2), 0.06f, h, 0.06f, Steel);
                    m.Rod(Kit3D.At(x, y, h), Kit3D.At(x + (dx * 1.5), y + (dy * 1.5), h + 0.1f), 0.05f, Steel, small: true);
                    m.Box(Kit3D.At(x + (dx * 1.6), y + (dy * 1.6), h + 0.1f), new Vector3(dx != 0 ? 0.9f : 0.36f, 0.12f, dy != 0 ? 0.9f : 0.36f), DarkSteel);
                    glow.Box(Kit3D.At(x + (dx * 1.6), y + (dy * 1.6), h + 0.03f), new Vector3(dx != 0 ? 0.7f : 0.26f, 0.05f, dy != 0 ? 0.7f : 0.26f), new Color("#FFF3CF"));
                }
            }
        }
    }

    // ------------------------------------------------------------------ planting

    /// <summary>
    /// Trees and bushes on open grass, and a hedge round the edge of the site. Always in the same places
    /// for the same site, and anything whose ground has been built over is simply left out.
    /// </summary>
    private static void Planting(MeshBatch m, SiteMap site)
    {
        bool Grass(int x, int y) => Is(site, x, y, TileKind.Grass);
        bool Open(int x, int y, int r)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    if (!Grass(x + dx, y + dy))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // The boundary hedge, a tile in from the edge, wherever the edge is grass.
        for (var x = 0; x < site.Width; x++)
        {
            foreach (var y in new[] { 0, site.Length - 1 })
            {
                if (Grass(x, y) && Grass(x - 1, y) && Grass(x + 1, y))
                {
                    m.Block(x, y + 0.1, 1, 0.8, 0.7f, (x / 3) % 2 == 0 ? Hedge : Hedge.Lightened(0.04f));
                }
            }
        }

        for (var y = 0; y < site.Length; y++)
        {
            foreach (var x in new[] { 0, site.Width - 1 })
            {
                if (Grass(x, y) && Grass(x, y - 1) && Grass(x, y + 1))
                {
                    m.Block(x + 0.1, y, 0.8, 1, 0.7f, (y / 3) % 2 == 0 ? Hedge : Hedge.Lightened(0.04f));
                }
            }
        }

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
                if (!Open(x, y, 3) || NearBuilding(site, x, y, 9))
                {
                    continue;
                }

                if (pick < 0.5f)
                {
                    Bush(m, x + 0.5, y + 0.5, gx + (gy * 31));
                }
                else
                {
                    Tree(m, x + 0.5, y + 0.5, gx + (gy * 31), pick > 0.86f);
                }
            }
        }
    }

    private static bool NearBuilding(SiteMap site, int x, int y, int within) =>
        site.Buildings.SelectMany(b => b.Areas).Any(r => x >= r.X - within && x < r.Right + within && y >= r.Y - within && y < r.Bottom + within && y < site.Length);

    private static void Tree(MeshBatch m, double x, double y, int seed, bool conifer)
    {
        var scale = 0.85f + (Hash(seed, 7) * 0.4f);
        var leaf = Leaves[seed % Leaves.Length];
        if (conifer)
        {
            m.Cylinder(Kit3D.At(x, y, 0.4f * scale), 0.14f, 0.8f * scale, 0.14f, Trunk);
            for (var i = 0; i < 3; i++)
            {
                var r = (1.25f - (i * 0.32f)) * scale;
                m.Cylinder(Kit3D.At(x, y, (0.75f + (i * 0.8f) + 0.55f) * scale), r, 1.3f * scale, r, new Color("#356E3F").Lightened(i * 0.05f), cone: true);
            }

            return;
        }

        m.Cylinder(Kit3D.At(x, y, 0.7f * scale), 0.16f, 1.4f * scale, 0.16f, Trunk);
        m.Ball(Kit3D.At(x, y, 2.2f * scale), new Vector3(2.6f, 2.1f, 2.6f) * scale, leaf);
        m.Ball(Kit3D.At(x + (0.7 * scale), y + (0.3 * scale), 1.9f * scale), new Vector3(1.7f, 1.4f, 1.7f) * scale, leaf.Darkened(0.06f));
        m.Ball(Kit3D.At(x - (0.6 * scale), y - (0.4 * scale), 2.0f * scale), new Vector3(1.8f, 1.5f, 1.8f) * scale, leaf.Lightened(0.05f));
        m.Ball(Kit3D.At(x + (0.1 * scale), y - (0.2 * scale), 2.9f * scale), new Vector3(1.6f, 1.2f, 1.6f) * scale, leaf.Lightened(0.09f));
    }

    private static void Bush(MeshBatch m, double x, double y, int seed)
    {
        var leaf = Leaves[(seed + 1) % Leaves.Length].Darkened(0.05f);
        m.Ball(Kit3D.At(x, y, 0.4f), new Vector3(1.4f, 0.9f, 1.4f), leaf);
        m.Ball(Kit3D.At(x + 0.6, y + 0.3, 0.32f), new Vector3(1.0f, 0.7f, 1.0f), leaf.Lightened(0.06f));
        if (seed % 3 == 0)
        {
            foreach (var (dx, dy) in new[] { (0.2, -0.3), (-0.4, 0.2), (0.5, 0.4) })
            {
                m.Ball(Kit3D.At(x + dx, y + dy, 0.8f), new Vector3(0.22f, 0.22f, 0.22f), Flowers[seed % Flowers.Length], small: true);
            }
        }
    }
}
