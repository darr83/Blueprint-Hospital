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

    /// <summary>What the player starts with.</summary>
    public StartingHospital Start { get; init; } = StartingHospital.SmallEmergency;

    /// <summary>The player can keep spending when the balance is below zero (the money is still counted).</summary>
    public bool UnlimitedMoney { get; init; }
}

/// <summary>The choice of how to start a new game.</summary>
public enum StartingHospital
{
    /// <summary>A small, staffed A&amp;E: cubicles, resus, triage, offices and a starting team.</summary>
    SmallEmergency,

    /// <summary>The hospital building and roads; everything else is built and hired by the player.</summary>
    EmptyBuilding,
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
        _spaces = EmergencyDepartmentLayout.Create(building, database).ToDictionary(s => s.Id);
        Nav = EmergencyDepartmentLayout.BuildNavGrid(site, _spaces.Values);
        foreach (var space in _spaces.Values)
        {
            DecorateNewRoom(space);
        }

        _generator = new PatientGenerator(database, Random);
        _diagnosis = new DiagnosisEngine(database) { CanDo = CanDo };
        _arrivalProfile = database.Arrivals.Values.FirstOrDefault(a => a.Department == EmergencyDepartmentLayout.Department);

        var entrance = building.Entrances.First(e => !e.IsAmbulanceEntrance);
        WalkInSpawn = new GridCoord(entrance.Tile.X + 4, building.Footprint.Bottom + 16);
        var ambulanceEntrance = building.Entrances.First(e => e.IsAmbulanceEntrance);
        AmbulanceBayPatientTiles = Enumerable.Range(0, 4)
            .Select(i => new GridCoord(building.Footprint.Right + 2, ambulanceEntrance.Tile.Y - 4 + (i * 4)))
            .ToArray();
        AmbulanceParkingTiles = AmbulanceBayPatientTiles.Select(t => new GridCoord(t.X + 6, t.Y)).ToArray();
        AmbulanceRoadEntry = new GridCoord(building.Footprint.Right + 19, site.Length - 1);

        UnlimitedMoney = _options.UnlimitedMoney;
        UnlockStartingDepartments();
        if (_options.Start == StartingHospital.EmptyBuilding)
        {
            ClearToEmptyBuilding();
        }
        else
        {
            CreateStartingStaff();
        }

        RefreshCandidatesIfDue();
        _wasOpen = IsEmergencyOpen;
    }

    public GameDatabase Database { get; }

    public SimulationClock Clock { get; }

    public EventBus Events { get; }

    public SiteMap Site { get; }

    public NavGrid Nav { get; private set; }

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

    public CareSpace? FindSpace(string id) => _spaces.GetValueOrDefault(id);

    /// <summary>Rooms of one kind, in a stable order.</summary>
    public IEnumerable<CareSpace> SpacesOfKind(CareSpaceKind kind) =>
        _spaces.Values.Where(s => s.Kind == kind).OrderBy(s => s.Id, StringComparer.Ordinal);

    public Patient? FindPatient(int id) =>
        _patients.TryGetValue(id, out var p) ? p : _departed.FirstOrDefault(d => d.Id == id);

    public StaffMember? FindStaff(int id) => _staff.FirstOrDefault(s => s.Id == id);

    /// <summary>Advances the hospital by one tick. Called by the clock.</summary>
    public void Tick()
    {
        var dt = SimulationClock.TickLength.TotalSeconds;

        RefreshCandidatesIfDue();
        AnnounceOpening();
        AnnounceMaternityOpening();

        if (_options.AutomaticArrivals)
        {
            GenerateArrivals(dt);
            GenerateMaternityArrivals(dt);
        }

        DispatchDuePreAlerts();

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

        KeepPushersWithPatients();

        UpdateAmbulances();
        UpdatePhysiology(dt);
        UpdateArrests();
        UpdateEmergencyCalls();
        UpdateGeneralAnnouncements();
        UpdatePatientFlow();
        ReseatStandingPatients();
        UpdateWards();
        UpdateClinics(dt);
        UpdateRefreshments(dt);
        UpdateToilets(dt);
        UpdateRecovery();
        UpdateBabies(dt);
        SchedulePhysio();
        ScheduleOutreach();
        ProcessScheduledResults();
        CheckOutcomeReviews();
        ScheduleObservations();
        UpdateRedMonitors();
        AllocateSpaces();
        UpdateCorridorCare();
        UpdateTaskPriorities();
        UpdateStaff(dt);
        ChargeRunningCosts(dt);
        RemoveDepartedPatients();
        Stats.Update(this);
        UpdateHistory();
    }

    // ---------------------------------------------------------------- arrivals

    private void GenerateArrivals(double dt)
    {
        if (_arrivalProfile is null || !IsEmergencyOpen)
        {
            return;
        }

        var expected = _arrivalProfile.RateAt(Now) * _options.ArrivalRateMultiplier * dt / 3600.0;
        var count = Random.Poisson(expected);
        for (var i = 0; i < count; i++)
        {
            var patient = _generator.Generate(Now);
            if (NeedsPreAlert(patient))
            {
                PreAlert(patient);
            }
            else
            {
                AdmitNewPatient(patient);
            }
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

    /// <summary>Sets the patient off along a path straight away (see <c>WalkTo</c> for porters).</summary>
    private void Go(Patient patient, GridCoord destination)
    {
        var path = PathFor(patient, destination);
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

        var path = PathFor(member, destination);
        if (path.Count > 0)
        {
            member.FollowPath(path);
        }
        else
        {
            member.PlaceAt(destination);
        }
    }

    private GridCoord QueueTile(int index)
    {
        if (MainWaitingRoom is not { } waiting)
        {
            return EntranceTile;
        }

        var tiles = waiting.QueueTiles;
        return tiles.Count == 0 ? waiting.PatientSpot : tiles[Math.Min(index, tiles.Count - 1)];
    }

    /// <summary>Just inside the main entrance: where people go when there is nowhere better.</summary>
    internal GridCoord EntranceTile
    {
        get
        {
            var e = MainBuilding.Entrances.First(e => !e.IsAmbulanceEntrance);
            var (dx, dy) = WaitingRoomLayout.Step(WaitingRoomLayout.Opposite(e.Side));
            var middle = e.Side is WallSide.North or WallSide.South ? new GridCoord(e.Tile.X + (e.WidthTiles / 2), e.Tile.Y) : new GridCoord(e.Tile.X, e.Tile.Y + (e.WidthTiles / 2));
            return new GridCoord(middle.X + (2 * dx), middle.Y + (2 * dy));
        }
    }

    internal string Describe(Patient p) => $"{p.FullName} ({p.Age}{(p.Sex == Sex.Female ? "F" : "M")})";

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    internal void ReleaseSpace(Patient patient)
    {
        if (patient.CareSpaceId is { } id && FindSpace(id) is { } space && space.OccupantId == patient.Id)
        {
            space.OccupantId = null;
        }

        if (BedOf(patient) is { } bed && bed.OccupantId == patient.Id)
        {
            bed.OccupantId = null;
        }

        patient.CareSpaceId = null;
        patient.BedIndex = null;
    }

    internal void ReleaseSeat(Patient patient) => patient.Seat = null;

    /// <summary>
    /// True if someone is still lying on this bed: a patient who has been given somewhere else to go
    /// keeps their old bed until a porter wheels them away, so nobody else is put in it on top of them.
    /// </summary>
    public bool SomeoneLyingOn(GridCoord spot) =>
        _patients.Values.Any(p => p.Tile == spot && !p.IsMoving && p.Stage != PatientStage.Gone);

    /// <summary>A cubicle or bay that is not booked for anyone and has nobody still lying in it.</summary>
    public bool IsVacant(CareSpace space) => space.IsFree && !SomeoneLyingOn(space.PatientSpot);

    /// <summary>A ward bed that is not booked for anyone and has nobody still lying in it.</summary>
    public bool IsVacant(WardBed bed) => bed.OccupantId is null && !SomeoneLyingOn(bed.PatientSpot);

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
        // A small A&E team to open with, one for days and one for nights. Everyone else, and everyone
        // for new rooms, is hired by the player.
        foreach (var shift in new[] { CurrentShift, CurrentShift == StaffShift.Day ? StaffShift.Night : StaffShift.Day })
        {
            HireStaff(StaffRole.Receptionist, skill: 0.5, shift: shift);
            HireStaff(StaffRole.Nurse, skill: 0.75, shift: shift);
            HireStaff(StaffRole.Nurse, skill: 0.65, shift: shift);
            HireStaff(StaffRole.Nurse, skill: 0.55, shift: shift);
            HireStaff(StaffRole.Nurse, skill: 0.5, shift: shift);
            HireStaff(StaffRole.Doctor, skill: 0.8, shift: shift);
            HireStaff(StaffRole.Doctor, skill: 0.55, shift: shift);
            HireStaff(StaffRole.Porter, skill: 0.4, shift: shift);
            HireStaff(StaffRole.Porter, skill: 0.4, shift: shift);
            HireStaff(StaffRole.HealthcareAssistant, skill: 0.5, shift: shift);
        }

        foreach (var member in _staff)
        {
            member.PlaceAt(HomeTile(member));
        }
    }

    internal GridCoord HomeTile(StaffMember member)
    {
        if (member.BaseRoomId is { } roomId && FindSpace(roomId) is { } room)
        {
            return room.IsWard ? WardHome(member, room) : ServiceRoomHome(room);
        }

        switch (member.Role)
        {
            case StaffRole.Receptionist:
                return MainWaitingRoom?.StaffSpot ?? SpacesOfKind(CareSpaceKind.Waiting).FirstOrDefault()?.StaffSpot ?? WaitingPlace(_staff.IndexOf(member));
            case StaffRole.Doctor:
                return BaseSeat(member, CareSpaceKind.DoctorsOffice);
            case StaffRole.Porter:
            {
                // Free porters wait in the staff room (from the far seats, leaving the near ones for
                // staff on a break); without one they wait in the main corridor.
                var index = Math.Max(0, _staff.Where(s => s.Role == StaffRole.Porter).ToList().IndexOf(member));
                var seats = SpacesOfKind(CareSpaceKind.StaffRoom).SelectMany(s => s.Seats).ToList();
                return seats.Count > 0 ? seats[seats.Count - 1 - (index % seats.Count)] : WaitingPlace(5 + index);
            }
            default:
                return BaseSeat(member, CareSpaceKind.NursesStation);
        }
    }

    /// <summary>A seat in the staff member's base room; without one they wait in a corridor.</summary>
    private GridCoord BaseSeat(StaffMember member, CareSpaceKind kind)
    {
        var seats = SpacesOfKind(kind).SelectMany(s => s.Seats).ToList();
        var index = Math.Max(0, _staff.Where(s => s.Role == member.Role && s.BaseRoomId is null).ToList().IndexOf(member));
        if (seats.Count > 0)
        {
            return seats[index % seats.Count];
        }

        return WaitingPlace(index);
    }

    /// <summary>Somewhere in a corridor to stand, spread out by <paramref name="index"/>.</summary>
    internal GridCoord WaitingPlace(int index)
    {
        var corridor = SpacesOfKind(CareSpaceKind.Corridor).OrderByDescending(c => c.Rect.Area).FirstOrDefault() ?? SpacesOfKind(CareSpaceKind.Waiting).FirstOrDefault();
        if (corridor is null)
        {
            return EntranceTile;
        }

        var r = corridor.Rect;
        return new GridCoord(r.X + 1 + ((index * 3) % Math.Max(1, r.Width - 2)), r.Y + 1);
    }
}
