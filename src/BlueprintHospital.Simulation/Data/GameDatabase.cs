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
    public const string ConditionsFolder = "Conditions";
    public const string InvestigationsFolder = "Investigations";
    public const string MedicationsFolder = "Medications";
    public const string NamesFolder = "Names";
    public const string ArrivalsFolder = "Arrivals";
    public const string PatientBackgroundsFolder = "PatientBackgrounds";

    private GameDatabase()
    {
    }

    public IReadOnlyDictionary<string, DepartmentDefinition> Departments { get; private init; } = null!;

    public IReadOnlyDictionary<string, SymptomDefinition> Symptoms { get; private init; } = null!;

    public IReadOnlyDictionary<string, ConditionDefinition> Conditions { get; private init; } = null!;

    public IReadOnlyDictionary<string, InvestigationDefinition> Investigations { get; private init; } = null!;

    public IReadOnlyDictionary<string, MedicationDefinition> Medications { get; private init; } = null!;

    public IReadOnlyDictionary<string, NamePool> Names { get; private init; } = null!;

    public IReadOnlyDictionary<string, ArrivalProfile> Arrivals { get; private init; } = null!;

    public IReadOnlyDictionary<string, PatientBackground> PatientBackgrounds { get; private init; } = null!;

    /// <summary>The department every new hospital starts with.</summary>
    public DepartmentDefinition StartingDepartment => Departments.Values.Single(d => d.StartsUnlocked);

    /// <summary>Loads every data folder. Throws <see cref="DataLoadException"/> listing every problem if anything is invalid.</summary>
    public static GameDatabase Load(IDataSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var errors = new List<DataValidationError>();

        var db = new GameDatabase
        {
            Departments = RecordLoader.LoadFolder<DepartmentDefinition>(source, DepartmentsFolder, errors),
            Symptoms = RecordLoader.LoadFolder<SymptomDefinition>(source, SymptomsFolder, errors),
            Conditions = RecordLoader.LoadFolder<ConditionDefinition>(source, ConditionsFolder, errors),
            Investigations = RecordLoader.LoadFolder<InvestigationDefinition>(source, InvestigationsFolder, errors),
            Medications = RecordLoader.LoadFolder<MedicationDefinition>(source, MedicationsFolder, errors),
            Names = RecordLoader.LoadFolder<NamePool>(source, NamesFolder, errors),
            Arrivals = RecordLoader.LoadFolder<ArrivalProfile>(source, ArrivalsFolder, errors),
            PatientBackgrounds = RecordLoader.LoadFolder<PatientBackground>(source, PatientBackgroundsFolder, errors),
        };

        db.CheckDatabaseRules(errors);

        if (errors.Count > 0)
        {
            throw new DataLoadException(errors);
        }

        return db;
    }

    private void CheckDatabaseRules(List<DataValidationError> errors)
    {
        // Skip if any department failed to load: the record that was rejected may be the starting one.
        if (!errors.Any(e => e.File.StartsWith(DepartmentsFolder + "/", StringComparison.Ordinal)))
        {
            var starting = Departments.Values.Where(d => d.StartsUnlocked).Select(d => d.Id).ToArray();
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

        foreach (var condition in Conditions.Values)
        {
            var file = $"{ConditionsFolder}/";
            void Missing(string field, string kind, string id) =>
                errors.Add(new DataValidationError(file, condition.Id, field, $"refers to unknown {kind} '{id}'."));

            if (!Departments.ContainsKey(condition.Department))
            {
                Missing("department", "department", condition.Department);
            }

            foreach (var s in condition.Symptoms.Where(s => !Symptoms.ContainsKey(s.Symptom)))
            {
                Missing("symptoms", "symptom", s.Symptom);
            }

            foreach (var i in condition.KeyInvestigations.Where(i => !Investigations.ContainsKey(i)))
            {
                Missing("key_investigations", "investigation", i);
            }

            foreach (var m in condition.Treatment.Where(t => t.Type == TreatmentStepType.Medication && !Medications.ContainsKey(t.Medication!)))
            {
                Missing("treatment", "medication", m.Medication!);
            }
        }

        foreach (var symptom in Symptoms.Values)
        {
            foreach (var i in symptom.TriageInvestigations.Where(i => !Investigations.ContainsKey(i)))
            {
                errors.Add(new DataValidationError($"{SymptomsFolder}/", symptom.Id, "triage_investigations", $"refers to unknown investigation '{i}'."));
            }
        }

        foreach (var investigation in Investigations.Values)
        {
            foreach (var a in investigation.Abnormal.Where(a => !Conditions.ContainsKey(a.Condition)))
            {
                errors.Add(new DataValidationError($"{InvestigationsFolder}/", investigation.Id, "abnormal.condition", $"refers to unknown condition '{a.Condition}'."));
            }
        }

        foreach (var profile in Arrivals.Values.Where(p => !Departments.ContainsKey(p.Department)))
        {
            errors.Add(new DataValidationError($"{ArrivalsFolder}/", profile.Id, "department", $"refers to unknown department '{profile.Department}'."));
        }
    }

    /// <summary>Creates a database directly from records. For tests.</summary>
    public static GameDatabase FromRecords(
        IEnumerable<DepartmentDefinition>? departments = null,
        IEnumerable<SymptomDefinition>? symptoms = null,
        IEnumerable<ConditionDefinition>? conditions = null,
        IEnumerable<InvestigationDefinition>? investigations = null,
        IEnumerable<MedicationDefinition>? medications = null,
        IEnumerable<NamePool>? names = null,
        IEnumerable<ArrivalProfile>? arrivals = null,
        IEnumerable<PatientBackground>? backgrounds = null) => new()
    {
        Departments = (departments ?? Array.Empty<DepartmentDefinition>()).ToDictionary(r => r.Id),
        Symptoms = (symptoms ?? Array.Empty<SymptomDefinition>()).ToDictionary(r => r.Id),
        Conditions = (conditions ?? Array.Empty<ConditionDefinition>()).ToDictionary(r => r.Id),
        Investigations = (investigations ?? Array.Empty<InvestigationDefinition>()).ToDictionary(r => r.Id),
        Medications = (medications ?? Array.Empty<MedicationDefinition>()).ToDictionary(r => r.Id),
        Names = (names ?? Array.Empty<NamePool>()).ToDictionary(r => r.Id),
        Arrivals = (arrivals ?? Array.Empty<ArrivalProfile>()).ToDictionary(r => r.Id),
        PatientBackgrounds = (backgrounds ?? Array.Empty<PatientBackground>()).ToDictionary(r => r.Id),
    };
}
