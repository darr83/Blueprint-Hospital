using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Paediatrics stage 4: play specialists, the play room and teenage room, the parents' room and the children's clinic.</summary>
public class PlayRoomTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A children's wing west of the starter A&amp;E: <paramref name="north"/> rooms north of a corridor, <paramref name="south"/> rooms south of it.</summary>
    private static SimulationHarness Wing(int seed, (string Template, int W)[] north, (string Template, int W)[] south)
    {
        var h = new SimulationHarness(seed);
        h.Sim.AdjustBalance(5_000_000);
        Assert.True(h.Sim.UnlockDepartment(HospitalSimulation.PaediatricsDepartmentId).CanBuild);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 40, B.Y, 40, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 40, B.Y + 9, 44, 8), WallSide.South)).CanBuild);
        void Row(IEnumerable<(string Template, int W)> rooms, int y, int length, WallSide door)
        {
            var x = B.X - 40;
            foreach (var (template, w) in rooms)
            {
                var check = h.Sim.BuildRoom(h.Sim.AutoPlan(template, new GridRect(x, y, w, length), doorSide: door));
                Assert.True(check.CanBuild, $"{template}: {string.Join("; ", check.Problems)}");
                x += w;
            }
        }

        Row(north, B.Y, 9, WallSide.South);
        Row(south, B.Y + 17, 15, WallSide.North);
        h.StaffToRecommended();
        return h;
    }

    [Fact]
    public void The_stage_4_rooms_staff_and_clinic_are_in_the_game_data()
    {
        var h = new SimulationHarness();
        Assert.Equal(CareSpaceKind.PlayRoom, h.Database.RoomTemplates["play_room"].Kind);
        Assert.Equal(CareSpaceKind.PlayRoom, h.Database.RoomTemplates["teen_room"].Kind);
        Assert.Equal(CareSpaceKind.ParentsRoom, h.Database.RoomTemplates["parents_room"].Kind);
        Assert.Equal(CareSpaceKind.Clinic, h.Database.RoomTemplates["childrens_clinic"].Kind);
        Assert.Equal(StaffRole.Paediatrician, h.Database.Clinics["childrens_clinic"].Clinician);
        Assert.True(h.Database.Clinics["childrens_clinic"].AgeRange![1] < HospitalSimulation.AdultAge);
        Assert.Equal(3, StaffGrades.For(StaffRole.PlaySpecialist).Count());
        Assert.Contains(StaffRole.PlaySpecialist, HospitalSimulation.RecruitableRoles);
    }

    [Fact]
    public void A_play_room_needs_a_play_specialist_by_day()
    {
        var h = Wing(30, new[] { ("play_room", 10) }, Array.Empty<(string, int)>());
        var room = h.Sim.Spaces.Single(s => s.TemplateId == "play_room");
        var needs = h.Sim.RecommendedStaffing().Where(n => n.RoomId == room.Id).ToList();
        var need = Assert.Single(needs);
        Assert.Equal(StaffRole.PlaySpecialist, need.Role);
        Assert.Equal(StaffShift.Day, need.Shift);
        Assert.Contains(h.Sim.Staff, s => s.Role == StaffRole.PlaySpecialist && s.BaseRoomId == room.Id);
        Assert.Empty(h.Sim.PlayRoomProblems(room));
        Assert.True(room.Seats.Count >= 2, $"{room.Seats.Count} seats");
    }

    [Fact]
    public void Waiting_children_play_in_the_play_room_with_their_parent_teenagers_use_the_teenage_room_and_adults_never_do()
    {
        var h = Wing(31, new[] { ("play_room", 10), ("teen_room", 10) }, Array.Empty<(string, int)>());
        var play = h.Sim.Spaces.Single(s => s.TemplateId == "play_room");
        var teen = h.Sim.Spaces.Single(s => s.TemplateId == "teen_room");

        // A busy A&E, so children wait long enough to want to go and play.
        for (var i = 0; i < 10; i++)
        {
            h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration", age: 30 + i);
        }

        var little = new[] { 5, 6, 8 }.Select(age => h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration", age: age)).ToList();
        var teenager = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration", age: 14);

        var parentSatWithChild = false;
        var adultPlayed = false;
        var littleInTeenRoom = false;
        h.RunUntil(() =>
        {
            adultPlayed |= h.Sim.Patients.Any(p => p.Age >= HospitalSimulation.AdultAge && (play.Rect.Contains(p.Tile) || teen.Rect.Contains(p.Tile)));
            littleInTeenRoom |= little.Any(c => h.Sim.PlayRoomOf(c) == teen);
            foreach (var child in little.Where(c => h.Sim.PlayRoomOf(c) == play))
            {
                parentSatWithChild |= h.Sim.ParentOf(child) is { } parent && !parent.IsMoving && play.Rect.Contains(parent.Tile);
            }

            return parentSatWithChild && teenager.Timeline.Any(e => e.Text.Contains(teen.Name));
        }, TimeSpan.FromHours(8), "a child to play with their parent and the teenager to use the teenage room");

        Assert.Contains(little, c => c.Timeline.Any(e => e.Text.StartsWith($"Went to {play.Name}", StringComparison.Ordinal)));
        Assert.False(adultPlayed, "an adult went into a play room");
        Assert.False(littleInTeenRoom, "a young child used the teenage room");
    }

    [Fact]
    public void A_parent_takes_a_break_in_the_parents_room_by_day_and_goes_back_to_the_bedside()
    {
        var h = Wing(32, new[] { ("parents_room", 8) }, new[] { ("childrens_ward", 22) });
        var lounge = h.Sim.Spaces.Single(s => s.TemplateId == "parents_room");
        Assert.NotEmpty(lounge.Seats);
        var child = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "diabetic_ketoacidosis", age: 9);
        h.RunUntil(() => child.Stage is PatientStage.OnWard or PatientStage.Gone, TimeSpan.FromHours(12), "the child to reach the ward");
        Assert.True(child.Stage == PatientStage.OnWard, h.Journey(child));

        var parent = h.Sim.ParentOf(child)!;
        h.RunUntil(() => (parent.Activity == ParentActivity.InParentsRoom && lounge.Rect.Contains(parent.Tile) && !parent.IsMoving) || child.Stage == PatientStage.Gone,
            TimeSpan.FromDays(2), "the parent to take a break in the parents' room");
        Assert.True(child.Stage != PatientStage.Gone, "went home before their parent took a break");
        Assert.False(h.Sim.IsParentBedtime);
        Assert.Contains(child.Timeline, e => e.Text.Contains("parents' room"));

        h.RunUntil(() => parent.FoldOutBed is not null || child.Stage == PatientStage.Gone, TimeSpan.FromHours(2), "the parent to go back to the bedside");
        Assert.True(parent.FoldOutBed is not null || child.Stage == PatientStage.Gone);
    }
}
