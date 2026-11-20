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
}

/// <summary>
/// A mum or dad who comes in with a child (under 16), stays beside them through A&amp;E and on the ward,
/// sleeps on the fold-out bed beside their bed at night, and goes home with them.
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

    /// <summary>"Mum" or "Dad".</summary>
    public string Relation => Sex == Sex.Female ? "Mum" : "Dad";

    public ParentActivity Activity { get; set; } = ParentActivity.WithChild;

    /// <summary>The fold-out bed they use on the ward, while they are sitting or sleeping on it.</summary>
    public Hospital.PlacedFurniture? FoldOutBed { get; set; }

    /// <summary>Where they last set off for, so they don't keep starting the same walk.</summary>
    internal World.GridCoord? Heading { get; set; }
}
