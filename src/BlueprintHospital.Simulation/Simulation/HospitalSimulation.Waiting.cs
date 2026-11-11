using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// Waiting rooms and waiting chairs. The player builds a waiting room as an empty room, then places its reception
/// desk, an entrance door (on the front path, where the blue mat is) and a door through to a corridor. Chairs go
/// anywhere in a waiting room or along the corridors, facing whichever way the player turns them, in the colour
/// they pick.
/// <para>
/// Patients waiting to be triaged sit in a waiting room. Everyone else waiting for something sits on the free chair
/// nearest to where they are going next (or stands nearby when every chair is taken).
/// </para>
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>Automatic sliding doors from the street into the waiting room.</summary>
    public const int EntranceDoorCost = 1_800;

    /// <summary>A pair of doors from the waiting room through to a corridor.</summary>
    public const int InnerDoorCost = 600;

    /// <summary>The most doors a waiting room can have.</summary>
    public const int MaxWaitingDoors = 4;

    /// <summary>The furniture item used for waiting chairs.</summary>
    public const string ChairItem = "waiting_seat";

    private readonly Dictionary<string, WaitingFittings> _fittings = new();
    private readonly List<PlacedChair> _chairs = new();
    private int _reseatCountdown;

    /// <summary>Waiting chairs the player has placed, in waiting rooms and along corridors.</summary>
    public IReadOnlyList<PlacedChair> Chairs => _chairs;

    public int ChairCost => Database.Furniture.TryGetValue(ChairItem, out var chair) ? chair.Cost : 0;

    /// <summary>A reception desk with its receptionist's chair.</summary>
    public int ReceptionDeskCost =>
        (Database.Furniture.TryGetValue("reception_desk", out var desk) ? desk.Cost : 0)
        + (Database.Furniture.TryGetValue("staff_chair", out var chair) ? chair.Cost : 0);

    public PlacedChair? ChairAt(GridCoord tile) => _chairs.FirstOrDefault(c => c.Tile == tile);

    /// <summary>The desk and doors placed in a waiting room.</summary>
    public WaitingFittings FittingsOf(CareSpace room) => _fittings.GetValueOrDefault(room.Id) ?? WaitingFittings.Empty;

    /// <summary>The reception desk's layout in a waiting room, if it has one.</summary>
    public DeskLayout? DeskOf(CareSpace room) =>
        FittingsOf(room).Desk is { } desk ? WaitingRoomLayout.Desk(room.Rect, desk, WaitingRoomLayout.DoorTiles(room.Rect, room.Doors)) : null;

    /// <summary>True for a door that opens onto the front path through the building's main entrance.</summary>
    public bool IsEntranceDoor(CareSpace room, Doorway door) => IsEntranceDoor(room.Rect, door);

    private bool IsEntranceDoor(GridRect rect, Doorway door) =>
        Site.FloorOf(rect) == 0 && WaitingRoomLayout.DoorTiles(rect, new[] { door }).All(t => MainBuilding.IsOpening(t, door.Side));

    private bool OpensOntoCorridor(GridRect rect, Doorway door) =>
        WaitingRoomLayout.DoorTiles(rect, new[] { door }).All(t => _spaces.Values.Any(s => s.Kind == CareSpaceKind.Corridor && s.Rect.Contains(Step(t, door.Side))));

    public bool HasEntranceDoor(CareSpace room) => room.Doors.Any(d => IsEntranceDoor(room, d));

    public bool HasInnerDoor(CareSpace room) => room.Doors.Any(d => !IsEntranceDoor(room, d));

    /// <summary>
    /// The waiting room walk-in patients come in to: one with a reception desk, either right behind the main
    /// entrance (an entrance door) or along a corridor from it.
    /// </summary>
    public CareSpace? MainWaitingRoom =>
        SpacesOfKind(CareSpaceKind.Waiting).FirstOrDefault(r => FittingsOf(r).Desk is not null && HasEntranceDoor(r) && HasInnerDoor(r))
        ?? SpacesOfKind(CareSpaceKind.Waiting).FirstOrDefault(r => FittingsOf(r).Desk is not null && HasEntranceDoor(r))
        ?? SpacesOfKind(CareSpaceKind.Waiting).FirstOrDefault(r => FittingsOf(r).Desk is not null && HasInnerDoor(r) && ReachedFromStreet(r));

    private NavGrid? _streetReachNav;
    private HashSet<GridCoord> _streetReach = new();

    /// <summary>True when people coming in through the main entrance can walk to the room's reception desk.</summary>
    public bool ReachedFromStreet(CareSpace room)
    {
        if (!ReferenceEquals(_streetReachNav, Nav))
        {
            _streetReach = Nav.Reachable(WalkInSpawn);
            _streetReachNav = Nav;
        }

        return DeskOf(room) is { } desk ? _streetReach.Contains(desk.PatientSpot) : _streetReach.Contains(room.StaffSpot);
    }

    /// <summary>What a waiting room still needs before walk-in patients can use it (empty when it is ready).</summary>
    public IReadOnlyList<string> WaitingRoomProblems(CareSpace room)
    {
        var missing = new List<string>();
        if (FittingsOf(room).Desk is null)
        {
            missing.Add("a reception desk");
        }

        if (!HasInnerDoor(room))
        {
            missing.Add("a door through to a corridor");
        }
        else if (!HasEntranceDoor(room) && FittingsOf(room).Desk is not null && !ReachedFromStreet(room))
        {
            missing.Add("a corridor from the main entrance (or an entrance door on the front path)");
        }

        return missing;
    }

    /// <summary>The waiting room's needs, for the list of what A&amp;E is missing.</summary>
    private IEnumerable<string> WaitingRoomReasons()
    {
        var rooms = SpacesOfKind(CareSpaceKind.Waiting).ToList();
        if (rooms.Count == 0)
        {
            return new[] { "a waiting room" };
        }

        if (rooms.Any(r => WaitingRoomProblems(r).Count == 0))
        {
            return Array.Empty<string>();
        }

        var best = rooms.OrderBy(r => WaitingRoomProblems(r).Count).First();
        return WaitingRoomProblems(best).Select(p => $"{p} in the waiting room");
    }

    // ---------------------------------------------------------------- reception desk

    /// <summary>Whether a reception desk can stand at <paramref name="tile"/> in a waiting room, facing <paramref name="facing"/>.</summary>
    public BuildCheck CheckReceptionDesk(string roomId, GridCoord tile, WallSide facing)
    {
        if (FindSpace(roomId) is not { Kind: CareSpaceKind.Waiting } room)
        {
            return new BuildCheck(0, new[] { "Reception desks go in a waiting room." });
        }

        var problems = new List<string>();
        var doors = WaitingRoomLayout.DoorTiles(room.Rect, room.Doors);
        var layout = WaitingRoomLayout.Desk(room.Rect, new ReceptionDesk(tile, facing), doors);
        var needed = layout.DeskTiles.Append(layout.StaffSpot).Append(layout.PatientSpot).ToList();
        if (!needed.All(room.Rect.Contains))
        {
            problems.Add("Doesn't fit: the desk, the receptionist behind it and a patient in front must all be inside the room.");
        }
        else if (needed.Any(doors.Contains))
        {
            problems.Add("Keep the doorways clear.");
        }
        else if (layout.AllTiles.Any(t => ChairAt(t) is not null))
        {
            problems.Add("Chairs are in the way: move them first.");
        }

        var existing = FittingsOf(room).Desk;
        if (existing is not null && room == MainWaitingRoom && _receptionQueue.Count > 0)
        {
            problems.Add("Patients are queueing at the desk.");
        }

        var cost = existing is null ? ReceptionDeskCost : 0;
        if (!CanAfford(cost))
        {
            problems.Add($"Not enough money: costs £{cost:N0}, you have £{Balance:N0}.");
        }

        return new BuildCheck(cost, problems, problems.Count == 0 ? Refit(room, FittingsOf(room) with { Desk = new ReceptionDesk(tile, facing) }) : null);
    }

    /// <summary>Places (or moves) a waiting room's reception desk.</summary>
    public BuildCheck PlaceReceptionDesk(string roomId, GridCoord tile, WallSide facing)
    {
        var check = CheckReceptionDesk(roomId, tile, facing);
        if (!check.CanBuild)
        {
            return check;
        }

        var room = _spaces[roomId];
        var moving = FittingsOf(room).Desk is not null;
        ApplyFittings(room, FittingsOf(room) with { Desk = new ReceptionDesk(tile, facing) });
        Book(MoneyCategory.Construction, -check.Cost);
        Notifications.Add(Now, NotificationLevel.Info, moving ? $"Moved the reception desk in {room.Name}." : $"Put a reception desk in {room.Name} for £{check.Cost:N0}.");
        return check;
    }

    // ---------------------------------------------------------------- doors

    /// <summary>The door a click on <paramref name="tile"/> (a tile along the room's wall) would place, and on which side.</summary>
    public Doorway? DoorAt(CareSpace room, GridCoord tile)
    {
        var r = room.Rect;
        var sides = new List<WallSide>();
        if (tile.Y == r.Y) sides.Add(WallSide.North);
        if (tile.Y == r.Bottom - 1) sides.Add(WallSide.South);
        if (tile.X == r.X) sides.Add(WallSide.West);
        if (tile.X == r.Right - 1) sides.Add(WallSide.East);
        if (!r.Contains(tile) || sides.Count == 0)
        {
            return null;
        }

        Doorway? first = null;
        foreach (var side in sides)
        {
            var length = side is WallSide.North or WallSide.South ? r.Width : r.Length;
            var along = side is WallSide.North or WallSide.South ? tile.X - r.X : tile.Y - r.Y;
            if (length < WaitingRoomLayout.DoorWidth + 2)
            {
                continue;
            }

            var offset = Math.Clamp(along - (WaitingRoomLayout.DoorWidth / 2), 1, length - 1 - WaitingRoomLayout.DoorWidth);
            var door = new Doorway(side, offset, WaitingRoomLayout.DoorWidth);
            first ??= door;
            if (DoorProblem(room, door) is null)
            {
                return door;
            }
        }

        return first;
    }

    /// <summary>The placed door covering a wall tile, if any.</summary>
    public Doorway? ExistingDoorAt(CareSpace room, GridCoord tile) =>
        room.Doors.FirstOrDefault(d => WaitingRoomLayout.DoorTiles(room.Rect, new[] { d }).Contains(tile));

    private string? DoorProblem(CareSpace room, Doorway door)
    {
        var inside = WaitingRoomLayout.DoorTiles(room.Rect, new[] { door }).ToList();
        if (!OpensOntoCorridor(room.Rect, door) && !IsEntranceDoor(room, door))
        {
            return "Doors must open onto a corridor, or onto the front path (the blue mat outside the main entrance).";
        }

        if (room.Doors.Any(d => d.Side == door.Side && d.Offset < door.Offset + door.Width + 1 && door.Offset < d.Offset + d.Width + 1))
        {
            return "There is already a door there.";
        }

        if (inside.Any(t => ChairAt(t) is not null))
        {
            return "Chairs are in the way: move them first.";
        }

        if (DeskOf(room) is { } desk && inside.Intersect(desk.DeskTiles.Append(desk.StaffSpot).Append(desk.PatientSpot)).Any())
        {
            return "The reception desk is in the way.";
        }

        return null;
    }

    /// <summary>Whether a door can go in the waiting room wall at <paramref name="tile"/>.</summary>
    public BuildCheck CheckWaitingDoor(string roomId, GridCoord tile)
    {
        if (FindSpace(roomId) is not { Kind: CareSpaceKind.Waiting } room)
        {
            return new BuildCheck(0, new[] { "Doors are placed in a waiting room's walls." });
        }

        if (DoorAt(room, tile) is not { } door)
        {
            return new BuildCheck(0, new[] { "Click a tile along the waiting room's wall." });
        }

        var problems = new List<string>();
        if (DoorProblem(room, door) is { } problem)
        {
            problems.Add(problem);
        }

        if (room.Doors.Count >= MaxWaitingDoors)
        {
            problems.Add($"A waiting room can have up to {MaxWaitingDoors} doors.");
        }

        var cost = IsEntranceDoor(room, door) ? EntranceDoorCost : InnerDoorCost;
        if (!CanAfford(cost))
        {
            problems.Add($"Not enough money: costs £{cost:N0}, you have £{Balance:N0}.");
        }

        var fittings = FittingsOf(room);
        var preview = problems.Count == 0 ? Refit(room, fittings with { Doors = fittings.Doors.Append(door).ToList() }) : null;
        return new BuildCheck(cost, problems, preview);
    }

    /// <summary>Puts a door in a waiting room's wall: an entrance on the front path, or a door through to a corridor.</summary>
    public BuildCheck PlaceWaitingDoor(string roomId, GridCoord tile)
    {
        var check = CheckWaitingDoor(roomId, tile);
        if (!check.CanBuild)
        {
            return check;
        }

        var room = _spaces[roomId];
        var door = DoorAt(room, tile)!;
        var entrance = IsEntranceDoor(room, door);
        var fittings = FittingsOf(room);
        ApplyFittings(room, fittings with { Doors = fittings.Doors.Append(door).ToList() });
        Book(MoneyCategory.Construction, -check.Cost);
        Notifications.Add(Now, NotificationLevel.Info, entrance
            ? $"Put the entrance doors into {room.Name} for £{check.Cost:N0}."
            : $"Put doors from {room.Name} through to the corridor for £{check.Cost:N0}.");
        return check;
    }

    /// <summary>Whether the door at <paramref name="tile"/> can be taken out (refunding half its cost).</summary>
    public BuildCheck CheckRemoveWaitingDoor(string roomId, GridCoord tile)
    {
        if (FindSpace(roomId) is not { Kind: CareSpaceKind.Waiting } room || ExistingDoorAt(room, tile) is not { } door)
        {
            return new BuildCheck(0, new[] { "There is no door there." });
        }

        var problems = new List<string>();
        var inside = WaitingRoomLayout.DoorTiles(room.Rect, new[] { door }).ToList();
        if (_patients.Values.Cast<Agent>().Concat(_staff).Concat(_outpatients).Any(a => inside.Contains(a.Tile) || inside.Contains(Step(a.Tile, WaitingRoomLayout.Opposite(door.Side)))))
        {
            problems.Add("Someone is in the doorway.");
        }

        var fittings = FittingsOf(room);
        var without = Refit(room, fittings with { Doors = fittings.Doors.Where(d => d != door).ToList() });
        if (problems.Count == 0)
        {
            problems.AddRange(CheckReachable(_spaces.Values.Where(s => s != room).Append(without).ToList()));
        }

        var cost = IsEntranceDoor(room, door) ? EntranceDoorCost : InnerDoorCost;
        return new BuildCheck(-cost / 2, problems, without);
    }

    public BuildCheck RemoveWaitingDoor(string roomId, GridCoord tile)
    {
        var check = CheckRemoveWaitingDoor(roomId, tile);
        if (!check.CanBuild)
        {
            return check;
        }

        var room = _spaces[roomId];
        var door = ExistingDoorAt(room, tile)!;
        var fittings = FittingsOf(room);
        ApplyFittings(room, fittings with { Doors = fittings.Doors.Where(d => d != door).ToList() });
        Book(MoneyCategory.Construction, -check.Cost);
        Notifications.Add(Now, NotificationLevel.Info, $"Took out a door from {room.Name}; £{-check.Cost:N0} refunded.");
        return check;
    }

    /// <summary>The room as it would be with these fittings.</summary>
    private CareSpace Refit(CareSpace room, WaitingFittings fittings)
    {
        var template = Database.RoomTemplates[room.TemplateId];
        return RoomLayoutEngine.Build(template, Database, room.Id, room.Name, room.Rect, room.DoorSide, fittings: fittings);
    }

    private void ApplyFittings(CareSpace room, WaitingFittings fittings)
    {
        _fittings[room.Id] = fittings;
        _spaces[room.Id] = Refit(room, fittings);
        AfterLayoutChange();
    }

    /// <summary>
    /// A waiting room being moved: if it keeps its size its desk and chairs go with it, along with any doors that
    /// still open onto a corridor or the front path. Otherwise the desk has to be placed again.
    /// </summary>
    private void MoveFittings(CareSpace room, GridRect to)
    {
        var from = room.Rect;
        var fittings = FittingsOf(room);
        var (dx, dy) = (to.X - from.X, to.Y - from.Y);
        var sameSize = to.Width == from.Width && to.Length == from.Length;
        var desk = sameSize && fittings.Desk is { } d ? d with { Tile = new GridCoord(d.Tile.X + dx, d.Tile.Y + dy) } : null;
        var doors = sameSize ? fittings.Doors.Where(door => OpensOntoCorridor(to, door) || IsEntranceDoor(to, door)).ToList() : new List<Doorway>();
        _fittings[room.Id] = new WaitingFittings(desk, doors);
        if (sameSize)
        {
            for (var i = 0; i < _chairs.Count; i++)
            {
                if (from.Contains(_chairs[i].Tile))
                {
                    UnseatFrom(_chairs[i].Tile);
                    _chairs[i] = _chairs[i] with { Tile = new GridCoord(_chairs[i].Tile.X + dx, _chairs[i].Tile.Y + dy) };
                }
            }
        }

        if (desk is null && fittings.Desk is not null || doors.Count < fittings.Doors.Count)
        {
            Notifications.Add(Now, NotificationLevel.Info, $"{room.Name} has moved: put its {(desk is null && fittings.Desk is not null ? "reception desk and " : string.Empty)}doors back in.");
        }
    }

    // ---------------------------------------------------------------- chairs

    /// <summary>Why a chair can't go on this tile, or null if it can.</summary>
    private string? ChairProblem(GridCoord tile)
    {
        var walled = _spaces.Values.FirstOrDefault(s => s.HasWalls && s.Rect.Contains(tile));
        if (walled is not null && walled.Kind != CareSpaceKind.Waiting)
        {
            return "Chairs go in a waiting room or along a corridor.";
        }

        if (walled is null && !_spaces.Values.Any(s => s.Kind == CareSpaceKind.Corridor && s.Rect.Contains(tile)))
        {
            return "Chairs go in a waiting room or along a corridor.";
        }

        if (DoorwayTiles().Contains(tile) || (walled is not null && WaitingRoomLayout.DoorTiles(walled.Rect, walled.Doors).Contains(tile)))
        {
            return "Keep doorways clear.";
        }

        if (MainBuilding.Entrances.Any(e => ApproachInside(e).Contains(tile)))
        {
            return "Keep the entrance clear.";
        }

        if (walled is not null && DeskOf(walled) is { } desk && desk.AllTiles.Contains(tile))
        {
            return "Keep the reception desk and its queue clear.";
        }

        return null;
    }

    /// <summary>The floor just inside an entrance, which must stay clear.</summary>
    private static GridRect ApproachInside(Entrance e) => e.Side switch
    {
        WallSide.North => new GridRect(e.Tile.X, e.Tile.Y, e.WidthTiles, 2),
        WallSide.South => new GridRect(e.Tile.X, e.Tile.Y - 1, e.WidthTiles, 2),
        WallSide.West => new GridRect(e.Tile.X, e.Tile.Y, 2, e.WidthTiles),
        _ => new GridRect(e.Tile.X - 1, e.Tile.Y, 2, e.WidthTiles),
    };

    /// <summary>Whether a chair can be placed here; on an existing chair it turns and recolours it for free.</summary>
    public BuildCheck CheckChair(GridCoord tile, WallSide facing, string colour)
    {
        colour = ChairColours.Normalise(colour);
        if (ChairAt(tile) is { } existing)
        {
            return existing.Facing == facing && existing.Colour == colour
                ? new BuildCheck(0, new[] { "There is already a chair there." })
                : new BuildCheck(0, Array.Empty<string>());
        }

        var problems = new List<string>();
        if (ChairProblem(tile) is { } problem)
        {
            problems.Add(problem);
        }

        if (!CanAfford(ChairCost))
        {
            problems.Add($"Not enough money: costs £{ChairCost:N0}, you have £{Balance:N0}.");
        }

        return new BuildCheck(ChairCost, problems);
    }

    /// <summary>Places a waiting chair (or turns and recolours the one already there).</summary>
    public BuildCheck PlaceChair(GridCoord tile, WallSide facing, string colour)
    {
        var check = CheckChair(tile, facing, colour);
        if (!check.CanBuild)
        {
            return check;
        }

        _chairs.RemoveAll(c => c.Tile == tile);
        _chairs.Add(new PlacedChair(tile, facing, ChairColours.Normalise(colour)));
        Book(MoneyCategory.Construction, -check.Cost);
        Events.Publish(new LayoutChanged());
        return check;
    }

    /// <summary>Takes a chair away, refunding half its cost. Whoever was sitting on it finds somewhere else.</summary>
    public bool RemoveChair(GridCoord tile)
    {
        if (ChairAt(tile) is not { } chair)
        {
            return false;
        }

        _chairs.Remove(chair);
        Book(MoneyCategory.Construction, ChairCost / 2);
        UnseatFrom(tile);
        Events.Publish(new LayoutChanged());
        return true;
    }

    private void UnseatFrom(GridCoord tile)
    {
        foreach (var p in _patients.Values.Where(p => p.Seat == tile))
        {
            p.Seat = null;
        }

        foreach (var o in _outpatients.Where(o => o.Seat == tile))
        {
            o.Seat = null;
        }
    }

    /// <summary>
    /// The way a new chair here would face: the same way as a chair beside it (so rows line up), otherwise
    /// with its back to the nearest wall.
    /// </summary>
    /// <summary>How far a chair stands from a wall beside it, in tiles; the wall itself takes up part of the tile.</summary>
    public const double ChairWallGap = 0.15;

    /// <summary>
    /// Where a chair at <paramref name="tile"/> stands, as a shift from the middle of its tile: away from every wall
    /// beside it (behind or to either side), so it stands clear of the wall instead of sinking into it.
    /// </summary>
    public (double X, double Y) ChairShift(GridCoord tile)
    {
        var (x, y) = (0.0, 0.0);
        foreach (var side in new[] { WallSide.North, WallSide.West, WallSide.East, WallSide.South })
        {
            var (dx, dy) = WaitingRoomLayout.Step(side);
            if (!ChairFloorOpen(tile, new GridCoord(tile.X + dx, tile.Y + dy)))
            {
                (x, y) = (x - (dx * ChairWallGap), y - (dy * ChairWallGap));
            }
        }

        return (x, y);
    }

    /// <summary>Whether the floor next to a chair's tile carries on without a wall in between.</summary>
    private bool ChairFloorOpen(GridCoord tile, GridCoord next)
    {
        var room = _spaces.Values.FirstOrDefault(s => s.HasWalls && s.Rect.Contains(tile));
        return room is not null
            ? room.Rect.Contains(next)
            : _spaces.Values.Any(s => s.Kind == CareSpaceKind.Corridor && s.Rect.Contains(next)) && !_spaces.Values.Any(s => s.HasWalls && s.Rect.Contains(next));
    }

    public WallSide SuggestChairFacing(GridCoord tile)
    {
        var sides = new[] { WallSide.North, WallSide.West, WallSide.East, WallSide.South };
        bool Open(GridCoord t) => ChairFloorOpen(tile, t);
        GridCoord Next(WallSide side)
        {
            var (dx, dy) = WaitingRoomLayout.Step(side);
            return new GridCoord(tile.X + dx, tile.Y + dy);
        }

        var neighbours = sides.Select(side => ChairAt(Next(side))).OfType<PlacedChair>().ToList();

        // Against a wall, the back goes to the wall (in a corner, whichever wall the chairs beside it use).
        var walls = sides.Where(side => !Open(Next(side))).ToList();
        if (walls.Count > 0)
        {
            var away = walls.Select(WaitingRoomLayout.Opposite).ToList();
            return neighbours.Select(c => c.Facing).FirstOrDefault(f => away.Contains(f), away[0]);
        }

        // Out in the open, line up with the chairs beside it.
        return neighbours.Count > 0 ? neighbours[0].Facing : WallSide.South;
    }

    /// <summary>After building work, chairs no longer on clear floor are taken away (refunded in full).</summary>
    private void PruneChairs()
    {
        foreach (var chair in _chairs.ToList())
        {
            if (ChairProblem(chair.Tile) is not null)
            {
                _chairs.Remove(chair);
                Book(MoneyCategory.Construction, ChairCost);
                UnseatFrom(chair.Tile);
            }
        }
    }

    // ---------------------------------------------------------------- where people wait

    /// <summary>Chairs someone is sitting on or heading for.</summary>
    private HashSet<GridCoord> TakenChairs()
    {
        var taken = _patients.Values.Where(p => p.Seat.HasValue && p.Stage != PatientStage.Gone).Select(p => p.Seat!.Value).ToHashSet();
        taken.UnionWith(_outpatients.Where(o => o.Seat.HasValue).Select(o => o.Seat!.Value));
        return taken;
    }

    private bool InWaitingRoom(GridCoord tile) => SpacesOfKind(CareSpaceKind.Waiting).Any(r => r.Rect.Contains(tile));

    /// <summary>How far apart two tiles are for choosing a chair: other floors count as a long way off.</summary>
    private int WaitDistance(GridCoord a, GridCoord b) =>
        Math.Abs(a.X - b.X) + Math.Abs(Site.Local(a).Y - Site.Local(b).Y) + (Site.FloorOf(a) == Site.FloorOf(b) ? 0 : 200);

    /// <summary>The corridor tile just outside a room's (first) door: where someone heading in arrives.</summary>
    private static GridCoord DoorstepOf(CareSpace room)
    {
        if (room.Doors.FirstOrDefault() is not { } door)
        {
            return new GridCoord(room.Rect.X + (room.Rect.Width / 2), room.Rect.Y + (room.Rect.Length / 2));
        }

        var tiles = WaitingRoomLayout.DoorTiles(room.Rect, new[] { door }).ToList();
        return Step(tiles[tiles.Count / 2], door.Side);
    }

    /// <summary>Where a waiting A&amp;E patient is going next: the room they are waiting for.</summary>
    private GridCoord WaitTarget(Patient patient)
    {
        // A test or procedure booked in a room.
        if (_tasks.Values.FirstOrDefault(t => t.PatientId == patient.Id && t.SpaceId is not null) is { SpaceId: { } booked } && FindSpace(booked) is { } bookedRoom)
        {
            return DoorstepOf(bookedRoom);
        }

        if (patient.Triage is not { } category)
        {
            var triage = SpacesOfKind(CareSpaceKind.Triage).OrderBy(t => WaitDistance(t.PatientSpot, patient.Tile)).FirstOrDefault();
            return triage is not null ? DoorstepOf(triage) : MainWaitingRoom?.StaffSpot ?? patient.Tile;
        }

        foreach (var kind in SpacePreference(category))
        {
            var nearest = SpacesOfKind(kind).OrderBy(s => WaitDistance(DoorstepOf(s), patient.Tile)).FirstOrDefault();
            if (nearest is not null)
            {
                return DoorstepOf(nearest);
            }
        }

        return MainWaitingRoom is { } waiting ? waiting.StaffSpot : patient.Tile;
    }

    /// <summary>
    /// Sends a waiting patient to sit down: before triage on a chair in a waiting room, afterwards on the free chair
    /// nearest to where they are going next. With no free chair they stand nearby.
    /// </summary>
    internal void SendToWaitingRoom(Patient patient)
    {
        ReleaseSpace(patient);
        patient.Seat = null;
        var preTriage = patient.Triage is null;
        var target = WaitTarget(patient);
        var taken = TakenChairs();
        var chair = _chairs
            .Where(c => !taken.Contains(c.Tile) && (!preTriage || InWaitingRoom(c.Tile)))
            .OrderBy(c => WaitDistance(c.Tile, target))
            .ThenBy(c => c.Tile.Y).ThenBy(c => c.Tile.X)
            .FirstOrDefault();
        if (chair is not null)
        {
            patient.Seat = chair.Tile;
            WalkTo(patient, chair.Tile);
            return;
        }

        WalkTo(patient, StandingSpot(preTriage && MainWaitingRoom is { } room ? room.StaffSpot : target, preTriage, patient));
    }

    /// <summary>
    /// A clear tile to stand on near <paramref name="near"/>: in a waiting room when <paramref name="inWaitingRoom"/>,
    /// otherwise along the corridors (or in a waiting room), out of doorways, chairs and queues.
    /// </summary>
    internal GridCoord StandingSpot(GridCoord near, bool inWaitingRoom, Agent who)
    {
        var blocked = DoorwayTiles();
        foreach (var room in _spaces.Values.Where(s => s.HasWalls))
        {
            blocked.UnionWith(WaitingRoomLayout.DoorTiles(room.Rect, room.Doors));
        }

        foreach (var room in SpacesOfKind(CareSpaceKind.Waiting))
        {
            if (DeskOf(room) is { } desk)
            {
                blocked.UnionWith(desk.AllTiles);
            }
        }

        blocked.UnionWith(_chairs.Select(c => c.Tile));
        foreach (var entrance in MainBuilding.Entrances)
        {
            blocked.UnionWith(ApproachInside(entrance).Tiles());
        }

        // Somewhere nobody else waiting is standing or heading.
        var others = _patients.Values.Where(p => !ReferenceEquals(p, who) && p.Stage != PatientStage.Gone).Cast<Agent>().Concat(_outpatients.Where(o => !ReferenceEquals(o, who)))
            .SelectMany(a => a.Destination is { } d ? new[] { a.Tile, d } : new[] { a.Tile })
            .ToHashSet();

        bool Floor(GridCoord t)
        {
            var walled = _spaces.Values.FirstOrDefault(s => s.HasWalls && s.Rect.Contains(t));
            if (walled is not null)
            {
                return walled.Kind == CareSpaceKind.Waiting;
            }

            return !inWaitingRoom && _spaces.Values.Any(s => s.Kind == CareSpaceKind.Corridor && s.Rect.Contains(t));
        }

        // Walk outwards from the target over open floor and take the first clear tile at least a step away.
        var seen = new HashSet<GridCoord> { near };
        var frontier = new Queue<(GridCoord Tile, int Steps)>();
        frontier.Enqueue((near, 0));
        GridCoord? fallback = null;
        while (frontier.Count > 0 && seen.Count < 1500)
        {
            var (tile, steps) = frontier.Dequeue();
            if (Floor(tile) && !blocked.Contains(tile))
            {
                fallback ??= tile;
                if (steps >= 2 && !others.Contains(tile))
                {
                    return tile;
                }
            }

            foreach (var side in new[] { WallSide.North, WallSide.East, WallSide.South, WallSide.West })
            {
                var next = Step(tile, side);
                if (Nav.InBounds(next) && Nav.CanStep(tile, next) && seen.Add(next) && (Floor(next) || steps < 3))
                {
                    frontier.Enqueue((next, steps + 1));
                }
            }
        }

        return fallback ?? near;
    }

    /// <summary>Every so often, waiting patients who are standing take a chair if one has come free.</summary>
    private void ReseatStandingPatients()
    {
        if (--_reseatCountdown > 0)
        {
            return;
        }

        _reseatCountdown = 30;
        if (_chairs.Count == 0)
        {
            return;
        }

        foreach (var p in _patients.Values.Where(p =>
                     p.Stage is PatientStage.WaitingForTriage or PatientStage.WaitingForSpace
                     && p.Seat is null && !p.IsMoving && p.BusyWithTaskId is null && p.CareSpaceId is null && p.PendingMove is null
                     && p.Mobility == Mobility.Walking && !Away(p)).ToList())
        {
            var preTriage = p.Triage is null;
            var taken = TakenChairs();
            if (_chairs.Any(c => !taken.Contains(c.Tile) && (!preTriage || InWaitingRoom(c.Tile))))
            {
                SendToWaitingRoom(p);
            }
        }
    }

    // ---------------------------------------------------------------- building and saving

    /// <summary>
    /// Builds the waiting room a new game used to start with: in front of A&amp;E, with its desk, doors and seats.
    /// Used by tests and the smoke test; a new game starts without one. Free of charge.
    /// </summary>
    public CareSpace BuildStarterWaitingRoom()
    {
        var rect = EmergencyDepartmentLayout.StarterWaitingRoom(MainBuilding.Footprint, Site.Layout?.Plan);
        var inner = Site.Layout?.Plan is { EntranceSide: WallSide.North } plan0 && EmergencyDepartmentLayout.FitsStarter(plan0) ? WallSide.South : WallSide.North;
        var plan = new RoomPlan("waiting_room", rect, inner);
        var (room, chairs) = UpgradeOldWaitingRoom(plan, "waiting_room", "Waiting room");
        _spaces[room.Id] = room;
        _chairs.RemoveAll(c => room.Rect.Contains(c.Tile));
        _chairs.AddRange(chairs);
        AfterLayoutChange();
        foreach (var member in _staff.Where(s => s.Role == StaffRole.Receptionist && s.BaseRoomId is null))
        {
            member.PlaceAt(HomeTile(member));
        }

        return room;
    }

    /// <summary>
    /// Turns a waiting room laid out the old way (desk, seats and doors all automatic) into one the player has fitted:
    /// the same desk and doors, and its seats as placed chairs. Used for old saves and the starter waiting room.
    /// </summary>
    private (CareSpace Room, List<PlacedChair> Chairs) UpgradeOldWaitingRoom(RoomPlan plan, string id, string name)
    {
        var template = Database.RoomTemplates[plan.TemplateId];
        var old = RoomLayoutEngine.Build(template, Database, id, name, plan.Rect, plan.DoorSide, plan.DoorStart, plan.DoorRun, StreetDoors(plan.Rect));
        var (sx, sy) = (old.StaffSpot.X, old.StaffSpot.Y);
        var (px, py) = (old.PatientSpot.X, old.PatientSpot.Y);
        var facing = Math.Abs(px - sx) >= Math.Abs(py - sy)
            ? (px > sx ? WallSide.East : WallSide.West)
            : (py > sy ? WallSide.South : WallSide.North);
        var desk = new ReceptionDesk(new GridCoord((sx + px) / 2, (sy + py) / 2), facing);
        var fittings = new WaitingFittings(desk, old.Doors.ToList());
        _fittings[id] = fittings;
        var room = RoomLayoutEngine.Build(template, Database, id, name, plan.Rect, plan.DoorSide, fittings: fittings);
        room.AllowedDepartments.UnionWith(old.AllowedDepartments);
        var layout = DeskOf(room);
        var keepClear = WaitingRoomLayout.DoorTiles(room.Rect, room.Doors);
        var chairs = old.Seats
            .Where(s => layout is null || !layout.AllTiles.Contains(s))
            .Where(s => !keepClear.Contains(s))
            .Select(s => new PlacedChair(s, WaitingRoomLayout.Opposite(plan.DoorSide), ChairColours.Default))
            .ToList();
        return (room, chairs);
    }
}
