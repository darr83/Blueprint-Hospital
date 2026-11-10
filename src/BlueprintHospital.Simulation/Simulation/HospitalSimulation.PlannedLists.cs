using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>Where a planned patient's booking has got to.</summary>
public enum PlannedStatus
{
    /// <summary>On the list; the patient hasn't come in yet.</summary>
    Booked,

    /// <summary>In a day surgery bed, waiting for (or having) their procedure.</summary>
    Admitted,

    /// <summary>Procedure done.</summary>
    Done,

    /// <summary>Cancelled on the day: no bed, no room, or the list ran out of time.</summary>
    Cancelled,
}

/// <summary>One patient booked onto a planned list.</summary>
public sealed class PlannedBooking
{
    public required int Id { get; init; }

    public required string ConditionId { get; init; }

    /// <summary>When they come in to the day surgery unit.</summary>
    public required DateTime ArrivesAt { get; init; }

    public PlannedStatus Status { get; set; } = PlannedStatus.Booked;

    /// <summary>The patient, once they have come in.</summary>
    public int? PatientId { get; set; }

    /// <summary>Why it was cancelled.</summary>
    public string? Note { get; set; }
}

/// <summary>
/// Planned lists. Every weekday morning patients are booked onto lists for planned day-case procedures:
/// operations in theatre (hernia repair, gallbladder removal), angiograms in the cath lab and endoscopies
/// in the endoscopy room. A list runs when its room is open (a theatre ticked for the department), the
/// department is unlocked and there is a day surgery unit. Patients come in to a day surgery bed, have
/// their procedure when the room is free, as emergencies always go first, and go home the same day. A
/// patient who has no bed when they arrive, or whose procedure hasn't started by the end of the list, is
/// cancelled: the hospital isn't paid and its reputation suffers.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>Planned patients come in between these times.</summary>
    public static readonly TimeSpan PlannedFirstArrival = TimeSpan.FromHours(7.5);

    public static readonly TimeSpan PlannedLastArrival = TimeSpan.FromHours(13);

    /// <summary>A planned procedure not started by this time is cancelled.</summary>
    public static readonly TimeSpan PlannedListEnds = TimeSpan.FromHours(18);

    private readonly List<PlannedBooking> _plannedBookings = new();
    private readonly HashSet<string> _bookedPlannedDays = new();
    private int _nextPlannedId = 1;

    public IReadOnlyList<PlannedBooking> PlannedBookings => _plannedBookings;

    /// <summary>The planned procedures (from the data), in a stable order.</summary>
    public IEnumerable<ConditionDefinition> PlannedProcedures =>
        Database.Conditions.Values.Where(c => c.IsPlanned).OrderBy(c => c.Id, StringComparer.Ordinal);

    public IEnumerable<CareSpace> DaySurgeryUnits => Wards.Where(w => UnitOf(w) == WardUnit.DaySurgery);

    /// <summary>The room a planned procedure is done in: "theatre", "the cath lab", "the endoscopy room".</summary>
    public static string ProcedurePlace(CareSpaceKind room) => room switch
    {
        CareSpaceKind.CathLab => "the cath lab",
        CareSpaceKind.Endoscopy => "the endoscopy room",
        CareSpaceKind.DeliveryRoom => "a delivery room",
        _ => "theatre",
    };

    /// <summary>What a planned procedure's list still needs before patients can be booked (empty when it can run).</summary>
    public IReadOnlyList<string> PlannedListProblems(ConditionDefinition procedure)
    {
        var problems = new List<string>();
        var operation = procedure.Operation!;
        if (!IsUnlocked(procedure.Department))
        {
            problems.Add($"the {DepartmentName(procedure.Department)} department");
        }
        else if (!HasProcedureRoomFor(operation, procedure.Department))
        {
            problems.Add(operation.Room == CareSpaceKind.Theatre
                ? $"a theatre ticked for {DepartmentName(procedure.Department)} with a surgeon and an anaesthetist"
                : operation.Room == CareSpaceKind.CathLab ? "a cath lab with a cardiac physiologist" : "an endoscopy room with an endoscopist");
        }

        if (!DaySurgeryUnits.Any())
        {
            problems.Add("a day surgery unit");
        }

        return problems;
    }

    /// <summary>How many staffed rooms can run this procedure's list.</summary>
    private int PlannedRoomsFor(ConditionDefinition procedure) =>
        procedure.Operation!.Room == CareSpaceKind.Theatre
            ? SpacesOfKind(CareSpaceKind.Theatre).Count(t => IsStaffed(t) && t.AllowedDepartments.Contains(procedure.Department))
            : SpacesOfKind(procedure.Operation.Room).Count(IsStaffed);

    /// <summary>Today's (or another day's) planned bookings, by arrival time.</summary>
    public IEnumerable<PlannedBooking> PlannedListFor(DateTime date) =>
        _plannedBookings.Where(b => b.ArrivesAt.Date == date.Date).OrderBy(b => b.ArrivesAt).ThenBy(b => b.Id);

    private void UpdatePlannedLists()
    {
        BookPlannedLists();
        AdmitPlannedPatients();
        CancelUnfinishedPlannedPatients();
    }

    /// <summary>Each weekday, once, before the first patient is due: books today's lists.</summary>
    private void BookPlannedLists()
    {
        var date = Now.Date;
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || Now.TimeOfDay >= PlannedLastArrival)
        {
            return;
        }

        var key = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var open = PlannedProcedures.Where(p => PlannedListProblems(p).Count == 0).ToList();
        if (open.Count == 0 || !_bookedPlannedDays.Add(key))
        {
            return;
        }

        // A list opened part way through the morning only books patients who can still get here.
        var earliest = Now.TimeOfDay > PlannedFirstArrival ? Now.TimeOfDay.Add(TimeSpan.FromMinutes(30)) : PlannedFirstArrival;
        if (earliest >= PlannedLastArrival)
        {
            return;
        }

        foreach (var procedure in open)
        {
            var planned = procedure.Planned!;
            var count = Random.Range(planned.PerDay[0], planned.PerDay[1]) * PlannedRoomsFor(procedure);
            for (var i = 0; i < count; i++)
            {
                var minutes = Random.Range(earliest.TotalMinutes, PlannedLastArrival.TotalMinutes);
                _plannedBookings.Add(new PlannedBooking
                {
                    Id = _nextPlannedId++,
                    ConditionId = procedure.Id,
                    ArrivesAt = date.AddMinutes(Math.Round(minutes / 15) * 15),
                });
            }
        }

        _bookedPlannedDays.RemoveWhere(k => DateTime.TryParse(k, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) && d < date.AddDays(-7));
        _plannedBookings.RemoveAll(b => b.ArrivesAt < date.AddDays(-7));
    }

    private void AdmitPlannedPatients()
    {
        foreach (var booking in _plannedBookings.Where(b => b.Status == PlannedStatus.Booked && Now >= b.ArrivesAt).ToList())
        {
            if (!Database.Conditions.TryGetValue(booking.ConditionId, out var procedure))
            {
                booking.Status = PlannedStatus.Cancelled;
                continue;
            }

            var problems = PlannedListProblems(procedure);
            var bed = problems.Count == 0 ? FreeDaySurgeryBed() : null;
            if (bed is null)
            {
                CancelPlanned(booking, procedure, problems.Count > 0 ? $"the list needs {Join(problems)}" : "the day surgery unit had no spare bed", patient: null);
                continue;
            }

            AdmitPlanned(booking, procedure, bed.Value);
        }
    }

    /// <summary>Books a planned patient in now and brings them straight in. Used by scenarios and tests.</summary>
    public Patient? SpawnPlannedPatient(string conditionId)
    {
        var booking = new PlannedBooking { Id = _nextPlannedId++, ConditionId = conditionId, ArrivesAt = Now };
        _plannedBookings.Add(booking);
        AdmitPlannedPatients();
        return booking.PatientId is { } id ? FindPatient(id) : null;
    }

    /// <summary>
    /// A free day surgery bed for a planned patient. Each unit keeps a quarter of its beds (at least one)
    /// free for emergency day cases from A&amp;E, so emergencies are never squeezed out by the lists.
    /// </summary>
    private (CareSpace Ward, WardBed Bed, int Index)? FreeDaySurgeryBed()
    {
        foreach (var ward in DaySurgeryUnits)
        {
            var reserved = Math.Max(1, ward.Beds.Count / 4);
            if (ward.Beds.Count(IsVacant) <= reserved)
            {
                continue;
            }

            for (var i = 0; i < ward.Beds.Count; i++)
            {
                if (IsVacant(ward.Beds[i]))
                {
                    return (ward, ward.Beds[i], i);
                }
            }
        }

        return null;
    }

    private void AdmitPlanned(PlannedBooking booking, ConditionDefinition procedure, (CareSpace Ward, WardBed Bed, int Index) place)
    {
        var patient = _generator.Generate(Now, ArrivalMethod.WalkIn, procedure.Id);
        var operation = procedure.Operation!;
        var (ward, bed, index) = place;
        _patients[patient.Id] = patient;
        patient.PlaceAt(WalkInSpawn);

        // Booked in advance: no A&E queue, triage or four-hour clock.
        patient.LeftEmergencyAt = Now;
        patient.HistoryTaken = true;
        patient.KnownSymptoms.UnionWith(patient.Symptoms);
        patient.Triage = TriageCategory.Green;
        patient.TriagedAt = Now;
        patient.TriageReason = "Planned admission.";
        patient.WorkingDiagnosis = new Diagnosis(procedure.Id, 1, Now, 0);
        patient.EffectiveTreatmentGiven = true;
        patient.RecordObservations(Now, patient.CurrentObservations());

        bed.OccupantId = patient.Id;
        patient.CareSpaceId = ward.Id;
        patient.BedIndex = index;
        patient.Stage = PatientStage.OnWard;
        patient.PreOp = true;
        patient.DayCase = true;
        patient.AdmittedAt = Now;
        WalkTo(patient, bed.PatientSpot);

        booking.Status = PlannedStatus.Admitted;
        booking.PatientId = patient.Id;
        patient.Log(Now, $"Came in for a planned {LowerFirst(operation.Name)}. Bed {index + 1} on {ward.Name} until {ProcedurePlace(operation.Room)} is ready.", important: true);
        AddTask(TaskKind.Surgery, RoleForRoom(operation.Room), patient, operation.Minutes, operation.Name, procedure.Id);
        Events.Publish(new PatientArrived(patient));
    }

    /// <summary>At the end of the list, anyone still waiting for their procedure is sent home and rebooked another day.</summary>
    private void CancelUnfinishedPlannedPatients()
    {
        if (Now.TimeOfDay < PlannedListEnds)
        {
            return;
        }

        foreach (var booking in _plannedBookings.Where(b => b.Status == PlannedStatus.Admitted && b.ArrivesAt.Date == Now.Date).ToList())
        {
            if (booking.PatientId is not { } id || FindPatient(id) is not { } patient || !patient.PreOp)
            {
                continue;
            }

            // Someone already being operated on finishes.
            if (_tasks.Values.Any(t => t.PatientId == id && t.Kind == TaskKind.Surgery && t.AssignedStaffId is not null))
            {
                continue;
            }

            CancelPlanned(booking, Database.Conditions[booking.ConditionId], "the list ran out of time (emergencies came first)", patient);
        }
    }

    private void CancelPlanned(PlannedBooking booking, ConditionDefinition procedure, string why, Patient? patient)
    {
        booking.Status = PlannedStatus.Cancelled;
        booking.Note = why;
        Stats.ReputationPenalties++;
        Stats.PlannedCancelled++;
        Notifications.Add(Now, NotificationLevel.Warning, $"Planned {LowerFirst(procedure.Operation!.Name)} cancelled: {why}.", patient?.Id);
        if (patient is null)
        {
            return;
        }

        patient.PreOp = false;
        patient.OutcomeNote = $"Planned {LowerFirst(procedure.Operation.Name)} cancelled on the day: {why}. Sent home to be rebooked.";
        patient.Log(Now, patient.OutcomeNote, important: true);
        EndVisit(patient, PatientOutcome.Discharged);
        WalkTo(patient, WalkInSpawn);
    }

    /// <summary>The procedure is done: the booking is marked and the hospital is paid for it.</summary>
    private void CompletePlanned(Patient patient, ConditionDefinition procedure)
    {
        if (_plannedBookings.FirstOrDefault(b => b.PatientId == patient.Id) is { } booking)
        {
            booking.Status = PlannedStatus.Done;
        }

        Stats.PlannedDone++;
        Book(MoneyCategory.Operations, procedure.Planned!.Tariff);
    }

    internal void RestorePlannedLists(List<PlannedBooking>? bookings, List<string>? days, int nextId)
    {
        _plannedBookings.Clear();
        _bookedPlannedDays.Clear();
        if (bookings is not null)
        {
            _plannedBookings.AddRange(bookings.Where(b => Database.Conditions.ContainsKey(b.ConditionId)));
        }

        if (days is not null)
        {
            _bookedPlannedDays.UnionWith(days);
        }

        _nextPlannedId = Math.Max(nextId, _plannedBookings.Count == 0 ? 1 : _plannedBookings.Max(b => b.Id) + 1);
    }
}
