using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Transport;
using BlueprintHospital.Simulation.World;
using Xunit;
using Xunit.Abstractions;

namespace BlueprintHospital.Simulation.Tests;

public class AmbulanceBayTests(ITestOutputHelper output)
{
    // The 3D model: 5.8 m long, 2.7 m across.
    private const double Length = AmbulanceVehicle.LengthTiles;
    private const double Width = 5.4;

    [Fact]
    public void The_bays_are_big_enough_for_an_ambulance_and_clear_of_the_building_and_road()
    {
        var h = new SimulationHarness();
        var sim = h.Sim;
        var building = sim.MainBuilding.Footprint;
        Assert.Equal(4, sim.AmbulanceBays.Count);
        foreach (var bay in sim.AmbulanceBays)
        {
            Assert.True(bay.Width >= Length + 1, $"Bay {bay} is too short for an ambulance.");
            Assert.True(bay.Length >= Width + 1, $"Bay {bay} is too narrow for an ambulance.");
            Assert.True(bay.X >= building.Right + 2, $"Bay {bay} leaves no path along the wall.");
            Assert.All(bay.Tiles(), t => Assert.Equal(TileKind.AmbulanceBay, sim.Site[t]));
        }

        // Side by side, never overlapping, and the road starts where the bays end.
        foreach (var a in sim.AmbulanceBays)
        {
            Assert.All(sim.AmbulanceBays.Where(b => b != a), b => Assert.False(a.Intersects(b)));
            Assert.Equal(TileKind.Road, sim.Site[new GridCoord(a.Right, a.Y + 3)]);
        }

        // The stretcher spot for each bay is just behind its rear doors, on the path by the wall.
        for (var i = 0; i < sim.AmbulanceBays.Count; i++)
        {
            var bay = sim.AmbulanceBays[i];
            Assert.True(bay.Contains(sim.AmbulanceParkingTiles[i]));
            Assert.Equal(bay.X - 1, sim.AmbulanceBayPatientTiles[i].X);
            Assert.InRange(sim.AmbulanceBayPatientTiles[i].Y, bay.Y, bay.Bottom - 1);
        }
    }

    [Fact]
    public void Each_ambulance_reverses_into_its_own_bay_and_the_rest_queue_on_the_road()
    {
        var h = new SimulationHarness(seed: 7);
        var sim = h.Sim;

        // No one to take handover, so the crews stay put and the bays fill up.
        foreach (var member in sim.Staff.ToList())
        {
            sim.Fire(member.Id);
        }

        for (var i = 0; i < 6; i++)
        {
            sim.SpawnPatient(ArrivalMethod.Ambulance);
        }

        h.RunUntil(() => sim.Ambulances.Count(a => a.State == AmbulanceState.AtBay) == 4 && sim.Ambulances.All(a => !a.IsMoving), TimeSpan.FromHours(1), "four parked ambulances");
        foreach (var a in sim.Ambulances)
        {
            output.WriteLine($"{a.CallSign} {a.State} bay {a.BaySlot} at {a.X:0.0},{a.Y:0.0} heading {a.Heading}");
        }

        var parked = sim.Ambulances.Where(a => a.State == AmbulanceState.AtBay).ToList();
        Assert.Equal(4, parked.Select(a => a.BaySlot).Distinct().Count());
        foreach (var a in parked)
        {
            var bay = sim.AmbulanceBays[a.BaySlot!.Value];
            var (x0, y0, x1, y1) = Footprint(a);

            // Inside its own bay lines, nose out towards the road and rear doors to the building.
            Assert.InRange(x0, bay.X, bay.Right);
            Assert.InRange(x1, bay.X, bay.Right);
            Assert.InRange(y0, bay.Y, bay.Bottom);
            Assert.InRange(y1, bay.Y, bay.Bottom);
            Assert.Equal((1.0, 0.0), a.Heading);
            Assert.True(x0 > sim.MainBuilding.Footprint.Right + 1, "Parked against the wall.");
        }

        // The two that did not fit wait one behind the other on the side road, out of everyone's way.
        var queued = sim.Ambulances.Where(a => a.State == AmbulanceState.WaitingForBay).ToList();
        Assert.Equal(2, queued.Count);
        Assert.All(queued, a => Assert.Equal(TileKind.Road, sim.Site[a.Tile]));
        Assert.All(queued, a => Assert.True(a.Y > sim.AmbulanceBays.Max(b => b.Bottom) + (Length / 2), $"{a.CallSign} queues among the bays."));

        var all = sim.Ambulances.ToList();
        for (var i = 0; i < all.Count; i++)
        {
            for (var j = i + 1; j < all.Count; j++)
            {
                Assert.False(Overlap(Footprint(all[i]), Footprint(all[j])), $"{all[i].CallSign} and {all[j].CallSign} overlap.");
            }
        }

        // A nurse arrives and takes handover: as bays free up, the queue moves in, first come first served.
        var first = queued.OrderBy(a => a.Id).First();
        var second = queued.OrderBy(a => a.Id).Last();
        sim.HireStaff(BlueprintHospital.Simulation.Staff.StaffRole.Nurse);
        h.RunUntil(() => first.State == AmbulanceState.AtBay, TimeSpan.FromHours(2), "the queued ambulance to park");
        Assert.NotEqual(AmbulanceState.AtBay, second.State);
        Assert.Equal((1.0, 0.0), first.Heading);
        Assert.Equal(sim.AmbulanceParkingTiles[first.BaySlot!.Value], first.Tile);
    }

    private static (double X0, double Y0, double X1, double Y1) Footprint(AmbulanceVehicle a)
    {
        var (hx, hy) = a.Heading;
        var along = Math.Abs(hx) > 0.5;
        var (w, l) = along ? (Length, Width) : (Width, Length);
        return (a.X - (w / 2), a.Y - (l / 2), a.X + (w / 2), a.Y + (l / 2));
    }

    private static bool Overlap((double X0, double Y0, double X1, double Y1) a, (double X0, double Y0, double X1, double Y1) b) =>
        a.X0 < b.X1 && b.X0 < a.X1 && a.Y0 < b.Y1 && b.Y0 < a.Y1;
}
