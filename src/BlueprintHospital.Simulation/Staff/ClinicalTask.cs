namespace BlueprintHospital.Simulation.Staff;

/// <summary>Kinds of work staff can pick up.</summary>
public enum TaskKind
{
    Register,
    Triage,
    AmbulanceHandover,
    Observations,
    DoctorAssessment,
    TakeBloods,
    BedsideTest,
    DoctorReview,
    GiveMedication,
    Procedure,
    Discharge,
    EscortToTransfer,
    EscortToWard,
    WardClerking,
    WardRound,
    WardDischarge,

    /// <summary>An X-ray or CT scan in the imaging room; the patient goes there and back.</summary>
    Imaging,

    /// <summary>An operation in theatre.</summary>
    Surgery,

    /// <summary>The pharmacist prepares medicines to take home before a ward discharge.</summary>
    DispenseMedicines,

    /// <summary>A porter pushes a patient's wheelchair or trolley to where they need to be.</summary>
    MovePatient,

    /// <summary>A physiotherapy session at a ward patient's bedside.</summary>
    Physio,

    /// <summary>The critical care outreach nurse reviews a ward patient who is getting worse.</summary>
    OutreachReview,
}

/// <summary>
/// A piece of work on the task board. Tasks are plain data (no callbacks), so the board can be saved
/// and the simulation decides what each kind of task does when it completes.
/// </summary>
public sealed class ClinicalTask
{
    public required int Id { get; init; }

    public required TaskKind Kind { get; init; }

    public required StaffRole Role { get; init; }

    public required int PatientId { get; init; }

    public required DateTime CreatedAt { get; init; }

    /// <summary>Base minutes the work takes for an average, rested member of staff.</summary>
    public required double Minutes { get; init; }

    /// <summary>What the task is about: an investigation id, a treatment step index, etc.</summary>
    public string? Detail { get; init; }

    /// <summary>Plain-English label, e.g. "Take bloods" or "Give aspirin".</summary>
    public required string Label { get; init; }

    public int? AssignedStaffId { get; set; }

    /// <summary>True once staff and patient are both in place and the work timer has started.</summary>
    public bool Started { get; set; }

    /// <summary>The room the task is done in, when it needs one of its own (triage, imaging, theatre or pharmacy).</summary>
    public string? SpaceId { get; set; }

    /// <summary>Calculated priority; higher is more urgent.</summary>
    public double Priority { get; set; }
}
