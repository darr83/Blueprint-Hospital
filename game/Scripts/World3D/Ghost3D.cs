using System.Linq;
using BlueprintHospital.Hospital;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// See-through copies of what the player is about to place (chairs, decorations), built from the
/// same 3D pieces as the real thing so the preview matches what appears once placed.
/// </summary>
public partial class Ghost3D : Node3D
{
    private static readonly StandardMaterial3D Material = new()
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        Roughness = 0.9f,
        AlbedoColor = new Color(1, 1, 1, 0.85f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        // Drawn after the other see-through things (painted floors, glass) so none of them hides it.
        RenderPriority = 100,
    };

    private MeshInstance3D? _mesh;
    private string _key = "";

    /// <summary>How many ghost pieces are showing (for tests).</summary>
    public int Count { get; private set; }

    public void Sync(BuildTool tool)
    {
        var ghosts = tool.Active ? tool.Ghosts : System.Array.Empty<BuildTool.Ghost>();
        var key = string.Join(";", ghosts.Select(g => $"{g.Kind},{g.X:0.###},{g.Y:0.###},{g.Dx},{g.Dy},{g.Colour.ToHtml()}"));
        if (key == _key)
        {
            return;
        }

        _key = key;
        _mesh?.QueueFree();
        _mesh = null;
        Count = 0;
        if (ghosts.Count == 0)
        {
            return;
        }

        var batch = new MeshBatch();
        var glass = new MeshBatch();
        foreach (var ghost in ghosts)
        {
            batch.Frame = glass.Frame = new Transform3D(new Basis(Vector3.Up, Kit3D.Yaw(ghost.Dx, ghost.Dy)), Kit3D.At(ghost.X, ghost.Y));
            if (ghost.Chair)
            {
                Furnish3D.Chair(batch, ghost.Kind, ghost.Colour);
                Count++;
            }
        }

        if (batch.Build() is { } mesh)
        {
            _mesh = new MeshInstance3D { Mesh = mesh, MaterialOverride = Material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(_mesh);
        }
    }
}
