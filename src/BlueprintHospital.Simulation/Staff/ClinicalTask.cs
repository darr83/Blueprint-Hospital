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

    /// <summary>Calculated priority; higher is more urgent.</summary>
    public double Priority { get; set; }
}
