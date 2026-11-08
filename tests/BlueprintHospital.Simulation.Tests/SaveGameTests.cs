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
