using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Mental health stage 2: the adult ward, observations, the wellbeing meter, the lounge and garden.</summary>
public class MentalHealthWardTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A west wing: a corridor with the assessment room, a mental health ward, a lounge and a garden off it, fully staffed.</summary>
    private static SimulationHarness Wing(int seed, bool dayRooms = true)
    {
        var h = new SimulationHarness(seed);
        h.Sim.AdjustBalance(5_000_000);
        Build(h.Sim.UnlockDepartment(HospitalSimulation.MentalHealthDepartmentId), "unlock");
        Build(h.Sim.ExtendBuilding(new GridRect(B.X - 40, B.Y, 40, 34)), "extension");
        Build(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 40, B.Y + 12, 44, 6), WallSide.South)), "corridor");
        Build(h.Sim.BuildRoom(h.Sim.AutoPlan("mental_health_assessment_room", new GridRect(B.X - 40, B.Y + 18, 8, 8))), "assessment room");
        Build(h.Sim.BuildRoom(h.Sim.AutoPlan("mental_health_ward", new GridRect(B.X - 31, B.Y + 18, 30, 11))), "ward");
        if (dayRooms)
        {
            Build(h.Sim.BuildRoom(new RoomPlan("mental_health_lounge", new GridRect(B.X - 40, B.Y, 14, 12), WallSide.South)), "lounge");
            Build(h.Sim.BuildRoom(new RoomPlan("mental_health_garden", new GridRect(B.X - 26, B.Y, 16, 12), WallSide.South)), "garden");
        }

        h.StaffToRecommended();
        return h;
    }

    private static void Build(BuildCheck result, string what) => Assert.True(result.CanBuild, $"{what}: {string.Join("; ", result.Problems)}");

    private static CareSpace Ward(SimulationHarness h) => h.Sim.Wards.Single(h.Sim.IsMentalHealthWard);

    [Fact]
    public void The_ward_has_single_bedrooms_with_their_own_shower_rooms()
    {
        var db = GameDatabase.Load(new FileSystemDataSource(TestData.GameDataDirectory()));
        Assert.Equal(CareSpaceKind.MentalHealthLounge, db.RoomTemplates["mental_health_lounge"].Kind);
        Assert.Equal(CareSpaceKind.MentalHealthGarden, db.RoomTemplates["mental_health_garden"].Kind);

        var h = Wing(1);
        var ward = Ward(h);
        Assert.True(ward.Beds.Count >= 4, $"{ward.Beds.Count} beds");
        Assert.Equal(ward.Beds.Count, ward.Furniture.Count(f => f.ItemId == RoomLayoutEngine.EnsuitePod));
        Assert.True(ward.Furniture.Count(f => f.ItemId == RoomLayoutEngine.BedroomWall) >= ward.Beds.Count - 2);
        Assert.DoesNotContain(ward.Furniture, f => f.ItemId == "visitor_chair");
        Assert.True(h.Sim.DayRooms.All(r => r.Seats.Count > 0));
    }

    [Fact]
    public void Only_the_mental_health_team_work_on_the_ward()
    {
        var h = Wing(2);
        var ward = Ward(h);
        var needs = h.Sim.RecommendedStaffing().Where(n => n.RoomId == ward.Id).Select(n => n.Role).Distinct().ToList();
        Assert.Contains(StaffRole.MentalHealthNurse, needs);
        Assert.Contains(StaffRole.Psychiatrist, needs);
        Assert.Contains(StaffRole.SupportWorker, needs);
        Assert.DoesNotContain(StaffRole.Nurse, needs);
        Assert.DoesNotContain(StaffRole.Doctor, needs);
        Assert.DoesNotContain(StaffRole.HealthcareAssistant, needs);
        Assert.DoesNotContain(h.Sim.PostingsFor(StaffRole.Nurse), p => p.RoomId == ward.Id);
        Assert.Contains(h.Sim.PostingsFor(StaffRole.SupportWorker), p => p.RoomId == ward.Id);
        Assert.Contains(h.Sim.PostingsFor(StaffRole.MentalHealthNurse), p => p.RoomId == ward.Id);
        Assert.Contains(h.Sim.Staff, s => s.Role == StaffRole.SupportWorker && s.BaseRoomId == ward.Id);
    }

    [Fact]
    public void Patients_who_need_admitting_go_to_the_mental_health_ward_with_a_wellbeing_meter()
    {
        var h = Wing(3);
        var ward = Ward(h);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "severe_depression", 40);
        h.RunUntil(() => patient.Stage == PatientStage.OnWard && patient.Wellbeing is not null, TimeSpan.FromHours(24), "admission to the mental health ward");
        Assert.Equal(ward.Id, patient.CareSpaceId);
        Assert.NotNull(patient.WellbeingBand);
        Assert.Contains("Settling in to their own room", h.Journey(patient));
        // The monitor shows the wellbeing meter, not NEWS2.
        var expected = patient.WellbeingBand switch
        {
            WellbeingBand.Settled => TriageCategory.Green,
            WellbeingBand.Unsettled => TriageCategory.Amber,
            _ => TriageCategory.Red,
        };
        Assert.Equal(expected, patient.MonitorColour);
    }

    [Fact]
    public void Other_patients_never_go_to_the_mental_health_ward()
    {
        var h = Wing(4);
        var ward = Ward(h);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "community_acquired_pneumonia", 60);
        h.RunUntil(() => patient.Stage is PatientStage.OnWard or PatientStage.Gone, TimeSpan.FromHours(24), "pneumonia patient to be admitted or leave");
        Assert.NotEqual(ward.Id, patient.CareSpaceId);
        Assert.Null(patient.Wellbeing);
    }

    [Fact]
    public void Close_observations_tie_up_a_support_worker_one_to_one()
    {
        var h = Wing(5);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "mania", 30);
        h.RunUntil(() => patient.Stage == PatientStage.OnWard && patient.Wellbeing is not null, TimeSpan.FromHours(24), "admission");
        patient.Wellbeing = 0.1;
        patient.ObservationLevel = ObservationLevel.WithinEyesight;
        h.RunUntil(() => h.Sim.CarerAtBedside(patient) is not null, TimeSpan.FromHours(2), "someone to stay with them");
        var carer = h.Sim.CarerAtBedside(patient)!;
        Assert.Contains(carer.Role, new[] { StaffRole.SupportWorker, StaffRole.MentalHealthNurse });
        Assert.Contains("within eyesight observations", h.Journey(patient));

        // Once they are better and stepped down, the carer goes back to the ward.
        patient.Wellbeing = 0.7;
        patient.ObservationLevel = ObservationLevel.General;
        h.RunUntil(() => h.Sim.BedsideCarerFor(patient) is null, TimeSpan.FromMinutes(30), "the carer to step back");
    }

    [Fact]
    public void Routine_checks_and_the_psychiatrists_review_happen_and_nobody_goes_home_unsettled()
    {
        var h = Wing(6);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "first_episode_psychosis", 28);
        h.RunUntil(() => patient.Stage == PatientStage.OnWard && patient.Wellbeing is not null, TimeSpan.FromHours(24), "admission");
        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromDays(14), "the patient to go home");
        var journey = h.Journey(patient);
        Assert.Contains("Checked on", journey);
        Assert.Contains("Reviewed by", journey);
        // They only went home settled, on general observations.
        Assert.Equal(WellbeingBand.Settled, patient.WellbeingBand);
        Assert.Equal(ObservationLevel.General, patient.ObservationLevel);
    }

    [Fact]
    public void Patients_spend_time_in_the_lounge_and_garden()
    {
        var h = Wing(7);
        var patients = new[] { "severe_depression", "mania", "first_episode_psychosis" }.Select(c => h.Sim.SpawnPatient(ArrivalMethod.WalkIn, c, 35)).ToList();
        var seen = false;
        h.RunUntil(() =>
        {
            seen |= patients.Any(p => h.Sim.DayRoomOf(p) is not null);
            return seen;
        }, TimeSpan.FromDays(3), "a patient to sit in the lounge or garden");
        Assert.Contains(patients, p => h.Journey(p).Contains("Went out of their room"));
    }

    [Fact]
    public void Without_a_mental_health_ward_admissions_still_go_to_a_mental_health_hospital()
    {
        var h = new SimulationHarness(8);
        h.Sim.AdjustBalance(3_000_000);
        Build(h.Sim.UnlockDepartment(HospitalSimulation.MentalHealthDepartmentId), "unlock");
        h.StaffToRecommended();
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "mania", 30);
        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromHours(24), "patient to leave");
        Assert.Contains("mental health hospital", h.Journey(patient));
        Assert.Null(patient.Wellbeing);
    }

    [Fact]
    public void Wellbeing_and_observations_are_saved()
    {
        var h = Wing(9);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "severe_depression", 40);
        h.RunUntil(() => patient.Stage == PatientStage.OnWard && patient.Wellbeing is not null, TimeSpan.FromHours(24), "admission");
        patient.Wellbeing = 0.42;
        patient.ObservationLevel = ObservationLevel.ArmsLength;
        var json = SaveSerializer.Serialize(new SaveGame { Clock = h.Clock.CaptureState(), Layout = h.Sim.CaptureLayout(), People = h.Sim.CapturePeople() });
        var save = SaveSerializer.Deserialize(json);
        var to = new SimulationHarness(seed: 99);
        to.Clock.RestoreState(save.Clock);
        to.Sim.RestoreLayout(save.Layout!);
        to.Sim.RestorePeople(save.People!);
        var again = to.Sim.Patients.Single(p => p.Id == patient.Id);
        Assert.Equal(0.42, again.Wellbeing!.Value, 3);
        Assert.Equal(ObservationLevel.ArmsLength, again.ObservationLevel);
    }

    [Fact]
    public void After_an_overdose_the_liaison_team_see_them_on_the_medical_ward_before_home()
    {
        var h = new SimulationHarness(10);
        h.Sim.AdjustBalance(5_000_000);
        Build(h.Sim.UnlockDepartment(HospitalSimulation.MentalHealthDepartmentId), "unlock");
        Build(h.Sim.ExtendBuilding(new GridRect(B.X - 40, B.Y, 40, 34)), "extension");
        Build(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 40, B.Y + 12, 44, 6), WallSide.South)), "corridor");
        Build(h.Sim.BuildRoom(h.Sim.AutoPlan("mental_health_assessment_room", new GridRect(B.X - 40, B.Y + 18, 8, 8))), "assessment room");
        Build(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_ward", new GridRect(B.X - 31, B.Y + 18, 26, 12))), "medical ward");
        h.StaffToRecommended();
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "paracetamol_overdose", 24);
        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromDays(5), "the overdose patient to go home");
        var journey = h.Journey(patient);
        Assert.True(patient.AdmittedAt is not null, journey);
        Assert.Contains("Seen on the ward by", journey);
        Assert.True(patient.LiaisonReviewed);
        Assert.Equal(1, h.Sim.LiaisonWardReviews);
    }
}
