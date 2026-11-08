namespace BlueprintHospital.Simulation.Core;

/// <summary>Simulation speed settings offered to the player.</summary>
public enum SimulationSpeed
{
    Paused = 0,
    Normal = 1,
    Fast = 2,
    VeryFast = 4,
}

/// <summary>
/// Fixed-timestep game clock. Real time is accumulated and converted into whole
/// simulation ticks, so medical progression is identical at every speed and frame rate.
/// </summary>
public sealed class SimulationClock
{
    /// <summary>Game time that passes in one simulation tick.</summary>
    public static readonly TimeSpan TickLength = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Game time that passes per real second at 1x speed. At 1x an hour takes four real minutes;
    /// at 4x a whole day takes about 24 minutes.
    /// </summary>
    public static readonly TimeSpan GameTimePerRealSecond = TimeSpan.FromSeconds(15);

    /// <summary>Upper bound on ticks processed in one frame so a long hitch cannot freeze the game.</summary>
    public const int MaxTicksPerAdvance = 240;

    /// <summary>The default start of a new game: a Monday morning.</summary>
    public static readonly DateTime DefaultStart = new(2027, 1, 4, 8, 0, 0, DateTimeKind.Unspecified);

    private readonly EventBus? _events;
    private double _accumulatedGameSeconds;

    public SimulationClock(EventBus? events = null, DateTime? start = null)
    {
        _events = events;
        Now = start ?? DefaultStart;
    }

    public DateTime Now { get; private set; }

    public long TickCount { get; private set; }

    public SimulationSpeed Speed { get; private set; } = SimulationSpeed.Normal;

    public bool IsPaused => Speed == SimulationSpeed.Paused;

    /// <summary>How far (0..1) real time has moved towards the next tick. Used to smooth animation between ticks.</summary>
    public double TickProgress => Math.Clamp(_accumulatedGameSeconds / TickLength.TotalSeconds, 0, 1);

    /// <summary>Raised once per simulation tick, after the clock has moved forward.</summary>
    public event Action<SimulationClock>? Ticked;

    public void SetSpeed(SimulationSpeed speed)
    {
        if (!Enum.IsDefined(speed))
        {
            throw new ArgumentOutOfRangeException(nameof(speed), speed, "Unknown simulation speed.");
        }

        if (speed == Speed)
        {
            return;
        }

        var previous = Speed;
        Speed = speed;
        _events?.Publish(new SimulationSpeedChanged(previous, speed));
    }

    /// <summary>
    /// Advances the clock by an amount of real (wall-clock) time.
    /// Returns the number of simulation ticks that were run.
    /// </summary>
    public int Advance(double realSeconds)
    {
        if (realSeconds < 0 || double.IsNaN(realSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(realSeconds), realSeconds, "Real time cannot go backwards.");
        }

        if (IsPaused)
        {
            return 0;
        }

        _accumulatedGameSeconds += realSeconds * GameTimePerRealSecond.TotalSeconds * (int)Speed;

        var tickSeconds = TickLength.TotalSeconds;
        var ticksRun = 0;
        while (_accumulatedGameSeconds >= tickSeconds && ticksRun < MaxTicksPerAdvance)
        {
            _accumulatedGameSeconds -= tickSeconds;
            Step();
            ticksRun++;
        }

        if (ticksRun == MaxTicksPerAdvance)
        {
            // Drop any remaining backlog rather than spiralling.
            _accumulatedGameSeconds = 0;
        }

        return ticksRun;
    }

    /// <summary>Runs exactly one tick regardless of speed. Used by tests and step debugging.</summary>
    public void Step()
    {
        var previousDay = Now.Date;
        Now = Now.Add(TickLength);
        TickCount++;

        Ticked?.Invoke(this);
        _events?.Publish(new SimulationTicked(Now, TickCount));

        if (Now.Date != previousDay)
        {
            _events?.Publish(new NewDayStarted(Now.Date));
        }
    }

    public ClockState CaptureState() => new(Now, TickCount, Speed);

    public void RestoreState(ClockState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Now = state.Now;
        TickCount = state.TickCount;
        _accumulatedGameSeconds = 0;
        SetSpeed(state.Speed);
    }
}

/// <summary>Serialisable snapshot of the clock.</summary>
public sealed record ClockState(DateTime Now, long TickCount, SimulationSpeed Speed);
