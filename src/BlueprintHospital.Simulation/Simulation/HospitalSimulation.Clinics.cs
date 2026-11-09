using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>A clinic consultation in progress: who is being seen, by whom, and whether it has started.</summary>
public sealed record ClinicConsult(string ClinicId, int OutpatientId, int StaffId);

/// <summary>
/// Outpatient clinics. Each clinic room keeps a calendar of booked appointments, filled two days
/// ahead for weekday sessions (09:00 to 12:00 and 13:30 to 16:30). Booked patients walk in a little
/// before their time, queue at the Outpatients reception desk and are checked in by the receptionist,
/// then sit on a free chair (in Outpatients reception, or on chairs placed in the corridors) until the
/// clinic's doctor calls them in. After their appointment they go home and the hospital is paid.
/// A clinic runs only with a doctor posted to it and a staffed Outpatients reception; otherwise its
/// patients are cancelled.
/// </summary>
public sealed partial class HospitalSimulation
{
    public static readonly (TimeSpan Start, TimeSpan End)[] ClinicSessions =
    {
        (TimeSpan.FromHours(9), TimeSpan.FromHours(12)),
        (TimeSpan.FromHours(13.5), TimeSpan.FromHours(16.5)),
    };

    /// <summary>Share of slots that get booked.</summary>
    private const double SlotBookedChance = 0.85;

    /// <summary>How early a doctor will call a patient in before their appointment time.</summary>
    private const int ClinicCallEarlyMinutes = 3;

    /// <summary>Share of booked patients who don't turn up.</summary>
    private const double DidNotAttendChance = 0.08;

    private readonly List<Appointment> _appointments = new();
    private readonly List<Outpatient> _outpatients = new();
    private readonly HashSet<string> _bookedClinicDays = new();
    private readonly Dictionary<string, ClinicConsult> _consults = new();
    private readonly HashSet<string> _cancelledToday = new();
    private int _nextAppointmentId = 1;
    private int _nextOutpatientId = 1;

    public IReadOnlyList<Appointment> Appointments => _appointments;

    public IReadOnlyList<Outpatient> Outpatients => _outpatients;

    public IReadOnlyCollection<ClinicConsult> Consults => _consults.Values;

    public IEnumerable<CareSpace> ClinicRooms => SpacesOfKind(CareSpaceKind.Clinic);

    public Appointment? FindAppointment(int id) => _appointments.FirstOrDefault(a => a.Id == id);

    public Outpatient? FindOutpatient(int id) => _outpatients.FirstOrDefault(o => o.Id == id);

    public ClinicDefinition? ClinicOf(CareSpace clinic) => Database.Clinics.GetValueOrDefault(clinic.TemplateId);

    /// <summary>Who sees a clinic's patients: a doctor, or the clinic's own kind of clinician (a midwife, a sonographer).</summary>
    public StaffRole ClinicianFor(CareSpace clinic) => ClinicOf(clinic)?.Clinician ?? StaffRole.Doctor;

    /// <summary>The clinic rooms whose patients are seen by someone in this role.</summary>
    public IEnumerable<CareSpace> ClinicsRunBy(StaffRole role) =>
        ClinicRooms.Where(c => ClinicianFor(c) == role).OrderBy(c => c.Id, StringComparer.Ordinal);

    /// <summary>What a clinic still needs before it can run (empty when it can).</summary>
    public IReadOnlyList<string> ClinicProblems(CareSpace clinic)
    {
        var problems = new List<string>();
        var clinician = ClinicianFor(clinic);
        if (!_staff.Any(s => s.BaseRoomId == clinic.Id && s.Role == clinician))
        {
            problems.Add($"a {StaffGrades.RoleName(clinician)} posted to it");
        }

        var receptions = SpacesOfKind(CareSpaceKind.OutpatientReception).ToList();
        if (receptions.Count == 0)
        {
            problems.Add("an Outpatients reception");
        }
        else if (!receptions.Any(IsStaffed))
        {
            problems.Add("a receptionist at Outpatients reception");
        }

        return problems;
    }

    public bool IsClinicRunning(CareSpace clinic) => ClinicProblems(clinic).Count == 0;

    private static bool IsClinicDay(DateTime date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>Whether this member of staff works for Outpatients (a clinic doctor or the Outpatients receptionist).</summary>
    private bool IsClinicStaff(StaffMember member) =>
        member.BaseRoomId is { } id && FindSpace(id) is { Kind: CareSpaceKind.Clinic or CareSpaceKind.OutpatientReception };

    private void UpdateClinics(double dt)
    {
        foreach (var outpatient in _outpatients)
        {
            outpatient.Move(dt);
        }

        if (Now.Hour == 0 && Now.Minute == 0 && Now.Second == 0)
        {
            _cancelledToday.Clear();
        }

        BookClinics();
        StartArrivals();
        SendHomeFromStoppedClinics();
        RunReceptions();
        RunClinics();
        SeatWaitingPatients();
        _outpatients.RemoveAll(o => o.Stage == OutpatientStage.Leaving && !o.IsMoving);
    }

    // ---------------------------------------------------------------- the calendar

    /// <summary>Fills each running clinic's calendar for today and tomorrow, once per day.</summary>
    private void BookClinics()
    {
        foreach (var clinic in ClinicRooms)
        {
            if (ClinicOf(clinic) is not { } definition || !IsClinicRunning(clinic))
            {
                continue;
            }

            foreach (var date in new[] { Now.Date, Now.Date.AddDays(1) })
            {
                var key = $"{clinic.Id}|{date:yyyy-MM-dd}";
                if (!IsClinicDay(date) || !_bookedClinicDays.Add(key))
                {
                    continue;
                }

                // A clinic opened part way through the day only books slots far enough ahead to reach.
                var earliest = date == Now.Date ? Now.AddMinutes(45) : date;
                Book(clinic, definition, date, earliest);
            }
        }

        // Forget old days so the set stays small.
        _bookedClinicDays.RemoveWhere(k => DateTime.TryParse(k[(k.IndexOf('|') + 1)..], out var d) && d < Now.Date.AddDays(-1));
    }

    private void Book(CareSpace clinic, ClinicDefinition definition, DateTime date, DateTime earliest)
    {
        var names = Database.Names.Values.First();
        foreach (var (start, end) in ClinicSessions)
        {
            for (var t = date + start; t + TimeSpan.FromMinutes(definition.AppointmentMinutes) <= date + end; t = t.AddMinutes(definition.AppointmentMinutes))
            {
                if (t < earliest || !Random.Chance(SlotBookedChance))
                {
                    continue;
                }

                var sex = definition.FemaleOnly || Random.Chance(0.5) ? Sex.Female : Sex.Male;
                var late = Random.Chance(0.1);
                _appointments.Add(new Appointment
                {
                    Id = _nextAppointmentId++,
                    ClinicId = clinic.Id,
                    Time = t,
                    Minutes = definition.AppointmentMinutes,
                    FirstName = Random.Pick(sex == Sex.Female ? names.FemaleFirstNames : names.MaleFirstNames),
                    Surname = Random.Pick(names.Surnames),
                    Age = definition.AgeRange is { } ages ? Random.Range(ages[0], ages[1]) : definition.FemaleOnly ? Random.Range(18, 80) : Random.Range(18, 92),
                    Sex = sex,
                    Reason = Random.Pick(definition.Reasons),
                    WillAttend = !Random.Chance(DidNotAttendChance),
                    ArrivesAt = late ? t.AddMinutes(Random.Range(3, 20)) : t.AddMinutes(-Random.Range(5, 25)),
                });
            }
        }
    }

    /// <summary>Today's (or another day's) appointments for one clinic, in time order.</summary>
    public IEnumerable<Appointment> CalendarFor(string clinicId, DateTime date) =>
        _appointments.Where(a => a.ClinicId == clinicId && a.Time.Date == date.Date).OrderBy(a => a.Time);

    // ---------------------------------------------------------------- arriving

    private void StartArrivals()
    {
        foreach (var appointment in _appointments.Where(a => a.Status == AppointmentStatus.Booked && Now >= a.ArrivesAt))
        {
            var clinic = FindSpace(appointment.ClinicId);
            if (clinic is null || !IsClinicRunning(clinic))
            {
                CancelAppointment(appointment, clinic);
                continue;
            }

            if (!appointment.WillAttend)
            {
                if (Now >= appointment.Time.AddMinutes(20))
                {
                    appointment.Status = AppointmentStatus.DidNotAttend;
                    Tally(StatKeys.ClinicDidNotAttend);
                }

                continue;
            }

            var outpatient = new Outpatient
            {
                Id = _nextOutpatientId++,
                AppointmentId = appointment.Id,
                ClinicId = appointment.ClinicId,
                WalkSpeed = Agent.DefaultWalkSpeed * (appointment.Age >= 75 ? 0.7 : 1.0),
            };
            outpatient.PlaceAt(WalkInSpawn);
            appointment.Status = AppointmentStatus.Arriving;
            appointment.OutpatientId = outpatient.Id;
            _outpatients.Add(outpatient);
            if (ReceptionFor(outpatient) is { } reception)
            {
                Walk(outpatient, QueueSpot(reception, QueueAt(reception).Count()));
            }
        }

        // Old appointments are dropped after a week to keep saves small.
        _appointments.RemoveAll(a => a.Time < Now.Date.AddDays(-7));
    }

    private void CancelAppointment(Appointment appointment, CareSpace? clinic)
    {
        appointment.Status = AppointmentStatus.Cancelled;
        var name = clinic?.Name ?? "A clinic";
        if (_cancelledToday.Add(appointment.ClinicId))
        {
            var why = clinic is null ? "the room has gone" : $"it needs {string.Join(" and ", ClinicProblems(clinic))}";
            Notifications.Add(Now, NotificationLevel.Warning, $"{name} is cancelling appointments: {why}.");
        }
    }

    /// <summary>The Outpatients reception nearest the patient's clinic that has a receptionist.</summary>
    private CareSpace? ReceptionFor(Outpatient outpatient)
    {
        var clinic = FindSpace(outpatient.ClinicId);
        var target = clinic?.Rect ?? Waiting.Rect;
        return SpacesOfKind(CareSpaceKind.OutpatientReception)
            .Where(IsStaffed)
            .OrderBy(r => Math.Abs(r.Rect.X - target.X) + Math.Abs(r.Rect.Y - target.Y))
            .FirstOrDefault();
    }

    private IEnumerable<Outpatient> QueueAt(CareSpace reception) =>
        _outpatients.Where(o => o.Stage is OutpatientStage.WalkingIn or OutpatientStage.Queueing or OutpatientStage.CheckingIn && ReceptionFor(o) == reception)
            .OrderBy(o => o.AppointmentId);

    private GridCoord QueueSpot(CareSpace reception, int place) =>
        place < reception.QueueTiles.Count ? reception.QueueTiles[place] : reception.QueueTiles.LastOrDefault(reception.StaffSpot);

    // ---------------------------------------------------------------- checking in

    private void RunReceptions()
    {
        foreach (var reception in SpacesOfKind(CareSpaceKind.OutpatientReception))
        {
            var queue = QueueAt(reception).ToList();
            for (var i = 0; i < queue.Count; i++)
            {
                var o = queue[i];
                if (o.Stage == OutpatientStage.CheckingIn)
                {
                    continue;
                }

                if (!o.IsMoving)
                {
                    o.Stage = OutpatientStage.Queueing;
                    o.StatusText = "Queueing to check in";
                }

                var spot = QueueSpot(reception, i);
                if (o.Destination != spot && o.Tile != spot)
                {
                    Walk(o, spot);
                }
            }

            var receptionist = _staff.FirstOrDefault(s => s.BaseRoomId == reception.Id && s.Activity is StaffActivity.Idle or StaffActivity.Working && !s.IsMoving && s.Tile == reception.StaffSpot);
            var front = queue.FirstOrDefault();
            if (front is null || receptionist is null)
            {
                continue;
            }

            var appointment = FindAppointment(front.AppointmentId)!;
            if (front.Stage == OutpatientStage.Queueing && !front.IsMoving && front.Tile == QueueSpot(reception, 0))
            {
                front.Stage = OutpatientStage.CheckingIn;
                front.StatusText = "Checking in";
                front.BusyUntil = Now.AddSeconds(Random.Range(60, 120) / (0.6 + (receptionist.Skill * 0.6)));
                receptionist.Activity = StaffActivity.Working;
                receptionist.StatusText = $"Checking in {appointment.FullName}";
            }
            else if (front.Stage == OutpatientStage.CheckingIn && Now >= front.BusyUntil)
            {
                front.Stage = OutpatientStage.Waiting;
                front.StatusText = "Waiting to be called";
                front.BusyUntil = null;
                appointment.Status = AppointmentStatus.Waiting;
                appointment.CheckedInAt = Now;
                receptionist.Activity = StaffActivity.Idle;
                receptionist.StatusText = "Available";
                receptionist.TasksCompleted++;
            }
        }

        // Without a receptionist anywhere, queueing patients can't check in: their clinics cancel.
        foreach (var o in _outpatients.Where(o => o.Stage is OutpatientStage.WalkingIn or OutpatientStage.Queueing && ReceptionFor(o) is null).ToList())
        {
            if (FindAppointment(o.AppointmentId) is { } appointment)
            {
                CancelAppointment(appointment, FindSpace(appointment.ClinicId));
            }

            SendHome(o, "Appointment cancelled");
        }
    }

    // ---------------------------------------------------------------- waiting

    /// <summary>Every chair a clinic patient can wait on: Outpatients reception seats and chairs in the corridors.</summary>
    public IEnumerable<GridCoord> OutpatientSeats =>
        SpacesOfKind(CareSpaceKind.OutpatientReception).SelectMany(r => r.Seats).Concat(_corridorChairs.Select(c => c.Tile));

    private void SeatWaitingPatients()
    {
        var taken = _outpatients.Where(o => o.Seat is not null).Select(o => o.Seat!.Value).ToHashSet();
        foreach (var o in _outpatients.Where(o => o.Stage == OutpatientStage.Waiting))
        {
            if (o.Seat is { } seat && OutpatientSeats.Contains(seat))
            {
                continue;
            }

            if (o.Seat is not null)
            {
                taken.Remove(o.Seat.Value);
                o.Seat = null;
            }

            var clinic = FindSpace(o.ClinicId);
            var near = clinic is null ? o.Tile : new GridCoord(clinic.Rect.X + (clinic.Rect.Width / 2), clinic.Rect.Y + (clinic.Rect.Length / 2));
            var free = OutpatientSeats.Where(s => !taken.Contains(s)).OrderBy(s => Math.Abs(s.X - near.X) + Math.Abs(s.Y - near.Y)).FirstOrDefault(new GridCoord(-1, -1));
            if (free.X >= 0)
            {
                o.Seat = free;
                taken.Add(free);
                Walk(o, free);
            }
            else if (!o.IsMoving && o.Destination is null && ReceptionFor(o) is { } reception)
            {
                // Nowhere to sit: stand near the reception desk.
                Walk(o, WaitingPlace(20 + (o.Id % 12)));
                o.StatusText = "Waiting (no free chair)";
            }
        }
    }

    /// <summary>Patients waiting for a clinic that has been demolished or has lost its doctor are sent home.</summary>
    private void SendHomeFromStoppedClinics()
    {
        foreach (var o in _outpatients.Where(o => o.Stage is OutpatientStage.WalkingIn or OutpatientStage.Queueing or OutpatientStage.CheckingIn or OutpatientStage.Waiting).ToList())
        {
            var clinic = FindSpace(o.ClinicId);
            if (clinic is not null && IsClinicRunning(clinic))
            {
                continue;
            }

            if (FindAppointment(o.AppointmentId) is { } appointment)
            {
                CancelAppointment(appointment, clinic);
            }

            SendHome(o, "Appointment cancelled");
        }
    }

    // ---------------------------------------------------------------- being seen

    private void RunClinics()
    {
        // Consultations in a room that has been demolished stop; the patient is sent home with the others.
        foreach (var orphan in _consults.Values.Where(c => FindSpace(c.ClinicId) is null).ToList())
        {
            StopConsult(orphan);
        }

        foreach (var clinic in ClinicRooms.ToList())
        {
            if (_consults.TryGetValue(clinic.Id, out var consult))
            {
                ProgressConsult(clinic, consult);
                continue;
            }

            var clinician = ClinicianFor(clinic);
            var doctor = _staff.FirstOrDefault(s => s.BaseRoomId == clinic.Id && s.Role == clinician && s.Activity == StaffActivity.Idle);
            if (doctor is null)
            {
                continue;
            }

            var next = _outpatients
                .Where(o => o.ClinicId == clinic.Id && o.Stage == OutpatientStage.Waiting)
                .Select(o => (Outpatient: o, Appointment: FindAppointment(o.AppointmentId)!))
                .Where(x => Now >= x.Appointment.Time.AddMinutes(-ClinicCallEarlyMinutes))
                .OrderBy(x => x.Appointment.Time)
                .FirstOrDefault();
            if (next.Outpatient is null)
            {
                continue;
            }

            // Call them in.
            var o = next.Outpatient;
            o.Stage = OutpatientStage.GoingIn;
            o.StatusText = $"Going in to {clinic.Name}";
            o.Seat = null;
            Walk(o, clinic.PatientSpot);
            next.Appointment.Status = AppointmentStatus.InClinic;
            doctor.Activity = StaffActivity.WalkingToTask;
            doctor.StatusText = $"Calling {next.Appointment.FullName}";
            WalkTo(doctor, clinic.StaffSpot);
            _consults[clinic.Id] = new ClinicConsult(clinic.Id, o.Id, doctor.Id);
        }
    }

    private void ProgressConsult(CareSpace clinic, ClinicConsult consult)
    {
        var o = FindOutpatient(consult.OutpatientId);
        var doctor = FindStaff(consult.StaffId);
        var appointment = o is null ? null : FindAppointment(o.AppointmentId);
        if (o is null || appointment is null || doctor is null || doctor.BaseRoomId != clinic.Id)
        {
            StopConsult(consult);
            return;
        }

        if (o.Stage == OutpatientStage.GoingIn && !o.IsMoving && !doctor.IsMoving)
        {
            o.Stage = OutpatientStage.BeingSeen;
            o.StatusText = $"Being seen: {appointment.Reason.ToLowerInvariant()}";
            var minutes = appointment.Minutes * Random.Range(0.7, 1.3) * (1.25 - (doctor.Skill * 0.5));
            o.BusyUntil = Now.AddMinutes(minutes);
            doctor.Activity = StaffActivity.Working;
            doctor.StatusText = $"Clinic: {appointment.FullName} ({appointment.Reason.ToLowerInvariant()})";
            return;
        }

        if (o.Stage == OutpatientStage.BeingSeen && Now >= o.BusyUntil)
        {
            appointment.Status = AppointmentStatus.Seen;
            Tally(StatKeys.ClinicSeen);
            appointment.SeenAt = Now;
            appointment.SeenByStaffId = doctor.Id;
            if (ClinicOf(clinic) is { } definition)
            {
                Book(MoneyCategory.Clinics, definition.Tariff);
            }

            doctor.TasksCompleted++;
            doctor.Activity = StaffActivity.Idle;
            doctor.StatusText = "Available";
            _consults.Remove(clinic.Id);
            SendHome(o, "Going home");
        }
    }

    /// <summary>The doctor left or was moved, or the room went: the patient goes back to waiting.</summary>
    private void StopConsult(ClinicConsult consult)
    {
        _consults.Remove(consult.ClinicId);
        var o = FindOutpatient(consult.OutpatientId);
        var appointment = o is null ? null : FindAppointment(o.AppointmentId);
        if (o is not null && appointment is not null && o.Stage is OutpatientStage.GoingIn or OutpatientStage.BeingSeen)
        {
            o.Stage = OutpatientStage.Waiting;
            o.StatusText = "Waiting to be called";
            o.BusyUntil = null;
            appointment.Status = AppointmentStatus.Waiting;
        }

        if (FindStaff(consult.StaffId) is { } doctor && doctor.Activity is StaffActivity.Working or StaffActivity.WalkingToTask)
        {
            doctor.Activity = StaffActivity.Idle;
            doctor.StatusText = "Available";
        }
    }

    private void SendHome(Outpatient o, string status)
    {
        o.Stage = OutpatientStage.Leaving;
        o.StatusText = status;
        o.Seat = null;
        o.BusyUntil = null;
        Walk(o, WalkInSpawn);
    }

    private void Walk(Outpatient o, GridCoord destination)
    {
        if (o.Tile == destination && !o.IsMoving)
        {
            return;
        }

        var path = Nav.FindPath(o.Tile, destination);
        if (path.Count > 0)
        {
            o.FollowPath(path);
        }
        else
        {
            o.PlaceAt(destination);
        }
    }

    /// <summary>Clinic staff: breaks when tired and between patients; otherwise they wait at their desk.</summary>
    private void UpdateClinicStaff(StaffMember member)
    {
        if (member.Activity is StaffActivity.Working or StaffActivity.WalkingToTask)
        {
            return;
        }

        var busy = _consults.Values.Any(c => c.StaffId == member.Id);
        if (!busy && member.Activity == StaffActivity.Idle && DueBreak(member) is { } due && CanTakeBreak(member, due, null))
        {
            TakeBreak(member, due);
            return;
        }

        if (!busy && member.Fatigue >= BreakThreshold && member.Role != StaffRole.Receptionist)
        {
            TakeBreak(member);
            return;
        }

        if (member.Activity == StaffActivity.Idle)
        {
            WalkTo(member, HomeTile(member));
        }
    }

    // ---------------------------------------------------------------- chairs in corridors

    private readonly List<PlacedFurniture> _corridorChairs = new();

    /// <summary>The furniture item used for chairs placed in corridors.</summary>
    public const string CorridorChairItem = "waiting_seat";

    /// <summary>Chairs the player has placed in corridors for clinic patients to wait on.</summary>
    public IReadOnlyList<PlacedFurniture> CorridorChairs => _corridorChairs;

    public int CorridorChairCost => Database.Furniture.TryGetValue(CorridorChairItem, out var chair) ? chair.Cost : 0;

    private static PlacedFurniture ChairAt(GridCoord tile) => new(CorridorChairItem, tile.X + 0.15, tile.Y + 0.15, 0.7, 0.7);

    /// <summary>Whether a chair can go on this corridor tile.</summary>
    public BuildCheck CheckCorridorChair(GridCoord tile)
    {
        var problems = new List<string>();
        var inCorridor = _spaces.Values.Any(s => s.Kind == CareSpaceKind.Corridor && s.Rect.Contains(tile))
                         && !_spaces.Values.Any(s => s.HasWalls && s.Rect.Contains(tile));
        if (!inCorridor)
        {
            problems.Add("Chairs go in corridors.");
        }
        else if (_corridorChairs.Any(c => c.Tile == tile))
        {
            problems.Add("There is already a chair there.");
        }
        else if (DoorwayTiles().Contains(tile))
        {
            problems.Add("Keep doorways clear.");
        }

        if (!CanAfford(CorridorChairCost))
        {
            problems.Add($"Not enough money: costs £{CorridorChairCost:N0}, you have £{Balance:N0}.");
        }

        return new BuildCheck(CorridorChairCost, problems);
    }

    public BuildCheck PlaceCorridorChair(GridCoord tile)
    {
        var check = CheckCorridorChair(tile);
        if (!check.CanBuild)
        {
            return check;
        }

        _corridorChairs.Add(ChairAt(tile));
        Book(MoneyCategory.Construction, -check.Cost);
        Events.Publish(new LayoutChanged());
        return check;
    }

    /// <summary>Takes a corridor chair away, refunding half its cost.</summary>
    public bool RemoveCorridorChair(GridCoord tile)
    {
        var chair = _corridorChairs.FirstOrDefault(c => c.Tile == tile);
        if (chair is null)
        {
            return false;
        }

        _corridorChairs.Remove(chair);
        Book(MoneyCategory.Construction, CorridorChairCost / 2);
        foreach (var o in _outpatients.Where(o => o.Seat == tile))
        {
            o.Seat = null;
        }

        Events.Publish(new LayoutChanged());
        return true;
    }

    /// <summary>The corridor tiles just outside each room's doors (and the entrances), which must stay clear.</summary>
    private HashSet<GridCoord> DoorwayTiles()
    {
        var tiles = new HashSet<GridCoord>();
        foreach (var space in _spaces.Values.Where(s => s.HasWalls))
        {
            var r = space.Rect;
            foreach (var t in r.Tiles())
            {
                if (t.Y == r.Y && space.IsDoor(t, WallSide.North))
                {
                    tiles.Add(new GridCoord(t.X, t.Y - 1));
                }

                if (t.Y == r.Bottom - 1 && space.IsDoor(t, WallSide.South))
                {
                    tiles.Add(new GridCoord(t.X, t.Y + 1));
                }

                if (t.X == r.X && space.IsDoor(t, WallSide.West))
                {
                    tiles.Add(new GridCoord(t.X - 1, t.Y));
                }

                if (t.X == r.Right - 1 && space.IsDoor(t, WallSide.East))
                {
                    tiles.Add(new GridCoord(t.X + 1, t.Y));
                }
            }
        }

        return tiles;
    }

    /// <summary>After building work, chairs no longer in a clear corridor are taken away (refunded in full).</summary>
    private void PruneCorridorChairs()
    {
        var doorways = DoorwayTiles();
        foreach (var chair in _corridorChairs.ToList())
        {
            var tile = chair.Tile;
            var ok = _spaces.Values.Any(s => s.Kind == CareSpaceKind.Corridor && s.Rect.Contains(tile))
                     && !_spaces.Values.Any(s => s.HasWalls && s.Rect.Contains(tile))
                     && !doorways.Contains(tile);
            if (!ok)
            {
                _corridorChairs.Remove(chair);
                Book(MoneyCategory.Construction, CorridorChairCost);
            }
        }
    }
}
