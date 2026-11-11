using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// The Blender-made body parts people are assembled from (tools/Blender/models/people_kit.py).
/// Each part fills a box one unit across, as the plain balls and capsules did, so a figure asks for
/// "head" or "sleeve" at a place and size and gets the modelled shape, painted in its own colour.
/// Surfaces in the material "Tint" take that colour; any other surface keeps the colour it was made with.
/// </summary>
public static class PeopleKit
{
    private const string Path = "res://Art/Models/people_kit.glb";

    /// <summary>One surface of a part: its triangles, and its own colour when it is not tinted.</summary>
    public sealed record Surface(Vector3[] Vertices, Vector3[] Normals, int[] Indices, Color? Fixed);

    private static Dictionary<string, Surface[]>? _parts;

    /// <summary>The surfaces of a part, or null if the kit has no such part.</summary>
    public static Surface[]? Part(string name)
    {
        _parts ??= Load();
        return _parts.TryGetValue(name, out var part) ? part : null;
    }

    public static IReadOnlyCollection<string> Names => (_parts ??= Load()).Keys;

    private static Dictionary<string, Surface[]> Load()
    {
        var parts = new Dictionary<string, Surface[]>(StringComparer.Ordinal);
        if (!ResourceLoader.Exists(Path))
        {
            GD.PushWarning($"People kit missing: {Path}");
            return parts;
        }

        var scene = GD.Load<PackedScene>(Path).Instantiate<Node3D>();
        foreach (var instance in scene.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        {
            if (instance.Mesh is not { } mesh)
            {
                continue;
            }

            var surfaces = new List<Surface>();
            for (var s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                var arrays = mesh.SurfaceGetArrays(s);
                var material = mesh.SurfaceGetMaterial(s) as BaseMaterial3D;
                var tinted = material is null || material.ResourceName.StartsWith("Tint", StringComparison.Ordinal);
                surfaces.Add(new Surface(
                    arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array(),
                    arrays[(int)Mesh.ArrayType.Normal].AsVector3Array(),
                    arrays[(int)Mesh.ArrayType.Index].AsInt32Array(),
                    tinted ? null : material!.AlbedoColor));
            }

            parts[instance.Name.ToString()] = surfaces.ToArray();
        }

        scene.Free();
        return parts;
    }
}
