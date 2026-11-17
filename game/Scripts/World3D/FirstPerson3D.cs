using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// What the world needs only when seen from inside (Be A Doctor): a ceiling over every room and
/// corridor, and a trail of arrows on the floor leading to where the player is going.
/// </summary>
public partial class FirstPerson3D : Node3D
{
    private HospitalSimulation _sim = null!;
    private readonly Node3D _ceiling = new() { Name = "Ceiling" };
    private readonly MeshInstance3D _trail = new() { Name = "Trail", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    private readonly MeshInstance3D _marker = new() { Name = "Marker", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
    private StandardMaterial3D _ceilingMaterial = null!;
    private string _trailSignature = string.Empty;

    /// <summary>How many arrows are on the floor now (for the smoke test).</summary>
    public int ArrowCount { get; private set; }

    /// <summary>How many rooms and corridors have a ceiling over them now (for the smoke test).</summary>
    public int CeilingCount => _ceiling.GetChildCount();

    /// <summary>
    /// How much taller the rooms are in first person than the walls drawn from above, in metres. The
    /// extra is a plain band of wall above the usual walls and doors, with the ceiling on top of it.
    /// </summary>
    public static double ExtraHeightMetres { get; set; } = 0.3;

    /// <summary>The band of wall above the usual walls: plain white, like a bulkhead under the ceiling.</summary>
    private static readonly Color Coving = new("#D3D9DF");

    private WorldLayer? _world;

    public void Initialise(HospitalSimulation simulation, WorldLayer? world = null)
    {
        _world = world;
        _sim = simulation;
        AddChild(_ceiling);
        _trail.MaterialOverride = new StandardMaterial3D { AlbedoColor = UI.UiStyle.Blue, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        AddChild(_trail);
        _marker.Mesh = new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0.42f, Height = 0.02f, RadialSegments = 32 };
        _marker.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(UI.UiStyle.Blue, 0.55f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        AddChild(_marker);
        _ceilingMaterial = CeilingMaterial(false);
        _ceilingShaded = CeilingMaterial(true);
    }

    private StandardMaterial3D _ceilingShaded = null!;

    /// <summary>
    /// Ceiling tiles: pale squares with a thin grid, mapped in world space so every room lines up.
    /// With surface textures on they are shaded, with the fissured mineral-fibre tile as a normal map,
    /// and the light panels light them; otherwise evenly lit as before.
    /// </summary>
    private static StandardMaterial3D CeilingMaterial(bool shaded)
    {
        var image = Image.CreateEmpty(64, 64, true, Image.Format.Rgb8);
        image.Fill(new Color("#DFE4E9"));
        for (var i = 0; i < 64; i++)
        {
            image.SetPixel(i, 0, new Color("#C3CBD3"));
            image.SetPixel(0, i, new Color("#C3CBD3"));
            image.SetPixel(i, 1, new Color("#D3DAE0"));
            image.SetPixel(1, i, new Color("#D3DAE0"));
        }

        image.GenerateMipmaps();
        var material = new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(image),
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(1f / 0.6f, 1f / 0.6f, 1f / 0.6f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        const string normal = SurfaceDetail.Folder + "/6_ceiling_normal.png";
        if (shaded && ResourceLoader.Exists(normal))
        {
            material.ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel;
            material.Roughness = 0.95f;
            material.NormalEnabled = true;
            material.NormalTexture = GD.Load<Texture2D>(normal);
            material.NormalScale = 0.8f;
            // A little glow of its own, as light bounces off the floor: lit only by the panels, the
            // ceiling read grey and gloomy next to the evenly lit ceiling it replaces.
            material.EmissionEnabled = true;
            material.Emission = Colors.White;
            material.EmissionTexture = material.AlbedoTexture;
            material.EmissionEnergyMultiplier = 0.1f;
        }

        return material;
    }

    /// <summary>Puts a ceiling over every room and corridor on the floor in view.</summary>
    public void RebuildCeiling()
    {
        foreach (var child in _ceiling.GetChildren())
        {
            child.QueueFree();
            _ceiling.RemoveChild(child);
        }

        var lit = SurfaceDetail.Enabled;
        var ceiling = lit ? _ceilingShaded : _ceilingMaterial;
        var extra = Kit3D.M(ExtraHeightMetres);
        var height = Walls3D.FullHeight + extra + 0.01f;
        if (extra > 0 && _world is not null)
        {
            // The band of wall between the tops of the usual walls (and doorways) and the raised ceiling.
            const float t = (float)WorldLayer.WallThickness;
            var band = new MeshBatch();
            foreach (var (x, y, horizontal) in _world.WallEdges.Concat(_world.DoorEdges))
            {
                if (horizontal)
                {
                    band.Block(x - (t / 2), y - (t / 2), 1 + t, t, extra, Coving, Walls3D.FullHeight);
                }
                else
                {
                    band.Block(x - (t / 2), y - (t / 2), t, 1 + t, extra, Coving, Walls3D.FullHeight);
                }
            }

            if (band.AddTo(_ceiling, shadow: false) is { } bulkhead)
            {
                // Evenly lit, like the ceiling it meets: shaded, it read as a dark beam round every room.
                bulkhead.MaterialOverride = new StandardMaterial3D { AlbedoColor = Coving, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
            }
        }

        // One ceiling over everything indoors on this floor, rooms or not, so no sky shows from inside.
        // Runs of indoor tiles along each row become one strip each.
        var site = _sim.Site;
        var baseY = IsoDraw.ViewFloor * site.FloorStride;
        var indoor = new HashSet<(int X, int Y)>();
        for (var y = 0; y < site.Length; y++)
        {
            var from = -1;
            for (var x = 0; x <= site.Width; x++)
            {
                var inside = x < site.Width && site.IsIndoors(new GridCoord(x, baseY + y));
                if (inside)
                {
                    indoor.Add((x, baseY + y));
                }

                if (inside && from < 0)
                {
                    from = x;
                }
                else if (!inside && from >= 0)
                {
                    var width = x - from;
                    _ceiling.AddChild(new MeshInstance3D
                    {
                        Mesh = new PlaneMesh { Size = new Vector2(width, 1), FlipFaces = true },
                        MaterialOverride = ceiling,
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                        Position = new Vector3(from + (width / 2f), height, baseY + y + 0.5f),
                    });
                    from = -1;
                }
            }
        }

        // Light panels: one ceiling tile in every third, both ways, wherever all of it is indoors.
        var lights = 0;
        var lamps = 0;
        for (var cy = 0; (cy + 1) * CeilingTile <= site.Length; cy += LightEvery)
        {
            for (var cx = 0; (cx + 1) * CeilingTile <= site.Width; cx += LightEvery)
            {
                var (x0, y0) = (cx * CeilingTile, baseY + (cy * CeilingTile));
                var corners = new[] { (x0 + 0.05, y0 + 0.05), (x0 + CeilingTile - 0.05, y0 + 0.05), (x0 + 0.05, y0 + CeilingTile - 0.05), (x0 + CeilingTile - 0.05, y0 + CeilingTile - 0.05) };
                if (!corners.All(c => indoor.Contains(((int)Math.Floor(c.Item1), (int)Math.Floor(c.Item2)))))
                {
                    continue;
                }

                _ceiling.AddChild(new MeshInstance3D
                {
                    Mesh = _lightMesh,
                    MaterialOverride = _lightMaterial,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    Position = new Vector3((float)(x0 + (CeilingTile / 2)), height - 0.012f, (float)(y0 + (CeilingTile / 2))),
                });
                if (lit)
                {
                    // Each panel lights the room under it: pools of light on the floor and the
                    // ceiling tiles round it, as on a real ward. No shadows, so dozens stay cheap.
                    _ceiling.AddChild(new OmniLight3D
                    {
                        Position = new Vector3((float)(x0 + (CeilingTile / 2)), height - 0.25f, (float)(y0 + (CeilingTile / 2))),
                        OmniRange = 4.5f,
                        OmniAttenuation = 1.2f,
                        LightEnergy = 0.28f,
                        LightColor = new Color("#FFF6EA"),
                        ShadowEnabled = false,
                        LightSpecular = 0.6f,
                    });
                    lamps++;
                }

                lights++;
            }
        }

        LightCount = lights;
        LampCount = lamps;
    }

    /// <summary>How many of the light panels give off light (all of them with surface textures on, for the smoke test).</summary>
    public int LampCount { get; private set; }

    /// <summary>A ceiling tile is 60 cm square: 1.2 grid tiles.</summary>
    private const double CeilingTile = 1.2;
    private const int LightEvery = 3;
    private readonly PlaneMesh _lightMesh = new() { Size = new Vector2((float)CeilingTile - 0.14f, (float)CeilingTile - 0.14f), FlipFaces = true };
    private readonly StandardMaterial3D _lightMaterial = new()
    {
        AlbedoColor = new Color("#FFFFFF"),
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// <summary>How many light panels are in the ceiling now (for the smoke test).</summary>
    public int LightCount { get; private set; }

    /// <summary>
    /// Lays arrows along <paramref name="route"/> (tiles, in walking order) on the floor in view, with a
    /// disc where it ends. An empty route clears them.
    /// </summary>
    public void ShowRoute(IReadOnlyList<GridCoord> route)
    {
        var onFloor = route.Where(t => _sim.FloorOf(t) == IsoDraw.ViewFloor).ToList();
        var signature = onFloor.Count == 0 ? string.Empty : $"{IsoDraw.ViewFloor}:{string.Join(";", onFloor.Select(t => $"{t.X},{t.Y}"))}";
        if (signature == _trailSignature)
        {
            return;
        }

        _trailSignature = signature;
        ArrowCount = 0;
        _marker.Visible = false;
        if (onFloor.Count < 2)
        {
            _trail.Mesh = null;
            if (onFloor.Count == 1 && route.Count > 0 && route[^1] == onFloor[0])
            {
                PlaceMarker(onFloor[0]);
            }

            return;
        }

        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        const float spacing = 1.25f;
        const float y = 0.03f;
        var carried = spacing * 0.5f;
        for (var i = 0; i + 1 < onFloor.Count; i++)
        {
            var a = new Vector2(onFloor[i].X + 0.5f, onFloor[i].Y + 0.5f);
            var b = new Vector2(onFloor[i + 1].X + 0.5f, onFloor[i + 1].Y + 0.5f);
            var length = a.DistanceTo(b);
            if (length < 0.01f || length > 6f && _sim.Nav.Neighbours(onFloor[i]).Any(n => n.IsLink && n.Tile == onFloor[i + 1]))
            {
                continue;
            }

            var dir = (b - a) / length;
            var side = new Vector2(-dir.Y, dir.X);
            for (var d = carried; d < length; d += spacing)
            {
                // A chevron pointing along the route: two slanted bars meeting at the tip.
                var tip = a + (dir * (d + 0.18f));
                foreach (var s in new[] { -1f, 1f })
                {
                    var outer = tip - (dir * 0.34f) + (side * s * 0.3f);
                    Quad(tool, tip, tip - (dir * 0.14f), outer - (dir * 0.14f), outer, y);
                }

                ArrowCount++;
            }

            carried = (carried + (spacing * MathF.Ceiling((length - carried) / spacing))) - length;
            carried = Mathf.Clamp(carried, 0, spacing);
        }

        _trail.Mesh = ArrowCount > 0 ? tool.Commit() : null;
        if (route[^1] == onFloor[^1])
        {
            PlaceMarker(onFloor[^1]);
        }
    }

    private void PlaceMarker(GridCoord tile)
    {
        _marker.Position = new Vector3(tile.X + 0.5f, 0.03f, tile.Y + 0.5f);
        _marker.Visible = true;
    }

    private static void Quad(SurfaceTool tool, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float y)
    {
        Vector3 P(Vector2 v) => new(v.X, y, v.Y);
        foreach (var v in new[] { a, b, c, a, c, d, a, c, b, a, d, c })
        {
            tool.SetNormal(Vector3.Up);
            tool.AddVertex(P(v));
        }
    }
}
