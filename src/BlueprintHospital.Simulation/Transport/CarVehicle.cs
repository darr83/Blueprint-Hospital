using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Transport;

/// <summary>How a walk-in patient is brought to the front of the hospital.</summary>
public enum CarTrip
{
    /// <summary>Drives themselves: the car stays in the car park until they go home.</summary>
    Parks,

    /// <summary>Dropped at the taxi rank by a friend or relative, who drives off again.</summary>
    DropOff,

    /// <summary>Arrives by taxi.</summary>
    Taxi,
}

public enum CarState
{
    /// <summary>Driving along the road and pulling in.</summary>
    Arriving,

    /// <summary>Stopped: in a car park space, or at the taxi rank letting its passenger out.</summary>
    Parked,

    /// <summary>Backing out of a car park space.</summary>
    BackingOut,

    /// <summary>Driving away along the road.</summary>
    Leaving,

    Gone,
}

/// <summary>
/// A visitor's car or a taxi bringing a walk-in patient. The patient is inside (and not yet in the
/// hospital) until it stops; a parked car then waits for them and leaves when they have gone.
/// </summary>
public sealed class CarVehicle : Agent
{
    /// <summary>Cars keep to a gentle site speed (about 10 mph), scaled like walking.</summary>
    public const double DriveSpeedTilesPerSecond = Agent.WalkPaceFactor * 4.5 / Units.MetresPerTile;

    /// <summary>Length of a car, in tiles (4.3 m).</summary>
    public const double LengthTiles = 8.6;

    public CarVehicle()
    {
        WalkSpeed = DriveSpeedTilesPerSecond;
    }

    public required CarTrip Trip { get; init; }

    /// <summary>The patient this car brought (still inside it while <see cref="Passenger"/> is set).</summary>
    public required int PatientId { get; init; }

    /// <summary>The patient on board, until the car stops and they get out.</summary>
    public Patient? Passenger { get; set; }

    public CarState State { get; set; } = CarState.Arriving;

    /// <summary>Which car park space (for <see cref="CarTrip.Parks"/>) or taxi stand it is heading for or stopped in.</summary>
    public int Slot { get; set; }

    public DateTime? StoppedAt { get; set; }

    /// <summary>True while backing out of a space: it moves tail first.</summary>
    public bool Reversing { get; set; }

    /// <summary>Which way the front of the car points.</summary>
    public (double X, double Y) Heading => Reversing ? (-Facing.X, -Facing.Y) : Facing;

    public bool IsTaxi => Trip == CarTrip.Taxi;
}
