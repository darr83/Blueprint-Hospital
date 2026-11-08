using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Data;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Reads game data through Godot's virtual file system so it works both in the editor
/// and in exported builds, where files live inside the .pck.
/// </summary>
public sealed class GodotDataSource(string root = "res://Data") : IDataSource
{
    public IReadOnlyList<string> ListJsonFiles(string folder)
    {
        var files = new List<string>();
        using var dir = DirAccess.Open($"{root}/{folder}");
        if (dir is null)
        {
            return files;
        }

        dir.ListDirBegin();
        for (var name = dir.GetNext(); name != string.Empty; name = dir.GetNext())
        {
            if (!dir.CurrentIsDir() && name.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
            {
                files.Add($"{folder}/{name}");
            }
        }

        dir.ListDirEnd();
        return files.OrderBy(f => f, System.StringComparer.Ordinal).ToList();
    }

    public string ReadText(string path)
    {
        var fullPath = $"{root}/{path}";
        using var file = FileAccess.Open(fullPath, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            throw new System.IO.FileNotFoundException($"Could not open {fullPath}: {FileAccess.GetOpenError()}", fullPath);
        }

        return file.GetAsText();
    }
}
