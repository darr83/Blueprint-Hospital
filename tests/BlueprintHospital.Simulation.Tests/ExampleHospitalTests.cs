using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>The ready-made example hospital that ships in Data/Examples and is offered in Load Game.</summary>
public class ExampleHospitalTests
{
    private static GameDatabase Database => GameDatabase.Load(new FileSystemDataSource(TestData.GameDataDirectory()));

    private static SaveGame Shipped() =>
        SaveSerializer.Deserialize(File.ReadAllText(Path.Combine(TestData.GameDataDirectory(), "Examples", ExampleHospital.FileName)));

    /// <summary>Loads a save the way the game does.</summary>
    private static (HospitalSimulation Sim, SimulationClock Clock) Load(GameDatabase database, SaveGame save)
    {
        var events = new EventBus();
        var clock = new SimulationClock(events);
        clock.RestoreState(save.Clock);
        var sim = new HospitalSimulation(database, clock, events, StarterSite.Create(save.SitePlan ?? SitePlan.Standard), new SimulationOptions { Seed = 7 });
        sim.RestoreLayout(save.Layout!);
        sim.RestorePeople(save.People!);
        clock.Ticked += _ => sim.Tick();
        return (sim, clock);
    }

    [Fact]
    public void The_shipped_example_loads_with_three_floors_every_room_and_every_post_filled()
    {
        var database = Database;
        var (sim, clock) = Load(database, Shipped());

        Assert.Equal(ExampleHospital.HospitalName, Shipped().HospitalName);
        Assert.Equal(3, sim.FloorCount);
        Assert.Empty(database.RoomTemplates.Keys.Where(t => sim.Spaces.All(r => r.TemplateId != t)));
        Assert.Empty(database.Departments.Keys.Where(d => !sim.IsUnlocked(d)));
        Assert.True(sim.IsEmergencyOpen, string.Join(", ", sim.ClosedReasons));
        Assert.Empty(sim.RecommendedStaffing().Where(n => n.Short > 0).Select(n => $"{n.Place} {n.Role} {n.Shift}"));
        Assert.True(sim.Balance > 0);

        // Every floor has rooms, and both lifts and the stairs reach all three.
        Assert.All(Enumerable.Range(0, 3), f => Assert.Contains(sim.Spaces, r => r.Kind != CareSpaceKind.Corridor && sim.FloorOf(r) == f));
        Assert.Equal(3, sim.Shafts.Count());
        Assert.All(sim.Shafts, s => Assert.Equal(3, s.Count));

        // Theatres have surgeons and anaesthetists, and take the surgical specialties.
        Assert.Contains(sim.Staff, s => s.Role == StaffRole.Anaesthetist);
        Assert.All(sim.Spaces.Where(s => s.Kind == CareSpaceKind.Theatre && sim.FixedTheatreDepartments(s).Count == 0),
            t => Assert.NotEmpty(t.AllowedDepartments));

        // It plays: an hour and a half of game time with patients coming in.
        var before = sim.Stats.ArrivalsTotal;
        for (var i = 0; i < 5400; i++)
        {
            clock.Step();
        }

        Assert.True(sim.Stats.ArrivalsTotal > before, "No patients arrived.");
    }

    [Fact]
    public void The_shipped_example_matches_the_plan_in_code()
    {
        // If room templates change so the plan builds differently, rebuild the file:
        //   dotnet run --project tools/SimulationRunner -- --example-hospital
        var database = Database;
        var events = new EventBus();
        var clock = new SimulationClock(events);
        var built = ExampleHospital.Build(database, clock, events, StarterSite.Create(SitePlan.Standard)).CaptureLayout();
        var shipped = Shipped().Layout!;

        static string Describe(RoomState r) => $"{r.Template} {r.X},{r.Y} {r.Width}x{r.Length} {r.DoorSide} {r.Name}";
        Assert.Equal(built.Floors, shipped.Floors);
        Assert.Equal(built.Rooms.Select(Describe).Order(), shipped.Rooms.Select(Describe).Order());
    }
}
