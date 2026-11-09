using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.Transport;
using Xunit;
using Xunit.Abstractions;

namespace BlueprintHospital.Simulation.Tests;

public class CorridorCareTests(ITestOutputHelper output)
{
    [Fact]
    public void The_starter_corridor_has_room_for_trolleys_clear_of_doors()
    {
        var h = new SimulationHarness();
        var spots = h.Sim.CorridorCareSpots();
        output.WriteLine(string.Join(" ", spots));
        Assert.True(spots.Count >= 4, $"Only {spots.Count} corridor spaces.");
        Assert.All(spots, s => Assert.Contains(h.Sim.Spaces, c => c.Kind == CareSpaceKind.Corridor && c.Rect.Contains(s)));
        Assert.Equal(spots.Count, spots.Distinct().Count());
    }

    [Fact]
    public void With_every_cubicle_full_ambulance_patients_are_handed_over_into_corridor_care()
    {
        var h = new SimulationHarness(seed: 21);
        h.StaffToRecommended();

        // Fill every cubicle.
        foreach (var space in h.Sim.Spaces.Where(s => s.IsTreatmentSpace))
        {
            space.OccupantId = -1;
        }

        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        h.RunUntil(() => patient.Stage == PatientStage.WaitingForSpace && patient.Triage.HasValue, TimeSpan.FromHours(2), "corridor obs and triage");
        output.WriteLine(h.Journey(patient));

        Assert.NotNull(patient.CorridorSpot);
        Assert.Equal(patient.CorridorSpot, patient.Tile);
        Assert.Null(patient.CareSpaceId);
        Assert.Null(AmbulanceOf(h, patient));
        Assert.Contains("corridor care", h.Journey(patient));
        Assert.Contains("Observations done in the corridor", h.Journey(patient));
        Assert.True(h.Sim.Stats.AverageHandoverMinutes > 0);

        // They wait there: observations but no doctor and no treatment.
        h.RunFor(TimeSpan.FromHours(2));
        Assert.Equal(PatientStage.WaitingForSpace, patient.Stage);
        Assert.Equal(patient.CorridorSpot, patient.Tile);
        Assert.Null(patient.FirstSeenByDoctorAt);
        Assert.False(patient.EffectiveTreatmentGiven);
        Assert.Equal(1, h.Sim.Stats.InCorridor);

        // A cubicle frees up: they go into it and leave the corridor.
        var majors = h.Sim.Spaces.First(s => s.Kind == CareSpaceKind.Majors);
        majors.OccupantId = null;
        h.RunUntil(() => patient.Stage == PatientStage.InTreatmentSpace, TimeSpan.FromHours(1), "a cubicle");
        Assert.Null(patient.CorridorSpot);
        Assert.Equal(1, h.Sim.Stats.CorridorCareTotal);
    }

    [Fact]
    public void A_free_cubicle_means_no_corridor_care()
    {
        var h = new SimulationHarness(seed: 22);
        h.StaffToRecommended();
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        h.RunUntil(() => patient.Stage == PatientStage.InTreatmentSpace, TimeSpan.FromHours(2), "a cubicle");
        Assert.Null(patient.CorridorSpot);
        Assert.DoesNotContain("corridor", h.Journey(patient));
    }

    [Fact]
    public void Ambulance_waits_for_handover_however_long_it_takes()
    {
        var h = new SimulationHarness(seed: 23);
        foreach (var nurse in h.Sim.Staff.Where(s => s.Role == StaffRole.Nurse).ToList())
        {
            h.Sim.Fire(nurse.Id);
        }

        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        h.RunFor(TimeSpan.FromHours(6));
        Assert.True(patient.Stage == PatientStage.InAmbulanceQueue || !patient.IsAlive);
        Assert.Null(patient.CorridorSpot);
        if (patient.IsAlive)
        {
            Assert.NotNull(AmbulanceOf(h, patient));
        }
    }

    [Fact]
    public void Corridor_care_survives_save_and_load()
    {
        var h = new SimulationHarness(seed: 24);
        h.StaffToRecommended();
        foreach (var space in h.Sim.Spaces.Where(s => s.IsTreatmentSpace))
        {
            space.OccupantId = -1;
        }

        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        h.RunUntil(() => patient.Stage == PatientStage.WaitingForSpace && patient.CorridorSpot.HasValue, TimeSpan.FromHours(2), "corridor care");
        var state = h.Sim.CapturePeople();
        var restored = state.Patients.Single(p => p.Id == patient.Id);
        Assert.Equal(patient.CorridorSpot, restored.CorridorSpot);
        Assert.Equal(patient.CorridorSince, restored.CorridorSince);
    }

    private static AmbulanceVehicle? AmbulanceOf(SimulationHarness h, Patient p) =>
        h.Sim.Ambulances.FirstOrDefault(a => a.PatientId == p.Id && a.State is not (AmbulanceState.Departing or AmbulanceState.Gone));
}
