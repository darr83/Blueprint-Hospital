using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Patients;

/// <summary>Where a booked appointment has got to.</summary>
public enum AppointmentStatus
{
    /// <summary>On the calendar; the patient hasn't arrived yet.</summary>
    Booked,

    /// <summary>Walking in from the car park, or queueing at the Outpatients desk.</summary>
    Arriving,

    /// <summary>Checked in and waiting to be called.</summary>
    Waiting,

    /// <summary>Called in and being seen in the clinic room.</summary>
    InClinic,

    /// <summary>Seen and gone home.</summary>
    Seen,

    /// <summary>Never turned up.</summary>
    DidNotAttend,

    /// <summary>The clinic couldn't run (no doctor or receptionist), so the appointment was cancelled.</summary>
    Cancelled,
}

/// <summary>One booked slot in a clinic's calendar.</summary>
public sealed class Appointment
{
    public required int Id { get; init; }

    /// <summary>The clinic room it is booked into.</summary>
    public required string ClinicId { get; init; }

    public required DateTime Time { get; init; }

    public required int Minutes { get; init; }

    public required string FirstName { get; set; }

    public required string Surname { get; set; }

    public string FullName => $"{FirstName} {Surname}";

    public required int Age { get; init; }

    public required Sex Sex { get; init; }

    public required string Reason { get; init; }

    /// <summary>A procedure (a smear test, a mole removal) rather than a consultation.</summary>
    public bool IsProcedure { get; init; }

    /// <summary>What this appointment pays, when it isn't the clinic's usual fee (procedures).</summary>
    public int? Tariff { get; init; }

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Booked;

    /// <summary>The person walking around the hospital, once they have arrived.</summary>
    public int? OutpatientId { get; set; }

    /// <summary>Whether they will turn up (decided when booked, so the calendar can't tell).</summary>
    public bool WillAttend { get; init; } = true;

    /// <summary>When they will arrive: a little early, or now and then late.</summary>
    public DateTime ArrivesAt { get; init; }

    public DateTime? CheckedInAt { get; set; }

    public DateTime? SeenAt { get; set; }

    public int? SeenByStaffId { get; set; }
}

/// <summary>Where a clinic patient is in their visit.</summary>
public enum OutpatientStage
{
    WalkingIn,
    Queueing,
    CheckingIn,
    Waiting,
    GoingIn,
    BeingSeen,
    Leaving,
}

/// <summary>A clinic patient: walks in, checks in at Outpatients reception, waits, is seen and goes home.</summary>
public sealed class Outpatient : Agent
{
    public required int AppointmentId { get; init; }

    public required string ClinicId { get; init; }

    public OutpatientStage Stage { get; set; } = OutpatientStage.WalkingIn;

    /// <summary>The chair they are sitting on (or heading to) while they wait.</summary>
    public GridCoord? Seat { get; set; }

    /// <summary>When the check-in or consultation they are in finishes.</summary>
    public DateTime? BusyUntil { get; set; }

    /// <summary>Shown under their name: what they are doing.</summary>
    public string StatusText { get; set; } = "Arriving";

    /// <summary>When they last got something to eat or drink while waiting.</summary>
    public DateTime? LastRefreshmentAt { get; set; }
}
