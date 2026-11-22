using BlueprintHospital.Simulation.Agents;

namespace BlueprintHospital.Simulation.Patients;

/// <summary>What a parent with a child in hospital is doing.</summary>
public enum ParentActivity
{
    /// <summary>Walking with their child, or standing (or sitting) beside them.</summary>
    WithChild,

    /// <summary>Waiting outside while their child has a scan or an operation.</summary>
    WaitingOutside,

    /// <summary>Sat on the fold-out bed beside their child's ward bed.</summary>
    SittingByBed,

    /// <summary>Asleep on the fold-out bed beside their child's ward bed, at night.</summary>
    Sleeping,

    /// <summary>In the parents' room: a cup of tea and a sit down, or waiting there while their child has a scan or an operation.</summary>
    InParentsRoom,
}

/// <summary>Who is staying with a patient.</summary>
public enum EscortKind
{
    /// <summary>A child's mum or dad.</summary>
    Family,

    /// <summary>A police officer who brought them in.</summary>
    Police,

    /// <summary>A firefighter who brought them out of a fire or a crash and came in with them.</summary>
    FireCrew,

    /// <summary>The air ambulance paramedic, who wheels them in from the helipad and hands over.</summary>
    AirCrew,
}

/// <summary>
/// A mum or dad who comes in with a child (under 16), stays beside them through A&amp;E and on the ward,
/// sleeps on the fold-out bed beside their bed at night, and goes home with them. Also a police officer
/// who brings someone in (<see cref="Police"/>): they stay beside them until they leave A&amp;E. Also a
/// firefighter or an air ambulance paramedic who comes in with a major incident casualty: they stay until
/// the handover, then walk back out (see <see cref="Escort"/>).
/// </summary>
public sealed class Parent : Agent
{
    /// <summary>Parents' ids are their child's id plus this, so they never clash with patients' or staff ids.</summary>
    public const int IdOffset = 5_000_000;

    public required int ChildId { get; init; }

    public required string FirstName { get; init; }

    public required string Surname { get; init; }

    public string FullName => $"{FirstName} {Surname}";

    public required Sex Sex { get; init; }

    public required int Age { get; init; }

    /// <summary>
    /// A police officer who brought the patient in and stays with them in A&amp;E, instead of a parent
    /// (their first name is then "PC").
    /// </summary>
    public bool Police => Escort == EscortKind.Police;

    /// <summary>Who they are to the patient.</summary>
    public EscortKind Escort { get; init; } = EscortKind.Family;

    /// <summary>A firefighter or air ambulance paramedic: they go once the patient has been handed over.</summary>
    public bool Crew => Escort is EscortKind.FireCrew or EscortKind.AirCrew;

    /// <summary>True once the patient has been handed over and they are walking back out.</summary>
    public bool Leaving { get; set; }

    /// <summary>"Mum", "Dad", "The police officer", "The firefighter" or "The air ambulance paramedic".</summary>
    public string Relation => Escort switch
    {
        EscortKind.Police => "The police officer",
        EscortKind.FireCrew => "The firefighter",
        EscortKind.AirCrew => "The air ambulance paramedic",
        _ => Sex == Sex.Female ? "Mum" : "Dad",
    };

    public ParentActivity Activity { get; set; } = ParentActivity.WithChild;

    /// <summary>The fold-out bed they use on the ward, while they are sitting or sleeping on it.</summary>
    public Hospital.PlacedFurniture? FoldOutBed { get; set; }

    /// <summary>When their break in the parents' room ends, while they are on one.</summary>
    internal DateTime? BreakUntil { get; set; }

    /// <summary>When they next feel like a break from the bedside.</summary>
    internal DateTime? NextBreakAt { get; set; }

    /// <summary>Where they last set off for, so they don't keep starting the same walk.</summary>
    internal World.GridCoord? Heading { get; set; }
}
