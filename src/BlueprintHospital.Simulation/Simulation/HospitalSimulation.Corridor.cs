using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.Transport;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// Corridor care. An ambulance crew waits with their patient until a nurse takes the handover, however
/// long that is. Once the handover is done, if there is no free cubicle, a nurse wheels the trolley to a
/// space along an A&amp;E corridor and does a set of observations there, which gives the Red/Amber/Green
/// rating. The crew are free to go. Corridor patients get repeat observations but no treatment: they
/// keep waiting for a cubicle, in triage order, like everyone else.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>Tiles between trolleys parked along a corridor wall (a trolley is about 4 tiles long, so this leaves a gap).</summary>
    public const int CorridorSpotSpacing = 5;

    /// <summary>Corridors narrower than this (2 m) are too tight to leave a trolley in.</summary>
    public const int MinCorridorWidthForTrolleys = 4;

    /// <summary>Trolleys aren't left within this many tiles of a doorway, an entrance or a corridor junction.</summary>
    private const int CorridorClearance = 2;

    private (NavGrid Nav, int Chairs, List<GridCoord> Spots)? _corridorSpotCache;

    /// <summary>Every corridor space a trolley could be parked in, nearest the ambulance entrance first.</summary>
    public IReadOnlyList<GridCoord> CorridorCareSpots()
    {
        // The nav grid is rebuilt whenever anything is built, moved or sold, so it marks a layout change.
        if (_corridorSpotCache is { } cache && ReferenceEquals(cache.Nav, Nav) && cache.Chairs == ChairLayoutKey())
        {
            return cache.Spots;
        }

        var result = FindCorridorCareSpots();
        _corridorSpotCache = (Nav, ChairLayoutKey(), result);
        return result;
    }

    /// <summary>Changes whenever a chair is placed, moved or removed.</summary>
    private int ChairLayoutKey() => _chairs.Aggregate(_chairs.Count, (h, c) => HashCode.Combine(h, c.Tile));

    private List<GridCoord> FindCorridorCareSpots()
    {
        var spots = new List<GridCoord>();
        var corridors = _spaces.Values.Where(s => s.Kind == CareSpaceKind.Corridor && Site.FloorOf(s.Rect) == 0).ToList();
        if (corridors.Count == 0)
        {
            return spots;
        }

        var doors = DoorTiles();
        foreach (var corridor in corridors.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            var r = corridor.Rect;
            var alongX = r.Width >= r.Length;
            var across = alongX ? r.Length : r.Width;
            var along = alongX ? r.Width : r.Length;
            if (across < MinCorridorWidthForTrolleys)
            {
                continue;
            }

            // Along each long wall, leaving the middle of the corridor clear for people walking. The south or
            // east wall faces the camera in the usual view, so trolleys stand a little further out from it
            // (where there's room) rather than vanish behind it.
            foreach (var lane in new[] { 1, across - (across >= 7 ? 3 : 2) })
            {
                for (var a = CorridorClearance; a < along - CorridorClearance; a += CorridorSpotSpacing)
                {
                    var tile = alongX ? new GridCoord(r.X + a, r.Y + lane) : new GridCoord(r.X + lane, r.Y + a);
                    if (CorridorSpotIsClear(tile, corridor, corridors, doors) && spots.All(s => Steps(s, tile) >= CorridorSpotSpacing - 1))
                    {
                        spots.Add(tile);
                    }
                }
            }
        }

        var from = AmbulanceBayPatientTiles.Count > 0 ? AmbulanceBayPatientTiles[0] : new GridCoord(0, 0);
        return spots.OrderBy(s => Steps(s, from)).ThenBy(s => s.X).ThenBy(s => s.Y).ToList();
    }

    /// <summary>Corridor spaces with nobody parked in them (or on their way to them) right now.</summary>
    public IReadOnlyList<GridCoord> FreeCorridorCareSpots()
    {
        var taken = _patients.Values.Where(p => p.CorridorSpot.HasValue && p.IsInDepartment).Select(p => p.CorridorSpot!.Value).ToHashSet();
        return CorridorCareSpots().Where(s => !taken.Contains(s)).ToList();
    }

    /// <summary>Patients on trolleys in the corridor (or being wheeled there).</summary>
    public IEnumerable<Patient> CorridorPatients => _patients.Values.Where(p => p.CorridorSpot.HasValue && p.IsInDepartment);

    /// <summary>Beside a parked trolley, on the side towards the middle of the corridor (not at either end of it).</summary>
    private GridCoord CorridorStaffSpot(GridCoord spot)
    {
        var corridor = _spaces.Values.FirstOrDefault(s => s.Kind == CareSpaceKind.Corridor && s.Rect.Contains(spot));
        if (corridor is null)
        {
            return BesideTile(spot);
        }

        var r = corridor.Rect;
        var beside = r.Width >= r.Length
            ? new GridCoord(spot.X, spot.Y + (spot.Y - r.Y < r.Bottom - 1 - spot.Y ? 1 : -1))
            : new GridCoord(spot.X + (spot.X - r.X < r.Right - 1 - spot.X ? 1 : -1), spot.Y);
        return Nav.IsWalkable(beside) ? beside : BesideTile(spot);
    }

    private static int Steps(GridCoord a, GridCoord b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    /// <summary>
    /// Is there a cubicle this patient could go into now? Before triage, only majors or resus count
    /// (where ambulance patients go); after it, the spaces their rating allows.
    /// </summary>
    private bool CubicleFreeFor(Patient patient)
    {
        var kinds = patient.Triage is { } category ? SpacePreference(category) : new[] { CareSpaceKind.Majors, CareSpaceKind.Resus };
        return _spaces.Values.Any(s => kinds.Contains(s.Kind) && IsVacant(s));
    }

    private HashSet<GridCoord> DoorTiles()
    {
        var tiles = new HashSet<GridCoord>();
        foreach (var space in _spaces.Values.Where(s => s.HasWalls))
        {
            foreach (var tile in space.Rect.Tiles().Where(space.Rect.IsOnEdge))
            {
                foreach (var side in new[] { WallSide.North, WallSide.South, WallSide.East, WallSide.West })
                {
                    if (space.IsDoor(tile, side))
                    {
                        tiles.Add(tile);
                    }
                }
            }
        }

        foreach (var building in Site.Buildings)
        {
            foreach (var entrance in building.Entrances)
            {
                for (var i = 0; i < Math.Max(1, entrance.WidthTiles); i++)
                {
                    tiles.Add(entrance.Side is WallSide.North or WallSide.South
                        ? new GridCoord(entrance.Tile.X + i, entrance.Tile.Y)
                        : new GridCoord(entrance.Tile.X, entrance.Tile.Y + i));
                }
            }
        }

        return tiles;
    }

    private bool CorridorSpotIsClear(GridCoord tile, CareSpace corridor, List<CareSpace> corridors, HashSet<GridCoord> doors)
    {
        if (!Nav.IsWalkable(tile))
        {
            return false;
        }

        bool Near(GridCoord other) => Math.Abs(other.X - tile.X) <= CorridorClearance + 1 && Math.Abs(other.Y - tile.Y) <= CorridorClearance + 1;

        // Keep clear of doorways, entrances, corridor junctions, lifts and stairs, and chairs.
        if (doors.Any(Near))
        {
            return false;
        }

        foreach (var other in corridors.Where(c => c != corridor))
        {
            var grown = new GridRect(other.Rect.X - CorridorClearance, other.Rect.Y - CorridorClearance,
                other.Rect.Width + (2 * CorridorClearance), other.Rect.Length + (2 * CorridorClearance));
            if (grown.Contains(tile))
            {
                return false;
            }
        }

        if (_spaces.Values.Any(s => s.Kind != CareSpaceKind.Corridor && s.Rect.Contains(tile)))
        {
            return false;
        }

        return _chairs.All(c => Math.Abs(c.Tile.X - tile.X) > 1 || Math.Abs(c.Tile.Y - tile.Y) > 1);
    }

    /// <summary>
    /// Handover is done but there is no cubicle: a nurse takes the trolley to a corridor space, where they
    /// will do the observations that give the triage rating. Returns false if the corridors are full too.
    /// </summary>
    private bool TryHandOverToCorridor(StaffMember nurse, Patient patient)
    {
        var free = CubicleFreeFor(patient) ? Array.Empty<GridCoord>() : FreeCorridorCareSpots();
        if (free.Count == 0)
        {
            return false;
        }

        foreach (var s in patient.Symptoms)
        {
            patient.KnownSymptoms.Add(s);
        }

        patient.Log(Now, $"Ambulance handover to {nurse.Name}: {patient.AmbulanceHandover}");
        patient.Log(Now, "No cubicle free, so the patient is going into corridor care for observations.", important: true);
        SendToCorridor(patient, free[0]);
        return true;
    }

    /// <summary>A nurse wheels the patient to <paramref name="spot"/> (a nurse, not a porter: the patient is in their care).</summary>
    private void SendToCorridor(Patient patient, GridCoord spot)
    {
        patient.CorridorSpot = spot;
        patient.CorridorSince ??= Now;
        UpdateMobility(patient);
        patient.StopMoving();
        patient.PendingMove = spot;
        if (!HasTask(patient, TaskKind.MovePatient))
        {
            AddTask(TaskKind.MovePatient, StaffRole.Nurse, patient, 0, patient.Mobility == Mobility.Trolley ? "Wheel trolley to corridor" : "Wheel to corridor");
        }

        Notifications.Add(Now, NotificationLevel.Warning, $"{patient.FullName} is in corridor care: no cubicle free.", patient.Id, null, patient.Triage);
    }

    /// <summary>Each tick: release crews whose patient has left the bay, start corridor obs, and move stuck stretchers in.</summary>
    private void UpdateCorridorCare()
    {
        foreach (var patient in _patients.Values.Where(p => p.CorridorSpot.HasValue).ToList())
        {
            if (!patient.IsInDepartment)
            {
                patient.CorridorSpot = null;
                continue;
            }

            // Off the ambulance trolley and away from the bay: the crew can go.
            if (AmbulanceFor(patient) is { Purpose: AmbulancePurpose.Arrival } ambulance && !AmbulanceBayPatientTiles.Contains(patient.Tile))
            {
                ambulance.HandoverCompletedAt = Now;
                var held = Now - (ambulance.ArrivedAtBayAt ?? Now);
                Stats.RecordHandoverTime(held);
                SendAmbulanceAway(ambulance);
                patient.Log(Now, $"Crew from {ambulance.CallSign} released after {held.TotalMinutes:0} minutes.");
            }

            var parked = patient.CorridorSpot is { } spot && patient.Tile == spot && !patient.IsMoving && patient.PendingMove is null && patient.PushedByStaffId is null;
            if (!parked)
            {
                continue;
            }

            if (patient.Stage == PatientStage.InAmbulanceQueue)
            {
                if (patient.TriagedAt is null)
                {
                    if (!HasTask(patient, TaskKind.Triage))
                    {
                        patient.QueuedSince = Now;
                        AddTask(TaskKind.Triage, StaffRole.Nurse, patient, 8, "Corridor observations and triage");
                    }
                }
                else
                {
                    // Already rated at the bay: they simply wait here now.
                    patient.Stage = PatientStage.WaitingForSpace;
                }
            }
        }

        // Rated at the bay, but the cubicle went to someone else and none is free: off the ambulance and into the corridor.
        foreach (var patient in _patients.Values.Where(p => p.Stage == PatientStage.InAmbulanceQueue && p.TriagedAt.HasValue
                                                             && p.CorridorSpot is null && p.BusyWithTaskId is null && !p.IsMoving && p.IsAlive
                                                             && AmbulanceFor(p) is { State: AmbulanceState.AtBay } && !CubicleFreeFor(p))
                     .OrderBy(p => p.Triage)
                     .ThenBy(p => p.QueuedSince)
                     .ToList())
        {
            var free = FreeCorridorCareSpots();
            if (free.Count == 0)
            {
                break;
            }

            patient.Log(Now, "Still no cubicle free, so the patient is going into corridor care.", important: true);
            SendToCorridor(patient, free[0]);
        }
    }

    private void LeaveCorridor(Patient patient)
    {
        if (patient.CorridorSpot is null)
        {
            return;
        }

        patient.CorridorSpot = null;
        if (patient.CorridorSince is { } since)
        {
            patient.Log(Now, $"Out of the corridor after {(Now - since).TotalMinutes:0} minutes of corridor care.");
            Stats.RecordCorridorTime(Now - since);
        }
    }
}
