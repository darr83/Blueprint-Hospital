using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>
/// A patient moved on to a ward, HDU or another cubicle keeps lying in their old bed until a porter
/// wheels them away, so nobody else is put in that bed on top of them.
/// </summary>
public class BedSharingTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(11)]
    public void Two_patients_never_lie_on_the_same_bed_even_when_porters_are_slow(int seed)
    {
        var h = new SimulationHarness(seed, arrivals: true, arrivalMultiplier: 2.5);
        var b = StarterSite.MainBuildingFootprint;
        Assert.True(h.Sim.ExtendBuilding(new GridRect(b.X - 30, b.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(b.X - 30, b.Y + 9, 34, 8), WallSide.South)).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_ward", new GridRect(b.X - 30, b.Y + 17, 26, 15))).CanBuild);
        h.StaffToRecommended();

        // One porter for a busy department: patients wait a long time in their old beds.
        foreach (var porter in h.Sim.Staff.Where(s => s.Role == StaffRole.Porter).Skip(1).ToList())
        {
            Assert.True(h.Sim.Fire(porter.Id));
        }

        var beds = h.Sim.Spaces.Where(s => s.IsTreatmentSpace).Select(s => s.PatientSpot)
            .Concat(h.Sim.Spaces.SelectMany(s => s.Beds).Select(b => b.PatientSpot))
            .ToHashSet();
        var sawSomeoneWaitInAnOldBed = false;
        var end = h.Clock.Now + TimeSpan.FromHours(10);
        while (h.Clock.Now < end)
        {
            h.Clock.Step();
            foreach (var spot in beds)
            {
                var lying = h.Sim.Patients.Where(p => p.Tile == spot && !p.IsMoving && p.Stage != PatientStage.Gone).ToList();
                Assert.True(lying.Count <= 1, $"{string.Join(" and ", lying.Select(p => p.FullName))} are both lying on the bed at {spot} at {h.Clock.Now:HH:mm}");
                sawSomeoneWaitInAnOldBed |= lying.Any(p => p.PendingMove is { } to && to != spot);
            }
        }

        Assert.True(sawSomeoneWaitInAnOldBed, "no patient waited for a porter in an old bed, so the test checked nothing");
    }

    [Fact]
    public void A_bed_someone_is_still_lying_on_is_not_vacant()
    {
        var h = new SimulationHarness();
        var cubicle = h.Sim.Spaces.First(s => s.IsTreatmentSpace && s.IsFree);
        Assert.True(h.Sim.IsVacant(cubicle));

        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn);
        patient.PlaceAt(cubicle.PatientSpot);
        Assert.True(cubicle.IsFree);
        Assert.False(h.Sim.IsVacant(cubicle));
    }
}
