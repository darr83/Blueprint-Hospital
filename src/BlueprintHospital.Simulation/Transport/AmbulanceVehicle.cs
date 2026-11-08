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
    public const double DriveSpeedTilesPerSecond = 6.0 / Units.MetresPerTile;

    public AmbulanceVehicle()
    {
        WalkSpeed = DriveSpeedTilesPerSecond;
    }

    public required string CallSign { get; init; }

    public required AmbulancePurpose Purpose { get; init; }

    public required int PatientId { get; init; }

    public AmbulanceState State { get; set; } = AmbulanceState.Driving;

    public int? BaySlot { get; set; }

    public DateTime? ArrivedAtBayAt { get; set; }

    /// <summary>For arrivals: when the patient was handed over and the crew could leave.</summary>
    public DateTime? HandoverCompletedAt { get; set; }
}
