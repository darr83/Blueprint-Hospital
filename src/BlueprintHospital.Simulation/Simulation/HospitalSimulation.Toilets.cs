using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>Where someone is on a trip to the toilet.</summary>
public enum ToiletStage
{
    /// <summary>Walking to a toilet (or to the queue just inside the door).</summary>
    Going,

    /// <summary>Waiting inside the door for a cubicle to come free.</summary>
    Queueing,

    /// <summary>In the cubicle or toilet room.</summary>
    Using,

    /// <summary>Washing their hands at a basin.</summary>
    Washing,
}

/// <summary>A wet patch on the floor where someone couldn't get to a toilet in time. The cleaners mop it up after a while.</summary>
public sealed record FloorAccident(GridCoord Tile, DateTime Until);

/// <summary>A trip to the toilet by a waiting patient, an outpatient or a member of staff on a break.</summary>
public sealed class ToiletTrip
{
    public required Agent Person { get; init; }

    public required string RoomId { get; set; }

    /// <summary>Where they are heading or standing now: a toilet, a queue tile or a basin.</summary>
    public required GridCoord Target { get; set; }

    public ToiletStage Stage { get; set; } = ToiletStage.Going;

    /// <summary>When the current stage (using the toilet, washing) finishes.</summary>
    public DateTime? Until { get; set; }

    /// <summary>When they joined the queue, so they give up after a while.</summary>
    public DateTime? QueuedAt { get; set; }

    /// <summary>For staff: the staff room seat to go back to.</summary>
    public GridCoord? ReturnTo { get; init; }
}

/// <summary>
/// Toilets. Patients waiting in A&amp;E and outpatients waiting for their clinic need the toilet now and
/// then (sooner after a drink from the cafe). They walk to the nearest free toilet, wait just inside the
/// door if every cubicle is in use, wash their hands and go back to their seat. Older patients use the
/// accessible toilet first. Staff on a break sometimes pop to the staff toilet, or to the public ones when
/// there isn't one. A hospital with no public toilets makes waiting patients more likely to give up.
/// Called away while they are there, people simply go straight to whoever called them.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>How long a patient waits before they might need the toilet.</summary>
    public static readonly TimeSpan ToiletAfterWaiting = TimeSpan.FromMinutes(30);

    /// <summary>After going, how long before they might need to go again.</summary>
    public static readonly TimeSpan ToiletGap = TimeSpan.FromMinutes(90);

    /// <summary>A patient older than this uses the accessible toilet when there is one.</summary>
    public const int AccessibleToiletAge = 75;

    /// <summary>With no public toilets, waiting patients are this much more likely to walk out.</summary>
    public const double NoToiletsWalkOutFactor = 1.5;

    /// <summary>The chance that an adult who needs the toilet and can't get to one has an accident on the floor.</summary>
    public const double AccidentChance = 0.05;

    /// <summary>Children are this much more likely to have an accident. (Raise it further for a children's hospital.)</summary>
    public const double ChildAccidentMultiplier = 3.0;

    /// <summary>Patients younger than this count as children.</summary>
    public const int ChildAge = 12;

    /// <summary>How long a wet patch stays before the cleaners have mopped it up.</summary>
    public static readonly TimeSpan AccidentCleanedAfter = TimeSpan.FromMinutes(30);

    /// <summary>On average, an eligible waiting patient needs the toilet about once in this many minutes.</summary>
    private const double ToiletEveryMinutes = 120;

    /// <summary>On average, a member of staff on a break goes about once in this many minutes.</summary>
    private const double StaffToiletEveryMinutes = 15;

    /// <summary>People give up queueing for a cubicle after this long and go back to their seat.</summary>
    private static readonly TimeSpan MaxToiletQueue = TimeSpan.FromMinutes(15);

    private readonly Dictionary<Agent, ToiletTrip> _toiletTrips = new();
    private readonly Dictionary<Agent, DateTime> _lastToiletAt = new();
    private readonly List<FloorAccident> _accidents = new();
    private GameRandom? _toiletRandom;

    /// <summary>A separate random stream, so building toilets doesn't change anything else that happens.</summary>
    private GameRandom ToiletRandom => _toiletRandom ??= new GameRandom(_options.Seed + 130_363);

    /// <summary>Public toilets, accessible toilets and staff toilets.</summary>
    public IEnumerable<CareSpace> ToiletRooms => _spaces.Values.Where(s => s.IsToilet);

    /// <summary>True when patients and visitors have a toilet they can use.</summary>
    public bool HasPublicToilets => _spaces.Values.Any(s => s.Kind is CareSpaceKind.Toilets or CareSpaceKind.AccessibleToilet);

    /// <summary>Everyone on their way to, in or washing up after the toilet right now.</summary>
    public IReadOnlyCollection<ToiletTrip> ToiletTrips => _toiletTrips.Values;

    /// <summary>Wet patches on the floor waiting to be mopped up.</summary>
    public IReadOnlyList<FloorAccident> FloorAccidents => _accidents;

    /// <summary>How likely a patient of this age is to have an accident when they can't get to a toilet.</summary>
    public static double AccidentChanceFor(int age) => age < ChildAge ? AccidentChance * ChildAccidentMultiplier : AccidentChance;

    /// <summary>Leaves a wet patch on a tile, as an accident would (for tests and screenshots).</summary>
    public void SpillForTest(GridCoord tile) => _accidents.Add(new FloorAccident(tile, Now + AccidentCleanedAfter));

    /// <summary>The toilet trip a person is on, if any.</summary>
    public ToiletTrip? ToiletTripOf(Agent person) => _toiletTrips.GetValueOrDefault(person);

    private void UpdateToilets(double dt)
    {
        var rooms = ToiletRooms.ToList();
        foreach (var trip in _toiletTrips.Values.ToList())
        {
            if (ToiletInterrupted(trip))
            {
                _toiletTrips.Remove(trip.Person);
                continue;
            }

            if (rooms.FirstOrDefault(r => r.Id == trip.RoomId) is not { } room)
            {
                // The toilets were knocked down while they were there.
                ToiletGoBack(trip);
                continue;
            }

            ToiletProgress(trip, room);
        }

        foreach (var (person, at) in _lastToiletAt.ToList())
        {
            if (Now - at > ToiletGap)
            {
                _lastToiletAt.Remove(person);
            }
        }

        _accidents.RemoveAll(a => Now >= a.Until);

        // Patients need the toilet whether or not there is one to go to.
        var chance = dt / (ToiletEveryMinutes * 60);
        foreach (var patient in _patients.Values.Where(CanGoToToilet).ToList())
        {
            // A drink in the last hour makes it twice as likely.
            var drank = patient.LastRefreshmentAt is { } had && Now - had < TimeSpan.FromHours(1);
            if (ToiletRandom.Chance(drank ? chance * 2 : chance)
                && !StartToiletTrip(patient, rooms, preferAccessible: patient.Age >= AccessibleToiletAge, staff: false)
                && !rooms.Any(r => UsableBy(r, staff: false)))
            {
                // Nowhere to go at all: they hold on, or now and then don't make it.
                NoToiletFor(patient);
            }
        }

        if (rooms.Count == 0)
        {
            return;
        }

        SendStrandedBackFromToilets(rooms);

        foreach (var outpatient in _outpatients.Where(CanGoToToilet).ToList())
        {
            var drank = outpatient.LastRefreshmentAt is { } had && Now - had < TimeSpan.FromHours(1);
            if (ToiletRandom.Chance(drank ? chance * 2 : chance))
            {
                StartToiletTrip(outpatient, rooms, preferAccessible: false, staff: false);
            }
        }

        var staffChance = dt / (StaffToiletEveryMinutes * 60);
        foreach (var member in _staff.Where(CanGoToToilet).ToList())
        {
            if (ToiletRandom.Chance(staffChance))
            {
                StartToiletTrip(member, rooms, preferAccessible: false, staff: true);
            }
        }
    }

    private bool RecentlyWent(Agent person) => _lastToiletAt.TryGetValue(person, out var at) && Now - at < ToiletGap;

    private bool Away(Agent person) => _toiletTrips.ContainsKey(person) || _trips.ContainsKey(person);

    /// <summary>A walking A&amp;E patient sitting in the waiting room (not Red) who has waited a while.</summary>
    private bool CanGoToToilet(Patient p) =>
        p.Stage is PatientStage.WaitingForTriage or PatientStage.WaitingForSpace
        && p.Triage != TriageCategory.Red
        && p.Mobility == Mobility.Walking
        && p.Seat is { } seat && p.Tile == seat && !p.IsMoving
        && p.BusyWithTaskId is null && p.CareSpaceId is null && p.PendingMove is null
        && Now - p.QueuedSince >= ToiletAfterWaiting
        && !Away(p) && !RecentlyWent(p);

    /// <summary>A checked-in clinic patient sitting down, whose appointment isn't due for a while.</summary>
    private bool CanGoToToilet(Outpatient o) =>
        o.Stage == OutpatientStage.Waiting
        && o.Seat is { } seat && o.Tile == seat && !o.IsMoving
        && FindAppointment(o.AppointmentId) is { } appointment && appointment.Time - Now >= TimeSpan.FromMinutes(15)
        && appointment.CheckedInAt is { } checkedIn && Now - checkedIn >= TimeSpan.FromMinutes(10)
        && !Away(o) && !RecentlyWent(o);

    /// <summary>Staff sitting down on a break with at least a few minutes of it left.</summary>
    private bool CanGoToToilet(StaffMember m) =>
        m.Activity == StaffActivity.OnBreak && !m.IsMoving && m.CurrentTaskId is null
        && m.BreakEndsAt is { } ends && ends - Now >= TimeSpan.FromMinutes(6)
        && !Away(m) && !RecentlyWent(m);

    /// <summary>Called, moved, gone home, back from their break or otherwise taken over: the trip is off.</summary>
    private bool ToiletInterrupted(ToiletTrip trip)
    {
        if (trip.Person.Destination is { } heading && heading != trip.Target)
        {
            return true;
        }

        return trip.Person switch
        {
            Patient p => p.Stage is not (PatientStage.WaitingForTriage or PatientStage.WaitingForSpace)
                         || p.BusyWithTaskId is not null || p.CareSpaceId is not null || p.PendingMove is not null,
            Outpatient o => o.Stage != OutpatientStage.Waiting,
            StaffMember m => m.Activity != StaffActivity.OnBreak || m.CurrentTaskId is not null,
            _ => true,
        };
    }

    /// <summary>Toilets in use or claimed by someone on their way.</summary>
    private HashSet<GridCoord> ToiletsTaken() =>
        _toiletTrips.Values.Where(t => t.Stage is ToiletStage.Going or ToiletStage.Using).Select(t => t.Target).ToHashSet();

    private bool UsableBy(CareSpace room, bool staff) =>
        room.Kind != CareSpaceKind.StaffToilet || staff;

    /// <summary>
    /// A patient who needed the toilet and couldn't get to one: they hold on (and won't ask again for a
    /// while), but sometimes don't make it and leave a wet patch on the floor for the cleaners.
    /// </summary>
    private void NoToiletFor(Patient patient)
    {
        _lastToiletAt[patient] = Now;
        if (!ToiletRandom.Chance(AccidentChanceFor(patient.Age)))
        {
            return;
        }

        _accidents.Add(new FloorAccident(patient.Tile, Now + AccidentCleanedAfter));
        patient.Log(Now, "Couldn't get to a toilet in time and had an accident.", important: true);
        var why = HasPublicToilets ? "the toilets were too busy" : "the hospital has no toilets";
        Notifications.Add(Now, NotificationLevel.Warning, $"{patient.FullName} had an accident on the floor because {why}.", patient.Id, null, patient.Triage);
    }

    /// <summary>Sends someone to the nearest free toilet, or to queue for one. False when there was nowhere to go.</summary>
    private bool StartToiletTrip(Agent person, IReadOnlyList<CareSpace> rooms, bool preferAccessible, bool staff)
    {
        // The nearest, with a pull towards the right kind: staff to their own toilet, older patients to the accessible one.
        var choices = rooms
            .Where(r => UsableBy(r, staff) && r.ToiletSpots.Count > 0)
            .Select(r => (Room: r, Score: Distance(person.Tile, r.PatientSpot)
                                           - (staff && r.Kind == CareSpaceKind.StaffToilet ? 60 : 0)
                                           - (preferAccessible && r.Kind == CareSpaceKind.AccessibleToilet ? 40 : 0)
                                           + (!preferAccessible && r.Kind == CareSpaceKind.AccessibleToilet ? 15 : 0)
                                           + ToiletRandom.Range(0, 10)))
            .OrderBy(c => c.Score)
            .Select(c => c.Room)
            .ToList();
        if (choices.Count == 0)
        {
            return false;
        }

        var taken = ToiletsTaken();
        var returnTo = person is StaffMember ? person.Tile : (GridCoord?)null;
        foreach (var room in choices)
        {
            var free = room.ToiletSpots.Where(t => !taken.Contains(t)).ToList();
            if (free.Count > 0)
            {
                var spot = free[ToiletRandom.Range(0, free.Count - 1)];
                BeginToiletTrip(new ToiletTrip { Person = person, RoomId = room.Id, Target = spot, ReturnTo = returnTo });
                return true;
            }
        }

        // Every toilet is busy: wait inside the door of the nearest block that has room to queue.
        foreach (var room in choices.Where(r => r.Kind == CareSpaceKind.Toilets))
        {
            var queued = _toiletTrips.Values.Where(t => t.RoomId == room.Id && t.Stage == ToiletStage.Queueing).Select(t => t.Target).ToHashSet();
            var spots = room.QueueTiles.Where(t => !queued.Contains(t)).ToList();
            if (spots.Count > 0)
            {
                BeginToiletTrip(new ToiletTrip { Person = person, RoomId = room.Id, Target = spots[0], Stage = ToiletStage.Queueing, QueuedAt = Now, ReturnTo = returnTo });
                return true;
            }
        }

        return false;
    }

    private void BeginToiletTrip(ToiletTrip trip)
    {
        _toiletTrips[trip.Person] = trip;
        Move(trip.Person, trip.Target);
        switch (trip.Person)
        {
            case Outpatient o:
                o.StatusText = "Gone to the toilet";
                break;
            case StaffMember m:
                m.StatusText = $"On a break · at the toilet · back at {m.BreakEndsAt:HH:mm}";
                break;
        }
    }

    private void ToiletProgress(ToiletTrip trip, CareSpace room)
    {
        var person = trip.Person;
        var arrived = !person.IsMoving && person.Tile == trip.Target;
        if (!person.IsMoving && !arrived)
        {
            // Got stuck on the way (the room was rebuilt): try again.
            Move(person, trip.Target);
            return;
        }

        switch (trip.Stage)
        {
            case ToiletStage.Going when arrived:
                trip.Stage = ToiletStage.Using;
                trip.Until = Now + TimeSpan.FromSeconds(person is StaffMember ? ToiletRandom.Range(120, 240) : ToiletRandom.Range(180, 360));
                break;

            case ToiletStage.Queueing:
                var free = room.ToiletSpots.Where(t => !ToiletsTaken().Contains(t)).ToList();
                if (free.Count > 0)
                {
                    trip.Stage = ToiletStage.Going;
                    trip.Target = free[0];
                    trip.QueuedAt = null;
                    Move(person, trip.Target);
                }
                else if (trip.QueuedAt is { } since && Now - since >= MaxToiletQueue)
                {
                    ToiletGoBack(trip);
                    if (person is Patient waited)
                    {
                        NoToiletFor(waited);
                    }
                }

                break;

            case ToiletStage.Using when Now >= trip.Until:
                _lastToiletAt[person] = Now;
                if (person is Patient p)
                {
                    p.Log(Now, $"Went to the toilet ({room.Name}).");
                }

                if (room.SinkSpots.Count > 0)
                {
                    var basins = room.SinkSpots.OrderBy(s => Distance(s, person.Tile)).ToList();
                    var busy = _toiletTrips.Values.Where(t => t.Stage == ToiletStage.Washing).Select(t => t.Target).ToHashSet();
                    trip.Stage = ToiletStage.Washing;
                    trip.Target = basins.FirstOrDefault(b => !busy.Contains(b), basins[0]);
                    trip.Until = null;
                    Move(person, trip.Target);
                }
                else
                {
                    ToiletGoBack(trip);
                }

                break;

            case ToiletStage.Washing when arrived && trip.Until is null:
                trip.Until = Now + TimeSpan.FromSeconds(ToiletRandom.Range(30, 60));
                break;

            case ToiletStage.Washing when arrived && Now >= trip.Until:
                ToiletGoBack(trip);
                break;
        }
    }

    private void ToiletGoBack(ToiletTrip trip)
    {
        _toiletTrips.Remove(trip.Person);
        switch (trip.Person)
        {
            case Patient p:
                if (p.Seat is { } seat)
                {
                    WalkTo(p, seat);
                }
                else
                {
                    SendToWaitingRoom(p);
                }

                break;
            case Outpatient o:
                o.StatusText = "Waiting";
                if (o.Seat is { } chair)
                {
                    Walk(o, chair);
                }

                break;
            case StaffMember m:
                m.StatusText = $"On a break · back at {m.BreakEndsAt:HH:mm}";
                if (trip.ReturnTo is { } back)
                {
                    WalkTo(m, back);
                }

                break;
        }
    }

    /// <summary>Waiting patients left in a toilet room without a trip (after loading a game) go back to their seat.</summary>
    private void SendStrandedBackFromToilets(IReadOnlyList<CareSpace> rooms)
    {
        bool InRoom(Agent a) => rooms.Any(r => r.Rect.Contains(a.Tile));

        foreach (var p in _patients.Values)
        {
            if (p.Stage is PatientStage.WaitingForTriage or PatientStage.WaitingForSpace
                && !p.IsMoving && p.BusyWithTaskId is null && p.PendingMove is null && p.CareSpaceId is null
                && !Away(p) && p.Seat is { } seat && p.Tile != seat && InRoom(p))
            {
                WalkTo(p, seat);
            }
        }

        foreach (var o in _outpatients)
        {
            if (o.Stage == OutpatientStage.Waiting && !o.IsMoving && !Away(o) && o.Seat is { } seat && o.Tile != seat && InRoom(o))
            {
                o.StatusText = "Waiting";
                Walk(o, seat);
            }
        }
    }

    private void Move(Agent person, GridCoord tile)
    {
        switch (person)
        {
            case Patient p:
                WalkTo(p, tile);
                break;
            case Outpatient o:
                Walk(o, tile);
                break;
            case StaffMember m:
                WalkTo(m, tile);
                break;
        }
    }

    /// <summary>With nowhere for them to go, waiting patients give up sooner.</summary>
    private double ToiletWalkOutFactor() => HasPublicToilets ? 1.0 : NoToiletsWalkOutFactor;
}
