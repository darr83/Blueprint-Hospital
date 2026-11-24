using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// Decorations the player places on corridors, in open areas and in waiting rooms: plants, benches, a fish tank,
/// sofas, tables, pictures on the walls and so on (see <see cref="DecorItems"/>). Benches, sofas, armchairs and
/// cafe tables are seats, used like waiting chairs. People walk round the rest. Patients waiting near something
/// nice to look at (plants, pictures, the fish tank) are a little less likely to walk out.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>How much more it costs to walk over a tile with a decoration on it: people go round if they can.</summary>
    private const byte DecorTileCost = 8;

    private readonly List<PlacedDecoration> _decorations = new();
    private int _nextDecorationId = 1;
    private int _decorStamp;
    private (int Stamp, HashSet<GridCoord> Solid, List<WaitSeat> Seats, List<GridCoord> Comfort)? _decorCache;

    /// <summary>Every decoration the player has placed.</summary>
    public IReadOnlyList<PlacedDecoration> PlacedDecorations => _decorations;

    public PlacedDecoration? FindDecoration(int id) => _decorations.FirstOrDefault(d => d.Id == id);

    /// <summary>The tiles a placed decoration covers (for a wall item, the floor in front of its stretch of wall).</summary>
    public IEnumerable<GridCoord> FootprintOf(PlacedDecoration placed) =>
        placed.Item is { } item ? DecorItems.Footprint(item, placed.Tile, placed.Facing) : Array.Empty<GridCoord>();

    /// <summary>The decoration standing on a tile: anything but a rug first, then a rug. Wall items are not found here.</summary>
    public PlacedDecoration? DecorationAt(GridCoord tile) =>
        _decorations.Where(d => d.Item is { OnWall: false } && FootprintOf(d).Contains(tile)).OrderBy(d => d.Item!.Flat ? 1 : 0).FirstOrDefault();

    /// <summary>The wall decoration hanging behind a tile, if any.</summary>
    public PlacedDecoration? WallDecorationAt(GridCoord tile) =>
        _decorations.FirstOrDefault(d => d.Item is { OnWall: true } && FootprintOf(d).Contains(tile));

    /// <summary>Tiles with a solid decoration on them (everything but rugs and wall items).</summary>
    public IReadOnlySet<GridCoord> DecorTiles => DecorCache().Solid;

    /// <summary>The seats on benches, sofas, armchairs and cafe tables.</summary>
    public IReadOnlyList<WaitSeat> DecorSeats => DecorCache().Seats;

    private (int Stamp, HashSet<GridCoord> Solid, List<WaitSeat> Seats, List<GridCoord> Comfort) DecorCache()
    {
        if (_decorCache is { } cache && cache.Stamp == _decorStamp)
        {
            return cache;
        }

        var solid = _decorations.Where(d => d.Item is { OnWall: false, Flat: false }).SelectMany(FootprintOf).ToHashSet();
        var seats = _decorations.SelectMany(DecorItems.SeatsOf).ToList();
        var comfort = _decorations.Where(d => d.Item is { Comfort: true }).SelectMany(FootprintOf).ToList();
        _decorCache = (_decorStamp, solid, seats, comfort);
        return _decorCache.Value;
    }

    /// <summary>Every seat someone waiting can use: the chairs the player placed and the seats on decorations.</summary>
    private IEnumerable<WaitSeat> WaitSeats => _chairs.Select(c => new WaitSeat(c.Tile, c.Facing)).Concat(DecorSeats);

    /// <summary>Whether something nice to look at is near this tile.</summary>
    public bool NearComfort(GridCoord tile) =>
        DecorCache().Comfort.Any(c => Math.Abs(c.X - tile.X) <= DecorItems.ComfortRange && Math.Abs(c.Y - tile.Y) <= DecorItems.ComfortRange);

    /// <summary>A waiting patient near plants, pictures or the fish tank is a little less likely to walk out.</summary>
    private double ComfortWalkOutFactor(Patient patient) => NearComfort(patient.Tile) ? DecorItems.ComfortWalkOutFactor : 1.0;

    /// <summary>Makes walking over decorations dear, so people go round them.</summary>
    private void MarkDecorTiles(NavGrid grid)
    {
        foreach (var tile in DecorTiles.Where(grid.InBounds))
        {
            if (grid.CostAt(tile) > 0)
            {
                grid.SetCost(tile, Math.Max(grid.CostAt(tile), DecorTileCost));
            }
        }
    }

    // ---------------------------------------------------------------- where things can go

    private enum DecorFloor
    {
        None,
        Corridor,
        OpenArea,
        Waiting,
    }

    private DecorFloor FloorAt(GridCoord tile)
    {
        var walled = _spaces.Values.FirstOrDefault(s => s.HasWalls && s.Rect.Contains(tile));
        if (walled is not null)
        {
            return walled.Kind == CareSpaceKind.Waiting ? DecorFloor.Waiting : DecorFloor.None;
        }

        var open = DecorFloor.None;
        foreach (var s in _spaces.Values)
        {
            if (s.Kind == CareSpaceKind.Corridor && s.Rect.Contains(tile))
            {
                if (s.TemplateId == DecorItems.OpenAreaTemplate)
                {
                    return DecorFloor.OpenArea;
                }

                open = DecorFloor.Corridor;
            }
        }

        return open;
    }

    /// <summary>
    /// Whether there is a wall behind <paramref name="tile"/> on its <paramref name="side"/>, not a doorway. With
    /// <paramref name="solid"/> (to hang something on) it must not be a room's door wall either, which is glass.
    /// </summary>
    private bool WallBehind(GridCoord tile, WallSide side, bool solid)
    {
        var next = Step(tile, side);
        var room = _spaces.Values.FirstOrDefault(s => s.HasWalls && s.Rect.Contains(tile));
        if (room is not null)
        {
            if (room.Rect.Contains(next))
            {
                return false;
            }

            if (WaitingRoomLayout.DoorTiles(room.Rect, room.Doors).Contains(tile))
            {
                return false;
            }

            return Site.IsIndoors(next) || !MainBuilding.IsOpening(tile, side);
        }

        if (!Site.IsIndoors(next))
        {
            return !MainBuilding.IsOpening(tile, side);
        }

        // A room's wall: not its door wall when hanging something, as that is glass and door panels on the corridor side.
        var behind = _spaces.Values.FirstOrDefault(s => s.HasWalls && s.Rect.Contains(next));
        return behind is not null && (!solid || !behind.Doors.Any(d => d.Side == WaitingRoomLayout.Opposite(side)));
    }

    /// <summary>Why a decoration can't go here (ignoring the one being moved), or null if it can.</summary>
    private string? DecorationProblem(DecorItem item, GridCoord tile, WallSide facing, int? ignoreId = null)
    {
        var footprint = DecorItems.Footprint(item, tile, facing).ToList();
        if (footprint.Any(t => !Site.Bounds.Contains(t)))
        {
            return "Doesn't fit there.";
        }

        var floors = footprint.Select(FloorAt).ToList();
        if (floors.Any(f => f == DecorFloor.None))
        {
            return "Decorations go on corridors, in open areas and in waiting rooms.";
        }

        if (item.OpenAreaOnly && floors.Any(f => f == DecorFloor.Corridor))
        {
            return $"{item.Name}s go in an open area or a waiting room.";
        }

        var others = _decorations.Where(d => d.Id != ignoreId && d.Item is not null).ToList();
        if (item.OnWall)
        {
            var back = WaitingRoomLayout.Opposite(facing);
            if (!footprint.All(t => WallBehind(t, back, solid: true)))
            {
                return "Hang it on a solid wall (not a doorway or a room's glass front).";
            }

            if (others.Where(d => d.Item!.OnWall && d.Facing == facing).SelectMany(FootprintOf).Intersect(footprint).Any())
            {
                return "Something is already hanging there.";
            }

            return null;
        }

        // The whole item on one stretch of floor, with no wall through it.
        foreach (var t in footprint)
        {
            foreach (var side in new[] { WallSide.East, WallSide.South })
            {
                var n = Step(t, side);
                if (footprint.Contains(n) && !ChairFloorOpen(t, n))
                {
                    return "Doesn't fit there: there's a wall in the way.";
                }
            }
        }

        var keepClear = DoorwayTiles();
        foreach (var room in _spaces.Values.Where(s => s.HasWalls && s.Kind == CareSpaceKind.Waiting))
        {
            keepClear.UnionWith(WaitingRoomLayout.DoorTiles(room.Rect, room.Doors));
            if (DeskOf(room) is { } desk)
            {
                keepClear.UnionWith(desk.AllTiles);
            }
        }

        if (footprint.Any(keepClear.Contains))
        {
            return "Keep doorways and the reception desk clear.";
        }

        if (MainBuilding.Entrances.Any(e => ApproachInside(e).Tiles().Any(footprint.Contains)))
        {
            return "Keep the entrance clear.";
        }

        if (item.Flat)
        {
            return others.Where(d => d.Item!.Flat).SelectMany(FootprintOf).Intersect(footprint).Any() ? "There's already a rug there." : null;
        }

        if (footprint.Any(t => ChairAt(t) is not null))
        {
            return "There's a chair in the way.";
        }

        var solid = others.Where(d => d.Item is { OnWall: false, Flat: false }).SelectMany(FootprintOf).ToHashSet();
        if (footprint.Any(solid.Contains))
        {
            return "Something is already there.";
        }

        // Along a plain corridor: back against the wall, leaving room to walk past.
        if (floors.Any(f => f == DecorFloor.Corridor))
        {
            var back = WaitingRoomLayout.Opposite(facing);
            var backRow = footprint.Where(t => !footprint.Contains(Step(t, back))).ToList();
            if (!backRow.All(t => WallBehind(t, back, solid: false)))
            {
                return "On a corridor, stand it against a wall.";
            }

            foreach (var t in footprint.Where(t => !footprint.Contains(Step(t, facing))))
            {
                var ahead = t;
                for (var i = 0; i < 2; i++)
                {
                    var next = Step(ahead, facing);
                    if (FloorAt(next) == DecorFloor.None || !ChairFloorOpen(ahead, next) || solid.Contains(next) || ChairAt(next) is not null)
                    {
                        return "Leave room to walk past: the corridor is too narrow here.";
                    }

                    ahead = next;
                }
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- placing

    /// <summary>Whether a decoration can be placed here, and what it costs.</summary>
    public BuildCheck CheckDecoration(string itemId, GridCoord tile, WallSide facing)
    {
        if (DecorItems.Find(itemId) is not { } item)
        {
            return new BuildCheck(0, new[] { "Unknown decoration." });
        }

        var problems = new List<string>();
        if (DecorationProblem(item, tile, facing) is { } problem)
        {
            problems.Add(problem);
        }

        if (!CanAfford(item.Cost))
        {
            problems.Add($"Not enough money: costs £{item.Cost:N0}, you have £{Balance:N0}.");
        }

        return new BuildCheck(item.Cost, problems);
    }

    /// <summary>Places a decoration, in <paramref name="colour"/> for items that take one.</summary>
    public BuildCheck PlaceDecoration(string itemId, GridCoord tile, WallSide facing, string? colour = null)
    {
        var check = CheckDecoration(itemId, tile, facing);
        if (!check.CanBuild)
        {
            return check;
        }

        var item = DecorItems.Find(itemId)!;
        _decorations.Add(new PlacedDecoration(_nextDecorationId++, itemId, tile, facing, item.Tintable ? DecorItems.TintOf(item, colour) : null));
        Book(MoneyCategory.Construction, -check.Cost);
        DecorationsChanged();
        return check;
    }

    /// <summary>Whether a placed decoration can be turned to face <paramref name="facing"/> and moved to <paramref name="tile"/> (free).</summary>
    public BuildCheck CheckMoveDecoration(int id, GridCoord tile, WallSide facing)
    {
        if (FindDecoration(id) is not { Item: { } item })
        {
            return new BuildCheck(0, new[] { "That decoration has gone." });
        }

        return DecorationProblem(item, tile, facing, id) is { } problem
            ? new BuildCheck(0, new[] { problem })
            : new BuildCheck(0, Array.Empty<string>());
    }

    /// <summary>Moves or turns a placed decoration, free of charge.</summary>
    public BuildCheck MoveDecoration(int id, GridCoord tile, WallSide facing)
    {
        var check = CheckMoveDecoration(id, tile, facing);
        if (!check.CanBuild)
        {
            return check;
        }

        var index = _decorations.FindIndex(d => d.Id == id);
        UnseatDecoration(_decorations[index]);
        _decorations[index] = _decorations[index] with { Tile = tile, Facing = facing };
        DecorationsChanged();
        return check;
    }

    /// <summary>The next way a placed decoration can face, turning clockwise, or null if it can't turn where it is.</summary>
    public WallSide? NextDecorationFacing(int id)
    {
        if (FindDecoration(id) is not { } placed)
        {
            return null;
        }

        var order = new[] { WallSide.North, WallSide.East, WallSide.South, WallSide.West };
        var at = Array.IndexOf(order, placed.Facing);
        for (var i = 1; i < 4; i++)
        {
            var facing = order[(at + i) % 4];
            if (CheckMoveDecoration(id, placed.Tile, facing).CanBuild)
            {
                return facing;
            }
        }

        return null;
    }

    /// <summary>Changes the colour of a decoration that takes one. Free.</summary>
    public bool RecolourDecoration(int id, string colour)
    {
        var index = _decorations.FindIndex(d => d.Id == id);
        if (index < 0 || _decorations[index].Item is not { Tintable: true } item)
        {
            return false;
        }

        _decorations[index] = _decorations[index] with { Colour = DecorItems.TintOf(item, colour) };
        DecorationsChanged();
        return true;
    }

    /// <summary>Sells a decoration for half what it cost. Anyone sitting on it finds somewhere else.</summary>
    public bool RemoveDecoration(int id)
    {
        if (FindDecoration(id) is not { } placed)
        {
            return false;
        }

        _decorations.Remove(placed);
        Book(MoneyCategory.Construction, (placed.Item?.Cost ?? 0) / 2);
        UnseatDecoration(placed);
        DecorationsChanged();
        return true;
    }

    private void UnseatDecoration(PlacedDecoration placed)
    {
        foreach (var seat in DecorItems.SeatsOf(placed))
        {
            UnseatFrom(seat.Tile);
        }
    }

    private void DecorationsChanged()
    {
        _decorStamp++;
        Nav = BuildNav();
        Events.Publish(new LayoutChanged());
    }

    /// <summary>After building work, decorations no longer on clear floor (or against a wall) are taken away, refunded in full.</summary>
    private void PruneDecorations()
    {
        foreach (var placed in _decorations.ToList())
        {
            var item = placed.Item;
            if (item is null || DecorationProblem(item, placed.Tile, placed.Facing, placed.Id) is not null)
            {
                _decorations.Remove(placed);
                Book(MoneyCategory.Construction, item?.Cost ?? 0);
                UnseatDecoration(placed);
                _decorStamp++;
            }
        }
    }

    // ---------------------------------------------------------------- saving

    private void RestoreDecorations(IReadOnlyList<PlacedDecoration> saved)
    {
        _decorations.Clear();
        foreach (var placed in saved.Where(d => d.Item is not null))
        {
            if (_decorations.All(d => d.Id != placed.Id))
            {
                _decorations.Add(placed);
            }
        }

        _nextDecorationId = _decorations.Count == 0 ? 1 : _decorations.Max(d => d.Id) + 1;
        _decorStamp++;
        PruneDecorations();
    }
}
