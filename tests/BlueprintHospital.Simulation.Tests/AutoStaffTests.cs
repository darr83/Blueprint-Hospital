using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>The "new rooms come with their minimum staff" setting.</summary>
public class AutoStaffTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    private static SimulationHarness WestWing()
    {
        var h = new SimulationHarness();
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        return h;
    }

    [Fact]
    public void Rooms_come_empty_when_the_setting_is_off()
    {
        var h = WestWing();
        var staff = h.Sim.Staff.Count;

        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_ward", new GridRect(B.X - 30, B.Y + 17, 26, 15))).CanBuild);

        Assert.Equal(staff, h.Sim.Staff.Count);
        var ward = h.Sim.Wards.Single();
        Assert.DoesNotContain(h.Sim.Staff, s => s.BaseRoomId == ward.Id);
    }

    [Fact]
    public void A_new_ward_arrives_with_its_day_and_night_team_and_leaves_old_shortages_alone()
    {
        var h = WestWing();
        var before = h.Sim.RecommendedStaffing();
        var aeShortBefore = before.Where(n => n.RoomId is null && n.Role != StaffRole.Porter).Sum(n => n.Short);
        var staff = h.Sim.Staff.Count;

        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_ward", new GridRect(B.X - 30, B.Y + 17, 26, 15)), withStaff: true).CanBuild);

        var ward = h.Sim.Wards.Single();
        var after = h.Sim.RecommendedStaffing();
        Assert.All(after.Where(n => n.RoomId == ward.Id), n => Assert.Equal(0, n.Short));
        foreach (var shift in new[] { StaffShift.Day, StaffShift.Night })
        {
            Assert.Contains(h.Sim.Staff, s => s.BaseRoomId == ward.Id && s.Role == StaffRole.Nurse && s.Shift == shift);
            Assert.Contains(h.Sim.Staff, s => s.BaseRoomId == ward.Id && s.Role == StaffRole.Doctor && s.Shift == shift);
        }

        // Shortages the player already had (A&E) are still theirs to fill.
        Assert.Equal(aeShortBefore, after.Where(n => n.RoomId is null && n.Role != StaffRole.Porter).Sum(n => n.Short));

        var hired = h.Sim.Staff.Count - staff;
        Assert.True(hired > 0);
        Assert.Equal(hired, (int)h.Sim.History.Total(StatsPeriod.AllTime, h.Sim.Now, StatKeys.Hired));
        Assert.All(h.Sim.Staff.Where(s => s.BaseRoomId == ward.Id), s => Assert.Equal(StaffGrades.For(s.Role).First().Title, s.Grade));
        Assert.All(h.Sim.Staff.Where(s => s.BaseRoomId == ward.Id), s => Assert.True(s.Salary > 0));
        Assert.Contains(h.Sim.Notifications.Recent, n => n.Text.Contains("came with its minimum staff"));
    }

    [Fact]
    public void A_new_theatre_opens_straight_away_with_a_surgeon_and_an_anaesthetist()
    {
        var h = WestWing();

        var built = h.Sim.BuildRoom(h.Sim.AutoPlan("operating_theatre", new GridRect(B.X - 30, B.Y + 17, 12, 12)), withStaff: true);
        Assert.True(built.CanBuild, string.Join("; ", built.Problems));

        var theatre = h.Sim.SpacesOfKind(Hospital.CareSpaceKind.Theatre).Single();
        Assert.True(h.Sim.IsStaffed(theatre));
    }
}
