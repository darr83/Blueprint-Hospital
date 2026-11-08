using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>
/// A red monitor needs a healthcare assistant sitting with the patient until it clears, and a doctor's review;
/// only with both do they get better.
/// </summary>
public class RedMonitorTests
{
    private static Patient RedPatient(SimulationHarness h, string condition = "septic_shock")
    {
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, condition);
        h.RunUntil(() => patient.MonitorIsRed, TimeSpan.FromHours(2), "the patient's monitor to go red");
        return patient;
    }

    [Fact]
    public void A_healthcare_assistant_sits_with_a_red_patient_until_it_clears_and_a_doctor_reviews_then_leaves()
    {
        var h = new SimulationHarness(seed: 5);
        Assert.Contains(h.Sim.Staff, s => s.Role == StaffRole.HealthcareAssistant && s.IsOnDuty);
        var patient = RedPatient(h);

        StaffMember? sitter = null;
        h.RunUntil(() => (sitter = h.Sim.CarerAtBedside(patient)) is not null, TimeSpan.FromHours(1), "someone to sit with the patient");
        Assert.Equal(StaffRole.HealthcareAssistant, sitter!.Role);
        h.RunUntil(() => patient.RedReviewed, TimeSpan.FromHours(1), "a doctor's review");
        Assert.Contains("Red monitor reviewed at the bedside by Dr", h.Journey(patient));

        // The doctor moves on: no doctor is tied to the bedside.
        Assert.DoesNotContain(h.Sim.Staff, s => s.Role == StaffRole.Doctor && s.BedsidePatientId == patient.Id);

        var lowest = patient.Severity;
        h.RunUntil(() =>
        {
            if (patient.MonitorIsRed && h.Sim.CarerAtBedside(patient) is not null)
            {
                Assert.True(patient.Severity <= lowest + 1e-9, $"got worse while reviewed and sat with ({lowest:0.000} -> {patient.Severity:0.000})");
                lowest = patient.Severity;
            }

            return !patient.MonitorIsRed;
        }, TimeSpan.FromHours(12), "the monitor to stop being red");

        if (patient.IsInDepartment)
        {
            Assert.Contains("has left the bedside", h.Journey(patient));
        }

        Assert.Null(sitter.BedsidePatientId);
    }

    [Fact]
    public void Without_a_doctor_a_red_patient_keeps_getting_worse_even_with_someone_there_and_treatment()
    {
        var h = new SimulationHarness(seed: 5);
        foreach (var doctor in h.Sim.Staff.Where(s => s.Role == StaffRole.Doctor).ToList())
        {
            Assert.True(h.Sim.Fire(doctor.Id));
        }

        var patient = RedPatient(h);
        h.RunUntil(() => h.Sim.CarerAtBedside(patient) is not null, TimeSpan.FromHours(1), "someone to sit with the patient");
        patient.EffectiveTreatmentGiven = true;
        var before = patient.Severity;

        h.RunFor(TimeSpan.FromMinutes(30));

        Assert.False(patient.RedReviewed);
        Assert.True(patient.Severity > before, $"severity {before:0.000} -> {patient.Severity:0.000}");
    }

    [Fact]
    public void Without_healthcare_assistants_a_nurse_sits_with_the_red_patient()
    {
        var h = new SimulationHarness(seed: 5);
        foreach (var hca in h.Sim.Staff.Where(s => s.Role == StaffRole.HealthcareAssistant).ToList())
        {
            Assert.True(h.Sim.Fire(hca.Id));
        }

        var patient = RedPatient(h);
        StaffMember? sitter = null;
        h.RunUntil(() => (sitter = h.Sim.CarerAtBedside(patient)) is not null, TimeSpan.FromHours(1), "someone to sit with the patient");
        Assert.Equal(StaffRole.Nurse, sitter!.Role);
    }

    [Fact]
    public void Reviewed_and_sat_with_but_untreated_a_red_patient_slowly_improves()
    {
        var h = new SimulationHarness(seed: 5);
        var patient = RedPatient(h);
        h.RunUntil(() => patient.RedReviewed && h.Sim.CarerAtBedside(patient) is not null, TimeSpan.FromHours(2), "review and a sitter");
        patient.EffectiveTreatmentGiven = false;
        var before = patient.Severity;

        h.RunFor(TimeSpan.FromMinutes(10));

        if (patient.MonitorIsRed && h.Sim.CarerAtBedside(patient) is not null && !patient.EffectiveTreatmentGiven)
        {
            Assert.True(patient.Severity < before, $"severity {before:0.000} -> {patient.Severity:0.000}");
        }
    }

    [Fact]
    public void Only_patients_on_a_monitor_show_red_and_a_stabilised_red_shows_amber()
    {
        var h = new SimulationHarness(seed: 2);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration");
        Assert.False(patient.UpdateMonitorRed()); // not triaged yet: nobody knows

        // In the waiting room there is no monitor, so nothing shows red.
        patient.Triage = TriageCategory.Red;
        Assert.False(patient.UpdateMonitorRed());

        patient.CareSpaceId = h.Sim.Spaces.First(s => s.Kind == Hospital.CareSpaceKind.Resus).Id;
        patient.Triage = TriageCategory.Green;
        Assert.False(patient.UpdateMonitorRed());
        Assert.Equal(TriageCategory.Green, patient.MonitorColour);

        // A Red triage is red until stabilised; then the border shows amber.
        patient.Triage = TriageCategory.Red;
        Assert.True(patient.UpdateMonitorRed());
        Assert.False(patient.RedReviewed);
        Assert.Equal(TriageCategory.Red, patient.MonitorColour);
        patient.RedStabilised = true;
        Assert.False(patient.UpdateMonitorRed());
        Assert.Equal(TriageCategory.Amber, patient.MonitorColour);
    }

    [Fact]
    public void Healthcare_assistants_are_recommended_and_can_be_hired_for_A_and_E_and_wards()
    {
        var h = new SimulationHarness(seed: 1);
        Assert.Contains(h.Sim.RecommendedStaffing(), n => n.Role == StaffRole.HealthcareAssistant && n.RoomId is null);
        Assert.Contains(h.Sim.PostingsFor(StaffRole.HealthcareAssistant), p => p.RoomId is null);
        var hired = h.Sim.HireStaff(StaffRole.HealthcareAssistant);
        Assert.StartsWith("Healthcare assistant", hired.Grade);
    }

    [Fact]
    public void Someone_sitting_with_a_red_patient_survives_save_and_load()
    {
        var h = new SimulationHarness(seed: 5);
        var patient = RedPatient(h);
        h.RunUntil(() => h.Sim.CarerAtBedside(patient) is not null, TimeSpan.FromHours(1), "someone to sit with the patient");
        var sitter = h.Sim.BedsideCarerFor(patient)!;

        var json = SaveSerializer.Serialize(new SaveGame { Clock = h.Clock.CaptureState(), Layout = h.Sim.CaptureLayout(), People = h.Sim.CapturePeople() });
        var save = SaveSerializer.Deserialize(json);
        var loaded = new SimulationHarness(seed: 99);
        loaded.Clock.RestoreState(save.Clock);
        loaded.Sim.RestoreLayout(save.Layout!);
        loaded.Sim.RestorePeople(save.People!);

        var again = loaded.Sim.FindPatient(patient.Id)!;
        Assert.Equal(sitter.Id, loaded.Sim.BedsideCarerFor(again)?.Id);
        Assert.Equal(patient.MonitorRed, again.MonitorRed);
        Assert.Equal(patient.RedReviewed, again.RedReviewed);
    }
}
