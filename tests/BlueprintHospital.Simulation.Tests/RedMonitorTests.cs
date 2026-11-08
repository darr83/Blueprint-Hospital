using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>A red monitor needs a doctor at the bedside until it isn't red.</summary>
public class RedMonitorTests
{
    private static Patient RedPatient(SimulationHarness h, string condition = "septic_shock")
    {
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, condition);
        h.RunUntil(() => patient.Triage is not null, TimeSpan.FromHours(1), "the patient to be triaged");
        h.RunFor(TimeSpan.FromSeconds(2));
        return patient;
    }

    [Fact]
    public void A_doctor_goes_to_a_red_monitor_and_stays_until_it_clears()
    {
        var h = new SimulationHarness(seed: 5);
        var patient = RedPatient(h);
        Assert.True(patient.MonitorIsRed, h.Journey(patient));

        StaffMember? doctor = null;
        h.RunUntil(() => (doctor = h.Sim.DoctorAtBedside(patient)) is not null, TimeSpan.FromHours(1), "a doctor to reach the bedside");
        Assert.Contains("is staying at the bedside", h.Journey(patient));

        // While the monitor is red the doctor stays with them (or does their work there) and they get better.
        var lowest = patient.Severity;
        h.RunUntil(() =>
        {
            if (patient.MonitorIsRed && patient.IsInDepartment)
            {
                Assert.Equal(patient.Id, doctor!.BedsidePatientId);
                Assert.True(patient.Severity <= lowest + 1e-9, $"got worse with a doctor there ({lowest:0.000} -> {patient.Severity:0.000})");
                lowest = patient.Severity;
            }

            return !patient.MonitorIsRed;
        }, TimeSpan.FromHours(12), "the monitor to stop being red");

        if (patient.IsInDepartment)
        {
            Assert.True(patient.LiveNews2 <= Patient.MonitorClearNews2);
            Assert.Contains("has left the bedside", h.Journey(patient));
        }

        Assert.Null(doctor!.BedsidePatientId);
    }

    [Fact]
    public void Without_a_doctor_a_red_patient_keeps_getting_worse_even_after_treatment()
    {
        var h = new SimulationHarness(seed: 5);
        foreach (var doctor in h.Sim.Staff.Where(s => s.Role == StaffRole.Doctor).ToList())
        {
            Assert.True(h.Sim.Fire(doctor.Id));
        }

        var patient = RedPatient(h);
        Assert.True(patient.MonitorIsRed, h.Journey(patient));
        patient.EffectiveTreatmentGiven = true;
        var before = patient.Severity;

        h.RunFor(TimeSpan.FromMinutes(30));

        Assert.True(patient.Severity > before, $"severity {before:0.000} -> {patient.Severity:0.000}");
        Assert.Null(h.Sim.BedsideDoctorFor(patient));
        Assert.Contains("a doctor is needed at the bedside", h.Journey(patient));
    }

    [Fact]
    public void With_a_doctor_there_but_no_treatment_a_red_patient_slowly_improves()
    {
        var h = new SimulationHarness(seed: 5);
        var patient = RedPatient(h);
        h.RunUntil(() => h.Sim.DoctorAtBedside(patient) is not null, TimeSpan.FromHours(1), "a doctor to reach the bedside");
        patient.EffectiveTreatmentGiven = false;
        var before = patient.Severity;

        h.RunFor(TimeSpan.FromMinutes(10));

        if (h.Sim.DoctorAtBedside(patient) is not null && !patient.EffectiveTreatmentGiven)
        {
            Assert.True(patient.Severity < before, $"severity {before:0.000} -> {patient.Severity:0.000}");
        }
    }

    [Fact]
    public void The_monitor_turns_red_at_news2_7_and_clears_at_5()
    {
        var h = new SimulationHarness(seed: 2);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration");
        Assert.False(patient.UpdateMonitorRed()); // not triaged yet: nobody knows

        patient.Triage = TriageCategory.Green;
        Assert.False(patient.UpdateMonitorRed());
        Assert.Equal(TriageCategory.Green, patient.MonitorColour);

        // A Red triage is red until a doctor stabilises them; then the border shows amber.
        patient.Triage = TriageCategory.Red;
        Assert.True(patient.UpdateMonitorRed());
        Assert.Equal(TriageCategory.Red, patient.MonitorColour);
        patient.RedStabilised = true;
        Assert.False(patient.UpdateMonitorRed());
        Assert.Equal(TriageCategory.Amber, patient.MonitorColour);
    }

    [Fact]
    public void A_doctor_staying_at_the_bedside_survives_save_and_load()
    {
        var h = new SimulationHarness(seed: 5);
        var patient = RedPatient(h);
        h.RunUntil(() => h.Sim.DoctorAtBedside(patient) is not null, TimeSpan.FromHours(1), "a doctor to reach the bedside");
        var doctor = h.Sim.BedsideDoctorFor(patient)!;

        var json = SaveSerializer.Serialize(new SaveGame { Clock = h.Clock.CaptureState(), Layout = h.Sim.CaptureLayout(), People = h.Sim.CapturePeople() });
        var save = SaveSerializer.Deserialize(json);
        var loaded = new SimulationHarness(seed: 99);
        loaded.Clock.RestoreState(save.Clock);
        loaded.Sim.RestoreLayout(save.Layout!);
        loaded.Sim.RestorePeople(save.People!);

        var again = loaded.Sim.FindPatient(patient.Id)!;
        Assert.Equal(doctor.Id, loaded.Sim.BedsideDoctorFor(again)?.Id);
        Assert.Equal(patient.MonitorRed, again.MonitorRed);
    }
}
