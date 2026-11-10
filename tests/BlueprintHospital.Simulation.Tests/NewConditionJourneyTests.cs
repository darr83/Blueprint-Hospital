using BlueprintHospital.Simulation.Patients;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>The illnesses added in the medical content expansion each make it through the starting A&amp;E.</summary>
public class NewConditionJourneyTests
{
    public static readonly TheoryData<string> Conditions = new()
    {
        "wrist_fracture", "shoulder_dislocation", "tibial_shaft_fracture", "septic_arthritis_knee",
        "minor_head_injury", "minor_burn", "epistaxis", "mechanical_back_pain", "gout", "panic_attack",
        "croup", "bronchiolitis", "febrile_convulsion",
        "paracetamol_overdose", "alcohol_withdrawal", "influenza", "fall_older_person",
        "nstemi", "complete_heart_block", "pericarditis",
        "bacterial_meningitis", "bells_palsy", "transient_ischaemic_attack", "status_epilepticus",
        "decompensated_cirrhosis", "ulcerative_colitis_flare",
        "diverticulitis", "testicular_torsion", "large_bowel_obstruction",
        "ovarian_torsion", "hyperemesis_gravidarum", "pelvic_inflammatory_disease", "bartholins_abscess",
    };

    [Theory]
    [MemberData(nameof(Conditions))]
    public void Patient_is_assessed_treated_and_leaves(string conditionId)
    {
        var h = new SimulationHarness(seed: 17);
        var method = h.Sim.Database.Conditions[conditionId].AmbulanceProbability >= 0.5 ? ArrivalMethod.Ambulance : ArrivalMethod.WalkIn;
        var patient = h.Sim.SpawnPatient(method, conditionId);

        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromHours(24), $"{conditionId} patient to leave");

        var journey = h.Journey(patient);
        Assert.True(patient.HistoryTaken, journey);
        Assert.NotNull(patient.WorkingDiagnosis);
        Assert.NotEqual(PatientOutcome.None, patient.Outcome);
    }
}
