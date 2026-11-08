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

    /// <summary>Does echocardiograms and runs the cath lab.</summary>
    CardiacPhysiologist,

    /// <summary>Records EEGs.</summary>
    Neurophysiologist,

    /// <summary>Does ultrasound scans in the early pregnancy unit.</summary>
    Sonographer,

    /// <summary>Does endoscopies (OGD).</summary>
    Endoscopist,

    /// <summary>Puts on plaster casts in the plaster room.</summary>
    PlasterTechnician,

    /// <summary>Gets ward patients moving after a broken hip or a stroke, so they go home sooner.</summary>
    Physiotherapist,

    /// <summary>Gives the anaesthetic for every operation, and runs the intensive care unit.</summary>
    Anaesthetist,

    /// <summary>Critical care outreach: reviews ward patients who are getting worse and moves them up to HDU or ICU.</summary>
    OutreachNurse,
}

public enum StaffActivity
{
    Idle,
    WalkingToTask,
    Working,
    OnBreak,
    WalkingToBreak,

    /// <summary>Their shift has ended: walking out of the main entrance.</summary>
    GoingHome,

    /// <summary>Not at work: the other team is on. Not in the building and can't be given work.</summary>
    OffDuty,
}

/// <summary>The two 12-hour shifts. Everyone belongs to the day team or the night team.</summary>
public enum StaffShift
{
    /// <summary>08:00 to 20:00.</summary>
    Day,

    /// <summary>20:00 to 08:00.</summary>
    Night,
}

/// <summary>A break planned for someone's shift: a 30-minute meal break or a 15-minute tea break.</summary>
public sealed class PlannedBreak
{
    public required DateTime Start { get; set; }

    public required int Minutes { get; init; }

    /// <summary>When they actually went, once they have.</summary>
    public DateTime? TakenAt { get; set; }

    /// <summary>True once they have been on it and come back.</summary>
    public bool Done { get; set; }

    /// <summary>True when the player sent them: they go as soon as they're free.</summary>
    public bool Requested { get; init; }

    public bool IsMeal => Minutes >= 30;
}

/// <summary>An employee. Staff choose their own work from the task board according to role and clinical priority.</summary>
public sealed class StaffMember : Agent
{
    public required string Name { get; set; }

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

    /// <summary>Set while a tannoy call for this member is playing; they set off when it ends. Not saved.</summary>
    public DateTime? PagedUntil { get; set; }

    public int? CurrentTaskId { get; set; }

    /// <summary>An anaesthetist giving the anaesthetic for this operation (the surgeon's task) is held in theatre until it is done.</summary>
    public int? AssistingTaskId { get; set; }

    /// <summary>The red-monitor patient this doctor is staying with until they are no longer red.</summary>
    public int? BedsidePatientId { get; set; }

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

    /// <summary>The team they are on: days (08:00 to 20:00) or nights (20:00 to 08:00).</summary>
    public StaffShift Shift { get; set; } = StaffShift.Day;

    /// <summary>Breaks planned for the shift they are on (or about to start), earliest first.</summary>
    public List<PlannedBreak> Breaks { get; } = new();

    /// <summary>True when the player has moved them and they will change place once their current task is done.</summary>
    public bool HasPendingMove { get; set; }

    /// <summary>Where they are moving to when <see cref="HasPendingMove"/> (null for A&amp;E or the whole hospital).</summary>
    public string? PendingRoomId { get; set; }

    /// <summary>At work: in the building on their shift (working, free or on a break).</summary>
    public bool IsOnDuty => Activity is not (StaffActivity.GoingHome or StaffActivity.OffDuty);

    /// <summary>In the building, so drawn on the map (includes staff walking out at the end of a shift).</summary>
    public bool IsInBuilding => Activity != StaffActivity.OffDuty;

    /// <summary>On a break, or walking to one.</summary>
    public bool IsOnBreak => Activity is StaffActivity.OnBreak or StaffActivity.WalkingToBreak;

    /// <summary>Multiplier on task duration from speed and tiredness.</summary>
    public double DurationFactor => (1.0 / Speed) * (1.0 + (Fatigue * 0.4));
}
