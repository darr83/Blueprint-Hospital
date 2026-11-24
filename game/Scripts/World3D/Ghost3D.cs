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
    private MeshInstance3D? _glass;
    private string _key = "";

    /// <summary>How many ghost pieces are showing (for tests).</summary>
    public int Count { get; private set; }

    public void Sync(BuildTool tool)
    {
        var ghosts = tool.Active ? tool.Ghosts : System.Array.Empty<BuildTool.Ghost>();
        var key = string.Join(";", ghosts.Select(g => $"{g.Kind},{g.X:0.###},{g.Y:0.###},{g.Dx},{g.Dy},{g.Colour.ToHtml()},{g.Wall},{g.Length}"));
        if (key == _key)
        {
            return;
        }

        _key = key;
        _mesh?.QueueFree();
        _mesh = null;
        _glass?.QueueFree();
        _glass = null;
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
            else if (ghost.Wall)
            {
                WallItem(batch, ghost.Kind, ghost.Length);
                Count++;
            }
            else if (Props3D.Floor(batch, glass, ghost.Kind) || PublicProps3D.Floor(batch, glass, ghost.Kind, ghost.Colour))
            {
                Count++;
            }
        }

        foreach (var piece in glass.Build() is { } see ? new[] { see } : System.Array.Empty<ArrayMesh>())
        {
            _glass?.QueueFree();
            _glass = new MeshInstance3D { Mesh = piece, MaterialOverride = Material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(_glass);
        }

        if (batch.Build() is { } mesh)
        {
            _mesh = new MeshInstance3D { Mesh = mesh, MaterialOverride = Material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(_mesh);
        }
    }

    /// <summary>A wall item about to be hung: a panel on the wall behind the tile (the real one is painted onto the wall).</summary>
    private static void WallItem(MeshBatch b, string kind, int length)
    {
        static float M(double metres) => Kit3D.H(metres * 28);
        var w = length == 1 ? 0.6f : length - 0.4f;
        var (face, from, to) = kind switch
        {
            "Tv" => (new Color("#1B1F24"), 1.45, 2.05),
            "Clock" => (new Color("#FFFFFF"), 1.85, 2.15),
            "Noticeboard" => (new Color("#C9A27A"), 1.1, 1.8),
            _ => (new Color("#F3E5D8"), 1.2, 1.8),
        };
        b.Box(new Vector3(0, M((from + to) / 2), -0.42f), new Vector3(w, M(to - from), 0.05f), face);
    }
}
