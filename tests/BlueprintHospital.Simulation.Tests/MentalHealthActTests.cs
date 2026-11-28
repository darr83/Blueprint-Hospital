using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Mental health stage 3: Section 136 and the suite, Mental Health Act assessments, and waits for a mental health bed.</summary>
public class MentalHealthActTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A west wing: a corridor with the assessment room, a mental health ward and (if asked for) a Section 136 suite, fully staffed.</summary>
    private static SimulationHarness Wing(int seed, bool suite = true, int wardWidth = 30)
    {
        var h = new SimulationHarness(seed);
        h.Sim.AdjustBalance(5_000_000);
        Build(h.Sim.UnlockDepartment(HospitalSimulation.MentalHealthDepartmentId), "unlock");
        Build(h.Sim.ExtendBuilding(new GridRect(B.X - 40, B.Y, 40, 34)), "extension");
        Build(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 40, B.Y + 12, 44, 6), WallSide.South)), "corridor");
        Build(h.Sim.BuildRoom(h.Sim.AutoPlan("mental_health_assessment_room", new GridRect(B.X - 40, B.Y + 18, 8, 8))), "assessment room");
        Build(h.Sim.BuildRoom(h.Sim.AutoPlan("mental_health_ward", new GridRect(B.X - 31, B.Y + 18, wardWidth, 11))), "ward");
        if (suite)
        {
            Build(h.Sim.BuildRoom(h.Sim.AutoPlan("section_136_suite", new GridRect(B.X - 40, B.Y + 3, 11, 9))), "Section 136 suite");
        }

        h.StaffToRecommended();
        return h;
    }

    private static void Build(BuildCheck result, string what) => Assert.True(result.CanBuild, $"{what}: {string.Join("; ", result.Problems)}");

    private static CareSpace Suite(SimulationHarness h) => h.Sim.Section136Suites.Single();

    [Fact]
    public void The_section_136_suite_is_a_mental_health_room_run_by_a_mental_health_nurse()
    {
        var db = GameDatabase.Load(new FileSystemDataSource(TestData.GameDataDirectory()));
        Assert.Equal(CareSpaceKind.Section136Suite, db.RoomTemplates["section_136_suite"].Kind);
        Assert.Equal(HospitalSimulation.MentalHealthDepartmentId, db.RoomTemplates["section_136_suite"].Department);

        var h = Wing(1);
        var suite = Suite(h);
        Assert.True(h.Sim.IsStaffed(suite));
        Assert.Contains(h.Sim.Staff, s => s.BaseRoomId == suite.Id && s.Role == StaffRole.MentalHealthNurse);
        Assert.Contains(suite.Furniture, f => f.ItemId == "safe_bed");
        Assert.Contains(suite.Furniture, f => f.ItemId == RoomLayoutEngine.EnsuitePod);
    }

    [Fact]
    public void The_police_hand_over_in_the_section_136_suite_and_the_patient_waits_there()
    {
        var h = Wing(2);
        var suite = Suite(h);
        var patient = h.Sim.SpawnSection136Patient("low_mood_suicidal_thoughts", 30);
        h.RunUntil(() => h.Sim.Parents.Any(p => p.ChildId == patient.Id && p.Police), TimeSpan.FromMinutes(30), "the officer to come in with them");
        Assert.Contains("Section 136", h.Journey(patient));

        h.RunUntil(() => patient.InSection136Suite, TimeSpan.FromHours(12), "the handover in the suite");
        Assert.Equal(suite.Id, patient.CareSpaceId);
        Assert.Equal(patient.Id, suite.OccupantId);
        h.RunFor(TimeSpan.FromMinutes(2));
        Assert.DoesNotContain(h.Sim.Parents, p => p.ChildId == patient.Id && p.Police);
        Assert.Contains("handed over and left", h.Journey(patient));
        Assert.Equal(1, h.Sim.Section136HandedOverInSuite);

        // Home with the crisis team in the end, and the suite is free again.
        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromHours(12), "the patient to go home");
        Assert.Null(suite.OccupantId);
    }

    [Fact]
    public void Without_a_suite_the_police_wait_in_ae_and_their_time_is_counted()
    {
        var h = Wing(3, suite: false);
        var patient = h.Sim.SpawnSection136Patient("low_mood_suicidal_thoughts", 35);
        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromHours(24), "the patient to go home");
        h.RunFor(TimeSpan.FromMinutes(1));
        Assert.False(patient.InSection136Suite);
        Assert.True(h.Sim.PoliceHoursInEmergency > 0.25, $"{h.Sim.PoliceHoursInEmergency:0.00} police hours\n{h.Journey(patient)}");
    }

    [Fact]
    public void A_mental_health_act_assessment_brings_in_the_amhp_and_ends_in_admission()
    {
        var h = Wing(4);
        var patients = Enumerable.Range(0, 3).Select(i => h.Sim.SpawnSection136Patient("mania", 25 + i)).ToList();
        var sawAmhp = false;
        h.RunUntil(() =>
        {
            sawAmhp |= h.Sim.Parents.Any(p => p.Escort == EscortKind.Amhp);
            return patients.Any(p => p.MentalHealthAct is MentalHealthActStatus.Detained or MentalHealthActStatus.Informal);
        }, TimeSpan.FromHours(24), "a Mental Health Act assessment");
        Assert.True(sawAmhp, "the AMHP should have come in");
        var assessed = patients.First(p => p.MentalHealthAct is MentalHealthActStatus.Detained or MentalHealthActStatus.Informal);
        Assert.Contains("Mental Health Act assessment by", h.Journey(assessed));
        Assert.Equal(1, h.Sim.MentalHealthActAssessments);

        h.RunUntil(() => assessed.Stage == PatientStage.OnWard && assessed.Wellbeing is not null, TimeSpan.FromHours(12), "admission after the assessment");
        h.RunUntil(() => h.Sim.AmhpFor(assessed) is null, TimeSpan.FromHours(1), "the AMHP to leave");
    }

    [Fact]
    public void With_no_psychiatrist_on_an_ae_doctor_does_the_assessment()
    {
        var h = Wing(5, suite: false);
        foreach (var psychiatrist in h.Sim.Staff.Where(s => s.Role == StaffRole.Psychiatrist).ToList())
        {
            h.Sim.Fire(psychiatrist.Id);
        }

        var patients = Enumerable.Range(0, 3).Select(i => h.Sim.SpawnSection136Patient("first_episode_psychosis", 30 + i)).ToList();
        h.RunUntil(() => patients.Any(p => p.MentalHealthAct is MentalHealthActStatus.Detained or MentalHealthActStatus.Informal), TimeSpan.FromHours(30), "a Mental Health Act assessment");
        var assessed = patients.First(p => p.MentalHealthAct is MentalHealthActStatus.Detained or MentalHealthActStatus.Informal);
        Assert.Contains("Dr ", h.Journey(assessed).Split("Mental Health Act assessment by")[1]);
    }

    [Fact]
    public void After_twelve_hours_without_a_mental_health_bed_one_is_paid_for_in_another_area()
    {
        var h = Wing(6, suite: false);
        var ward = h.Sim.Wards.Single(h.Sim.IsMentalHealthWard);
        foreach (var bed in ward.Beds)
        {
            bed.OccupantId = 999_999;
        }

        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "severe_depression", 45);
        h.RunUntil(() => patient.Stage == PatientStage.AwaitingBed, TimeSpan.FromHours(24), "the decision to admit");
        var asked = patient.BedRequestedAt!.Value;
        Assert.Contains("Waiting for a mental health bed", h.Sim.MentalHealthActLine(patient));

        // Not sent away at six hours like other patients: they wait for 12.
        h.RunFor(TimeSpan.FromHours(7));
        Assert.Equal(PatientStage.AwaitingBed, patient.Stage);

        h.RunUntil(() => patient.Stage is PatientStage.AwaitingTransfer or PatientStage.Gone, TimeSpan.FromHours(7), "an out-of-area bed");
        Assert.True(h.Sim.Now - asked >= HospitalSimulation.MentalHealthBedWait);
        Assert.Contains("bed in another area", h.Journey(patient));
        Assert.Equal(1, h.Sim.OutOfAreaPlacements);
        Assert.Equal(1, h.Sim.MentalHealthTwelveHourWaits);
        Assert.Contains(h.Sim.Accounts.Take(2), d => d[MoneyCategory.OutOfAreaBeds] == -HospitalSimulation.OutOfAreaBedCost);
    }

    [Fact]
    public void The_player_can_pay_for_an_out_of_area_bed_straight_away()
    {
        var h = Wing(7, suite: false);
        var ward = h.Sim.Wards.Single(h.Sim.IsMentalHealthWard);
        foreach (var bed in ward.Beds)
        {
            bed.OccupantId = 999_999;
        }

        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "severe_depression", 50);
        h.RunUntil(() => h.Sim.CanBuyOutOfAreaBed(patient), TimeSpan.FromHours(24), "the decision to admit");
        Assert.True(h.Sim.BuyOutOfAreaBed(patient));
        Assert.Equal(PatientStage.AwaitingTransfer, patient.Stage);
        Assert.Equal(1, h.Sim.OutOfAreaPlacements);
        Assert.Equal(0, h.Sim.MentalHealthTwelveHourWaits);
        Assert.False(h.Sim.BuyOutOfAreaBed(patient));
    }

    [Fact]
    public void Section_136_and_the_assessment_are_saved()
    {
        var h = Wing(8);
        var patients = Enumerable.Range(0, 3).Select(i => h.Sim.SpawnSection136Patient("mania", 30 + i)).ToList();
        h.RunUntil(() => patients.Any(p => p.MentalHealthAct == MentalHealthActStatus.Assessing), TimeSpan.FromHours(24), "the AMHP to arrive");
        var patient = patients.First(p => p.MentalHealthAct == MentalHealthActStatus.Assessing);
        var json = SaveSerializer.Serialize(new SaveGame { Clock = h.Clock.CaptureState(), Layout = h.Sim.CaptureLayout(), People = h.Sim.CapturePeople() });
        var save = SaveSerializer.Deserialize(json);
        var to = new SimulationHarness(seed: 99);
        to.Clock.RestoreState(save.Clock);
        to.Sim.RestoreLayout(save.Layout!);
        to.Sim.RestorePeople(save.People!);
        var again = to.Sim.Patients.Single(p => p.Id == patient.Id);
        Assert.True(again.Section136);
        Assert.Equal(MentalHealthActStatus.Assessing, again.MentalHealthAct);
        Assert.NotNull(to.Sim.AmhpFor(again));
    }
}
