using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// The floors inside: every room's floor colour and finish, the wayfinding lines, lift and stair
/// markings and the ambulance bay boxes are painted flat (from above) into a texture, which lies on
/// the 3D floor of the storey in view. It is repainted only when the layout or the floor changes.
/// </summary>
public partial class FloorPainter : Node3D
{
    /// <summary>Texture pixels per tile.</summary>
    private const int TilePixels = (int)IsoDraw.PlanTile;

    private HospitalSimulation _sim = null!;
    private SubViewport _viewport = null!;
    private Node2D _canvas = null!;
    private DepartmentRenderer _rooms = null!;
    private GridLines _grid = null!;
    private MeshInstance3D _quad = null!;
    private StandardMaterial3D _material = null!;
    private ShaderMaterial? _detailed;
    private Texture2D? _painted;

    /// <summary>Which finish each tile is laid with (one texel per tile), for the surface detail.</summary>
    public Image? Finishes { get; private set; }

    /// <summary>Whether the floor is drawn with surface detail now.</summary>
    public bool Detailed => _quad.MaterialOverride is ShaderMaterial;

    private void SetPainted(Texture2D texture)
    {
        _painted = texture;
        _material.AlbedoTexture = texture;
        _detailed?.SetShaderParameter("painted", texture);
    }

    /// <summary>The surface each tile of the area is laid with: the finish of the room on it, sealed vinyl in corridors.</summary>
    private Image PaintFinishes()
    {
        var image = Image.CreateEmpty(_area.Size.X, _area.Size.Y, false, Image.Format.R8);
        var shift = (int)Math.Round(IsoDraw.FloorShift);
        foreach (var space in _sim.Spaces.Where(s => IsoDraw.OnViewFloor(s.Rect.Y)).OrderByDescending(s => s.Rect.Area))
        {
            var kind = space.IsShaft ? SurfaceKind.Metal : SurfaceDetail.Of(DecorArt.LookOf(_sim, space).Floor);
            var value = new Color((int)kind / 255f, 0, 0);
            var r = space.Rect;
            for (var ty = r.Y; ty < r.Bottom; ty++)
            {
                var py = ty - shift - _area.Position.Y;
                for (var tx = r.X; tx < r.Right; tx++)
                {
                    var px = tx - _area.Position.X;
                    if (px >= 0 && py >= 0 && px < _area.Size.X && py < _area.Size.Y)
                    {
                        image.SetPixel(px, py, value);
                    }
                }
            }
        }

        return image;
    }
    private Rect2I _area;
    private bool _waiting;

    public bool ShowGrid
    {
        get => _grid.Visible;
        set
        {
            _grid.Visible = value;
            Repaint();
        }
    }

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        _viewport = new SubViewport
        {
            Name = "FloorCanvas",
            TransparentBg = true,
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Linear,
            Msaa2D = Viewport.Msaa.Msaa2X,
        };
        AddChild(_viewport);
        _canvas = new Node2D { Name = "Canvas" };
        _viewport.AddChild(_canvas);
        _rooms = new DepartmentRenderer { Name = "Rooms", Flat = true };
        _canvas.AddChild(_rooms);
        _rooms.Initialise(simulation);
        _grid = new GridLines { Name = "Grid", Site = simulation.Site };
        _canvas.AddChild(_grid);

        _material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass,
            Roughness = 0.75f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        _quad = new MeshInstance3D
        {
            Name = "Floor",
            Mesh = new PlaneMesh { Size = Vector2.One },
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_quad);
    }

    /// <summary>Paints the floors of the storey in view again (after a build, a decorate or a change of floor).</summary>
    public void Repaint()
    {
        if (_sim is null)
        {
            return;
        }

        // The building's outline on this storey, plus the ambulance bay outside it on the ground floor.
        var rects = _sim.Site.Buildings.SelectMany(b => b.Areas).ToList();
        if (IsoDraw.ViewFloor == 0)
        {
            rects.AddRange(_sim.AmbulanceBays);
        }

        if (rects.Count == 0)
        {
            _quad.Visible = false;
            return;
        }

        var x0 = rects.Min(r => r.X) - 1;
        var y0 = rects.Min(r => r.Y) - 1;
        var x1 = rects.Max(r => r.Right) + 1;
        var y1 = rects.Max(r => r.Bottom) + 1;
        _area = new Rect2I(x0, y0, x1 - x0, y1 - y0);

        var size = new Vector2I(Math.Min(_area.Size.X * TilePixels, 16384), Math.Min(_area.Size.Y * TilePixels, 16384));
        _viewport.Size = size;
        _canvas.Position = new Vector2(-_area.Position.X * TilePixels, -_area.Position.Y * TilePixels);
        _rooms.Repaint();
        _grid.QueueRedraw();
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;

        // The quad sits over the same tiles on the floor in view (the parent shifts floors into place).
        var shift = (float)IsoDraw.FloorShift;
        _quad.Position = new Vector3(_area.Position.X + (_area.Size.X / 2f), 0.03f, _area.Position.Y + (_area.Size.Y / 2f) + shift);
        _quad.Scale = new Vector3(_area.Size.X, 1, _area.Size.Y);
        _quad.Visible = true;

        // Surface detail: each tile's finish, and the detailed material (or the plain one, with Surface textures off).
        Finishes = PaintFinishes();
        if (SurfaceDetail.Enabled)
        {
            _detailed ??= SurfaceDetail.Floor();
            _detailed.SetShaderParameter("finishes", ImageTexture.CreateFromImage(Finishes));
            if (_painted is not null)
            {
                _detailed.SetShaderParameter("painted", _painted);
            }

            _quad.MaterialOverride = _detailed;
        }
        else
        {
            _quad.MaterialOverride = _material;
        }

        // Until the new texture has been copied with mipmaps, show the live one.
        SetPainted(_viewport.GetTexture());
        if (!_waiting)
        {
            _waiting = true;
            RenderingServer.FramePostDraw += CopyTexture;
        }
    }

    /// <summary>Copies the painted floor into a texture with mipmaps, so it stays crisp and calm when zoomed out.</summary>
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
        SetPainted(ImageTexture.CreateFromImage(image));
    }

    /// <summary>Grid lines every metre inside the buildings, for building (G).</summary>
    private sealed partial class GridLines : Node2D
    {
        public SiteMap Site { get; set; } = null!;

        public override void _Draw()
        {
            using var flat = IsoDraw.PushFlat();
            var line = new Color(0.25f, 0.35f, 0.45f, 0.22f);
            var tilesPerMetre = (int)(1 / Units.MetresPerTile);
            var shift = IsoDraw.FloorShift;
            foreach (var r in Site.Buildings.SelectMany(b => b.Areas))
            {
                for (var x = r.X; x <= r.Right; x += tilesPerMetre)
                {
                    DrawLine(IsoDraw.ToScreen(x, r.Y + shift), IsoDraw.ToScreen(x, r.Bottom + shift), line, 1.5f);
                }

                for (var y = r.Y; y <= r.Bottom; y += tilesPerMetre)
                {
                    DrawLine(IsoDraw.ToScreen(r.X, y + shift), IsoDraw.ToScreen(r.Right, y + shift), line, 1.5f);
                }
            }
        }
    }
}
