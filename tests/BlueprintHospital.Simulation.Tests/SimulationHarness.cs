using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Builds a full hospital simulation on the real game data for integration tests.</summary>
internal sealed class SimulationHarness
{
    private static GameDatabase? _database;

    public SimulationHarness(int seed = 1234, bool arrivals = false, DateTime? start = null, double arrivalMultiplier = 1.0, StartingHospital startWith = StartingHospital.SmallEmergency, bool waitingRoom = true, GameType type = GameType.Sandbox)
    {
        Database = _database ??= GameDatabase.Load(new FileSystemDataSource(TestData.GameDataDirectory()));
        Events = new EventBus();
        Clock = new SimulationClock(Events, start ?? new DateTime(2027, 1, 4, 10, 0, 0));
        Sim = new HospitalSimulation(Database, Clock, Events, StarterSite.Create(), new SimulationOptions
        {
            Seed = seed,
            AutomaticArrivals = arrivals,
            ArrivalRateMultiplier = arrivalMultiplier,
            Start = startWith,
            Type = type,
        });
        Clock.Ticked += _ => Sim.Tick();

        // A new game has no waiting room; most tests want A&E's usual one, fitted out with desk, doors and seats.
        if (waitingRoom && startWith == StartingHospital.SmallEmergency && type == GameType.Sandbox)
        {
            Sim.BuildStarterWaitingRoom();
        }
    }

    public GameDatabase Database { get; }

    public EventBus Events { get; }

    public SimulationClock Clock { get; }

    public HospitalSimulation Sim { get; }

    public void RunFor(TimeSpan time)
    {
        var ticks = (long)(time.TotalSeconds / SimulationClock.TickLength.TotalSeconds);
        for (var i = 0; i < ticks; i++)
        {
            Clock.Step();
        }
    }

    /// <summary>Runs until the condition holds, failing the test if it doesn't within <paramref name="limit"/>.</summary>
    public void RunUntil(Func<bool> condition, TimeSpan limit, string what)
    {
        var end = Clock.Now + limit;
        while (!condition())
        {
            if (Clock.Now >= end)
            {
                var journeys = string.Join(Environment.NewLine + Environment.NewLine, Sim.Patients.Select(Journey));
                throw new Xunit.Sdk.XunitException($"Timed out after {limit} waiting for: {what}{Environment.NewLine}{journeys}");
            }

            Clock.Step();
        }
    }

    /// <summary>Hires everyone the recommended staffing says is missing, the way a player following it would.</summary>
    public void StaffToRecommended()
    {
        foreach (var need in Sim.RecommendedStaffing())
        {
            for (var i = 0; i < need.Short; i++)
            {
                Sim.HireStaff(need.Role, need.RoomId, skill: 0.7, shift: need.Shift);
            }
        }
    }

    public string Journey(Patient p) => string.Join(Environment.NewLine, p.Timeline.Select(t => $"{t.Time:HH:mm:ss} {t.Text}"));
}
