using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>Raised whenever rooms, corridors or the building change, so the view can redraw.</summary>
public sealed record LayoutChanged : IGameEvent;

/// <summary>
/// A room the player wants to build: template, footprint and which wall the door is in. When
/// <see cref="DoorStart"/> and <see cref="DoorRun"/> are set, the door is centred on that stretch of the wall
/// (tiles from the room's corner) rather than on the whole wall.
/// </summary>
public sealed record RoomPlan(string TemplateId, GridRect Rect, WallSide DoorSide, int? DoorStart = null, int? DoorRun = null);

/// <summary>
/// The answer to "can I build this here?": the cost, every reason it can't be built (empty if it
/// can) and the furnished room it would make, for the preview.
/// </summary>
public sealed record BuildCheck(int Cost, IReadOnlyList<string> Problems, CareSpace? Preview = null)
{
    public bool CanBuild => Problems.Count == 0;
}

/// <summary>A room as stored in a save file.</summary>
/// <remarks>
/// A waiting room also stores what the player has fitted: <see cref="Desk"/> and <see cref="Doors"/>. Saves made before
/// waiting rooms were fitted out by hand have neither, and are laid out the old way and converted when loaded.
/// </remarks>
public sealed record RoomState(string Id, string Name, string Template, int X, int Y, int Width, int Length, WallSide DoorSide, int? DoorStart = null, int? DoorRun = null)
{
    public ReceptionDesk? Desk { get; init; }

    public IReadOnlyList<Doorway>? Doors { get; init; }
}

/// <summary>An area of ground as stored in a save file.</summary>
public sealed record AreaState(int X, int Y, int Width, int Length);

/// <summary>Everything the player has built, and their money, as stored in a save file.</summary>
public sealed class LayoutState
{
    public long Balance { get; init; }

    /// <summary>Floors in the building, counting the ground floor. Saves made before floors were added have 1.</summary>
    public int Floors { get; init; } = 1;

    public IReadOnlyList<AreaState> BuildingExtensions { get; init; } = Array.Empty<AreaState>();

    public IReadOnlyList<RoomState> Rooms { get; init; } = Array.Empty<RoomState>();

    /// <summary>Chairs placed in corridors by older saves (tiles only); newer saves use <see cref="Chairs"/>.</summary>
    public IReadOnlyList<GridCoord> CorridorChairs { get; init; } = Array.Empty<GridCoord>();

    /// <summary>Waiting chairs in waiting rooms and corridors, with the way they face and their colour.</summary>
    public IReadOnlyList<PlacedChair> Chairs { get; init; } = Array.Empty<PlacedChair>();
}

/// <summary>Building: placing, moving and demolishing rooms and corridors, extending the building, and paying for it.</summary>
public sealed partial class HospitalSimulation
{
    /// <summary>
    /// Money a new hospital starts with, in pounds. It includes about £75,000 for the waiting room, which
    /// the player now builds and fits out themselves (it used to come free with the hospital).
    /// </summary>
    public const long StartingBalance = 825_000;

    /// <summary>Cost of extending the building, per square metre of new floor.</summary>
    public const int ExtensionCostPerSquareMetre = 1_400;

    /// <summary>Share of a room's cost given back when it is demolished.</summary>
    public const double DemolitionRefund = 0.5;

    /// <summary>Moving a room costs this share of its value, plus any extra size.</summary>
    public const double MoveFee = 0.1;

    /// <summary>Tiles in front of an entrance that must stay clear for people and ambulances.</summary>
    private const int EntranceApproach = 4;

    /// <summary>Rooms the hospital cannot run without; the last one can't be demolished. (None: even the waiting room can go.)</summary>
    private static readonly CareSpaceKind[] RequiredKinds = Array.Empty<CareSpaceKind>();

    private int _nextRoomNumber = 1;

    /// <summary>Money available to build with, in pounds.</summary>
    public long Balance { get; private set; } = StartingBalance;

    public Building MainBuilding => Site.Buildings[0];

    /// <summary>Lays out (but does not build) a room from a plan.</summary>
    public CareSpace Furnish(RoomPlan plan, string? id = null, string? name = null)
    {
        var template = Database.RoomTemplates[plan.TemplateId];
        if (template.Kind == CareSpaceKind.Waiting)
        {
            // Built empty: the player places the desk, doors and chairs.
            var fittings = id is not null && _fittings.TryGetValue(id, out var placed) ? placed : WaitingFittings.Empty;
            return RoomLayoutEngine.Build(template, Database, id ?? "preview", name ?? template.Name, plan.Rect, plan.DoorSide, fittings: fittings);
        }

        var extra = template.DoorBothEnds ? StreetDoors(plan.Rect) : null;
        return RoomLayoutEngine.Build(template, Database, id ?? "preview", name ?? template.Name, plan.Rect, plan.DoorSide, plan.DoorStart, plan.DoorRun, extra);
    }

    /// <summary>
    /// Plans a room dragged out over <paramref name="rect"/>: the door goes on the wall with the longest
    /// stretch onto a corridor (or the waiting room), centred on that stretch.
    /// </summary>
    public RoomPlan AutoPlan(string templateId, GridRect rect, string? replacingId = null)
    {
        var template = Database.RoomTemplates[templateId];
        if (template.IsCorridor)
        {
            return new RoomPlan(templateId, rect, WallSide.South);
        }

        var replacing = replacingId is null ? null : FindSpace(replacingId);
        bool Open(GridCoord t) => _spaces.Values.Any(s => s != replacing && s.Rect.Contains(t)
            && (s.Kind == CareSpaceKind.Corridor || (s.Kind == CareSpaceKind.Waiting && template.Kind != CareSpaceKind.Waiting)));

        // A lift or staircase has its door in the same place on every floor: choose it from the best floor.
        var floors = template.IsShaft ? Enumerable.Range(0, Site.FloorCount).Select(f => Site.OnFloor(Site.Local(rect), f)).ToList() : new List<GridRect> { rect };
        (WallSide Side, int Start, int Run)? best = null;
        foreach (var (side, onFloor) in new[] { WallSide.South, WallSide.East, WallSide.North, WallSide.West }.SelectMany(side => floors.Select(f => (side, f))))
        {
            var r = onFloor;
            var length = side is WallSide.North or WallSide.South ? rect.Width : rect.Length;
            var run = 0;
            for (var i = 0; i < length; i++)
            {
                var outside = side switch
                {
                    WallSide.North => new GridCoord(r.X + i, r.Y - 1),
                    WallSide.South => new GridCoord(r.X + i, r.Bottom),
                    WallSide.West => new GridCoord(r.X - 1, r.Y + i),
                    _ => new GridCoord(r.Right, r.Y + i),
                };

                // The corner tiles of a wall can't take a door, so they don't count.
                run = i > 0 && i < length - 1 && Open(outside) ? run + 1 : 0;
                if (run > 0 && (best is null || run > best.Value.Run))
                {
                    best = (side, i - run + 1, run);
                }
            }
        }

        return best is { } b ? new RoomPlan(templateId, rect, b.Side, b.Start, b.Run) : new RoomPlan(templateId, rect, WallSide.South, 1, 0);
    }

    /// <summary>Checks a room dragged out over <paramref name="rect"/>, with its door placed automatically.</summary>
    public BuildCheck CheckRoomAt(string templateId, GridRect rect, string? replacingId = null) =>
        CheckRoom(AutoPlan(templateId, rect, replacingId), replacingId);

    /// <summary>Doorways where a room's wall lines up with an entrance in the building's outer wall.</summary>
    private List<Doorway> StreetDoors(GridRect rect)
    {
        var doors = new List<Doorway>();
        foreach (var e in MainBuilding.Entrances.Where(e => !e.IsAmbulanceEntrance))
        {
            var tiles = Enumerable.Range(0, e.WidthTiles)
                .Select(i => e.Side is WallSide.North or WallSide.South ? new GridCoord(e.Tile.X + i, e.Tile.Y) : new GridCoord(e.Tile.X, e.Tile.Y + i))
                .Where(t => rect.Contains(t) && e.Side switch
                {
                    WallSide.North => t.Y == rect.Y,
                    WallSide.South => t.Y == rect.Bottom - 1,
                    WallSide.West => t.X == rect.X,
                    _ => t.X == rect.Right - 1,
                })
                .ToList();
            if (tiles.Count > 0)
            {
                var offset = e.Side is WallSide.North or WallSide.South ? tiles.Min(t => t.X) - rect.X : tiles.Min(t => t.Y) - rect.Y;
                doors.Add(new Doorway(e.Side, offset, tiles.Count));
            }
        }

        return doors;
    }

    // ------------------------------------------------------------------ checks

    /// <summary>Checks whether a room can be built (or an existing room moved to <paramref name="plan"/>).</summary>
    public BuildCheck CheckRoom(RoomPlan plan, string? replacingId = null)
    {
        var problems = new List<string>();
        if (!Database.RoomTemplates.TryGetValue(plan.TemplateId, out var template))
        {
            return new BuildCheck(0, new[] { $"Unknown room type '{plan.TemplateId}'." });
        }

        if (template.IsShaft)
        {
            return CheckShaft(template, plan, replacingId);
        }

        var room = Furnish(plan);
        var replacing = replacingId is null ? null : FindSpace(replacingId);
        var cost = template.IsCorridor ? CorridorCost(template, plan.Rect, replacing) : replacing is null ? room.BuildCost : MoveCost(replacing, room);

        CheckSize(template, plan, problems);
        if (!IsUnlocked(template.Department) && Database.Departments.TryGetValue(template.Department, out var locked))
        {
            problems.Add($"Unlock {locked.Name} first (£{locked.OpeningCost:N0}, in the Departments tab).");
        }
        if (!plan.Rect.Tiles().All(Site.IsIndoors))
        {
            problems.Add(Site.FloorOf(plan.Rect) == 0 ? "Must be inside the building. Extend the building first." : "Must be inside the building's outline on this floor.");
        }

        // Corridors may overlap other corridors (they simply join up); nothing else may overlap.
        if (_spaces.Values.Where(s => s != replacing && !(template.IsCorridor && s.Kind == CareSpaceKind.Corridor))
                .FirstOrDefault(s => s.Rect.Intersects(plan.Rect)) is { } overlap)
        {
            problems.Add($"Overlaps {overlap.Name}.");
        }

        // A corridor is open floor, so people can carry on walking across it while it is laid.
        if (!template.IsCorridor && AgentsOn(plan.Rect, replacing).Any())
        {
            problems.Add("Someone is standing there.");
        }

        if (template.IsCorridor)
        {
            if (!JoinsCirculation(plan.Rect, replacing))
            {
                problems.Add("Must join an existing corridor or an entrance.");
            }
            else if (NewCorridorTiles(plan.Rect, replacing) == 0)
            {
                problems.Add("That is all corridor already.");
            }
        }
        else
        {
            CheckDoors(room, replacing, problems);
        }

        if (template.Layout == RoomLayoutKind.Ward && room.Beds.Count == 0)
        {
            problems.Add("No space for a bed here. Make the ward longer.");
        }

        if (replacing is not null)
        {
            problems.AddRange(CheckRoomIsClear(replacing));
        }

        if (problems.Count == 0)
        {
            var trial = _spaces.Values.Where(s => s != replacing).Append(room).ToList();
            problems.AddRange(CheckReachable(trial));
        }

        if (!CanAfford(cost))
        {
            problems.Add($"Not enough money: costs £{cost:N0}, you have £{Balance:N0}.");
        }

        return new BuildCheck(cost, problems, room);
    }

    internal static void CheckSize(RoomTemplate template, RoomPlan plan, List<string> problems)
    {
        var (w, d) = RoomLayoutEngine.LocalSize(plan.Rect, plan.DoorSide);
        if (template.IsCorridor)
        {
            var narrow = Units.TilesToMetres(Math.Min(plan.Rect.Width, plan.Rect.Length));
            if (narrow < template.MinWidthM)
            {
                problems.Add($"Too narrow: corridors must be at least {template.MinWidthM:0.#} m wide.");
            }

            return;
        }

        // Rooms are dragged out, so either side may be the long one.
        var (wm, dm) = (Units.TilesToMetres(Math.Min(w, d)), Units.TilesToMetres(Math.Max(w, d)));
        var (minShort, minLong) = (Math.Min(template.MinWidthM, template.MinLengthM), Math.Max(template.MinWidthM, template.MinLengthM));
        var (maxShort, maxLong) = (Math.Min(template.MaxWidthM, template.MaxLengthM), Math.Max(template.MaxWidthM, template.MaxLengthM));
        if (wm < minShort - 1e-6 || dm < minLong - 1e-6)
        {
            problems.Add($"Too small: a {template.Name.ToLowerInvariant()} needs at least {template.MinWidthM:0.#} × {template.MinLengthM:0.#} m (this is {Units.TilesToMetres(plan.Rect.Width):0.#} × {Units.TilesToMetres(plan.Rect.Length):0.#} m).");
        }

        if (wm > maxShort + 1e-6 || dm > maxLong + 1e-6)
        {
            problems.Add($"Too big: a {template.Name.ToLowerInvariant()} can be at most {template.MaxWidthM:0.#} × {template.MaxLengthM:0.#} m.");
        }
    }

    /// <summary>Each door must open onto a corridor or the waiting room (a waiting room's second door may open to the street).</summary>
    internal void CheckDoors(CareSpace room, CareSpace? replacing, List<string> problems)
    {
        for (var i = 0; i < room.Doors.Count; i++)
        {
            var door = room.Doors[i];
            var outside = DoorTiles(room, door).Select(t => Step(t, door.Side)).ToList();
            var ok = outside.All(o =>
                _spaces.Values.Any(s => s != replacing && s.Kind is CareSpaceKind.Corridor or CareSpaceKind.Waiting && s.Rect.Contains(o)));
            var toStreet = i > 0 && DoorTiles(room, door).All(t => MainBuilding.IsOpening(t, door.Side));
            if (!ok && !toStreet)
            {
                problems.Add(i == 0 ? "Must touch a corridor so it can have a door." : "The second door must open onto a corridor or the main entrance.");
            }
        }
    }

    /// <summary>A corridor must touch another corridor or a waiting room, or take in an entrance.</summary>
    private bool JoinsCirculation(GridRect rect, CareSpace? replacing)
    {
        if (MainBuilding.Entrances.Any(e => rect.Contains(e.Tile)))
        {
            return true;
        }

        // Upstairs, corridors start from the landing outside a lift or staircase door.
        if (_spaces.Values.Where(s => s.IsShaft).SelectMany(s => s.Doors.SelectMany(d => DoorTiles(s, d).Select(t => Step(t, d.Side)))).Any(rect.Contains))
        {
            return true;
        }

        var grown = new GridRect(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Length + 2);
        return _spaces.Values.Any(s => s != replacing && s.Kind is CareSpaceKind.Corridor or CareSpaceKind.Waiting
                                       && s.Rect.Tiles().Any(t => grown.Contains(t) && !IsCorner(grown, t)));
    }

    private static bool IsCorner(GridRect r, GridCoord t) =>
        (t.X == r.X || t.X == r.Right - 1) && (t.Y == r.Y || t.Y == r.Bottom - 1);

    /// <summary>Why a room can't be moved or demolished right now, if anything.</summary>
    private IEnumerable<string> CheckRoomIsClear(CareSpace space)
    {
        if (space.OccupantId is not null || _patients.Values.Any(p => p.CareSpaceId == space.Id))
        {
            yield return $"{space.Name} has a patient in it.";
        }
        else if (space.Beds.Any(b => b.OccupantId is not null) || _patients.Values.Any(p => p.ReservedWardId == space.Id))
        {
            yield return $"A bed on {space.Name} is booked for a patient in A&E.";
        }
        else if (space.ReservedByStaffId is not null || _tasks.Values.Any(t => t.SpaceId == space.Id))
        {
            yield return $"{space.Name} is being used.";
        }
        else if (_patients.Values.Any(p => InsideOrHeading(p, space.Rect)))
        {
            yield return $"Patients are in {space.Name}.";
        }
        else if (_staff.Any(s => s.CurrentTaskId is not null && InsideOrHeading(s, space.Rect)))
        {
            yield return $"Staff are working in {space.Name}.";
        }
    }

    private static bool InsideOrHeading(Agent agent, GridRect rect) =>
        rect.Contains(agent.Tile) || (agent.Destination is { } d && rect.Contains(d));

    private IEnumerable<Agent> AgentsOn(GridRect rect, CareSpace? ignoringRoom) =>
        _patients.Values.Where(p => p.Stage != PatientStage.Gone).Cast<Agent>().Concat(_staff)
            .Where(a => rect.Contains(a.Tile) && (ignoringRoom is null || !ignoringRoom.Rect.Contains(a.Tile)));

    /// <summary>Everyone must still be able to walk from the street to every room, and to the ambulance bay.</summary>
    private IEnumerable<string> CheckReachable(IReadOnlyCollection<CareSpace> spaces)
    {
        var nav = EmergencyDepartmentLayout.BuildNavGrid(Site, spaces);
        var reached = nav.Reachable(WalkInSpawn);
        var stepFree = Site.FloorCount > 1 ? nav.Reachable(WalkInSpawn, stepFreeOnly: true) : reached;

        // A waiting room built over the main entrance is reached through the entrance doors the player puts in it.
        foreach (var entrance in MainBuilding.Entrances.Where(e => !reached.Contains(e.Tile) && !spaces.Any(s => s.Kind == CareSpaceKind.Waiting && s.Rect.Contains(e.Tile))))
        {
            yield return $"Would block the {entrance.Name.ToLowerInvariant()}.";
        }

        foreach (var tile in AmbulanceBayPatientTiles.Where(t => !reached.Contains(t)).Take(1))
        {
            yield return "Would block the ambulance bay.";
        }

        // A waiting room with no doors yet is still being fitted out.
        foreach (var space in spaces.Where(s => s.Kind != CareSpaceKind.Corridor && !(s.Kind == CareSpaceKind.Waiting && s.Doors.Count == 0)))
        {
            var spots = new[] { space.StaffSpot, space.PatientSpot }.Concat(space.Seats).Concat(space.QueueTiles)
                .Concat(space.Beds.SelectMany(b => new[] { b.PatientSpot, b.StaffSpot }));
            if (spots.Any(t => !reached.Contains(t)))
            {
                yield return $"{space.Name} could not be reached.";
            }
            else if (NeedsLift(space) && !stepFree.Contains(space.PatientSpot) && !space.Beds.All(b => stepFree.Contains(b.PatientSpot)))
            {
                yield return $"{space.Name} needs a lift: wheelchairs and trolleys can't use the stairs.";
            }
        }
    }

    private static GridCoord Step(GridCoord c, WallSide side) => side switch
    {
        WallSide.North => new GridCoord(c.X, c.Y - 1),
        WallSide.South => new GridCoord(c.X, c.Y + 1),
        WallSide.East => new GridCoord(c.X + 1, c.Y),
        _ => new GridCoord(c.X - 1, c.Y),
    };

    /// <summary>The floor tiles just inside a doorway.</summary>
    private static IEnumerable<GridCoord> DoorTiles(CareSpace room, Doorway door)
    {
        var r = room.Rect;
        for (var i = door.Offset; i < door.Offset + door.Width; i++)
        {
            yield return door.Side switch
            {
                WallSide.North => new GridCoord(r.X + i, r.Y),
                WallSide.South => new GridCoord(r.X + i, r.Bottom - 1),
                WallSide.West => new GridCoord(r.X, r.Y + i),
                _ => new GridCoord(r.Right - 1, r.Y + i),
            };
        }
    }

    /// <summary>Tiles of <paramref name="rect"/> not already covered by another corridor.</summary>
    private int NewCorridorTiles(GridRect rect, CareSpace? except)
    {
        var others = _spaces.Values.Where(s => s != except && s.Kind == CareSpaceKind.Corridor && s.Rect.Intersects(rect)).ToList();
        return rect.Tiles().Count(t => !others.Any(o => o.Rect.Contains(t)));
    }

    /// <summary>A corridor costs only the floor it adds: tiles already corridor are free.</summary>
    private int CorridorCost(RoomTemplate template, GridRect rect, CareSpace? except) =>
        (int)Math.Round(template.CostPerSquareMetre * NewCorridorTiles(rect, except) * Units.MetresPerTile * Units.MetresPerTile);

    private static int MoveCost(CareSpace old, CareSpace moved) =>
        Math.Max(0, moved.BuildCost - old.BuildCost) + (int)Math.Round(moved.BuildCost * MoveFee);

    /// <summary>Checks whether a room or corridor can be demolished, and what would be refunded (as a negative cost).</summary>
    public BuildCheck CheckDemolish(string id)
    {
        if (FindSpace(id) is not { } space)
        {
            return new BuildCheck(0, new[] { "That room no longer exists." });
        }

        var parts = ShaftOf(space);
        var problems = parts.SelectMany(CheckRoomIsClear).Distinct().ToList();
        if (RequiredKinds.Contains(space.Kind) && SpacesOfKind(space.Kind).Count() == 1)
        {
            problems.Add($"The department needs at least one {space.Name.ToLowerInvariant()}.");
        }

        if (problems.Count == 0)
        {
            problems.AddRange(CheckReachable(_spaces.Values.Where(s => !parts.Contains(s)).ToList()));
        }

        var value = space.Kind == CareSpaceKind.Corridor && Database.RoomTemplates.TryGetValue(space.TemplateId, out var corridor)
            ? CorridorCost(corridor, space.Rect, space)
            : parts.Sum(p => p.BuildCost);
        return new BuildCheck(-(int)Math.Round(value * DemolitionRefund), problems);
    }

    /// <summary>Checks whether the building can be extended over <paramref name="area"/>.</summary>
    public BuildCheck CheckExtension(GridRect area)
    {
        var problems = new List<string>();
        var building = MainBuilding;
        var newTiles = area.Tiles().Where(t => !building.Contains(t)).ToList();
        var cost = (int)Math.Round(newTiles.Count * Units.MetresPerTile * Units.MetresPerTile * ExtensionCostPerSquareMetre * Site.FloorCount);

        if (area.Width < 2 || area.Length < 2)
        {
            problems.Add("Too small: an extension must be at least 1 × 1 m.");
        }

        if (!Site.InBounds(new GridCoord(area.X, area.Y)) || !Site.InBounds(new GridCoord(area.Right - 1, area.Bottom - 1)))
        {
            problems.Add("Must be inside the hospital site.");
            return new BuildCheck(cost, problems);
        }

        if (newTiles.Count == 0)
        {
            problems.Add("That area is already part of the building.");
        }
        else if (!newTiles.Any(t => new[] { WallSide.North, WallSide.East, WallSide.South, WallSide.West }.Any(s => building.Contains(Step(t, s)))))
        {
            problems.Add("Must join on to the building.");
        }

        var unbuildable = newTiles.Select(t => Site[t]).Where(k => k is not (TileKind.Grass or TileKind.Paving)).Distinct().ToList();
        foreach (var kind in unbuildable)
        {
            var what = kind switch
            {
                TileKind.AmbulanceBay => "ambulance bay",
                TileKind.Parking => "car park",
                TileKind.Road => "road",
                _ => "existing building",
            };
            problems.Add($"Can't build over the {what}.");
        }

        foreach (var entrance in building.Entrances.Where(e => ApproachOf(e).Intersects(area)))
        {
            problems.Add($"Would block the approach to the {entrance.Name.ToLowerInvariant()}.");
        }

        if (AmbulanceBayPatientTiles.Concat(AmbulanceParkingTiles).Append(WalkInSpawn).Any(area.Contains))
        {
            problems.Add("Would block the ambulance bay.");
        }

        if (AgentsOn(area, null).Any(a => !building.Contains(a.Tile)) || _ambulances.Any(a => area.Contains(a.Tile)))
        {
            problems.Add("Someone is standing there.");
        }

        if (!CanAfford(cost))
        {
            problems.Add($"Not enough money: costs £{cost:N0}, you have £{Balance:N0}.");
        }

        return new BuildCheck(cost, problems);
    }

    /// <summary>The strip of ground outside an entrance that must be kept clear.</summary>
    private static GridRect ApproachOf(Entrance e) => e.Side switch
    {
        WallSide.North => new GridRect(e.Tile.X - 1, e.Tile.Y - EntranceApproach, e.WidthTiles + 2, EntranceApproach),
        WallSide.South => new GridRect(e.Tile.X - 1, e.Tile.Y + 1, e.WidthTiles + 2, EntranceApproach),
        WallSide.West => new GridRect(e.Tile.X - EntranceApproach, e.Tile.Y - 1, EntranceApproach, e.WidthTiles + 2),
        _ => new GridRect(e.Tile.X + 1, e.Tile.Y - 1, EntranceApproach, e.WidthTiles + 2),
    };

    // ------------------------------------------------------------------ actions

    /// <summary>Builds a room or corridor if the check passes. Returns the check either way.</summary>
    public BuildCheck BuildRoom(RoomPlan plan, string? name = null)
    {
        var check = CheckRoom(plan);
        if (!check.CanBuild)
        {
            return check;
        }

        var template = Database.RoomTemplates[plan.TemplateId];
        if (template.IsShaft)
        {
            BuildShaft(template, plan, name ?? NextName(template), check.Cost);
            return check;
        }

        var id = NewRoomId(template);
        var room = Furnish(plan, id, name ?? NextName(template));
        _spaces[id] = room;
        Book(MoneyCategory.Construction, -check.Cost);
        Notifications.Add(Now, NotificationLevel.Info, $"Built {room.Name} ({room.AreaSquareMetres:0} m²) for £{check.Cost:N0}.");
        AfterLayoutChange();
        return check;
    }

    /// <summary>Moves (and possibly resizes or turns) an existing room. It keeps its id and name.</summary>
    public BuildCheck MoveRoom(string id, RoomPlan plan)
    {
        var check = CheckRoom(plan, id);
        if (!check.CanBuild)
        {
            return check;
        }

        var old = _spaces[id];
        if (old.Kind == CareSpaceKind.Waiting)
        {
            MoveFittings(old, plan.Rect);
        }

        var moved = Furnish(plan, id, old.Name);
        moved.AllowedDepartments.UnionWith(old.AllowedDepartments);
        _spaces[id] = moved;
        Book(MoneyCategory.Construction, -check.Cost);
        Notifications.Add(Now, NotificationLevel.Info, $"Rebuilt {moved.Name} for £{check.Cost:N0}.");
        AfterLayoutChange();
        return check;
    }

    public BuildCheck DemolishRoom(string id)
    {
        var check = CheckDemolish(id);
        if (!check.CanBuild)
        {
            return check;
        }

        var space = _spaces[id];
        foreach (var part in ShaftOf(space))
        {
            _spaces.Remove(part.Id);
            _fittings.Remove(part.Id);
        }

        Book(MoneyCategory.Construction, -check.Cost);
        Notifications.Add(Now, NotificationLevel.Info, $"Demolished {space.Name}; £{-check.Cost:N0} refunded.");
        AfterLayoutChange();
        return check;
    }

    public BuildCheck ExtendBuilding(GridRect area)
    {
        var check = CheckExtension(area);
        if (!check.CanBuild)
        {
            return check;
        }

        Site.ExtendBuilding(MainBuilding, area);
        Book(MoneyCategory.Construction, -check.Cost);
        var storeys = Site.FloorCount > 1 ? $" on all {Site.FloorCount} floors" : string.Empty;
        Notifications.Add(Now, NotificationLevel.Info, $"Extended the building by {Units.AreaSquareMetres(area.Width, area.Length):0} m²{storeys} for £{check.Cost:N0}.");
        AfterLayoutChange();
        return check;
    }

    public void RenameRoom(string id, string name)
    {
        if (FindSpace(id) is { } space && !string.IsNullOrWhiteSpace(name))
        {
            foreach (var part in ShaftOf(space))
            {
                part.Name = name.Trim();
            }

            Events.Publish(new LayoutChanged());
        }
    }

    /// <summary>Adds or removes money, e.g. for running costs. Used by tests and scenarios.</summary>
    public void AdjustBalance(long amount) => Balance += amount;

    private string NewRoomId(RoomTemplate template)
    {
        string id;
        do
        {
            id = $"{template.Id}_{_nextRoomNumber++}";
        }
        while (_spaces.ContainsKey(id));

        return id;
    }

    private string NextName(RoomTemplate template)
    {
        var baseName = template.Kind switch
        {
            CareSpaceKind.Minors => "Minors",
            CareSpaceKind.Majors => "Majors",
            CareSpaceKind.Resus => "Resus",
            CareSpaceKind.Triage => "Triage",
            CareSpaceKind.Ward when template.Department == "general_medicine" => "Ward",
            CareSpaceKind.Ward when template.Department == EmergencyDepartmentId => "A&E ward",
            CareSpaceKind.Ward => template.Name,
            CareSpaceKind.Theatre when template.TheatreDepartments.Count > 0 => template.Name,
            CareSpaceKind.Theatre => "Theatre",
            CareSpaceKind.Pharmacy => "Pharmacy",
            _ => template.Name,
        };
        var n = 1;
        while (_spaces.Values.Any(s => s.Name == $"{baseName} {n}"))
        {
            n++;
        }

        return $"{baseName} {n}";
    }

    /// <summary>Rebuilds pathfinding and sends anyone whose route or base changed on their way again.</summary>
    private void AfterLayoutChange()
    {
        Nav = EmergencyDepartmentLayout.BuildNavGrid(Site, _spaces.Values);
        SyncRoomStaff();
        PruneChairs();

        foreach (var agent in _patients.Values.Cast<Agent>().Concat(_staff).Concat(_outpatients))
        {
            if (agent.Destination is { } destination)
            {
                var path = PathFor(agent, destination);
                if (path.Count > 0)
                {
                    agent.FollowPath(path);
                }
            }
        }

        foreach (var member in _staff)
        {
            if (member.Activity is StaffActivity.OnBreak or StaffActivity.WalkingToBreak
                && !_toiletTrips.ContainsKey(member)
                && !SpacesOfKind(CareSpaceKind.StaffRoom).Any(r => r.Seats.Contains(member.Destination ?? member.Tile)))
            {
                member.Activity = StaffActivity.Idle;
                member.BreakEndsAt = null;
            }

            if (member.Activity == StaffActivity.Idle && member.CurrentTaskId is null)
            {
                var home = HomeTile(member);
                var path = Nav.FindPath(member.Tile, home);
                if (path.Count > 0)
                {
                    member.FollowPath(path);
                }
            }
        }

        Events.Publish(new LayoutChanged());
    }

    // ------------------------------------------------------------------ saving

    public LayoutState CaptureLayout() => new()
    {
        Balance = Balance,
        Floors = Site.FloorCount,
        BuildingExtensions = MainBuilding.Extensions.Select(e => new AreaState(e.X, e.Y, e.Width, e.Length)).ToList(),
        Rooms = _spaces.Values.OrderBy(s => s.Id, StringComparer.Ordinal).Select(s =>
        {
            var (w, d) = RoomLayoutEngine.LocalSize(s.Rect, s.DoorSide);
            if (s.Kind == CareSpaceKind.Waiting)
            {
                var fitted = FittingsOf(s);
                return new RoomState(s.Id, s.Name, s.TemplateId, s.Rect.X, s.Rect.Y, w, d, s.DoorSide) { Desk = fitted.Desk, Doors = fitted.Doors.ToList() };
            }

            var door = s.Doors.FirstOrDefault();
            return new RoomState(s.Id, s.Name, s.TemplateId, s.Rect.X, s.Rect.Y, w, d, s.DoorSide, door?.Offset, door?.Width);
        }).ToList(),
        Chairs = _chairs.ToList(),
    };

    /// <summary>Replaces the starter layout with a saved one. Call before the game starts running.</summary>
    /// <remarks>Corridor chairs from older saves (tiles only) face away from the nearest wall, in the default colour.</remarks>
    public void RestoreLayout(LayoutState state)
    {
        foreach (var e in state.BuildingExtensions)
        {
            Site.ExtendBuilding(MainBuilding, new GridRect(e.X, e.Y, e.Width, e.Length));
        }

        Site.SetFloorCount(state.Floors);

        _spaces.Clear();
        _fittings.Clear();
        _chairs.Clear();
        var upgraded = new List<PlacedChair>();
        foreach (var r in state.Rooms.Where(r => Database.RoomTemplates.ContainsKey(r.Template)))
        {
            var plan = new RoomPlan(r.Template, RoomLayoutEngine.RectFor(new GridCoord(r.X, r.Y), r.Width, r.Length, r.DoorSide), r.DoorSide, r.DoorStart, r.DoorRun);
            if (Database.RoomTemplates[r.Template].Kind == CareSpaceKind.Waiting && r.Doors is null)
            {
                // Saved before waiting rooms were fitted out by hand: keep its desk, doors and seats.
                var (room, chairs) = UpgradeOldWaitingRoom(plan, r.Id, r.Name);
                _spaces[room.Id] = room;
                upgraded.AddRange(chairs);
                continue;
            }

            if (Database.RoomTemplates[r.Template].Kind == CareSpaceKind.Waiting)
            {
                _fittings[r.Id] = new WaitingFittings(r.Desk, r.Doors!.ToList());
            }

            _spaces[r.Id] = Furnish(plan, r.Id, r.Name);
        }

        Nav = EmergencyDepartmentLayout.BuildNavGrid(Site, _spaces.Values);
        _chairs.AddRange(state.Chairs.Concat(upgraded).GroupBy(c => c.Tile).Select(g => g.First() with { Colour = ChairColours.Normalise(g.First().Colour) }));
        foreach (var tile in state.CorridorChairs.Distinct().Where(t => ChairAt(t) is null))
        {
            _chairs.Add(new PlacedChair(tile, SuggestChairFacing(tile), ChairColours.Default));
        }

        PruneChairs();
        Balance = state.Balance;
        Nav = EmergencyDepartmentLayout.BuildNavGrid(Site, _spaces.Values);
        SyncRoomStaff();
        foreach (var member in _staff)
        {
            member.PlaceAt(HomeTile(member));
        }

        Events.Publish(new LayoutChanged());
    }
}
