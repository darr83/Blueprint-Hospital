namespace BlueprintHospital.Simulation.Data.Models;

/// <summary>A clinical department, e.g. A&amp;E or Cardiology. Loaded from Data/Departments.</summary>
public sealed class DepartmentDefinition : IDataRecord
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Short label for tight UI spaces, e.g. "A&amp;E" or "CARD".</summary>
    public string ShortName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>Colour used for room floors, map overlays and dashboards, as #RRGGBB.</summary>
    public string Colour { get; init; } = string.Empty;

    /// <summary>True for the department every new hospital starts with.</summary>
    public bool StartsUnlocked { get; init; }

    /// <summary>True for shared services (e.g. Radiology) that serve other departments.</summary>
    public bool IsSharedService { get; init; }

    /// <summary>Cost to open the department, in pounds.</summary>
    public int OpeningCost { get; init; }

    public void Validate(ValidationContext context)
    {
        context.RequireId("id", Id);
        context.RequireText("name", Name);
        context.RequireText("short_name", ShortName);
        context.RequireHexColour("colour", Colour);
        context.RequireRange("opening_cost", OpeningCost, 0, 100_000_000);
    }
}
