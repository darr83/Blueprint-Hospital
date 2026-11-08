using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.Transport;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

public sealed record PatientArrived(Patient Patient) : IGameEvent;

public sealed record PatientTriaged(Patient Patient, TriageCategory Category, bool IsUpgrade) : IGameEvent;

public sealed record PatientLeft(Patient Patient, PatientOutcome Outcome) : IGameEvent;

/// <summary>Options for a new simulation.</summary>
public sealed class SimulationOptions
{
    public int Seed { get; init; } = Environment.TickCount;

    /// <summary>Scales the arrival profile; 0 turns automatic arrivals off (tests use this).</summary>
    public double ArrivalRateMultiplier { get; init; } = 1.0;

    public bool AutomaticArrivals { get; init; } = true;
}

/// <summary>
/// The running hospital. Owns patients, staff, rooms and the task board and advances them
/// one simulation tick at a time. Contains no engine code, so whole patient journeys can be
/// simulated and tested without Godot.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>Patients who have left are kept for review, up to this many.</summary>
    private const int MaxDischargedRecords = 300;

    private readonly Dictionary<int, Patient> _patients = new();
    private readonly List<Patient> _departed = new();
    private readonly List<StaffMember> _staff = new();
    private readonly Dictionary<int, ClinicalTask> _tasks = new();
    private readonly List<AmbulanceVehicle> _ambulances = new();
    private readonly List<int> _receptionQueue = new();
    private readonly Dictionary<string, CareSpace> _spaces;
    private readonly IReadOnlyList<GridCoord> _receptionQueueTiles;
    private readonly PatientGenerator _generator;
    private readonly DiagnosisEngine _diagnosis;
    private readonly ArrivalProfile? _arrivalProfile;
    private readonly SimulationOptions _options;
    private int _nextTaskId = 1;
    private int _nextStaffId = 1;
    private int _nextAmbulanceId = 1;
    private readonly HashSet<string> _activeCapacityAlerts = new();

    public HospitalSimulation(GameDatabase database, SimulationClock clock, EventBus events, SiteMap site, SimulationOptions? options = null)
    {
        Database = database;
        Clock = clock;
        Events = events;
        Site = site;
        _options = options ?? new SimulationOptions();
        Random = new GameRandom(_options.Seed);
        Notifications = new NotificationLog(events);

        var building = site.Buildings[0];
        _spaces = EmergencyDepartmentLayout.Create(building).ToDictionary(s => s.Id);
        _receptionQueueTiles = EmergencyDepartmentLayout.ReceptionQueue(building);
        Nav = EmergencyDepartmentLayout.BuildNavGrid(site, _spaces.Values);
        _generator = new PatientGenerator(database, Random);
        _diagnosis = new DiagnosisEngine(database);
        _arrivalProfile = database.Arrivals.Values.FirstOrDefault(a => a.Department == EmergencyDepartmentLayout.Department);

        var entrance = building.Entrances.First(e => !e.IsAmbulanceEntrance);
        WalkInSpawn = new GridCoord(entrance.Tile.X + 4, building.Footprint.Bottom + 16);
        var ambulanceEntrance = building.Entrances.First(e => e.IsAmbulanceEntrance);
        AmbulanceBayPatientTiles = Enumerable.Range(0, 4)
            .Select(i => new GridCoord(building.Footprint.Right + 2, ambulanceEntrance.Tile.Y - 4 + (i * 4)))
            .ToArray();
        AmbulanceParkingTiles = AmbulanceBayPatientTiles.Select(t => new GridCoord(t.X + 6, t.Y)).ToArray();
        AmbulanceRoadEntry = new GridCoord(building.Footprint.Right + 19, site.Length - 1);

        CreateStartingStaff();
    }

    public GameDatabase Database { get; }

    public SimulationClock Clock { get; }

    public EventBus Events { get; }

    public SiteMap Site { get; }

    public NavGrid Nav { get; }

    public GameRandom Random { get; }

    public NotificationLog Notifications { get; }

    public EmergencyStats Stats { get; } = new();

    public DateTime Now => Clock.Now;

    public IEnumerable<Patient> Patients => _patients.Values;

    /// <summary>Patients who have left (most recent first), kept so the player can review their journeys.</summary>
    public IReadOnlyList<Patient> DepartedPatients => _departed;

    public IReadOnlyList<StaffMember> Staff => _staff;

    public IEnumerable<ClinicalTask> Tasks => _tasks.Values;

    public IReadOnlyList<AmbulanceVehicle> Ambulances => _ambulances;

    public IEnumerable<CareSpace> Spaces => _spaces.Values;

    public IReadOnlyList<int> ReceptionQueue => _receptionQueue;

    public GridCoord WalkInSpawn { get; }

    public IReadOnlyList<GridCoord> AmbulanceBayPatientTiles { get; }

    public IReadOnlyList<GridCoord> AmbulanceParkingTiles { get; }

    public GridCoord AmbulanceRoadEntry { get; }

    public CareSpace Space(string id) => _spaces[id];

    public Patient? FindPatient(int id) =>
        _patients.TryGetValue(id, out var p) ? p : _departed.FirstOrDefault(d => d.Id == id);

    public StaffMember? FindStaff(int id) => _staff.FirstOrDefault(s => s.Id == id);

    public CareSpace Waiting => _spaces["waiting_room"];

    public CareSpace TriageRoom => _spaces["triage_1"];

    /// <summary>Advances the hospital by one tick. Called by the clock.</summary>
    public void Tick()
    {
        var dt = SimulationClock.TickLength.TotalSeconds;

        if (_options.AutomaticArrivals)
        {
            GenerateArrivals(dt);
        }

        foreach (var ambulance in _ambulances)
        {
            ambulance.Move(dt);
        }

        foreach (var patient in _patients.Values)
        {
            patient.Move(dt);
        }

        foreach (var member in _staff)
        {
            member.Move(dt);
        }

        UpdateAmbulances();
        UpdatePhysiology(dt);
        UpdatePatientFlow();
        ProcessScheduledResults();
        CheckOutcomeReviews();
        ScheduleObservations();
        AllocateSpaces();
        UpdateTaskPriorities();
        UpdateStaff(dt);
        RemoveDepartedPatients();
        Stats.Update(this);
    }

    // ---------------------------------------------------------------- arrivals

    private void GenerateArrivals(double dt)
    {
        if (_arrivalProfile is null)
        {
            return;
        }

        var expected = _arrivalProfile.RateAt(Now) * _options.ArrivalRateMultiplier * dt / 3600.0;
        var count = Random.Poisson(expected);
        for (var i = 0; i < count; i++)
        {
            AdmitNewPatient(_generator.Generate(Now));
        }
    }

    /// <summary>Brings a specific patient into the hospital. Used by arrivals and by scenarios/tests.</summary>
    public Patient SpawnPatient(ArrivalMethod method, string? conditionId = null)
    {
        var patient = _generator.Generate(Now, method, conditionId);
        AdmitNewPatient(patient);
        return patient;
    }

    private void AdmitNewPatient(Patient patient)
    {
        _patients[patient.Id] = patient;
        Stats.RecordArrival(patient);

        if (patient.ArrivalMethod == ArrivalMethod.WalkIn)
        {
            patient.PlaceAt(WalkInSpawn);
            patient.Stage = PatientStage.WalkingIn;
            _receptionQueue.Add(patient.Id);
            patient.Log(Now, $"Walked in to A&E: \"{Database.Symptoms[patient.PresentingComplaint].PatientDescription}\"");
            WalkTo(patient, QueueTile(_receptionQueue.Count - 1));
        }
        else
        {
            patient.Stage = PatientStage.InAmbulanceQueue;
            patient.AmbulanceHandover = BuildHandoverNote(patient);
            var ambulance = DispatchAmbulance(patient, AmbulancePurpose.Arrival);
            patient.Log(Now, $"Ambulance {ambulance.CallSign} en route to A&E.");
            Notifications.Add(Now, NotificationLevel.Info, $"Ambulance {ambulance.CallSign} arriving: {Describe(patient)} with {Database.Symptoms[patient.PresentingComplaint].Name.ToLowerInvariant()}.", patient.Id);
        }

        Events.Publish(new PatientArrived(patient));
    }

    private string BuildHandoverNote(Patient patient)
    {
        var obs = patient.CurrentObservations();
        var symptoms = string.Join(", ", patient.Symptoms.Select(s => Database.Symptoms[s].Name.ToLowerInvariant()));
        var onset = Now.AddMinutes(-Random.Range(30, 180));
        var history = patient.PastHistory.Count == 0 ? "no significant past history" : "PMH " + string.Join(", ", patient.PastHistory).ToLowerInvariant();
        return $"{patient.Age}{(patient.Sex == Sex.Female ? "F" : "M")}. {Capitalise(symptoms)} since {onset:HH:mm}. "
               + $"Crew obs: HR {obs.HeartRate}, BP {obs.BloodPressure}, RR {obs.RespiratoryRate}, SpO2 {obs.OxygenSaturation}%. {Capitalise(history)}.";
    }

    // ---------------------------------------------------------------- helpers

    internal void WalkTo(Patient patient, GridCoord destination)
    {
        var path = Nav.FindPath(patient.Tile, destination);
        if (path.Count > 0)
        {
            patient.FollowPath(path);
        }
        else
        {
            patient.PlaceAt(destination);
        }
    }

    internal void WalkTo(StaffMember member, GridCoord destination)
    {
        if (member.Tile == destination && !member.IsMoving)
        {
            return;
        }

        if (member.Destination == destination)
        {
            return;
        }

        var path = Nav.FindPath(member.Tile, destination);
        if (path.Count > 0)
        {
            member.FollowPath(path);
        }
        else
        {
            member.PlaceAt(destination);
        }
    }

    private GridCoord QueueTile(int index) => _receptionQueueTiles[Math.Min(index, _receptionQueueTiles.Count - 1)];

    internal string Describe(Patient p) => $"{p.FullName} ({p.Age}{(p.Sex == Sex.Female ? "F" : "M")})";

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private int? FreeSeat()
    {
        var taken = _patients.Values.Where(p => p.SeatIndex.HasValue).Select(p => p.SeatIndex!.Value).ToHashSet();
        for (var i = 0; i < Waiting.Seats.Count; i++)
        {
            if (!taken.Contains(i))
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>Sends a patient to a waiting-room seat (or to stand in the room if all seats are taken).</summary>
    internal void SendToWaitingRoom(Patient patient)
    {
        ReleaseSpace(patient);
        var seat = FreeSeat();
        patient.SeatIndex = seat;
        var destination = seat is { } s ? Waiting.Seats[s] : new GridCoord(Waiting.Rect.X + 10 + Random.Range(-3, 3), Waiting.Rect.Y + 3);
        WalkTo(patient, destination);
    }

    internal void ReleaseSpace(Patient patient)
    {
        if (patient.CareSpaceId is { } id && _spaces[id].OccupantId == patient.Id)
        {
            _spaces[id].OccupantId = null;
        }

        patient.CareSpaceId = null;
    }

    internal void ReleaseSeat(Patient patient) => patient.SeatIndex = null;

    private void RemoveDepartedPatients()
    {
        foreach (var patient in _patients.Values.Where(p => p.Stage == PatientStage.Gone).ToList())
        {
            _patients.Remove(patient.Id);
            _departed.Insert(0, patient);
        }

        if (_departed.Count > MaxDischargedRecords)
        {
            _departed.RemoveRange(MaxDischargedRecords, _departed.Count - MaxDischargedRecords);
        }
    }

    // ---------------------------------------------------------------- staff roster

    private void CreateStartingStaff()
    {
        var names = Database.Names.Values.First();
        StaffMember Make(StaffRole role, string grade, double skill, double speed, int years, int salary)
        {
            var female = Random.Chance(0.6);
            var first = Random.Pick(female ? names.FemaleFirstNames : names.MaleFirstNames);
            var surname = Random.Pick(names.Surnames);
            var title = role == StaffRole.Doctor ? "Dr " : string.Empty;
            return new StaffMember
            {
                Id = _nextStaffId++,
                Name = $"{title}{first} {surname}",
                Role = role,
                Grade = grade,
                DepartmentId = EmergencyDepartmentLayout.Department,
                Skill = skill,
                Speed = speed,
                YearsExperience = years,
                Age = 23 + years + Random.Range(0, 6),
                Salary = salary,
                Fatigue = Random.Range(0.0, 0.15),
            };
        }

        _staff.Add(Make(StaffRole.Receptionist, "Receptionist", 0.5, 1.0, 4, 23_000));
        _staff.Add(Make(StaffRole.Nurse, "Senior emergency nurse", 0.85, 1.1, 12, 47_000));
        _staff.Add(Make(StaffRole.Nurse, "Emergency nurse", 0.7, 1.0, 6, 37_000));
        _staff.Add(Make(StaffRole.Nurse, "Registered nurse", 0.6, 1.0, 3, 31_000));
        _staff.Add(Make(StaffRole.Nurse, "Registered nurse", 0.55, 0.95, 1, 29_000));
        _staff.Add(Make(StaffRole.Doctor, "Emergency medicine registrar", 0.8, 1.05, 7, 63_000));
        _staff.Add(Make(StaffRole.Doctor, "Junior doctor (FY2)", 0.55, 0.95, 2, 40_000));

        foreach (var member in _staff)
        {
            member.PlaceAt(HomeTile(member));
        }
    }

    internal GridCoord HomeTile(StaffMember member)
    {
        switch (member.Role)
        {
            case StaffRole.Receptionist:
                return Waiting.StaffSpot;
            case StaffRole.Doctor:
            {
                var office = _spaces["doctors_office"];
                var index = _staff.Where(s => s.Role == StaffRole.Doctor).ToList().IndexOf(member);
                return office.Seats[Math.Max(0, index) % office.Seats.Count];
            }

            default:
            {
                var station = _spaces["nurses_station"];
                var index = _staff.Where(s => s.Role == StaffRole.Nurse).ToList().IndexOf(member);
                return station.Seats[Math.Max(0, index) % station.Seats.Count];
            }
        }
    }
}
