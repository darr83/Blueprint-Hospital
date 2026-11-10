using BlueprintHospital.Simulation.Core;

namespace BlueprintHospital.Simulation.Tests;

public class SimulationClockTests
{
    [Fact]
    public void Starts_at_default_date_running_at_normal_speed()
    {
        var clock = new SimulationClock();
        Assert.Equal(SimulationClock.DefaultStart, clock.Now);
        Assert.Equal(SimulationSpeed.Normal, clock.Speed);
        Assert.Equal(DayOfWeek.Monday, clock.Now.DayOfWeek);
    }

    [Fact]
    public void Two_real_seconds_at_1x_are_fifteen_game_seconds()
    {
        var clock = new SimulationClock();
        var ticks = clock.Advance(2.0);
        Assert.Equal(15, ticks);
        Assert.Equal(SimulationClock.DefaultStart.AddSeconds(15), clock.Now);
    }

    [Theory]
    [InlineData(SimulationSpeed.Normal, 15)]
    [InlineData(SimulationSpeed.Fast, 30)]
    [InlineData(SimulationSpeed.VeryFast, 60)]
    [InlineData(SimulationSpeed.Fastest, 120)]
    public void Speed_multiplies_ticks(SimulationSpeed speed, int expectedTicks)
    {
        var clock = new SimulationClock();
        clock.SetSpeed(speed);
        Assert.Equal(expectedTicks, clock.Advance(2.0));
    }

    [Fact]
    public void Paused_clock_does_not_move()
    {
        var clock = new SimulationClock();
        clock.SetSpeed(SimulationSpeed.Paused);
        Assert.Equal(0, clock.Advance(10));
        Assert.Equal(SimulationClock.DefaultStart, clock.Now);
    }

    [Fact]
    public void Fractional_frames_accumulate_into_whole_ticks()
    {
        var clock = new SimulationClock();
        var total = 0;
        for (var i = 0; i < 120; i++)
        {
            total += clock.Advance(1.0 / 60.0);
        }

        Assert.Equal(15, total);
    }

    [Fact]
    public void Huge_frame_is_capped()
    {
        var clock = new SimulationClock();
        Assert.Equal(SimulationClock.MaxTicksPerAdvance, clock.Advance(100_000));
        Assert.Equal(0, clock.Advance(0));
    }

    [Fact]
    public void Publishes_tick_day_and_speed_events()
    {
        var bus = new EventBus();
        var clock = new SimulationClock(bus, new DateTime(2027, 1, 4, 23, 59, 59));
        var ticks = 0;
        var days = new List<DateTime>();
        var speeds = new List<SimulationSpeedChanged>();
        bus.Subscribe<SimulationTicked>(_ => ticks++);
        bus.Subscribe<NewDayStarted>(e => days.Add(e.Date));
        bus.Subscribe<SimulationSpeedChanged>(speeds.Add);

        clock.Step();
        clock.SetSpeed(SimulationSpeed.Fast);
        clock.SetSpeed(SimulationSpeed.Fast);

        Assert.Equal(1, ticks);
        Assert.Equal(new[] { new DateTime(2027, 1, 5) }, days);
        Assert.Single(speeds);
        Assert.Equal(SimulationSpeed.Fast, speeds[0].Current);
    }

    [Fact]
    public void State_round_trips()
    {
        var clock = new SimulationClock();
        clock.Advance(5);
        clock.SetSpeed(SimulationSpeed.VeryFast);
        var state = clock.CaptureState();

        var restored = new SimulationClock();
        restored.RestoreState(state);

        Assert.Equal(clock.Now, restored.Now);
        Assert.Equal(clock.TickCount, restored.TickCount);
        Assert.Equal(SimulationSpeed.VeryFast, restored.Speed);
    }

    [Fact]
    public void Rejects_negative_time()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationClock().Advance(-1));
    }

    [Fact]
    public void Crossing_a_20_metre_corridor_takes_several_real_seconds_at_1x()
    {
        // 40 tiles is 20 m; people should be visible walking it, not dart across in a second.
        var gameSeconds = 40 / BlueprintHospital.Simulation.Agents.Agent.DefaultWalkSpeed;
        var realSeconds = gameSeconds / SimulationClock.GameTimePerRealSecond.TotalSeconds;
        Assert.InRange(realSeconds, 5, 10);
        Assert.InRange(gameSeconds / 60, 0.5, 1.5);
    }
}
