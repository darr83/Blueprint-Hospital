namespace BlueprintHospital.Simulation.Staff;

/// <summary>
/// Grades and pay. Nurses are on bands 5 to 9 by how good they are; doctors are Junior Doctor,
/// Doctor, Registrar or Consultant. Other roles have one grade each.
/// </summary>
public static class StaffGrades
{
    public sealed record Grade(StaffRole Role, string Title, double MinSkill, double MaxSkill, int MinYears, int MaxYears, int Salary, double Weight);

    /// <summary>Every grade, lowest first within each role. Weight is how often a candidate of that grade applies.</summary>
    public static readonly IReadOnlyList<Grade> All = new[]
    {
        new Grade(StaffRole.Nurse, "Band 5 nurse", 0.45, 0.60, 0, 3, 31_000, 5),
        new Grade(StaffRole.Nurse, "Band 6 nurse", 0.60, 0.70, 3, 7, 39_000, 4),
        new Grade(StaffRole.Nurse, "Band 7 nurse", 0.70, 0.80, 6, 12, 48_000, 3),
        new Grade(StaffRole.Nurse, "Band 8 nurse", 0.80, 0.90, 10, 18, 60_000, 1.5),
        new Grade(StaffRole.Nurse, "Band 9 nurse", 0.90, 0.97, 15, 25, 100_000, 0.5),
        new Grade(StaffRole.Doctor, "Junior Doctor", 0.45, 0.60, 0, 2, 38_000, 4),
        new Grade(StaffRole.Doctor, "Doctor", 0.60, 0.72, 2, 5, 55_000, 3),
        new Grade(StaffRole.Doctor, "Registrar", 0.72, 0.85, 4, 9, 66_000, 2),
        new Grade(StaffRole.Doctor, "Consultant", 0.85, 0.97, 9, 25, 110_000, 1),
        new Grade(StaffRole.Radiographer, "Radiographer", 0.60, 0.85, 1, 15, 37_000, 2),
        new Grade(StaffRole.Surgeon, "Consultant surgeon", 0.80, 0.97, 10, 25, 112_000, 1),
        new Grade(StaffRole.Pharmacist, "Pharmacist", 0.60, 0.85, 1, 15, 46_000, 1.5),
        new Grade(StaffRole.Porter, "Porter", 0.30, 0.50, 0, 15, 24_000, 2.5),
        new Grade(StaffRole.CardiacPhysiologist, "Cardiac physiologist", 0.55, 0.85, 1, 15, 38_000, 1),
        new Grade(StaffRole.Neurophysiologist, "Clinical neurophysiologist", 0.55, 0.85, 1, 15, 38_000, 1),
        new Grade(StaffRole.Sonographer, "Sonographer", 0.55, 0.85, 1, 15, 42_000, 1),
        new Grade(StaffRole.Endoscopist, "Consultant endoscopist", 0.75, 0.95, 6, 25, 95_000, 0.8),
        new Grade(StaffRole.PlasterTechnician, "Plaster technician", 0.50, 0.80, 0, 20, 28_000, 1),
        new Grade(StaffRole.Physiotherapist, "Physiotherapist", 0.55, 0.85, 1, 15, 37_000, 1.2),
        new Grade(StaffRole.Anaesthetist, "Anaesthetic registrar", 0.70, 0.85, 4, 9, 68_000, 1),
        new Grade(StaffRole.Anaesthetist, "Consultant anaesthetist", 0.85, 0.97, 9, 25, 112_000, 0.6),
        new Grade(StaffRole.OutreachNurse, "Outreach nurse", 0.65, 0.90, 5, 20, 49_000, 0.8),
        new Grade(StaffRole.HealthcareAssistant, "Healthcare assistant (Band 3)", 0.35, 0.60, 0, 15, 25_000, 2.5),
        new Grade(StaffRole.Receptionist, "Receptionist", 0.40, 0.60, 0, 15, 24_000, 1.5),
        new Grade(StaffRole.Midwife, "Band 5 midwife", 0.45, 0.60, 0, 3, 31_000, 3),
        new Grade(StaffRole.Midwife, "Band 6 midwife", 0.60, 0.72, 2, 8, 39_000, 3),
        new Grade(StaffRole.Midwife, "Band 7 midwife", 0.72, 0.85, 6, 15, 48_000, 1.5),
        new Grade(StaffRole.Midwife, "Band 8 consultant midwife", 0.85, 0.97, 12, 30, 62_000, 0.5),
    };

    public static IEnumerable<Grade> For(StaffRole role) => All.Where(g => g.Role == role);

    /// <summary>The grade that fits someone in <paramref name="role"/> with this skill.</summary>
    public static Grade Of(StaffRole role, double skill)
    {
        var grades = For(role).ToList();
        return grades.LastOrDefault(g => skill >= g.MinSkill) ?? grades[0];
    }

    /// <summary>Senior nurses (Band 7 and above) wear navy.</summary>
    public static bool IsSeniorNurse(StaffMember member) =>
        member.Role == StaffRole.Nurse && member.Grade is "Band 7 nurse" or "Band 8 nurse" or "Band 9 nurse";

    /// <summary>The role in plain words, e.g. "nurse" or "radiographer".</summary>
    public static string RoleName(StaffRole role, bool plural = false) => (role, plural) switch
    {
        (StaffRole.Receptionist, false) => "receptionist",
        (StaffRole.Receptionist, true) => "receptionists",
        (StaffRole.Nurse, false) => "nurse",
        (StaffRole.Nurse, true) => "nurses",
        (StaffRole.Doctor, false) => "doctor",
        (StaffRole.Doctor, true) => "doctors",
        (StaffRole.Radiographer, false) => "radiographer",
        (StaffRole.Radiographer, true) => "radiographers",
        (StaffRole.Surgeon, false) => "surgeon",
        (StaffRole.Surgeon, true) => "surgeons",
        (StaffRole.Pharmacist, false) => "pharmacist",
        (StaffRole.Pharmacist, true) => "pharmacists",
        (StaffRole.Porter, false) => "porter",
        (StaffRole.Porter, true) => "porters",
        (StaffRole.CardiacPhysiologist, false) => "cardiac physiologist",
        (StaffRole.CardiacPhysiologist, true) => "cardiac physiologists",
        (StaffRole.Neurophysiologist, false) => "neurophysiologist",
        (StaffRole.Neurophysiologist, true) => "neurophysiologists",
        (StaffRole.Sonographer, false) => "sonographer",
        (StaffRole.Sonographer, true) => "sonographers",
        (StaffRole.Endoscopist, false) => "endoscopist",
        (StaffRole.Endoscopist, true) => "endoscopists",
        (StaffRole.PlasterTechnician, false) => "plaster technician",
        (StaffRole.PlasterTechnician, true) => "plaster technicians",
        (StaffRole.Physiotherapist, false) => "physiotherapist",
        (StaffRole.Physiotherapist, true) => "physiotherapists",
        (StaffRole.Anaesthetist, false) => "anaesthetist",
        (StaffRole.Anaesthetist, true) => "anaesthetists",
        (StaffRole.OutreachNurse, false) => "outreach nurse",
        (StaffRole.OutreachNurse, true) => "outreach nurses",
        (StaffRole.HealthcareAssistant, false) => "healthcare assistant",
        (StaffRole.Midwife, false) => "midwife",
        (StaffRole.Midwife, true) => "midwives",
        _ => "healthcare assistants",
    };
}

/// <summary>Someone applying for a job. A fresh list arrives every morning.</summary>
public sealed class JobCandidate
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required StaffRole Role { get; init; }

    public required string Grade { get; init; }

    public required double Skill { get; init; }

    public required double Speed { get; init; }

    public required int YearsExperience { get; init; }

    public required int Age { get; init; }

    /// <summary>Annual salary in pounds. Each hire covers one 12-hour shift a day (see the finances).</summary>
    public required int Salary { get; init; }
}
