using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>
/// The minors room: an open room of minors beds, each behind its own curtains. The bigger it is
/// dragged, the more beds it holds, and every bed works like a minors cubicle of its own.
/// </summary>
public class MinorsRoomTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A&amp;E with its two minors cubicles swapped for a minors room in a new wing.</summary>
    private static (SimulationHarness H, CareSpace Room) HospitalWithMinorsRoom(int seed, int width = 16, int length = 11)
    {
        var h = new SimulationHarness(seed, arrivals: true, arrivalMultiplier: 2.0);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        var check = h.Sim.BuildRoom(h.Sim.AutoPlan("minors_room", new GridRect(B.X - 30, B.Y + 17, width, length)));
        Assert.True(check.CanBuild, string.Join("; ", check.Problems));
        foreach (var cubicle in h.Sim.Spaces.Where(s => s.Kind == CareSpaceKind.Minors && !s.HasBeds).ToList())
        {
            Assert.True(h.Sim.DemolishRoom(cubicle.Id).CanBuild);
        }

        h.StaffToRecommended();
        return (h, h.Sim.Spaces.Single(s => s.TemplateId == "minors_room"));
    }

    [Fact]
    public void Dragging_the_room_bigger_gives_it_more_beds_with_curtains_between_them()
    {
        var h = new SimulationHarness();
        CareSpace Room(int w, int l) => h.Sim.CheckRoomAt("minors_room", new GridRect(B.X + 2, B.Y + 2, w, l)).Preview!;
        int Beds(int w, int l) => Room(w, l).Beds.Count;

        Assert.Equal(2, Beds(10, 10));
        Assert.Equal(3, Beds(16, 11));
        Assert.Equal(5, Beds(26, 11));
        Assert.True(Beds(26, 16) > Beds(26, 11), "a deeper room should get a second row of beds");

        var room = Room(26, 11);
        Assert.Equal(CareSpaceKind.Minors, room.Kind);
        Assert.True(room.IsMultiBed && room.IsTreatmentSpace && !room.IsWard);
        Assert.Equal(room.Beds.Count - 1, room.Furniture.Count(f => f.ItemId == RoomLayoutEngine.BayCurtain));
        Assert.Equal(room.Beds.Count, room.Furniture.Count(f => f.ItemId == "obs_monitor"));
        Assert.Equal(room.Beds.Count, room.Places);
    }

    [Fact]
    public void The_minors_room_is_in_the_AE_build_menu_and_open_from_career_level_one()
    {
        var h = new SimulationHarness();
        Assert.True(h.Database.RoomTemplates.ContainsKey("minors_room"));
        Assert.True(h.Database.RoomTemplates.ContainsKey("minors_cubicle"), "the single minors cubicle stays");
        var level1 = h.Database.Levels.OrderBy(l => l.Number).First();
        Assert.Contains("minors_room", level1.Rooms);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(17)]
    public void Each_bed_takes_its_own_minors_patient_and_nobody_shares_a_bed(int seed)
    {
        var (h, room) = HospitalWithMinorsRoom(seed);
        var spots = room.Beds.Select(b => b.PatientSpot).ToHashSet();
        var mostAtOnce = 0;
        var treatedThere = new HashSet<int>();
        var end = h.Clock.Now + TimeSpan.FromHours(8);
        while (h.Clock.Now < end)
        {
            h.Clock.Step();
            var lying = h.Sim.Patients.Where(p => !p.IsMoving && p.Stage != PatientStage.Gone && spots.Contains(p.Tile)).ToList();
            foreach (var group in lying.GroupBy(p => p.Tile))
            {
                Assert.True(group.Count() <= 1, $"{string.Join(" and ", group.Select(p => p.FullName))} share the bed at {group.Key}");
            }

            // Every booked bed belongs to the patient who is booked into it.
            for (var i = 0; i < room.Beds.Count; i++)
            {
                if (room.Beds[i].OccupantId is { } id && h.Sim.FindPatient(id) is { } p)
                {
                    Assert.Equal(room.Id, p.CareSpaceId);
                    Assert.Equal(i, p.BedIndex);
                }
            }

            var inBeds = h.Sim.Patients.Count(p => p.CareSpaceId == room.Id && h.Sim.BedOf(p) is { } bed && p.Tile == bed.PatientSpot && !p.IsMoving);
            mostAtOnce = Math.Max(mostAtOnce, inBeds);
            foreach (var p in h.Sim.Patients.Where(p => p.CareSpaceId == room.Id && p.FirstSeenByDoctorAt is not null))
            {
                treatedThere.Add(p.Id);
            }
        }

        Assert.True(mostAtOnce >= 2, $"at most {mostAtOnce} patient(s) were in the minors room at once");
        Assert.True(treatedThere.Count >= 4, $"only {treatedThere.Count} patients were seen by a doctor in the minors room");
        Assert.Contains(h.Sim.DepartedPatients, p => treatedThere.Contains(p.Id));
        Assert.Equal(room.Beds.Count, h.Sim.Stats.Minors.Total);
    }

    [Fact]
    public void Minors_room_beds_survive_save_and_load()
    {
        var (h, room) = HospitalWithMinorsRoom(seed: 9);
        h.RunUntil(() => room.Beds.Count(b => b.OccupantId is not null) >= 2, TimeSpan.FromHours(8), "two minors room beds in use");

        var json = SaveSerializer.Serialize(new SaveGame { Clock = h.Clock.CaptureState(), Layout = h.Sim.CaptureLayout(), People = h.Sim.CapturePeople() });
        var save = SaveSerializer.Deserialize(json);
        var to = new SimulationHarness(seed: 99, arrivals: true);
        to.Clock.RestoreState(save.Clock);
        to.Sim.RestoreLayout(save.Layout!);
        to.Sim.RestorePeople(save.People!);

        var loaded = to.Sim.Spaces.Single(s => s.TemplateId == "minors_room");
        Assert.Equal(room.Beds.Select(b => b.OccupantId), loaded.Beds.Select(b => b.OccupantId));
        foreach (var bed in loaded.Beds.Where(b => b.OccupantId.HasValue))
        {
            Assert.Same(bed, to.Sim.BedOf(to.Sim.FindPatient(bed.OccupantId!.Value)!));
        }

        to.RunFor(TimeSpan.FromHours(2));
    }
}
