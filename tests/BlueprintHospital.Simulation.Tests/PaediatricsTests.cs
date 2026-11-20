using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Paediatrics: the children's ward and HDU, children's nurses and paediatric doctors, and keeping children and adults apart.</summary>
public class PaediatricsTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A west wing with a corridor and, south of it, the given rooms side by side; staffed to the recommended numbers.</summary>
    private static SimulationHarness Wing(int seed, params (string Template, int W)[] rooms)
    {
        var h = new SimulationHarness(seed);
        h.Sim.AdjustBalance(5_000_000);
        Build(h.Sim.UnlockDepartment(HospitalSimulation.PaediatricsDepartmentId), "paediatrics");
        Build(h.Sim.ExtendBuilding(new GridRect(B.X - 40, B.Y, 40, 34)), "extension");
        Build(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 40, B.Y + 9, 44, 8), WallSide.South)), "corridor");
        var x = B.X - 40;
        foreach (var (template, w) in rooms)
        {
            Build(h.Sim.BuildRoom(h.Sim.AutoPlan(template, new GridRect(x, B.Y + 17, w, 15))), template);
            x += w;
        }

        h.StaffToRecommended();
        return h;
    }

    private static void Build(BuildCheck result, string what) => Assert.True(result.CanBuild, $"{what}: {string.Join("; ", result.Problems)}");

    private static CareSpace Room(SimulationHarness h, string template) => h.Sim.Spaces.Single(s => s.TemplateId == template);

    [Fact]
    public void The_department_its_rooms_and_its_staff_are_in_the_game_data()
    {
        var h = new SimulationHarness();
        var department = h.Database.Departments[HospitalSimulation.PaediatricsDepartmentId];
        Assert.Equal(225_000, department.OpeningCost);
        Assert.Equal("paediatrics", h.Database.RoomTemplates["childrens_ward"].Department);
        Assert.Contains("parent_bed", h.Database.RoomTemplates["childrens_ward"].Furniture);
        Assert.Equal(WardUnit.HighDependency, h.Database.RoomTemplates["childrens_hdu"].Unit);
        Assert.Equal(4, StaffGrades.For(StaffRole.ChildrensNurse).Count());
        Assert.Equal(4, StaffGrades.For(StaffRole.Paediatrician).Count());
        Assert.Equal("children's nurse", StaffGrades.RoleName(StaffRole.ChildrensNurse));
    }

    [Fact]
    public void A_childrens_ward_has_a_parent_bed_beside_every_bed()
    {
        var h = Wing(1, ("childrens_ward", 26));
        var ward = Room(h, "childrens_ward");
        Assert.True(ward.Beds.Count >= 2);
        Assert.Equal(ward.Beds.Count, ward.Furniture.Count(f => f.ItemId == "parent_bed"));
        var beds = ward.Furniture.Where(f => f.ItemId is "hospital_bed" or "parent_bed").ToList();
        Assert.All(beds, b => Assert.False(beds.Any(o => o != b && o.Overlaps(b.X, b.Y, b.Width, b.Length)), "beds overlap"));
    }

    [Fact]
    public void Only_childrens_nurses_and_paediatric_doctors_are_posted_to_the_childrens_wards()
    {
        var h = Wing(2, ("medical_ward", 14), ("childrens_ward", 14), ("childrens_hdu", 12));
        var ward = Room(h, "childrens_ward");
        var hdu = Room(h, "childrens_hdu");

        Assert.DoesNotContain(h.Sim.PostingsFor(StaffRole.Nurse), p => p.RoomId == ward.Id || p.RoomId == hdu.Id);
        Assert.DoesNotContain(h.Sim.PostingsFor(StaffRole.Doctor), p => p.RoomId == ward.Id);
        Assert.Contains(h.Sim.PostingsFor(StaffRole.HealthcareAssistant), p => p.RoomId == ward.Id);
        Assert.Equal(new[] { ward.Id, hdu.Id }.Order(), h.Sim.PostingsFor(StaffRole.ChildrensNurse).Select(p => p.RoomId!).Order());
        Assert.Equal(new[] { ward.Id, hdu.Id }.Order(), h.Sim.PostingsFor(StaffRole.Paediatrician).Select(p => p.RoomId!).Order());

        // The recommended team: a children's nurse for every 4 beds (2 on the HDU) and a paediatric doctor, day and night.
        var needs = h.Sim.RecommendedStaffing();
        Assert.Contains(needs, n => n.RoomId == ward.Id && n.Role == StaffRole.ChildrensNurse && n.Recommended == (ward.Beds.Count + 3) / 4);
        Assert.Contains(needs, n => n.RoomId == hdu.Id && n.Role == StaffRole.ChildrensNurse && n.Recommended == (hdu.Beds.Count + 1) / 2);
        Assert.Contains(needs, n => n.RoomId == ward.Id && n.Role == StaffRole.Paediatrician);
        Assert.DoesNotContain(needs, n => (n.RoomId == ward.Id || n.RoomId == hdu.Id) && n.Role is StaffRole.Nurse or StaffRole.Doctor);
        Assert.All(h.Sim.Staff.Where(s => s.BaseRoomId == ward.Id || s.BaseRoomId == hdu.Id), s => Assert.True(s.Role is StaffRole.ChildrensNurse or StaffRole.Paediatrician or StaffRole.HealthcareAssistant, s.Role.ToString()));

        // An adult nurse can't be moved there either.
        var nurse = h.Sim.Staff.First(s => s.Role == StaffRole.Nurse);
        Assert.False(h.Sim.MoveStaff(nurse.Id, ward.Id));
    }

    [Fact]
    public void A_child_is_admitted_to_the_childrens_ward_and_looked_after_by_the_childrens_team()
    {
        var h = Wing(3, ("medical_ward", 18), ("childrens_ward", 22));
        var ward = Room(h, "childrens_ward");
        var child = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "diabetic_ketoacidosis", age: 14);
        var adult = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "diabetic_ketoacidosis", age: 40);

        h.RunUntil(() => child.Stage is PatientStage.OnWard or PatientStage.Gone, TimeSpan.FromHours(12), "the child to reach a ward");
        Assert.True(child.Stage == PatientStage.OnWard, h.Journey(child));
        Assert.Equal(ward.Id, child.CareSpaceId);

        h.RunUntil(() => adult.Stage is PatientStage.OnWard or PatientStage.Gone, TimeSpan.FromHours(12), "the adult to reach a ward");
        Assert.True(adult.Stage == PatientStage.OnWard, h.Journey(adult));
        Assert.Equal(Room(h, "medical_ward").Id, adult.CareSpaceId);

        h.RunUntil(() => child.Stage == PatientStage.Gone, TimeSpan.FromDays(6), "the child to go home");
        var journey = h.Journey(child);
        Assert.Equal(PatientOutcome.Discharged, child.Outcome);
        var paediatricians = h.Sim.Staff.Where(s => s.Role == StaffRole.Paediatrician).Select(s => s.Name).ToList();
        var childrensNurses = h.Sim.Staff.Where(s => s.Role == StaffRole.ChildrensNurse).Select(s => s.Name).ToList();
        Assert.Contains(paediatricians, name => journey.Contains($"Clerked by {name}"));
        Assert.Contains(childrensNurses, name => journey.Contains($"by {name} after"));
    }

    [Fact]
    public void Without_a_childrens_ward_a_child_is_transferred_even_with_room_on_an_adult_ward()
    {
        var h = Wing(4, ("medical_ward", 22));
        var child = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "diabetic_ketoacidosis", age: 12);
        h.RunUntil(() => child.Stage is PatientStage.OnWard or PatientStage.Gone, TimeSpan.FromHours(12), "the child to leave A&E");
        var journey = h.Journey(child);
        Assert.NotEqual(PatientStage.OnWard, child.Stage);
        if (child.WorkingDiagnosis?.ConditionId == "diabetic_ketoacidosis")
        {
            Assert.Equal(PatientOutcome.Transferred, child.Outcome);
            Assert.Contains("children's ward", journey);
        }
    }

    [Fact]
    public void Adults_never_use_the_childrens_wards()
    {
        var h = Wing(5, ("childrens_ward", 22), ("childrens_hdu", 14));
        var adult = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "diabetic_ketoacidosis", age: 35);
        h.RunUntil(() => adult.Stage is PatientStage.OnWard or PatientStage.Gone, TimeSpan.FromHours(12), "the adult to leave A&E");
        Assert.True(adult.Stage == PatientStage.Gone, h.Journey(adult));
        Assert.All(h.Sim.PaediatricWards.SelectMany(w => w.Beds), b => Assert.Null(b.OccupantId));
    }

    [Fact]
    public void Childrens_staff_with_no_childrens_ward_are_posted_when_one_is_built()
    {
        var h = new SimulationHarness(6);
        h.Sim.AdjustBalance(5_000_000);
        var nurse = h.Sim.HireStaff(StaffRole.ChildrensNurse, null, skill: 0.7);
        Assert.Null(nurse.BaseRoomId);
        Assert.Equal("No children's ward yet", Assert.Single(h.Sim.PostingsFor(StaffRole.ChildrensNurse)).Name);

        Build(h.Sim.UnlockDepartment(HospitalSimulation.PaediatricsDepartmentId), "paediatrics");
        Build(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)), "extension");
        Build(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)), "corridor");
        Build(h.Sim.BuildRoom(h.Sim.AutoPlan("childrens_ward", new GridRect(B.X - 30, B.Y + 17, 22, 15))), "childrens_ward");
        h.RunFor(TimeSpan.FromSeconds(2));
        Assert.Equal(Room(h, "childrens_ward").Id, nurse.BaseRoomId);
    }
}
