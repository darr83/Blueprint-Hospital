using System.Linq;
using System.Collections.Generic;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Draws the hospital site: ground tiles, the building floor, the placement grid and entrance mats.
/// Walls are drawn by <see cref="WorldLayer"/> so they depth-sort with people and furniture.
/// Geometry is built into meshes once and redrawn only when something changes.
/// </summary>
public partial class SiteRenderer : Node2D
{
    private static readonly Dictionary<TileKind, Color> GroundColours = new()
    {
        [TileKind.Grass] = new Color("#6E9E57"),
        [TileKind.Paving] = new Color("#B9B5AA"),
        [TileKind.Road] = new Color("#55595E"),
        [TileKind.AmbulanceBay] = new Color("#D9C23E"),
        [TileKind.Parking] = new Color("#6D7176"),
        [TileKind.BuildingFloor] = new Color("#E7E3DA"),
    };

    private SiteMap _site = null!;
    private IsoProjection _projection = null!;
    private ArrayMesh? _groundMesh;
    private ArrayMesh? _gridMesh;
    private bool _showGrid = true;

    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            _showGrid = value;
            QueueRedraw();
        }
    }

    public void Initialise(SiteMap site, IsoProjection projection)
    {
        _site = site;
        _projection = projection;
        Rebuild();
    }

    /// <summary>Redraws the ground after the building has been extended.</summary>
    public void Rebuild()
    {
        _groundMesh = BuildGroundMesh();
        _gridMesh = BuildGridMesh();
        QueueRedraw();
    }

    /// <summary>Screen position of a point on the site (the ground floor), whichever floor is in view.</summary>
    public Vector2 ToScreen(double gridX, double gridY)
    {
        return IsoDraw.ToScreenGround(gridX, gridY);
    }

    public override void _Draw()
    {
        if (_groundMesh is null)
        {
            return;
        }

        if (IsoDraw.ViewFloor == 0)
        {
            DrawMesh(_groundMesh, null);
        }
        else
        {
            // Upstairs: the grounds below show faded, under this floor's slab (the building's outline).
            DrawMesh(_groundMesh, null);
            var site = new[] { ToScreen(0, 0), ToScreen(_site.Width, 0), ToScreen(_site.Width, _site.Length), ToScreen(0, _site.Length) };
            DrawColoredPolygon(site, UpperFloorGround);
            foreach (var r in _site.Buildings.SelectMany(b => b.Areas))
            {
                DrawColoredPolygon(new[] { ToScreen(r.X, r.Y), ToScreen(r.Right, r.Y), ToScreen(r.Right, r.Bottom), ToScreen(r.X, r.Bottom) }, UpperFloorSlab);
            }
        }

        if (_showGrid && _gridMesh is not null)
        {
            DrawMesh(_gridMesh, null);
        }

        if (IsoDraw.ViewFloor == 0)
        {
            foreach (var building in _site.Buildings)
            {
                DrawBuildingWalls(building);
            }
        }
    }

    /// <summary>The grounds seen from an upper floor: washed out towards the sky.</summary>
    private static readonly Color UpperFloorGround = new(0.9f, 0.93f, 0.96f, 0.62f);

    private static readonly Color UpperFloorSlab = new("#D6D3CC");

    private ArrayMesh BuildGroundMesh()
    {
        var vertices = new List<Vector2>(_site.Width * _site.Length * 6);
        var colours = new List<Color>(vertices.Capacity);

        foreach (var tile in _site.Bounds.Tiles())
        {
            var kind = _site[tile];
            var colour = GroundColours[kind];

            // Subtle variation so large areas don't look flat.
            var noise = Hash(tile) * 0.06f - 0.03f;
            colour = kind switch
            {
                TileKind.BuildingFloor => ((tile.X / 2) + (tile.Y / 2)) % 2 == 0 ? colour : colour.Darkened(0.035f),
                TileKind.Parking when tile.X % 6 == 0 => new Color("#E9E9E9"),
                _ => new Color(colour.R + noise, colour.G + noise, colour.B + noise),
            };

            AddQuad(vertices, colours, tile.X, tile.Y, 1, 1, colour);
        }

        return MakeMesh(Mesh.PrimitiveType.Triangles, vertices, colours);
    }

    private ArrayMesh BuildGridMesh()
    {
        // Grid lines every metre (two tiles) inside buildings, where the player will build.
        var vertices = new List<Vector2>();
        var colours = new List<Color>();
        var line = new Color(0.25f, 0.35f, 0.45f, 0.18f);
        var tilesPerMetre = (int)(1 / Units.MetresPerTile);

        foreach (var r in _site.Buildings.SelectMany(b => b.Areas))
        {
            for (var x = r.X; x <= r.Right; x += tilesPerMetre)
            {
                vertices.Add(ToScreen(x, r.Y));
                vertices.Add(ToScreen(x, r.Bottom));
                colours.Add(line);
                colours.Add(line);
            }

            for (var y = r.Y; y <= r.Bottom; y += tilesPerMetre)
            {
                vertices.Add(ToScreen(r.X, y));
                vertices.Add(ToScreen(r.Right, y));
                colours.Add(line);
                colours.Add(line);
            }
        }

        return MakeMesh(Mesh.PrimitiveType.Lines, vertices, colours);
    }

    private void DrawBuildingWalls(Building building)
    {
        foreach (var entrance in building.Entrances)
        {
            DrawEntranceMat(entrance);
        }
    }

    private void DrawEntranceMat(Entrance entrance)
    {
        var colour = entrance.IsAmbulanceEntrance ? new Color("#E53935") : new Color("#005EB8");
        var (x, y, w, l) = entrance.Side switch
        {
            WallSide.North or WallSide.South => (entrance.Tile.X, entrance.Tile.Y, entrance.WidthTiles, 1),
            _ => (entrance.Tile.X, entrance.Tile.Y, 1, entrance.WidthTiles),
        };

        DrawColoredPolygon(
            new[] { ToScreen(x, y), ToScreen(x + w, y), ToScreen(x + w, y + l), ToScreen(x, y + l) },
            new Color(colour, 0.55f));
    }

    private void AddQuad(List<Vector2> vertices, List<Color> colours, double x, double y, double w, double l, Color colour)
    {
        var p0 = ToScreen(x, y);
        var p1 = ToScreen(x + w, y);
        var p2 = ToScreen(x + w, y + l);
        var p3 = ToScreen(x, y + l);
        vertices.AddRange(new[] { p0, p1, p2, p0, p2, p3 });
        for (var i = 0; i < 6; i++)
        {
            colours.Add(colour);
        }
    }

    private static ArrayMesh MakeMesh(Mesh.PrimitiveType type, List<Vector2> vertices, List<Color> colours)
    {
        var mesh = new ArrayMesh();
        if (vertices.Count == 0)
        {
            return mesh;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();
        mesh.AddSurfaceFromArrays(type, arrays);
        return mesh;
    }

    private static float Hash(GridCoord c)
    {
        unchecked
        {
            var h = (c.X * 73856093) ^ (c.Y * 19349663);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
        }
    }
}
