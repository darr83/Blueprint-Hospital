namespace BlueprintHospital.Simulation.World;

/// <summary>
/// Where the player puts the two ways into the hospital: the main entrance and the ambulance entrance with
/// its bays. Each is a wall of the main building plus how far along that wall (in tiles, from the west end
/// of a north or south wall, or the north end of an east or west wall) its 4 m doorway starts.
/// </summary>
public sealed record SitePlan(WallSide EntranceSide, int EntranceAt, WallSide AmbulanceSide, int AmbulanceAt)
{
    /// <summary>The usual layout: front doors in the middle of the south wall, ambulances round the east side.</summary>
    public static readonly SitePlan Standard = new(WallSide.South, 22, WallSide.East, 9);
}

/// <summary>
/// The drive the ambulances take: in from the site edge at <see cref="Entry"/>, along the road past the
/// bays (stopping level with theirs and reversing in), and out at <see cref="Exit"/>.
/// </summary>
public sealed record AmbulanceLane(GridCoord Entry, GridCoord Exit)
{
    /// <summary>True when the lane runs north-south.</summary>
    public bool Vertical => Entry.X == Exit.X;

    /// <summary>The point on the lane level with <paramref name="tile"/>.</summary>
    public GridCoord Beside(GridCoord tile) => Vertical ? new GridCoord(Entry.X, tile.Y) : new GridCoord(tile.X, Entry.Y);

    /// <summary>How far along the lane from the entry <paramref name="tile"/> is.</summary>
    public int Along(GridCoord tile) => Vertical ? Math.Abs(tile.Y - Entry.Y) : Math.Abs(tile.X - Entry.X);

    /// <summary>The point on the lane <paramref name="distance"/> tiles in from the entry.</summary>
    public GridCoord At(int distance) => Vertical
        ? new GridCoord(Entry.X, Entry.Y + (Math.Sign(Exit.Y - Entry.Y) * distance))
        : new GridCoord(Entry.X + (Math.Sign(Exit.X - Entry.X) * distance), Entry.Y);
}

/// <summary>Everything outside the walls the simulation needs to know: the bays, the ambulance lane and where people arrive.</summary>
public sealed record SiteLayout(SitePlan Plan, IReadOnlyList<GridRect> AmbulanceBays, AmbulanceLane Lane, GridCoord WalkInSpawn)
{
    /// <summary>Which wall the bays stand against: their open ends face away from it.</summary>
    public WallSide BaySide => Plan.AmbulanceSide;

    /// <summary>Where the stretcher comes out of each bay: the path between the bay and the building.</summary>
    public IReadOnlyList<GridCoord> PatientTiles => AmbulanceBays.Select(b => BaySide switch
    {
        WallSide.East => new GridCoord(b.X - 1, b.Y + (b.Length / 2)),
        WallSide.West => new GridCoord(b.Right, b.Y + (b.Length / 2)),
        WallSide.North => new GridCoord(b.X + (b.Width / 2), b.Bottom),
        _ => new GridCoord(b.X + (b.Width / 2), b.Y - 1),
    }).ToArray();

    /// <summary>Where each ambulance stands when parked: the middle of its bay.</summary>
    public IReadOnlyList<GridCoord> ParkingTiles => AmbulanceBays.Select(b => new GridCoord(b.X + (b.Width / 2), b.Y + (b.Length / 2))).ToArray();
}

/// <summary>Builds the site layout a new game starts with: one hospital building, roads, parking and the ambulance bays.</summary>
public static class StarterSite
{
    public const int SiteWidth = 160;   // 80 m
    public const int SiteLength = 120;  // 60 m

    /// <summary>The main hospital building: 30 m x 20 m.</summary>
    public static readonly GridRect MainBuildingFootprint = new(40, 30, 60, 40);

    /// <summary>How many ambulances can park at the ambulance entrance at once.</summary>
    public const int AmbulanceBays = 4;

    /// <summary>Each bay is 3.5 m wide (the ambulance is 2.7 m across)...</summary>
    public const int BayWidth = 7;

    /// <summary>...and 6.5 m deep (the ambulance is 5.8 m long).</summary>
    public const int BayLength = 13;

    /// <summary>The bays start this far out from the building wall, leaving a path for the stretchers.</summary>
    public const int BayOffset = 3;

    /// <summary>The road beside the bays starts this far out from the building wall, just past the bays.</summary>
    public const int RoadOffset = BayOffset + BayLength;

    /// <summary>Other roads start this far out from the building wall.</summary>
    public const int FrontRoadOffset = 14;

    /// <summary>Roads are 5 m wide.</summary>
    public const int RoadWidth = 10;

    /// <summary>Entrance doorways are 4 m wide.</summary>
    public const int DoorWidth = 8;

    /// <summary>Paving round the building.</summary>
    private const int Apron = 4;

    /// <summary>The car park: 12 m along the road.</summary>
    private const int CarParkLength = 24;

    /// <summary>Doorways keep this far from the building's corners.</summary>
    public const int CornerGap = 2;

    /// <summary>Length of a wall of the starting building.</summary>
    public static int WallLength(WallSide side) => side is WallSide.North or WallSide.South ? MainBuildingFootprint.Width : MainBuildingFootprint.Length;

    /// <summary>The furthest along a wall a doorway can start.</summary>
    public static int MaxDoorAt(WallSide side) => WallLength(side) - DoorWidth - CornerGap;

    /// <summary>Why a plan won't work (empty when it will).</summary>
    public static IReadOnlyList<string> Problems(SitePlan plan)
    {
        var problems = new List<string>();
        if (plan.EntranceAt < CornerGap || plan.EntranceAt > MaxDoorAt(plan.EntranceSide))
        {
            problems.Add("The main entrance must be on the wall, away from the corners.");
        }

        if (plan.AmbulanceAt < CornerGap || plan.AmbulanceAt > MaxDoorAt(plan.AmbulanceSide))
        {
            problems.Add("The ambulance entrance must be on the wall, away from the corners.");
        }

        var b = MainBuildingFootprint;
        var bays = BayBlock(b, plan);
        if (!new GridRect(0, 0, SiteWidth, SiteLength).Contains(new GridCoord(bays.X, bays.Y)) || bays.Right > SiteWidth || bays.Bottom > SiteLength)
        {
            problems.Add("The ambulance bays would not fit on the site there.");
        }

        if (FrontPath(b, plan).Intersects(Grown(bays, 1)) || DoorStrip(b, plan.EntranceSide, plan.EntranceAt).Intersects(Grown(bays, 1)))
        {
            problems.Add("The main entrance is too close to the ambulance bays.");
        }

        if (plan.EntranceSide == plan.AmbulanceSide && Math.Abs((plan.EntranceAt + (DoorWidth / 2)) - (plan.AmbulanceAt + (DoorWidth / 2))) < DoorWidth + 2)
        {
            problems.Add("The two entrances can't share a doorway.");
        }

        return problems.Distinct().ToList();
    }

    public static SiteMap Create() => Create(SitePlan.Standard);

    public static SiteMap Create(SitePlan plan)
    {
        if (Problems(plan) is { Count: > 0 } problems)
        {
            throw new ArgumentException(string.Join(" ", problems), nameof(plan));
        }

        var site = new SiteMap(SiteWidth, SiteLength);
        var b = MainBuildingFootprint;

        // The public road along the south of the site, plus a road along each other side the
        // entrances face, so cars and ambulances can reach them.
        var sides = new[] { WallSide.South, plan.EntranceSide, plan.AmbulanceSide }.Distinct();
        foreach (var side in sides)
        {
            site.Fill(Road(b, side, RoadOffsetFor(plan, side)), TileKind.Road);
        }

        // Paved apron around the building.
        site.Fill(new GridRect(b.X - Apron, b.Y - Apron, b.Width + (Apron * 2), b.Length + (Apron * 2)), TileKind.Paving);

        // Front path from the main entrance out to the road.
        var path = FrontPath(b, plan);
        site.Fill(path, TileKind.Paving);

        // The ambulance bays against the chosen wall, opening onto the road.
        var block = BayBlock(b, plan);
        site.Fill(block, TileKind.AmbulanceBay);

        // The car park beside the front path, on whichever side is clear.
        if (CarPark(b, plan, block) is { } park)
        {
            site.Fill(park, TileKind.Parking);
        }

        var building = new Building("main", "Main Hospital Building", b);
        building.AddEntrance(new Entrance("main_entrance", "Main entrance", DoorTile(b, plan.EntranceSide, plan.EntranceAt), plan.EntranceSide, DoorWidth, false));
        building.AddEntrance(new Entrance("ambulance_entrance", "Ambulance entrance", DoorTile(b, plan.AmbulanceSide, plan.AmbulanceAt), plan.AmbulanceSide, DoorWidth, true));
        site.AddBuilding(building);
        site.Layout = new SiteLayout(plan, Bays(block, plan.AmbulanceSide), Lane(b, plan), WalkInSpawn(b, plan));
        return site;
    }

    /// <summary>The floor tile just inside the first tile of a doorway <paramref name="at"/> tiles along a wall.</summary>
    public static GridCoord DoorTile(GridRect b, WallSide side, int at) => side switch
    {
        WallSide.North => new GridCoord(b.X + at, b.Y),
        WallSide.South => new GridCoord(b.X + at, b.Bottom - 1),
        WallSide.West => new GridCoord(b.X, b.Y + at),
        _ => new GridCoord(b.Right - 1, b.Y + at),
    };

    private static int RoadOffsetFor(SitePlan plan, WallSide side) => side == plan.AmbulanceSide ? RoadOffset : FrontRoadOffset;

    /// <summary>A road running the length of the site, <paramref name="offset"/> tiles out from a wall.</summary>
    private static GridRect Road(GridRect b, WallSide side, int offset) => side switch
    {
        WallSide.North => new GridRect(0, b.Y - offset - RoadWidth, SiteWidth, RoadWidth),
        WallSide.South => new GridRect(0, b.Bottom + offset, SiteWidth, RoadWidth),
        WallSide.West => new GridRect(b.X - offset - RoadWidth, 0, RoadWidth, SiteLength),
        _ => new GridRect(b.Right + offset, 0, RoadWidth, SiteLength),
    };

    /// <summary>
    /// A strip outside a wall: <paramref name="along"/> tiles from the start of the wall for
    /// <paramref name="length"/> tiles, from <paramref name="from"/> to <paramref name="to"/> tiles out.
    /// </summary>
    private static GridRect Outside(GridRect b, WallSide side, int along, int length, int from, int to) => side switch
    {
        WallSide.North => new GridRect(b.X + along, b.Y - to, length, to - from),
        WallSide.South => new GridRect(b.X + along, b.Bottom + from, length, to - from),
        WallSide.West => new GridRect(b.X - to, b.Y + along, to - from, length),
        _ => new GridRect(b.Right + from, b.Y + along, to - from, length),
    };

    /// <summary>Where the four bays go for a plan (all of them together).</summary>
    public static GridRect BayBlockFor(SitePlan plan) => BayBlock(MainBuildingFootprint, plan);

    /// <summary>Where the front path goes for a plan.</summary>
    public static GridRect FrontPathFor(SitePlan plan) => FrontPath(MainBuildingFootprint, plan);

    private static GridRect DoorStrip(GridRect b, WallSide side, int at) => Outside(b, side, at - 1, DoorWidth + 2, 0, Apron);

    private static GridRect FrontPath(GridRect b, SitePlan plan) =>
        Outside(b, plan.EntranceSide, plan.EntranceAt, DoorWidth, Apron, RoadOffsetFor(plan, plan.EntranceSide));

    /// <summary>All the bays together, centred on the ambulance doors.</summary>
    private static GridRect BayBlock(GridRect b, SitePlan plan)
    {
        var centre = plan.AmbulanceAt + (DoorWidth / 2);
        return Outside(b, plan.AmbulanceSide, centre - (AmbulanceBays * BayWidth / 2), AmbulanceBays * BayWidth, BayOffset, BayOffset + BayLength);
    }

    private static IReadOnlyList<GridRect> Bays(GridRect block, WallSide side) => Enumerable.Range(0, AmbulanceBays)
        .Select(i => side is WallSide.East or WallSide.West
            ? new GridRect(block.X, block.Y + (i * BayWidth), BayLength, BayWidth)
            : new GridRect(block.X + (i * BayWidth), block.Y, BayWidth, BayLength))
        .ToArray();

    /// <summary>
    /// The car park next to the front path: before it along the wall if there is room, otherwise after it.
    /// Null when the bays leave no room on either side.
    /// </summary>
    private static GridRect? CarPark(GridRect b, SitePlan plan, GridRect bays)
    {
        var depth = RoadOffsetFor(plan, plan.EntranceSide);
        var site = new GridRect(0, 0, SiteWidth, SiteLength);
        foreach (var along in new[] { plan.EntranceAt - CarParkLength - 2, plan.EntranceAt + DoorWidth + 2 })
        {
            var park = Outside(b, plan.EntranceSide, along, CarParkLength, Apron, depth);
            var inside = park.X >= 0 && park.Y >= 0 && park.Right <= site.Right && park.Bottom <= site.Bottom;
            if (inside && !park.Intersects(Grown(bays, 1)) && !Road(b, plan.AmbulanceSide, RoadOffset).Intersects(park))
            {
                return park;
            }
        }

        return null;
    }

    /// <summary>
    /// The ambulances' drive: up the east or west road from the south edge of the site, or along the
    /// north or south road from the east edge, on the middle line of the road beside the bays.
    /// </summary>
    private static AmbulanceLane Lane(GridRect b, SitePlan plan)
    {
        var road = Road(b, plan.AmbulanceSide, RoadOffset);
        return plan.AmbulanceSide switch
        {
            WallSide.East or WallSide.West => new AmbulanceLane(new GridCoord(road.X + (RoadWidth / 2), SiteLength - 1), new GridCoord(road.X + (RoadWidth / 2), 0)),
            _ => new AmbulanceLane(new GridCoord(SiteWidth - 1, road.Y + (RoadWidth / 2)), new GridCoord(0, road.Y + (RoadWidth / 2))),
        };
    }

    /// <summary>Where people on foot arrive: on the road at the end of the front path.</summary>
    private static GridCoord WalkInSpawn(GridRect b, SitePlan plan)
    {
        var path = FrontPath(b, plan);
        return plan.EntranceSide switch
        {
            WallSide.North => new GridCoord(path.X + (DoorWidth / 2), path.Y - 2),
            WallSide.South => new GridCoord(path.X + (DoorWidth / 2), path.Bottom + 2),
            WallSide.West => new GridCoord(path.X - 2, path.Y + (DoorWidth / 2)),
            _ => new GridCoord(path.Right + 2, path.Y + (DoorWidth / 2)),
        };
    }

    private static GridRect Grown(GridRect r, int by) => new(r.X - by, r.Y - by, r.Width + (by * 2), r.Length + (by * 2));
}
