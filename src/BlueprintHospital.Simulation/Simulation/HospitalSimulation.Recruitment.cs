using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>Where a member of staff can be based: A&amp;E (no room), a ward or a service room.</summary>
public sealed record StaffPosting(string? RoomId, string Name);

/// <summary>How many of one kind of staff a place needs to run well, and how many it has.</summary>
public sealed record StaffingNeed(string Place, string? RoomId, StaffRole Role, int Recommended, int Have, StaffShift Shift = StaffShift.Day)
{
    public int Short => Math.Max(0, Recommended - Have);
}

/// <summary>
/// Hiring and letting staff go. Rooms come empty: the player hires everyone from a list of
/// candidates that is refreshed every morning. A recommended headcount for each place is a guide
/// only. Specialists (radiographers, surgeons, pharmacists) work in a room of their kind; nurses and
/// doctors work in A&amp;E or on a ward; porters and receptionists work across the hospital.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>A fresh list of candidates arrives every this many game hours (00:00, 04:00, 08:00 ...).</summary>
    public const int CandidatesEveryHours = 4;

    /// <summary>Extra candidates on top of one of each kind of staff, for the kinds the hospital is short of.</summary>
    public const int ExtraCandidatesForShortages = 4;

    /// <summary>Every kind of staff that can be recruited. Each list of candidates has at least one of each.</summary>
    public static IReadOnlyList<StaffRole> RecruitableRoles { get; } =
        Enum.GetValues<StaffRole>().Where(r => StaffGrades.For(r).Any()).ToArray();

    private readonly List<JobCandidate> _candidates = new();
    private int _nextCandidateId = 1;
    private DateTime? _candidatesDate;
    private GameRandom? _recruitRandom;

    /// <summary>Candidates have their own dice, so a new list every few hours doesn't change what happens to patients.</summary>
    private GameRandom RecruitRandom => _recruitRandom ??= new GameRandom(_options.Seed + 104_729);

    public IReadOnlyList<JobCandidate> Candidates => _candidates;

    /// <summary>When the current list of candidates arrived.</summary>
    public DateTime? CandidatesDate => _candidatesDate;

    /// <summary>The kinds of room a specialist works in.</summary>
    public static IReadOnlyList<CareSpaceKind> RoomKindsFor(StaffRole role) => role switch
    {
        StaffRole.Radiographer => new[] { CareSpaceKind.XRay, CareSpaceKind.CT },
        StaffRole.Surgeon => new[] { CareSpaceKind.Theatre },
        StaffRole.Pharmacist => new[] { CareSpaceKind.Pharmacy },
        StaffRole.CardiacPhysiologist => new[] { CareSpaceKind.Echo, CareSpaceKind.CathLab },
        StaffRole.Neurophysiologist => new[] { CareSpaceKind.Eeg },
        StaffRole.Sonographer => new[] { CareSpaceKind.Ultrasound },
        StaffRole.Endoscopist => new[] { CareSpaceKind.Endoscopy },
        StaffRole.PlasterTechnician => new[] { CareSpaceKind.PlasterRoom },
        StaffRole.Physiotherapist => new[] { CareSpaceKind.PhysioGym },
        StaffRole.Anaesthetist => new[] { CareSpaceKind.Theatre },
        StaffRole.OutreachNurse => new[] { CareSpaceKind.OutreachOffice },
        _ => Array.Empty<CareSpaceKind>(),
    };

    /// <summary>Who runs a service room.</summary>
    public static StaffRole RoleForRoom(CareSpaceKind kind) => kind switch
    {
        CareSpaceKind.Theatre => StaffRole.Surgeon,
        CareSpaceKind.Pharmacy => StaffRole.Pharmacist,
        CareSpaceKind.Echo or CareSpaceKind.CathLab => StaffRole.CardiacPhysiologist,
        CareSpaceKind.Eeg => StaffRole.Neurophysiologist,
        CareSpaceKind.Ultrasound => StaffRole.Sonographer,
        CareSpaceKind.Endoscopy => StaffRole.Endoscopist,
        CareSpaceKind.PlasterRoom => StaffRole.PlasterTechnician,
        CareSpaceKind.PhysioGym => StaffRole.Physiotherapist,
        CareSpaceKind.OutreachOffice => StaffRole.OutreachNurse,
        _ => StaffRole.Radiographer,
    };

    private static bool IsSpecialist(StaffRole role) => RoomKindsFor(role).Count > 0;

    /// <summary>Whether someone works in the room, so it is open for business.</summary>
    public bool IsStaffed(CareSpace room) => room.Kind == CareSpaceKind.Theatre
        ? _staff.Any(s => s.BaseRoomId == room.Id && s.Role == StaffRole.Surgeon) && _staff.Any(s => s.BaseRoomId == room.Id && s.Role == StaffRole.Anaesthetist)
        : _staff.Any(s => s.BaseRoomId == room.Id);

    /// <summary>Where someone in this role could be based.</summary>
    public IReadOnlyList<StaffPosting> PostingsFor(StaffRole role)
    {
        if (IsSpecialist(role))
        {
            var rooms = RoomKindsFor(role).SelectMany(SpacesOfKind).Select(r => new StaffPosting(r.Id, r.Name)).ToList();
            if (role == StaffRole.Anaesthetist)
            {
                // Anaesthetists also run intensive care units.
                rooms.AddRange(Wards.Where(w => UnitOf(w) == WardUnit.IntensiveCare).Select(w => new StaffPosting(w.Id, w.Name)));
            }

            return rooms.Count > 0 ? rooms : new[] { new StaffPosting(null, "No room yet") };
        }

        var postings = new List<StaffPosting> { new(null, role is StaffRole.Porter or StaffRole.Receptionist ? "Whole hospital" : "A&E") };
        if (role is StaffRole.Nurse or StaffRole.Doctor or StaffRole.HealthcareAssistant)
        {
            postings.AddRange(Wards.Select(w => new StaffPosting(w.Id, w.Name)));
        }

        if (role == StaffRole.Doctor)
        {
            postings.AddRange(ClinicRooms.OrderBy(c => c.Id, StringComparer.Ordinal).Select(c => new StaffPosting(c.Id, c.Name)));
        }

        if (role == StaffRole.Receptionist)
        {
            postings.AddRange(SpacesOfKind(CareSpaceKind.OutpatientReception).OrderBy(c => c.Id, StringComparer.Ordinal).Select(c => new StaffPosting(c.Id, c.Name)));
        }

        return postings;
    }

    /// <summary>
    /// A guide to the staff the hospital needs, place by place and shift by shift (day and night).
    /// Clinics and Outpatients reception only need a day team. The player is free to ignore it.
    /// </summary>
    public IReadOnlyList<StaffingNeed> RecommendedStaffing() =>
        RecommendedForShift(StaffShift.Day).Concat(RecommendedForShift(StaffShift.Night).Where(n => !IsDayOnly(n.RoomId))).ToList();

    private bool IsDayOnly(string? roomId) =>
        roomId is { } id && FindSpace(id) is { Kind: CareSpaceKind.Clinic or CareSpaceKind.OutpatientReception };

    private IEnumerable<StaffingNeed> RecommendedForShift(StaffShift shift)
    {
        int Have(StaffRole role, string? roomId) => _staff.Count(s => s.Role == role && s.BaseRoomId == roomId && s.Shift == shift);

        var treatment = _spaces.Values.Count(s => s.IsTreatmentSpace);
        var triage = SpacesOfKind(CareSpaceKind.Triage).Count();
        var needs = new List<StaffingNeed>
        {
            new("A&E", null, StaffRole.Receptionist, 1, Have(StaffRole.Receptionist, null)),
            new("A&E", null, StaffRole.Nurse, Math.Max(2, triage + (int)Math.Ceiling(treatment / 4.0)), Have(StaffRole.Nurse, null)),
            new("A&E", null, StaffRole.Doctor, Math.Max(2, (int)Math.Ceiling(treatment / 5.0)), Have(StaffRole.Doctor, null)),
            new("A&E", null, StaffRole.HealthcareAssistant, Math.Max(1, (int)Math.Ceiling(treatment / 6.0)), Have(StaffRole.HealthcareAssistant, null)),
        };

        var movesTo = _spaces.Values.Count(s => s.IsWard || (s.IsServiceRoom && s.Kind != CareSpaceKind.Pharmacy));
        needs.Add(new("Whole hospital", null, StaffRole.Porter, 1 + (int)Math.Ceiling(movesTo / 2.0), Have(StaffRole.Porter, null)));

        foreach (var ward in Wards)
        {
            var unit = UnitOf(ward);
            var perNurse = unit switch
            {
                WardUnit.IntensiveCare => PatientsPerIntensiveCareNurse,
                WardUnit.HighDependency => PatientsPerHighDependencyNurse,
                _ => PatientsPerWardNurse,
            };
            needs.Add(new(ward.Name, ward.Id, StaffRole.Nurse, Math.Max(1, (ward.Beds.Count + perNurse - 1) / perNurse), Have(StaffRole.Nurse, ward.Id)));
            var doctor = unit == WardUnit.IntensiveCare ? StaffRole.Anaesthetist : StaffRole.Doctor;
            needs.Add(new(ward.Name, ward.Id, doctor, 1, Have(doctor, ward.Id)));
            needs.Add(new(ward.Name, ward.Id, StaffRole.HealthcareAssistant, 1, Have(StaffRole.HealthcareAssistant, ward.Id)));
        }

        foreach (var room in _spaces.Values.Where(s => s.IsServiceRoom).OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            var role = RoleForRoom(room.Kind);
            needs.Add(new(room.Name, room.Id, role, 1, Have(role, room.Id)));
            if (room.Kind == CareSpaceKind.Theatre)
            {
                needs.Add(new(room.Name, room.Id, StaffRole.Anaesthetist, 1, Have(StaffRole.Anaesthetist, room.Id)));
            }
        }

        foreach (var reception in SpacesOfKind(CareSpaceKind.OutpatientReception).OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            needs.Add(new(reception.Name, reception.Id, StaffRole.Receptionist, 1, Have(StaffRole.Receptionist, reception.Id)));
        }

        foreach (var clinic in ClinicRooms.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            needs.Add(new(clinic.Name, clinic.Id, StaffRole.Doctor, 1, Have(StaffRole.Doctor, clinic.Id)));
        }

        return needs.Select(n => n with { Shift = shift });
    }

    // ---------------------------------------------------------------- candidates

    /// <summary>
    /// A fresh list of candidates arrives every <see cref="CandidatesEveryHours"/> game hours; anyone not
    /// hired by then has gone elsewhere.
    /// </summary>
    private void RefreshCandidatesIfDue()
    {
        var slot = Now.Date.AddHours(Now.Hour / CandidatesEveryHours * CandidatesEveryHours);
        if (_candidatesDate is { } date && date >= slot)
        {
            return;
        }

        RefreshCandidates(slot);
        if (Now == slot)
        {
            Notifications.Add(Now, NotificationLevel.Info, $"{_candidates.Count} new job candidates are waiting in Recruitment.");
        }
    }

    private void RefreshCandidates(DateTime arrived)
    {
        _candidates.Clear();
        _candidatesDate = arrived;

        // One of every kind of staff, whether or not there is a room for them yet.
        foreach (var role in RecruitableRoles)
        {
            _candidates.Add(MakeCandidate(PickGrade(StaffGrades.For(role).ToList())));
        }

        // Then a second choice for the kinds of staff the hospital is short of.
        var short_ = RecommendedStaffing().Where(n => n.Short > 0).Select(n => n.Role).Distinct();
        foreach (var role in short_.Take(ExtraCandidatesForShortages))
        {
            _candidates.Add(MakeCandidate(PickGrade(StaffGrades.For(role).ToList())));
        }
    }

    private StaffGrades.Grade PickGrade(IReadOnlyList<StaffGrades.Grade> grades)
    {
        var roll = RecruitRandom.Range(0.0, grades.Sum(g => g.Weight));
        foreach (var grade in grades)
        {
            roll -= grade.Weight;
            if (roll <= 0)
            {
                return grade;
            }
        }

        return grades[^1];
    }

    private JobCandidate MakeCandidate(StaffGrades.Grade grade)
    {
        var (first, surname) = PickName();
        var skill = Math.Round(RecruitRandom.Range(grade.MinSkill, grade.MaxSkill), 2);
        var years = RecruitRandom.Range(grade.MinYears, grade.MaxYears + 1);
        var speed = Math.Round(RecruitRandom.Range(0.85, 1.15), 2);

        // Better people ask for a little more.
        var range = Math.Max(0.01, grade.MaxSkill - grade.MinSkill);
        var salary = (int)(Math.Round(grade.Salary * (0.95 + (0.1 * (skill - grade.MinSkill) / range)) / 100) * 100);
        return new JobCandidate
        {
            Id = _nextCandidateId++,
            Name = $"{(grade.Role is StaffRole.Doctor or StaffRole.Surgeon or StaffRole.Endoscopist or StaffRole.Anaesthetist ? "Dr " : string.Empty)}{first} {surname}",
            Role = grade.Role,
            Grade = grade.Title,
            Skill = skill,
            Speed = speed,
            YearsExperience = years,
            Age = 22 + years + RecruitRandom.Range(0, 8),
            Salary = salary,
        };
    }

    private (string First, string Surname) PickName()
    {
        var names = Database.Names.Values.First();
        var female = RecruitRandom.Chance(0.6);
        return (RecruitRandom.Pick(female ? names.FemaleFirstNames : names.MaleFirstNames), RecruitRandom.Pick(names.Surnames));
    }

    // ---------------------------------------------------------------- hiring and letting go

    /// <summary>
    /// Takes on a candidate, based at <paramref name="roomId"/> (null for A&amp;E or the whole hospital), on
    /// <paramref name="shift"/> (by default whichever shift has fewer of their kind there).
    /// </summary>
    public StaffMember? Hire(int candidateId, string? roomId, StaffShift? shift = null)
    {
        if (_candidates.FirstOrDefault(c => c.Id == candidateId) is not { } candidate)
        {
            return null;
        }

        if (!PostingsFor(candidate.Role).Any(p => p.RoomId == roomId))
        {
            roomId = PostingsFor(candidate.Role)[0].RoomId;
        }

        _candidates.Remove(candidate);
        var member = new StaffMember
        {
            Id = _nextStaffId++,
            Name = candidate.Name,
            Role = candidate.Role,
            Grade = candidate.Grade,
            DepartmentId = EmergencyDepartmentLayout.Department,
            BaseRoomId = roomId,
            Skill = candidate.Skill,
            Speed = candidate.Speed,
            YearsExperience = candidate.YearsExperience,
            Age = candidate.Age,
            Salary = candidate.Salary,
            Fatigue = Random.Range(0.0, 0.1),
            Shift = shift ?? ShorterShift(candidate.Role, roomId),
        };
        Join(member);
        var place = roomId is { } id && FindSpace(id) is { } room ? room.Name : PostingsFor(member.Role)[0].Name;
        var starts = member.Shift == CurrentShift ? "starting now" : $"starting at {NextStart(member.Shift):HH:mm}";
        Notifications.Add(Now, NotificationLevel.Info, $"{member.Name} ({member.Grade}) has joined the {ShiftName(member.Shift)} team: {place}, {starts}.");
        return member;
    }

    /// <summary>Takes on a new member of staff straight away, without a candidate (starting teams, scenarios and tests).</summary>
    public StaffMember HireStaff(StaffRole role, string? roomId = null, double? skill = null, StaffShift? shift = null)
    {
        var grades = StaffGrades.For(role).ToList();
        var grade = skill is { } s ? StaffGrades.Of(role, s) : grades[0];
        var candidate = MakeCandidate(grade);
        var member = new StaffMember
        {
            Id = _nextStaffId++,
            Name = candidate.Name,
            Role = role,
            Grade = grade.Title,
            DepartmentId = EmergencyDepartmentLayout.Department,
            BaseRoomId = roomId,
            Skill = skill ?? candidate.Skill,
            Speed = 1.0,
            YearsExperience = candidate.YearsExperience,
            Age = candidate.Age,
            Salary = grade.Salary,
            Fatigue = Random.Range(0.0, 0.15),
            Shift = shift ?? CurrentShift,
        };
        Join(member);
        return member;
    }

    private void Join(StaffMember member)
    {
        _staff.Add(member);
        if (member.BaseRoomId is null && IsSpecialist(member.Role))
        {
            PostSpecialists();
        }

        member.PlaceAt(HomeTile(member));
        if (member.Shift == CurrentShift)
        {
            PlanBreaks(member, CurrentShiftStart);
        }
        else
        {
            member.Activity = StaffActivity.OffDuty;
            member.StatusText = OffDutyText(member);
        }
    }

    /// <summary>Lets a member of staff go. Whatever they were doing goes back on the board for someone else.</summary>
    public bool Fire(int staffId)
    {
        if (FindStaff(staffId) is not { } member)
        {
            return false;
        }

        if (member.CurrentTaskId is { } taskId && _tasks.TryGetValue(taskId, out var task))
        {
            Unassign(task, member);
        }

        foreach (var patient in _patients.Values.Where(p => p.PushedByStaffId == member.Id))
        {
            EndPush(patient);
        }

        _staff.Remove(member);
        Notifications.Add(Now, NotificationLevel.Info, $"{member.Name} ({member.Grade}) has left the hospital.");
        CancelUnavailableWork();
        return true;
    }

    /// <summary>Puts a task back on the board for someone else to pick up.</summary>
    private void Unassign(ClinicalTask task, StaffMember member)
    {
        if (task.Kind == TaskKind.MovePatient && task.Started)
        {
            // The patient is already on the way; they get there without this porter.
            _tasks.Remove(task.Id);
        }

        task.AssignedStaffId = null;
        task.Started = false;
        if (FindPatient(task.PatientId) is { } patient && patient.BusyWithTaskId == task.Id)
        {
            patient.BusyWithTaskId = null;
        }

        if (task.SpaceId is { } spaceId && FindSpace(spaceId) is { } room && room.ReservedByStaffId == member.Id)
        {
            room.ReservedByStaffId = null;
        }

        member.CurrentTaskId = null;
    }

    // ---------------------------------------------------------------- rooms coming and going

    /// <summary>
    /// Keeps everyone based somewhere that exists. When a room is demolished its staff move to
    /// another room of the same kind, or to A&amp;E (nurses and doctors), or wait for a new room
    /// (specialists). Specialists without a room are posted to a new room of their kind.
    /// </summary>
    private void SyncRoomStaff()
    {
        foreach (var member in _staff.Where(s => s.BaseRoomId is { } id && FindSpace(id) is null).ToList())
        {
            if (member.CurrentTaskId is { } taskId && _tasks.TryGetValue(taskId, out var task))
            {
                Unassign(task, member);
            }

            member.BaseRoomId = null;
            if (member.Role is StaffRole.Nurse or StaffRole.Doctor)
            {
                var ward = Wards.FirstOrDefault(w => !_staff.Any(s => s.BaseRoomId == w.Id && s.Role == member.Role));
                member.BaseRoomId = ward?.Id;
            }
        }

        PostSpecialists();
        CancelUnavailableWork();
    }

    private void PostSpecialists()
    {
        foreach (var member in _staff.Where(s => s.BaseRoomId is null && IsSpecialist(s.Role)).ToList())
        {
            var room = RoomKindsFor(member.Role).SelectMany(SpacesOfKind)
                .OrderBy(r => _staff.Count(s => s.BaseRoomId == r.Id))
                .FirstOrDefault();
            if (room is not null)
            {
                member.BaseRoomId = room.Id;
            }
        }
    }

    /// <summary>A ward with nobody of the task's role of its own at work is covered by the A&amp;E team.</summary>
    private bool CoveredByEmergencyTeam(ClinicalTask task, StaffMember member) =>
        member.BaseRoomId is null && TeamFor(task) is { } ward
        && !_staff.Any(s => s.BaseRoomId == ward && s.IsOnDuty && (s.Role == task.Role || (task.Role == StaffRole.Doctor && s.Role == StaffRole.Anaesthetist)));
}
