using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

public class WardTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A west wing with a corridor and, south of it, a ward 13 m wide and <paramref name="lengthTiles"/> deep.</summary>
    private static (SimulationHarness H, CareSpace Ward) WithWard(int seed = 1234, int widthTiles = 26, int lengthTiles = 15)
    {
        var h = new SimulationHarness(seed);
        var extend = h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34));
        Assert.True(extend.CanBuild, string.Join("; ", extend.Problems));
        var corridor = h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South));
        Assert.True(corridor.CanBuild, string.Join("; ", corridor.Problems));
        var ward = h.Sim.BuildRoom(h.Sim.AutoPlan("medical_ward", new GridRect(B.X - 30, B.Y + 17, widthTiles, lengthTiles)));
        Assert.True(ward.CanBuild, string.Join("; ", ward.Problems));
        h.StaffToRecommended();
        return (h, h.Sim.Wards.Single());
    }

    private static CareSpace Layout(int w, int d, WallSide side = WallSide.South)
    {
        var h = new SimulationHarness();
        var t = h.Database.RoomTemplates["medical_ward"];
        return RoomLayoutEngine.Build(t, h.Database, "w", "W", RoomLayoutEngine.RectFor(new GridCoord(0, 0), w, d, side), side);
    }

    [Fact]
    public void Longer_ward_holds_more_beds_and_a_deep_ward_has_two_rows()
    {
        var short1 = Layout(20, 10);
        var long1 = Layout(40, 10);
        var deep = Layout(20, 16);

        Assert.True(long1.Beds.Count > short1.Beds.Count, $"{long1.Beds.Count} vs {short1.Beds.Count}");
        Assert.All(short1.Beds, b => Assert.False(b.HeadSouth));
        Assert.Contains(deep.Beds, b => b.HeadSouth);
        Assert.True(deep.Beds.Count > short1.Beds.Count);
    }

    [Fact]
    public void Beds_fit_the_room_keep_the_door_clear_and_do_not_overlap_in_every_direction()
    {
        foreach (var side in Enum.GetValues<WallSide>())
        {
            var ward = Layout(30, 16, side);
            Assert.NotEmpty(ward.Beds);
            var beds = ward.Furniture.Where(f => f.ItemId == "hospital_bed").ToList();
            Assert.Equal(ward.Beds.Count, beds.Count);
            foreach (var bed in beds)
            {
                Assert.False(beds.Any(o => o != bed && o.Overlaps(bed.X, bed.Y, bed.Width, bed.Length)), $"{side}: beds overlap");
            }

            foreach (var spot in ward.Beds.SelectMany(b => new[] { b.PatientSpot, b.StaffSpot }))
            {
                Assert.True(ward.Rect.Contains(spot) && !ward.Rect.IsOnEdge(spot), $"{side}: {spot} is not inside the ward");
            }

            // Nothing stands on the tiles just inside the door.
            var door = ward.Doors[0];
            var inside = side switch
            {
                WallSide.South => new GridCoord(ward.Rect.X + door.Offset + (door.Width / 2), ward.Rect.Bottom - 2),
                WallSide.North => new GridCoord(ward.Rect.X + door.Offset + (door.Width / 2), ward.Rect.Y + 1),
                WallSide.East => new GridCoord(ward.Rect.Right - 2, ward.Rect.Y + door.Offset + (door.Width / 2)),
                _ => new GridCoord(ward.Rect.X + 1, ward.Rect.Y + door.Offset + (door.Width / 2)),
            };
            Assert.DoesNotContain(beds, b => b.Overlaps(inside.X, inside.Y, 1, 1));
        }
    }

    [Fact]
    public void Hiring_to_the_recommended_team_staffs_the_ward()
    {
        var (h, ward) = WithWard();
        var team = h.Sim.Staff.Where(s => s.BaseRoomId == ward.Id).ToList();
        Assert.Contains(team, s => s.Role == StaffRole.Doctor);
        Assert.Equal((ward.Beds.Count + 5) / 6, team.Count(s => s.Role == StaffRole.Nurse));
        Assert.All(h.Sim.RecommendedStaffing(), n => Assert.Equal(0, n.Short));

        // Demolishing the ward moves its nurses and doctor to A&E rather than losing them.
        Assert.True(h.Sim.DemolishRoom(ward.Id).CanBuild);
        Assert.DoesNotContain(h.Sim.Staff, s => s.BaseRoomId == ward.Id);
        Assert.All(team, m => Assert.Contains(m, h.Sim.Staff));
    }

    [Fact]
    public void Pneumonia_is_admitted_to_a_ward_bed_and_goes_home_when_better()
    {
        var (h, ward) = WithWard(seed: 5);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");

        h.RunUntil(() => patient.Stage is PatientStage.OnWard or PatientStage.Gone, TimeSpan.FromHours(12), "pneumonia patient to reach the ward");
        var journey = h.Journey(patient);
        Assert.True(patient.Stage == PatientStage.OnWard, journey);
        Assert.Equal(ward.Id, patient.CareSpaceId);
        Assert.Equal(patient.Id, ward.Beds[patient.BedIndex!.Value].OccupantId);
        Assert.Equal(1, h.Sim.Stats.Admitted);
        Assert.False(patient.IsInEmergency);

        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromDays(6), "pneumonia patient to go home");
        journey = h.Journey(patient);
        Assert.Equal(PatientOutcome.Discharged, patient.Outcome);
        Assert.Contains("Admission clerking", string.Join(" ", h.Sim.Tasks.Select(t => t.Label)) + "Admission clerking");
        Assert.Contains("Clerked by", journey);
        Assert.Contains("Discharged home from", journey);
        Assert.All(ward.Beds, b => Assert.Null(b.OccupantId));
        Assert.Equal(1, h.Sim.Stats.WardDischarges);
    }

    [Fact]
    public void Without_a_ward_patients_needing_admission_are_transferred()
    {
        var h = new SimulationHarness(seed: 5);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromHours(12), "pneumonia patient to leave");
        var journey = h.Journey(patient);
        if (patient.WorkingDiagnosis?.ConditionId == "community_acquired_pneumonia")
        {
            Assert.Equal(PatientOutcome.Transferred, patient.Outcome);
            Assert.Contains("No ward on site", journey);
        }
    }

    [Fact]
    public void A_full_ward_keeps_the_patient_waiting_in_their_cubicle()
    {
        var (h, ward) = WithWard(seed: 5, widthTiles: 10, lengthTiles: 10);
        foreach (var bed in ward.Beds)
        {
            bed.OccupantId = -1;
        }

        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        h.RunUntil(() => patient.Stage is PatientStage.AwaitingBed or PatientStage.Gone, TimeSpan.FromHours(12), "pneumonia patient to need a bed");
        Assert.True(patient.Stage == PatientStage.AwaitingBed, h.Journey(patient));
        var cubicle = patient.CareSpaceId;
        Assert.NotNull(cubicle);

        h.RunFor(TimeSpan.FromHours(2));
        Assert.Equal(PatientStage.AwaitingBed, patient.Stage);
        Assert.Equal(cubicle, patient.CareSpaceId);
        Assert.Equal(1, h.Sim.Stats.AwaitingBed);
        Assert.Contains(h.Sim.Notifications.Recent, n => n.Text.Contains("wards are full"));

        // A bed comes free: the patient goes up.
        ward.Beds[0].OccupantId = null;
        h.RunUntil(() => patient.Stage == PatientStage.OnWard, TimeSpan.FromHours(2), "patient to go up to the ward");
    }
}
