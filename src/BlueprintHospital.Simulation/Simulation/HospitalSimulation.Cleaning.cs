using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>What a cleaner is doing on their round.</summary>
public enum CleanerStage
{
    /// <summary>Walking to the cleaning room to collect their mop, bucket and brush.</summary>
    FetchingKit,

    /// <summary>Walking (with their kit) to the next spill or spot on their round.</summary>
    Walking,

    /// <summary>Mopping or sweeping where they stand.</summary>
    Cleaning,

    /// <summary>Taking their kit back to the cleaning room before a break.</summary>
    ReturningKit,
}

/// <summary>A cleaner's round, for the renderer: where they are going, whether they have their kit and how they're cleaning.</summary>
public sealed class CleanerJob
{
    public CleanerStage Stage { get; internal set; }

    /// <summary>Where they stand to clean (or to collect their kit).</summary>
    public GridCoord Spot { get; internal set; }

    /// <summary>True while they are pushing their bucket and carrying their mop and brush.</summary>
    public bool HasKit { get; internal set; }

    /// <summary>Mopping (a spill, a toilet, a ward) rather than sweeping with the brush.</summary>
    public bool Mopping { get; internal set; }

    /// <summary>The spill they are on their way to (or mopping up), if any.</summary>
    public FloorAccident? Spill { get; internal set; }

    /// <summary>When they finish cleaning this spot.</summary>
    public DateTime Until { get; internal set; }

    /// <summary>Which place on the round they did last, so the round moves on round the hospital.</summary>
    internal int Round { get; set; }
}

/// <summary>
/// Cleaners. They collect their kit from a cleaning room, then mop up spills (toilet accidents and
/// dropped drinks) as soon as they appear, and otherwise work their way round the hospital: mopping
/// toilets and wards and sweeping corridors, waiting rooms and cafes. Without a cleaner a spill stays on
/// the floor until it dries, which takes hours. Before a break they put their kit back.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>A spill nobody mops up dries up on its own after this long.</summary>
    public static readonly TimeSpan SpillDriesAfter = TimeSpan.FromHours(3);

    /// <summary>How long a cleaner spends mopping up a spill.</summary>
    public static readonly TimeSpan MopSpillTime = TimeSpan.FromMinutes(6);

    /// <summary>How long a cleaner spends on each spot of their round.</summary>
    public static readonly TimeSpan CleanSpotTime = TimeSpan.FromMinutes(8);

    /// <summary>How likely someone is to drop their drink on the way back from the cafe or a vending machine.</summary>
    public const double DrinkSpillChance = 0.04;

    private readonly Dictionary<StaffMember, CleanerJob> _cleanerJobs = new();
    private GameRandom? _cleaningRandom;

    /// <summary>A separate random stream, so cleaning doesn't change anything else that happens.</summary>
    private GameRandom CleaningRandom => _cleaningRandom ??= new GameRandom(_options.Seed + 271_829);

    /// <summary>Every cleaning room, where cleaners keep their kit.</summary>
    public IEnumerable<CareSpace> CleaningRooms => SpacesOfKind(CareSpaceKind.CleaningRoom);

    /// <summary>A cleaner's round, while they are on one.</summary>
    public CleanerJob? CleanerJobOf(StaffMember member) => _cleanerJobs.GetValueOrDefault(member);

    /// <summary>The spill a cleaner is mopping up or on their way to, if any.</summary>
    public bool IsSpillClaimed(FloorAccident spill) => _cleanerJobs.Any(j => j.Value.Spill == spill && j.Key.IsOnDuty && _staff.Contains(j.Key));

    /// <summary>Someone heading back from the cafe or a vending machine now and then drops their drink.</summary>
    private void MaybeSpillDrink(GridCoord tile)
    {
        if (CleaningRandom.Chance(DrinkSpillChance) && !_accidents.Any(a => a.Tile == tile))
        {
            _accidents.Add(new FloorAccident(tile, Now + SpillDriesAfter, SpillKind.Drink));
        }
    }

    /// <summary>Drops a cleaner's round (going home, or let go), freeing any spill they were heading for.</summary>
    private void StopCleaning(StaffMember member) => _cleanerJobs.Remove(member);

    /// <summary>A cleaner's turn. Returns true when they have been dealt with.</summary>
    private bool UpdateCleaner(StaffMember member)
    {
        if (member.Role != StaffRole.Cleaner)
        {
            return false;
        }

        var cupboard = CleaningRooms.OrderBy(r => Distance(r.StaffSpot, member.Tile)).FirstOrDefault();
        _cleanerJobs.TryGetValue(member, out var job);

        if (cupboard is null && job is not { HasKit: true })
        {
            // Nowhere to get a mop from.
            _cleanerJobs.Remove(member);
            if (DueBreak(member) is { } due)
            {
                TakeBreak(member, due);
                return true;
            }

            member.Activity = StaffActivity.Idle;
            member.StatusText = "Needs a cleaning room for their kit";
            WalkTo(member, HomeTile(member));
            return true;
        }

        if (job is null)
        {
            if (WantsBreak(member) is { } wanted)
            {
                TakeBreak(member, wanted.Due);
                return true;
            }

            job = new CleanerJob { Stage = CleanerStage.FetchingKit, Spot = cupboard!.StaffSpot };
            _cleanerJobs[member] = job;
            member.Activity = StaffActivity.WalkingToTask;
            member.StatusText = "Collecting their mop and bucket";
            WalkTo(member, job.Spot);
            return true;
        }

        // A spill they were heading for has dried up (or someone else got there first).
        if (job.Spill is { } spill && !_accidents.Contains(spill))
        {
            job.Spill = null;
            NextCleaningSpot(member, job);
            return true;
        }

        switch (job.Stage)
        {
            case CleanerStage.FetchingKit:
                if (cupboard is not null)
                {
                    job.Spot = cupboard.StaffSpot;
                }

                if (member.IsMoving || member.Tile != job.Spot)
                {
                    WalkTo(member, job.Spot);
                    return true;
                }

                job.HasKit = true;
                NextCleaningSpot(member, job);
                return true;

            case CleanerStage.Walking:
                // A new spill takes priority over sweeping.
                if (job.Spill is null && ClosestFreeSpill(member) is { } fresh)
                {
                    GoToSpill(member, job, fresh);
                    return true;
                }

                if (member.IsMoving || member.Tile != job.Spot)
                {
                    if (!member.IsMoving)
                    {
                        WalkTo(member, job.Spot);
                        if (!member.IsMoving && member.Tile != job.Spot)
                        {
                            // Can't get there: try somewhere else.
                            NextCleaningSpot(member, job);
                        }
                    }

                    return true;
                }

                job.Stage = CleanerStage.Cleaning;
                job.Until = Now + (job.Spill is null ? CleanSpotTime : MopSpillTime);
                member.Activity = StaffActivity.Working;
                member.StatusText = job.Spill is not null ? "Mopping up a spill" : job.Mopping ? $"Mopping {PlaceName(job.Spot)}" : $"Sweeping {PlaceName(job.Spot)}";
                return true;

            case CleanerStage.Cleaning:
                if (Now < job.Until)
                {
                    return true;
                }

                if (job.Spill is { } done)
                {
                    _accidents.Remove(done);
                    job.Spill = null;
                }

                member.TasksCompleted++;
                NextCleaningSpot(member, job);
                return true;

            case CleanerStage.ReturningKit:
                if (cupboard is not null)
                {
                    job.Spot = cupboard.StaffSpot;
                }

                if (member.IsMoving || member.Tile != job.Spot)
                {
                    WalkTo(member, job.Spot);
                    return true;
                }

                _cleanerJobs.Remove(member);
                TakeBreak(member, WantsBreak(member)?.Due);
                return true;
        }

        return true;
    }

    /// <summary>A due break they can go on now, or an extra one because they are worn out.</summary>
    private (PlannedBreak? Due, bool Tired)? WantsBreak(StaffMember member)
    {
        if (DueBreak(member) is { } due && CanTakeBreak(member, due, null))
        {
            return (due, false);
        }

        return member.Fatigue >= BreakThreshold ? (null, true) : null;
    }

    /// <summary>Picks where the cleaner goes next: a spill if there is one, else the next place on their round, or back with the kit for a break.</summary>
    private void NextCleaningSpot(StaffMember member, CleanerJob job)
    {
        job.Spill = null;
        if (WantsBreak(member) is not null && CleaningRooms.Any())
        {
            job.Stage = CleanerStage.ReturningKit;
            job.Spot = CleaningRooms.OrderBy(r => Distance(r.StaffSpot, member.Tile)).First().StaffSpot;
            member.Activity = StaffActivity.WalkingToTask;
            member.StatusText = "Putting their kit away";
            WalkTo(member, job.Spot);
            return;
        }

        if (ClosestFreeSpill(member) is { } spill)
        {
            GoToSpill(member, job, spill);
            return;
        }

        var places = _spaces.Values
            .Where(s => s.Kind is not (CareSpaceKind.CleaningRoom or CareSpaceKind.Lift or CareSpaceKind.Stairs))
            .OrderBy(s => s.Id, StringComparer.Ordinal)
            .ToList();
        for (var attempt = 0; attempt < places.Count; attempt++)
        {
            job.Round = (job.Round + 1) % Math.Max(1, places.Count);
            var place = places[(job.Round + (member.Id * 7)) % places.Count];
            if (FreeFloorIn(place) is { } spot)
            {
                job.Stage = CleanerStage.Walking;
                job.Spot = spot;
                job.Mopping = place.IsToilet || place.IsWard || place.Kind is CareSpaceKind.Theatre or CareSpaceKind.Resus or CareSpaceKind.DeliveryRoom;
                member.Activity = StaffActivity.WalkingToTask;
                member.StatusText = $"Going to clean {place.Name}";
                WalkTo(member, spot);
                return;
            }
        }

        // Nothing to clean: wait in the cleaning room.
        job.Stage = CleanerStage.Walking;
        job.Spot = HomeTile(member);
        job.Mopping = false;
        WalkTo(member, job.Spot);
    }

    private void GoToSpill(StaffMember member, CleanerJob job, FloorAccident spill)
    {
        job.Spill = spill;
        job.Stage = CleanerStage.Walking;
        job.Spot = spill.Tile;
        job.Mopping = true;
        member.Activity = StaffActivity.WalkingToTask;
        member.StatusText = "Going to mop up a spill";
        WalkTo(member, spill.Tile);
    }

    private FloorAccident? ClosestFreeSpill(StaffMember member) =>
        _accidents.Where(a => !IsSpillClaimed(a) && Nav.IsWalkable(a.Tile))
            .OrderBy(a => Distance(a.Tile, member.Tile))
            .FirstOrDefault();

    /// <summary>A walkable bit of floor in a room, clear of furniture, seats and beds, to sweep or mop.</summary>
    private GridCoord? FreeFloorIn(CareSpace room)
    {
        var r = room.Rect;
        for (var i = 0; i < 16; i++)
        {
            var tile = new GridCoord(r.X + 1 + CleaningRandom.Range(0, Math.Max(0, r.Width - 3)), r.Y + 1 + CleaningRandom.Range(0, Math.Max(0, r.Length - 3)));
            if (!r.Contains(tile) || !Nav.IsWalkable(tile) || room.Seats.Contains(tile) || room.Beds.Any(b => b.PatientSpot == tile)
                || room.Furniture.Any(f => f.Overlaps(tile.X, tile.Y, 1, 1)) || tile == room.PatientSpot)
            {
                continue;
            }

            if (_cleanerJobs.Values.Any(j => j.Spot == tile))
            {
                continue;
            }

            return tile;
        }

        return null;
    }

    private string PlaceName(GridCoord tile) =>
        _spaces.Values.Where(s => s.Rect.Contains(tile)).OrderBy(s => s.Kind == CareSpaceKind.Corridor ? 1 : 0).Select(s => s.Name).FirstOrDefault() ?? "the floor";
}
