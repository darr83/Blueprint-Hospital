using BlueprintHospital.Simulation.Data.Models;

namespace BlueprintHospital.Simulation.Data;

/// <summary>
/// All data-driven game content, loaded and validated once at startup.
/// New content types are added here as each milestone introduces them.
/// </summary>
public sealed class GameDatabase
{
    public const string DepartmentsFolder = "Departments";
    public const string SymptomsFolder = "Symptoms";

    private GameDatabase(
        IReadOnlyDictionary<string, DepartmentDefinition> departments,
        IReadOnlyDictionary<string, SymptomDefinition> symptoms)
    {
        Departments = departments;
        Symptoms = symptoms;
    }

    public IReadOnlyDictionary<string, DepartmentDefinition> Departments { get; }

    public IReadOnlyDictionary<string, SymptomDefinition> Symptoms { get; }

    /// <summary>The department every new hospital starts with.</summary>
    public DepartmentDefinition StartingDepartment => Departments.Values.Single(d => d.StartsUnlocked);

    /// <summary>Loads every data folder. Throws <see cref="DataLoadException"/> listing every problem if anything is invalid.</summary>
    public static GameDatabase Load(IDataSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var errors = new List<DataValidationError>();

        var departments = RecordLoader.LoadFolder<DepartmentDefinition>(source, DepartmentsFolder, errors);
        var symptoms = RecordLoader.LoadFolder<SymptomDefinition>(source, SymptomsFolder, errors);

        CheckDatabaseRules(departments, errors);

        if (errors.Count > 0)
        {
            throw new DataLoadException(errors);
        }

        return new GameDatabase(departments, symptoms);
    }

    private static void CheckDatabaseRules(
        IReadOnlyDictionary<string, DepartmentDefinition> departments,
        List<DataValidationError> errors)
    {
        // Skip if any department failed to load: the record that was rejected may be the starting one.
        if (errors.Any(e => e.File.StartsWith(DepartmentsFolder + "/", StringComparison.Ordinal)))
        {
            return;
        }

        var starting = departments.Values.Where(d => d.StartsUnlocked).Select(d => d.Id).ToArray();
        if (starting.Length != 1)
        {
            errors.Add(new DataValidationError(
                DepartmentsFolder,
                null,
                "starts_unlocked",
                starting.Length == 0
                    ? "exactly one department must have starts_unlocked set to true; none do."
                    : $"exactly one department must have starts_unlocked set to true; found {string.Join(", ", starting)}."));
        }
    }
}
