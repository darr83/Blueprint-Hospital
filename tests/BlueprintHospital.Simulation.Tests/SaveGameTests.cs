using BlueprintHospital.Simulation.Core;

namespace BlueprintHospital.Simulation.Tests;

public class SaveGameTests
{
    [Fact]
    public void Save_round_trips()
    {
        var clock = new SimulationClock();
        clock.Advance(30);
        clock.SetSpeed(SimulationSpeed.Fast);
        var save = new SaveGame
        {
            HospitalName = "St Elsewhere",
            SavedAtUtc = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc),
            Clock = clock.CaptureState(),
            Camera = new CameraState(120.5, -40, 1.5),
        };

        var loaded = SaveSerializer.Deserialize(SaveSerializer.Serialize(save));

        Assert.Equal("St Elsewhere", loaded.HospitalName);
        Assert.Equal(save.Clock, loaded.Clock);
        Assert.Equal(save.Camera, loaded.Camera);
    }

    [Fact]
    public void Damaged_save_gives_clear_error()
    {
        var ex = Assert.Throws<InvalidDataException>(() => SaveSerializer.Deserialize("{ not json"));
        Assert.Contains("damaged", ex.Message);
    }

    [Fact]
    public void Newer_save_format_is_refused()
    {
        var json = SaveSerializer.Serialize(new SaveGame { FormatVersion = SaveGame.CurrentFormatVersion + 1 });
        Assert.Throws<InvalidDataException>(() => SaveSerializer.Deserialize(json));
    }

    [Theory]
    [InlineData("My Hospital", "my_hospital")]
    [InlineData("  ../../etc  ", "etc")]
    [InlineData("!!!", "save")]
    public void Slot_names_are_safe(string input, string expected)
    {
        Assert.Equal(expected, SaveSerializer.ToSlotName(input));
    }
}

public class FullSaveTests
{
    private static readonly World.GridRect B = World.StarterSite.MainBuildingFootprint;

    private static SimulationHarness BusyHospitalWithWard(int seed)
    {
        var h = new SimulationHarness(seed: seed, arrivals: true, start: new DateTime(2027, 1, 4, 6, 0, 0), arrivalMultiplier: 1.5);
        Assert.True(h.Sim.ExtendBuilding(new World.GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new Simulation.RoomPlan("corridor", new World.GridRect(B.X - 30, B.Y + 9, 34, 8), World.WallSide.South)).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_ward", new World.GridRect(B.X - 30, B.Y + 17, 26, 15))).CanBuild);
        h.StaffToRecommended();
        h.RunFor(TimeSpan.FromHours(10));
        return h;
    }

    private static SimulationHarness Load(SimulationHarness from)
    {
        var json = SaveSerializer.Serialize(new SaveGame
        {
            Clock = from.Clock.CaptureState(),
            Layout = from.Sim.CaptureLayout(),
            People = from.Sim.CapturePeople(),
        });
        var save = SaveSerializer.Deserialize(json);
        var to = new SimulationHarness(seed: 99, arrivals: true);
        to.Clock.RestoreState(save.Clock);
        to.Sim.RestoreLayout(save.Layout!);
        to.Sim.RestorePeople(save.People!);
        return to;
    }

    [Fact]
    public void Patients_staff_tasks_and_money_survive_save_and_load_exactly()
    {
        var h = BusyHospitalWithWard(seed: 8);
        Assert.NotEmpty(h.Sim.Patients);
        var loaded = Load(h);

        string Snapshot(SimulationHarness x) => SaveSerializer.Serialize(new SaveGame { People = x.Sim.CapturePeople(), Layout = x.Sim.CaptureLayout() });
        Assert.Equal(Snapshot(h), Snapshot(loaded));
        Assert.Equal(h.Sim.Balance, loaded.Sim.Balance);
        Assert.Equal(h.Sim.Stats.ArrivalsTotal, loaded.Sim.Stats.ArrivalsTotal);
        Assert.Equal(h.Sim.Stats.FourHourPerformance, loaded.Sim.Stats.FourHourPerformance);
    }

    [Fact]
    public void A_loaded_hospital_carries_on_running()
    {
        var h = BusyHospitalWithWard(seed: 3);
        var loaded = Load(h);
        var before = loaded.Sim.Patients.Select(p => p.Id).ToHashSet();

        loaded.RunFor(TimeSpan.FromHours(6));

        // The patients who were in hospital at the save have moved on, and rooms still add up.
        Assert.Contains(loaded.Sim.DepartedPatients, p => before.Contains(p.Id));
        foreach (var space in loaded.Sim.Spaces.Where(s => s.OccupantId.HasValue))
        {
            Assert.Equal(space.Id, loaded.Sim.FindPatient(space.OccupantId!.Value)?.CareSpaceId);
        }

        foreach (var bed in loaded.Sim.Wards.SelectMany(w => w.Beds).Where(b => b.OccupantId.HasValue))
        {
            Assert.True(loaded.Sim.FindPatient(bed.OccupantId!.Value)?.IsInDepartment);
        }
    }
}
