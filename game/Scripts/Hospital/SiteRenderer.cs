using System.Collections.Generic;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Draws the hospital site: ground tiles, the building floor, the placement grid and outer walls.
/// Geometry is built into meshes once and redrawn only when something changes.
/// </summary>
public partial class SiteRenderer : Node2D
{
    /// <summary>Height of a full outer wall on screen (about 3 m).</summary>
    public const float WallHeight = 84f;

    /// <summary>Height of the cut-away front walls, so the inside stays visible.</summary>
    public const float CutawayWallHeight = 10f;

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
        _groundMesh = BuildGroundMesh();
        _gridMesh = BuildGridMesh();
        QueueRedraw();
    }

    public Vector2 ToScreen(double gridX, double gridY)
    {
        var (x, y) = _projection.GridToScreen(gridX, gridY);
        return new Vector2((float)x, (float)y);
    }

    public override void _Draw()
    {
        if (_groundMesh is null)
        {
            return;
        }

        DrawMesh(_groundMesh, null);
        if (_showGrid && _gridMesh is not null)
        {
            DrawMesh(_gridMesh, null);
        }

        foreach (var building in _site.Buildings)
        {
            DrawBuildingWalls(building);
        }
    }

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

        foreach (var building in _site.Buildings)
        {
            var r = building.Footprint;
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
        var r = building.Footprint;
        var backNorth = new Color("#D4DCE4");
        var backWest = new Color("#B8C3CE");
        var front = new Color("#8E9AA6");
        var cap = new Color("#5D6873");

        // Back walls (north and west) are drawn full height.
        for (var x = r.X; x < r.Right; x++)
        {
            if (!building.IsOpening(new GridCoord(x, r.Y), WallSide.North))
            {
                DrawWall(ToScreen(x, r.Y), ToScreen(x + 1, r.Y), WallHeight, backNorth, cap);
            }
        }

        for (var y = r.Y; y < r.Bottom; y++)
        {
            if (!building.IsOpening(new GridCoord(r.X, y), WallSide.West))
            {
                DrawWall(ToScreen(r.X, y), ToScreen(r.X, y + 1), WallHeight, backWest, cap);
            }
        }

        // Front walls (south and east) are cut away so the player can see inside.
        for (var x = r.X; x < r.Right; x++)
        {
            if (!building.IsOpening(new GridCoord(x, r.Bottom - 1), WallSide.South))
            {
                DrawWall(ToScreen(x, r.Bottom), ToScreen(x + 1, r.Bottom), CutawayWallHeight, front, cap);
            }
        }

        for (var y = r.Y; y < r.Bottom; y++)
        {
            if (!building.IsOpening(new GridCoord(r.Right - 1, y), WallSide.East))
            {
                DrawWall(ToScreen(r.Right, y), ToScreen(r.Right, y + 1), CutawayWallHeight, front, cap);
            }
        }

        foreach (var entrance in building.Entrances)
        {
            DrawEntranceMat(entrance);
        }
    }

    private void DrawWall(Vector2 a, Vector2 b, float height, Color face, Color capColour)
    {
        var up = new Vector2(0, -height);
        DrawColoredPolygon(new[] { a, b, b + up, a + up }, face);
        DrawLine(a + up, b + up, capColour, 2f);
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
