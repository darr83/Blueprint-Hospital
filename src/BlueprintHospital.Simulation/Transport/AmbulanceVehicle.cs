using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Transport;

public enum AmbulancePurpose
{
    /// <summary>Bringing a patient in.</summary>
    Arrival,

    /// <summary>Collecting a patient for transfer to another hospital.</summary>
    Transfer,
}

public enum AmbulanceState
{
    Driving,
    WaitingForBay,
    AtBay,
    Departing,
    Gone,
}

/// <summary>An ambulance on the hospital site. Drives to a bay, waits for handover or collection, then leaves.</summary>
public sealed class AmbulanceVehicle : Agent
{
    public const double DriveSpeedTilesPerSecond = Agent.WalkPaceFactor * 6.0 / Units.MetresPerTile;

    public AmbulanceVehicle()
    {
        WalkSpeed = DriveSpeedTilesPerSecond;
    }

    public required string CallSign { get; init; }

    public required AmbulancePurpose Purpose { get; init; }

    public required int PatientId { get; init; }

    public AmbulanceState State { get; set; } = AmbulanceState.Driving;

    public int? BaySlot { get; set; }

    /// <summary>
    /// True while backing into its bay and while parked there: the vehicle moves (or last moved)
    /// tail first, so its nose points the opposite way to <see cref="Agent.Facing"/>.
    /// </summary>
    public bool Reversing { get; set; }

    /// <summary>Which way the front of the ambulance points.</summary>
    public (double X, double Y) Heading => Reversing ? (-Facing.X, -Facing.Y) : Facing;

    /// <summary>Length of the vehicle, in tiles (5.8 m).</summary>
    public const double LengthTiles = 11.6;

    public DateTime? ArrivedAtBayAt { get; set; }

    /// <summary>For arrivals: when the patient was handed over and the crew could leave.</summary>
    public DateTime? HandoverCompletedAt { get; set; }
}
