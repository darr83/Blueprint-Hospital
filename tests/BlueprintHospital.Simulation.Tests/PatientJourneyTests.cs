using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Tests;

public class PatientJourneyTests
{
    [Fact]
    public void Heart_attack_by_ambulance_goes_through_resus_ecg_troponin_diagnosis_and_transfer()
    {
        var h = new SimulationHarness(seed: 7);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_myocardial_infarction");

        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromHours(6), "heart attack patient to leave");

        var journey = h.Journey(patient);
        Assert.True(patient.Outcome == PatientOutcome.Transferred, journey);
        Assert.Contains("Ambulance handover", journey);
        Assert.NotNull(patient.Triage);
        Assert.NotEqual(TriageCategory.Green, patient.Triage);
        Assert.Contains(patient.Investigations, i => i.InvestigationId == "ecg" && i.Status == InvestigationStatus.Resulted);
        // A clearly abnormal ECG is decisive: the team acts on it rather than waiting an hour for troponin.
        Assert.Contains("upgraded", journey, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("acute_myocardial_infarction", patient.WorkingDiagnosis?.ConditionId);
        Assert.Contains("Aspirin", journey);
        Assert.Contains("primary PCI", journey);
        Assert.True(patient.EffectiveTreatmentGiven, journey);
    }

    [Fact]
    public void Walk_in_with_cut_registers_is_triaged_green_treated_in_minors_and_discharged()
    {
        var h = new SimulationHarness(seed: 11);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration");
        var visitedMinors = false;

        h.RunUntil(() =>
        {
            visitedMinors |= patient.CareSpaceId is { } id && h.Sim.Space(id).Kind == CareSpaceKind.Minors;
            return patient.Stage == PatientStage.Gone;
        }, TimeSpan.FromHours(6), "laceration patient to leave");

        var journey = h.Journey(patient);
        Assert.Equal(PatientOutcome.Discharged, patient.Outcome);
        Assert.Contains("Registered by", journey);
        Assert.Contains("Triaged Green", journey);
        Assert.True(visitedMinors, journey);
        Assert.Contains("Wound cleaning and closure", journey);
        Assert.Empty(patient.Investigations);
    }

    [Fact]
    public void Appendicitis_gets_bloods_and_is_transferred_for_surgery()
    {
        var h = new SimulationHarness(seed: 21);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "acute_appendicitis");

        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromHours(8), "appendicitis patient to leave");

        var journey = h.Journey(patient);
        Assert.Contains(patient.Investigations, i => i.InvestigationId == "full_blood_count");
        Assert.Contains(patient.Investigations, i => i.InvestigationId == "c_reactive_protein");
        Assert.Contains("Blood samples taken", journey);
        if (patient.WorkingDiagnosis?.ConditionId == "acute_appendicitis")
        {
            Assert.Equal(PatientOutcome.Transferred, patient.Outcome);
        }
    }

    [Fact]
    public void Lab_results_take_their_turnaround_time()
    {
        // Non-cardiac chest pain in a middle-aged patient: the ECG is normal, but a heart attack
        // must still be ruled out, so troponin is sent and the team waits for it.
        SimulationHarness h;
        Patient patient;
        var seed = 3;
        do
        {
            h = new SimulationHarness(seed: seed++);
            patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "musculoskeletal_chest_pain");
        }
        while (patient.Age < 45);

        h.RunUntil(() => patient.Stage == PatientStage.Gone || patient.Investigations.Any(i => i.InvestigationId == "troponin" && i.Status == InvestigationStatus.Resulted), TimeSpan.FromHours(4), "troponin result");
        Assert.True(patient.Investigations.Any(i => i.InvestigationId == "troponin"), h.Journey(patient));

        var troponin = patient.Investigations.Single(i => i.InvestigationId == "troponin");
        Assert.True(troponin.ResultedAt - troponin.SampleTakenAt >= TimeSpan.FromMinutes(60));
    }

    [Fact]
    public void Red_patient_tasks_outrank_green_patient_tasks()
    {
        var h = new SimulationHarness(seed: 5);
        var doctors = h.Sim.Staff.Where(s => s.Role == StaffRole.Doctor).ToList();
        foreach (var doctor in doctors)
        {
            doctor.Activity = StaffActivity.OnBreak;
            doctor.BreakEndsAt = DateTime.MaxValue;
        }

        var green = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "ankle_sprain");
        var red = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_myocardial_infarction");
        red.Severity = 0.8;

        // Both patients reach a cubicle and wait for a doctor while the doctors are busy elsewhere.
        h.RunUntil(
            () => h.Sim.Tasks.Count(t => t.Kind == TaskKind.DoctorAssessment) == 2,
            TimeSpan.FromHours(2),
            "both patients to need a doctor");

        var redTask = h.Sim.Tasks.Single(t => t.Kind == TaskKind.DoctorAssessment && t.PatientId == red.Id);
        var greenTask = h.Sim.Tasks.Single(t => t.Kind == TaskKind.DoctorAssessment && t.PatientId == green.Id);
        Assert.Equal(TriageCategory.Red, red.Triage);
        Assert.Equal(TriageCategory.Green, green.Triage);
        Assert.True(redTask.Priority > greenTask.Priority);

        // One doctor comes back: they must see the Red patient first.
        doctors[0].BreakEndsAt = h.Clock.Now;
        h.RunUntil(() => red.FirstSeenByDoctorAt.HasValue || green.FirstSeenByDoctorAt.HasValue, TimeSpan.FromHours(1), "a doctor to see someone");
        Assert.NotNull(red.FirstSeenByDoctorAt);
        Assert.Null(green.FirstSeenByDoctorAt);
    }

    [Fact]
    public void Untreated_appendicitis_deteriorates_and_is_upgraded()
    {
        var h = new SimulationHarness(seed: 9);

        // Take every doctor out of action so the patient is observed but not treated.
        foreach (var doctor in h.Sim.Staff.Where(s => s.Role == StaffRole.Doctor))
        {
            doctor.Activity = StaffActivity.OnBreak;
            doctor.BreakEndsAt = DateTime.MaxValue;
        }

        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "acute_appendicitis");
        patient.Severity = 0.45;

        h.RunUntil(() => patient.Timeline.Any(t => t.Text.StartsWith("Upgraded", StringComparison.Ordinal)) || !patient.IsAlive, TimeSpan.FromHours(10), "an upgrade");

        Assert.Contains(patient.Timeline, t => t.Text.StartsWith("Upgraded", StringComparison.Ordinal));
        Assert.Contains(h.Sim.Notifications.Recent, n => n.PatientId == patient.Id && n.Text.Contains("deteriorating"));
    }

    [Fact]
    public void Busy_day_runs_without_errors_and_keeps_rooms_consistent()
    {
        var h = new SimulationHarness(seed: 42, arrivals: true, start: new DateTime(2027, 1, 4, 0, 0, 0));

        for (var hour = 0; hour < 24; hour++)
        {
            h.RunFor(TimeSpan.FromHours(1));

            // No room holds a patient who isn't there, and no patient is in two rooms.
            foreach (var space in h.Sim.Spaces.Where(s => s.OccupantId.HasValue))
            {
                var occupant = h.Sim.FindPatient(space.OccupantId!.Value);
                Assert.NotNull(occupant);
                Assert.Equal(space.Id, occupant!.CareSpaceId);
            }

            var seats = h.Sim.Patients.Where(p => p.SeatIndex.HasValue).Select(p => p.SeatIndex).ToList();
            Assert.Equal(seats.Count, seats.Distinct().Count());
        }

        var stats = h.Sim.Stats;
        Assert.InRange(stats.ArrivalsTotal, 35, 110);
        Assert.True(stats.DepartedTotal > stats.ArrivalsTotal / 2, $"Only {stats.DepartedTotal} of {stats.ArrivalsTotal} left");
        Assert.True(stats.Discharged > 0, "No discharges");
        Assert.True(stats.Transferred > 0, "No transfers");
        Assert.True(stats.DiagnosesMade > 0, "No diagnoses");
        Assert.True(stats.DiagnosticAccuracy > 0.75, $"Accuracy {stats.DiagnosticAccuracy:P0}");
    }
}
