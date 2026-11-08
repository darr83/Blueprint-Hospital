namespace BlueprintHospital.Simulation.Medical;

/// <summary>ACVPU consciousness scale used by NEWS2.</summary>
public enum Consciousness
{
    Alert,
    NewConfusion,
    Voice,
    Pain,
    Unresponsive,
}

/// <summary>A set of vital signs taken at one moment.</summary>
public sealed record Observations(
    int HeartRate,
    int SystolicBp,
    int DiastolicBp,
    int RespiratoryRate,
    int OxygenSaturation,
    double Temperature,
    Consciousness Consciousness,
    int PainScore,
    bool OnSupplementalOxygen = false)
{
    public string BloodPressure => $"{SystolicBp}/{DiastolicBp}";

    public override string ToString() =>
        $"HR {HeartRate}, BP {BloodPressure}, RR {RespiratoryRate}, SpO2 {OxygenSaturation}%, T {Temperature:0.0}°C, {Consciousness}, pain {PainScore}/10";
}

/// <summary>A NEWS2 score with the per-parameter breakdown.</summary>
public sealed record News2Score(int Total, int RespiratoryRate, int OxygenSaturation, int SupplementalOxygen, int SystolicBp, int HeartRate, int Consciousness, int Temperature)
{
    /// <summary>True if any single parameter scores 3 (a "red score"), which needs urgent review.</summary>
    public bool HasRedScore => new[] { RespiratoryRate, OxygenSaturation, SystolicBp, HeartRate, Consciousness, Temperature }.Any(s => s >= 3);

    public News2Risk Risk => Total >= 7 ? News2Risk.High
        : Total >= 5 ? News2Risk.Medium
        : HasRedScore ? News2Risk.LowMedium
        : News2Risk.Low;
}

public enum News2Risk
{
    Low,
    LowMedium,
    Medium,
    High,
}

/// <summary>
/// National Early Warning Score 2 (Royal College of Physicians, 2017), using SpO2 scale 1.
/// </summary>
public static class News2
{
    public static News2Score Calculate(Observations o)
    {
        var rr = o.RespiratoryRate switch
        {
            <= 8 => 3,
            <= 11 => 1,
            <= 20 => 0,
            <= 24 => 2,
            _ => 3,
        };

        var spo2 = o.OxygenSaturation switch
        {
            <= 91 => 3,
            <= 93 => 2,
            <= 95 => 1,
            _ => 0,
        };

        var air = o.OnSupplementalOxygen ? 2 : 0;

        var sbp = o.SystolicBp switch
        {
            <= 90 => 3,
            <= 100 => 2,
            <= 110 => 1,
            <= 219 => 0,
            _ => 3,
        };

        var pulse = o.HeartRate switch
        {
            <= 40 => 3,
            <= 50 => 1,
            <= 90 => 0,
            <= 110 => 1,
            <= 130 => 2,
            _ => 3,
        };

        var conscious = o.Consciousness == Consciousness.Alert ? 0 : 3;

        // Temperature bands are defined to one decimal place.
        var t = Math.Round(o.Temperature, 1);
        var temp = t switch
        {
            <= 35.0 => 3,
            <= 36.0 => 1,
            <= 38.0 => 0,
            <= 39.0 => 1,
            _ => 2,
        };

        return new News2Score(rr + spo2 + air + sbp + pulse + conscious + temp, rr, spo2, air, sbp, pulse, conscious, temp);
    }

    /// <summary>How often observations should be repeated in A&amp;E for this score.</summary>
    public static TimeSpan ObservationInterval(News2Score score) => score.Risk switch
    {
        News2Risk.High => TimeSpan.FromMinutes(15),
        News2Risk.Medium => TimeSpan.FromMinutes(30),
        News2Risk.LowMedium => TimeSpan.FromMinutes(60),
        _ => TimeSpan.FromMinutes(120),
    };
}
