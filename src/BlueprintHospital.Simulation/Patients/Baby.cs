namespace BlueprintHospital.Simulation.Patients;

/// <summary>Where a newborn baby is.</summary>
public enum BabyPlace
{
    /// <summary>With mum: on her chest after the birth, in the cot beside her bed, or in her arms.</summary>
    WithMother,

    /// <summary>Being carried between mum and the neonatal unit by a nurse.</summary>
    Carried,

    /// <summary>In an incubator on the neonatal unit.</summary>
    NeonatalUnit,

    /// <summary>Sent to another hospital's neonatal unit.</summary>
    Transferred,

    /// <summary>Gone home.</summary>
    Home,
}

/// <summary>
/// A baby born in the hospital. Babies stay with their mother (in the cot beside her bed) unless they
/// need the neonatal unit, and go home with her.
/// </summary>
public sealed class Baby
{
    private readonly List<TimelineEntry> _timeline = new();

    public required int Id { get; init; }

    public required string FirstName { get; set; }

    public required string Surname { get; set; }

    public string FullName => $"{FirstName} {Surname}";

    public required Sex Sex { get; init; }

    /// <summary>Birth weight in kilograms.</summary>
    public required double WeightKg { get; init; }

    public required int GestationWeeks { get; init; }

    public required DateTime BornAt { get; init; }

    /// <summary>The mother's patient id.</summary>
    public required int MotherId { get; init; }

    /// <summary>"Normal vaginal birth" or "Emergency caesarean section".</summary>
    public required string Delivery { get; init; }

    public BabyPlace Place { get; set; } = BabyPlace.WithMother;

    /// <summary>Why the baby needs (or needed) the neonatal unit; null for a well baby.</summary>
    public string? NeonatalReason { get; set; }

    /// <summary>Needs the neonatal unit and isn't there yet.</summary>
    public bool NeedsNeonatalUnit { get; set; }

    /// <summary>0 = well, 1 = very unwell. Only babies who need the neonatal unit are unwell.</summary>
    public double Severity { get; set; }

    /// <summary>The neonatal unit and incubator the baby is in (or is being taken to).</summary>
    public string? UnitId { get; set; }

    public int? CotIndex { get; set; }

    /// <summary>The nurse carrying the baby right now.</summary>
    public int? CarriedByStaffId { get; set; }

    public DateTime? AdmittedToNeonatalAt { get; set; }

    /// <summary>The earliest the neonatal team expect the baby to be well enough to leave the unit.</summary>
    public DateTime? NeonatalDischargeDueAt { get; set; }

    public DateTime? LeftAt { get; set; }

    /// <summary>
    /// The baby's vital signs on the incubator monitor: newborn ranges (heart rate 120-160, breathing 40-60),
    /// faster and lower in oxygen the more unwell the baby is.
    /// </summary>
    public Medical.Observations CurrentObservations()
    {
        var s = Math.Clamp(Severity, 0, 1);
        var heart = 128 + (Id % 13) + (int)Math.Round(s * 40);
        var breathing = 42 + (Id % 7) + (int)Math.Round(s * 24);
        var oxygen = Math.Max(84, 98 - (Id % 2) - (int)Math.Round(s * 14));
        var systolic = Math.Clamp(30 + GestationWeeks, 50, 75);
        return new Medical.Observations(heart, systolic, systolic - 25, breathing, oxygen, Math.Round(37.0 - (s * 0.8), 1), Medical.Consciousness.Alert, 0, OnSupplementalOxygen: s >= 0.4);
    }

    /// <summary>The incubator monitor's colour: green when settled, amber when poorly, red when very unwell.</summary>
    public Medical.TriageCategory MonitorColour => Severity >= 0.6 ? Medical.TriageCategory.Red : Severity >= 0.4 ? Medical.TriageCategory.Amber : Medical.TriageCategory.Green;

    public bool IsInHospital => Place is BabyPlace.WithMother or BabyPlace.Carried or BabyPlace.NeonatalUnit;

    /// <summary>Hours old at <paramref name="now"/>.</summary>
    public double HoursOld(DateTime now) => (now - BornAt).TotalHours;

    public IReadOnlyList<TimelineEntry> Timeline => _timeline;

    public void Log(DateTime time, string text, bool important = false) => _timeline.Add(new TimelineEntry(time, text, important));

    internal void RestoreTimeline(IEnumerable<TimelineEntry> entries) => _timeline.AddRange(entries);
}
