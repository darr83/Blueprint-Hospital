using BlueprintHospital.Simulation.Agents;

namespace BlueprintHospital.Simulation.Staff;

public enum StaffRole
{
    Receptionist,
    Nurse,
    Doctor,
    Radiographer,
    Surgeon,
    Pharmacist,

    /// <summary>Moves patients who can't walk: wheelchairs and trolleys.</summary>
    Porter,
}

public enum StaffActivity
{
    Idle,
    WalkingToTask,
    Working,
    OnBreak,
    WalkingToBreak,
}

/// <summary>An employee. Staff choose their own work from the task board according to role and clinical priority.</summary>
public sealed class StaffMember : Agent
{
    public required string Name { get; init; }

    public required StaffRole Role { get; init; }

    /// <summary>Job title shown to the player, e.g. "Registrar" or "Emergency nurse".</summary>
    public required string Grade { get; init; }

    public required string DepartmentId { get; init; }

    /// <summary>The room this member of staff is based in (a ward, X-ray, CT, theatre or pharmacy); null for A&amp;E staff.</summary>
    public string? BaseRoomId { get; set; }

    public int Age { get; init; }

    public int YearsExperience { get; init; }

    /// <summary>Clinical skill 0..1: affects diagnostic accuracy.</summary>
    public double Skill { get; init; } = 0.6;

    /// <summary>Work speed multiplier (1 = average).</summary>
    public double Speed { get; init; } = 1.0;

    /// <summary>Annual salary in pounds.</summary>
    public int Salary { get; init; }

    /// <summary>0 = fresh, 1 = exhausted. Rises while working, falls on breaks.</summary>
    public double Fatigue { get; set; }

    /// <summary>0..1.</summary>
    public double Morale { get; set; } = 0.75;

    public StaffActivity Activity { get; set; } = StaffActivity.Idle;

    public int? CurrentTaskId { get; set; }

    /// <summary>Game seconds of work left on the current task.</summary>
    public double WorkRemainingSeconds { get; set; }

    public DateTime? BreakEndsAt { get; set; }

    /// <summary>Plain-English description of what they're doing, for the UI.</summary>
    public string StatusText { get; set; } = "Available";

    /// <summary>Patients this doctor is currently responsible for.</summary>
    public HashSet<int> Caseload { get; } = new();

    public int TasksCompleted { get; set; }

    /// <summary>The patient whose wheelchair or trolley this member of staff is pushing, if any.</summary>
    public int? PushingPatientId { get; set; }

    /// <summary>Multiplier on task duration from speed and tiredness.</summary>
    public double DurationFactor => (1.0 / Speed) * (1.0 + (Fatigue * 0.4));
}
