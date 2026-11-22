using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Transport;
using BlueprintHospital.Simulation.World;
using Xunit;

namespace BlueprintHospital.Simulation.Tests;

public class HelipadTests
{
    /// <summary>Open grass north of the starting building.</summary>
    private static readonly GridCoord North = new(50, 1);

    [Fact]
    public void A_helipad_goes_on_open_ground_away_from_the_building()
    {
        var h = new SimulationHarness(seed: 41);
        var sim = h.Sim;
        var b = StarterSite.MainBuildingFootprint;
        Assert.Contains(sim.CheckHelipad(new GridCoord(b.X + 5, b.Y + 5)).Problems, p => p.Contains("building", StringComparison.Ordinal));
        Assert.Contains(sim.CheckHelipad(new GridCoord(b.X + 5, b.Y - HospitalSimulation.HelipadSize - 1)).Problems, p => p.StartsWith("Too close", StringComparison.Ordinal));
        Assert.Contains(sim.CheckHelipad(new GridCoord(-3, 1)).Problems, p => p.StartsWith("Must be inside", StringComparison.Ordinal));

        var balance = sim.Balance;
        var built = sim.BuildHelipad(North);
        Assert.True(built.CanBuild, string.Join(" ", built.Problems));
        Assert.Equal(balance - HospitalSimulation.HelipadCost, sim.Balance);
        Assert.Equal(HospitalSimulation.HelipadAt(North), sim.Helipad);
        Assert.Equal(TileKind.Helipad, sim.Site[new GridCoord(North.X + 3, North.Y + 3)]);
        Assert.Equal(1, sim.BuiltCount(Data.BuildMenu.Helipad));

        // Only one, and nothing can be built over it.
        Assert.False(sim.CheckHelipad(new GridCoord(10, 40)).CanBuild);
        Assert.Contains(sim.CheckExtension(new GridRect(b.X, North.Y + 2, 20, b.Y - North.Y - 2)).Problems, p => p.Contains("helipad", StringComparison.Ordinal));

        // Taking it out gives half back.
        var removed = sim.RemoveHelipad();
        Assert.True(removed.CanBuild);
        Assert.Null(sim.Helipad);
        Assert.Equal(balance - (HospitalSimulation.HelipadCost / 2), sim.Balance);
    }

    [Fact]
    public void The_air_ambulance_lands_on_the_helipad_and_its_paramedic_hands_over_at_the_ambulance_entrance()
    {
        var h = new SimulationHarness(seed: 42);
        h.StaffToRecommended();
        Assert.True(h.Sim.BuildHelipad(North).CanBuild);
        var incident = h.Sim.StartIncident("road_collision")!;
        var air = incident.Casualties.Single(c => c.Transport == CasualtyTransport.Air);

        h.RunUntil(() => air.PatientId is not null, TimeSpan.FromHours(1), "the air casualty");
        var patient = h.Sim.FindPatient(air.PatientId!.Value)!;
        Assert.True(patient.AirAmbulance);
        var helicopter = h.Sim.Helicopters.Single(x => x.PatientId == patient.Id);
        Assert.Equal(HelicopterState.Inbound, helicopter.State);
        Assert.True(h.Sim.InHelicopter(patient));

        h.RunUntil(() => helicopter.State == HelicopterState.Landed, TimeSpan.FromMinutes(10), "the helicopter to land");
        Assert.Equal(HospitalSimulation.HelipadCentre(h.Sim.Helipad!.Value), helicopter.Tile);
        Assert.Equal(0, helicopter.Altitude);
        Assert.False(h.Sim.InHelicopter(patient));
        h.RunFor(TimeSpan.FromSeconds(5));
        var paramedic = h.Sim.Parents.Single(p => p.ChildId == patient.Id);
        Assert.Equal(EscortKind.AirCrew, paramedic.Escort);
        Assert.Equal(Mobility.Trolley, patient.Mobility);

        h.RunUntil(() => patient.TriagedAt is not null, TimeSpan.FromMinutes(40), "the handover");
        Assert.Contains(patient.Timeline, e => e.Text.StartsWith("At the ambulance entrance with the air ambulance crew", StringComparison.Ordinal));
        Assert.Contains(patient.Timeline, e => e.Text.StartsWith("Ambulance handover to", StringComparison.Ordinal));

        h.RunUntil(() => !h.Sim.Helicopters.Contains(helicopter), TimeSpan.FromHours(1), "the helicopter to leave");
        Assert.DoesNotContain(h.Sim.Parents, p => p.ChildId == patient.Id && p.Escort == EscortKind.AirCrew);
        Assert.Contains(patient.Timeline, e => e.Text.EndsWith("handed over and left.", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_helipad_the_air_casualty_comes_later_by_road_with_the_paramedic()
    {
        var h = new SimulationHarness(seed: 43);
        h.StaffToRecommended();
        var incident = h.Sim.StartIncident("road_collision")!;
        var air = incident.Casualties.Single(c => c.Transport == CasualtyTransport.Air);
        var planned = air.ArriveAt;
        h.RunUntil(() => air.ByRoad, TimeSpan.FromHours(1), "the helicopter to land nearby");
        Assert.True(air.ArriveAt >= planned + HospitalSimulation.NoHelipadDelay);
        Assert.Contains(h.Sim.Notifications.Recent, n => n.Text.StartsWith("The air ambulance has no helipad here", StringComparison.Ordinal));

        h.RunUntil(() => air.PatientId is not null, TimeSpan.FromMinutes(20), "the air casualty by road");
        var patient = h.Sim.FindPatient(air.PatientId!.Value)!;
        Assert.Empty(h.Sim.Helicopters);
        Assert.Equal(ArrivalMethod.Ambulance, patient.ArrivalMethod);
        Assert.True(patient.AirAmbulance);
        h.RunUntil(() => h.Sim.Parents.Any(p => p.ChildId == patient.Id), TimeSpan.FromMinutes(10), "the paramedic at the bay");
        Assert.Equal(EscortKind.AirCrew, h.Sim.Parents.Single(p => p.ChildId == patient.Id).Escort);
    }

    [Fact]
    public void A_firefighter_comes_in_with_a_house_fire_casualty_and_leaves_after_the_handover()
    {
        var h = new SimulationHarness(seed: 44);
        h.StaffToRecommended();
        var incident = h.Sim.StartIncident("house_fire")!;
        var rescued = incident.Casualties.First(c => c.FireCrew);
        h.RunUntil(() => rescued.PatientId is not null && h.Sim.Parents.Any(p => p.ChildId == rescued.PatientId), TimeSpan.FromHours(1), "the firefighter");
        var patient = h.Sim.FindPatient(rescued.PatientId!.Value)!;
        var firefighter = h.Sim.Parents.Single(p => p.ChildId == patient.Id);
        if (!HospitalSimulation.HasParentWith(patient))
        {
            Assert.Equal(EscortKind.FireCrew, firefighter.Escort);
            Assert.Equal("The firefighter", firefighter.Relation);
            h.RunUntil(() => !h.Sim.Parents.Contains(firefighter), TimeSpan.FromHours(2), "the firefighter to leave");
            Assert.Contains(patient.Timeline, e => e.Text.StartsWith("Brought out by the fire service", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_helipad_and_a_helicopter_on_it_survive_save_and_load()
    {
        var from = new SimulationHarness(seed: 45);
        from.StaffToRecommended();
        from.Sim.BuildHelipad(North);
        var incident = from.Sim.StartIncident("road_collision")!;
        var air = incident.Casualties.Single(c => c.Transport == CasualtyTransport.Air);
        from.RunUntil(() => from.Sim.Helicopters.Any(x => x.State == HelicopterState.Landed), TimeSpan.FromHours(1), "the helicopter to land");

        var json = SaveSerializer.Serialize(new SaveGame { Clock = from.Clock.CaptureState(), Layout = from.Sim.CaptureLayout(), People = from.Sim.CapturePeople() });
        var save = SaveSerializer.Deserialize(json);
        var to = new SimulationHarness(seed: 99);
        to.Clock.RestoreState(save.Clock);
        to.Sim.RestoreLayout(save.Layout!);
        to.Sim.RestorePeople(save.People!);
        Assert.Equal(from.Sim.Helipad, to.Sim.Helipad);
        Assert.Equal(TileKind.Helipad, to.Sim.Site[new GridCoord(North.X + 1, North.Y + 1)]);
        var helicopter = to.Sim.Helicopters.Single();
        Assert.Equal(HelicopterState.Landed, helicopter.State);
        Assert.Equal(air.PatientId, helicopter.PatientId);
        Assert.True(to.Sim.FindPatient(helicopter.PatientId)!.AirAmbulance);
    }
}
