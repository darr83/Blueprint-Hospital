using System;
using System.Collections.Generic;
using BlueprintHospital.Hospital;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// Units and shared building blocks for the 3D world.
/// One world unit across the floor is one grid tile (half a metre). Heights are scaled so a 3D
/// object lines up on screen with the same object in the old 2D isometric drawing: the camera looks
/// down at 30° from 45° round, so a tile edge and a pixel of "2D height" match the 2:1 projection.
/// </summary>
public static class Kit3D
{
    /// <summary>Screen pixels per world unit at zoom 1, measured in the camera's view plane (32·√2).</summary>
    public const float PxPerUnit = 45.254834f;

    /// <summary>Screen pixels one unit of height covers at zoom 1 (PxPerUnit · cos 30°).</summary>
    public const float HeightPxPerUnit = 39.191836f;

    /// <summary>World height of something the 2D art drew <paramref name="px"/> pixels tall.</summary>
    public static float H(double px) => (float)(px / HeightPxPerUnit);

    /// <summary>World height of something <paramref name="metres"/> tall, at the game's stylised height scale.</summary>
    public static float M(double metres) => (float)(metres * IsoDraw.PixelsPerMetre / HeightPxPerUnit);

    /// <summary>A floor point (grid x, grid y) at a height in world units.</summary>
    public static Vector3 At(double x, double y, float height = 0) => new((float)x, height, (float)y);

    // ------------------------------------------------------------------ materials

    private static readonly Dictionary<(Color, int), StandardMaterial3D> Materials = new();

    /// <summary>A plain painted surface. Shared by colour, so thousands of parts cost a handful of materials.</summary>
    public static StandardMaterial3D Mat(Color colour, Finish finish = Finish.Matt)
    {
        var key = (colour, (int)finish);
        if (Materials.TryGetValue(key, out var m))
        {
            return m;
        }

        m = new StandardMaterial3D
        {
            AlbedoColor = colour,
            Roughness = finish switch { Finish.Gloss => 0.35f, Finish.Metal => 0.35f, Finish.Satin => 0.6f, _ => 0.85f },
            Metallic = finish == Finish.Metal ? 0.55f : 0f,
        };
        if (finish == Finish.Glow)
        {
            m.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        }

        if (finish == Finish.Screen)
        {
            m.EmissionEnabled = true;
            m.Emission = colour;
            m.EmissionEnergyMultiplier = 0.6f;
            m.Roughness = 0.3f;
        }

        if (colour.A < 0.999f)
        {
            m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        }

        Materials[key] = m;
        return m;
    }

    private static StandardMaterial3D? _vertexColours;

    /// <summary>The material for merged meshes whose colour is carried per vertex.</summary>
    public static StandardMaterial3D VertexColours => _vertexColours ??= new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        Roughness = 0.85f,
    };

    private static StandardMaterial3D? _vertexColoursAlpha;

    /// <summary>Vertex-coloured and see-through where the colour is.</summary>
    public static StandardMaterial3D VertexColoursAlpha => _vertexColoursAlpha ??= new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        Roughness = 0.9f,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    };

    // ------------------------------------------------------------------ shapes

    public static readonly BoxMesh UnitBox = new() { Size = Vector3.One };

    public static readonly SphereMesh UnitSphere = new() { Radius = 0.5f, Height = 1f, RadialSegments = 18, Rings = 9 };

    /// <summary>A coarse sphere for small details (eyes, buttons), where a smooth one would be wasted.</summary>
    public static readonly SphereMesh SmallSphere = new() { Radius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 4 };

    /// <summary>A coarse cylinder for thin rods and rails.</summary>
    public static readonly CylinderMesh SmallCylinder = new() { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 1 };

    public static readonly CylinderMesh UnitCylinder = new() { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 18, Rings = 1 };

    public static readonly CylinderMesh UnitCone = new() { TopRadius = 0.0f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 18, Rings = 1 };

    /// <summary>A capsule one unit tall and one across (scale it to make limbs and rounded bodies).</summary>
    public static readonly CapsuleMesh UnitCapsule = new() { Radius = 0.5f, Height = 1f, RadialSegments = 14, Rings = 4 };

    public static readonly QuadMesh UnitQuad = new() { Size = Vector2.One };

    public static readonly TorusMesh UnitRing = new() { InnerRadius = 0.4f, OuterRadius = 0.5f, Rings = 18, RingSegments = 8 };

    /// <summary>A part: <paramref name="mesh"/> stretched to <paramref name="size"/> and centred at <paramref name="centre"/>.</summary>
    public static MeshInstance3D Part(Node3D parent, Mesh mesh, Vector3 centre, Vector3 size, Color colour, Finish finish = Finish.Matt, bool shadow = true)
    {
        var part = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = Mat(colour, finish),
            Position = centre,
            Scale = size,
            CastShadow = shadow && colour.A > 0.99f ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(part);
        return part;
    }

    public static MeshInstance3D Box(Node3D parent, Vector3 centre, Vector3 size, Color colour, Finish finish = Finish.Matt, bool shadow = true) =>
        Part(parent, UnitBox, centre, size, colour, finish, shadow);

    /// <summary>A box standing on the floor: its footprint corner at (x, y), <paramref name="w"/> by <paramref name="l"/> tiles, from height <paramref name="from"/> up by <paramref name="height"/>.</summary>
    public static MeshInstance3D Block(Node3D parent, double x, double y, double w, double l, float height, Color colour, float from = 0, Finish finish = Finish.Matt) =>
        Box(parent, new Vector3((float)(x + (w / 2)), from + (height / 2), (float)(y + (l / 2))), new Vector3((float)w, height, (float)l), colour, finish);

    public static MeshInstance3D Ball(Node3D parent, Vector3 centre, Vector3 size, Color colour, Finish finish = Finish.Matt, bool shadow = true) =>
        Part(parent, UnitSphere, centre, size, colour, finish, shadow);

    /// <summary>An upright cylinder centred at <paramref name="centre"/>.</summary>
    public static MeshInstance3D Post(Node3D parent, Vector3 centre, float radius, float height, Color colour, Finish finish = Finish.Matt, bool shadow = true) =>
        Part(parent, UnitCylinder, centre, new Vector3(radius * 2, height, radius * 2), colour, finish, shadow);

    /// <summary>A cylinder lying along the x axis (wheels, rails, rolls).</summary>
    public static MeshInstance3D Rod(Node3D parent, Vector3 centre, float radius, float length, Color colour, Vector3 along, Finish finish = Finish.Matt)
    {
        var part = Part(parent, UnitCylinder, centre, Vector3.One, colour, finish);
        part.Basis = AlongBasis(along.Normalized()) * Basis.FromScale(new Vector3(radius * 2, length, radius * 2));
        return part;
    }

    /// <summary>A capsule between two points, for limbs.</summary>
    public static MeshInstance3D Limb(Node3D parent, Vector3 from, Vector3 to, float radius, Color colour, Finish finish = Finish.Matt)
    {
        var part = Part(parent, UnitCapsule, (from + to) / 2, Vector3.One, colour, finish);
        part.Basis = AlongBasis((to - from).Normalized()) * Basis.FromScale(new Vector3(radius * 2, (to - from).Length() + (radius * 2), radius * 2));
        return part;
    }

    /// <summary>A rotation that turns the y axis to <paramref name="dir"/>.</summary>
    public static Basis AlongBasis(Vector3 dir)
    {
        if (dir.LengthSquared() < 1e-6f)
        {
            return Basis.Identity;
        }

        var up = Vector3.Up;
        if (Math.Abs(dir.Dot(up)) > 0.999f)
        {
            return dir.Y > 0 ? Basis.Identity : new Basis(Vector3.Right, Mathf.Pi);
        }

        var axis = up.Cross(dir).Normalized();
        var angle = up.AngleTo(dir);
        return new Basis(axis, angle);
    }

    /// <summary>Rotation about the vertical for something facing grid direction (dx, dy) (its own +z faces that way).</summary>
    public static float Yaw(double dx, double dy) => (float)Math.Atan2(dx, dy);

    /// <summary>A flat disc lying on the floor (shadows, puddles, rings).</summary>
    public static MeshInstance3D Disc(Node3D parent, Vector3 centre, float rx, float rz, Color colour)
    {
        var part = Part(parent, UnitCylinder, centre, new Vector3(rx * 2, 0.004f, rz * 2), colour, Finish.Matt, shadow: false);
        return part;
    }
}

public enum Finish
{
    Matt,
    Satin,
    Gloss,
    Metal,
    Screen,
    Glow,
}
