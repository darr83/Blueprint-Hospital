using BlueprintHospital.Simulation.Hospital;

namespace BlueprintHospital.Simulation.Data.Models;

/// <summary>A preset size offered in the build menu.</summary>
public sealed class RoomSize
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Along the wall with the door, in metres.</summary>
    public double WidthM { get; init; }

    /// <summary>From the door wall to the back wall, in metres.</summary>
    public double LengthM { get; init; }
}

/// <summary>Which furniture layout a room uses. Each layout adapts to the room's size and door direction.</summary>
public enum RoomLayoutKind
{
    /// <summary>No furniture: corridors.</summary>
    None,

    /// <summary>Rows of beds along the walls with a clear aisle between; more beds the bigger the ward.</summary>
    Ward,

    /// <summary>One patient bed with an obs monitor and room-specific equipment (Minors, Majors, Resus).</summary>
    BedSpace,

    /// <summary>A table or scanner in the middle (the first furniture item drawn as a bed) with room for staff beside it: X-ray, CT, theatre.</summary>
    Equipment,

    /// <summary>A nurse's desk, a patient chair and an obs monitor.</summary>
    Triage,

    /// <summary>A reception desk with a queue, and rows of seats.</summary>
    Waiting,

    /// <summary>Desks with chairs for staff (doctors' office, nurses' station).</summary>
    Office,

    /// <summary>A table, chairs and a kitchenette.</summary>
    StaffRoom,

    /// <summary>A food counter on the back wall, vending machines (up to 4) and a water machine, and tables of four filling the floor.</summary>
    Cafeteria,

    /// <summary>A row of vending machines (up to 4) and a water machine, with a seat or two if there is room.</summary>
    Vending,

    /// <summary>Toilets: a row of cubicles along the back wall with wash basins and a hand dryer, or (with no cubicles) a single toilet and basin.</summary>
    Toilets,

    /// <summary>A lift car: empty floor, drawn as the lift. It stands in the same place on every floor.</summary>
    Lift,

    /// <summary>A flight of stairs, drawn as the steps. It stands in the same place on every floor.</summary>
    Stairs,
}

/// <summary>What kind of unit a ward is, when it is more than a plain ward.</summary>
public enum WardUnit
{
    /// <summary>An ordinary ward.</summary>
    None,

    /// <summary>An assessment unit (e.g. the surgical assessment unit): patients wait here for theatre or a ward bed instead of in A&amp;E.</summary>
    Assessment,

    /// <summary>A day surgery unit: patients come round after a small operation and go home the same day.</summary>
    DaySurgery,

    /// <summary>A high dependency unit (HDU): level 2 critical care, a nurse for every two patients.</summary>
    HighDependency,

    /// <summary>An intensive care unit (ICU): level 3 critical care, a ventilator and a nurse at every bed.</summary>
    IntensiveCare,

    /// <summary>A neonatal unit: incubators for newborn babies who need special care, a nurse for every two babies.</summary>
    Neonatal,
}

/// <summary>
/// A room the player can build: what it is for, the sizes it comes in, what it costs and how
/// it is furnished. Loaded from Data/RoomTemplates.
/// </summary>
public sealed class RoomTemplate : IDataRecord
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Build menu tab, e.g. "Emergency" or "Staff".</summary>
    public string Category { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public CareSpaceKind Kind { get; init; }

    public string Department { get; init; } = string.Empty;

    public RoomLayoutKind Layout { get; init; }

    public IReadOnlyList<RoomSize> Sizes { get; init; } = Array.Empty<RoomSize>();

    public double MinWidthM { get; init; }

    public double MinLengthM { get; init; }

    public double MaxWidthM { get; init; }

    public double MaxLengthM { get; init; }

    public double DoorWidthM { get; init; } = 1.0;

    /// <summary>For wards: a same-day (ambulatory care) unit, used only for patients who can go home the same day.</summary>
    public bool SameDay { get; init; }

    /// <summary>True if the room has a second door opposite the first (the waiting room's street entrance).</summary>
    public bool DoorBothEnds { get; init; }

    /// <summary>For wards: the kind of unit (assessment, day surgery, HDU or ICU). An ordinary ward if left out.</summary>
    public WardUnit Unit { get; init; }

    /// <summary>
    /// For operating theatres kept for one department (the maternity C-section theatre): the only
    /// departments it takes, fixed for good. Empty for an ordinary theatre, whose departments the player ticks.
    /// </summary>
    public IReadOnlyList<string> TheatreDepartments { get; init; } = Array.Empty<string>();

    /// <summary>Fixed cost to fit out the room, in pounds.</summary>
    public int BaseCost { get; init; }

    /// <summary>Construction cost per square metre of floor, in pounds.</summary>
    public int CostPerSquareMetre { get; init; }

    /// <summary>Who is needed to run the room, shown in the build menu.</summary>
    public string Staffing { get; init; } = string.Empty;

    /// <summary>Furniture and equipment ids the layout may use; checked against Data/Furniture.</summary>
    public IReadOnlyList<string> Furniture { get; init; } = Array.Empty<string>();

    /// <summary>True for corridors: open floor with no walls or furniture.</summary>
    public bool IsCorridor => Kind == CareSpaceKind.Corridor;

    /// <summary>True for lifts and stairs, which are built through every floor at once.</summary>
    public bool IsShaft => Kind is CareSpaceKind.Lift or CareSpaceKind.Stairs;

    public void Validate(ValidationContext context)
    {
        context.RequireId("id", Id);
        context.RequireText("name", Name);
        context.RequireText("category", Category);
        context.RequireId("department", Department);
        context.RequireRange("min_width_m", MinWidthM, 1, 60);
        context.RequireRange("min_length_m", MinLengthM, 1, 60);
        context.RequireRange("max_width_m", MaxWidthM, MinWidthM, 60);
        context.RequireRange("max_length_m", MaxLengthM, MinLengthM, 60);
        context.RequireRange("door_width_m", DoorWidthM, 0.5, 10);
        context.RequireRange("base_cost", BaseCost, 0, 10_000_000);
        context.RequireRange("cost_per_square_metre", CostPerSquareMetre, 0, 100_000);
        context.RequireIds("furniture", Furniture);

        if (Sizes.Count == 0)
        {
            context.Error("sizes", "must offer at least one size.");
        }

        for (var i = 0; i < Sizes.Count; i++)
        {
            var size = Sizes[i];
            context.RequireText($"sizes[{i}].name", size.Name);
            context.RequireRange($"sizes[{i}].width_m", size.WidthM, MinWidthM, MaxWidthM);
            context.RequireRange($"sizes[{i}].length_m", size.LengthM, MinLengthM, MaxLengthM);
        }

        if (Unit != WardUnit.None && Kind != CareSpaceKind.Ward)
        {
            context.Error("unit", "only wards can be a unit.");
        }

        context.RequireIds("theatre_departments", TheatreDepartments);
        if (TheatreDepartments.Count > 0 && Kind != CareSpaceKind.Theatre)
        {
            context.Error("theatre_departments", "only operating theatres can be kept for a department.");
        }

        if ((Layout == RoomLayoutKind.None) != IsCorridor)
        {
            context.Error("layout", "corridors, and only corridors, use the 'none' layout.");
        }
    }
}
