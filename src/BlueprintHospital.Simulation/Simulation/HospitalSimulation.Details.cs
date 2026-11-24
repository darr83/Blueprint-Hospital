using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// One illness in the illness journal: how many patients the hospital has looked after with it and how they got on.
/// Every patient counts once, when they leave (home, a transfer, or death).
/// </summary>
public sealed class JournalEntry
{
    public string ConditionId { get; set; } = string.Empty;

    /// <summary>Patients who have left after care for this illness (not counting anyone who left before being seen).</summary>
    public int Treated { get; set; }

    public int WentHome { get; set; }

    public int Transferred { get; set; }

    public int Died { get; set; }

    public int LeftUnseen { get; set; }

    /// <summary>Of those treated, how many were admitted to a ward first.</summary>
    public int Admitted { get; set; }

    public int Ambulance { get; set; }

    public int Red { get; set; }

    public int Amber { get; set; }

    public int Green { get; set; }

    public int Male { get; set; }

    public long AgeTotal { get; set; }

    /// <summary>Minutes from arrival to leaving the hospital, added up over everyone treated.</summary>
    public long StayMinutes { get; set; }

    /// <summary>Minutes in A&amp;E (to leaving A&amp;E or the hospital), added up.</summary>
    public long EmergencyMinutes { get; set; }

    /// <summary>Diagnoses made for patients who really had this illness, and how many of those were right.</summary>
    public int Diagnosed { get; set; }

    public int DiagnosedRight { get; set; }

    public DateTime? FirstSeen { get; set; }

    /// <summary>Patients treated in each week of the game (week 0 is the first week).</summary>
    public Dictionary<int, int> PerWeek { get; set; } = new();

    public double AverageStayDays => Treated == 0 ? 0 : StayMinutes / (double)Treated / 1440.0;

    public double AverageEmergencyMinutes => Treated == 0 ? 0 : EmergencyMinutes / (double)Treated;

    public double AverageAge => Treated == 0 ? 0 : AgeTotal / (double)Treated;

    public double DiagnosedRightShare => Diagnosed == 0 ? 0 : DiagnosedRight / (double)Diagnosed;
}

/// <summary>What a room's log says happened.</summary>
public enum RoomLogKind
{
    Note,
    Admitted,
    WentHome,
    Transferred,
    Died,
    RedMonitor,
}

/// <summary>A line in a room's log (wards, cubicles, bays): who came, who went, who turned red.</summary>
public sealed record RoomLogEntry(string RoomId, DateTime Time, RoomLogKind Kind, string Text);

/// <summary>A room's day: admissions, people going home, deaths and the average stay of those who left.</summary>
public sealed record RoomDay(int Admitted, int WentHome, int Died, double AverageStayDays);

/// <summary>One ward's line on the Inpatients board.</summary>
public sealed record WardBoardLine(CareSpace Ward, int BedsUsed, int Beds, double AverageStayDays, bool Closed);

/// <summary>The Inpatients board: everyone on a ward, by live NEWS2 band, and each ward's beds and stays.</summary>
public sealed record InpatientBoard(
    int Admitted, int Red, int Amber, int Green, IReadOnlyList<WardBoardLine> Wards,
    int AwaitingBed, int GoingHomeToday, double AverageStayDays, double LongestStayDays, int TheatreToday);

/// <summary>One clinic's line on the Clinics board.</summary>
public sealed record ClinicBoardLine(CareSpace Clinic, int Seen, int Booked, int MinutesLate, bool Running, string Problem);

/// <summary>The Clinics board: today's clinic patients by where they are, and each clinic's progress.</summary>
public sealed record ClinicBoard(
    int BookedToday, int Waiting, int InClinic, int Seen, int LongestWaitMinutes, IReadOnlyList<ClinicBoardLine> Clinics,
    int DidNotAttend, int StillToArrive, double AverageWaitMinutes, double SeenOnTimeShare, int IncomeToday);

/// <summary>The rhythm a bedside monitor's ECG shows.</summary>
public enum MonitorRhythm
{
    Sinus,
    SinusTachycardia,
    SinusBradycardia,
    AtrialFibrillation,
    Svt,
    HeartBlock,
    VentricularFibrillation,
    Asystole,
}

/// <summary>What is happening to a patient right now, for a ward or room list.</summary>
public enum BedsideState
{
    None,
    New,
    Treating,
    Waiting,
    GoingHome,
    Arrest,
}

/// <summary>The extra detail behind the room panel, the illness journal, the boards and the big monitor.</summary>
public sealed partial class HospitalSimulation
{
    /// <summary>Lines kept in each room's log.</summary>
    public const int RoomLogLength = 60;

    /// <summary>A clinic counts as running late once its next patient has waited this long past their time.</summary>
    public const int ClinicLateMinutes = 10;

    private readonly Dictionary<string, JournalEntry> _journal = new();
    private readonly Dictionary<string, List<RoomLogEntry>> _roomLogs = new();
    private DateTime? _journalStart;

    /// <summary>Every illness the hospital has looked after, by condition id.</summary>
    public IReadOnlyDictionary<string, JournalEntry> Journal => _journal;

    public JournalEntry? JournalFor(string conditionId) => _journal.GetValueOrDefault(conditionId);

    /// <summary>The week of the game a moment falls in (0 = the first week).</summary>
    public int GameWeek(DateTime time) => Math.Max(0, (int)((time - (_journalStart ?? StartTime)).TotalDays / 7));

    private DateTime StartTime => _journalStart ??= Now;

    /// <summary>Counts a patient in the illness journal as they leave.</summary>
    private void RecordJournal(Patient patient, PatientOutcome outcome)
    {
        var id = patient.Condition.Id;
        if (!_journal.TryGetValue(id, out var entry))
        {
            entry = new JournalEntry { ConditionId = id };
            _journal[id] = entry;
        }

        if (outcome == PatientOutcome.LeftWithoutBeingSeen)
        {
            entry.LeftUnseen++;
            return;
        }

        _ = StartTime;
        entry.FirstSeen ??= patient.ArrivedAt;
        entry.Treated++;
        switch (outcome)
        {
            case PatientOutcome.Died:
                entry.Died++;
                break;
            case PatientOutcome.Transferred:
                entry.Transferred++;
                break;
            default:
                entry.WentHome++;
                break;
        }

        if (patient.AdmittedAt is not null)
        {
            entry.Admitted++;
        }

        if (patient.ArrivalMethod == ArrivalMethod.Ambulance)
        {
            entry.Ambulance++;
        }

        switch (patient.Triage)
        {
            case TriageCategory.Red:
                entry.Red++;
                break;
            case TriageCategory.Amber:
                entry.Amber++;
                break;
            case TriageCategory.Green:
                entry.Green++;
                break;
        }

        if (patient.Sex == Sex.Male)
        {
            entry.Male++;
        }

        entry.AgeTotal += patient.Age;
        entry.StayMinutes += (long)Math.Round((Now - patient.ArrivedAt).TotalMinutes);
        entry.EmergencyMinutes += (long)Math.Round(patient.TimeInEmergency(Now).TotalMinutes);
        var week = GameWeek(Now);
        entry.PerWeek[week] = entry.PerWeek.GetValueOrDefault(week) + 1;
    }

    /// <summary>Counts a diagnosis against the illness the patient really has (right or not).</summary>
    private void RecordJournalDiagnosis(Patient patient, bool correct)
    {
        var id = patient.Condition.Id;
        if (!_journal.TryGetValue(id, out var entry))
        {
            entry = new JournalEntry { ConditionId = id };
            _journal[id] = entry;
        }

        entry.Diagnosed++;
        if (correct)
        {
            entry.DiagnosedRight++;
        }
    }

    // ------------------------------------------------------------------ room logs

    /// <summary>A room's log, newest first.</summary>
    public IReadOnlyList<RoomLogEntry> RoomLog(string roomId) =>
        _roomLogs.TryGetValue(roomId, out var log) ? log.AsEnumerable().Reverse().ToList() : Array.Empty<RoomLogEntry>();

    /// <summary>Adds a line to a room's log.</summary>
    public void LogRoom(string? roomId, RoomLogKind kind, string text)
    {
        if (roomId is null)
        {
            return;
        }

        if (!_roomLogs.TryGetValue(roomId, out var log))
        {
            log = new List<RoomLogEntry>();
            _roomLogs[roomId] = log;
        }

        log.Add(new RoomLogEntry(roomId, Now, kind, text));
        if (log.Count > RoomLogLength)
        {
            log.RemoveRange(0, log.Count - RoomLogLength);
        }
    }

    /// <summary>A room's day so far: admissions, people going home, deaths and the average stay of those who went.</summary>
    public RoomDay RoomToday(CareSpace room)
    {
        var today = Now.Date;
        var lines = RoomLog(room.Id).Where(l => l.Time.Date == today).ToList();
        var left = _departedToday.Where(d => d.RoomId == room.Id && d.Time.Date == today).ToList();
        return new RoomDay(
            lines.Count(l => l.Kind == RoomLogKind.Admitted),
            lines.Count(l => l.Kind is RoomLogKind.WentHome or RoomLogKind.Transferred),
            lines.Count(l => l.Kind == RoomLogKind.Died),
            left.Count == 0 ? 0 : left.Average(d => d.StayDays));
    }

    private readonly List<(string RoomId, DateTime Time, double StayDays)> _departedToday = new();

    /// <summary>Logs a patient leaving the room they were in (called as their visit ends).</summary>
    private void LogDeparture(Patient patient, PatientOutcome outcome)
    {
        if (patient.CareSpaceId is not { } roomId || FindSpace(roomId) is not { } room)
        {
            return;
        }

        var bed = patient.Stage == PatientStage.OnWard && patient.BedIndex is { } b ? $" (bed {b + 1})" : string.Empty;
        var diagnosis = patient.WorkingDiagnosis is { } d && Database.Conditions.TryGetValue(d.ConditionId, out var c) ? c.Name.ToLowerInvariant() : null;
        var stay = (Now - (patient.AdmittedAt ?? patient.ArrivedAt)).TotalDays;
        var (kind, text) = outcome switch
        {
            PatientOutcome.Died => (RoomLogKind.Died, $"{patient.FullName}{bed} died"),
            PatientOutcome.Transferred => (RoomLogKind.Transferred, $"{patient.FullName}{bed} transferred to another hospital"),
            PatientOutcome.Admitted => (RoomLogKind.Transferred, $"{patient.FullName}{bed} left for a ward"),
            PatientOutcome.LeftWithoutBeingSeen => (RoomLogKind.Note, $"{patient.FullName} left without being seen"),
            _ => (RoomLogKind.WentHome, $"{patient.FullName}{bed} went home" + (diagnosis is null ? string.Empty : $" ({diagnosis}, {StayText(stay)})")),
        };
        LogRoom(room.Id, kind, text);
        _departedToday.RemoveAll(x => x.Time.Date < Now.Date);
        _departedToday.Add((room.Id, Now, stay));
    }

    private static string StayText(double days) => days >= 1 ? $"{days:0.#} day{(Math.Abs(days - 1) < 0.05 ? string.Empty : "s")}" : $"{Math.Max(1, days * 24):0} h";

    // ------------------------------------------------------------------ who is where

    /// <summary>The patients in a room: on a ward's beds, in a cubicle or bay, or in a clinic.</summary>
    public IEnumerable<Patient> PatientsIn(CareSpace room) =>
        _patients.Values.Where(p => p.IsInDepartment && p.CareSpaceId == room.Id);

    /// <summary>Staff who belong to a room (posted there) and are on duty, then anyone else working in it now.</summary>
    public IEnumerable<StaffMember> StaffIn(CareSpace room)
    {
        var posted = _staff.Where(s => s.BaseRoomId == room.Id && s.IsOnDuty);
        var visiting = _staff.Where(s => s.BaseRoomId != room.Id && s.IsOnDuty && s.IsInBuilding && room.Rect.Contains(s.Tile));
        return posted.Concat(visiting).Distinct();
    }

    /// <summary>What is happening to a patient right now, and a short line saying so (for a room's patient list).</summary>
    public (BedsideState State, string Text) BedsideStatus(Patient patient)
    {
        if (patient.InArrest)
        {
            return (BedsideState.Arrest, "Cardiac arrest: crash team");
        }

        var started = _tasks.Values.FirstOrDefault(t => t.PatientId == patient.Id && t.Started && t.AssignedStaffId is not null && t.Kind is not TaskKind.StayWithPatient);
        if (started is not null && FindStaff(started.AssignedStaffId!.Value) is { } member)
        {
            return (BedsideState.Treating, $"{started.Label} · {member.Name}");
        }

        if (patient.Stage == PatientStage.OnWard && patient.DischargeDueAt is { } due && due <= Now)
        {
            return (BedsideState.GoingHome, "Ready to go home");
        }

        var waiting = patient.Investigations.FirstOrDefault(i => i.Status != InvestigationStatus.Resulted);
        if (waiting is not null)
        {
            var name = Database.Investigations.TryGetValue(waiting.InvestigationId, out var test) ? test.Name : "a test";
            return (BedsideState.Waiting, $"Waiting for {name}");
        }

        var pending = _tasks.Values.Where(t => t.PatientId == patient.Id && !t.Started && t.Kind is not (TaskKind.StayWithPatient or TaskKind.WardDischarge)).OrderBy(t => t.CreatedAt).FirstOrDefault();
        if (pending is not null)
        {
            return (BedsideState.Waiting, $"Waiting: {pending.Label.ToLowerInvariant()}");
        }

        if (patient.AdmittedAt is { } admitted && Now - admitted < TimeSpan.FromHours(2))
        {
            return (BedsideState.New, $"Admitted {admitted:HH:mm}");
        }

        if (patient.Stage == PatientStage.OnWard && patient.DischargeDueAt is { } home)
        {
            return (BedsideState.None, home.Date == Now.Date ? $"Home today, about {home:HH:mm}" : $"Home {home:ddd}");
        }

        return (BedsideState.None, patient.Stage == PatientStage.OnWard ? "Settled" : "In the cubicle");
    }

    // ------------------------------------------------------------------ boards

    /// <summary>The Inpatients board, worked out now.</summary>
    public InpatientBoard BuildInpatientBoard()
    {
        var onWards = _patients.Values.Where(p => p.Stage == PatientStage.OnWard && p.IsAlive).ToList();
        var bands = onWards.Select(p => Patient.MonitorColourFor(News2.Calculate(p.CurrentObservations()))).ToList();
        var stays = onWards.Select(p => p.LengthOfStay(Now)?.TotalDays ?? 0).ToList();
        var wards = Spaces.Where(s => s.IsWard && s.Beds.Count > 0)
            .Select(w =>
            {
                var here = onWards.Where(p => p.CareSpaceId == w.Id).ToList();
                var closed = !w.InUse || (UnitOf(w) == Data.Models.WardUnit.IntensiveCare && !IsIntensiveCareOpen(w)) || (OutbreakOn(w.Id)?.IsClosedAt(Now) ?? false);
                return new WardBoardLine(w, w.Beds.Count(b => b.OccupantId is not null), w.Beds.Count, here.Count == 0 ? 0 : here.Average(p => p.LengthOfStay(Now)!.Value.TotalDays), closed);
            })
            .OrderByDescending(l => l.BedsUsed)
            .ThenBy(l => l.Ward.Name, StringComparer.Ordinal)
            .ToList();
        var theatre = _tasks.Values.Count(t => t.Kind == TaskKind.Surgery) + _patients.Values.Count(p => p.Stage == PatientStage.AwaitingSurgery && p.IsInDepartment);
        return new InpatientBoard(
            onWards.Count,
            bands.Count(b => b == TriageCategory.Red),
            bands.Count(b => b == TriageCategory.Amber),
            bands.Count(b => b == TriageCategory.Green),
            wards,
            _patients.Values.Count(p => p.IsInEmergency && p.Stage == PatientStage.AwaitingBed),
            onWards.Count(p => p.DischargeDueAt is { } due && due.Date <= Now.Date),
            stays.Count == 0 ? 0 : stays.Average(),
            stays.Count == 0 ? 0 : stays.Max(),
            theatre);
    }

    /// <summary>The Clinics board for today, worked out now.</summary>
    public ClinicBoard BuildClinicBoard()
    {
        var today = Now.Date;
        var booked = _appointments.Where(a => a.Time.Date == today).ToList();
        var waiting = booked.Where(a => a.Status == AppointmentStatus.Waiting).ToList();
        var seen = booked.Where(a => a.Status == AppointmentStatus.Seen).ToList();
        var waits = booked.Where(a => a.CheckedInAt is not null && a.CalledInAt is not null).Select(a => (a.CalledInAt!.Value - a.CheckedInAt!.Value).TotalMinutes).ToList();
        var onTime = booked.Where(a => a.CalledInAt is not null).Select(a => a.CalledInAt!.Value <= a.Time.AddMinutes(ClinicLateMinutes)).ToList();
        var clinics = ClinicRooms
            .Select(c =>
            {
                var mine = booked.Where(a => a.ClinicId == c.Id).ToList();
                var late = mine.Where(a => a.Status == AppointmentStatus.Waiting && Now > a.Time).Select(a => (int)(Now - a.Time).TotalMinutes).DefaultIfEmpty(0).Max();
                var problems = ClinicProblems(c);
                return new ClinicBoardLine(c, mine.Count(a => a.Status == AppointmentStatus.Seen), mine.Count(a => a.Status != AppointmentStatus.Cancelled), late, problems.Count == 0, problems.Count == 0 ? string.Empty : $"needs {string.Join(" and ", problems)}");
            })
            .OrderBy(l => l.Clinic.Name, StringComparer.Ordinal)
            .ToList();
        var income = seen.Sum(a => a.Tariff ?? (FindSpace(a.ClinicId) is { } room && ClinicOf(room) is { } d ? d.Tariff : 0));
        return new ClinicBoard(
            booked.Count(a => a.Status != AppointmentStatus.Cancelled),
            waiting.Count,
            booked.Count(a => a.Status == AppointmentStatus.InClinic),
            seen.Count,
            waiting.Where(a => a.CheckedInAt is not null).Select(a => (int)(Now - a.CheckedInAt!.Value).TotalMinutes).DefaultIfEmpty(0).Max(),
            clinics,
            booked.Count(a => a.Status == AppointmentStatus.DidNotAttend),
            booked.Count(a => a.Status is AppointmentStatus.Booked or AppointmentStatus.Arriving),
            waits.Count == 0 ? 0 : waits.Average(),
            onTime.Count == 0 ? 1 : onTime.Count(x => x) / (double)onTime.Count,
            income);
    }

    // ------------------------------------------------------------------ the big monitor

    /// <summary>The rhythm the patient's monitor shows: the arrest rhythm, the illness's own rhythm, or sinus at their heart rate.</summary>
    public MonitorRhythm RhythmOf(Patient patient)
    {
        if (patient.InArrest)
        {
            var arrest = _arrests.LastOrDefault(a => a.PatientId == patient.Id && a.IsActive);
            return arrest?.Rhythm == ArrestRhythm.Shockable ? MonitorRhythm.VentricularFibrillation : MonitorRhythm.Asystole;
        }

        return RhythmFor(patient.Condition.Id, patient.CurrentObservations().HeartRate);
    }

    /// <summary>The rhythm for an illness at a heart rate.</summary>
    public static MonitorRhythm RhythmFor(string conditionId, int heartRate) => conditionId switch
    {
        "atrial_fibrillation" => MonitorRhythm.AtrialFibrillation,
        "supraventricular_tachycardia" when heartRate >= 130 => MonitorRhythm.Svt,
        "complete_heart_block" => MonitorRhythm.HeartBlock,
        _ when heartRate > 100 => MonitorRhythm.SinusTachycardia,
        _ when heartRate < 60 => MonitorRhythm.SinusBradycardia,
        _ => MonitorRhythm.Sinus,
    };

    public static string RhythmName(MonitorRhythm rhythm) => rhythm switch
    {
        MonitorRhythm.SinusTachycardia => "Sinus tachycardia",
        MonitorRhythm.SinusBradycardia => "Sinus bradycardia",
        MonitorRhythm.AtrialFibrillation => "Atrial fibrillation",
        MonitorRhythm.Svt => "SVT",
        MonitorRhythm.HeartBlock => "Complete heart block",
        MonitorRhythm.VentricularFibrillation => "VF: shockable",
        MonitorRhythm.Asystole => "Asystole",
        _ => "Sinus rhythm",
    };

    // ------------------------------------------------------------------ saving

    private DetailsState CaptureDetails() => new()
    {
        Journal = _journal.Values.OrderBy(j => j.ConditionId, StringComparer.Ordinal).ToList(),
        RoomLogs = _roomLogs.Values.SelectMany(l => l).ToList(),
        JournalStart = _journalStart,
    };

    private void RestoreDetails(DetailsState? state)
    {
        _journal.Clear();
        _roomLogs.Clear();
        if (state is null)
        {
            return;
        }

        _journalStart = state.JournalStart;
        foreach (var entry in state.Journal.Where(e => Database.Conditions.ContainsKey(e.ConditionId)))
        {
            _journal[entry.ConditionId] = entry;
        }

        foreach (var line in state.RoomLogs.Where(l => FindSpace(l.RoomId) is not null))
        {
            if (!_roomLogs.TryGetValue(line.RoomId, out var log))
            {
                log = new List<RoomLogEntry>();
                _roomLogs[line.RoomId] = log;
            }

            log.Add(line);
        }
    }
}

/// <summary>The illness journal and room logs in a save (absent in older saves).</summary>
public sealed class DetailsState
{
    public List<JournalEntry> Journal { get; init; } = new();

    public List<RoomLogEntry> RoomLogs { get; init; } = new();

    public DateTime? JournalStart { get; init; }
}
