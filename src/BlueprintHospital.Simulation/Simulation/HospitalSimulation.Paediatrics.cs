using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// Paediatrics, the children's department. Children (under 16) who need admitting go to a children's
/// ward, and children who need high dependency care to a children's HDU. Adults never go to them, and
/// children never go to an adult ward: with no children's ward a child is transferred to a neighbouring
/// hospital, and a child who needs intensive care goes to the regional children's intensive care unit.
///
/// Only children's nurses and paediatric doctors (and healthcare assistants) can be posted to the
/// children's wards, and they do the nursing and doctors' work there. The A&amp;E team does not cover
/// a children's ward that has nobody on.
///
/// In a Career game the rule only applies once Paediatrics is available; before that children are
/// admitted to the adult wards as they always were.
/// </summary>
public sealed partial class HospitalSimulation
{
    public const string PaediatricsDepartmentId = "paediatrics";

    /// <summary>Children's nurses on an ordinary children's ward: one for every four beds.</summary>
    public const int PatientsPerChildrensNurse = 4;

    /// <summary>Whether children are kept to the children's wards in this game.</summary>
    private bool ChildrenGoToPaediatrics =>
        Database.Departments.ContainsKey(PaediatricsDepartmentId) && IsDepartmentAvailable(PaediatricsDepartmentId);

    /// <summary>A patient the children's department looks after: under 16 (not a mother in labour).</summary>
    public bool IsChildPatient(Patient patient) => patient.Age < AdultAge && !patient.Maternity && ChildrenGoToPaediatrics;

    /// <summary>A ward or unit that belongs to Paediatrics (the children's ward or the children's HDU).</summary>
    public bool IsPaediatricWard(CareSpace space) => space.IsWard && space.DepartmentId == PaediatricsDepartmentId;

    public IEnumerable<CareSpace> PaediatricWards => Wards.Where(IsPaediatricWard);

    private IEnumerable<CareSpace> PaediatricHighDependencyUnits => PaediatricWards.Where(w => UnitOf(w) == WardUnit.HighDependency);

    /// <summary>Children's intensive care units (PICUs) that are open: like an adult ICU, one needs an anaesthetist posted to it.</summary>
    private IEnumerable<CareSpace> OpenPaediatricIntensiveCareUnits => PaediatricWards.Where(IsIntensiveCareOpen);

    /// <summary>A&amp;E rooms kept for children: the children's waiting room and children's cubicles.</summary>
    public bool IsChildrensEmergencyRoom(CareSpace space) => !space.IsWard && space.DepartmentId == PaediatricsDepartmentId;

    /// <summary>Whether a patient may use an A&amp;E room: children's rooms are for children only.</summary>
    private bool MayUse(CareSpace space, Patient patient) => !IsChildrensEmergencyRoom(space) || IsChildPatient(patient);

    /// <summary>Staff who can only work with children.</summary>
    public static bool IsPaediatricRole(StaffRole role) => role is StaffRole.ChildrensNurse or StaffRole.Paediatrician;

    /// <summary>Whether there is a high dependency bed for this patient: a children's HDU for a child, otherwise an adult HDU or open ICU.</summary>
    private bool HasHighDependencyBedsFor(Patient patient) =>
        IsChildPatient(patient) ? PaediatricHighDependencyUnits.Any() || OpenPaediatricIntensiveCareUnits.Any() : HasHighDependencyBeds;

    /// <summary>Whether there is an open intensive care unit for this patient: a PICU for a child, otherwise an adult ICU.</summary>
    private bool HasIntensiveCareFor(Patient patient) => IsChildPatient(patient) ? OpenPaediatricIntensiveCareUnits.Any() : OpenIntensiveCareUnits.Any();

    /// <summary>
    /// The wards a child can be admitted to, best first: an open PICU for intensive care; the children's HDU
    /// (then a PICU) for high dependency care; then the children's wards, then the children's assessment
    /// unit. A child here for a few hours' observation goes to the children's assessment unit first. A child
    /// back from theatre, or here for observation, can use the A&amp;E ward when there is no children's ward.
    /// Empty when there is nowhere for them.
    /// </summary>
    private List<CareSpace> WardsForChild(Patient patient)
    {
        var wards = PaediatricWards.Where(w => UnitOf(w) == WardUnit.None).ToList();
        var assessment = PaediatricWards.Where(w => UnitOf(w) == WardUnit.Assessment).ToList();
        var plain = wards.Concat(assessment).ToList();
        var emergency = WardsOf(EmergencyDepartmentId).Where(w => !IsSameDayUnit(w) && UnitOf(w) is WardUnit.None or WardUnit.Assessment).ToList();
        if (patient.ForObservation)
        {
            return assessment.Concat(wards).Concat(emergency).ToList();
        }

        if (patient.CareLevel >= 3)
        {
            // With no open PICU here, children who need intensive care go to the regional children's ICU.
            return OpenPaediatricIntensiveCareUnits.ToList();
        }

        var critical = patient.CareLevel == 2 ? PaediatricHighDependencyUnits.Concat(OpenPaediatricIntensiveCareUnits).ToList() : new List<CareSpace>();
        var ordinary = plain.Count > 0 ? plain : patient.HadSurgery ? emergency : plain;
        return critical.Concat(ordinary).ToList();
    }

    /// <summary>Why a child who needs admitting can't be.</summary>
    private string NoWardReasonForChild(Patient patient)
    {
        if (patient.CareLevel >= 3)
        {
            return PaediatricWards.Any(w => UnitOf(w) == WardUnit.IntensiveCare)
                ? "Needs intensive care, but the children's intensive care unit has no anaesthetist, so the regional children's intensive care team are coming to take them to their PICU."
                : "Needs intensive care: the regional children's intensive care team are coming to take them to their PICU.";
        }

        return IsUnlocked(PaediatricsDepartmentId)
            ? "There is no children's ward here, so they are being transferred to a neighbouring hospital's children's ward."
            : "This hospital has no Paediatrics department, so they are being transferred to a neighbouring hospital's children's ward.";
    }

    /// <summary>Children's nurses do the nursing on their own children's ward.</summary>
    private bool ChildrensNurseCanCover(ClinicalTask task, StaffMember member) =>
        member.Role == StaffRole.ChildrensNurse && task.Role == StaffRole.Nurse
        && task.Kind is not (TaskKind.MovePatient or TaskKind.CarryBaby or TaskKind.DeliverMedicines);

    /// <summary>Paediatric doctors clerk, review and discharge on their own children's ward.</summary>
    private bool PaediatricianCanCover(ClinicalTask task, StaffMember member) =>
        member.Role == StaffRole.Paediatrician && task.Role == StaffRole.Doctor;

    /// <summary>Adult nurses and doctors don't work on a children's ward (healthcare assistants do).</summary>
    private static bool CanWorkOnChildrensWard(StaffRole role) =>
        IsPaediatricRole(role) || role == StaffRole.HealthcareAssistant;
}
