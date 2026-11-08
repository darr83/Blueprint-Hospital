using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Patients who can walk, walk; the rest are pushed in a wheelchair or on a trolley.</summary>
public class TransportTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    private static SimulationHarness WithXray(int seed)
    {
        var h = new SimulationHarness(seed);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 10, 34, 6), WallSide.South)).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("xray_room", new GridRect(B.X - 30, B.Y + 1, 9, 9))).CanBuild);
        h.StaffToRecommended();
        return h;
    }

    [Fact]
    public void The_starting_team_includes_porters()
    {
        var h = new SimulationHarness();
        Assert.Equal(2, h.Sim.Staff.Count(s => s.Role == StaffRole.Porter && s.Shift == StaffShift.Day));
        Assert.Equal(2, h.Sim.Staff.Count(s => s.Role == StaffRole.Porter && s.Shift == StaffShift.Night));
    }

    [Fact]
    public void A_walk_in_with_a_sprain_walks_everywhere()
    {
        var h = new SimulationHarness(seed: 2);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "ankle_sprain");
        var pushed = false;
        h.RunUntil(() =>
        {
            pushed |= h.Sim.Tasks.Any(t => t.PatientId == patient.Id && t.Kind == TaskKind.MovePatient);
            return patient.Stage == PatientStage.Gone;
        }, TimeSpan.FromHours(6), "sprain to go home");

        Assert.False(pushed, h.Journey(patient));
        Assert.Equal(Mobility.Walking, patient.Mobility);
    }

    [Fact]
    public void Ambulance_patients_come_in_on_the_crews_trolley()
    {
        var h = new SimulationHarness(seed: 4);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_myocardial_infarction");
        h.RunUntil(() => patient.Stage == PatientStage.InTreatmentSpace, TimeSpan.FromHours(2), "the patient to get a space");

        Assert.Equal(Mobility.Trolley, patient.Mobility);
        Assert.True(patient.IsMoving || patient.Tile == h.Sim.Spaces.Single(s => s.Id == patient.CareSpaceId).PatientSpot);
        Assert.DoesNotContain(h.Sim.Tasks, t => t.PatientId == patient.Id && t.Kind == TaskKind.MovePatient);
    }

    [Fact]
    public void A_porter_pushes_a_poorly_patient_to_xray_and_walks_behind_them()
    {
        var h = WithXray(seed: 6);
        var room = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.XRay);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");

        var maxGap = 0.0;
        StaffMember? porter = null;
        h.RunUntil(() =>
        {
            if (patient.PushedByStaffId is { } id && patient.IsMoving)
            {
                porter = h.Sim.FindStaff(id);
                maxGap = Math.Max(maxGap, Math.Sqrt(Math.Pow(porter!.X - patient.X, 2) + Math.Pow(porter.Y - patient.Y, 2)));
            }

            return patient.Investigations.Any(o => o.InvestigationId == "chest_xray" && o.Status != InvestigationStatus.Requested) || patient.Stage == PatientStage.Gone;
        }, TimeSpan.FromHours(8), "the chest X-ray");

        var journey = h.Journey(patient);
        Assert.NotNull(porter);
        Assert.Equal(StaffRole.Porter, porter!.Role);
        Assert.Contains("(porter)", journey);
        Assert.InRange(maxGap, 0.5, 3.5);
        Assert.Contains("Chest X-ray done by", journey);
        Assert.NotEqual(Mobility.Walking, patient.Mobility);
    }

    [Fact]
    public void Without_porters_a_nurse_does_the_pushing()
    {
        var h = WithXray(seed: 5);
        foreach (var porter in h.Sim.Staff.Where(s => s.Role == StaffRole.Porter).ToList())
        {
            Assert.True(h.Sim.Fire(porter.Id));
        }

        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        StaffMember? pusher = null;
        h.RunUntil(() =>
        {
            pusher ??= patient.PushedByStaffId is { } id ? h.Sim.FindStaff(id) : null;
            return pusher is not null || patient.Stage == PatientStage.Gone;
        }, TimeSpan.FromHours(8), "someone to push the patient");

        Assert.Equal(StaffRole.Nurse, pusher?.Role);
    }
}
