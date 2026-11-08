using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlueprintHospital.Simulation.Data;

/// <summary>
/// Loads every record of one type from a data folder. Each .json file in the folder
/// holds an array of records, so content can be split across as many files as is convenient.
/// </summary>
public static class RecordLoader
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    /// <summary>
    /// Reads and validates all records in <paramref name="folder"/>. Problems are added to
    /// <paramref name="errors"/>; valid records are returned keyed by id.
    /// </summary>
    public static IReadOnlyDictionary<string, T> LoadFolder<T>(IDataSource source, string folder, List<DataValidationError> errors)
        where T : class, IDataRecord
    {
        var records = new Dictionary<string, T>(StringComparer.Ordinal);
        var origins = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in source.ListJsonFiles(folder))
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(source.ReadText(file), new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
            }
            catch (JsonException ex)
            {
                errors.Add(new DataValidationError(file, null, "(file)", $"is not valid JSON: {ex.Message}"));
                continue;
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    errors.Add(new DataValidationError(file, null, "(file)", "must contain a JSON array of records."));
                    continue;
                }

                var index = 0;
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    LoadRecord(file, index++, element, records, origins, errors);
                }
            }
        }

        return records;
    }

    private static void LoadRecord<T>(
        string file,
        int index,
        JsonElement element,
        Dictionary<string, T> records,
        Dictionary<string, string> origins,
        List<DataValidationError> errors)
        where T : class, IDataRecord
    {
        var fallbackId = element.ValueKind == JsonValueKind.Object && element.TryGetProperty("id", out var idProp)
                         && idProp.ValueKind == JsonValueKind.String
            ? idProp.GetString()
            : $"#{index}";

        T? record;
        try
        {
            record = element.Deserialize<T>(JsonOptions);
        }
        catch (JsonException ex)
        {
            var field = string.IsNullOrEmpty(ex.Path) ? "(record)" : ex.Path.TrimStart('$', '.');
            errors.Add(new DataValidationError(file, fallbackId, field, CleanMessage(ex.Message)));
            return;
        }

        if (record is null)
        {
            errors.Add(new DataValidationError(file, fallbackId, "(record)", "must be a JSON object."));
            return;
        }

        var before = errors.Count;
        record.Validate(new ValidationContext(file, record.Id ?? fallbackId, errors));
        if (errors.Count != before)
        {
            return;
        }

        if (origins.TryGetValue(record.Id!, out var firstFile))
        {
            errors.Add(new DataValidationError(file, record.Id, "id", $"is already defined in {firstFile}."));
            return;
        }

        records[record.Id!] = record;
        origins[record.Id!] = file;
    }

    private static string CleanMessage(string message)
    {
        // System.Text.Json appends "Path: ... | LineNumber: ..." which we report separately.
        var cut = message.IndexOf(" Path:", StringComparison.Ordinal);
        return cut > 0 ? message[..cut] : message;
    }
}
