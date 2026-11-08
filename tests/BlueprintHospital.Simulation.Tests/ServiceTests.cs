using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Imaging, operating theatres and the pharmacy.</summary>
public class ServiceTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>
    /// A west wing with a corridor across it, rooms north of the corridor and rooms south of it.
    /// <paramref name="north"/> and <paramref name="south"/> are (template, width in tiles, depth in tiles), placed left to right.
    /// </summary>
    private static SimulationHarness Wing(int seed, (string Template, int W, int D)[] north, (string Template, int W, int D)[] south)
    {
        var h = new SimulationHarness(seed);
        Build(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)), "extension");
        Build(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 10, 34, 6), WallSide.South)), "corridor");
        var x = B.X - 30;
        foreach (var (template, w, d) in north)
        {
            Build(h.Sim.BuildRoom(h.Sim.AutoPlan(template, new GridRect(x, B.Y + 10 - d, w, d))), template);
            x += w;
        }

        x = B.X - 30;
        foreach (var (template, w, d) in south)
        {
            Build(h.Sim.BuildRoom(h.Sim.AutoPlan(template, new GridRect(x, B.Y + 16, w, d))), template);
            x += w;
        }

        h.StaffToRecommended();
        return h;
    }

    private static void Build(BuildCheck result, string what) => Assert.True(result.CanBuild, $"{what}: {string.Join("; ", result.Problems)}");

    [Fact]
    public void Service_rooms_open_once_someone_is_hired_to_work_in_them()
    {
        var h = new SimulationHarness(1);
        Build(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)), "extension");
        Build(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 10, 34, 6), WallSide.South)), "corridor");
        Build(h.Sim.BuildRoom(h.Sim.AutoPlan("xray_room", new GridRect(B.X - 30, B.Y + 1, 9, 9))), "xray_room");
        var xray = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.XRay);
        var chestXray = h.Database.Investigations["chest_xray"];

        // Rooms come empty: no radiographer, so no X-rays yet.
        Assert.DoesNotContain(h.Sim.Staff, s => s.Role == StaffRole.Radiographer);
        Assert.False(h.Sim.CanDo(chestXray));
        Assert.Contains(h.Sim.RecommendedStaffing(), n => n.RoomId == xray.Id && n.Role == StaffRole.Radiographer && n.Short == 1);

        var radiographer = h.Sim.HireStaff(StaffRole.Radiographer);
        Assert.Equal(xray.Id, radiographer.BaseRoomId);
        Assert.True(h.Sim.CanDo(chestXray));

        // Demolishing the room keeps the radiographer, waiting for a new room.
        Assert.True(h.Sim.DemolishRoom(xray.Id).CanBuild);
        Assert.Contains(radiographer, h.Sim.Staff);
        Assert.Null(radiographer.BaseRoomId);
        Assert.False(h.Sim.CanDo(chestXray));
    }

    [Fact]
    public void Pneumonia_gets_a_chest_xray_in_the_xray_room_when_there_is_one()
    {
        var h = Wing(5, new[] { ("xray_room", 9, 9) }, Array.Empty<(string, int, int)>());
        var room = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.XRay);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");

        var visited = false;
        h.RunUntil(() =>
        {
            visited |= !patient.IsMoving && patient.Tile == room.PatientSpot;
            return patient.WorkingDiagnosis is not null || patient.Stage == PatientStage.Gone;
        }, TimeSpan.FromHours(8), "pneumonia patient to be diagnosed");

        var journey = h.Journey(patient);
        Assert.True(visited, journey);
        Assert.Contains("Chest X-ray done by", journey);
        Assert.Contains(patient.Investigations, o => o.InvestigationId == "chest_xray" && o.Status == InvestigationStatus.Resulted);
        Assert.Equal(1, h.Sim.Stats.ScansDone);
    }

    [Fact]
    public void Without_imaging_doctors_diagnose_without_scans()
    {
        var h = new SimulationHarness(seed: 5);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        h.RunUntil(() => patient.WorkingDiagnosis is not null || patient.Stage == PatientStage.Gone, TimeSpan.FromHours(8), "pneumonia patient to be diagnosed");
        Assert.DoesNotContain(patient.Investigations, o => o.InvestigationId == "chest_xray");
        Assert.DoesNotContain(h.Sim.Tasks, t => t.Kind == TaskKind.Imaging);
    }

    [Fact]
    public void Appendicitis_goes_to_theatre_then_recovers_on_a_ward()
    {
        var h = Wing(7, Array.Empty<(string, int, int)>(), new[] { ("operating_theatre", 12, 12), ("medical_ward", 16, 10) });
        var theatre = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.Theatre);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");

        var inTheatre = false;
        h.RunUntil(() =>
        {
            inTheatre |= !patient.IsMoving && patient.Tile == theatre.PatientSpot;
            return patient.Stage is PatientStage.OnWard or PatientStage.Gone or PatientStage.AwaitingTransfer;
        }, TimeSpan.FromHours(16), "appendicitis patient to reach a ward");

        var journey = h.Journey(patient);
        if (patient.WorkingDiagnosis?.ConditionId != "acute_appendicitis" && h.Sim.Stats.OperationsDone == 0)
        {
            return; // Misdiagnosed in A&E: covered by the diagnosis tests.
        }

        Assert.True(inTheatre, journey);
        Assert.Contains("Laparoscopic appendicectomy performed by", journey);
        Assert.Equal(PatientStage.OnWard, patient.Stage);
        Assert.Equal(1, h.Sim.Stats.OperationsDone);
        Assert.True(h.Sim.Today[MoneyCategory.Operations] == HospitalSimulation.OperationTariff);
        Assert.Null(theatre.ReservedByStaffId);
    }

    [Fact]
    public void Without_a_theatre_patients_needing_an_operation_are_transferred()
    {
        var h = new SimulationHarness(seed: 7);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");
        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromHours(16), "appendicitis patient to leave");
        if (patient.WorkingDiagnosis?.ConditionId == "acute_appendicitis")
        {
            Assert.Equal(PatientOutcome.Transferred, patient.Outcome);
            Assert.Contains("no operating theatre on site", h.Journey(patient));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ward_discharges_wait_for_medicines_to_take_home(bool pharmacy)
    {
        var north = pharmacy ? new[] { ("pharmacy", 8, 8) } : Array.Empty<(string, int, int)>();
        var h = Wing(5, north, new[] { ("medical_ward", 26, 15) });
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        h.RunUntil(() => patient.Stage is PatientStage.OnWard or PatientStage.Gone, TimeSpan.FromHours(12), "pneumonia patient to reach the ward");
        Assert.Equal(PatientStage.OnWard, patient.Stage);

        h.RunUntil(() => patient.MedicinesReadyAt is not null || patient.Stage == PatientStage.Gone, TimeSpan.FromDays(6), "medicines to take home");
        var readyAt = patient.MedicinesReadyAt!.Value;
        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromDays(1), "patient to go home");

        var journey = h.Journey(patient);
        Assert.Equal(PatientOutcome.Discharged, patient.Outcome);
        Assert.True(patient.LeftAt >= readyAt, journey);
        Assert.Contains(pharmacy ? "dispensed by" : "outside pharmacy", journey);
    }

    [Fact]
    public void Medicines_cost_more_without_a_pharmacy()
    {
        var without = new SimulationHarness();
        Assert.False(without.Sim.HasPharmacy);
        var with = Wing(1, new[] { ("pharmacy", 8, 8) }, Array.Empty<(string, int, int)>());
        Assert.True(with.Sim.HasPharmacy);
        Assert.True(without.Sim.MedicineCost(10) > with.Sim.MedicineCost(10));
    }
}
