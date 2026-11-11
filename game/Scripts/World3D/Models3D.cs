using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// Models made in Blender (tools/Blender/build.py writes them to res://Art/Models/*.glb).
/// They are modelled in real metres; the game's floor is two tiles to the metre and its heights are
/// stylised, so every model is scaled by <see cref="MetreScale"/> as it is loaded. A model's parts
/// (see bhkit.py) arrive as child nodes named after the part, "Main" when it has only one.
/// </summary>
public static class Models3D
{
    public const string Folder = "res://Art/Models";

    /// <summary>The most triangles any one model may have, so a full ward stays cheap to draw.</summary>
    public const int TriangleLimit = 6000;

    /// <summary>World units per metre: two tiles across the floor, the game's height scale upwards.</summary>
    public static Vector3 MetreScale => new(2f, Kit3D.M(1), 2f);

    private static readonly Dictionary<string, PackedScene?> Scenes = new();

    /// <summary>The names of every Blender-made model in the game.</summary>
    public static IReadOnlyList<string> All()
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in DirAccess.GetFilesAt(Folder))
        {
            // In an exported game only the ".import" / ".remap" stubs are listed.
            var name = file.Replace(".import", "", StringComparison.Ordinal).Replace(".remap", "", StringComparison.Ordinal);
            if (name.EndsWith(".glb", StringComparison.Ordinal))
            {
                names.Add(name[..^4]);
            }
        }

        return names.ToList();
    }

    public static bool Has(string name) => Scene(name) is not null;

    /// <summary>
    /// A new copy of a model, sized for the world, standing on the floor at its parent's origin.
    /// Null if there is no such model.
    /// </summary>
    public static Node3D? Make(string name)
    {
        if (Scene(name) is not { } scene)
        {
            return null;
        }

        var model = scene.Instantiate<Node3D>();
        model.Name = name;
        model.Scale = MetreScale;
        return model;
    }

    /// <summary>How many triangles a model (or anything else) is drawn with.</summary>
    public static int Triangles(Node root)
    {
        var total = 0;
        foreach (var instance in root.FindChildren("*", nameof(MeshInstance3D), true, false).Append(root).OfType<MeshInstance3D>())
        {
            if (instance.Mesh is not { } mesh)
            {
                continue;
            }

            for (var s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                var arrays = mesh.SurfaceGetArrays(s);
                var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                total += (indices.Length > 0 ? indices.Length : arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length) / 3;
            }
        }

        return total;
    }

    private static PackedScene? Scene(string name)
    {
        if (!Scenes.TryGetValue(name, out var scene))
        {
            var path = $"{Folder}/{name}.glb";
            scene = ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;
            Scenes[name] = scene;
        }

        return scene;
    }
}
