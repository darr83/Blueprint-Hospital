using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// One person to a chair. Staff head for a seat in their base room, the staff room or behind a desk, picked by
/// rota rather than by looking; this makes sure the seat is free first, and finds them another (or somewhere to
/// stand nearby) when someone is already sitting there or on their way to it. Patients already book their chairs.
/// </summary>
public sealed partial class HospitalSimulation
{
    private HashSet<GridCoord>? _seatTiles;
    private (int Spaces, int Chairs, int Furniture) _seatKey;

    /// <summary>Every tile someone can sit on: room chairs and sofas, and the waiting chairs the player placed.</summary>
    public IReadOnlySet<GridCoord> SeatTiles
    {
        get
        {
            var key = (_spaces.Count, _chairs.Count, _spaces.Values.Sum(s => s.Furniture.Count));
            if (_seatTiles is null || key != _seatKey)
            {
                _seatKey = key;
                _seatTiles = new HashSet<GridCoord>(_chairs.Select(c => c.Tile));
                foreach (var space in _spaces.Values)
                {
                    _seatTiles.UnionWith(space.Seats);
                    _seatTiles.UnionWith(space.Furniture
                        .Where(f => Database.Furniture.TryGetValue(f.ItemId, out var def) && def.Style == FurnitureStyle.Chair)
                        .Select(f => f.Tile));
                }
            }

            return _seatTiles;
        }
    }

    public bool IsSeat(GridCoord tile) => SeatTiles.Contains(tile);

    /// <summary>Whether anyone but <paramref name="except"/> is on <paramref name="tile"/>, heading for it, or has it booked.</summary>
    public bool IsSpotTaken(GridCoord tile, Agent? except = null) => TakenSpots(except).Contains(tile);

    /// <summary>Where everyone but <paramref name="except"/> is standing, sitting or heading, and the chairs patients have booked.</summary>
    private HashSet<GridCoord> TakenSpots(Agent? except)
    {
        var taken = new HashSet<GridCoord>();
        void Add(Agent a)
        {
            if (a == except)
            {
                return;
            }

            if (a.IsMoving)
            {
                if (a.Destination is { } to)
                {
                    taken.Add(to);
                }
            }
            else
            {
                taken.Add(a.Tile);
            }
        }

        foreach (var member in _staff.Where(s => s.IsInBuilding))
        {
            Add(member);
        }

        foreach (var patient in _patients.Values.Where(p => p.Stage != PatientStage.Gone))
        {
            Add(patient);
            if (patient.Seat is { } seat && patient != except)
            {
                taken.Add(seat);
            }
        }

        foreach (var outpatient in _outpatients)
        {
            Add(outpatient);
            if (outpatient.Seat is { } seat && outpatient != except)
            {
                taken.Add(seat);
            }
        }

        foreach (var trip in _trips.Values)
        {
            if (trip.Table is { } table && trip.Person != except)
            {
                taken.Add(table);
            }
        }

        return taken;
    }

    /// <summary>
    /// Where <paramref name="who"/> should go when they mean to settle at <paramref name="wanted"/>: that spot when
    /// it is free; otherwise the nearest free seat in the same room (when it was a seat), else the nearest clear
    /// tile in the same room to stand on. Nobody ends up sitting on someone else's chair, or in their lap.
    /// </summary>
    internal GridCoord FreeSpot(Agent who, GridCoord wanted)
    {
        var taken = TakenSpots(who);
        if (!taken.Contains(wanted))
        {
            return wanted;
        }

        var room = _spaces.Values.Where(s => s.Rect.Contains(wanted)).OrderBy(s => s.Kind == CareSpaceKind.Corridor ? 1 : 0).FirstOrDefault();
        var area = room?.Rect ?? new GridRect(wanted.X - 3, wanted.Y - 3, 7, 7);
        var seats = SeatTiles;
        if (seats.Contains(wanted))
        {
            var seat = seats
                .Where(s => area.Contains(s) && !taken.Contains(s) && Site.FloorOf(s) == Site.FloorOf(wanted))
                .OrderBy(s => Distance(s, wanted)).ThenBy(s => s.Y).ThenBy(s => s.X)
                .Cast<GridCoord?>()
                .FirstOrDefault();
            if (seat is { } free)
            {
                return free;
            }
        }

        // Nowhere to sit: stand nearby, clear of chairs and of everyone else.
        var stand = area.Tiles()
            .Where(t => t != wanted && !taken.Contains(t) && !seats.Contains(t) && Nav.IsWalkable(t) && (room is null || !room.Rect.IsOnEdge(t) || !room.HasWalls))
            .OrderBy(t => Distance(t, wanted)).ThenBy(t => t.Y).ThenBy(t => t.X)
            .Cast<GridCoord?>()
            .FirstOrDefault();
        return stand ?? wanted;
    }
}
