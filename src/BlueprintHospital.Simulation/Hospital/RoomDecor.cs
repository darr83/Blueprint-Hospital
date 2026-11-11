namespace BlueprintHospital.Simulation.Hospital;

/// <summary>What a room's floor is laid with. Standard is the plain tinted floor every room had before decorating.</summary>
public enum FloorFinish
{
    Standard,
    Vinyl,
    SafetyVinyl,
    CeramicTiles,
    WoodPlanks,
    Terrazzo,
    CarpetTiles,
    Chequer,
}

/// <summary>How a room's walls are finished on the inside. Standard is the plain grey wall.</summary>
public enum WallFinish
{
    Standard,
    Paint,
    TwoTone,
    Stripe,
    Tiles,
    WoodPanel,
}

/// <summary>A named colour the player can pick for a floor or walls.</summary>
public sealed record DecorColour(string Name, string Hex);

/// <summary>
/// A room's floor and wall finish. A null colour means the finish's usual colour for that room
/// (the room's own floor tint, oak for wood, the department colour for a stripe, and so on).
/// </summary>
public sealed record RoomDecor(FloorFinish Floor = FloorFinish.Standard, string? FloorColour = null, WallFinish Walls = WallFinish.Standard, string? WallColour = null)
{
    public static readonly RoomDecor Standard = new();

    public bool IsStandard => this == Standard;
}

/// <summary>The finishes on offer: names, prices and colours, and the look each kind of room starts with.</summary>
public static class DecorCatalogue
{
    public static readonly IReadOnlyList<FloorFinish> Floors = Enum.GetValues<FloorFinish>();

    public static readonly IReadOnlyList<WallFinish> Walls = Enum.GetValues<WallFinish>();

    public static readonly IReadOnlyList<DecorColour> FloorColours = new DecorColour[]
    {
        new("Sky", "#CFE0EE"), new("Sage", "#BFDCC8"), new("Mint", "#A9D4CC"), new("Blush", "#E9CDBE"),
        new("Stone", "#E6E1D8"), new("Butter", "#F2E2B8"), new("Ash", "#E3C79F"), new("Oak", "#C99A6B"),
        new("Walnut", "#8E6446"), new("Slate", "#8FA7B8"), new("Lavender", "#B9A3C9"), new("Navy", "#3B4A5A"),
    };

    public static readonly IReadOnlyList<DecorColour> WallColours = new DecorColour[]
    {
        new("White", "#F6F8FA"), new("Sky", "#DCEBF5"), new("Sage", "#E3EFE6"), new("Mint", "#DDF0EC"),
        new("Peach", "#F4DFC9"), new("Butter", "#F7E3B5"), new("Rose", "#F6E1E4"), new("Lilac", "#E9E1F2"),
        new("Sand", "#E9D8B4"), new("Stone", "#DDE2E6"), new("Teal", "#B9DCD8"), new("Navy", "#56677A"),
    };

    public static string Name(FloorFinish finish) => finish switch
    {
        FloorFinish.Standard => "Standard",
        FloorFinish.Vinyl => "Vinyl sheet",
        FloorFinish.SafetyVinyl => "Safety vinyl",
        FloorFinish.CeramicTiles => "Ceramic tiles",
        FloorFinish.WoodPlanks => "Wood planks",
        FloorFinish.Terrazzo => "Terrazzo",
        FloorFinish.CarpetTiles => "Carpet tiles",
        FloorFinish.Chequer => "Chequer",
        _ => finish.ToString(),
    };

    public static string Name(WallFinish finish) => finish switch
    {
        WallFinish.Standard => "Standard",
        WallFinish.Paint => "Paint",
        WallFinish.TwoTone => "Two-tone",
        WallFinish.Stripe => "Stripe",
        WallFinish.Tiles => "Wall tiles",
        WallFinish.WoodPanel => "Wood panelling",
        _ => finish.ToString(),
    };

    /// <summary>Pounds per m² of floor to lay it.</summary>
    public static int PricePerSquareMetre(FloorFinish finish) => finish switch
    {
        FloorFinish.Standard => 12,
        FloorFinish.Vinyl => 18,
        FloorFinish.SafetyVinyl => 24,
        FloorFinish.CeramicTiles => 30,
        FloorFinish.WoodPlanks => 40,
        FloorFinish.Terrazzo => 55,
        FloorFinish.CarpetTiles => 22,
        FloorFinish.Chequer => 28,
        _ => 20,
    };

    /// <summary>Pounds per m² of the room's floor area to redo its walls.</summary>
    public static int PricePerSquareMetre(WallFinish finish) => finish switch
    {
        WallFinish.Standard => 6,
        WallFinish.Paint => 8,
        WallFinish.TwoTone => 12,
        WallFinish.Stripe => 12,
        WallFinish.Tiles => 25,
        WallFinish.WoodPanel => 30,
        _ => 10,
    };

    /// <summary>The look a newly built room of this kind starts with.</summary>
    public static RoomDecor StartingLook(CareSpaceKind kind) => kind switch
    {
        CareSpaceKind.Ward or CareSpaceKind.Majors or CareSpaceKind.Minors or CareSpaceKind.Resus or CareSpaceKind.Triage
            or CareSpaceKind.EndoscopyRecovery or CareSpaceKind.DeliveryRoom => new(FloorFinish.SafetyVinyl, null, WallFinish.TwoTone),
        CareSpaceKind.Theatre or CareSpaceKind.Endoscopy or CareSpaceKind.XRay or CareSpaceKind.CT or CareSpaceKind.CathLab
            or CareSpaceKind.PlasterRoom => new(FloorFinish.SafetyVinyl, null, WallFinish.Tiles),
        CareSpaceKind.Toilets or CareSpaceKind.AccessibleToilet or CareSpaceKind.StaffToilet or CareSpaceKind.CleaningRoom => new(FloorFinish.CeramicTiles, null, WallFinish.Tiles),
        CareSpaceKind.Waiting or CareSpaceKind.OutpatientReception => new(FloorFinish.Terrazzo, null, WallFinish.Paint),
        CareSpaceKind.Clinic or CareSpaceKind.DoctorsOffice or CareSpaceKind.OutreachOffice or CareSpaceKind.Echo or CareSpaceKind.Eeg
            or CareSpaceKind.Ultrasound => new(FloorFinish.CarpetTiles, null, WallFinish.Stripe),
        CareSpaceKind.StaffRoom => new(FloorFinish.WoodPlanks, null, WallFinish.WoodPanel),
        CareSpaceKind.Cafeteria => new(FloorFinish.Chequer, null, WallFinish.WoodPanel),
        CareSpaceKind.PhysioGym => new(FloorFinish.WoodPlanks, "#E3C79F", WallFinish.Stripe),
        CareSpaceKind.NursesStation or CareSpaceKind.Pharmacy or CareSpaceKind.VendingPoint => new(FloorFinish.Vinyl, null, WallFinish.Paint),
        _ => RoomDecor.Standard,
    };

    /// <summary>Lifts and stairs keep their steel plate and treads.</summary>
    public static bool CanDecorate(CareSpace space) => !space.IsShaft;
}
