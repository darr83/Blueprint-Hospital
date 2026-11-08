namespace BlueprintHospital.Simulation.Data.Models;

/// <summary>Names used to generate patients and staff. Loaded from Data/Names.</summary>
public sealed class NamePool : IDataRecord
{
    public string Id { get; init; } = string.Empty;

    public IReadOnlyList<string> FemaleFirstNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> MaleFirstNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Surnames { get; init; } = Array.Empty<string>();

    public void Validate(ValidationContext context)
    {
        context.RequireId("id", Id);
        if (FemaleFirstNames.Count == 0)
        {
            context.Error("female_first_names", "must not be empty.");
        }

        if (MaleFirstNames.Count == 0)
        {
            context.Error("male_first_names", "must not be empty.");
        }

        if (Surnames.Count == 0)
        {
            context.Error("surnames", "must not be empty.");
        }
    }
}
