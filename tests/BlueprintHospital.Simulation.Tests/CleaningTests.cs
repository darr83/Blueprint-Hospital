using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

public class CleaningTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    private static CareSpace Layout(SimulationHarness h, double widthM, double lengthM, WallSide side = WallSide.South) =>
        RoomLayoutEngine.Build(h.Database.RoomTemplates["cleaning_room"], h.Database, "r", "R",
            RoomLayoutEngine.RectFor(new GridCoord(0, 0), Units.MetresToTiles(widthM), Units.MetresToTiles(lengthM), side), side);

    /// <summary>A west wing with a corridor and, south of it, a cleaning room.</summary>
    private static SimulationHarness WithCleaningRoom(bool room = true)
    {
        var h = new SimulationHarness(5);
        h.Sim.AdjustBalance(2_000_000);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        if (room)
        {
            var check = h.Sim.BuildRoom(h.Sim.AutoPlan("cleaning_room", new GridRect(B.X - 11, B.Y + 17, 5, 6)));
            Assert.True(check.CanBuild, string.Join("; ", check.Problems));
        }

        return h;
    }

    [Fact]
    public void A_cleaning_room_has_a_sink_shelves_a_brush_rack_and_a_mop_bucket_at_every_size_and_turn()
    {
        var h = new SimulationHarness();
        foreach (var side in Enum.GetValues<WallSide>())
        {
            foreach (var (w, l) in new[] { (2.0, 2.5), (2.5, 3.0), (3.5, 3.5) })
            {
                var room = Layout(h, w, l, side);
                foreach (var item in new[] { "cleaners_sink", "cleaning_shelves", "broom_rack", "mop_bucket" })
                {
                    Assert.True(room.Furniture.Any(f => f.ItemId == item), $"{item} missing at {w} x {l} facing {side}");
                }

                Assert.True(room.Rect.Contains(room.StaffSpot));
                Assert.DoesNotContain(room.Furniture, f => f.Overlaps(room.StaffSpot.X, room.StaffSpot.Y, 1, 1));
            }
        }
    }

    [Fact]
    public void Cleaners_can_be_hired_for_the_whole_hospital_and_are_on_the_list()
    {
        var h = WithCleaningRoom();
        Assert.Contains(StaffRole.Cleaner, HospitalSimulation.RecruitableRoles);
        Assert.Equal("cleaners", StaffGrades.RoleName(StaffRole.Cleaner, plural: true));
        Assert.Equal("Whole hospital", h.Sim.PostingsFor(StaffRole.Cleaner).Single().Name);
        Assert.Contains(h.Sim.RecommendedStaffing(), n => n.Role == StaffRole.Cleaner && n.Recommended >= 1);
        Assert.Equal("Facilities", h.Database.RoomTemplates["cleaning_room"].Category);
    }

    [Fact]
    public void A_cleaner_collects_their_kit_and_mops_up_a_spill()
    {
        var h = WithCleaningRoom();
        var cleaner = h.Sim.HireStaff(StaffRole.Cleaner, shift: h.Sim.CurrentShift);
        var spill = h.Sim.SpillForTest(new GridCoord(B.X - 20, B.Y + 12));

        h.RunUntil(() => h.Sim.CleanerJobOf(cleaner) is { HasKit: true }, TimeSpan.FromMinutes(30), "the cleaner to collect their kit");
        h.RunUntil(() => !h.Sim.FloorAccidents.Contains(spill), TimeSpan.FromHours(1), "the spill to be mopped up");
        Assert.True(h.Sim.Now < spill.Until, "The spill dried up on its own instead of being mopped.");
    }

    [Fact]
    public void Without_a_cleaner_a_spill_stays_for_hours_then_dries()
    {
        var h = WithCleaningRoom();
        var spill = h.Sim.SpillForTest(new GridCoord(B.X - 20, B.Y + 12));
        h.RunFor(TimeSpan.FromHours(2));
        Assert.Contains(spill, h.Sim.FloorAccidents);
        h.RunFor(TimeSpan.FromHours(1.1));
        Assert.DoesNotContain(spill, h.Sim.FloorAccidents);
    }

    [Fact]
    public void Without_a_cleaning_room_a_cleaner_has_no_kit()
    {
        var h = WithCleaningRoom(room: false);
        var cleaner = h.Sim.HireStaff(StaffRole.Cleaner, shift: h.Sim.CurrentShift);
        h.Sim.SpillForTest(new GridCoord(B.X - 20, B.Y + 12));
        h.RunFor(TimeSpan.FromMinutes(30));
        Assert.Null(h.Sim.CleanerJobOf(cleaner));
        Assert.Single(h.Sim.FloorAccidents);
    }

    [Fact]
    public void With_no_spills_a_cleaner_works_round_the_hospital_and_takes_their_breaks()
    {
        var h = WithCleaningRoom();
        var cleaner = h.Sim.HireStaff(StaffRole.Cleaner, shift: h.Sim.CurrentShift);
        var spots = new HashSet<GridCoord>();
        var swept = false;
        var breaks = false;
        for (var i = 0; i < 12 * 60; i++)
        {
            h.RunFor(TimeSpan.FromMinutes(1));
            if (h.Sim.CleanerJobOf(cleaner) is { Stage: CleanerStage.Cleaning } job)
            {
                spots.Add(job.Spot);
                swept |= !job.Mopping;
            }

            breaks |= cleaner.IsOnBreak;
        }

        Assert.True(spots.Count >= 4, $"Only cleaned {spots.Count} spots.");
        Assert.True(swept, "Never swept with the brush.");
        Assert.True(breaks, "Never took a break.");
    }
}
