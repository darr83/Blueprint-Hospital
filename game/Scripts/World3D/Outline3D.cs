using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// The outline pass: a quad that rides on the camera and fills the screen, drawing thin dark edges
/// where depth or facing jumps (Shaders/outline.gdshader). Settings > Display turns it off.
/// </summary>
public static class Outline3D
{
    public static MeshInstance3D Make() => new()
    {
        Name = "Outlines",
        Mesh = new QuadMesh { Size = Vector2.One },
        MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/outline.gdshader") },
        // The shader places it over the whole screen, so it must never be culled.
        ExtraCullMargin = 16384,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Position = new Vector3(0, 0, -1),
    };
}
