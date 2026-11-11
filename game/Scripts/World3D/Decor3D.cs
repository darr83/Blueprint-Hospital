using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// Everything that dresses the rooms on the floor in view: posters, clocks and signs on the walls,
/// the name board over each doorway, painted, striped, tiled and panelled wall finishes, the props
/// standing about (plants, water coolers, models), the things on desks, theatre lights, and wet
/// floors after an accident. Flat pictures are painted once by the 2D art into one texture (an
/// atlas) and laid on the walls as thin quads; props are merged into a couple of meshes.
/// </summary>
public partial class Decor3D : Node3D
{
    private const int AtlasWidth = 2048;
    private const int Padding = 3;

    private View3D _view = null!;
    private SubViewport _viewport = null!;
    private AtlasCanvas _canvas = null!;
    private StandardMaterial3D _material = null!;
    private bool _waiting;
    private Node3D? _puddles;
    private string _puddleKey = "";

    /// <summary>How many wall pictures, name boards and floor props were built (for tests).</summary>
    public int WallArtCount { get; private set; }

    public int BoardCount { get; private set; }

    public int PropCount { get; private set; }

    /// <summary>Screen pixels a tile covers along a wall in the old 2D drawing (the art's own scale).</summary>
    private static float TilePx => (IsoDraw.ToScreen(1, 0) - IsoDraw.ToScreen(0, 0)).Length();

    public void Initialise(View3D view)
    {
        _view = view;
        _viewport = new SubViewport
        {
            Name = "DecorCanvas",
            TransparentBg = true,
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Linear,
            Msaa2D = Viewport.Msaa.Msaa4X,
            Size = new Vector2I(16, 16),
        };
        AddChild(_viewport);
        _canvas = new AtlasCanvas { Name = "Canvas" };
        _viewport.AddChild(_canvas);
        _material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass,
            Roughness = 0.8f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            CullMode = BaseMaterial3D.CullModeEnum.Back,
        };
    }

    // ------------------------------------------------------------------ building

    private sealed record Cell(float W, float H, Action<CanvasItem> Draw)
    {
        public Vector2 At { get; set; }

        public float Scale { get; set; } = 1;
    }

    /// <summary>A picture quad: from <see cref="Origin"/> (its bottom left as seen from the front) along <see cref="Right"/> and <see cref="Up"/>, showing the bottom <see cref="Show"/> of its cell.</summary>
    private readonly record struct Quad(Vector3 Origin, Vector3 Right, Vector3 Up, int Cell, float Show = 1f);

    public void Rebuild(bool cutaway)
    {
        foreach (var child in GetChildren())
        {
            if (child != _viewport && child != _puddles)
            {
                child.QueueFree();
            }
        }

        WallArtCount = BoardCount = PropCount = 0;
        var cells = new List<Cell>();
        var keys = new Dictionary<string, int>();
        var quads = new List<Quad>();
        int CellFor(string key, float w, float h, Action<CanvasItem> draw)
        {
            if (!keys.TryGetValue(key, out var index))
            {
                index = cells.Count;
                cells.Add(new Cell(w, h, draw));
                keys[key] = index;
            }

            return index;
        }

        var solid = new MeshBatch();
        var glass = new MeshBatch();
        var glow = new MeshBatch();
        if (!cutaway)
        {
            AddWallPieces(quads, CellFor);
            AddNameBoards(solid, quads, CellFor);
        }

        AddWallFinishes(quads, CellFor, cutaway);
        AddProps(solid, glass, glow);

        solid.Frame = glass.Frame = glow.Frame = Transform3D.Identity;
        solid.AddTo(this);
        glass.AddTo(this, alpha: true, shadow: false);
        if (glow.Build() is { } glowMesh)
        {
            AddChild(new MeshInstance3D { Name = "Glow", Mesh = glowMesh, MaterialOverride = Kit3D.VertexColoursUnshaded, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }

        if (quads.Count > 0)
        {
            var size = Pack(cells);
            BuildQuads(quads, cells, size);
            Paint(cells, size);
        }
    }

    /// <summary>Lays the cells out in rows across the atlas, shrinking them all until they fit.</summary>
    private static Vector2I Pack(List<Cell> cells)
    {
        foreach (var scale in new[] { 2.5f, 2f, 1.5f, 1f, 0.6f })
        {
            float x = Padding, y = Padding, row = 0;
            foreach (var cell in cells)
            {
                var (w, h) = (Mathf.Ceil(cell.W * scale), Mathf.Ceil(cell.H * scale));
                if (x + w + Padding > AtlasWidth)
                {
                    x = Padding;
                    y += row + Padding;
                    row = 0;
                }

                cell.At = new Vector2(x, y);
                cell.Scale = scale;
                x += w + Padding;
                row = Math.Max(row, h);
            }

            var height = (int)(y + row + Padding);
            if (height <= 8192 || scale <= 0.6f)
            {
                return new Vector2I(AtlasWidth, Math.Min(height, 8192));
            }
        }

        return new Vector2I(AtlasWidth, 8192);
    }

    private void BuildQuads(List<Quad> quads, List<Cell> cells, Vector2I size)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var q in quads)
        {
            var cell = cells[q.Cell];
            var (u0, u1) = ((cell.At.X + 0.5f) / size.X, (cell.At.X + (cell.W * cell.Scale) - 0.5f) / size.X);
            var bottom = cell.At.Y + (cell.H * cell.Scale);
            var (vBottom, vTop) = ((bottom - 0.5f) / size.Y, (bottom - (cell.H * cell.Scale * q.Show) + 0.5f) / size.Y);
            var n = q.Right.Cross(q.Up).Normalized();
            var corners = new[]
            {
                (q.Origin, new Vector2(u0, vBottom)), (q.Origin + q.Right, new Vector2(u1, vBottom)),
                (q.Origin + q.Right + q.Up, new Vector2(u1, vTop)), (q.Origin + q.Up, new Vector2(u0, vTop)),
            };
            foreach (var k in new[] { 0, 2, 1, 0, 3, 2 })
            {
                st.SetNormal(n);
                st.SetUV(corners[k].Item2);
                st.AddVertex(corners[k].Item1);
            }
        }

        var instance = new MeshInstance3D
        {
            Name = "Pictures",
            Mesh = st.Commit(),
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(instance);
    }

    /// <summary>Paints every cell into the atlas, then keeps a copy with mipmaps so the pictures stay calm when zoomed out.</summary>
    private void Paint(List<Cell> cells, Vector2I size)
    {
        _viewport.Size = size;
        _canvas.Cells = cells.Select(c => (c.At, c.Scale, c.Draw)).ToList();
        _canvas.QueueRedraw();
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _material.AlbedoTexture = _viewport.GetTexture();
        if (!_waiting)
        {
            _waiting = true;
            RenderingServer.FramePostDraw += CopyTexture;
        }
    }

    private void CopyTexture()
    {
        RenderingServer.FramePostDraw -= CopyTexture;
        _waiting = false;
        if (!IsInstanceValid(this) || DisplayServer.GetName() == "headless")
        {
            return;
        }

        var image = _viewport.GetTexture()?.GetImage();
        if (image is null || image.IsEmpty())
        {
            return;
        }

        image.GenerateMipmaps();
        _material.AlbedoTexture = ImageTexture.CreateFromImage(image);
    }

    /// <summary>The 2D art drawn into the atlas, each cell under its own offset and scale.</summary>
    private sealed partial class AtlasCanvas : Node2D
    {
        public List<(Vector2 At, float Scale, Action<CanvasItem> Draw)> Cells { get; set; } = new();

        public override void _Draw()
        {
            var before = Flat.Override;
            try
            {
                foreach (var (at, scale, draw) in Cells)
                {
                    var xf = new Transform2D(0, new Vector2(scale, scale), 0, at);
                    Flat.Override = xf;
                    DrawSetTransformMatrix(xf);
                    draw(this);
                }
            }
            finally
            {
                Flat.Override = before;
                DrawSetTransformMatrix(Transform2D.Identity);
            }
        }
    }

    // ------------------------------------------------------------------ walls

    /// <summary>A point on the inside face of a room's wall, t tiles along it, and the way into the room.</summary>
    private static (Vector3 Point, Vector3 Inward) InnerFace(GridRect r, WallSide side, double t, double off)
    {
        var half = (WorldLayer.WallThickness / 2) + off;
        return side switch
        {
            WallSide.North => (Kit3D.At(r.X + t, r.Y + half), new Vector3(0, 0, 1)),
            WallSide.South => (Kit3D.At(r.X + t, r.Bottom - half), new Vector3(0, 0, -1)),
            WallSide.West => (Kit3D.At(r.X + half, r.Y + t), new Vector3(1, 0, 0)),
            _ => (Kit3D.At(r.Right - half, r.Y + t), new Vector3(-1, 0, 0)),
        };
    }

    /// <summary>The direction that runs left to right for someone facing a surface whose front is <paramref name="normal"/>.</summary>
    private static Vector3 RightOf(Vector3 normal) => new(normal.Z, 0, -normal.X);

    private void AddWallPieces(List<Quad> quads, Func<string, float, float, Action<CanvasItem>, int> cellFor)
    {
        var tile = TilePx;
        var wallH = Walls3D.FullHeight;
        foreach (var piece in Decorations.WallPieces)
        {
            var width = Math.Round((piece.T1 - piece.T0) * 10) / 10;
            var (w, h) = ((float)(width * tile), WorldLayer.WallHeight);
            var cell = cellFor($"wall:{piece.Kind}:{width}", w, h, c => piece.DrawFlat(c, w, h));
            var (a, inward) = InnerFace(piece.Rect, piece.Side, piece.T0, 0.012);
            var (b, _) = InnerFace(piece.Rect, piece.Side, piece.T1, 0.012);
            var right = RightOf(inward);
            var origin = (b - a).Dot(right) > 0 ? a : b;
            quads.Add(new Quad(origin, right * (float)(piece.T1 - piece.T0), new Vector3(0, wallH, 0), cell));
            WallArtCount++;
        }
    }

    /// <summary>Each named room's board standing on its door frame, lettered on both faces.</summary>
    private void AddNameBoards(MeshBatch solid, List<Quad> quads, Func<string, float, float, Action<CanvasItem>, int> cellFor)
    {
        var tile = TilePx;
        const float thick = 0.1f;
        foreach (var space in _view.Simulation.Spaces.Where(s => IsoDraw.OnViewFloor(s.Rect.Y) && RoomSigns.Named(s)))
        {
            var d = space.Doors[0];
            var horizontal = d.Side is WallSide.North or WallSide.South;
            var outward = d.Side switch { WallSide.North => new Vector3(0, 0, -1), WallSide.South => new Vector3(0, 0, 1), WallSide.West => new Vector3(-1, 0, 0), _ => new Vector3(1, 0, 0) };
            var kind = RoomSigns.KindOf(space);
            var leaves = kind switch { RoomSigns.DoorKind.Double => 2, RoomSigns.DoorKind.Single => 1, _ => 0 };
            var leafTiles = leaves == 0 ? 0 : Math.Min(d.Width / (double)leaves, 2.0);
            var open = leaves == 0 ? d.Width : leafTiles * leaves;
            var icon = Signs.IconFor(space);
            var colour = RoomSigns.Colour(space);
            var style = Signs.DoorStyle;
            var name = space.Name;
            var tiles = Math.Max(Math.Min(open, 4), Signs.BoardLength(name, icon) / tile);
            var length = (float)(tiles * tile);
            var cell = cellFor($"board:{name}:{colour.ToHtml()}:{icon}:{style}:{length:0}", length, Signs.BoardHeight, c => Signs.NameBoard(c, style, name, colour, icon, length));

            var (mx, my) = RoomSigns.DoorMiddle(space);
            if (RoomSigns.OpenGap(space) && !_view.Simulation.IsEntranceDoor(space, d))
            {
                // A plain gap has nothing over it to stand on, so the board stands on the wall beside it,
                // on whichever side has more wall (centred over the gap if neither has room).
                var wallLength = horizontal ? space.Rect.Width : space.Rect.Length;
                var before = d.Offset;
                var after = wallLength - d.Offset - d.Width;
                var shift = (d.Width / 2.0) + (tiles / 2) + 0.3;
                if (Math.Max(before, after) >= (tiles + 0.6))
                {
                    var step = after >= before ? shift : -shift;
                    (mx, my) = horizontal ? (mx + step, my) : (mx, my + step);
                }
            }

            var lift = Kit3D.H(Math.Max(RoomSigns.FrameHeight, WorldLayer.WallHeight) + 2);
            var bh = Kit3D.H(Signs.BoardHeight);
            var along = horizontal ? new Vector3(1, 0, 0) : new Vector3(0, 0, 1);
            var mid = Kit3D.At(mx, my, lift);
            var (top, face) = Signs.BoardColours(style, colour);
            solid.Box(mid + new Vector3(0, bh / 2, 0), horizontal ? new Vector3((float)tiles, bh, thick) : new Vector3(thick, bh, (float)tiles), face);
            solid.Box(mid + new Vector3(0, bh + 0.005f, 0), horizontal ? new Vector3((float)tiles, 0.01f, thick) : new Vector3(thick, 0.01f, (float)tiles), top);
            foreach (var n in new[] { outward, -outward })
            {
                var right = RightOf(n);
                var centre = mid + (n * ((thick / 2) + 0.004f));
                quads.Add(new Quad(centre - (right * (float)(tiles / 2)), right * (float)tiles, new Vector3(0, bh, 0), cell));
            }

            _ = along;
            BoardCount++;
        }
    }

    /// <summary>Painted, striped, tiled or panelled walls: a swatch of the 2D finish repeated along every face that looks into a decorated room.</summary>
    private void AddWallFinishes(List<Quad> quads, Func<string, float, float, Action<CanvasItem>, int> cellFor, bool cutaway)
    {
        var sim = _view.Simulation;
        var world = _view.World;
        var roomAt = new Dictionary<GridCoord, CareSpace>();
        foreach (var space in world.ViewSpaces.Where(s => s.HasWalls).OrderByDescending(s => s.Rect.Area))
        {
            var r = space.Rect;
            for (var tx = r.X; tx < r.Right; tx++)
            {
                for (var ty = r.Y; ty < r.Bottom; ty++)
                {
                    roomAt[new GridCoord(tx, ty)] = space;
                }
            }
        }

        if (roomAt.Count == 0)
        {
            return;
        }

        var tile = TilePx;
        var full = Walls3D.FullHeight;
        var shown = cutaway ? Kit3D.H(12) / full : 1f;
        var half = (float)(WorldLayer.WallThickness / 2) + 0.006f;
        int? Swatch(GridCoord at)
        {
            if (!roomAt.TryGetValue(at, out var room) || DecorArt.LookOf(sim, room) is not { Walls: not WallFinish.Standard } look)
            {
                return null;
            }

            var (finish, colour, accent) = (look.Walls, DecorArt.WallColour(look), DecorArt.WallAccent(room, look));
            var h = WorldLayer.WallHeight;
            return cellFor($"finish:{finish}:{colour.ToHtml()}:{accent?.ToHtml()}", tile, h,
                c => DecorArt.DrawWallFace(c, new Vector2(0, h), new Vector2(tile, h), h, 0f, finish, colour, accent, Walls3D.Skirting));
        }

        void Face(Vector3 linePoint, Vector3 n, int cell)
        {
            var right = RightOf(n);
            var start = linePoint + (n * half);
            var origin = right.X + right.Z > 0 ? start : start - right;
            quads.Add(new Quad(origin, right, new Vector3(0, full * shown, 0), cell, shown));
        }

        foreach (var (x, y, horizontal) in world.WallEdges)
        {
            if (horizontal)
            {
                // A wall along y between (x, y) and (x + 1, y): the room above it and the one below.
                if (Swatch(new GridCoord(x, y - 1)) is { } north)
                {
                    Face(Kit3D.At(x, y), new Vector3(0, 0, -1), north);
                }

                if (Swatch(new GridCoord(x, y)) is { } south)
                {
                    Face(Kit3D.At(x, y), new Vector3(0, 0, 1), south);
                }
            }
            else
            {
                if (Swatch(new GridCoord(x - 1, y)) is { } west)
                {
                    Face(Kit3D.At(x, y), new Vector3(-1, 0, 0), west);
                }

                if (Swatch(new GridCoord(x, y)) is { } east)
                {
                    Face(Kit3D.At(x, y), new Vector3(1, 0, 0), east);
                }
            }
        }
    }

    // ------------------------------------------------------------------ props

    private void AddProps(MeshBatch solid, MeshBatch glass, MeshBatch glow)
    {
        foreach (var prop in Decorations.FloorPieces)
        {
            var frame = new Transform3D(new Basis(Vector3.Up, Kit3D.Yaw(prop.Dx, prop.Dy)).Scaled(Vector3.One * prop.Scale * 0.85f), Kit3D.At(prop.X, prop.Y));
            solid.Frame = glass.Frame = frame;
            if (Props3D.Floor(solid, glass, prop.Kind))
            {
                PropCount++;
            }
        }

        foreach (var top in Decorations.TopPieces)
        {
            solid.Frame = new Transform3D(new Basis(Vector3.Up, top.AlongX ? 0 : Mathf.Pi / 2), Kit3D.At(top.X, top.Y, Kit3D.H(top.TopPx)));
            Props3D.Top(solid, top.Kind);
        }

        foreach (var over in Decorations.OverheadPieces)
        {
            solid.Frame = glow.Frame = new Transform3D(Basis.Identity, Kit3D.At(over.X, over.Y));
            if (over.Theatre)
            {
                Props3D.SurgicalLights(solid, glow);
            }
            else
            {
                Props3D.MonitorBank(solid, glow);
            }
        }
    }

    /// <summary>Wet floors come and go: rebuilt only when the set of them changes.</summary>
    public void Sync()
    {
        var sim = _view.Simulation;
        var accidents = sim.FloorAccidents.Where(a => IsoDraw.OnViewFloor(a.Tile.Y)).Select(a => a.Tile).OrderBy(t => t.X).ThenBy(t => t.Y).ToList();
        var key = string.Join(";", accidents.Select(t => $"{t.X},{t.Y}")) + $"|{IsoDraw.ViewFloor}";
        if (key == _puddleKey)
        {
            return;
        }

        _puddleKey = key;
        _puddles?.QueueFree();
        _puddles = null;
        if (accidents.Count == 0)
        {
            return;
        }

        _puddles = new Node3D { Name = "Puddles" };
        AddChild(_puddles);
        var solid = new MeshBatch();
        var wet = new MeshBatch();
        foreach (var t in accidents)
        {
            Props3D.Puddle(solid, wet, t.X + 0.5, t.Y + 0.5, Math.Abs((t.X * 31) + (t.Y * 17)));
        }

        solid.AddTo(_puddles);
        wet.AddTo(_puddles, alpha: true, shadow: false);
    }
}
