using BlueprintHospital.Simulation.World;
using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Medical;

namespace BlueprintHospital.Simulation.Patients;

public enum Sex
{
    Female,
    Male,
}

public enum ArrivalMethod
{
    WalkIn,
    Ambulance,
}

/// <summary>Where a patient is in their A&amp;E journey.</summary>
public enum PatientStage
{
    WalkingIn,
    WaitingToRegister,
    WaitingForTriage,
    InAmbulanceQueue,
    WaitingForSpace,
    InTreatmentSpace,
    AwaitingTransfer,

    /// <summary>Treated in A&amp;E and referred for admission; waiting in their cubicle for a ward bed.</summary>
    AwaitingBed,

    /// <summary>Admitted to a ward bed.</summary>
    OnWard,

    /// <summary>Treated in A&amp;E and waiting in their cubicle for theatre.</summary>
    AwaitingSurgery,
    Leaving,
    Gone,
}

/// <summary>How a patient gets about: on their own feet, in a wheelchair, or lying on a trolley.</summary>
public enum Mobility
{
    Walking,
    Wheelchair,
    Trolley,
}

public enum PatientOutcome
{
    None,
    Discharged,
    Transferred,
    Admitted,
    LeftWithoutBeingSeen,
    Died,
}

public sealed record TimelineEntry(DateTime Time, string Text, bool Important = false);

public enum InvestigationStatus
{
    Requested,
    SampleTaken,
    Resulted,
}

/// <summary>An investigation ordered for a patient and, once back, its result.</summary>
public sealed class InvestigationOrder
{
    public required string InvestigationId { get; init; }

    public required DateTime RequestedAt { get; init; }

    public InvestigationStatus Status { get; set; } = InvestigationStatus.Requested;

    public DateTime? SampleTakenAt { get; set; }

    public DateTime? ResultDueAt { get; set; }

    public DateTime? ResultedAt { get; set; }

    public bool Abnormal { get; set; }

    public double? Value { get; set; }

    public string Summary { get; set; } = string.Empty;

    /// <summary>True once the doctor has seen this result.</summary>
    public bool Reviewed { get; set; }
}

/// <summary>A patient's working diagnosis, set by a doctor. May be wrong.</summary>
public sealed record Diagnosis(string ConditionId, double Confidence, DateTime MadeAt, int DoctorId);

/// <summary>A treatment step planned or given.</summary>
public sealed class TreatmentRecord
{
    public required TreatmentStep Step { get; init; }

    public bool Done { get; set; }

    public bool Withheld { get; set; }

    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// One patient: who they are, their hidden condition and physiology, and their medical record.
/// The player and staff see only what has been observed or tested.
/// </summary>
public sealed class Patient : Agent
{
    private readonly List<TimelineEntry> _timeline = new();
    private readonly List<(DateTime Time, Observations Obs, News2Score News2)> _observationHistory = new();

    public required string HospitalNumber { get; init; }

    public required string FirstName { get; set; }

    public required string Surname { get; set; }

    public string FullName => $"{FirstName} {Surname}";

    public required Sex Sex { get; init; }

    public required DateTime DateOfBirth { get; init; }

    public int AgeAt(DateTime date) => date.Year - DateOfBirth.Year - (date.DayOfYear < DateOfBirth.DayOfYear ? 1 : 0);

    public required int Age { get; init; }

    public required IReadOnlyList<string> PastHistory { get; init; }

    public required IReadOnlyList<string> RegularMedications { get; init; }

    /// <summary>Allergy display names, e.g. "Penicillin (rash)".</summary>
    public required IReadOnlyList<string> Allergies { get; init; }

    /// <summary>Allergy groups used for prescribing checks.</summary>
    public required IReadOnlyList<string> AllergyGroups { get; init; }

    public required int PreviousVisits { get; init; }

    public required ArrivalMethod ArrivalMethod { get; init; }

    public required DateTime ArrivedAt { get; init; }

    public required string PresentingComplaint { get; init; }

    /// <summary>All symptoms the patient has. Staff learn them through history taking.</summary>
    public required IReadOnlyList<string> Symptoms { get; init; }

    /// <summary>The true diagnosis. Hidden from staff and the player until the patient's journey is reviewed.</summary>
    public required ConditionDefinition Condition { get; init; }

    /// <summary>Observations the patient would have at their starting severity.</summary>
    public required Observations BaselineObservations { get; init; }

    public required double InitialSeverity { get; init; }

    /// <summary>0 = well, 1 = peri-arrest. Driven by the condition and the care received.</summary>
    public double Severity { get; set; }

    /// <summary>Handover notes from the ambulance crew, if they came by ambulance.</summary>
    public string? AmbulanceHandover { get; set; }

    public PatientStage Stage { get; set; } = PatientStage.WalkingIn;

    public PatientOutcome Outcome { get; set; } = PatientOutcome.None;

    public string OutcomeNote { get; set; } = string.Empty;

    public DateTime? LeftAt { get; set; }

    public TriageCategory? Triage { get; set; }

    public string TriageReason { get; set; } = string.Empty;

    public DateTime? TriagedAt { get; set; }

    /// <summary>Symptoms staff know about. Starts with the presenting complaint.</summary>
    public HashSet<string> KnownSymptoms { get; } = new();

    public bool HistoryTaken { get; set; }

    public DateTime? FirstSeenByDoctorAt { get; set; }

    public List<InvestigationOrder> Investigations { get; } = new();

    public Diagnosis? WorkingDiagnosis { get; set; }

    public List<TreatmentRecord> TreatmentPlan { get; } = new();

    /// <summary>True when every essential step for the true condition has been given.</summary>
    public bool EffectiveTreatmentGiven { get; set; }

    /// <summary>Fraction of pain relieved by analgesia (0..0.7).</summary>
    public double Analgesia { get; set; }

    public DateTime? LastObservationsAt { get; set; }

    /// <summary>Care space currently occupied (or reserved), if any.</summary>
    public string? CareSpaceId { get; set; }

    /// <summary>The waiting-room seat the patient has taken, if any.</summary>
    public GridCoord? Seat { get; set; }

    /// <summary>Id of the task currently being done to this patient, so two staff don't treat them at once.</summary>
    public int? BusyWithTaskId { get; set; }

    /// <summary>Walking, wheelchair or trolley. Patients who can't walk wait for a porter to move them.</summary>
    public Mobility Mobility { get; set; } = Mobility.Walking;

    /// <summary>Where the patient is waiting to be pushed to, while they wait for a porter.</summary>
    public GridCoord? PendingMove { get; set; }

    /// <summary>The porter (or nurse) pushing the patient's wheelchair or trolley right now.</summary>
    public int? PushedByStaffId { get; set; }

    /// <summary>When the patient joined their current queue (for ordering by waiting time).</summary>
    public DateTime QueuedSince { get; set; }

    public DateTime? TransferAmbulanceDueAt { get; set; }

    public bool IsAlive => Outcome != PatientOutcome.Died;

    public bool IsInDepartment => Stage is not (PatientStage.Gone or PatientStage.Leaving) && IsAlive;

    /// <summary>Still in A&amp;E (in the hospital and not yet on a ward).</summary>
    public bool IsInEmergency => IsInDepartment && Stage != PatientStage.OnWard && LeftEmergencyAt is null;

    /// <summary>The ward bed (index into the ward's beds) when the patient is on, or going to, a ward.</summary>
    public int? BedIndex { get; set; }

    /// <summary>A ward bed held for the patient while they wait to be taken up.</summary>
    public string? ReservedWardId { get; set; }

    public int? ReservedBedIndex { get; set; }

    /// <summary>Has had an operation this stay (so can recover on the A&amp;E ward if their specialty has no ward).</summary>
    public bool HadSurgery { get; set; }

    /// <summary>Going to the A&amp;E ward only to be observed for a few hours before going home.</summary>
    public bool ForObservation { get; set; }

    /// <summary>Treated in the ambulatory care unit and going home the same day, instead of being admitted.</summary>
    public bool SameDay { get; set; }

    /// <summary>
    /// The level of care the patient needs now: 0 for a ward, 2 for high dependency (HDU), 3 for
    /// intensive care (ICU). Set on admission, after a big operation, by outreach and on ward rounds.
    /// </summary>
    public int CareLevel { get; set; }

    /// <summary>Had a day case operation: comes round on a day surgery unit and goes home the same day.</summary>
    public bool DayCase { get; set; }

    /// <summary>Waiting for theatre on an assessment unit (e.g. the surgical assessment unit) rather than in A&amp;E.</summary>
    public bool PreOp { get; set; }

    /// <summary>Already on a ward but needs a bed on another (up to ICU/HDU, down from them, or out of an assessment unit).</summary>
    public DateTime? WardMoveRequestedAt { get; set; }

    /// <summary>Hours spent on an intensive care unit this stay (paid for at the critical care rate).</summary>
    public double IntensiveCareHours { get; set; }

    /// <summary>Hours spent on a high dependency unit this stay.</summary>
    public double HighDependencyHours { get; set; }

    /// <summary>Physiotherapy sessions on the ward this stay.</summary>
    public int PhysioSessions { get; set; }

    public DateTime? LastPhysioAt { get; set; }

    /// <summary>When the doctor asked for a ward bed.</summary>
    public DateTime? BedRequestedAt { get; set; }

    /// <summary>When the patient left A&amp;E for a ward (this ends the A&amp;E 4-hour clock).</summary>
    public DateTime? LeftEmergencyAt { get; set; }

    /// <summary>When the patient arrived on the ward.</summary>
    public DateTime? AdmittedAt { get; set; }

    /// <summary>The earliest the ward team expect the patient to be ready to go home.</summary>
    public DateTime? DischargeDueAt { get; set; }

    /// <summary>When the medicines to take home will be ready; set once the ward round says they can go.</summary>
    public DateTime? MedicinesReadyAt { get; set; }

    public IReadOnlyList<TimelineEntry> Timeline => _timeline;

    public IReadOnlyList<(DateTime Time, Observations Obs, News2Score News2)> ObservationHistory => _observationHistory;

    public (Observations Obs, News2Score News2)? LatestObservations =>
        _observationHistory.Count == 0 ? null : (_observationHistory[^1].Obs, _observationHistory[^1].News2);

    public TimeSpan TimeInDepartment(DateTime now) => (LeftAt ?? now) - ArrivedAt;

    public void Log(DateTime time, string text, bool important = false) => _timeline.Add(new TimelineEntry(time, text, important));

    /// <summary>Puts back a saved timeline (loading a game).</summary>
    internal void RestoreTimeline(IEnumerable<TimelineEntry> entries) => _timeline.AddRange(entries);

    public void RecordObservations(DateTime time, Observations obs)
    {
        var score = News2.Calculate(obs);
        _observationHistory.Add((time, obs, score));
        LastObservationsAt = time;
    }

    /// <summary>The patient's true observations right now, from their condition and severity.</summary>
    public Observations CurrentObservations()
    {
        var b = BaselineObservations;
        var e = Condition.DeteriorationEffects;
        var delta = Severity - InitialSeverity;

        var consciousness = Severity switch
        {
            >= 0.95 => Consciousness.Pain,
            >= 0.85 => Consciousness.Voice,
            >= 0.75 => Consciousness.NewConfusion,
            _ => b.Consciousness,
        };

        var pain = (int)Math.Round(b.PainScore * (1 - Analgesia) * (Severity < 0.05 && EffectiveTreatmentGiven ? 0.5 : 1));

        return new Observations(
            HeartRate: Clamp(b.HeartRate + (e.HeartRate * delta), 25, 220),
            SystolicBp: Clamp(b.SystolicBp + (e.SystolicBp * delta), 40, 240),
            DiastolicBp: Clamp(b.DiastolicBp + (e.SystolicBp * 0.5 * delta), 20, 140),
            RespiratoryRate: Clamp(b.RespiratoryRate + (e.RespiratoryRate * delta), 6, 50),
            OxygenSaturation: Clamp(b.OxygenSaturation + (e.OxygenSaturation * delta), 60, 100),
            Temperature: Math.Round(Math.Clamp(b.Temperature + (e.Temperature * delta), 33, 42.5), 1),
            Consciousness: consciousness,
            PainScore: Math.Clamp(pain, 0, 10));
    }

    private static int Clamp(double value, int min, int max) => (int)Math.Round(Math.Clamp(value, min, max));
}
