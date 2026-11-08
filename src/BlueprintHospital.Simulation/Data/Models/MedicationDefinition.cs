namespace BlueprintHospital.Simulation.Data.Models;

/// <summary>A medicine. Loaded from Data/Medications.</summary>
public sealed class MedicationDefinition : IDataRecord
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string DrugClass { get; init; } = string.Empty;

    /// <summary>oral, intravenous, sublingual, inhaled, subcutaneous...</summary>
    public string Route { get; init; } = string.Empty;

    public string TypicalDose { get; init; } = string.Empty;

    public string Indications { get; init; } = string.Empty;

    public string Contraindications { get; init; } = string.Empty;

    /// <summary>Allergy groups that rule this drug out, e.g. "nsaid" or "penicillin".</summary>
    public IReadOnlyList<string> AllergyGroups { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CommonSideEffects { get; init; } = Array.Empty<string>();

    public int CostPerDose { get; init; }

    public void Validate(ValidationContext context)
    {
        context.RequireId("id", Id);
        context.RequireText("name", Name);
        context.RequireText("drug_class", DrugClass);
        context.RequireText("route", Route);
        context.RequireText("typical_dose", TypicalDose);
        context.RequireIds("allergy_groups", AllergyGroups);
        context.RequireRange("cost_per_dose", CostPerDose, 0, 100_000);
    }
}
