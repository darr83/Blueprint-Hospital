using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Hospital;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Every line the simulation can send to the tannoy has been recorded in both announcer voices.</summary>
public class TannoyRecordingTests
{
    private static readonly string Pa = Path.Combine(Path.GetDirectoryName(TestData.GameDataDirectory())!, "Audio", "Sounds", "pa");

    private static void AssertRecorded(IEnumerable<string> clips)
    {
        var missing = clips.SelectMany(c => new[] { $"{c}_f.ogg", $"{c}_m.ogg" })
            .Where(f => !File.Exists(Path.Combine(Pa, f)))
            .ToList();
        Assert.True(missing.Count == 0, "Not recorded (run tools/SoundBaker/generate_pa.py): " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_ward_has_a_crash_call()
    {
        var db = GameDatabase.Load(new FileSystemDataSource(TestData.GameDataDirectory()));
        // Maternity wards use the obstetric emergency and paediatric arrest calls instead.
        var wards = db.RoomTemplates.Values.Where(t => t.Kind == CareSpaceKind.Ward && t.Category != "Maternity");
        AssertRecorded(wards.Select(t => $"arrest_{t.Id}"));
    }

    [Fact]
    public void Every_other_crash_call_and_ambulance_pre_alert_is_recorded()
    {
        var clips = new List<string> { "arrest_ae", "arrest_majors", "arrest_x", "paed_arrest", "obstetric_emergency", "amb_due_5", "amb_due_10" };
        for (var bay = 1; bay <= 6; bay++)
        {
            clips.Add($"arrest_resus_{bay}");
        }

        clips.AddRange(new[] { "theatre", "xray", "ct", "cathlab", "endoscopy", "physiogym", "endoscopyrecovery", "plasterroom", "clinic", "echo", "eeg", "ultrasound" }
            .Select(k => $"arrest_{k}"));
        AssertRecorded(clips);
    }

    [Fact]
    public void Every_ambulance_condition_has_a_recorded_crew_assessment()
    {
        var db = GameDatabase.Load(new FileSystemDataSource(TestData.GameDataDirectory()));
        AssertRecorded(db.Conditions.Values.Where(c => !string.IsNullOrWhiteSpace(c.CrewAssessment)).Select(c => $"assess_{c.Id}"));
    }
}
