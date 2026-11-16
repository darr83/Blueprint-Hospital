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

    public void Initialise(HospitalSimulation simulation)
    {
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
        _ceilingMaterial = CeilingMaterial();
    }

    /// <summary>Ceiling tiles: pale squares with a thin grid, mapped in world space so every room lines up.</summary>
    private static StandardMaterial3D CeilingMaterial()
    {
        var image = Image.CreateEmpty(64, 64, true, Image.Format.Rgb8);
        image.Fill(new Color("#E9EDF1"));
        for (var i = 0; i < 64; i++)
        {
            image.SetPixel(i, 0, new Color("#CAD2DA"));
            image.SetPixel(0, i, new Color("#CAD2DA"));
            image.SetPixel(i, 1, new Color("#DBE1E7"));
            image.SetPixel(1, i, new Color("#DBE1E7"));
        }

        image.GenerateMipmaps();
        return new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(image),
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
    }

    /// <summary>Puts a ceiling over every room and corridor on the floor in view.</summary>
    public void RebuildCeiling()
    {
        foreach (var child in _ceiling.GetChildren())
        {
            child.QueueFree();
            _ceiling.RemoveChild(child);
        }

        var height = Walls3D.FullHeight + 0.01f;
        foreach (var space in _sim.Spaces.Where(s => _sim.FloorOf(s) == IsoDraw.ViewFloor))
        {
            var r = space.Rect;
            _ceiling.AddChild(new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = new Vector2(r.Width, r.Length), FlipFaces = true },
                MaterialOverride = _ceilingMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(r.X + (r.Width / 2f), height, r.Y + (r.Length / 2f)),
            });
        }
    }

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
