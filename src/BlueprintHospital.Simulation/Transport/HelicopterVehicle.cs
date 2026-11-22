using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Transport;

/// <summary>Where the air ambulance is in its visit.</summary>
public enum HelicopterState
{
    /// <summary>Flying in towards the helipad at cruising height.</summary>
    Inbound,

    /// <summary>Hovering down onto the pad.</summary>
    Landing,

    /// <summary>On the pad, rotors winding down, while the crew take the patient in and hand over.</summary>
    Landed,

    /// <summary>Rotors spinning up and lifting off.</summary>
    TakingOff,

    /// <summary>Flying away off the site.</summary>
    Outbound,

    Gone,
}

/// <summary>
/// The air ambulance: flies in over the site, lands on the helipad, waits while its crew wheel the patient in
/// to the ambulance entrance and hand over, then lifts off once the crew are back on board.
/// Its tile position is the middle of the pad when landed; <see cref="Altitude"/> says how high it is.
/// </summary>
public sealed class HelicopterVehicle : Agent
{
    /// <summary>Flying speed over the site (sped up with everything else by the walking pace).</summary>
    public const double FlySpeedTilesPerSecond = WalkPaceFactor * 30.0 / Units.MetresPerTile;

    /// <summary>Cruising height over the site, in metres.</summary>
    public const double CruiseMetres = 40;

    /// <summary>How long the hover down onto the pad (and the climb away) takes, in game seconds.</summary>
    public const double HoverSeconds = 40;

    /// <summary>How long the rotors take to wind down or spin up, in game seconds.</summary>
    public const double SpinSeconds = 30;

    /// <summary>Ids of helicopters start here, clear of everyone else's.</summary>
    public const int IdOffset = 7_000_000;

    public HelicopterVehicle()
    {
        WalkSpeed = FlySpeedTilesPerSecond;
    }

    public required string CallSign { get; init; }

    /// <summary>The patient on board (or brought in by its crew).</summary>
    public required int PatientId { get; init; }

    public HelicopterState State { get; set; } = HelicopterState.Inbound;

    /// <summary>How high it is, in metres above the ground.</summary>
    public double Altitude { get; set; } = CruiseMetres;

    /// <summary>How fast the rotors turn: 1 in flight, 0 when stopped on the pad.</summary>
    public double Rotor { get; set; } = 1;

    /// <summary>When it touched down.</summary>
    public DateTime? LandedAt { get; set; }

    /// <summary>True once the crew have handed the patient over and are back on board (or given up waiting).</summary>
    public bool CrewAboard { get; set; }

    /// <summary>Where it came in from (it leaves back the same way).</summary>
    public GridCoord From { get; init; }
}
