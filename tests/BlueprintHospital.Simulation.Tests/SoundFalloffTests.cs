using BlueprintHospital.Simulation.Audio;

namespace BlueprintHospital.Simulation.Tests;

public class SoundFalloffTests
{
    [Fact]
    public void Monitor_beeps_get_louder_as_you_zoom_in()
    {
        var far = SoundFalloff.ZoomGain(0.3, 0.4, 1.2);
        var middle = SoundFalloff.ZoomGain(0.8, 0.4, 1.2);
        var close = SoundFalloff.ZoomGain(1.5, 0.4, 1.2);

        Assert.Equal(0, far);
        Assert.InRange(middle, 0.1, 0.9);
        Assert.Equal(1, close);
    }

    [Fact]
    public void A_floor_keeps_red_alarms_faintly_audible_when_zoomed_out()
    {
        Assert.Equal(0.3, SoundFalloff.ZoomGain(0.1, 0.4, 1.2, floor: 0.3), 6);
        Assert.Equal(1, SoundFalloff.ZoomGain(2, 0.4, 1.2, floor: 0.3));
    }

    [Fact]
    public void Sounds_fade_out_away_from_the_middle_of_the_screen()
    {
        Assert.Equal(1, SoundFalloff.DistanceGain(100, 200, 800));
        Assert.Equal(0, SoundFalloff.DistanceGain(900, 200, 800));
        var half = SoundFalloff.DistanceGain(500, 200, 800);
        Assert.True(half is > 0 and < 1);
        Assert.True(SoundFalloff.DistanceGain(400, 200, 800) > half);
    }

    [Fact]
    public void Decibels_are_in_range()
    {
        Assert.Equal(0, SoundFalloff.ToDecibels(1), 6);
        Assert.Equal(-80, SoundFalloff.ToDecibels(0));
    }
}
