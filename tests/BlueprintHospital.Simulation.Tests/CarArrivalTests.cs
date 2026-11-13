using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Transport;
using BlueprintHospital.Simulation.World;
using Xunit;

namespace BlueprintHospital.Simulation.Tests;

public class CarArrivalTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    public static IEnumerable<object[]> Plans() => SitePlanTests.AnyWall()
        .Append(new object[] { SitePlan.Standard.EntranceSide, SitePlan.Standard.EntranceAt, SitePlan.Standard.AmbulanceSide, SitePlan.Standard.AmbulanceAt });

    [Fact]
    public void The_usual_site_has_a_marked_car_park_and_a_taxi_rank()
    {
        var layout = StarterSite.Create().Layout!;
        Assert.Equal(12, layout.ParkingBays.Count);
        Assert.NotNull(layout.CarPark);
        Assert.NotNull(layout.MainCarPark);

        // The spaces by the entrance come first, and stay within the building's width so wings can still be added.
        Assert.All(layout.ParkingBays.Take(4), bay => Assert.True(layout.CarPark!.Value.Contains(new GridCoord(bay.X, bay.Y))));
        Assert.True(layout.CarPark!.Value.X >= B.X - 4 && layout.CarPark.Value.Right <= B.Right + 4);
        Assert.Equal(2, layout.DisabledBays);
        Assert.NotNull(layout.TaxiRank);
        Assert.Equal(StarterSite.TaxiStands, layout.TaxiStands.Count);
    }

    [Theory]
    [MemberData(nameof(Plans))]
    public void The_car_park_and_taxi_rank_fit_on_any_site_plan(WallSide front, int frontAt, WallSide bays, int baysAt)
    {
        var site = StarterSite.Create(new SitePlan(front, frontAt, bays, baysAt));
        var layout = site.Layout!;
        var lane = layout.CarLane!;
        Assert.Equal(TileKind.Road, site[lane.Entry]);
        Assert.Equal(TileKind.Road, site[lane.Exit]);

        // Every space is car park, big enough for a car, clear of the building and the ambulance bays, and opens onto the road.
        foreach (var bay in layout.ParkingBays)
        {
            Assert.All(bay.Tiles(), t => Assert.Equal(TileKind.Parking, site[t]));
            Assert.True(Math.Max(bay.Width, bay.Length) >= CarVehicle.LengthTiles + 1, $"Space {bay} is too short.");
            Assert.True(Math.Min(bay.Width, bay.Length) >= 5, $"Space {bay} is too narrow.");
            Assert.False(bay.Intersects(B));
            Assert.All(layout.AmbulanceBays, a => Assert.False(a.Intersects(bay)));
            Assert.All(layout.ParkingBays.Where(o => o != bay), o => Assert.False(o.Intersects(bay)));
            var centre = new GridCoord(bay.X + (bay.Width / 2), bay.Y + (bay.Length / 2));
            Assert.All(StraightLine(lane.Beside(centre), centre), t => Assert.True(site[t] is TileKind.Road or TileKind.Parking, $"{t} between the road and space {bay} is {site[t]}."));
        }

        // The rank is a lay-by on the road with its stands inside it.
        if (layout.TaxiRank is { } rank)
        {
            Assert.All(rank.Tiles(), t => Assert.Equal(TileKind.TaxiRank, site[t]));
            Assert.All(layout.TaxiStands, s => Assert.True(rank.Contains(s)));
            Assert.All(layout.AmbulanceBays, a => Assert.False(a.Intersects(rank)));
            Assert.All(layout.ParkingBays, p => Assert.False(p.Intersects(rank)));
            Assert.False(rank.Intersects(layout.FrontPath));
        }

        Assert.True(layout.ParkingBays.Count > 0 || layout.TaxiRank is not null, "Nowhere for cars to stop.");
    }

    [Theory]
    [MemberData(nameof(Plans))]
    public void A_patient_who_drives_parks_walks_in_and_the_car_waits(WallSide front, int frontAt, WallSide bays, int baysAt)
    {
        var h = new SimulationHarness(startWith: StartingHospital.EmptyBuilding, plan: new SitePlan(front, frontAt, bays, baysAt));
        var sim = h.Sim;
        if (sim.ParkingBays.Count == 0)
        {
            return;
        }

        // Fill the car park a space at a time, so every space gets tried; follow the last car in.
        for (var i = 1; i < sim.ParkingBays.Count - sim.Site.Layout!.DisabledBays; i++)
        {
            Assert.True(sim.SpawnCarArrival(sim.GenerateWalkIn(), CarTrip.Parks));
        }

        h.RunUntil(() => sim.Cars.All(c => c.State == CarState.Parked), TimeSpan.FromMinutes(30), "the car park filled up");
        Assert.All(sim.Cars, c => Assert.True(sim.Nav.IsWalkable(sim.CarDoorTile(c)), $"Nowhere to get out of the car in space {c.Slot}."));
        Assert.All(sim.Cars, c => Assert.True(sim.ParkingBays[c.Slot].Contains(sim.CarDoorTile(c)), $"The door of the car in space {c.Slot} opens outside it."));
        var patient = sim.GenerateWalkIn();
        Assert.True(sim.SpawnCarArrival(patient, CarTrip.Parks));
        var car = sim.Cars[^1];

        // Still in the car on the way in: not in the hospital yet, and the car keeps to tarmac.
        Assert.DoesNotContain(patient, sim.Patients);
        while (car.State == CarState.Arriving)
        {
            h.RunFor(TimeSpan.FromSeconds(2));
            Assert.True(sim.Site[car.Tile] is TileKind.Road or TileKind.Parking, $"Car drove over {sim.Site[car.Tile]} at {car.Tile}.");
            Assert.True(h.Clock.Now < new DateTime(2027, 1, 4, 11, 0, 0), "The car never parked.");
        }

        // Parked nose in, in its own space; the patient is out, beside it, on ground they can walk on.
        var bay = sim.ParkingBays[car.Slot];
        Assert.True(bay.Contains(car.Tile));
        var road = sim.Site.Layout!.CarLane!.Beside(car.Tile);
        Assert.Equal(((double)Math.Sign(car.Tile.X - road.X), (double)Math.Sign(car.Tile.Y - road.Y)), car.Heading);
        Assert.Contains(patient, sim.Patients);
        Assert.True(sim.Nav.IsWalkable(sim.CarDoorTile(car)));
        Assert.Same(car, sim.ParkedCarOf(patient));

        // It stays put while they are in the hospital.
        h.RunFor(TimeSpan.FromMinutes(20));
        Assert.Equal(CarState.Parked, car.State);
    }

    [Theory]
    [MemberData(nameof(Plans))]
    public void A_taxi_drops_its_passenger_at_the_rank_and_drives_off(WallSide front, int frontAt, WallSide bays, int baysAt)
    {
        var h = new SimulationHarness(startWith: StartingHospital.EmptyBuilding, plan: new SitePlan(front, frontAt, bays, baysAt));
        var sim = h.Sim;
        if (sim.TaxiStands.Count == 0)
        {
            return;
        }

        var first = sim.GenerateWalkIn();
        var second = sim.GenerateWalkIn();
        Assert.True(sim.SpawnCarArrival(first, CarTrip.Taxi));
        Assert.True(sim.SpawnCarArrival(second, CarTrip.DropOff));
        Assert.Equal(2, sim.Cars.Select(c => c.Slot).Distinct().Count());

        h.RunUntil(() => sim.Cars.All(c => c.State == CarState.Parked), TimeSpan.FromMinutes(30), "both cars at the rank");
        Assert.All(sim.Cars, c => Assert.Equal(TileKind.TaxiRank, sim.Site[c.Tile]));
        Assert.All(sim.Cars, c => Assert.Equal(((double, double))StarterSite.CarDirection(front), c.Heading));
        Assert.Contains(first, sim.Patients);
        Assert.Contains(second, sim.Patients);
        Assert.All(sim.Cars, c => Assert.Equal(TileKind.Paving, sim.Site[sim.CarDoorTile(c)]));

        h.RunUntil(() => sim.Cars.Count == 0, TimeSpan.FromMinutes(30), "both cars gone");
    }

    [Fact]
    public void A_full_car_park_and_rank_send_people_in_on_foot()
    {
        var h = new SimulationHarness(startWith: StartingHospital.EmptyBuilding);
        var sim = h.Sim;
        for (var i = 0; i < sim.ParkingBays.Count + 4; i++)
        {
            sim.SpawnCarArrival(sim.GenerateWalkIn(), CarTrip.Parks);
        }

        Assert.True(sim.Cars.Count <= sim.ParkingBays.Count);
        Assert.Equal(sim.Cars.Count, sim.Cars.Select(c => c.Slot).Distinct().Count());
        Assert.True(sim.SpawnCarArrival(sim.GenerateWalkIn(), CarTrip.Taxi));
        Assert.True(sim.SpawnCarArrival(sim.GenerateWalkIn(), CarTrip.Taxi));
        Assert.False(sim.SpawnCarArrival(sim.GenerateWalkIn(), CarTrip.Taxi));
    }

    [Fact]
    public void The_car_leaves_once_its_owner_has_gone_home()
    {
        var h = new SimulationHarness(seed: 11);
        var sim = h.Sim;
        h.StaffToRecommended();
        var patient = sim.GenerateWalkIn();
        Assert.True(sim.SpawnCarArrival(patient, CarTrip.Parks));
        var car = sim.Cars[0];
        h.RunUntil(() => car.State == CarState.Parked, TimeSpan.FromMinutes(30), "the car parked");
        h.RunUntil(() => patient.Stage is PatientStage.Leaving or PatientStage.Gone || patient.LeftEmergencyAt is not null, TimeSpan.FromHours(12), "the patient finished in A&E");
        if (patient.Stage == PatientStage.Leaving)
        {
            // Going home: they walk back to the car, not out to the road.
            Assert.Equal(sim.CarDoorTile(car), patient.Destination ?? patient.Tile);
        }

        h.RunUntil(() => sim.Cars.Count == 0, TimeSpan.FromHours(2), "the car drove away");
    }

    [Fact]
    public void Cars_and_their_passengers_survive_a_save()
    {
        var from = new SimulationHarness(startWith: StartingHospital.EmptyBuilding);
        var parked = from.Sim.GenerateWalkIn();
        from.Sim.SpawnCarArrival(parked, CarTrip.Parks);
        from.RunUntil(() => from.Sim.Cars[0].State == CarState.Parked, TimeSpan.FromMinutes(30), "the car parked");
        var onTheWay = from.Sim.GenerateWalkIn();
        from.Sim.SpawnCarArrival(onTheWay, CarTrip.Taxi);
        from.RunFor(TimeSpan.FromSeconds(10));

        var to = new SimulationHarness(startWith: StartingHospital.EmptyBuilding);
        to.Sim.RestorePeople(from.Sim.CapturePeople());
        Assert.Equal(2, to.Sim.Cars.Count);
        var car = to.Sim.Cars.Single(c => c.Trip == CarTrip.Parks);
        Assert.Equal(CarState.Parked, car.State);
        Assert.Equal(from.Sim.Cars[0].Tile, car.Tile);
        Assert.Equal(from.Sim.Cars[0].Heading, car.Heading);

        // The taxi still has its passenger, and delivers them.
        Assert.DoesNotContain(to.Sim.Patients, p => p.Id == onTheWay.Id);
        to.RunUntil(() => to.Sim.Patients.Any(p => p.Id == onTheWay.Id), TimeSpan.FromMinutes(30), "the taxi's passenger got out");
    }

    [Fact]
    public void With_arrivals_on_some_patients_come_by_car_and_taxi_and_the_cars_go_again()
    {
        var h = new SimulationHarness(seed: 5, arrivals: true);
        var sim = h.Sim;
        h.StaffToRecommended();
        var seen = new Dictionary<int, CarTrip>();
        var mostParked = 0;
        for (var i = 0; i < 12 * 60; i++)
        {
            h.RunFor(TimeSpan.FromMinutes(1));
            foreach (var car in sim.Cars)
            {
                seen[car.Id] = car.Trip;
            }

            mostParked = Math.Max(mostParked, sim.Cars.Count(c => c.Trip == CarTrip.Parks));
        }

        Assert.Contains(CarTrip.Parks, seen.Values);
        Assert.Contains(CarTrip.Taxi, seen.Values);
        Assert.True(mostParked <= sim.ParkingBays.Count);
        Assert.True(seen.Count > sim.Cars.Count, "No car ever left.");
    }

    private static IEnumerable<GridCoord> StraightLine(GridCoord from, GridCoord to)
    {
        var current = from;
        yield return current;
        while (current != to)
        {
            current = new GridCoord(current.X + Math.Sign(to.X - current.X), current.Y + Math.Sign(to.Y - current.Y));
            yield return current;
        }
    }
}
