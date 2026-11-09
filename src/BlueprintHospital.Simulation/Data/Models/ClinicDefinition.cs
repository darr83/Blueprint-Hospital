using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Data.Models;

/// <summary>
/// How a clinic room runs its appointments. Loaded from Data/Clinics; the id is the clinic's room
/// template id (e.g. "cardiology_clinic").
/// </summary>
public sealed class ClinicDefinition : IDataRecord
{
    public string Id { get; init; } = string.Empty;

    /// <summary>Length of each booked appointment, in minutes.</summary>
    public int AppointmentMinutes { get; init; } = 20;

    /// <summary>What the hospital is paid for each patient seen, in pounds.</summary>
    public int Tariff { get; init; }

    /// <summary>Why patients are booked in, shown on the calendar.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();

    /// <summary>Only women are booked (gynaecology).</summary>
    public bool FemaleOnly { get; init; }

    /// <summary>Who sees the patients: a doctor unless it says otherwise (a midwife in the antenatal clinic, a sonographer in the scan room).</summary>
    public StaffRole Clinician { get; init; } = StaffRole.Doctor;

    /// <summary>[youngest, oldest] patients booked. Left out: 18 to 92 (18 to 80 for women-only clinics).</summary>
    public int[]? AgeRange { get; init; }

    public void Validate(ValidationContext context)
    {
        context.RequireId("id", Id);
        context.RequireRange("appointment_minutes", AppointmentMinutes, 5, 120);
        context.RequireRange("tariff", Tariff, 0, 100_000);
        if (AgeRange is { } ages && (ages.Length != 2 || ages[0] < 0 || ages[1] > 110 || ages[0] > ages[1]))
        {
            context.Error("age_range", "must be [youngest, oldest] between 0 and 110.");
        }

        if (Clinician is not (StaffRole.Doctor or StaffRole.Midwife or StaffRole.Sonographer or StaffRole.Nurse))
        {
            context.Error("clinician", "must be doctor, midwife, sonographer or nurse.");
        }

        if (Reasons.Count == 0)
        {
            context.Error("reasons", "list at least one reason for an appointment.");
        }
    }
}
