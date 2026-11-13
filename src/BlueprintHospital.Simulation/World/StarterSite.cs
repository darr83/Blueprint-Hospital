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
    /// <summary>The marked spaces of the car park, nearest the front path first (empty when there is no car park).</summary>
    public IReadOnlyList<GridRect> ParkingBays { get; init; } = Array.Empty<GridRect>();

    /// <summary>How many of the first <see cref="ParkingBays"/> are blue badge spaces.</summary>
    public int DisabledBays { get; init; }

    /// <summary>The taxi rank: a lay-by off the front road, beside the front path (null when there is no room).</summary>
    public GridRect? TaxiRank { get; init; }

    /// <summary>Where each taxi stands in the rank, front of the rank first.</summary>
    public IReadOnlyList<GridCoord> TaxiStands { get; init; } = Array.Empty<GridCoord>();

    /// <summary>The whole car park (null when there is none).</summary>
    public GridRect? CarPark { get; init; }

    /// <summary>The bigger car park on the far side of the front road (null when there is no room).</summary>
    public GridRect? MainCarPark { get; init; }

    /// <summary>Every road on the site, each running the full width or length of it.</summary>
    public IReadOnlyList<GridRect> Roads { get; init; } = Array.Empty<GridRect>();

    /// <summary>The path from the main entrance out to the road.</summary>
    public GridRect FrontPath { get; init; }

    /// <summary>The drive visitors' cars and taxis take along the front road, on the side nearest the hospital.</summary>
    public AmbulanceLane? CarLane { get; init; }

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

    /// <summary>Each car park space is 2.5 m wide (and as deep as the strip between the paving and the road).</summary>
    public const int ParkingBayWidth = 5;

    /// <summary>The car park by the entrance has up to five spaces, without reaching past the building's corners.</summary>
    private static readonly int[] CarParkLengths = { 5 * ParkingBayWidth, 4 * ParkingBayWidth, 3 * ParkingBayWidth, 2 * ParkingBayWidth };

    /// <summary>The main car park across the road has up to eight spaces.</summary>
    private static readonly int[] MainCarParkLengths = { 8 * ParkingBayWidth, 6 * ParkingBayWidth, 4 * ParkingBayWidth };

    /// <summary>Spaces across the road are 5 m deep.</summary>
    private const int MainCarParkDepth = 10;

    /// <summary>The taxi rank holds two taxis nose to tail (5 m each) in a 2.5 m lay-by.</summary>
    public const int TaxiRankLength = 20;

    public const int TaxiRankDepth = 5;

    public const int TaxiStands = 2;

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
        var roads = sides.Select(side => Road(b, side, RoadOffsetFor(plan, side))).ToArray();
        foreach (var road in roads)
        {
            site.Fill(road, TileKind.Road);
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
        var park = CarPark(site, b, plan, block);
        if (park is { } p)
        {
            site.Fill(p, TileKind.Parking);
        }

        // The main car park, across the front road where nothing can be built, reached by the crossing.
        var mainPark = MainCarPark(site, b, plan, block);
        if (mainPark is { } mp)
        {
            site.Fill(mp, TileKind.Parking);
        }

        // The taxi rank: a lay-by off the road on the other side of the path, with pavement behind it.
        var rank = TaxiRank(site, b, plan, block);
        if (rank is { } r)
        {
            site.Fill(TaxiPavement(b, plan, r), TileKind.Paving);
            site.Fill(r, TileKind.TaxiRank);
        }

        var building = new Building("main", "Main Hospital Building", b);
        building.AddEntrance(new Entrance("main_entrance", "Main entrance", DoorTile(b, plan.EntranceSide, plan.EntranceAt), plan.EntranceSide, DoorWidth, false));
        building.AddEntrance(new Entrance("ambulance_entrance", "Ambulance entrance", DoorTile(b, plan.AmbulanceSide, plan.AmbulanceAt), plan.AmbulanceSide, DoorWidth, true));
        site.AddBuilding(building);
        var spaces = new[] { park, mainPark }.Where(r => r.HasValue).SelectMany(r => ParkingBays(r!.Value, b, plan)).ToArray();
        site.Layout = new SiteLayout(plan, Bays(block, plan.AmbulanceSide), Lane(b, plan), WalkInSpawn(b, plan))
        {
            ParkingBays = spaces,

            // One blue badge space for every four, up to two.
            DisabledBays = Math.Min(2, spaces.Length / 4),
            MainCarPark = mainPark,
            TaxiRank = rank,
            TaxiStands = rank is { } tr ? Stands(tr, plan) : Array.Empty<GridCoord>(),
            CarLane = CarLane(b, plan),
            CarPark = park,
            Roads = roads,
            FrontPath = path,
        };
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
    /// The car park next to the front path: before it along the wall if there is room, otherwise after it,
    /// as long as will fit. Null when the bays leave no room on either side.
    /// </summary>
    private static GridRect? CarPark(SiteMap site, GridRect b, SitePlan plan, GridRect bays)
    {
        var depth = RoadOffsetFor(plan, plan.EntranceSide);
        foreach (var before in new[] { true, false })
        {
            foreach (var length in CarParkLengths)
            {
                var along = before ? plan.EntranceAt - length - 2 : plan.EntranceAt + DoorWidth + 2;
                var park = Outside(b, plan.EntranceSide, along, length, Apron, depth);
                var pastCorner = along < -Apron || along + length > WallLength(plan.EntranceSide) + Apron;
                if (!pastCorner && Clear(site, park, bays))
                {
                    return park;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The main car park: a row of spaces on the far side of the front road, beside the crossing. Ground
    /// out there can never be built on, so it takes nothing from the hospital. Null when the site edge is too close.
    /// </summary>
    private static GridRect? MainCarPark(SiteMap site, GridRect b, SitePlan plan, GridRect bays)
    {
        var from = RoadOffsetFor(plan, plan.EntranceSide) + RoadWidth;
        foreach (var length in MainCarParkLengths)
        {
            foreach (var along in new[] { plan.EntranceAt + DoorWidth + 2, plan.EntranceAt - length - 2 })
            {
                var park = Outside(b, plan.EntranceSide, along, length, from, from + MainCarParkDepth);
                if (Clear(site, park, bays))
                {
                    return park;
                }
            }
        }

        return null;
    }

    /// <summary>The car park's spaces, each running from the paving out to the road, nearest the front path first.</summary>
    private static IReadOnlyList<GridRect> ParkingBays(GridRect park, GridRect b, SitePlan plan)
    {
        var horizontal = plan.EntranceSide is WallSide.North or WallSide.South;
        var count = (horizontal ? park.Width : park.Length) / ParkingBayWidth;
        var path = FrontPath(b, plan);
        var bays = Enumerable.Range(0, count).Select(i => horizontal
            ? new GridRect(park.X + (i * ParkingBayWidth), park.Y, ParkingBayWidth, park.Length)
            : new GridRect(park.X, park.Y + (i * ParkingBayWidth), park.Width, ParkingBayWidth));
        return (horizontal ? bays.OrderBy(r => Math.Abs(r.X - path.X)) : bays.OrderBy(r => Math.Abs(r.Y - path.Y))).ToArray();
    }

    /// <summary>
    /// The taxi rank: a lay-by against the front road, on whichever side of the front path is clear
    /// (the car park takes the other). Null when neither side has room.
    /// </summary>
    private static GridRect? TaxiRank(SiteMap site, GridRect b, SitePlan plan, GridRect bays)
    {
        var depth = RoadOffsetFor(plan, plan.EntranceSide);
        foreach (var along in new[] { plan.EntranceAt + DoorWidth, plan.EntranceAt - TaxiRankLength })
        {
            var rank = Outside(b, plan.EntranceSide, along, TaxiRankLength, depth - TaxiRankDepth, depth);
            if (Clear(site, Outside(b, plan.EntranceSide, along, TaxiRankLength, Apron, depth), bays))
            {
                return rank;
            }
        }

        return null;
    }

    /// <summary>The pavement between the taxi rank and the paving round the building.</summary>
    private static GridRect TaxiPavement(GridRect b, SitePlan plan, GridRect rank)
    {
        var depth = RoadOffsetFor(plan, plan.EntranceSide);
        var along = plan.EntranceSide is WallSide.North or WallSide.South ? rank.X - b.X : rank.Y - b.Y;
        return Outside(b, plan.EntranceSide, along, TaxiRankLength, Apron, depth - TaxiRankDepth);
    }

    /// <summary>Where the taxis stand: nose to tail along the rank, the one that will leave first in front.</summary>
    private static IReadOnlyList<GridCoord> Stands(GridRect rank, SitePlan plan)
    {
        var (dx, dy) = CarDirection(plan.EntranceSide);
        var slot = TaxiRankLength / TaxiStands;
        var centre = new GridCoord(rank.X + (rank.Width / 2), rank.Y + (rank.Length / 2));
        return Enumerable.Range(0, TaxiStands)
            .Select(i => (slot / 2) - (i * slot))
            .Select(o => new GridCoord(centre.X + (dx * o), centre.Y + (dy * o)))
            .ToArray();
    }

    /// <summary>True when a strip of the grounds is all grass, on the site, and clear of the ambulance bays.</summary>
    private static bool Clear(SiteMap site, GridRect area, GridRect bays) =>
        area.X >= 0 && area.Y >= 0 && area.Right <= SiteWidth && area.Bottom <= SiteLength
        && !area.Intersects(Grown(bays, 1))
        && area.Tiles().All(t => site[t] == TileKind.Grass);

    /// <summary>
    /// Which way cars drive along the front road: on the left, so the hospital side of the road is the
    /// side they pull in from.
    /// </summary>
    public static (int X, int Y) CarDirection(WallSide entranceSide) => entranceSide switch
    {
        WallSide.South => (1, 0),
        WallSide.North => (-1, 0),
        WallSide.West => (0, 1),
        _ => (0, -1),
    };

    /// <summary>The cars' drive: the length of the front road, down the middle of the lane nearest the hospital.</summary>
    private static AmbulanceLane CarLane(GridRect b, SitePlan plan)
    {
        var road = Road(b, plan.EntranceSide, RoadOffsetFor(plan, plan.EntranceSide));
        const int inset = 2;
        return plan.EntranceSide switch
        {
            WallSide.South => new AmbulanceLane(new GridCoord(0, road.Y + inset), new GridCoord(SiteWidth - 1, road.Y + inset)),
            WallSide.North => new AmbulanceLane(new GridCoord(SiteWidth - 1, road.Bottom - 1 - inset), new GridCoord(0, road.Bottom - 1 - inset)),
            WallSide.West => new AmbulanceLane(new GridCoord(road.Right - 1 - inset, 0), new GridCoord(road.Right - 1 - inset, SiteLength - 1)),
            _ => new AmbulanceLane(new GridCoord(road.X + inset, SiteLength - 1), new GridCoord(road.X + inset, 0)),
        };
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
