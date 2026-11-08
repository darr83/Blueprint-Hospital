using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// Departments. A&amp;E, General Medicine and the shared services (imaging, theatres, pharmacy) are
/// open from the start; each other specialty (Cardiology, Neurology and so on) is unlocked by paying
/// its opening cost, which lets its rooms (such as its ward) be built.
///
/// Admitted patients go to a ward of their own specialty. If those wards are full they wait on the
/// A&amp;E ward instead; if the hospital has no ward for that specialty they are transferred out.
/// Each operating theatre only takes patients from the departments ticked in its room panel.
/// </summary>
public sealed partial class HospitalSimulation
{
    public const string EmergencyDepartmentId = "emergency";

    private readonly HashSet<string> _unlockedDepartments = new();

    public IReadOnlyCollection<string> UnlockedDepartments => _unlockedDepartments;

    /// <summary>Whether a department's rooms can be built.</summary>
    public bool IsUnlocked(string departmentId) => _unlockedDepartments.Contains(departmentId);

    /// <summary>Departments open from the start: A&amp;E, any department with no opening cost, and the shared services.</summary>
    private void UnlockStartingDepartments()
    {
        foreach (var d in Database.Departments.Values.Where(d => d.StartsUnlocked || d.IsSharedService || d.OpeningCost == 0))
        {
            _unlockedDepartments.Add(d.Id);
        }
    }

    /// <summary>Whether a department can be unlocked now, and what it costs.</summary>
    public BuildCheck CheckUnlock(string departmentId)
    {
        if (!Database.Departments.TryGetValue(departmentId, out var department))
        {
            return new BuildCheck(0, new[] { $"Unknown department '{departmentId}'." });
        }

        var problems = new List<string>();
        if (IsUnlocked(departmentId))
        {
            problems.Add($"{department.Name} is already unlocked.");
        }
        else if (Balance < department.OpeningCost)
        {
            problems.Add($"Not enough money: costs £{department.OpeningCost:N0}, you have £{Balance:N0}.");
        }

        return new BuildCheck(department.OpeningCost, problems);
    }

    /// <summary>Pays a department's opening cost so its rooms can be built.</summary>
    public BuildCheck UnlockDepartment(string departmentId)
    {
        var check = CheckUnlock(departmentId);
        if (!check.CanBuild)
        {
            return check;
        }

        var department = Database.Departments[departmentId];
        _unlockedDepartments.Add(departmentId);
        Book(MoneyCategory.Construction, -check.Cost);
        Notifications.Add(Now, NotificationLevel.Info, $"Unlocked {department.Name} for £{check.Cost:N0}. Build its ward and hire its team to open it.");
        return check;
    }

    /// <summary>The department a patient's working diagnosis belongs to.</summary>
    private string DepartmentOf(Patient patient) =>
        patient.WorkingDiagnosis is { } d && Database.Conditions.TryGetValue(d.ConditionId, out var condition) ? condition.Department : EmergencyDepartmentId;

    public string DepartmentName(string departmentId) =>
        Database.Departments.TryGetValue(departmentId, out var d) ? d.Name : departmentId;

    // ---------------------------------------------------------------- wards

    /// <summary>The wards that belong to a department.</summary>
    public IEnumerable<CareSpace> WardsOf(string departmentId) => Wards.Where(w => w.DepartmentId == departmentId);

    /// <summary>
    /// The wards a patient can be admitted to, best first: observation patients only go to the A&amp;E
    /// ward; others go to their specialty's wards, then the A&amp;E ward when those are full. Patients back
    /// from theatre can recover on the A&amp;E ward even if their specialty has no ward. Empty if there is
    /// nowhere for them.
    /// </summary>
    private List<CareSpace> WardsFor(Patient patient)
    {
        var emergency = WardsOf(EmergencyDepartmentId).ToList();
        if (patient.ForObservation)
        {
            return emergency;
        }

        var department = DepartmentOf(patient);
        var own = department == EmergencyDepartmentId ? new List<CareSpace>() : WardsOf(department).ToList();
        if (own.Count == 0 && !patient.HadSurgery)
        {
            return department == EmergencyDepartmentId ? emergency : own;
        }

        return own.Concat(emergency).ToList();
    }

    /// <summary>Why a patient who needs admitting can't be: their specialty has no ward here.</summary>
    private string NoWardReason(Patient patient)
    {
        var department = DepartmentOf(patient);
        var name = DepartmentName(department);
        return IsUnlocked(department)
            ? $"There is no {name} ward on site, so they are being transferred to a neighbouring hospital for admission."
            : $"This hospital has no {name} department, so they are being transferred to a neighbouring hospital for admission.";
    }

    private (CareSpace Ward, WardBed Bed, int Index)? FreeWardBed(Patient patient)
    {
        foreach (var ward in WardsFor(patient))
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

    /// <summary>
    /// A patient who needs a few hours' observation before going home moves to the A&amp;E ward, freeing
    /// their cubicle, if it has a free bed. False if not (they are then discharged from the cubicle).
    /// </summary>
    private bool StartObservation(Patient patient, ConditionDefinition diagnosed)
    {
        patient.ForObservation = true;
        if (FreeWardBed(patient) is not { } free)
        {
            patient.ForObservation = false;
            return false;
        }

        patient.Stage = PatientStage.AwaitingBed;
        patient.BedRequestedAt = Now;
        free.Bed.OccupantId = patient.Id;
        patient.ReservedWardId = free.Ward.Id;
        patient.ReservedBedIndex = free.Index;
        patient.Log(Now, $"Treatment done. Needs {diagnosed.ObserveHours![0]:0} to {diagnosed.ObserveHours[1]:0} hours of observation before going home: bed {free.Index + 1} on {free.Ward.Name}.", important: true);
        AddTask(TaskKind.EscortToWard, StaffRole.Nurse, patient, 5, $"Take to {free.Ward.Name}");
        return true;
    }

    // ---------------------------------------------------------------- theatres

    /// <summary>Departments whose patients can need an operation (the ones a theatre can be opened to).</summary>
    public IReadOnlyList<DepartmentDefinition> SurgicalDepartments =>
        Database.Departments.Values
            .Where(d => Database.Conditions.Values.Any(c => c.Disposition == Disposition.Surgery && c.Department == d.Id))
            .OrderBy(d => d.Name)
            .ToList();

    /// <summary>Lets (or stops) a department's patients use an operating theatre.</summary>
    public void SetTheatreAccess(string roomId, string departmentId, bool allowed)
    {
        if (FindSpace(roomId) is not { Kind: CareSpaceKind.Theatre } theatre)
        {
            return;
        }

        if (allowed)
        {
            theatre.AllowedDepartments.Add(departmentId);
        }
        else
        {
            theatre.AllowedDepartments.Remove(departmentId);
        }

        CancelUnavailableWork();
    }

    /// <summary>Whether a staffed theatre will take patients from this department.</summary>
    public bool HasTheatreFor(string departmentId) =>
        SpacesOfKind(CareSpaceKind.Theatre).Any(t => IsStaffed(t) && t.AllowedDepartments.Contains(departmentId));

    /// <summary>The department whose theatre list an operation is on (from the planned diagnosis).</summary>
    private string? SurgeryDepartment(string? conditionId) =>
        conditionId is not null && Database.Conditions.TryGetValue(conditionId, out var condition) ? condition.Department : null;

    // ---------------------------------------------------------------- names

    /// <summary>Renames a member of staff.</summary>
    public void RenameStaff(int staffId, string name)
    {
        if (FindStaff(staffId) is { } member && !string.IsNullOrWhiteSpace(name))
        {
            member.Name = name.Trim();
        }
    }

    /// <summary>Renames a patient: the last word is their surname, the rest their first name.</summary>
    public void RenamePatient(int patientId, string fullName)
    {
        if (FindPatient(patientId) is not { } patient || string.IsNullOrWhiteSpace(fullName))
        {
            return;
        }

        var words = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1)
        {
            patient.FirstName = words[0];
            return;
        }

        patient.FirstName = string.Join(' ', words[..^1]);
        patient.Surname = words[^1];
    }
}
