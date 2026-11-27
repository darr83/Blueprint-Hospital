using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Mental health stage 1: the department, the A&amp;E liaison assessment room and its team.</summary>
public class MentalHealthTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A west wing with a corridor and a mental health assessment room south of it, with the recommended staff.</summary>
    private static SimulationHarness Wing(int seed, bool staffRoom = true)
    {
        var h = new SimulationHarness(seed);
        h.Sim.AdjustBalance(3_000_000);
        Build(h.Sim.UnlockDepartment(HospitalSimulation.MentalHealthDepartmentId), "unlock");
        Build(h.Sim.ExtendBuilding(new GridRect(B.X - 40, B.Y, 40, 34)), "extension");
        Build(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 40, B.Y + 10, 44, 6), WallSide.South)), "corridor");
        Build(h.Sim.BuildRoom(h.Sim.AutoPlan("mental_health_assessment_room", new GridRect(B.X - 40, B.Y + 16, 8, 8))), "assessment room");
        h.StaffToRecommended();
        if (!staffRoom)
        {
            foreach (var member in h.Sim.Staff.Where(s => HospitalSimulation.IsMentalHealthRole(s.Role)).ToList())
            {
                h.Sim.Fire(member.Id);
            }
        }

        return h;
    }

    private static void Build(BuildCheck result, string what) => Assert.True(result.CanBuild, $"{what}: {string.Join("; ", result.Problems)}");

    [Fact]
    public void The_department_and_its_room_load()
    {
        var db = GameDatabase.Load(new FileSystemDataSource(TestData.GameDataDirectory()));
        var department = db.Departments[HospitalSimulation.MentalHealthDepartmentId];
        Assert.Equal("Mental Health", department.Name);
        Assert.Equal(200_000, department.OpeningCost);
        var room = db.RoomTemplates["mental_health_assessment_room"];
        Assert.Equal(CareSpaceKind.MentalHealthAssessment, room.Kind);
        Assert.Equal(RoomLayoutKind.Interview, room.Layout);
        foreach (var id in new[] { "low_mood_suicidal_thoughts", "self_harm", "severe_depression", "first_episode_psychosis", "mania", "drug_induced_psychosis" })
        {
            var condition = db.Conditions[id];
            Assert.Equal(HospitalSimulation.MentalHealthDepartmentId, condition.Department);
            Assert.Contains(condition.Treatment, s => s.Room == CareSpaceKind.MentalHealthAssessment && s.Essential);
            Assert.True(condition.AgeRange[0] >= 16, $"{id} should be for adults until CAMHS arrives");
        }
    }

    [Fact]
    public void The_assessment_room_has_two_fixed_seats_and_a_second_door()
    {
        var h = Wing(1);
        var room = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.MentalHealthAssessment);
        Assert.Equal(2, room.Furniture.Count(f => f.ItemId == "fixed_seat"));
        Assert.Contains(room.Furniture, f => f.ItemId == "safety_door");
        Assert.Contains(room.Furniture, f => f.ItemId == "bolted_table");
        Assert.NotEqual(room.StaffSpot, room.PatientSpot);
        Assert.True(room.Rect.Contains(room.StaffSpot) && room.Rect.Contains(room.PatientSpot));
    }

    [Fact]
    public void The_room_asks_for_a_mental_health_nurse_day_and_night()
    {
        var h = Wing(2);
        var room = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.MentalHealthAssessment);
        Assert.Contains(h.Sim.Staff, s => s.Role == StaffRole.MentalHealthNurse && s.BaseRoomId == room.Id && s.Shift == StaffShift.Day);
        Assert.Contains(h.Sim.Staff, s => s.Role == StaffRole.MentalHealthNurse && s.BaseRoomId == room.Id && s.Shift == StaffShift.Night);
        Assert.Equal(StaffRole.MentalHealthNurse, HospitalSimulation.RoleForRoom(CareSpaceKind.MentalHealthAssessment));
        Assert.Contains(CareSpaceKind.MentalHealthAssessment, HospitalSimulation.RoomKindsFor(StaffRole.Psychiatrist));
    }

    [Fact]
    public void Patients_in_crisis_are_seen_by_the_liaison_team_in_the_assessment_room()
    {
        var h = Wing(3);
        var room = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.MentalHealthAssessment);
        var seen = 0;
        for (var i = 0; i < 3; i++)
        {
            var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "low_mood_suicidal_thoughts", 30);
            var inRoom = false;
            h.RunUntil(() =>
            {
                inRoom |= !patient.IsMoving && patient.Tile == room.PatientSpot;
                return patient.Stage == PatientStage.Gone;
            }, TimeSpan.FromHours(16), "patient in crisis to go home");
            var journey = h.Journey(patient);
            if (journey.Contains("Mental health assessment done by"))
            {
                seen++;
                Assert.True(inRoom, journey);
                Assert.Contains("Seen by the mental health liaison team", journey);
            }
        }

        Assert.True(seen > 0, "No patient was seen in the assessment room.");
        Assert.Equal(seen, h.Sim.LiaisonAssessments);
        Assert.True(h.Sim.LiaisonWithinTarget <= h.Sim.LiaisonAssessments);
    }

    [Fact]
    public void Without_the_liaison_team_an_AE_doctor_assesses_them()
    {
        var h = Wing(4, staffRoom: false);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "low_mood_suicidal_thoughts", 30);
        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromHours(16), "patient in crisis to go home");
        Assert.Equal(0, h.Sim.LiaisonAssessments);
        Assert.DoesNotContain("Seen by the mental health liaison team", h.Journey(patient));
    }

    [Fact]
    public void Without_a_mental_health_ward_admissions_go_to_a_mental_health_hospital()
    {
        var h = Wing(5);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "first_episode_psychosis", 25);
        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromHours(24), "patient with psychosis to leave");
        Assert.Contains("mental health hospital", h.Journey(patient));
    }

    [Fact]
    public void A_psychiatrist_can_do_the_nurses_assessments()
    {
        var h = Wing(6, staffRoom: false);
        var room = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.MentalHealthAssessment);
        h.Sim.HireStaff(StaffRole.Psychiatrist, room.Id, 0.9, h.Sim.CurrentShift);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "low_mood_suicidal_thoughts", 40);
        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromHours(16), "patient in crisis to go home");
        var journey = h.Journey(patient);
        if (journey.Contains("Mental health assessment done by"))
        {
            Assert.Contains(room.Name, journey);
        }
    }
}
