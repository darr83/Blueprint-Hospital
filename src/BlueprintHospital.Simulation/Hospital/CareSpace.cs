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
    Ward,

    /// <summary>Plain X-rays, done by a radiographer.</summary>
    XRay,

    /// <summary>CT scanner, done by a radiographer.</summary>
    CT,

    /// <summary>Operating theatre, run by a surgeon.</summary>
    Theatre,

    /// <summary>Dispenses medicines; run by a pharmacist.</summary>
    Pharmacy,

    /// <summary>An outpatient clinic room: a doctor sees booked patients at set times.</summary>
    Clinic,

    /// <summary>Where clinic patients check in and wait; run by a receptionist.</summary>
    OutpatientReception,
}

/// <summary>A doorway in a room wall. <see cref="Offset"/> counts tiles along the wall from the room's corner.</summary>
public sealed record Doorway(WallSide Side, int Offset, int Width);

/// <summary>
/// One bed on a ward. <see cref="PatientSpot"/> is the middle of the bed, <see cref="StaffSpot"/> where
/// staff stand (at the foot of the bed) and <see cref="HeadSouth"/> says which way the bed points.
/// </summary>
public sealed class WardBed
{
    public required GridCoord PatientSpot { get; init; }

    public required GridCoord StaffSpot { get; init; }

    public bool HeadSouth { get; init; }

    /// <summary>The patient in (or on their way to) this bed.</summary>
    public int? OccupantId { get; set; }
}

/// <summary>
/// A piece of furniture standing in a room. <see cref="X"/>/<see cref="Y"/> is its corner on the
/// site grid and <see cref="Width"/>/<see cref="Length"/> its footprint along X and Y, all in tiles.
/// </summary>
public sealed record PlacedFurniture(string ItemId, double X, double Y, double Width, double Length)
{
    public double CentreX => X + (Width / 2);

    public double CentreY => Y + (Length / 2);

    /// <summary>The tile under the middle of the item.</summary>
    public GridCoord Tile => new((int)Math.Floor(CentreX), (int)Math.Floor(CentreY));

    public bool Overlaps(double x, double y, double w, double l, double gap = 0) =>
        X < x + w + gap && x < X + Width + gap && Y < y + l + gap && y < Y + Length + gap;
}

/// <summary>
/// A room or area in the hospital. Treatment spaces hold one patient at a time;
/// the waiting room holds many on its seats.
/// </summary>
public sealed class CareSpace
{
    public required string Id { get; init; }

    /// <summary>Shown on the room's sign. The player can rename rooms.</summary>
    public required string Name { get; set; }

    public required CareSpaceKind Kind { get; init; }

    public required string DepartmentId { get; init; }

    /// <summary>Footprint on the site grid.</summary>
    public required GridRect Rect { get; init; }

    /// <summary>Walls are drawn and block movement, except at the door. Open areas (corridors) have none.</summary>
    public bool HasWalls { get; init; } = true;

    public IReadOnlyList<Doorway> Doors { get; init; } = Array.Empty<Doorway>();

    /// <summary>The room template it was built from.</summary>
    public string TemplateId { get; init; } = string.Empty;

    /// <summary>Which wall the main door is in.</summary>
    public WallSide DoorSide { get; init; } = WallSide.South;

    /// <summary>Furniture and equipment, placed by <see cref="RoomLayoutEngine"/>.</summary>
    public IReadOnlyList<PlacedFurniture> Furniture { get; init; } = Array.Empty<PlacedFurniture>();

    /// <summary>Where patients queue (the reception queue in a waiting room), nearest the desk first.</summary>
    public IReadOnlyList<GridCoord> QueueTiles { get; init; } = Array.Empty<GridCoord>();

    /// <summary>What the room cost to build, in pounds (used for refunds).</summary>
    public int BuildCost { get; init; }

    /// <summary>Where the patient lies or sits (bed, trolley or chair).</summary>
    public GridCoord PatientSpot { get; init; }

    /// <summary>Where staff stand when working with the patient.</summary>
    public GridCoord StaffSpot { get; init; }

    /// <summary>Seats for waiting patients.</summary>
    public IReadOnlyList<GridCoord> Seats { get; init; } = Array.Empty<GridCoord>();

    /// <summary>The beds on a ward, in a stable order. Empty for every other kind of room.</summary>
    public IReadOnlyList<WardBed> Beds { get; init; } = Array.Empty<WardBed>();

    public bool IsWard => Kind == CareSpaceKind.Ward;

    /// <summary>For an operating theatre: the departments whose patients may be operated on in it.</summary>
    public HashSet<string> AllowedDepartments { get; } = new();

    /// <summary>A room with its own staff who come and go with it: imaging, theatres and pharmacy.</summary>
    public bool IsServiceRoom => Kind is CareSpaceKind.XRay or CareSpaceKind.CT or CareSpaceKind.Theatre or CareSpaceKind.Pharmacy;

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
