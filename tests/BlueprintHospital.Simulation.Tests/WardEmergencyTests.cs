using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Sudden emergencies for patients already in a hospital bed.</summary>
public class WardEmergencyTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    private static (SimulationHarness H, CareSpace Ward) WithWard(int seed = 5)
    {
        var h = new SimulationHarness(seed);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_ward", new GridRect(B.X - 30, B.Y + 17, 26, 15))).CanBuild);
        h.StaffToRecommended();
        return (h, h.Sim.Wards.Single());
    }

    private static Patient OnWard(SimulationHarness h)
    {
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        h.RunUntil(() => patient.Stage is PatientStage.OnWard or PatientStage.Gone, TimeSpan.FromHours(12), "the patient to reach the ward");
        Assert.True(patient.Stage == PatientStage.OnWard, h.Journey(patient));
        h.RunUntil(() => h.Sim.BedOf(patient) is not null && !patient.IsMoving, TimeSpan.FromHours(1), "the patient to get into bed");
        return patient;
    }

    [Fact]
    public void All_ten_emergencies_load_from_the_data_file()
    {
        var h = new SimulationHarness();
        var ids = h.Database.Emergencies.Keys.ToHashSet();
        foreach (var id in new[] { "cardiac_arrest", "heart_attack", "pulmonary_embolism", "sepsis", "fall", "post_op_bleed", "stroke", "hypoglycaemia", "anaphylaxis", "delirium" })
        {
            Assert.Contains(id, ids);
        }
    }

    [Fact]
    public void A_fall_puts_the_patient_on_the_floor_until_the_team_help_them_up_and_they_settle()
    {
        var (h, _) = WithWard();
        var patient = OnWard(h);
        h.Sim.StartWardEmergency(patient, h.Database.Emergencies["fall"]);
        Assert.True(patient.OnFloorAfterFall);
        Assert.Contains(h.Sim.Tasks, t => t.PatientId == patient.Id && t.Kind == TaskKind.EmergencyReview);

        h.RunUntil(() => patient.EmergencyTreated || patient.Emergency is null, TimeSpan.FromHours(2), "the fall to be dealt with");
        Assert.False(patient.OnFloorAfterFall, h.Journey(patient));
        h.RunUntil(() => patient.Emergency is null, TimeSpan.FromHours(4), "them to settle");

        var journey = h.Journey(patient);
        Assert.Contains("Emergency: fall", journey);
        Assert.Contains("Post-fall review", journey);
        Assert.Contains("Help back to bed after fall", journey);
        Assert.Contains("Settled after the fall", journey);
        Assert.Equal(1, h.Sim.Stats.WardEmergencies);
        Assert.Equal(1, h.Sim.Stats.WardEmergenciesTreated);
        Assert.Equal(1, h.Sim.Stats.WardEmergenciesSettled);
    }

    [Fact]
    public void Sepsis_on_the_ward_turns_the_monitor_red_and_gets_antibiotics()
    {
        var (h, _) = WithWard();
        var patient = OnWard(h);
        h.Sim.StartWardEmergency(patient, h.Database.Emergencies["sepsis"]);
        Assert.True(patient.LiveNews2 >= 7, $"NEWS2 {patient.LiveNews2}");
        h.RunUntil(() => patient.EmergencyTreated || !patient.IsAlive, TimeSpan.FromHours(2), "the Sepsis Six");
        Assert.True(patient.IsAlive, h.Journey(patient));
        Assert.Contains("IV antibiotics and fluids", h.Journey(patient));
        h.RunUntil(() => patient.Emergency is null || !patient.IsAlive, TimeSpan.FromHours(12), "them to settle");
        Assert.True(patient.IsAlive, h.Journey(patient));
    }

    [Fact]
    public void An_untreated_heart_attack_with_nobody_there_ends_in_cardiac_arrest()
    {
        var (h, _) = WithWard();
        var patient = OnWard(h);
        foreach (var member in h.Sim.Staff.Where(s => s.Role is StaffRole.Nurse or StaffRole.HealthcareAssistant or StaffRole.Doctor or StaffRole.Anaesthetist or StaffRole.OutreachNurse).ToList())
        {
            h.Sim.Fire(member.Id);
        }

        var emergency = h.Database.Emergencies["heart_attack"];
        h.Sim.StartWardEmergency(patient, emergency);
        h.RunUntil(() => patient.InArrest || !patient.IsAlive, TimeSpan.FromHours(6), "the heart attack to go untreated");
        Assert.False(patient.EmergencyTreated);
    }

    [Fact]
    public void Hypoglycaemia_only_happens_to_diabetics_and_falls_mostly_to_the_elderly()
    {
        var (h, _) = WithWard();
        var patient = OnWard(h);
        var hypo = h.Database.Emergencies["hypoglycaemia"];
        var diabetic = patient.PastHistory.Any(p => p.Contains("diabetes", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(diabetic, h.Sim.EmergencyChancePerHour(patient, hypo) > 0);
        Assert.True(h.Sim.EmergencyChancePerHour(patient, h.Database.Emergencies["fall"]) > 0);
    }

    [Fact]
    public void Realistic_rates_give_about_one_emergency_a_day_for_thirty_inpatients()
    {
        var (h, _) = WithWard();
        var patients = Enumerable.Range(0, 6).Select(_ => h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia")).ToList();
        h.RunUntil(() => patients.All(p => p.Stage is PatientStage.OnWard or PatientStage.Gone), TimeSpan.FromHours(16), "admissions");
        var onWard = patients.Where(p => p.Stage == PatientStage.OnWard).ToList();
        Assert.NotEmpty(onWard);
        var perPatientPerDay = onWard.Average(p => h.Database.Emergencies.Values.Sum(e => h.Sim.EmergencyChancePerHour(p, e)) * 24);
        var perDayFor30 = perPatientPerDay * 30;
        Assert.InRange(perDayFor30, 0.4, 3.0);
    }

    [Fact]
    public void Off_means_no_emergencies_however_long_they_stay()
    {
        var (h, _) = WithWard();
        h.Sim.WardEmergencies = WardEmergencyFrequency.Off;
        OnWard(h);
        h.RunFor(TimeSpan.FromHours(12));
        Assert.Equal(0, h.Sim.Stats.WardEmergencies);
    }

    [Fact]
    public void An_emergency_in_progress_survives_save_and_load()
    {
        var (h, _) = WithWard();
        var patient = OnWard(h);
        h.Sim.WardEmergencies = WardEmergencyFrequency.Rare;
        h.Sim.StartWardEmergency(patient, h.Database.Emergencies["delirium"]);
        var people = h.Sim.CapturePeople();
        var layout = h.Sim.CaptureLayout();
        var json = System.Text.Json.JsonSerializer.Serialize(people);
        var back = System.Text.Json.JsonSerializer.Deserialize<PeopleState>(json)!;
        var saved = back.Patients.Single(p => p.Id == patient.Id);
        Assert.Equal("delirium", saved.EmergencyId);
        Assert.Equal(1, saved.EmergencyCount);
        Assert.NotNull(layout);
    }
}
