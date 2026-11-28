using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Transport;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// Parents. Every child (under 16) comes in with a mum or dad, who walks with them, stands or sits beside
/// them in A&amp;E, waits outside while they have a scan or an operation, sits on the fold-out bed beside their
/// ward bed by day and sleeps on it at night, and goes home with them.
///
/// Parents are worked out from their child each tick (their name and looks come from the child), so they
/// need no saving: after a load they come back beside their child.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>Parents sleep on the fold-out bed from this hour...</summary>
    public const int ParentBedtimeHour = 22;

    /// <summary>...until this one.</summary>
    public const int ParentWakeHour = 7;

    private readonly Dictionary<int, Parent> _parents = new();

    private static readonly string[] MumNames = { "Sarah", "Emma", "Laura", "Gemma", "Rachel", "Amy", "Hannah", "Priya", "Aisha", "Natalie", "Zoe", "Kirsty", "Fatima", "Joanne", "Claire", "Sophie" };

    private static readonly string[] DadNames = { "James", "Daniel", "Chris", "Mark", "Tom", "Ryan", "Lee", "Imran", "Ben", "Craig", "Gareth", "Adam", "Mohammed", "Paul", "Jason", "Stuart" };

    /// <summary>Firefighters and air ambulance crew walking back out after handing their patient over.</summary>
    private readonly List<Parent> _leavingCrew = new();

    /// <summary>Parents in the hospital with their children (and police, fire and air ambulance crew with their patients).</summary>
    public IEnumerable<Parent> Parents => _parents.Values.Concat(_leavingCrew).Concat(_amhps.Values);

    /// <summary>The mum or dad with a child, if they are here.</summary>
    public Parent? ParentOf(Patient child) => _parents.GetValueOrDefault(child.Id);

    public Parent? FindParent(int id) => Parents.FirstOrDefault(p => p.Id == id);

    /// <summary>A patient who comes with a parent: any child, apart from a girl in labour.</summary>
    public static bool HasParentWith(Patient patient) => patient.Age < AdultAge && !patient.Maternity;

    /// <summary>A police officer is with them: brought in by the police and still in A&amp;E (they go once the patient goes up to a ward or home).</summary>
    public static bool HasPoliceWith(Patient patient) =>
        patient.PoliceEscort && !HasParentWith(patient) && !patient.InSection136Suite && patient.Stage is not (PatientStage.OnWard or PatientStage.Gone) && patient.LeftEmergencyAt is null;

    /// <summary>
    /// A firefighter or the air ambulance paramedic is with them: they came in with a major incident casualty
    /// and stay until they have handed over to a nurse (a child's mum or dad, or the police, take their place).
    /// </summary>
    public static bool HasCrewWith(Patient patient) =>
        (patient.FireCrewEscort || patient.AirAmbulance) && !patient.PoliceEscort && !HasParentWith(patient)
        && patient.Stage == PatientStage.InAmbulanceQueue && patient.TriagedAt is null && patient.IsAlive;

    private static readonly string[] CrewFirstNames = { "Sam", "Jo", "Alex", "Chris", "Lee", "Danny", "Becky", "Kieran", "Nadia", "Owen", "Tasha", "Wes" };

    private static readonly string[] PoliceSurnames = { "Taylor", "Walsh", "Okafor", "Hughes", "Patel", "Murray", "Bennett", "Kaur", "Fletcher", "Doyle", "Ahmed", "Price" };

    /// <summary>Whether a parent is asleep on the fold-out bed at this time of day.</summary>
    public bool IsParentBedtime => Now.Hour >= ParentBedtimeHour || Now.Hour < ParentWakeHour;

    private void UpdateParents(double dt)
    {
        foreach (var id in _parents.Keys.Where(id => !_patients.TryGetValue(id, out var c) || !ChildIsOnSite(c) || (_parents[id].Police && !HasPoliceWith(c)) || (_parents[id].Crew && !HasCrewWith(c))).ToList())
        {
            var escort = _parents[id];
            if ((escort.Police || escort.Crew) && _patients.TryGetValue(id, out var left) && left.Stage != PatientStage.Gone)
            {
                left.Log(Now, $"{escort.FullName} handed over and left.");
            }

            if (escort.Police && (_patients.GetValueOrDefault(id) ?? _departed.FirstOrDefault(p => p.Id == id)) is { } escorted)
            {
                NotePoliceLeft(escorted);
            }

            _parents.Remove(id);
            if (escort.Crew)
            {
                // Firefighters and the air crew walk back out: the air crew to the helicopter, the fire crew off down the road to their engine.
                escort.Leaving = true;
                escort.Activity = ParentActivity.WithChild;
                var way = CrewWayOut(escort);
                var path = PathFor(escort, way);
                if (path.Count > 0)
                {
                    escort.FollowPath(path);
                    _leavingCrew.Add(escort);
                }
                else
                {
                    CrewBackAboard(escort);
                }
            }
        }

        foreach (var crew in _leavingCrew.ToList())
        {
            crew.Move(dt);
            if (!crew.IsMoving)
            {
                _leavingCrew.Remove(crew);
                CrewBackAboard(crew);
            }
        }

        foreach (var child in _patients.Values)
        {
            if (!(HasParentWith(child) || HasPoliceWith(child) || HasCrewWith(child)) || !ChildIsOnSite(child))
            {
                continue;
            }

            if (!_parents.TryGetValue(child.Id, out var parent))
            {
                parent = NewParent(child);
                _parents[child.Id] = parent;
            }

            parent.Move(dt);
            Accompany(parent, child);
        }
    }

    /// <summary>On site and on foot (or in bed), not still in the ambulance or the car.</summary>
    private bool ChildIsOnSite(Patient child) =>
        child.Stage != PatientStage.Gone && child.IsAlive
        && !_ambulances.Any(a => a.PatientId == child.Id && a.Purpose == AmbulancePurpose.Arrival && a.State is AmbulanceState.Driving or AmbulanceState.WaitingForBay)
        && !_cars.Any(c => ReferenceEquals(c.Passenger, child))
        && !InHelicopter(child);

    private Parent NewParent(Patient child)
    {
        if (!HasParentWith(child))
        {
            return HasCrewWith(child) ? NewCrewMember(child) : NewPoliceOfficer(child);
        }

        // Mostly mums; the name and age come from the child, so the same parent comes back after a load.
        var roll = (child.Id * 7919) + child.Surname.Length;
        var mum = roll % 10 < 7;
        var names = mum ? MumNames : DadNames;
        var parent = new Parent
        {
            Id = Parent.IdOffset + child.Id,
            ChildId = child.Id,
            FirstName = names[(roll / 10) % names.Length],
            Surname = child.Surname,
            Sex = mum ? Sex.Female : Sex.Male,
            Age = Math.Max(child.Age, 1) + 22 + (roll / 7 % 14),
        };
        parent.PlaceAt(ParentSpotBeside(child.Tile));
        if (!child.Timeline.Any(e => e.Text.StartsWith("Came in with", StringComparison.Ordinal)))
        {
            child.Log(Now, $"Came in with {(mum ? "their mum" : "their dad")}, {parent.FirstName}.");
        }

        return parent;
    }

    private Parent NewPoliceOfficer(Patient patient)
    {
        var roll = (patient.Id * 6151) + patient.Surname.Length;
        var officer = new Parent
        {
            Id = Parent.IdOffset + patient.Id,
            ChildId = patient.Id,
            FirstName = "PC",
            Surname = PoliceSurnames[roll % PoliceSurnames.Length],
            Sex = roll % 3 == 0 ? Sex.Female : Sex.Male,
            Age = 24 + (roll / 7 % 24),
            Escort = EscortKind.Police,
        };
        officer.PlaceAt(ParentSpotBeside(patient.Tile));
        if (!patient.Timeline.Any(e => e.Text.StartsWith("Brought in by the police", StringComparison.Ordinal)))
        {
            patient.Log(Now, $"Brought in by the police: {officer.FullName} is staying with them.");
        }

        return officer;
    }

    /// <summary>A firefighter (for a fire crew casualty) or the air ambulance paramedic who came in with them.</summary>
    private Parent NewCrewMember(Patient patient)
    {
        var roll = (patient.Id * 4271) + patient.Surname.Length;
        var air = patient.AirAmbulance;
        var crew = new Parent
        {
            Id = Parent.IdOffset + patient.Id,
            ChildId = patient.Id,
            FirstName = CrewFirstNames[roll % CrewFirstNames.Length],
            Surname = PoliceSurnames[(roll / 3) % PoliceSurnames.Length],
            Sex = roll % 3 == 0 ? Sex.Female : Sex.Male,
            Age = 26 + (roll / 7 % 22),
            Escort = air ? EscortKind.AirCrew : EscortKind.FireCrew,
        };
        crew.PlaceAt(ParentSpotBeside(patient.Tile));
        var line = air ? "Came in with the air ambulance crew" : "Brought out by the fire service";
        if (!patient.Timeline.Any(e => e.Text.StartsWith(line, StringComparison.Ordinal)))
        {
            patient.Log(Now, $"{line}: {(air ? "paramedic" : "firefighter")} {crew.FullName} is staying with them until the handover.");
        }

        return crew;
    }

    /// <summary>Where a firefighter or air crew member heads once they have handed over.</summary>
    private GridCoord CrewWayOut(Parent crew) =>
        crew.Escort == EscortKind.AirCrew && HelicopterFor(crew.ChildId) is { State: HelicopterState.Landed } && Helipad is { } pad
            ? HelipadDoorTile(pad)
            : AmbulanceLane.Exit;

    /// <summary>A crew member has walked back out: the air crew are back on board their helicopter.</summary>
    private void CrewBackAboard(Parent crew)
    {
        if (crew.Escort == EscortKind.AirCrew && HelicopterFor(crew.ChildId) is { } helicopter)
        {
            helicopter.CrewAboard = true;
        }
    }

    /// <summary>Keeps a parent with their child, or on the fold-out bed beside their ward bed.</summary>
    private void Accompany(Parent parent, Patient child)
    {
        // On the ward and in bed: the fold-out bed beside it.
        if (child.Stage == PatientStage.OnWard && !child.IsMoving && BedOf(child) is { } bed && child.Tile == bed.PatientSpot
            && FindSpace(child.CareSpaceId!) is { } ward && FoldOutBedBeside(ward, bed) is { } foldOut)
        {
            // Now and then, by day, a break in the parents' room.
            if (TakeParentsRoomBreak(parent, child))
            {
                return;
            }

            var by = TileBeside(foldOut, ward);
            if (parent.Tile == by && !parent.IsMoving)
            {
                if (parent.FoldOutBed is null)
                {
                    child.Log(Now, $"{parent.Relation} is staying with them on the fold-out bed.");
                }

                parent.FoldOutBed = foldOut;
                parent.Activity = IsParentBedtime ? ParentActivity.Sleeping : ParentActivity.SittingByBed;
                return;
            }

            parent.FoldOutBed = null;
            parent.Activity = ParentActivity.WithChild;
            Head(parent, by);
            return;
        }

        parent.FoldOutBed = null;
        parent.BreakUntil = null;

        // Scans and operations: wait in the parents' room if there is one, or in the corridor outside, until they come out.
        if (child.CareSpaceId is { } spaceId && FindSpace(spaceId) is { } room && !child.IsMoving && room.Rect.Contains(child.Tile)
            && (room.IsServiceRoom || room.Kind is CareSpaceKind.AnaestheticRoom or CareSpaceKind.TheatreRecovery))
        {
            if (parent.Activity == ParentActivity.InParentsRoom || (parent.Activity != ParentActivity.WaitingOutside && !parent.Police && ParentsRoomSeat(parent) is { } sofa && GoToParentsRoom(parent, sofa)))
            {
                return;
            }

            if (parent.Activity != ParentActivity.WaitingOutside)
            {
                parent.Activity = ParentActivity.WaitingOutside;
                Head(parent, FreeSpot(parent, OutsideDoor(room)));
            }

            return;
        }

        parent.Activity = ParentActivity.WithChild;

        // On the move: keep up, a step behind.
        if (child.IsMoving)
        {
            if (Chebyshev(parent.Tile, child.Tile) > 1 && (parent.Destination is not { } d || Chebyshev(d, child.Tile) > 2))
            {
                Head(parent, child.Destination is { } where && Chebyshev(child.Tile, where) <= 2 ? ParentSpotBeside(where) : child.Tile, force: true);
            }

            return;
        }

        if (parent.IsMoving && parent.Destination is { } heading && Chebyshev(heading, child.Tile) <= 2)
        {
            return;
        }

        // Sitting down already: stay put.
        if (IsSeat(parent.Tile) && Chebyshev(parent.Tile, child.Tile) <= 4 && !SeatBooked(parent.Tile) && !IsSpotTaken(parent.Tile, parent))
        {
            return;
        }

        // A free chair: in the play room with them, next to theirs in the waiting room, or the visitor's chair in their cubicle.
        var chair = PlayRoomOf(child) is { } playRoom
            ? SeatTiles.Where(t => playRoom.Rect.Contains(t) && t != child.Tile && !IsSpotTaken(t, parent) && !SeatBooked(t) && !PlaySeatBooked(t))
                .OrderBy(t => Chebyshev(t, child.Tile)).Cast<GridCoord?>().FirstOrDefault()
            : child.Seat == child.Tile
            ? Neighbours(child.Tile).Where(t => IsSeat(t) && !IsSpotTaken(t, parent) && !SeatBooked(t)).Cast<GridCoord?>().FirstOrDefault()
            : child.CareSpaceId is { } cubicleId && FindSpace(cubicleId) is { IsWard: false, Kind: not CareSpaceKind.Waiting } cubicle && cubicle.Rect.Contains(child.Tile)
                ? SeatTiles.Where(t => cubicle.Rect.Contains(t) && !IsSpotTaken(t, parent) && !SeatBooked(t)).OrderBy(t => Chebyshev(t, child.Tile)).Cast<GridCoord?>().FirstOrDefault()
                : null;
        if (chair is { } seat)
        {
            Head(parent, seat);
            return;
        }

        // Otherwise somewhere to stand beside them.
        if (Chebyshev(parent.Tile, child.Tile) <= 2 && parent.Tile != child.Tile && !IsSpotTaken(parent.Tile, parent) && !IsSeat(parent.Tile))
        {
            return;
        }

        Head(parent, FreeSpot(parent, child.Tile));
    }

    /// <summary>How long a parent's break in the parents' room lasts, and roughly how often they take one by day.</summary>
    private static readonly TimeSpan ParentsRoomBreak = TimeSpan.FromMinutes(25);

    private static readonly TimeSpan ParentsRoomBreakEvery = TimeSpan.FromHours(3);

    /// <summary>Parents' rooms.</summary>
    public IEnumerable<CareSpace> ParentsRooms => SpacesOfKind(CareSpaceKind.ParentsRoom);

    /// <summary>
    /// A parent sitting with their child on the ward takes a break in the parents' room every few hours by
    /// day (never at night), then goes back to the bedside. True while they are on one.
    /// </summary>
    private bool TakeParentsRoomBreak(Parent parent, Patient child)
    {
        if (parent.BreakUntil is { } until)
        {
            if (Now < until && !IsParentBedtime)
            {
                if (!parent.IsMoving && parent.Heading is { } sofa && parent.Tile != sofa)
                {
                    Head(parent, sofa, force: true);
                }

                return true;
            }

            parent.BreakUntil = null;
            parent.NextBreakAt = Now + ParentsRoomBreakEvery + TimeSpan.FromMinutes(parent.Id % 60);
            parent.Activity = ParentActivity.WithChild;
            return false;
        }

        if (parent.NextBreakAt is null)
        {
            parent.NextBreakAt = Now + TimeSpan.FromMinutes(60 + (parent.Id % 120));
            return false;
        }

        if (IsParentBedtime || Now.Hour < ParentWakeHour + 1 || Now < parent.NextBreakAt || parent.Activity != ParentActivity.SittingByBed
            || ParentsRoomSeat(parent) is not { } seat)
        {
            return false;
        }

        parent.FoldOutBed = null;
        parent.BreakUntil = Now + ParentsRoomBreak;
        child.Log(Now, $"{parent.Relation} went for a cup of tea in the parents' room.");
        GoToParentsRoom(parent, seat);
        return true;
    }

    private bool GoToParentsRoom(Parent parent, GridCoord seat)
    {
        parent.Activity = ParentActivity.InParentsRoom;
        Head(parent, seat, force: true);
        return true;
    }

    /// <summary>A free seat in the nearest parents' room on the parent's floor, if there is one.</summary>
    private GridCoord? ParentsRoomSeat(Parent parent)
    {
        var floor = Site.FloorOf(parent.Tile);
        return ParentsRooms
            .Where(r => Site.FloorOf(new GridCoord(r.Rect.X, r.Rect.Y)) == floor)
            .SelectMany(r => r.Seats)
            .Where(t => !IsSpotTaken(t, parent) && !_parents.Values.Any(o => o != parent && (o.Heading == t || o.Tile == t)))
            .OrderBy(t => Chebyshev(t, parent.Tile))
            .Cast<GridCoord?>()
            .FirstOrDefault();
    }

    private bool SeatBooked(GridCoord tile) =>
        _patients.Values.Any(p => p.Seat == tile && p.Stage != PatientStage.Gone) || _outpatients.Any(o => o.Seat == tile);

    private static IEnumerable<GridCoord> Neighbours(GridCoord tile)
    {
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1) })
        {
            yield return new GridCoord(tile.X + dx, tile.Y + dy);
        }
    }

    private void Head(Parent parent, GridCoord to, bool force = false)
    {
        if (!force && parent.Heading == to && (parent.IsMoving || parent.Tile == to))
        {
            return;
        }

        parent.Heading = to;
        if (parent.Tile == to)
        {
            parent.StopMoving();
            return;
        }

        var path = PathFor(parent, to);
        if (path.Count > 0)
        {
            parent.FollowPath(path);
        }
        else
        {
            parent.PlaceAt(to);
        }
    }

    /// <summary>A walkable tile next to <paramref name="tile"/>, or the tile itself.</summary>
    private GridCoord ParentSpotBeside(GridCoord tile) =>
        Neighbours(tile).Where(t => Nav.IsWalkable(t) && Site.FloorOf(t) == Site.FloorOf(tile)).Cast<GridCoord?>().FirstOrDefault() ?? tile;

    /// <summary>The corridor tile just outside a room's door.</summary>
    private GridCoord OutsideDoor(CareSpace room)
    {
        foreach (var door in room.Doors)
        {
            foreach (var outside in DoorTiles(room, door).Select(t => Step(t, door.Side)))
            {
                if (Nav.IsWalkable(outside))
                {
                    return outside;
                }
            }
        }

        return room.StaffSpot;
    }

    /// <summary>The fold-out bed beside a ward bed: the nearest one on the ward.</summary>
    public static PlacedFurniture? FoldOutBedBeside(CareSpace ward, WardBed bed) =>
        ward.Furniture.Where(f => f.ItemId == "parent_bed")
            .OrderBy(f => Math.Abs(f.CentreX - (bed.PatientSpot.X + 0.5)) + Math.Abs(f.CentreY - (bed.PatientSpot.Y + 0.5)))
            .FirstOrDefault();

    /// <summary>Where a parent stands to get on to a fold-out bed: a free floor tile beside it, on the open side.</summary>
    private GridCoord TileBeside(PlacedFurniture foldOut, CareSpace ward)
    {
        var centre = new GridCoord((int)Math.Floor(ward.Rect.X + (ward.Rect.Width / 2.0)), (int)Math.Floor(ward.Rect.Y + (ward.Rect.Length / 2.0)));
        var x0 = (int)Math.Floor(foldOut.X) - 1;
        var y0 = (int)Math.Floor(foldOut.Y) - 1;
        var x1 = (int)Math.Ceiling(foldOut.X + foldOut.Width);
        var y1 = (int)Math.Ceiling(foldOut.Y + foldOut.Length);
        var ring = new List<GridCoord>();
        for (var x = x0; x <= x1; x++)
        {
            for (var y = y0; y <= y1; y++)
            {
                if ((x == x0 || x == x1 || y == y0 || y == y1) && ward.Rect.Contains(new GridCoord(x, y)) && Nav.IsWalkable(new GridCoord(x, y)))
                {
                    ring.Add(new GridCoord(x, y));
                }
            }
        }

        var bedTile = foldOut.Tile;
        return ring.OrderBy(t => Math.Abs(t.Y - centre.Y)).ThenBy(t => Math.Abs(t.X - bedTile.X)).Cast<GridCoord?>().FirstOrDefault() ?? bedTile;
    }
}
