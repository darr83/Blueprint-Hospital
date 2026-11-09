using BlueprintHospital.Simulation.Hospital;

namespace BlueprintHospital.Simulation.Data.Models;

/// <summary>How a patient leaves A&amp;E once treated.</summary>
public enum Disposition
{
    Discharge,
    Transfer,
    Admit,

    /// <summary>Needs an operation (<see cref="ConditionDefinition.Operation"/>), then a ward bed to recover in.</summary>
    Surgery,
}

/// <summary>Who carries out a task.</summary>
public enum ClinicalRole
{
    Nurse,
    Doctor,
}

public enum TreatmentStepType
{
    Medication,
    Procedure,
}

/// <summary>An operation or procedure, done in theatre (or another procedure room such as the cath lab).</summary>
public sealed class OperationDefinition
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Time in the room for an average team.</summary>
    public int Minutes { get; init; }

    /// <summary>Where it is done: an operating theatre unless it says otherwise (cath_lab).</summary>
    public CareSpaceKind Room { get; init; } = CareSpaceKind.Theatre;

    /// <summary>A big operation: the patient recovers on the high dependency unit first, if there is one.</summary>
    public bool Major { get; init; }

    /// <summary>A small operation: the patient comes round on a day surgery unit, if there is one, and goes home the same day.</summary>
    public bool DayCase { get; init; }
}

/// <summary>
/// Pregnancy and birth: a woman in labour (or with a pregnancy emergency) comes straight to the maternity
/// unit instead of A&amp;E, is looked after by midwives and has her baby by the condition's operation (a
/// birth in the delivery room, or a caesarean section in theatre).
/// </summary>
public sealed class MaternityDefinition
{
    /// <summary>False for something that only happens once she is in hospital (labour that stops progressing); such conditions never arrive.</summary>
    public bool Arrives { get; init; } = true;

    /// <summary>[earliest, latest] weeks of pregnancy.</summary>
    public int[] GestationWeeks { get; init; } = { 37, 42 };

    /// <summary>Chance each baby needs the neonatal unit after the birth.</summary>
    public double NeonatalChance { get; init; }

    /// <summary>Chance labour in the delivery room ends in an emergency caesarean section instead.</summary>
    public double EmergencyCaesareanChance { get; init; }

    /// <summary>The condition (with a theatre operation) used when labour ends in an emergency caesarean section.</summary>
    public string? EmergencyCaesarean { get; init; }

    /// <summary>Chance of twins.</summary>
    public double TwinsChance { get; init; }

    /// <summary>Why a baby from this birth needs the neonatal unit, shown in the baby's notes.</summary>
    public IReadOnlyList<string> NeonatalReasons { get; init; } = Array.Empty<string>();

    /// <summary>[shortest, longest] hours a baby from this birth stays on the neonatal unit.</summary>
    public double[] NeonatalStayHours { get; init; } = { 24, 96 };

    internal void Validate(ValidationContext context, ConditionDefinition owner)
    {
        if (GestationWeeks.Length != 2 || GestationWeeks[0] < 20 || GestationWeeks[1] > 43 || GestationWeeks[0] > GestationWeeks[1])
        {
            context.Error("maternity.gestation_weeks", "must be [earliest, latest] weeks between 20 and 43.");
        }

        context.RequireRange("maternity.neonatal_chance", NeonatalChance, 0, 1);
        context.RequireRange("maternity.emergency_caesarean_chance", EmergencyCaesareanChance, 0, 1);
        context.RequireRange("maternity.twins_chance", TwinsChance, 0, 0.5);
        if (EmergencyCaesareanChance > 0 && string.IsNullOrWhiteSpace(EmergencyCaesarean))
        {
            context.Error("maternity.emergency_caesarean", "must name the caesarean section condition when emergency_caesarean_chance is above 0.");
        }

        if (NeonatalChance > 0 && NeonatalReasons.Count == 0)
        {
            context.Error("maternity.neonatal_reasons", "list at least one reason a baby needs the neonatal unit.");
        }

        if (NeonatalStayHours.Length != 2 || NeonatalStayHours[0] < 1 || NeonatalStayHours[1] < NeonatalStayHours[0] || NeonatalStayHours[1] > 24 * 60)
        {
            context.Error("maternity.neonatal_stay_hours", "must be [shortest, longest] in hours, between 1 and 1440.");
        }

        if (owner.Disposition != Disposition.Surgery || owner.Operation is null)
        {
            context.Error("maternity", "maternity conditions need disposition surgery and an operation (the birth).");
        }
    }
}

public sealed class SymptomChance
{
    public string Symptom { get; init; } = string.Empty;

    public double Probability { get; init; }
}

/// <summary>Mean and standard deviation of an observation at presentation.</summary>
public sealed class ObservationProfile
{
    /// <summary>[mean, standard deviation] in beats per minute.</summary>
    public double[] HeartRate { get; init; } = Array.Empty<double>();

    public double[] SystolicBp { get; init; } = Array.Empty<double>();

    public double[] DiastolicBp { get; init; } = Array.Empty<double>();

    public double[] RespiratoryRate { get; init; } = Array.Empty<double>();

    public double[] OxygenSaturation { get; init; } = Array.Empty<double>();

    public double[] Temperature { get; init; } = Array.Empty<double>();

    /// <summary>[minimum, maximum] pain score out of 10.</summary>
    public double[] PainScore { get; init; } = Array.Empty<double>();

    internal void Validate(ValidationContext context)
    {
        CheckPair(context, "observations.heart_rate", HeartRate, 20, 250);
        CheckPair(context, "observations.systolic_bp", SystolicBp, 40, 260);
        CheckPair(context, "observations.diastolic_bp", DiastolicBp, 20, 160);
        CheckPair(context, "observations.respiratory_rate", RespiratoryRate, 4, 60);
        CheckPair(context, "observations.oxygen_saturation", OxygenSaturation, 50, 100);
        CheckPair(context, "observations.temperature", Temperature, 30, 43);
        CheckPair(context, "observations.pain_score", PainScore, 0, 10);
    }

    private static void CheckPair(ValidationContext context, string field, double[] values, double min, double max)
    {
        if (values.Length != 2)
        {
            context.Error(field, "must be a pair of numbers.");
            return;
        }

        context.RequireRange(field + "[0]", values[0], min, max);
    }
}

/// <summary>How much each observation changes when severity reaches 1 (the patient is peri-arrest).</summary>
public sealed class DeteriorationEffects
{
    public double HeartRate { get; init; }

    public double SystolicBp { get; init; }

    public double RespiratoryRate { get; init; }

    public double OxygenSaturation { get; init; }

    public double Temperature { get; init; }
}

public sealed class TreatmentStep
{
    public TreatmentStepType Type { get; init; }

    /// <summary>For medication steps: the medication id.</summary>
    public string? Medication { get; init; }

    /// <summary>For procedure steps: what is done.</summary>
    public string? Name { get; init; }

    public ClinicalRole PerformedBy { get; init; } = ClinicalRole.Nurse;

    public int Minutes { get; init; } = 5;

    /// <summary>Essential steps must be given for the patient to start recovering.</summary>
    public bool Essential { get; init; }

    /// <summary>
    /// For procedure steps: a specialist room that does it better (plaster_room). When that room is
    /// staffed, its specialist does it there; otherwise A&amp;E staff do it in the cubicle.
    /// </summary>
    public CareSpaceKind? Room { get; init; }

    public string Describe() => Type == TreatmentStepType.Medication ? Medication ?? "?" : Name ?? "?";
}

/// <summary>A real medical condition with how it presents, how it is diagnosed and how it is managed. Loaded from Data/Conditions.</summary>
public sealed class ConditionDefinition : IDataRecord
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Department id that owns this condition.</summary>
    public string Department { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>Relative frequency among A&amp;E attendances.</summary>
    public double Prevalence { get; init; }

    public int[] AgeRange { get; init; } = Array.Empty<int>();

    public double FemaleFraction { get; init; } = 0.5;

    public double AmbulanceProbability { get; init; }

    /// <summary>What an ambulance crew would say on a pre-alert, e.g. "potential heart attack". Spoken on the tannoy.</summary>
    public string CrewAssessment { get; init; } = string.Empty;

    /// <summary>The symptom the patient gives as their reason for coming. Always present.</summary>
    public string PresentingComplaint { get; init; } = string.Empty;

    public IReadOnlyList<SymptomChance> Symptoms { get; init; } = Array.Empty<SymptomChance>();

    public ObservationProfile Observations { get; init; } = new();

    /// <summary>[min, max] severity on arrival, where 0 is well and 1 is peri-arrest.</summary>
    public double[] InitialSeverity { get; init; } = Array.Empty<double>();

    /// <summary>Severity added per hour while the condition is untreated.</summary>
    public double DeteriorationPerHour { get; init; }

    public DeteriorationEffects DeteriorationEffects { get; init; } = new();

    /// <summary>If true the patient can die when severity reaches 1.</summary>
    public bool CanBeFatal { get; init; }

    /// <summary>Investigations a doctor needs before confidently diagnosing this condition.</summary>
    public IReadOnlyList<string> KeyInvestigations { get; init; } = Array.Empty<string>();

    public IReadOnlyList<TreatmentStep> Treatment { get; init; } = Array.Empty<TreatmentStep>();

    /// <summary>Severity removed per hour once all essential treatment has been given.</summary>
    public double RecoveryPerHour { get; init; }

    public Disposition Disposition { get; init; }

    public string DispositionNote { get; init; } = string.Empty;

    /// <summary>The operation, for conditions whose disposition is surgery.</summary>
    public OperationDefinition? Operation { get; init; }

    /// <summary>For admitted patients: [shortest, longest] stay on the ward in hours.</summary>
    public double[] LengthOfStayHours { get; init; } = { 24, 72 };

    /// <summary>
    /// For patients who go home: [shortest, longest] hours of observation on the A&amp;E ward first,
    /// when it has a free bed (e.g. 4 to 6 hours after anaphylaxis). Null if they can go straight home.
    /// </summary>
    public double[]? ObserveHours { get; init; }

    /// <summary>
    /// For patients who would be admitted: [shortest, longest] hours of same-day emergency care in an
    /// ambulatory care unit instead, when it has room and they are not Red (e.g. a pulmonary embolism).
    /// </summary>
    public double[]? AmbulatoryHours { get; init; }

    /// <summary>Patients who need intensive care when they are admitted, however they look (e.g. septic shock).</summary>
    public bool NeedsIntensiveCare { get; init; }

    /// <summary>Ward patients who need physiotherapy to get home (after a broken hip or a stroke).</summary>
    public bool NeedsPhysio { get; init; }

    /// <summary>Labour and pregnancy emergencies: the patient comes straight to the maternity unit and has her baby here. Null for everything else.</summary>
    public MaternityDefinition? Maternity { get; init; }

    /// <summary>Women in labour come to the maternity unit, not A&amp;E.</summary>
    public bool IsMaternity => Maternity is not null;

    public double ChanceOf(string symptomId) =>
        Symptoms.FirstOrDefault(s => s.Symptom == symptomId)?.Probability ?? 0;

    public void Validate(ValidationContext context)
    {
        context.RequireId("id", Id);
        context.RequireText("name", Name);
        context.RequireId("department", Department);
        context.RequireRange("prevalence", Prevalence, 0.0001, 1000);
        if (AgeRange.Length != 2 || AgeRange[0] < 0 || AgeRange[1] > 110 || AgeRange[0] > AgeRange[1])
        {
            context.Error("age_range", "must be [youngest, oldest] between 0 and 110.");
        }

        context.RequireRange("female_fraction", FemaleFraction, 0, 1);
        context.RequireRange("ambulance_probability", AmbulanceProbability, 0, 1);
        context.RequireId("presenting_complaint", PresentingComplaint);

        if (Symptoms.Count == 0)
        {
            context.Error("symptoms", "must list at least one symptom.");
        }

        for (var i = 0; i < Symptoms.Count; i++)
        {
            context.RequireId($"symptoms[{i}].symptom", Symptoms[i].Symptom);
            context.RequireRange($"symptoms[{i}].probability", Symptoms[i].Probability, 0, 1);
        }

        if (Symptoms.All(s => s.Symptom != PresentingComplaint))
        {
            context.Error("presenting_complaint", $"'{PresentingComplaint}' must also be listed in symptoms.");
        }

        Observations.Validate(context);

        if (InitialSeverity.Length != 2 || InitialSeverity[0] < 0 || InitialSeverity[1] > 1 || InitialSeverity[0] > InitialSeverity[1])
        {
            context.Error("initial_severity", "must be [min, max] between 0 and 1.");
        }

        context.RequireRange("deterioration_per_hour", DeteriorationPerHour, 0, 5);
        context.RequireRange("recovery_per_hour", RecoveryPerHour, 0, 5);
        context.RequireIds("key_investigations", KeyInvestigations);

        for (var i = 0; i < Treatment.Count; i++)
        {
            var step = Treatment[i];
            if (step.Type == TreatmentStepType.Medication)
            {
                context.RequireId($"treatment[{i}].medication", step.Medication);
            }
            else
            {
                context.RequireText($"treatment[{i}].name", step.Name);
                context.RequireRange($"treatment[{i}].minutes", step.Minutes, 1, 600);
                if (step.Room is { } room && room != CareSpaceKind.PlasterRoom)
                {
                    context.Error($"treatment[{i}].room", "must be plaster_room.");
                }
            }
        }

        if (AmbulatoryHours is { } sameDay && (Disposition != Disposition.Admit || sameDay.Length != 2 || sameDay[0] < 1 || sameDay[1] < sameDay[0] || sameDay[1] > 12))
        {
            context.Error("ambulatory_hours", "must be [shortest, longest] in hours, up to 12, and only for admitted conditions.");
        }

        context.RequireText("disposition_note", DispositionNote);
        if (NeedsIntensiveCare && Disposition != Disposition.Admit)
        {
            context.Error("needs_intensive_care", "only applies to admitted conditions.");
        }

        if (Disposition == Disposition.Surgery)
        {
            if (Operation is null)
            {
                context.Error("operation", "is required when the disposition is surgery.");
            }
            else
            {
                context.RequireText("operation.name", Operation.Name);
                context.RequireRange("operation.minutes", Operation.Minutes, 10, 600);
                if (Operation.Room is not (CareSpaceKind.Theatre or CareSpaceKind.CathLab or CareSpaceKind.DeliveryRoom))
                {
                    context.Error("operation.room", "must be theatre, cath_lab or delivery_room.");
                }

                if (Operation.Room == CareSpaceKind.DeliveryRoom && Maternity is null)
                {
                    context.Error("operation.room", "only maternity conditions can use the delivery room.");
                }

                if (Operation.Major && Operation.DayCase)
                {
                    context.Error("operation", "can't be both major and a day case.");
                }
            }
        }

        Maternity?.Validate(context, this);

        if (LengthOfStayHours.Length != 2 || LengthOfStayHours[0] < 1 || LengthOfStayHours[1] < LengthOfStayHours[0] || LengthOfStayHours[1] > 24 * 60)
        {
            context.Error("length_of_stay_hours", "must be [shortest, longest] in hours, between 1 and 1440.");
        }
    }
}
