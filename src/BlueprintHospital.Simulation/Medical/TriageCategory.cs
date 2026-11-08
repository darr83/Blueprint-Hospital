namespace BlueprintHospital.Simulation.Medical;

/// <summary>
/// The three-category A&amp;E triage model used throughout the game.
/// Values are ordered so a lower number means more urgent.
/// </summary>
public enum TriageCategory
{
    Red = 0,
    Amber = 1,
    Green = 2,
}

public static class TriageCategoryInfo
{
    /// <summary>Display colour for each category, as #RRGGBB. Use these everywhere so colours stay consistent.</summary>
    public static string HexColour(TriageCategory category) => category switch
    {
        TriageCategory.Red => "#E53935",
        TriageCategory.Amber => "#F9A825",
        TriageCategory.Green => "#43A047",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    public static string Label(TriageCategory category) => category switch
    {
        TriageCategory.Red => "Red – Emergency",
        TriageCategory.Amber => "Amber – Urgent",
        TriageCategory.Green => "Green – Routine",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    public static bool IsMoreUrgentThan(this TriageCategory a, TriageCategory b) => a < b;
}
