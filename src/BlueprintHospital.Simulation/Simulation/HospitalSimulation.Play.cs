using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>A waiting child's spell in the play room (or the teenage room).</summary>
public sealed class PlayVisit
{
    public required Patient Child { get; init; }

    public required string RoomId { get; init; }

    /// <summary>The chair (or beanbag) they play at.</summary>
    public required GridCoord Seat { get; init; }

    /// <summary>When they have had enough and go back to their seat in the waiting room.</summary>
    public DateTime Until { get; set; }

    /// <summary>Whether they have got there and started playing.</summary>
    public bool Playing { get; set; }
}

/// <summary>
/// Play rooms and teenage rooms, run by play specialists. A child who has been waiting in A&amp;E for a
/// while, and is well enough to be up and about, now and then goes to play there with their parent while
/// a play specialist is on duty. Under 12s use the play room; 12 to 15 year olds the teenage room if
/// there is one. A child who has been playing is half as likely to give up and go home before being seen,
/// and one who is called simply goes straight to whoever called them.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>The age from which children use the teenage room, if there is one.</summary>
    public const int TeenRoomAge = 12;

    /// <summary>How long a child waits before they want to go and play.</summary>
    public static readonly TimeSpan PlayAfterWaiting = TimeSpan.FromMinutes(10);

    /// <summary>Once they have played, how long it keeps them happy (and before they want to go again).</summary>
    public static readonly TimeSpan PlayLasts = TimeSpan.FromHours(2);

    /// <summary>On average, an eligible waiting child goes to play about once in this many minutes.</summary>
    private const double PlayEveryMinutes = 15;

    private readonly Dictionary<Patient, PlayVisit> _playVisits = new();
    private readonly Dictionary<int, DateTime> _lastPlayed = new();
    private GameRandom? _playRandom;

    /// <summary>A separate random stream, so building a play room doesn't change anything else that happens.</summary>
    private GameRandom PlayRandom => _playRandom ??= new GameRandom(_options.Seed + 130_363);

    /// <summary>Play rooms and teenage rooms.</summary>
    public IEnumerable<CareSpace> PlayRooms => SpacesOfKind(CareSpaceKind.PlayRoom);

    /// <summary>Children in a play room (or on their way) right now.</summary>
    public IReadOnlyCollection<PlayVisit> PlayVisits => _playVisits.Values;

    /// <summary>Whether a play room is open: a play specialist based there is at work.</summary>
    public bool IsPlayRoomOpen(CareSpace room) =>
        _staff.Any(s => s.BaseRoomId == room.Id && s.Role == StaffRole.PlaySpecialist && s.Activity is not (StaffActivity.OffDuty or StaffActivity.GoingHome));

    /// <summary>The play room a child is playing in (once they have got there).</summary>
    public CareSpace? PlayRoomOf(Patient child) =>
        _playVisits.TryGetValue(child, out var visit) && visit.Playing && child.Tile == visit.Seat ? FindSpace(visit.RoomId) : null;

    /// <summary>Whether a child has played recently, which keeps them patient.</summary>
    public bool HasPlayedRecently(Patient child) => _lastPlayed.TryGetValue(child.Id, out var at) && Now - at < PlayLasts;

    /// <summary>What a play room still needs before it can open (empty when it can).</summary>
    public IReadOnlyList<string> PlayRoomProblems(CareSpace room)
    {
        var problems = new List<string>();
        if (!_staff.Any(s => s.BaseRoomId == room.Id && s.Role == StaffRole.PlaySpecialist))
        {
            problems.Add("a play specialist posted to it");
        }

        if (room.Seats.Count == 0)
        {
            problems.Add("room for chairs");
        }

        return problems;
    }

    private bool PlaySeatBooked(GridCoord tile) => _playVisits.Values.Any(v => v.Seat == tile);

    private void UpdatePlay(double dt)
    {
        foreach (var visit in _playVisits.Values.ToList())
        {
            if (PlayInterrupted(visit))
            {
                _playVisits.Remove(visit.Child);
                continue;
            }

            if (FindSpace(visit.RoomId) is not { } room || !IsPlayRoomOpen(room))
            {
                // The room was knocked down, or the play specialist went home: back to the waiting room.
                EndPlay(visit);
                continue;
            }

            ProgressPlay(visit, room);
        }

        var open = PlayRooms.Where(IsPlayRoomOpen).ToList();
        SendStrandedPlayersBack();
        if (open.Count == 0)
        {
            return;
        }

        var chance = dt / (PlayEveryMinutes * 60);
        foreach (var child in _patients.Values.Where(CanGoAndPlay).OrderBy(p => p.Id).ToList())
        {
            if (PlayRandom.Chance(chance))
            {
                StartPlay(child, open);
            }
        }
    }

    /// <summary>A child up and about in the waiting room (not Red), who has waited a while and hasn't played lately.</summary>
    private bool CanGoAndPlay(Patient p) =>
        p.Age < AdultAge
        && p.Stage is PatientStage.WaitingForTriage or PatientStage.WaitingForSpace
        && p.Triage != TriageCategory.Red
        && p.Mobility == Mobility.Walking
        && p.Seat is { } seat && p.Tile == seat && !p.IsMoving
        && p.BusyWithTaskId is null && p.CareSpaceId is null && p.PendingMove is null
        && Now - p.QueuedSince >= PlayAfterWaiting
        && !_playVisits.ContainsKey(p) && !Away(p)
        && !HasPlayedRecently(p);

    /// <summary>Called, moved, gone home or otherwise taken over by something else: the visit is off.</summary>
    private bool PlayInterrupted(PlayVisit visit)
    {
        var p = visit.Child;
        return (p.Destination is { } heading && heading != visit.Seat)
               || p.Stage is not (PatientStage.WaitingForTriage or PatientStage.WaitingForSpace)
               || p.BusyWithTaskId is not null || p.CareSpaceId is not null || p.PendingMove is not null;
    }

    /// <summary>The room for this child: teenagers the teenage room if one is open, everyone else the play room (or whichever is open).</summary>
    private IEnumerable<CareSpace> PlayRoomsFor(Patient child, IReadOnlyList<CareSpace> open)
    {
        var teen = child.Age >= TeenRoomAge;
        bool IsTeenRoom(CareSpace r) => r.TemplateId == "teen_room";
        var preferred = open.Where(r => IsTeenRoom(r) == teen).ToList();
        var floor = Site.FloorOf(child.Tile);

        // A little one never goes to the teenage room; a teenager will use the play room if there is nothing else.
        var rooms = preferred.Count > 0 ? preferred : teen ? open.ToList() : new List<CareSpace>();
        return rooms.Where(r => Site.FloorOf(r.Rect) == floor).OrderBy(r => Distance(child.Tile, r.PatientSpot));
    }

    private void StartPlay(Patient child, IReadOnlyList<CareSpace> open)
    {
        foreach (var room in PlayRoomsFor(child, open))
        {
            var free = room.Seats.Where(s => !PlaySeatBooked(s) && !IsSpotTaken(s, child) && !_parents.Values.Any(p => p.Tile == s || p.Heading == s)).ToList();
            // Leave a seat for their parent.
            if (free.Count < 2)
            {
                continue;
            }

            var seat = free[PlayRandom.Range(0, free.Count - 1)];
            _playVisits[child] = new PlayVisit { Child = child, RoomId = room.Id, Seat = seat };
            WalkTo(child, seat);
            return;
        }
    }

    private void ProgressPlay(PlayVisit visit, CareSpace room)
    {
        var child = visit.Child;
        if (!visit.Playing && !child.IsMoving && child.Tile == visit.Seat)
        {
            visit.Playing = true;
            visit.Until = Now + TimeSpan.FromMinutes(PlayRandom.Range(15, 35));
            var what = room.TemplateId == "teen_room"
                ? PlayRandom.Chance(0.5) ? "played on the games console" : "read and played cards"
                : PlayRandom.Chance(0.5) ? "did some colouring" : "played with the toys";
            child.Log(Now, $"Went to {room.Name} and {what}.");
            _lastPlayed[child.Id] = Now;
        }
        else if (!visit.Playing && !child.IsMoving)
        {
            // Got stuck on the way (the room was rebuilt): try again.
            WalkTo(child, visit.Seat);
        }
        else if (visit.Playing && Now >= visit.Until)
        {
            EndPlay(visit);
        }
    }

    private void EndPlay(PlayVisit visit)
    {
        _playVisits.Remove(visit.Child);
        if (visit.Child.Seat is { } seat)
        {
            WalkTo(visit.Child, seat);
        }
        else
        {
            SendToWaitingRoom(visit.Child);
        }
    }

    /// <summary>A waiting child left in a play room without a visit (after loading a game) goes back to their seat.</summary>
    private void SendStrandedPlayersBack()
    {
        var rooms = PlayRooms.ToList();
        if (rooms.Count == 0)
        {
            return;
        }

        foreach (var p in _patients.Values)
        {
            if (p.Stage is PatientStage.WaitingForTriage or PatientStage.WaitingForSpace
                && !p.IsMoving && p.BusyWithTaskId is null && p.PendingMove is null && p.CareSpaceId is null
                && !_playVisits.ContainsKey(p) && p.Seat is { } seat && p.Tile != seat && rooms.Any(r => r.Rect.Contains(p.Tile)))
            {
                WalkTo(p, seat);
            }
        }
    }

    /// <summary>A child who has been playing is half as likely to walk out.</summary>
    private double PlayWalkOutFactor(Patient patient) => HasPlayedRecently(patient) ? 0.5 : 1.0;
}
