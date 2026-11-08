using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Hospital;

/// <summary>What a room or area is used for.</summary>
public enum CareSpaceKind
{
    Waiting,
    Triage,
    Minors,
    Majors,
    Resus,
    NursesStation,
    DoctorsOffice,
    StaffRoom,
    Corridor,
}

/// <summary>A doorway in a room wall.</summary>
public sealed record Doorway(WallSide Side, int Offset, int Width);

/// <summary>
/// A room or area in the hospital. Treatment spaces hold one patient at a time;
/// the waiting room holds many on its seats.
/// </summary>
public sealed class CareSpace
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required CareSpaceKind Kind { get; init; }

    public required string DepartmentId { get; init; }

    /// <summary>Footprint on the site grid.</summary>
    public required GridRect Rect { get; init; }

    /// <summary>Walls are drawn and block movement, except at the door. Open areas (corridors) have none.</summary>
    public bool HasWalls { get; init; } = true;

    public IReadOnlyList<Doorway> Doors { get; init; } = Array.Empty<Doorway>();

    /// <summary>Where the patient lies or sits (bed, trolley or chair).</summary>
    public GridCoord PatientSpot { get; init; }

    /// <summary>Where staff stand when working with the patient.</summary>
    public GridCoord StaffSpot { get; init; }

    /// <summary>Seats for waiting patients.</summary>
    public IReadOnlyList<GridCoord> Seats { get; init; } = Array.Empty<GridCoord>();

    /// <summary>Patient currently in (or on their way to) this space.</summary>
    public int? OccupantId { get; set; }

    /// <summary>Staff member using a room that is staff-led, e.g. the triage nurse.</summary>
    public int? ReservedByStaffId { get; set; }

    public bool IsTreatmentSpace => Kind is CareSpaceKind.Minors or CareSpaceKind.Majors or CareSpaceKind.Resus;

    public bool IsFree => OccupantId is null;

    public double AreaSquareMetres => Units.AreaSquareMetres(Rect.Width, Rect.Length);

    /// <summary>True if the edge from <paramref name="inside"/> outward on <paramref name="side"/> is a doorway.</summary>
    public bool IsDoor(GridCoord inside, WallSide side)
    {
        foreach (var door in Doors.Where(d => d.Side == side))
        {
            var along = side is WallSide.North or WallSide.South ? inside.X - Rect.X : inside.Y - Rect.Y;
            if (along >= door.Offset && along < door.Offset + door.Width)
            {
                return true;
            }
        }

        return false;
    }
}
