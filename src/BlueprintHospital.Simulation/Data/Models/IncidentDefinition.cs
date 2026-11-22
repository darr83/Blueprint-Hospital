namespace BlueprintHospital.Simulation.Data.Models;

/// <summary>How one or more casualties of a major incident come in.</summary>
public enum CasualtyTransport
{
    /// <summary>A road ambulance, each casualty in their own.</summary>
    Ambulance,

    /// <summary>The air ambulance (it lands on the helipad when there is one, otherwise the crew bring them by road).</summary>
    Air,

    /// <summary>The walking wounded: they make their own way in (a lift from a friend, or on foot).</summary>
    Walking,
}

/// <summary>One kind of casualty in a major incident ("the driver, 25 to 60, chest injury, red").</summary>
public sealed class IncidentCasualtyDefinition
{
    /// <summary>Who they are, for the casualty list ("driver", "passenger", "child", "worker").</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>What they might have (one is picked).</summary>
    public IReadOnlyList<string> Conditions { get; init; } = Array.Empty<string>();

    /// <summary>The crew's colour on scene: "red", "amber" or "green".</summary>
    public string Triage { get; init; } = "amber";

    /// <summary>How many of them: [fewest, most].</summary>
    public int[] Count { get; init; } = { 1, 1 };

    /// <summary>Their age: [youngest, oldest].</summary>
    public int[] Age { get; init; } = { 18, 80 };

    public CasualtyTransport Transport { get; init; } = CasualtyTransport.Ambulance;

    /// <summary>A firefighter comes in with them and stays a while (rescued from a fire).</summary>
    public bool FireCrew { get; init; }
}

/// <summary>
/// A kind of major incident: a road collision, a house fire, a coach crash... Ambulance control phones
/// ahead (the METHANE message), then the casualties come in a few minutes apart. Loaded from Data/Incidents.
/// </summary>
public sealed class IncidentDefinition : IDataRecord
{
    public string Id { get; init; } = string.Empty;

    /// <summary>"Road traffic collision".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Where it might happen ("A34 northbound, junction 9"); one is picked.</summary>
    public IReadOnlyList<string> Locations { get; init; } = Array.Empty<string>();

    /// <summary>What happened, for the METHANE message ("minibus and two cars, head-on").</summary>
    public string Type { get; init; } = string.Empty;

    public string Hazards { get; init; } = string.Empty;

    public string Access { get; init; } = string.Empty;

    /// <summary>Who is at the scene ("4 ambulances, fire, police, air ambulance").</summary>
    public string Services { get; init; } = string.Empty;

    public IReadOnlyList<IncidentCasualtyDefinition> Casualties { get; init; } = Array.Empty<IncidentCasualtyDefinition>();

    /// <summary>How likely this kind is, compared with the others.</summary>
    public double Weight { get; init; } = 1;

    /// <summary>A big one (8 or more casualties): only on Realistic, and from career level 7.</summary>
    public bool Big { get; init; }

    /// <summary>How much likelier it is in some weather ("ice": 3, "fog": 3).</summary>
    public IReadOnlyDictionary<string, double> Weather { get; init; } = new Dictionary<string, double>();

    /// <summary>The months it can happen in (all if empty).</summary>
    public IReadOnlyList<int> Months { get; init; } = Array.Empty<int>();

    public static readonly IReadOnlySet<string> TriageColours = new HashSet<string>(StringComparer.Ordinal) { "red", "amber", "green" };

    public void Validate(ValidationContext context)
    {
        context.RequireId("id", Id);
        context.RequireText("name", Name);
        context.RequireText("type", Type);
        if (Locations.Count == 0)
        {
            context.Error("locations", "must list at least one place.");
        }

        if (Casualties.Count == 0)
        {
            context.Error("casualties", "must list at least one casualty.");
        }

        context.RequireRange("weight", Weight, 0.0001, 1000);
        foreach (var month in Months)
        {
            context.RequireRange("months", month, 1, 12);
        }

        for (var i = 0; i < Casualties.Count; i++)
        {
            var c = Casualties[i];
            context.RequireText($"casualties[{i}].label", c.Label);
            context.RequireIds($"casualties[{i}].conditions", c.Conditions, allowEmpty: false);
            if (!TriageColours.Contains(c.Triage))
            {
                context.Error($"casualties[{i}].triage", "must be red, amber or green.");
            }

            if (c.Count.Length != 2 || c.Count[0] < 1 || c.Count[1] < c.Count[0] || c.Count[1] > 20)
            {
                context.Error($"casualties[{i}].count", "must be [fewest, most] between 1 and 20.");
            }

            if (c.Age.Length != 2 || c.Age[0] < 0 || c.Age[1] > 100 || c.Age[1] < c.Age[0])
            {
                context.Error($"casualties[{i}].age", "must be [youngest, oldest] between 0 and 100.");
            }
        }
    }
}
