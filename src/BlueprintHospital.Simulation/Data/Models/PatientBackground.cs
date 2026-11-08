namespace BlueprintHospital.Simulation.Data.Models;

public sealed class PastHistoryEntry
{
    public string Name { get; init; } = string.Empty;

    public int MinAge { get; init; }

    /// <summary>[[age, probability], ...] in increasing age; the probability for an age is taken from the last point at or below it.</summary>
    public IReadOnlyList<double[]> ProbabilityByAge { get; init; } = Array.Empty<double[]>();

    public IReadOnlyList<string> RegularMedications { get; init; } = Array.Empty<string>();

    public double ProbabilityAt(int age)
    {
        if (age < MinAge)
        {
            return 0;
        }

        var p = 0.0;
        foreach (var point in ProbabilityByAge)
        {
            if (age >= point[0])
            {
                p = point[1];
            }
        }

        return p;
    }
}

public sealed class AllergyEntry
{
    /// <summary>Allergy group matched against medication allergy_groups.</summary>
    public string Group { get; init; } = string.Empty;

    /// <summary>How it appears on the patient record.</summary>
    public string Name { get; init; } = string.Empty;

    public double Probability { get; init; }
}

/// <summary>Background medical history and allergies for generated patients. Loaded from Data/PatientBackgrounds.</summary>
public sealed class PatientBackground : IDataRecord
{
    public string Id { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<PastHistoryEntry> PastHistory { get; init; } = Array.Empty<PastHistoryEntry>();

    public IReadOnlyList<AllergyEntry> Allergies { get; init; } = Array.Empty<AllergyEntry>();

    public void Validate(ValidationContext context)
    {
        context.RequireId("id", Id);
        for (var i = 0; i < PastHistory.Count; i++)
        {
            var entry = PastHistory[i];
            context.RequireText($"past_history[{i}].name", entry.Name);
            if (entry.ProbabilityByAge.Count == 0 || entry.ProbabilityByAge.Any(p => p.Length != 2 || p[1] < 0 || p[1] > 1))
            {
                context.Error($"past_history[{i}].probability_by_age", "must be a list of [age, probability] pairs with probabilities between 0 and 1.");
            }
        }

        for (var i = 0; i < Allergies.Count; i++)
        {
            context.RequireId($"allergies[{i}].group", Allergies[i].Group);
            context.RequireText($"allergies[{i}].name", Allergies[i].Name);
            context.RequireRange($"allergies[{i}].probability", Allergies[i].Probability, 0, 1);
        }
    }
}
