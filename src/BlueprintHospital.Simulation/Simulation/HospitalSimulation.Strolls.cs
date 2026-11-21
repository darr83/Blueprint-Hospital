using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>Where someone is on a short walk to stretch their legs.</summary>
public enum StrollStage
{
    /// <summary>Walking out to somewhere a little way off.</summary>
    Out,

    /// <summary>Stopped for a minute or two: looking out of the window, reading the noticeboard.</summary>
    Pausing,

    /// <summary>Walking back to their seat or bed.</summary>
    Back,
}

/// <summary>A patient stretching their legs: a waiting patient up from their chair, or a ward patient up from their bed.</summary>
public sealed class Stroll
{
    public required Agent Person { get; init; }

    /// <summary>Where they walk to.</summary>
    public required GridCoord To { get; init; }

    /// <summary>Where they go back to: their chair or their bed.</summary>
    public required GridCoord Home { get; init; }

    /// <summary>A ward patient up from their bed (as opposed to someone waiting on a chair).</summary>
    public bool FromBed { get; init; }

    public StrollStage Stage { get; set; } = StrollStage.Out;

    public DateTime? Until { get; set; }
}

/// <summary>An idle member of staff's walk round: checking on patients, popping to the nurses' station.</summary>
internal sealed class Walkabout
{
    public GridCoord? To { get; set; }

    public DateTime? Until { get; set; }

    public DateTime NextAt { get; set; }

    public string What { get; set; } = string.Empty;
}

/// <summary>
/// People moving about between the things they came for, so the hospital looks lived in. Patients who have
/// been waiting a while get up now and then for a short walk and come back to their chair; ward patients who
/// are well enough walk up and down the ward by day; staff with nothing to do don't just stand at their post
/// but walk round their patients, check the board at the nurses' station, look in on a cubicle. Anything
/// real (a call, a task, a porter) cuts a walk short. Nothing here is saved: after a load everyone is where they
/// were and the walks simply start again.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>A waiting patient thinks about a walk once they have waited this long.</summary>
    public static readonly TimeSpan StrollAfterWaiting = TimeSpan.FromMinutes(30);

    /// <summary>On average an eligible waiting patient gets up for a walk about once in this many minutes.</summary>
    private const double StrollEveryMinutes = 45;

    /// <summary>On average a ward patient who is up to it walks up and down the ward once in this many minutes (by day).</summary>
    private const double WardStrollEveryMinutes = 150;

    private readonly Dictionary<Agent, Stroll> _strolls = new();
    private readonly Dictionary<Agent, DateTime> _lastStrollAt = new();
    private readonly Dictionary<int, Walkabout> _walkabouts = new();
    private GameRandom? _strollRandom;

    /// <summary>A separate random stream, so walks don't change anything else that happens.</summary>
    private GameRandom StrollRandom => _strollRandom ??= new GameRandom(_options.Seed + 2_750_159);

    /// <summary>Everyone out for a walk right now.</summary>
    public IReadOnlyCollection<Stroll> Strolls => _strolls.Values;

    /// <summary>The walk someone is on, if any.</summary>
    public Stroll? StrollOf(Agent person) => _strolls.GetValueOrDefault(person);

    /// <summary>What an idle member of staff is doing on their walk round, if they are on one.</summary>
    public string? WalkaboutOf(StaffMember member) =>
        _walkabouts.TryGetValue(member.Id, out var w) && w.To is not null ? w.What : null;

    /// <summary>A ward patient out of bed for a walk: no tasks start on them until they are back.</summary>
    private bool OutOfBedForWalk(Patient patient) => _strolls.TryGetValue(patient, out var s) && s.FromBed;

    private void UpdateStrolls(double dt)
    {
        if (!_options.LivelyPeople)
        {
            return;
        }

        foreach (var stroll in _strolls.Values.ToList())
        {
            ProgressStroll(stroll);
        }

        var chance = dt / (StrollEveryMinutes * 60);
        foreach (var patient in _patients.Values.Where(CanStroll).ToList())
        {
            if (StrollRandom.Chance(chance))
            {
                StartStroll(patient, patient.Seat!.Value, fromBed: false);
            }
        }

        foreach (var outpatient in _outpatients.Where(CanStroll).ToList())
        {
            if (StrollRandom.Chance(chance))
            {
                StartStroll(outpatient, outpatient.Seat!.Value, fromBed: false);
            }
        }

        if (Now.Hour is >= 9 and < 20)
        {
            var wardChance = dt / (WardStrollEveryMinutes * 60);
            foreach (var patient in _patients.Values.Where(CanWalkOnWard).ToList())
            {
                if (StrollRandom.Chance(wardChance))
                {
                    StartStroll(patient, patient.Tile, fromBed: true);
                }
            }
        }
    }

    /// <summary>A walking A&amp;E patient sitting in the waiting area (not Red) who has waited a good while.</summary>
    private bool CanStroll(Patient p) =>
        p.Stage is PatientStage.WaitingForTriage or PatientStage.WaitingForSpace
        && p.Triage != TriageCategory.Red
        && p.Mobility == Mobility.Walking
        && p.Seat is { } seat && p.Tile == seat && !p.IsMoving
        && p.BusyWithTaskId is null && p.CareSpaceId is null && p.PendingMove is null
        && Now - p.QueuedSince >= StrollAfterWaiting
        && !Away(p) && !_strolls.ContainsKey(p) && !StrolledLately(p);

    /// <summary>A checked-in clinic patient sitting down, whose appointment isn't due for a while.</summary>
    private bool CanStroll(Outpatient o) =>
        o.Stage == OutpatientStage.Waiting
        && o.Seat is { } seat && o.Tile == seat && !o.IsMoving
        && FindAppointment(o.AppointmentId) is { } appointment && appointment.Time - Now >= TimeSpan.FromMinutes(20)
        && appointment.CheckedInAt is { } checkedIn && Now - checkedIn >= TimeSpan.FromMinutes(15)
        && !Away(o) && !_strolls.ContainsKey(o) && !StrolledLately(o);

    /// <summary>An adult on an ordinary ward, walking, settled (low early warning score), in their bed with nothing being done for them.</summary>
    private bool CanWalkOnWard(Patient p) =>
        p.Stage == PatientStage.OnWard && p.IsAlive && !p.InArrest && !p.OnFloorAfterFall
        && p.Mobility == Mobility.Walking && p.Age >= AdultAge && !p.Maternity
        && !p.IsMoving && p.BusyWithTaskId is null && p.PendingMove is null
        && (p.LatestObservations?.News2.Total ?? 0) <= 2
        && BedOf(p) is { } bed && p.Tile == bed.PatientSpot
        && p.CareSpaceId is { } wardId && FindSpace(wardId) is { } ward && UnitOf(ward) is Data.Models.WardUnit.None or Data.Models.WardUnit.Assessment
        && !_theatreRecovery.ContainsKey(p.Id)
        && !_tasks.Values.Any(t => t.PatientId == p.Id)
        && !_strolls.ContainsKey(p) && !StrolledLately(p);

    private bool StrolledLately(Agent person) => _lastStrollAt.TryGetValue(person, out var at) && Now - at < TimeSpan.FromMinutes(90);

    private void StartStroll(Agent person, GridCoord home, bool fromBed)
    {
        if (StrollTarget(person, fromBed) is not { } to)
        {
            _lastStrollAt[person] = Now;
            return;
        }

        _strolls[person] = new Stroll { Person = person, To = to, Home = home, FromBed = fromBed };
        _lastStrollAt[person] = Now;
        StrollMove(person, to);
        switch (person)
        {
            case Outpatient o:
                o.StatusText = "Stretching their legs";
                break;
            case Patient p when fromBed:
                p.Log(Now, "Got up for a walk on the ward.");
                break;
        }
    }

    /// <summary>
    /// Somewhere a few steps away to walk to: for a waiting patient a corridor tile within ten or so tiles; for a
    /// ward patient the far end of the ward or just outside its door.
    /// </summary>
    private GridCoord? StrollTarget(Agent person, bool fromBed)
    {
        var floor = Site.FloorOf(person.Tile);
        var taken = TakenSpots(person);
        if (fromBed && person is Patient p && p.CareSpaceId is { } wardId && FindSpace(wardId) is { } ward)
        {
            var outside = OutsideDoor(ward);
            var aisle = ward.Rect.Tiles()
                .Where(t => !ward.Rect.IsOnEdge(t) && Nav.IsWalkable(t) && !IsBedTile(ward, t) && !IsSeat(t) && !taken.Contains(t)
                            && !ward.Beds.Any(b => b.PatientSpot == t || b.StaffSpot == t) && Chebyshev(t, person.Tile) >= 4)
                .ToList();
            var choices = aisle.Count > 0 && StrollRandom.Chance(0.6) ? aisle : new List<GridCoord>();
            if (choices.Count == 0 && Nav.IsWalkable(outside))
            {
                var corridor = Neighbours(outside).Append(outside).Where(t => Nav.IsWalkable(t) && !ward.Rect.Contains(t) && !taken.Contains(t)).ToList();
                choices = corridor;
            }

            return choices.Count == 0 ? null : choices[StrollRandom.Range(0, choices.Count - 1)];
        }

        // A waiting patient: somewhere along a corridor nearby, or the far side of the waiting room.
        var near = new List<GridCoord>();
        for (var dx = -10; dx <= 10; dx++)
        {
            for (var dy = -10; dy <= 10; dy++)
            {
                var t = new GridCoord(person.Tile.X + dx, person.Tile.Y + dy);
                var d = Math.Abs(dx) + Math.Abs(dy);
                if (d < 4 || d > 12 || !Nav.IsWalkable(t) || Site.FloorOf(t) != floor || IsSeat(t) || taken.Contains(t))
                {
                    continue;
                }

                if (RoomAt(t) is { } room && room.Kind is not (CareSpaceKind.Corridor or CareSpaceKind.Waiting))
                {
                    continue;
                }

                near.Add(t);
            }
        }

        return near.Count == 0 ? null : near[StrollRandom.Range(0, near.Count - 1)];
    }

    private CareSpace? RoomAt(GridCoord tile) =>
        _spaces.Values.Where(s => s.Rect.Contains(tile)).OrderBy(s => s.Kind == CareSpaceKind.Corridor ? 1 : 0).FirstOrDefault();

    private void ProgressStroll(Stroll stroll)
    {
        var person = stroll.Person;
        if (StrollInterrupted(stroll))
        {
            _strolls.Remove(person);
            if (stroll.FromBed && person is Patient p && p.Stage == PatientStage.OnWard && !p.InArrest && p.PendingMove is null && BedOf(p) is { } bed)
            {
                // Called back: a nurse is coming, so back to bed.
                WalkTo(p, bed.PatientSpot);
            }

            return;
        }

        var expected = stroll.Stage == StrollStage.Back ? stroll.Home : stroll.To;
        var arrived = !person.IsMoving && person.Tile == expected;
        if (!person.IsMoving && !arrived)
        {
            // Got stuck (a wall went up): head home.
            if (stroll.Stage == StrollStage.Back)
            {
                _strolls.Remove(person);
                return;
            }

            stroll.Stage = StrollStage.Back;
            StrollMove(person, stroll.Home);
            return;
        }

        switch (stroll.Stage)
        {
            case StrollStage.Out when arrived:
                stroll.Stage = StrollStage.Pausing;
                stroll.Until = Now + TimeSpan.FromSeconds(StrollRandom.Range(45, 150));
                break;

            case StrollStage.Pausing when Now >= stroll.Until:
                stroll.Stage = StrollStage.Back;
                StrollMove(person, stroll.Home);
                break;

            case StrollStage.Back when arrived:
                _strolls.Remove(person);
                if (person is Outpatient o)
                {
                    o.StatusText = "Waiting";
                }

                break;
        }
    }

    /// <summary>Called, moved, gone home or otherwise taken over: the walk is off.</summary>
    private bool StrollInterrupted(Stroll stroll)
    {
        var expected = stroll.Stage == StrollStage.Back ? stroll.Home : stroll.To;
        if (stroll.Person.Destination is { } heading && heading != expected)
        {
            return true;
        }

        return stroll.Person switch
        {
            Patient p when stroll.FromBed => p.Stage != PatientStage.OnWard || !p.IsAlive || p.InArrest || p.OnFloorAfterFall || p.PendingMove is not null
                                             || (stroll.Stage != StrollStage.Back && _tasks.Values.Any(t => t.PatientId == p.Id)),
            Patient p => p.Stage is not (PatientStage.WaitingForTriage or PatientStage.WaitingForSpace)
                         || p.BusyWithTaskId is not null || p.CareSpaceId is not null || p.PendingMove is not null,
            Outpatient o => o.Stage != OutpatientStage.Waiting,
            _ => true,
        };
    }

    private void StrollMove(Agent person, GridCoord to)
    {
        switch (person)
        {
            case Patient p:
                WalkTo(p, to);
                break;
            case Outpatient o:
                Walk(o, to);
                break;
        }
    }

    // ---------------------------------------------------------------- staff walking round

    /// <summary>
    /// For an idle member of staff with nothing to do: now and then, instead of standing at their post, a walk
    /// round. True while they are on one (the caller then leaves them to it).
    /// </summary>
    private bool IdleWalkabout(StaffMember member)
    {
        if (!_options.LivelyPeople || !Wanders(member))
        {
            return false;
        }

        if (!_walkabouts.TryGetValue(member.Id, out var walk))
        {
            walk = new Walkabout { NextAt = Now + TimeSpan.FromMinutes(5 + (member.Id * 7 % 25)) };
            _walkabouts[member.Id] = walk;
        }

        if (walk.To is { } to)
        {
            if (member.Tile == to && !member.IsMoving)
            {
                walk.Until ??= Now + TimeSpan.FromSeconds(StrollRandom.Range(40, 120));
                if (Now < walk.Until)
                {
                    member.StatusText = walk.What;
                    return true;
                }

                walk.To = null;
                walk.Until = null;
                walk.NextAt = Now + TimeSpan.FromMinutes(StrollRandom.Range(12, 35));
                return false;
            }

            if (!member.IsMoving && member.Destination != to)
            {
                WalkTo(member, to);
                if (!member.IsMoving && member.Tile != to)
                {
                    walk.To = null;
                    walk.NextAt = Now + TimeSpan.FromMinutes(20);
                    return false;
                }
            }

            member.StatusText = walk.What;
            return true;
        }

        if (Now < walk.NextAt || member.IsMoving)
        {
            return false;
        }

        if (WalkaboutTarget(member) is not { } target)
        {
            walk.NextAt = Now + TimeSpan.FromMinutes(20);
            return false;
        }

        walk.To = target.Tile;
        walk.What = target.What;
        walk.Until = null;
        member.StatusText = target.What;
        WalkTo(member, target.Tile);
        return true;
    }

    /// <summary>Nurses, doctors, HCAs, midwives and the children's team walk round; desk, clinic and service-room staff stay put.</summary>
    private bool Wanders(StaffMember member)
    {
        if (member.BedsidePatientId is not null || member.Role is not (StaffRole.Nurse or StaffRole.Doctor or StaffRole.HealthcareAssistant
                or StaffRole.Midwife or StaffRole.ChildrensNurse or StaffRole.Paediatrician or StaffRole.OutreachNurse))
        {
            return false;
        }

        return member.BaseRoomId is not { } roomId || FindSpace(roomId) is not { } room || room.IsWard || room.Kind == CareSpaceKind.OutreachOffice;
    }

    /// <summary>Somewhere useful-looking to walk to: beside a patient's bed or cubicle, or the nurses' station.</summary>
    private (GridCoord Tile, string What)? WalkaboutTarget(StaffMember member)
    {
        var floor = Site.FloorOf(member.Tile);
        var options = new List<(GridCoord Tile, string What)>();
        if (member.BaseRoomId is { } roomId && FindSpace(roomId) is { IsWard: true } ward)
        {
            foreach (var bed in ward.Beds.Where(b => b.OccupantId is { } id && _patients.TryGetValue(id, out var p) && p.Stage == PatientStage.OnWard))
            {
                var patient = _patients[bed.OccupantId!.Value];
                options.Add((bed.StaffSpot, $"Checking on {patient.FirstName}"));
            }

            foreach (var station in ward.Furniture.Where(f => f.ItemId == "station_counter"))
            {
                options.Add((NearbyFloor(station.Tile, ward, 1), "At the nurses' station"));
            }
        }
        else
        {
            // A&E: look in on someone in a cubicle, or walk the floor.
            foreach (var space in _spaces.Values.Where(s => s.IsTreatmentSpace && Site.FloorOf(s.StaffSpot) == floor && Chebyshev(s.StaffSpot, member.Tile) <= 30))
            {
                if (space.IsMultiBed)
                {
                    foreach (var bed in space.Beds.Where(b => b.OccupantId is not null))
                    {
                        options.Add((bed.StaffSpot, "Looking in on a patient"));
                    }
                }
                else if (space.OccupantId is not null && space.ReservedByStaffId is null)
                {
                    options.Add((space.StaffSpot, $"Looking in on {space.Name}"));
                }
            }

            foreach (var station in SpacesOfKind(CareSpaceKind.NursesStation).Where(s => Site.FloorOf(s.StaffSpot) == floor))
            {
                options.Add((station.StaffSpot, "At the nurses' station"));
            }
        }

        var taken = TakenSpots(member);
        var free = options.Where(o => o.Tile != member.Tile && Nav.IsWalkable(o.Tile) && !taken.Contains(o.Tile)).ToList();
        return free.Count == 0 ? null : free[StrollRandom.Range(0, free.Count - 1)];
    }
}
