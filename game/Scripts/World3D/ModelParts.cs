using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// The triangles of the Blender-made models, read once so a <see cref="MeshBatch"/> can merge a model
/// (or one named part of it) into its single mesh. Surfaces in a material called "Tint" take the
/// colour the game asks for; any other surface keeps the colour it was made with.
/// </summary>
public static class ModelParts
{
    /// <summary>One surface of a part: its triangles, and its own colour when it is not tinted.</summary>
    public sealed record Surface(Vector3[] Vertices, Vector3[] Normals, int[] Indices, Color? Fixed);

    private static readonly Dictionary<string, Dictionary<string, Surface[]>> Models = new(StringComparer.Ordinal);

    /// <summary>The surfaces of one part of a model, or null if there is no such model or part.</summary>
    public static Surface[]? Part(string model, string part) => Load(model).TryGetValue(part, out var found) ? found : null;

    /// <summary>The names of a model's parts.</summary>
    public static IReadOnlyCollection<string> Names(string model) => Load(model).Keys;

    private static Dictionary<string, Surface[]> Load(string model)
    {
        if (Models.TryGetValue(model, out var parts))
        {
            return parts;
        }

        parts = new Dictionary<string, Surface[]>(StringComparer.Ordinal);
        Models[model] = parts;
        var path = $"{Models3D.Folder}/{model}.glb";
        if (!ResourceLoader.Exists(path))
        {
            GD.PushWarning($"Model missing: {path}");
            return parts;
        }

        var scene = GD.Load<PackedScene>(path).Instantiate<Node3D>();
        foreach (var instance in scene.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        {
            if (instance.Mesh is not { } mesh)
            {
                continue;
            }

            // Where the part sits in the model (the scene is not in the tree, so walk up by hand).
            var xf = Transform3D.Identity;
            for (Node? n = instance; n is not null && n != scene; n = n.GetParent())
            {
                if (n is Node3D n3)
                {
                    xf = n3.Transform * xf;
                }
            }

            var normalBasis = xf.Basis.Inverse().Transposed();
            var surfaces = new List<Surface>();
            for (var s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                var arrays = mesh.SurfaceGetArrays(s);
                var material = mesh.SurfaceGetMaterial(s) as BaseMaterial3D;
                var tinted = material is null || material.ResourceName.StartsWith("Tint", StringComparison.Ordinal);
                surfaces.Add(new Surface(
                    arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Select(v => xf * v).ToArray(),
                    arrays[(int)Mesh.ArrayType.Normal].AsVector3Array().Select(n => (normalBasis * n).Normalized()).ToArray(),
                    arrays[(int)Mesh.ArrayType.Index].AsInt32Array(),
                    tinted ? null : material!.AlbedoColor));
            }

            parts[instance.Name.ToString()] = surfaces.ToArray();
        }

        scene.Free();
        return parts;
    }
}
