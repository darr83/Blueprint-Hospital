using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>Where a patient is on a trip for something to eat or drink.</summary>
public enum RefreshmentStage
{
    /// <summary>Walking to the counter or a vending machine.</summary>
    GoingToBuy,

    /// <summary>At the counter or machine, paying.</summary>
    Buying,

    /// <summary>Sitting at a cafe table with their food and drink.</summary>
    Eating,
}

/// <summary>A waiting patient's trip to a cafeteria or vending point.</summary>
public sealed class RefreshmentTrip
{
    public required Agent Person { get; init; }

    public required string RoomId { get; init; }

    /// <summary>The counter queue tile or vending machine they buy from.</summary>
    public required GridCoord BuyAt { get; init; }

    public RefreshmentStage Stage { get; set; } = RefreshmentStage.GoingToBuy;

    /// <summary>The cafe chair they sit on to eat, if any.</summary>
    public GridCoord? Table { get; set; }

    /// <summary>When the current stage (paying, eating) finishes.</summary>
    public DateTime? Until { get; set; }
}

/// <summary>
/// Cafeterias and vending points. Patients who have been waiting a while, and are well enough to walk
/// about, now and then get up and fetch something to eat or drink from the nearest one, then go back to
/// their seat. Each purchase pays the hospital a share of the takings, and a patient who has had
/// something is less likely to give up and leave before being seen. A patient who is called while they
/// are away simply goes straight to whoever called them.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>How long a patient waits before they think about getting something.</summary>
    public static readonly TimeSpan RefreshmentAfterWaiting = TimeSpan.FromMinutes(20);

    /// <summary>Once they have had something, how long before they want anything else (and how long it keeps them patient).</summary>
    public static readonly TimeSpan RefreshmentLasts = TimeSpan.FromHours(3);

    /// <summary>On average, an eligible waiting patient goes for something about once in this many minutes.</summary>
    private const double RefreshmentEveryMinutes = 60;

    /// <summary>Outpatients only go if their appointment is at least this far off.</summary>
    private static readonly TimeSpan OutpatientRefreshmentMargin = TimeSpan.FromMinutes(25);

    /// <summary>How much further (in tiles) people will walk to a cafe than to a vending machine.</summary>
    private const int CafeDrawTiles = 40;

    private readonly Dictionary<Agent, RefreshmentTrip> _trips = new();
    private GameRandom? _refreshmentRandom;

    /// <summary>A separate random stream, so building a cafe doesn't change anything else that happens.</summary>
    private GameRandom RefreshmentRandom => _refreshmentRandom ??= new GameRandom(_options.Seed + 104_729);

    /// <summary>Cafeterias and vending points.</summary>
    public IEnumerable<CareSpace> RefreshmentRooms => _spaces.Values.Where(s => s.IsRefreshments);

    /// <summary>Patients (and outpatients) out getting something to eat or drink right now.</summary>
    public IReadOnlyCollection<RefreshmentTrip> RefreshmentTrips => _trips.Values;

    /// <summary>The trip a person is on, if they are away getting refreshments.</summary>
    public RefreshmentTrip? TripOf(Agent person) => _trips.GetValueOrDefault(person);

    private void UpdateRefreshments(double dt)
    {
        var rooms = RefreshmentRooms.ToList();
        foreach (var trip in _trips.Values.ToList())
        {
            if (Interrupted(trip))
            {
                _trips.Remove(trip.Person);
                continue;
            }

            if (!rooms.Any(r => r.Id == trip.RoomId))
            {
                // The cafe was knocked down while they were in it.
                GoBack(trip);
                continue;
            }

            Progress(trip, rooms.First(r => r.Id == trip.RoomId));
        }

        if (rooms.Count == 0)
        {
            return;
        }

        SendStrandedBack(rooms);

        var chance = dt / (RefreshmentEveryMinutes * 60);
        foreach (var patient in _patients.Values.Where(CanFetchRefreshments).ToList())
        {
            if (RefreshmentRandom.Chance(chance))
            {
                StartTrip(patient, rooms);
            }
        }

        foreach (var outpatient in _outpatients.Where(CanFetchRefreshments).ToList())
        {
            if (RefreshmentRandom.Chance(chance))
            {
                StartTrip(outpatient, rooms);
            }
        }
    }

    /// <summary>A walking A&amp;E patient sitting in the waiting room (not Red) who has waited a while and hasn't had anything lately.</summary>
    private bool CanFetchRefreshments(Patient p) =>
        p.Stage is PatientStage.WaitingForTriage or PatientStage.WaitingForSpace
        && p.Triage != TriageCategory.Red
        && p.Mobility == Mobility.Walking
        && p.Seat is { } seat && p.Tile == seat && !p.IsMoving
        && p.BusyWithTaskId is null && p.CareSpaceId is null && p.PendingMove is null
        && Now - p.QueuedSince >= RefreshmentAfterWaiting
        && !_trips.ContainsKey(p)
        && (p.LastRefreshmentAt is null || Now - p.LastRefreshmentAt >= RefreshmentLasts);

    /// <summary>A checked-in clinic patient sitting down, whose appointment isn't due for a while.</summary>
    private bool CanFetchRefreshments(Outpatient o) =>
        o.Stage == OutpatientStage.Waiting
        && o.Seat is { } seat && o.Tile == seat && !o.IsMoving
        && FindAppointment(o.AppointmentId) is { } appointment && appointment.Time - Now >= OutpatientRefreshmentMargin
        && appointment.CheckedInAt is { } checkedIn && Now - checkedIn >= RefreshmentAfterWaiting / 2
        && !_trips.ContainsKey(o)
        && (o.LastRefreshmentAt is null || Now - o.LastRefreshmentAt >= RefreshmentLasts);

    /// <summary>Called, moved, gone home or otherwise taken over by something else: the trip is off.</summary>
    private bool Interrupted(RefreshmentTrip trip)
    {
        var expected = trip.Stage switch
        {
            RefreshmentStage.Eating => trip.Table ?? trip.BuyAt,
            _ => trip.BuyAt,
        };

        // Someone else sent them somewhere.
        if (trip.Person.Destination is { } heading && heading != expected)
        {
            return true;
        }

        return trip.Person switch
        {
            Patient p => p.Stage is not (PatientStage.WaitingForTriage or PatientStage.WaitingForSpace)
                         || p.BusyWithTaskId is not null || p.CareSpaceId is not null || p.PendingMove is not null,
            Outpatient o => o.Stage != OutpatientStage.Waiting,
            _ => true,
        };
    }

    private void StartTrip(Agent person, IReadOnlyList<CareSpace> rooms)
    {
        // Most go to the nearest, but a cafe draws people from further away than a vending machine does.
        var choices = rooms
            .Select(r => (Room: r, Score: Distance(person.Tile, r.PatientSpot) - (r.Kind == CareSpaceKind.Cafeteria ? CafeDrawTiles : 0) + RefreshmentRandom.Range(0, 30)))
            .OrderBy(c => c.Score)
            .Select(c => c.Room)
            .ToList();
        foreach (var room in choices)
        {
            var buyFrom = room.Kind == CareSpaceKind.Cafeteria ? room.QueueTiles.Concat(room.VendingSpots) : room.VendingSpots;
            var taken = _trips.Values.Where(t => t.RoomId == room.Id && t.Stage != RefreshmentStage.Eating).Select(t => t.BuyAt).ToHashSet();
            var free = buyFrom.Where(t => !taken.Contains(t)).ToList();
            if (free.Count == 0)
            {
                continue;
            }

            var spot = free[RefreshmentRandom.Range(0, free.Count - 1)];
            _trips[person] = new RefreshmentTrip { Person = person, RoomId = room.Id, BuyAt = spot };
            Send(person, spot);
            Describe(person, room.Kind == CareSpaceKind.Cafeteria ? $"Going to {room.Name}" : "Getting a snack");
            return;
        }
    }

    private void Progress(RefreshmentTrip trip, CareSpace room)
    {
        var person = trip.Person;
        switch (trip.Stage)
        {
            case RefreshmentStage.GoingToBuy when !person.IsMoving && person.Tile == trip.BuyAt:
                trip.Stage = RefreshmentStage.Buying;
                trip.Until = Now + TimeSpan.FromSeconds(RefreshmentRandom.Range(60, 150));
                Describe(person, "Buying something to eat and drink");
                break;

            case RefreshmentStage.GoingToBuy when !person.IsMoving:
                // Got stuck on the way (the room was rebuilt): try again.
                Send(person, trip.BuyAt);
                break;

            case RefreshmentStage.Buying when Now >= trip.Until:
                Pay(person, room, trip.BuyAt);
                var table = room.Kind == CareSpaceKind.Cafeteria ? FreeCafeSeat(room, person) : null;
                if (table is { } chair)
                {
                    trip.Stage = RefreshmentStage.Eating;
                    trip.Table = chair;
                    trip.Until = Now + TimeSpan.FromMinutes(RefreshmentRandom.Range(5, 15));
                    Send(person, chair);
                    Describe(person, $"Eating in {room.Name}");
                }
                else
                {
                    GoBack(trip);
                }

                break;

            case RefreshmentStage.Eating when Now >= trip.Until:
                GoBack(trip);
                break;
        }
    }

    private GridCoord? FreeCafeSeat(CareSpace room, Agent person)
    {
        var taken = _trips.Values.Where(t => t.Table is not null).Select(t => t.Table!.Value).ToHashSet();
        var free = room.Seats.Where(s => !taken.Contains(s)).OrderBy(s => Distance(s, person.Tile)).ToList();
        return free.Count == 0 ? null : free[Math.Min(free.Count - 1, RefreshmentRandom.Range(0, 2))];
    }

    /// <summary>The patient pays; the hospital gets its share of the takings.</summary>
    private void Pay(Agent person, CareSpace room, GridCoord at)
    {
        var fromMachine = room.VendingSpots.Contains(at);
        var (amount, what) = fromMachine
            ? (RefreshmentRandom.Range(2, 3), RefreshmentRandom.Chance(0.5) ? "a drink and a chocolate bar from a vending machine" : "a bottle of water and some crisps from a vending machine")
            : (RefreshmentRandom.Range(4, 6), RefreshmentRandom.Chance(0.5) ? "a tea and a sandwich" : "a coffee and a toastie");
        Book(MoneyCategory.Catering, amount);
        switch (person)
        {
            case Patient p:
                p.LastRefreshmentAt = Now;
                p.Log(Now, $"Got {what} in {room.Name}.");
                break;
            case Outpatient o:
                o.LastRefreshmentAt = Now;
                break;
        }
    }

    private void GoBack(RefreshmentTrip trip)
    {
        _trips.Remove(trip.Person);
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
        }
    }

    /// <summary>
    /// Anyone still waiting who has been left in a cafe without a trip (after being seen to there, or
    /// after loading a game) goes back to their seat.
    /// </summary>
    private void SendStrandedBack(IReadOnlyList<CareSpace> rooms)
    {
        bool InRoom(Agent a) => rooms.Any(r => r.Rect.Contains(a.Tile));

        foreach (var p in _patients.Values)
        {
            if (p.Stage is PatientStage.WaitingForTriage or PatientStage.WaitingForSpace
                && !p.IsMoving && p.BusyWithTaskId is null && p.PendingMove is null && p.CareSpaceId is null
                && !_trips.ContainsKey(p) && p.Seat is { } seat && p.Tile != seat && InRoom(p))
            {
                WalkTo(p, seat);
            }
        }

        foreach (var o in _outpatients)
        {
            if (o.Stage == OutpatientStage.Waiting && !o.IsMoving && !_trips.ContainsKey(o) && o.Seat is { } seat && o.Tile != seat && InRoom(o))
            {
                o.StatusText = "Waiting";
                Walk(o, seat);
            }
        }
    }

    private void Send(Agent person, GridCoord tile)
    {
        switch (person)
        {
            case Patient p:
                WalkTo(p, tile);
                break;
            case Outpatient o:
                Walk(o, tile);
                break;
        }
    }

    private static void Describe(Agent person, string text)
    {
        if (person is Outpatient o)
        {
            o.StatusText = text;
        }
    }

    private static int Distance(GridCoord a, GridCoord b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    /// <summary>A patient who has had something to eat or drink recently is half as likely to walk out.</summary>
    private double WalkOutFactor(Patient patient) =>
        patient.LastRefreshmentAt is { } at && Now - at < RefreshmentLasts ? 0.5 : 1.0;
}
