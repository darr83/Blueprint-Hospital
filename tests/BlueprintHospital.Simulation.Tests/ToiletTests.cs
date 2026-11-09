using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

public class ToiletTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    private static CareSpace Layout(SimulationHarness h, string template, double widthM, double lengthM, WallSide side = WallSide.South) =>
        RoomLayoutEngine.Build(h.Database.RoomTemplates[template], h.Database, "r", "R",
            RoomLayoutEngine.RectFor(new GridCoord(0, 0), Units.MetresToTiles(widthM), Units.MetresToTiles(lengthM), side), side);

    private static int Count(CareSpace room, string item) => room.Furniture.Count(f => f.ItemId == item);

    /// <summary>A west wing with a corridor and, south of it, a toilet block, an accessible toilet and a staff toilet.</summary>
    private static SimulationHarness WithToilets(bool publicToilets = true, bool accessible = true, bool staff = true)
    {
        var h = new SimulationHarness(5);
        h.Sim.AdjustBalance(2_000_000);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        void Build(string template, GridRect rect)
        {
            var check = h.Sim.BuildRoom(h.Sim.AutoPlan(template, rect));
            Assert.True(check.CanBuild, string.Join("; ", check.Problems));
        }

        if (publicToilets)
        {
            Build("public_toilets", new GridRect(B.X - 30, B.Y + 17, 10, 8));
        }

        if (accessible)
        {
            Build("accessible_toilet", new GridRect(B.X - 18, B.Y + 17, 5, 5));
        }

        if (staff)
        {
            Build("staff_toilet", new GridRect(B.X - 11, B.Y + 17, 3, 5));
        }

        return h;
    }

    [Fact]
    public void A_toilet_block_has_more_cubicles_the_wider_it_is_and_basins_inside_the_room()
    {
        var h = new SimulationHarness();
        var counts = new[] { 3.0, 3.5, 5, 7, 10 }.Select(w => Count(Layout(h, "public_toilets", w, 3.5), "toilet_cubicle")).ToList();
        Assert.Equal(new[] { 2, 3, 4, 6, 6 }, counts);

        foreach (var side in Enum.GetValues<WallSide>())
        {
            foreach (var (w, l) in new[] { (3.0, 3.0), (5.0, 3.5), (7.0, 4.0) })
            {
                var room = Layout(h, "public_toilets", w, l, side);
                Assert.Equal(Count(room, "toilet_cubicle"), room.ToiletSpots.Count);
                Assert.Equal(room.ToiletSpots.Count, room.ToiletSpots.Distinct().Count());
                Assert.NotEmpty(room.SinkSpots);
                Assert.Equal(room.SinkSpots.Count, Count(room, "wash_basin"));
                Assert.NotEmpty(room.QueueTiles);
                Assert.All(room.ToiletSpots.Concat(room.SinkSpots).Concat(room.QueueTiles), t => Assert.True(room.Rect.Contains(t)));
            }
        }
    }

    [Fact]
    public void Accessible_and_staff_toilets_hold_one_toilet_and_a_basin_at_every_size_and_direction()
    {
        var h = new SimulationHarness();
        foreach (var side in Enum.GetValues<WallSide>())
        {
            foreach (var (template, w, l, pan) in new[] { ("accessible_toilet", 2.5, 2.5, "accessible_wc"), ("accessible_toilet", 3.0, 3.0, "accessible_wc"), ("staff_toilet", 1.5, 2.5, "toilet_pan"), ("staff_toilet", 2.0, 3.0, "toilet_pan") })
            {
                var room = Layout(h, template, w, l, side);
                Assert.Equal(1, Count(room, pan));
                Assert.Equal(1, Count(room, "wash_basin"));
                Assert.Single(room.ToiletSpots);
                Assert.Single(room.SinkSpots);
                Assert.All(room.ToiletSpots.Concat(room.SinkSpots), t => Assert.True(room.Rect.Contains(t)));
            }
        }
    }

    [Fact]
    public void Waiting_patients_use_the_toilet_wash_their_hands_and_go_back_to_their_seat()
    {
        var h = WithToilets(staff: false);
        var patients = Enumerable.Range(0, 14).Select(_ => h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration")).ToList();

        h.RunUntil(() => h.Sim.ToiletTrips.Any(t => t.Person is Patient), TimeSpan.FromHours(6), "a waiting patient to go to the toilet");
        var trip = h.Sim.ToiletTrips.First(t => t.Person is Patient);
        var patient = (Patient)trip.Person;
        var room = h.Sim.ToiletRooms.Single(r => r.Id == trip.RoomId);
        Assert.NotEqual(CareSpaceKind.StaffToilet, room.Kind);

        var washed = false;
        h.RunUntil(() =>
        {
            washed |= h.Sim.ToiletTripOf(patient)?.Stage == ToiletStage.Washing;
            return h.Sim.ToiletTripOf(patient) is null;
        }, TimeSpan.FromMinutes(40), "the patient to finish at the toilet");

        h.RunFor(TimeSpan.FromMinutes(5));
        if (patient.Stage is PatientStage.WaitingForTriage or PatientStage.WaitingForSpace && patient.BusyWithTaskId is null)
        {
            Assert.True(washed, "the patient didn't wash their hands");
            Assert.Contains(patient.Timeline, e => e.Text.StartsWith("Went to the toilet"));
            Assert.False(h.Sim.ToiletRooms.Any(r => r.Rect.Contains(patient.Tile)), "patient left in the toilets");
        }
    }

    [Fact]
    public void Staff_on_a_break_use_the_staff_toilet_and_patients_never_do()
    {
        var h = WithToilets();
        foreach (var _ in Enumerable.Range(0, 10))
        {
            h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration");
        }

        var staffToilet = h.Sim.ToiletRooms.Single(r => r.Kind == CareSpaceKind.StaffToilet);
        var staffWent = false;
        for (var i = 0; i < 24 * 60 && !staffWent; i++)
        {
            h.RunFor(TimeSpan.FromMinutes(1));
            Assert.DoesNotContain(h.Sim.ToiletTrips, t => t.RoomId == staffToilet.Id && t.Person is not StaffMember);
            staffWent = h.Sim.ToiletTrips.Any(t => t.Person is StaffMember);
        }

        Assert.True(staffWent, "no member of staff went to the toilet on a break in a day");
    }

    [Fact]
    public void Without_public_toilets_patients_walk_out_more_and_sometimes_have_an_accident()
    {
        var h = WithToilets(publicToilets: false, accessible: false, staff: true);
        Assert.False(h.Sim.HasPublicToilets);
        Assert.Equal(0.05, HospitalSimulation.AccidentChanceFor(40), 6);
        Assert.True(HospitalSimulation.AccidentChanceFor(6) > HospitalSimulation.AccidentChanceFor(40));

        foreach (var _ in Enumerable.Range(0, 20))
        {
            h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration");
        }

        for (var i = 0; i < 12 * 60; i++)
        {
            h.RunFor(TimeSpan.FromMinutes(1));
            Assert.DoesNotContain(h.Sim.ToiletTrips, t => t.Person is not StaffMember);
        }

        var built = WithToilets(staff: false);
        Assert.True(built.Sim.HasPublicToilets);
    }

    [Fact]
    public void Toilets_are_in_the_facilities_build_menu()
    {
        var h = new SimulationHarness();
        foreach (var id in new[] { "public_toilets", "accessible_toilet", "staff_toilet" })
        {
            Assert.Equal("Facilities", h.Database.RoomTemplates[id].Category);
        }
    }
}
