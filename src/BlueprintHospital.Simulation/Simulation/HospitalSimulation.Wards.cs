using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// Wards: admitting patients from A&amp;E to a bed, the ward team who look after them, and discharge home.
/// A patient who needs admitting waits in their A&amp;E cubicle until a bed is free (as in a real hospital,
/// a full ward blocks A&amp;E). If there is no ward, or no bed comes free in time, they are transferred to
/// a neighbouring hospital instead.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>How long a patient waits in A&amp;E for a bed before being sent to another hospital.</summary>
    public static readonly TimeSpan MaxWaitForBed = TimeSpan.FromHours(6);

    /// <summary>Patients each ward nurse looks after.</summary>
    public const int PatientsPerWardNurse = 6;

    /// <summary>A patient is well enough to go home once their illness has settled below this.</summary>
    private const double FitForDischargeSeverity = 0.35;

    public IEnumerable<CareSpace> Wards => SpacesOfKind(CareSpaceKind.Ward);

    /// <summary>The ward bed a patient is in or heading to.</summary>
    public WardBed? BedOf(Patient patient) =>
        patient.CareSpaceId is { } id && FindSpace(id) is { IsWard: true } ward && patient.BedIndex is { } index && index < ward.Beds.Count
            ? ward.Beds[index]
            : null;

    /// <summary>The ward whose team does the task: the patient's ward once they are on it, otherwise null (A&amp;E).</summary>
    private string? TeamFor(ClinicalTask task) =>
        _patients.TryGetValue(task.PatientId, out var patient) && patient.Stage == PatientStage.OnWard ? patient.CareSpaceId : null;

    // ---------------------------------------------------------------- referral

    /// <summary>The doctor has decided the patient needs admitting.</summary>
    private void RequestWardBed(Patient patient, ConditionDefinition diagnosed)
    {
        if (!Wards.Any())
        {
            ArrangeTransfer(patient, diagnosed, "No ward on site, so they are being transferred to a neighbouring hospital for admission.");
            return;
        }

        patient.Stage = PatientStage.AwaitingBed;
        patient.BedRequestedAt = Now;
        patient.Log(Now, $"{diagnosed.DispositionNote} Referred to the medical team; waiting for a ward bed.", important: true);
        Notifications.Add(Now, NotificationLevel.Info, $"{patient.FullName} needs a ward bed.", patient.Id, patient.CareSpaceId, patient.Triage);
    }

    /// <summary>Calls a transfer ambulance to take the patient to another hospital.</summary>
    private void ArrangeTransfer(Patient patient, ConditionDefinition diagnosed, string? reason = null)
    {
        patient.Stage = PatientStage.AwaitingTransfer;

        // Time-critical transfers (e.g. a heart attack going for PCI) get an emergency ambulance.
        var timeCritical = diagnosed.CanBeFatal && patient.Triage == TriageCategory.Red;
        var wait = timeCritical ? Random.Range(10, 25) : Random.Range(30, 75);
        patient.TransferAmbulanceDueAt = Now.AddMinutes(wait);
        var note = reason ?? diagnosed.DispositionNote;
        patient.Log(Now, $"{note} Ambulance expected in about {wait} minutes.", important: true);
        Notifications.Add(Now, NotificationLevel.Info, $"Transfer arranged for {patient.FullName}: {note}", patient.Id, patient.CareSpaceId, patient.Triage);
    }

    // ---------------------------------------------------------------- every tick

    private void UpdateWards()
    {
        var waiting = _patients.Values
            .Where(p => p.Stage == PatientStage.AwaitingBed && p.IsAlive)
            .OrderBy(p => p.Triage)
            .ThenBy(p => p.BedRequestedAt)
            .ToList();

        foreach (var patient in waiting)
        {
            // A reserved bed whose ward has gone is no longer held.
            if (patient.ReservedWardId is { } heldWard && FindSpace(heldWard) is null)
            {
                patient.ReservedWardId = null;
                patient.ReservedBedIndex = null;
                RemoveTasks(patient, TaskKind.EscortToWard);
            }

            if (patient.ReservedWardId is not null)
            {
                continue;
            }

            var diagnosed = Database.Conditions[patient.WorkingDiagnosis!.ConditionId];
            if (!Wards.Any() || Now - patient.BedRequestedAt > MaxWaitForBed)
            {
                var reason = Wards.Any()
                    ? $"No ward bed after {MaxWaitForBed.TotalHours:0} hours, so they are being transferred to a neighbouring hospital."
                    : "No ward on site, so they are being transferred to a neighbouring hospital for admission.";
                ArrangeTransfer(patient, diagnosed, reason);
                continue;
            }

            if (FreeWardBed() is { } free)
            {
                _activeCapacityAlerts.Remove("wards");
                free.Bed.OccupantId = patient.Id;
                patient.ReservedWardId = free.Ward.Id;
                patient.ReservedBedIndex = free.Index;
                patient.Log(Now, $"Bed {free.Index + 1} on {free.Ward.Name} allocated.");
                AddTask(TaskKind.EscortToWard, StaffRole.Nurse, patient, 5, $"Take to {free.Ward.Name}");
            }
            else if (Now - patient.BedRequestedAt > TimeSpan.FromHours(1))
            {
                RaiseCapacityAlert("wards", $"The wards are full: {patient.FullName} has waited over an hour in A&E for a bed. Build more ward beds or patients will be sent elsewhere.", patient);
            }
        }

        foreach (var patient in _patients.Values.Where(p => p.Stage == PatientStage.OnWard && p.IsAlive && p.BusyWithTaskId is null))
        {
            if (Now >= patient.DischargeDueAt && !HasTask(patient, TaskKind.WardRound) && !HasTask(patient, TaskKind.WardDischarge) && !HasTask(patient, TaskKind.WardClerking))
            {
                AddTask(TaskKind.WardRound, StaffRole.Doctor, patient, 10, "Ward round review");
            }
        }
    }

    private (CareSpace Ward, WardBed Bed, int Index)? FreeWardBed()
    {
        foreach (var ward in Wards)
        {
            for (var i = 0; i < ward.Beds.Count; i++)
            {
                if (ward.Beds[i].OccupantId is null)
                {
                    return (ward, ward.Beds[i], i);
                }
            }
        }

        return null;
    }

    private void ReleaseReservedBed(Patient patient)
    {
        if (patient.ReservedWardId is { } wardId && FindSpace(wardId) is { } ward && patient.ReservedBedIndex is { } index
            && index < ward.Beds.Count && ward.Beds[index].OccupantId == patient.Id)
        {
            ward.Beds[index].OccupantId = null;
        }

        patient.ReservedWardId = null;
        patient.ReservedBedIndex = null;
    }

    private void RemoveTasks(Patient patient, TaskKind kind)
    {
        foreach (var task in _tasks.Values.Where(t => t.PatientId == patient.Id && t.Kind == kind).ToList())
        {
            RemoveTask(task);
        }
    }

    // ---------------------------------------------------------------- moving to the ward

    /// <summary>The nurse sets off: the patient leaves their cubicle and goes up to the ward bed.</summary>
    private void StartEscortToWard(Patient patient)
    {
        if (patient.ReservedWardId is not { } wardId || FindSpace(wardId) is not { } ward || patient.ReservedBedIndex is not { } index)
        {
            return;
        }

        ReleaseSpace(patient);
        ward.Beds[index].OccupantId = patient.Id;
        patient.CareSpaceId = ward.Id;
        patient.BedIndex = index;
        WalkTo(patient, ward.Beds[index].PatientSpot);
        patient.Log(Now, $"Left A&E for {ward.Name}.");
    }

    private void CompleteEscortToWard(StaffMember nurse, Patient patient)
    {
        if (BedOf(patient) is null || patient.CareSpaceId is not { } wardId || FindSpace(wardId) is not { } ward)
        {
            return;
        }

        var diagnosed = Database.Conditions[patient.WorkingDiagnosis!.ConditionId];
        patient.Stage = PatientStage.OnWard;
        patient.ReservedWardId = null;
        patient.ReservedBedIndex = null;
        patient.AdmittedAt = Now;
        patient.LeftEmergencyAt = Now;
        var stay = diagnosed.LengthOfStayHours;
        patient.DischargeDueAt = Now.AddHours(Random.Range(stay[0], stay[1]));
        patient.Log(Now, $"Admitted to {ward.Name}, bed {patient.BedIndex + 1}, handed over by {nurse.Name}. Time in A&E {(Now - patient.ArrivedAt).TotalHours:0.0} hours.", important: true);
        Stats.RecordAdmission(patient, Now);

        foreach (var doctor in _staff.Where(s => s.WardId is null))
        {
            doctor.Caseload.Remove(patient.Id);
        }

        AddTask(TaskKind.WardClerking, StaffRole.Doctor, patient, 15, "Admission clerking");
    }

    // ---------------------------------------------------------------- care on the ward

    /// <summary>
    /// The ward doctor sees the new admission. A skilled doctor can spot a wrong A&amp;E diagnosis and put
    /// the treatment right; otherwise the patient carries on deteriorating until someone does.
    /// </summary>
    private void CompleteClerking(StaffMember doctor, Patient patient)
    {
        doctor.Caseload.Add(patient.Id);
        var wrong = patient.WorkingDiagnosis?.ConditionId != patient.Condition.Id;
        if (wrong && Random.Chance(doctor.Skill * (1 - (doctor.Fatigue * 0.5)) * 0.8))
        {
            patient.WorkingDiagnosis = new Diagnosis(patient.Condition.Id, 0.9, Now, doctor.Id);
            patient.EffectiveTreatmentGiven = true;
            Stats.WardDiagnosesCorrected++;
            patient.Log(Now, $"Clerked by {doctor.Name}, who revised the diagnosis to {patient.Condition.Name.ToLowerInvariant()} and started the right treatment.", important: true);
            Notifications.Add(Now, NotificationLevel.Warning, $"Ward team corrected {patient.FullName}'s diagnosis to {patient.Condition.Name.ToLowerInvariant()}.", patient.Id, patient.CareSpaceId);
            return;
        }

        if (!wrong && !patient.EffectiveTreatmentGiven)
        {
            // Something was withheld in A&E (an allergy): the ward team find an alternative.
            patient.EffectiveTreatmentGiven = true;
            patient.Log(Now, $"Clerked by {doctor.Name}. Alternative treatment started on the ward.");
            return;
        }

        patient.Log(Now, $"Clerked by {doctor.Name}. Plan: continue treatment, observations on the ward, review on the ward round.");
    }

    private void CompleteWardRound(StaffMember doctor, Patient patient)
    {
        if (patient.Severity <= FitForDischargeSeverity)
        {
            patient.Log(Now, $"Ward round with {doctor.Name}: well enough to go home.");
            AddTask(TaskKind.WardDischarge, StaffRole.Nurse, patient, 15, "Discharge home");
            return;
        }

        patient.DischargeDueAt = Now.AddHours(12);
        patient.Log(Now, $"Ward round with {doctor.Name}: not yet well enough to go home. Review in 12 hours.");
    }

    private void CompleteWardDischarge(StaffMember nurse, Patient patient)
    {
        var ward = patient.CareSpaceId is { } id ? FindSpace(id) : null;
        var days = (Now - (patient.AdmittedAt ?? Now)).TotalDays;
        patient.OutcomeNote = $"Admitted to {ward?.Name ?? "a ward"} and discharged home after {days:0.0} days.";
        patient.Log(Now, $"Discharged home from {ward?.Name ?? "the ward"} by {nurse.Name} after {days:0.0} days.");
        EndVisit(patient, PatientOutcome.Discharged);
        WalkTo(patient, WalkInSpawn);
    }

    // ---------------------------------------------------------------- ward teams

    /// <summary>
    /// Each ward has its own nurses (one for every <see cref="PatientsPerWardNurse"/> beds) and a doctor,
    /// who join when it is built and leave when it is demolished.
    /// </summary>
    private void SyncWardStaff()
    {
        var wards = Wards.ToDictionary(w => w.Id);
        foreach (var member in _staff.Where(s => s.WardId is { } id && !wards.ContainsKey(id)).ToList())
        {
            if (member.CurrentTaskId is { } taskId && _tasks.TryGetValue(taskId, out var task))
            {
                RemoveTask(task);
            }

            _staff.Remove(member);
        }

        foreach (var ward in wards.Values)
        {
            var nurses = Math.Max(1, (ward.Beds.Count + PatientsPerWardNurse - 1) / PatientsPerWardNurse);
            var team = _staff.Where(s => s.WardId == ward.Id).ToList();
            var haveNurses = team.Count(s => s.Role == StaffRole.Nurse);
            for (var i = haveNurses; i < nurses; i++)
            {
                Hire(StaffRole.Nurse, i == 0 ? "Ward sister" : "Ward nurse", ward);
            }

            foreach (var spare in team.Where(s => s.Role == StaffRole.Nurse && s.CurrentTaskId is null).Skip(nurses).ToList())
            {
                _staff.Remove(spare);
            }

            if (!team.Any(s => s.Role == StaffRole.Doctor))
            {
                Hire(StaffRole.Doctor, "Ward doctor (FY1)", ward);
            }
        }
    }

    private void Hire(StaffRole role, string grade, CareSpace ward)
    {
        var senior = grade == "Ward sister";
        var member = MakeStaff(role, grade, senior ? 0.8 : role == StaffRole.Doctor ? 0.55 : 0.6, 1.0, senior ? 10 : role == StaffRole.Doctor ? 1 : 3,
            senior ? 43_000 : role == StaffRole.Doctor ? 36_000 : 31_000, ward.DepartmentId, ward.Id);
        _staff.Add(member);
        member.PlaceAt(HomeTile(member));
    }

    /// <summary>Where ward staff wait between tasks: spread along the aisle inside the ward door.</summary>
    private GridCoord WardHome(StaffMember member, CareSpace ward)
    {
        var team = _staff.Where(s => s.WardId == ward.Id).ToList();
        var index = Math.Max(0, team.IndexOf(member));
        var spot = ward.StaffSpot;
        var r = ward.Rect;
        var x = Math.Clamp(spot.X + ((index % 2 == 0 ? 1 : -1) * ((index + 1) / 2) * 2), r.X + 1, r.Right - 2);
        return new GridCoord(x, Math.Clamp(spot.Y, r.Y + 1, r.Bottom - 2));
    }
}
