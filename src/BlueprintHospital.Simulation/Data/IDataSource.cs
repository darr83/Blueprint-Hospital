namespace BlueprintHospital.Simulation.Data;

/// <summary>
/// Abstracts where game data files live. The Godot game reads from the packed
/// res:// file system; tests and tools read from disk.
/// </summary>
public interface IDataSource
{
    /// <summary>Lists the .json files directly inside <paramref name="folder"/>, as paths usable with <see cref="ReadText"/>. Sorted for deterministic loading.</summary>
    IReadOnlyList<string> ListJsonFiles(string folder);

    string ReadText(string path);
}

/// <summary>Reads data files from a directory on disk.</summary>
public sealed class FileSystemDataSource(string rootDirectory) : IDataSource
{
    public string RootDirectory { get; } = Path.GetFullPath(rootDirectory);

    public IReadOnlyList<string> ListJsonFiles(string folder)
    {
        var directory = Path.Combine(RootDirectory, folder);
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(f => Path.GetRelativePath(RootDirectory, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();
    }

    public string ReadText(string path) => File.ReadAllText(Path.Combine(RootDirectory, path));
}

/// <summary>In-memory data source, handy for tests.</summary>
public sealed class InMemoryDataSource : IDataSource
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    public InMemoryDataSource Add(string path, string json)
    {
        _files[path] = json;
        return this;
    }

    public IReadOnlyList<string> ListJsonFiles(string folder)
    {
        var prefix = folder.TrimEnd('/') + "/";
        return _files.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal)
                        && k.EndsWith(".json", StringComparison.Ordinal)
                        && !k[prefix.Length..].Contains('/'))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
    }

    public string ReadText(string path) =>
        _files.TryGetValue(path, out var text) ? text : throw new FileNotFoundException("No in-memory data file.", path);
}
