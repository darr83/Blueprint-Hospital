using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

public class RefreshmentTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    private static CareSpace Layout(SimulationHarness h, string template, double widthM, double lengthM, WallSide side = WallSide.South) =>
        RoomLayoutEngine.Build(h.Database.RoomTemplates[template], h.Database, "r", "R",
            RoomLayoutEngine.RectFor(new GridCoord(0, 0), Units.MetresToTiles(widthM), Units.MetresToTiles(lengthM), side), side);

    private static int Count(CareSpace room, string item) => room.Furniture.Count(f => f.ItemId == item);

    /// <summary>A west wing with a corridor, a cafeteria and a vending point south of it.</summary>
    private static (SimulationHarness H, CareSpace Cafe, CareSpace Vending) WithRefreshments(bool cafe = true, bool vending = true)
    {
        var h = new SimulationHarness(5);
        h.Sim.AdjustBalance(2_000_000);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        CareSpace? cafeRoom = null, vendingRoom = null;
        if (cafe)
        {
            var check = h.Sim.BuildRoom(h.Sim.AutoPlan("cafeteria", new GridRect(B.X - 30, B.Y + 17, 18, 14)));
            Assert.True(check.CanBuild, string.Join("; ", check.Problems));
            cafeRoom = h.Sim.SpacesOfKind(CareSpaceKind.Cafeteria).Single();
        }

        if (vending)
        {
            var check = h.Sim.BuildRoom(h.Sim.AutoPlan("vending_point", new GridRect(B.X - 10, B.Y + 17, 8, 6)));
            Assert.True(check.CanBuild, string.Join("; ", check.Problems));
            vendingRoom = h.Sim.SpacesOfKind(CareSpaceKind.VendingPoint).Single();
        }

        return (h, cafeRoom!, vendingRoom!);
    }

    [Fact]
    public void A_cafeteria_has_a_counter_water_machine_and_tables_and_more_of_them_the_bigger_it_is()
    {
        var h = new SimulationHarness();
        var small = Layout(h, "cafeteria", 5, 5);
        var medium = Layout(h, "cafeteria", 8, 6.5);
        var large = Layout(h, "cafeteria", 14, 10);
        var huge = Layout(h, "cafeteria", 20, 16);

        foreach (var cafe in new[] { small, medium, large, huge })
        {
            Assert.Equal(1, Count(cafe, "food_counter"));
            Assert.Equal(1, Count(cafe, "water_cooler"));
            Assert.Equal(4 * Count(cafe, "cafe_table"), Count(cafe, "cafe_chair"));
            Assert.Equal(cafe.Seats.Count, Count(cafe, "cafe_chair"));
            Assert.Equal(Count(cafe, "vending_machine"), cafe.VendingSpots.Count);
            Assert.NotEmpty(cafe.QueueTiles);
            Assert.All(cafe.VendingSpots.Concat(cafe.QueueTiles).Concat(cafe.Seats), t => Assert.True(cafe.Rect.Contains(t)));
        }

        Assert.True(Count(small, "cafe_table") >= 1);
        Assert.True(Count(medium, "cafe_table") > Count(small, "cafe_table"));
        Assert.True(Count(large, "cafe_table") > Count(medium, "cafe_table"));
        Assert.Equal(1, Count(small, "vending_machine"));
        Assert.Equal(2, Count(medium, "vending_machine"));
        Assert.Equal(4, Count(large, "vending_machine"));
        Assert.Equal(4, Count(huge, "vending_machine"));
    }

    [Fact]
    public void A_vending_point_holds_one_to_four_machines_by_width_and_a_water_machine()
    {
        var h = new SimulationHarness();
        var counts = new[] { 2.0, 2.5, 3.5, 4.5, 8 }.Select(w => Count(Layout(h, "vending_point", w, 2.5), "vending_machine")).ToList();
        Assert.Equal(new[] { 1, 2, 3, 4, 4 }, counts);
        foreach (var side in Enum.GetValues<WallSide>())
        {
            var room = Layout(h, "vending_point", 3.5, 3, side);
            Assert.Equal(1, Count(room, "water_cooler"));
            Assert.Equal(3, room.VendingSpots.Count);
        }
    }

    [Fact]
    public void Waiting_patients_fetch_food_and_drink_then_go_back_and_the_hospital_gets_a_share()
    {
        var (h, cafe, vending) = WithRefreshments();
        var patients = Enumerable.Range(0, 14).Select(_ => h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration")).ToList();

        h.RunUntil(() => h.Sim.RefreshmentTrips.Any(t => t.Person is Patient), TimeSpan.FromHours(6), "a waiting patient to go for something to eat");
        var trip = h.Sim.RefreshmentTrips.First(t => t.Person is Patient);
        var patient = (Patient)trip.Person;
        Assert.NotEqual(TriageCategory.Red, patient.Triage);
        Assert.Contains(trip.RoomId, new[] { cafe.Id, vending.Id });

        h.RunUntil(() => patient.LastRefreshmentAt is not null || h.Sim.TripOf(patient) is null, TimeSpan.FromHours(1), "the patient to buy something");
        if (patient.LastRefreshmentAt is not null)
        {
            Assert.True(h.Sim.Today[MoneyCategory.Catering] > 0);
            Assert.Contains(patient.Timeline, e => e.Text.StartsWith("Got "));
        }

        // Before long they are back (or have been called through), not left standing in the cafe.
        h.RunUntil(() => h.Sim.TripOf(patient) is null, TimeSpan.FromMinutes(40), "the patient to finish their trip");
        h.RunFor(TimeSpan.FromMinutes(5));
        if (patient.Stage is PatientStage.WaitingForTriage or PatientStage.WaitingForSpace && patient.BusyWithTaskId is null)
        {
            Assert.False(cafe.Rect.Contains(patient.Tile) || vending.Rect.Contains(patient.Tile), "patient left in the cafe");
        }

        h.RunFor(TimeSpan.FromHours(4));
        Assert.True(h.Sim.Accounts.Sum(d => d[MoneyCategory.Catering]) > 0);
    }

    [Fact]
    public void Without_a_cafe_or_vending_point_nobody_goes()
    {
        var (h, _, _) = WithRefreshments(cafe: false, vending: false);
        foreach (var _ in Enumerable.Range(0, 14))
        {
            h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration");
        }

        for (var i = 0; i < 6 * 60; i++)
        {
            h.RunFor(TimeSpan.FromMinutes(1));
            Assert.Empty(h.Sim.RefreshmentTrips);
        }

        Assert.Equal(0, h.Sim.Accounts.Sum(d => d[MoneyCategory.Catering]));
    }

    [Fact]
    public void Facilities_are_open_from_the_start_and_listed_in_the_build_menu()
    {
        var h = new SimulationHarness();
        Assert.True(h.Sim.IsUnlocked("facilities"));
        Assert.Equal("Facilities", h.Database.RoomTemplates["cafeteria"].Category);
        Assert.Equal("Facilities", h.Database.RoomTemplates["vending_point"].Category);
    }
}
