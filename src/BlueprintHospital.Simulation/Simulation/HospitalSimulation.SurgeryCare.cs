using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>A patient waking up after an operation: in a recovery bay, or on the table when there was none.</summary>
public sealed class TheatreRecoveryEntry
{
    public int PatientId { get; init; }

    /// <summary>The theatre they were operated on in.</summary>
    public string TheatreId { get; init; } = string.Empty;

    /// <summary>The recovery room they wake up in; null when they wake up on the table.</summary>
    public string? RecoveryRoomId { get; init; }

    public int? Bay { get; init; }

    /// <summary>Minutes to come round, once they are in place.</summary>
    public double Minutes { get; init; }

    /// <summary>When they will be awake (null while still being wheeled through to recovery).</summary>
    public DateTime? Until { get; set; }

    /// <summary>The diagnosis the ward bed is for, and the note that goes with the request.</summary>
    public string ConditionId { get; init; } = string.Empty;

    public string Note { get; init; } = string.Empty;

    /// <summary>A complication that shows itself as they wake: bleeding, infection or critical_care.</summary>
    public string? Complication { get; init; }

    /// <summary>Bleeding: they wait in their bay for the return to theatre.</summary>
    public bool Returning { get; set; }
}

/// <summary>What a theatre session is used for.</summary>
public static class TheatreSessions
{
    /// <summary>The session takes emergencies (and any planned case if it is free).</summary>
    public const string Emergency = "emergency";

    /// <summary>Morning (08:00 to 13:00) and afternoon (13:00 to 18:00) sessions, Monday to Sunday: 14 a week.</summary>
    public const int PerWeek = 14;

    public static int SlotOf(DateTime at) => at.Hour is < 8 or >= 18 ? -1 : (DayIndex(at.DayOfWeek) * 2) + (at.Hour >= 13 ? 1 : 0);

    /// <summary>Monday first.</summary>
    public static int DayIndex(DayOfWeek day) => ((int)day + 6) % 7;
}

/// <summary>
/// Surgery, stage 2: the recovery room, surgeons' specialties and grades, the weekly theatre timetable,
/// and complications.
/// <para>
/// After the operation a porter wheels the patient to a bay in the recovery room, where a recovery nurse
/// (one for every two bays) watches them come round for 20 to 40 minutes before they go on to their ward.
/// With no recovery room, or every bay taken, they wake up on the table and the theatre can't start its
/// next case until they have.
/// </para>
/// <para>
/// Surgeons specialise in general surgery, trauma and orthopaedics, or gynaecology (gynaecologists also do
/// caesarean sections). Operating outside their specialty takes longer and goes wrong more often, as does a
/// junior surgeon, a tired one, or a patient who waited too long. Complications show as the patient wakes:
/// bleeding (straight back to theatre), a wound infection (a longer stay) or the need for critical care.
/// </para>
/// <para>
/// Each theatre has a weekly timetable of morning and afternoon sessions. An emergency session takes
/// anyone; a session given to a department's planned list takes only that department's planned patients,
/// plus Immediate cases that can't wait. Evenings and nights are always for emergencies.
/// </para>
/// </summary>
public sealed partial class HospitalSimulation
{
    public const double MinRecoveryMinutes = 20;
    public const double MaxRecoveryMinutes = 40;

    /// <summary>Recovery bays one recovery nurse can watch.</summary>
    public const int BaysPerRecoveryNurse = 2;

    /// <summary>Minutes to go back in and stop bleeding.</summary>
    public const int ReturnToTheatreMinutes = 45;

    /// <summary>Extra hours on the ward with a wound infection.</summary>
    public const double InfectionExtraHours = 48;

    /// <summary>The specialties a surgeon can have (department ids).</summary>
    public static readonly IReadOnlyList<string> SurgicalSpecialties = new[] { "general_surgery", "trauma_orthopaedics", "gynaecology" };

    private readonly Dictionary<int, TheatreRecoveryEntry> _theatreRecovery = new();
    private readonly Dictionary<string, string[]> _theatreSessions = new();
    private GameRandom? _surgeryRandom;

    /// <summary>Complications have their own dice, so they don't change what else happens.</summary>
    private GameRandom SurgeryRandom => _surgeryRandom ??= new GameRandom(_options.Seed + 15_485_863);

    /// <summary>Everyone waking up after an operation.</summary>
    public IReadOnlyCollection<TheatreRecoveryEntry> TheatreRecovery => _theatreRecovery.Values;

    /// <summary>The recovery entry for a patient, while they are coming round.</summary>
    public TheatreRecoveryEntry? RecoveryOf(Patient patient) => _theatreRecovery.GetValueOrDefault(patient.Id);

    // ---------------------------------------------------------------- specialties and grades

    /// <summary>The surgical specialty that does a department's operations (gynaecologists do caesareans).</summary>
    public static string SpecialtyFor(string? department) => department switch
    {
        null => "general_surgery",
        "maternity" => "gynaecology",
        _ => department,
    };

    /// <summary>"General surgeon", "Orthopaedic surgeon" or "Gynaecologist".</summary>
    public static string SpecialtyTitle(string? specialty) => specialty switch
    {
        "general_surgery" => "General surgeon",
        "trauma_orthopaedics" => "Orthopaedic surgeon",
        "gynaecology" => "Gynaecologist",
        _ => "Surgeon",
    };

    /// <summary>A job title with the surgeon's specialty, e.g. "Surgical registrar · Orthopaedic surgeon".</summary>
    public static string JobTitle(string grade, string? specialty) => specialty is null ? grade : $"{grade} · {SpecialtyTitle(specialty)}";

    /// <summary>Whether the surgeon specialises in this department's operations (surgeons with no specialty do everything).</summary>
    public static bool InSpecialty(StaffMember surgeon, string? department) =>
        surgeon.Specialty is null || department is null || surgeon.Specialty == SpecialtyFor(department);

    /// <summary>Candidates' specialties take turns, so every list has a mix without using up any dice.</summary>
    private string PickSpecialty() => SurgicalSpecialties[_nextCandidateId % SurgicalSpecialties.Count];

    /// <summary>A surgeon hired straight into a theatre specialises in what it does that has the fewest surgeons.</summary>
    private string SpecialtyForRoom(string? roomId)
    {
        if (roomId is null || FindSpace(roomId) is not { Kind: CareSpaceKind.Theatre } theatre || theatre.AllowedDepartments.Count == 0)
        {
            return SurgicalSpecialties[0];
        }

        return theatre.AllowedDepartments.Select(SpecialtyFor).Distinct()
            .OrderBy(s => _staff.Count(m => m.BaseRoomId == roomId && m.Specialty == s))
            .ThenBy(s => s, StringComparer.Ordinal)
            .First();
    }

    /// <summary>How long a surgeon takes compared with the operation's usual time: consultants are quickest.</summary>
    public static double OperatingPace(StaffMember surgeon, string? department)
    {
        var grade = surgeon.Grade switch
        {
            "Core surgical trainee" => 1.25,
            "Surgical registrar" => 1.1,
            _ => 1.0,
        };
        return grade * (InSpecialty(surgeon, department) ? 1.0 : 1.3);
    }

    /// <summary>The chance an operation goes wrong, from the surgeon, how tired they are, the patient's wait and the operation.</summary>
    public double ComplicationRisk(StaffMember surgeon, Patient patient, OperationDefinition operation)
    {
        var grade = surgeon.Grade switch
        {
            "Core surgical trainee" => 0.12,
            "Surgical registrar" => 0.08,
            "Consultant surgeon" => 0.05,
            _ => 0.06,
        };
        var department = patient.WorkingDiagnosis is { } d ? SurgeryDepartment(d.ConditionId) : null;
        return grade
               * (InSpecialty(surgeon, department) ? 1.0 : 1.6)
               * (1 + (surgeon.Fatigue * 0.8))
               * (patient.SurgeryBreached ? 1.4 : 1.0)
               * (operation.Major ? 1.5 : 1.0);
    }

    /// <summary>
    /// Sets what happens after the next operation instead of rolling for it ("bleeding", "infection", "critical_care",
    /// or "none"), for the cheat console and tests.
    /// </summary>
    public string? NextComplication { get; set; }

    private string? RollComplication(StaffMember surgeon, Patient patient, OperationDefinition operation)
    {
        if (NextComplication is { } forced)
        {
            NextComplication = null;
            return forced == "none" ? null : forced;
        }

        var risk = ComplicationRisk(surgeon, patient, operation);
        var roll = SurgeryRandom.NextDouble();
        var kind = SurgeryRandom.NextDouble();
        if (roll >= risk)
        {
            return null;
        }

        return kind < 0.3 ? "bleeding" : kind < 0.8 || !(operation.Major || patient.Age >= 70) ? "infection" : "critical_care";
    }

    /// <summary>A complication in plain words.</summary>
    public static string ComplicationName(string? complication) => complication switch
    {
        "bleeding" => "Bleeding: back to theatre",
        "infection" => "Wound infection",
        "critical_care" => "Needed critical care",
        _ => string.Empty,
    };

    // ---------------------------------------------------------------- recovery

    /// <summary>Recovery bays one room can use now: one recovery nurse on duty for every two.</summary>
    public int RecoveryBaysOpen(CareSpace room) =>
        Math.Min(room.Beds.Count, _staff.Count(s => s.BaseRoomId == room.Id && s.IsOnDuty && s.Role == StaffRole.Nurse) * BaysPerRecoveryNurse);

    private (CareSpace Room, int Bay)? FreeRecoveryBay()
    {
        foreach (var room in SpacesOfKind(CareSpaceKind.TheatreRecovery).OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            if (room.Beds.Count(b => b.OccupantId is not null) >= RecoveryBaysOpen(room))
            {
                continue;
            }

            var bay = room.Beds.ToList().FindIndex(b => b.OccupantId is null);
            if (bay >= 0)
            {
                return (room, bay);
            }
        }

        return null;
    }

    /// <summary>After the operation: off to a recovery bay to wake up, or wake up on the table.</summary>
    private void StartTheatreRecovery(Patient patient, StaffMember surgeon, ConditionDefinition done, string note, string? complication)
    {
        var theatre = surgeon.BaseRoomId is { } id ? FindSpace(id) : null;
        var minutes = MinRecoveryMinutes + (patient.Id * 7 % (int)(MaxRecoveryMinutes - MinRecoveryMinutes + 1));
        if (FreeRecoveryBay() is { } free)
        {
            free.Room.Beds[free.Bay].OccupantId = patient.Id;
            _theatreRecovery[patient.Id] = new TheatreRecoveryEntry
            {
                PatientId = patient.Id, TheatreId = theatre?.Id ?? string.Empty, RecoveryRoomId = free.Room.Id, Bay = free.Bay, Minutes = minutes,
                ConditionId = done.Id, Note = note, Complication = complication,
            };
            patient.Mobility = Mobility.Trolley;
            WalkTo(patient, free.Room.Beds[free.Bay].PatientSpot);
            patient.Log(Now, $"Wheeled through to {free.Room.Name}, bay {free.Bay + 1}, to wake up.");
            return;
        }

        _theatreRecovery[patient.Id] = new TheatreRecoveryEntry
        {
            PatientId = patient.Id, TheatreId = theatre?.Id ?? string.Empty, Minutes = minutes, Until = Now.AddMinutes(minutes),
            ConditionId = done.Id, Note = note, Complication = complication,
        };
        if (theatre is not null)
        {
            var until = Now.AddMinutes(minutes + TheatreTurnaroundMinutes);
            if (!_theatreCleanUntil.TryGetValue(theatre.Id, out var clean) || clean < until)
            {
                _theatreCleanUntil[theatre.Id] = until;
            }
        }

        var why = SpacesOfKind(CareSpaceKind.TheatreRecovery).Any() ? "every recovery bay with a nurse is taken" : "there is no recovery room";
        patient.Log(Now, $"Waking up on the table in {theatre?.Name ?? "theatre"}: {why}, so the theatre can't start its next case for about {minutes:0} minutes.");
        if (theatre is not null)
        {
            Notifications.Add(Now, NotificationLevel.Info, $"{theatre.Name} is held up: {patient.FullName} is waking up on the table because {why}.", patient.Id, theatre.Id);
        }
    }

    /// <summary>Whether a patient is still waking up in (or waiting to leave) this theatre.</summary>
    private bool TheatreHeldBy(CareSpace theatre, Patient? except = null) =>
        _theatreRecovery.Values.Any(r => r.TheatreId == theatre.Id && r.PatientId != except?.Id && FindPatient(r.PatientId) is { } p && theatre.Rect.Contains(p.Tile));

    /// <summary>Whether someone is waking up on this theatre's table because no recovery bay was free.</summary>
    private bool WakingOnTable(CareSpace theatre) =>
        _theatreRecovery.Values.Any(r => r.TheatreId == theatre.Id && r.RecoveryRoomId is null && !r.Returning && FindPatient(r.PatientId) is { } p && theatre.Rect.Contains(p.Tile));

    private void UpdateTheatreRecovery()
    {
        foreach (var entry in _theatreRecovery.Values.ToList())
        {
            var patient = FindPatient(entry.PatientId);
            var room = entry.RecoveryRoomId is { } roomId ? FindSpace(roomId) : null;
            var bay = room is not null && entry.Bay is { } index && index < room.Beds.Count ? room.Beds[index] : null;
            if (patient is null || !patient.IsAlive || !patient.IsInDepartment)
            {
                FreeBay(bay, entry.PatientId);
                _theatreRecovery.Remove(entry.PatientId);
                continue;
            }

            if (entry.Returning)
            {
                // Waiting in the bay to go back to theatre: the bay is free once they have gone.
                if (bay is null || patient.IsMoving || patient.Tile != bay.PatientSpot)
                {
                    FreeBay(bay, entry.PatientId);
                    _theatreRecovery.Remove(entry.PatientId);
                }

                continue;
            }

            if (entry.Until is null)
            {
                if (bay is null)
                {
                    entry.Until = Now.AddMinutes(entry.Minutes);
                }
                else if (!patient.IsMoving && patient.PendingMove is null && patient.Tile == bay.PatientSpot)
                {
                    entry.Until = Now.AddMinutes(entry.Minutes);
                    var nurse = _staff.FirstOrDefault(s => s.BaseRoomId == room!.Id && s.IsOnDuty && s.Role == StaffRole.Nurse);
                    patient.Log(Now, $"In recovery{(nurse is null ? string.Empty : $" with {nurse.Name}")}: oxygen, pain relief and observations every few minutes while they come round.");
                }

                continue;
            }

            if (Now < entry.Until)
            {
                continue;
            }

            FinishTheatreRecovery(entry, patient, bay);
        }
    }

    private void FreeBay(WardBed? bay, int patientId)
    {
        if (bay is not null && bay.OccupantId == patientId)
        {
            bay.OccupantId = null;
        }
    }

    private void FinishTheatreRecovery(TheatreRecoveryEntry entry, Patient patient, WardBed? bay)
    {
        var condition = Database.Conditions.GetValueOrDefault(entry.ConditionId) ?? patient.Condition;
        if (entry.Complication == "bleeding")
        {
            entry.Returning = true;
            RecordComplication(patient, entry.Complication);
            patient.Log(Now, "Bleeding from the operation site as they woke up: going straight back to theatre to stop it.", important: true);
            Notifications.Add(Now, NotificationLevel.Warning, $"{patient.FullName} is bleeding after their operation and needs to go back to theatre.", patient.Id);
            patient.ReturningToTheatre = true;
            AddTask(TaskKind.Surgery, StaffRole.Surgeon, patient, ReturnToTheatreMinutes, "Return to theatre to stop bleeding", entry.ConditionId);
            patient.SurgeryUrgency = SurgeryUrgency.Immediate;
            patient.ConsentAt = Now;
            patient.GroupAndSaveAt = Now;
            if (bay is null)
            {
                _theatreRecovery.Remove(entry.PatientId);
            }

            return;
        }

        FreeBay(bay, entry.PatientId);
        _theatreRecovery.Remove(entry.PatientId);
        switch (entry.Complication)
        {
            case "infection":
                RecordComplication(patient, entry.Complication);
                patient.ExtraStayHours += InfectionExtraHours;
                patient.Severity = Math.Min(1, patient.Severity + 0.08);
                patient.Log(Now, "The wound looks infected: antibiotics started, and they will need a longer stay on the ward.", important: true);
                break;
            case "critical_care":
                RecordComplication(patient, entry.Complication);
                var level = OpenIntensiveCareUnits.Any() ? 3 : HasHighDependencyBeds ? 2 : 0;
                patient.CareLevel = Math.Max(patient.CareLevel, level);
                patient.Severity = Math.Min(1, patient.Severity + 0.15);
                patient.Log(Now, level > 0
                    ? $"Slow to come round, with low blood pressure: needs {LevelName(level)} after the operation."
                    : "Slow to come round, with low blood pressure, but there is no critical care bed: back to the ward with close observations.", important: true);
                break;
            default:
                patient.Log(Now, "Awake and comfortable after the operation.");
                break;
        }

        ReturnFromServiceRoom(patient);
        RequestWardBed(patient, condition, entry.Note);
    }

    private void RecordComplication(Patient patient, string complication)
    {
        RollSurgeryDay();
        _surgeryDay.Complications++;
        patient.Complication = complication;
        if (complication != "bleeding")
        {
            Notifications.Add(Now, NotificationLevel.Warning, $"Complication after {patient.FullName}'s operation: {ComplicationName(complication).ToLowerInvariant()}.", patient.Id);
        }
    }

    // ---------------------------------------------------------------- theatre sessions

    /// <summary>What each session of a theatre's week is for (14 sessions, Monday morning first).</summary>
    public IReadOnlyList<string> TheatreTimetable(CareSpace theatre) =>
        _theatreSessions.TryGetValue(theatre.Id, out var slots) ? slots : Enumerable.Repeat(TheatreSessions.Emergency, TheatreSessions.PerWeek).ToArray();

    /// <summary>What a theatre is being used for at a time (emergencies outside the morning and afternoon sessions).</summary>
    public string TheatreSessionAt(CareSpace theatre, DateTime at) =>
        TheatreSessions.SlotOf(at) is var slot && slot >= 0 ? TheatreTimetable(theatre)[slot] : TheatreSessions.Emergency;

    /// <summary>Gives a session to a department's planned list, or back to emergencies.</summary>
    public void SetTheatreSession(string theatreId, int slot, string use)
    {
        if (FindSpace(theatreId) is not { Kind: CareSpaceKind.Theatre } theatre || slot is < 0 or >= TheatreSessions.PerWeek)
        {
            return;
        }

        if (use != TheatreSessions.Emergency && !theatre.AllowedDepartments.Contains(use))
        {
            return;
        }

        if (!_theatreSessions.TryGetValue(theatreId, out var slots))
        {
            slots = Enumerable.Repeat(TheatreSessions.Emergency, TheatreSessions.PerWeek).ToArray();
            _theatreSessions[theatreId] = slots;
        }

        slots[slot] = use;
        if (slots.All(s => s == TheatreSessions.Emergency))
        {
            _theatreSessions.Remove(theatreId);
        }
    }

    /// <summary>Whether the theatre's session now takes this case.</summary>
    private bool SessionTakes(CareSpace theatre, ClinicalTask task)
    {
        var use = TheatreSessionAt(theatre, Now);
        if (use == TheatreSessions.Emergency)
        {
            return true;
        }

        var department = SurgeryDepartment(task.Detail);
        var patient = FindPatient(task.PatientId);
        var planned = task.Detail is { } id && Database.Conditions.TryGetValue(id, out var condition) && condition.IsPlanned;
        return (planned && department == use) || (patient?.SurgeryUrgency ?? UrgencyOf(task.Detail)) == SurgeryUrgency.Immediate;
    }

    /// <summary>A session name for the timetable: "Emergencies" or the department's name.</summary>
    public string SessionName(string use) => use == TheatreSessions.Emergency ? "Emergencies" : DepartmentName(use);

    /// <summary>Drops sessions given to departments a theatre no longer takes, and timetables of theatres that have gone.</summary>
    private void TidyTheatreSessions()
    {
        foreach (var (id, slots) in _theatreSessions.ToList())
        {
            if (FindSpace(id) is not { Kind: CareSpaceKind.Theatre } theatre)
            {
                _theatreSessions.Remove(id);
                continue;
            }

            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i] != TheatreSessions.Emergency && !theatre.AllowedDepartments.Contains(slots[i]))
                {
                    slots[i] = TheatreSessions.Emergency;
                }
            }
        }
    }

    /// <summary>A recovery nurse stands by an occupied bay (sharing them out), or by the door when none is.</summary>
    private GridCoord RecoveryHome(StaffMember member, CareSpace room)
    {
        var occupied = room.Beds.Where(b => b.OccupantId is not null).ToList();
        if (occupied.Count == 0)
        {
            return room.StaffSpot;
        }

        var nurses = _staff.Where(s => s.BaseRoomId == room.Id && s.IsOnDuty).OrderBy(s => s.Id).ToList();
        var index = Math.Max(0, nurses.IndexOf(member));
        return occupied[index % occupied.Count].StaffSpot;
    }
}
