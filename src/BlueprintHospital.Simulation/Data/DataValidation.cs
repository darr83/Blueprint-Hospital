using System.Text.RegularExpressions;

namespace BlueprintHospital.Simulation.Data;

/// <summary>One problem found in a data file, pinned to the file, record and field.</summary>
public sealed record DataValidationError(string File, string? RecordId, string Field, string Message)
{
    public override string ToString()
    {
        var record = RecordId is null ? string.Empty : $" record '{RecordId}'";
        return $"{File}{record}, field '{Field}': {Message}";
    }
}

/// <summary>Thrown when game data fails validation. Contains every error found, not just the first.</summary>
public sealed class DataLoadException(IReadOnlyList<DataValidationError> errors)
    : Exception(BuildMessage(errors))
{
    public IReadOnlyList<DataValidationError> Errors { get; } = errors;

    private static string BuildMessage(IReadOnlyList<DataValidationError> errors) =>
        $"Game data is invalid ({errors.Count} error{(errors.Count == 1 ? string.Empty : "s")}):"
        + Environment.NewLine
        + string.Join(Environment.NewLine, errors.Select(e => "  - " + e));
}

/// <summary>Collects validation errors for a single record.</summary>
public sealed partial class ValidationContext(string file, string? recordId, List<DataValidationError> sink)
{
    public string File { get; } = file;

    public string? RecordId { get; } = recordId;

    public void Error(string field, string message) => sink.Add(new DataValidationError(File, RecordId, field, message));

    public void RequireText(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Error(field, "is required and must not be empty.");
        }
    }

    public void RequireId(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Error(field, "is required and must not be empty.");
        }
        else if (!IdPattern().IsMatch(value))
        {
            Error(field, $"'{value}' is not a valid id. Use lowercase letters, digits and underscores, starting with a letter.");
        }
    }

    public void RequireIds(string field, IReadOnlyList<string>? values, bool allowEmpty = true)
    {
        if (values is null || (!allowEmpty && values.Count == 0))
        {
            if (!allowEmpty)
            {
                Error(field, "must contain at least one id.");
            }

            return;
        }

        for (var i = 0; i < values.Count; i++)
        {
            RequireId($"{field}[{i}]", values[i]);
        }

        foreach (var duplicate in values.GroupBy(v => v).Where(g => g.Count() > 1))
        {
            Error(field, $"lists '{duplicate.Key}' more than once.");
        }
    }

    public void RequireRange(string field, double value, double min, double max)
    {
        if (double.IsNaN(value) || value < min || value > max)
        {
            Error(field, $"must be between {min} and {max} (was {value}).");
        }
    }

    public void RequireHexColour(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !HexColourPattern().IsMatch(value))
        {
            Error(field, $"'{value}' is not a colour. Use the form #RRGGBB.");
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColourPattern();
}

/// <summary>A record loaded from a JSON data file.</summary>
public interface IDataRecord
{
    string Id { get; }

    /// <summary>Checks the record's own fields. Cross-references to other data are checked by <see cref="GameDatabase"/>.</summary>
    void Validate(ValidationContext context);
}
