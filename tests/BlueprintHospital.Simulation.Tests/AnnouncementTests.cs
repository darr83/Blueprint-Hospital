using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Tests;

public class AnnouncementTests
{
    [Fact]
    public void Doctor_is_called_over_the_tannoy_before_walking_to_resus()
    {
        var h = new SimulationHarness(seed: 7);
        var calls = new List<Announcement>();
        h.Events.Subscribe<Announcement>(a => calls.Add(a));
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_myocardial_infarction");

        h.RunUntil(() => calls.Any(c => c.Kind == AnnouncementKind.StaffCall), TimeSpan.FromHours(2), "a doctor to be called to resus");

        var call = calls.First(c => c.Kind == AnnouncementKind.StaffCall);
        Assert.Matches(@"^(Doctor|Registrar|Consultant) is required at Resus( \d)?\.$", call.Text);
        Assert.Single(call.Clips);
        Assert.StartsWith("call_", call.Clips[0]);

        // The doctor stays put while the call plays, then sets off.
        var doctor = h.Sim.Staff.Single(s => s.PagedUntil is not null);
        var start = doctor.Tile;
        h.RunFor(HospitalSimulation.PageDelay - TimeSpan.FromSeconds(5));
        Assert.Equal(start, doctor.Tile);
        h.RunUntil(() => patient.FirstSeenByDoctorAt is not null, TimeSpan.FromHours(1), "the doctor to see the patient");
        Assert.Null(doctor.PagedUntil);
    }

    [Fact]
    public void Serious_ambulances_phone_ahead_and_arrive_when_due()
    {
        var h = new SimulationHarness(seed: 3, arrivals: true, arrivalMultiplier: 3);
        var alerts = new List<(DateTime At, Announcement A)>();
        h.Events.Subscribe<Announcement>(a =>
        {
            if (a.Kind == AnnouncementKind.AmbulancePreAlert)
            {
                alerts.Add((h.Clock.Now, a));
            }
        });

        h.RunUntil(() => alerts.Count > 0, TimeSpan.FromHours(12), "an ambulance pre-alert");
        var (at, alert) = alerts[0];
        Assert.Matches(@"^Ambulance due in (five|ten) minutes\. Assessment: .+\.$", alert.Text);
        Assert.Equal(2, alert.Clips.Count);
        var conditionId = alert.Clips[1]["assess_".Length..];
        var due = at.AddMinutes(alert.Clips[0] == "amb_due_5" ? 5 : 10);

        h.RunUntil(() => h.Clock.Now >= due, TimeSpan.FromMinutes(11), "the due time");
        Assert.Contains(h.Sim.Patients, p => p.Condition.Id == conditionId && p.ArrivalMethod == ArrivalMethod.Ambulance && p.ArrivedAt >= due.AddSeconds(-5));
    }

    [Fact]
    public void Every_condition_has_a_crew_assessment_for_the_pre_alert()
    {
        var h = new SimulationHarness();
        Assert.All(h.Database.Conditions.Values, c => Assert.False(string.IsNullOrWhiteSpace(c.CrewAssessment), c.Id));
    }

    [Fact]
    public void Doctors_are_called_by_their_grade()
    {
        var h = new SimulationHarness();
        foreach (var member in h.Sim.Staff.Where(s => s.Role == StaffRole.Doctor))
        {
            var caller = HospitalSimulation.CallerFor(member);
            Assert.Contains(caller, new[] { "Doctor", "Registrar", "Consultant" });
        }
    }
}
