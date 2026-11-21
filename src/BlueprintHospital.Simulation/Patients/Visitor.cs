using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Patients;

/// <summary>Where a visitor is in their visit.</summary>
public enum VisitorActivity
{
    /// <summary>Walking in from the street or the car park to the reception desk.</summary>
    Arriving,

    /// <summary>At the desk, asking the way to the ward.</summary>
    CheckingIn,

    /// <summary>Walking (or taking the lift) to the bedside.</summary>
    GoingToBed,

    /// <summary>Sitting (or standing) at the bedside.</summary>
    Visiting,

    /// <summary>Gone for a cup of tea or to the toilet; back in a bit.</summary>
    Away,

    /// <summary>Walking back out of the hospital.</summary>
    Leaving,
}

/// <summary>
/// A relative or friend visiting a patient on a ward (or a baby on the neonatal unit). They come in at the
/// front, check in at reception, walk (or take the lift) to the ward, sit on the chair by the bed for a while,
/// perhaps pop to the café or the toilet, and go home. Visitors are not saved: after a load the day's visits
/// are planned again.
/// </summary>
public sealed class Visitor : Agent
{
    /// <summary>Visitors' ids start here, so they never clash with patients', staff, parents' or outpatients' ids.</summary>
    public const int IdOffset = 6_000_000;

    /// <summary>The patient they are visiting (for a baby on the neonatal unit, the baby's mother).</summary>
    public required int PatientId { get; init; }

    /// <summary>The baby they are visiting on the neonatal unit, if that is who they came to see.</summary>
    public int? BabyId { get; init; }

    public required string FirstName { get; init; }

    public required string Surname { get; init; }

    public string FullName => $"{FirstName} {Surname}";

    public required Sex Sex { get; init; }

    public required int Age { get; init; }

    /// <summary>Who they are to the patient: "daughter", "husband", "friend", "grandad"...</summary>
    public required string Relation { get; init; }

    /// <summary>A partner on the maternity ward, who may come and go at any time of day.</summary>
    public bool Partner { get; init; }

    /// <summary>Bringing flowers (or a card and grapes): carried in, put down by the bed.</summary>
    public bool Flowers { get; init; }

    public VisitorActivity Activity { get; set; } = VisitorActivity.Arriving;

    /// <summary>What they are doing, for the pointer card: "Visiting Mary Jones".</summary>
    public string StatusText { get; set; } = "Coming to visit";

    /// <summary>The ward (or neonatal unit) they are visiting.</summary>
    public required string WardId { get; init; }

    /// <summary>Where they sit or stand at the bedside, once chosen.</summary>
    public GridCoord? Spot { get; set; }

    /// <summary>When they started visiting at the bedside.</summary>
    public DateTime? VisitStartedAt { get; set; }

    /// <summary>When they mean to go home.</summary>
    public DateTime LeaveAt { get; set; }

    /// <summary>When the current stage (checking in) ends.</summary>
    internal DateTime? Until { get; set; }

    /// <summary>When, part-way through the visit, they get up for a cup of tea or the toilet (if at all).</summary>
    internal DateTime? BreakAt { get; set; }

    /// <summary>Where they last set off for, so they don't keep starting the same walk.</summary>
    internal GridCoord? Heading { get; set; }
}
