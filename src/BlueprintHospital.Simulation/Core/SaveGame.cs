using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlueprintHospital.Simulation.Core;

/// <summary>
/// Everything needed to restore a game. Each milestone adds its own state here
/// (rooms, patients, staff, finances...). <see cref="FormatVersion"/> is bumped when the layout changes.
/// </summary>
public sealed class SaveGame
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public string HospitalName { get; init; } = "Blueprint General Hospital";

    /// <summary>Real-world time the file was written.</summary>
    public DateTime SavedAtUtc { get; init; }

    public ClockState Clock { get; init; } = new(SimulationClock.DefaultStart, 0, SimulationSpeed.Normal);

    public CameraState Camera { get; init; } = new(0, 0, 1);
}

/// <summary>Where the player was looking, so a loaded game opens on the same view.</summary>
public sealed record CameraState(double X, double Y, double Zoom);

/// <summary>Summary shown in the Load Game list without loading the whole file.</summary>
public sealed record SaveSlotInfo(string SlotName, string HospitalName, DateTime GameTime, DateTime SavedAtUtc);

public static class SaveSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static string Serialize(SaveGame save) => JsonSerializer.Serialize(save, Options);

    public static SaveGame Deserialize(string json)
    {
        SaveGame? save;
        try
        {
            save = JsonSerializer.Deserialize<SaveGame>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The save file is damaged: {ex.Message}", ex);
        }

        if (save is null)
        {
            throw new InvalidDataException("The save file is empty.");
        }

        if (save.FormatVersion > SaveGame.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"This save was made by a newer version of the game (format {save.FormatVersion}, this game reads up to {SaveGame.CurrentFormatVersion}).");
        }

        return save;
    }

    /// <summary>Turns a player-typed name into a safe file name.</summary>
    public static string ToSlotName(string displayName)
    {
        var cleaned = new string(displayName.Trim()
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_')
            .ToArray());
        while (cleaned.Contains("__", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        }

        cleaned = cleaned.Trim('_');
        return cleaned.Length == 0 ? "save" : cleaned[..Math.Min(cleaned.Length, 48)];
    }
}
