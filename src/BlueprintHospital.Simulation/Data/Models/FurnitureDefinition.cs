namespace BlueprintHospital.Simulation.Data.Models;

/// <summary>How a piece of furniture is drawn.</summary>
public enum FurnitureStyle
{
    /// <summary>A plain box: desks, cupboards, counters, trolleys.</summary>
    Box,

    /// <summary>A chair with a back rest; people can sit on it.</summary>
    Chair,

    /// <summary>A hospital bed with a patient lying on it.</summary>
    Bed,

    /// <summary>An observation monitor on a stand.</summary>
    Monitor,
}

/// <summary>
/// A piece of furniture or equipment with its real-world size. Loaded from Data/Furniture.
/// Room layouts place these at their true dimensions, so a bed is always 1 m by 2.1 m.
/// </summary>
public sealed class FurnitureDefinition : IDataRecord
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Side to side, in metres.</summary>
    public double WidthM { get; init; }

    /// <summary>Front to back, in metres.</summary>
    public double DepthM { get; init; }

    /// <summary>Height, in metres.</summary>
    public double HeightM { get; init; }

    public FurnitureStyle Style { get; init; } = FurnitureStyle.Box;

    /// <summary>Main colour, as #RRGGBB.</summary>
    public string Colour { get; init; } = "#A0A0A0";

    /// <summary>Cost to buy and install, in pounds.</summary>
    public int Cost { get; init; }

    /// <summary>True for medical equipment (listed separately from furniture in the build menu).</summary>
    public bool IsEquipment { get; init; }

    public void Validate(ValidationContext context)
    {
        context.RequireId("id", Id);
        context.RequireText("name", Name);
        context.RequireRange("width_m", WidthM, 0.1, 10);
        context.RequireRange("depth_m", DepthM, 0.1, 10);
        context.RequireRange("height_m", HeightM, 0.05, 3);
        context.RequireHexColour("colour", Colour);
        context.RequireRange("cost", Cost, 0, 1_000_000);
    }
}
