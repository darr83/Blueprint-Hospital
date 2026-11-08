using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// Shifts, breaks and moving staff about. Everyone is on the day team (08:00 to 20:00) or the night
/// team (20:00 to 08:00). Off-duty staff are not in the building. At shift change the outgoing team
/// finish what they are doing and walk out of the main entrance; the incoming team walk in.
/// Each shift has a 30-minute meal break and two 15-minute tea breaks at random times. On a break
/// staff sit in the staff room and take no work. No more than a third of a team is away at once, and a
/// break waits (for up to an hour) while a Red patient needs them.
/// </summary>
public sealed partial class HospitalSimulation
{
    public const int DayShiftStartHour = 8;
    public const int NightShiftStartHour = 20;
    public const int ShiftHours = 12;
    public const int MealBreakMinutes = 30;
    public const int TeaBreakMinutes = 15;

    /// <summary>How long a planned break can be put off for patients before they go anyway.</summary>
    public static readonly TimeSpan MaxBreakDelay = TimeSpan.FromHours(1);

    /// <summary>A break this late is shown as put off.</summary>
    public static readonly TimeSpan BreakLateAfter = TimeSpan.FromMinutes(5);

    private GameRandom? _rotaRandom;

    /// <summary>Break times have their own random numbers, so planning breaks doesn't change how patients turn out.</summary>
    private GameRandom RotaRandom => _rotaRandom ??= new GameRandom(_options.Seed + 7919);

    /// <summary>The shift running now.</summary>
    public StaffShift CurrentShift => ShiftAt(Now);

    /// <summary>When the shift running now began.</summary>
    public DateTime CurrentShiftStart => ShiftStart(Now);

    /// <summary>When the shift running now ends and the other team takes over.</summary>
    public DateTime CurrentShiftEnd => ShiftStart(Now).AddHours(ShiftHours);

    /// <summary>Staff at work now (working, free or on a break).</summary>
    public IEnumerable<StaffMember> OnDutyStaff => _staff.Where(s => s.IsOnDuty);

    public static StaffShift ShiftAt(DateTime time) =>
        time.Hour >= DayShiftStartHour && time.Hour < NightShiftStartHour ? StaffShift.Day : StaffShift.Night;

    /// <summary>When the shift running at <paramref name="time"/> began.</summary>
    public static DateTime ShiftStart(DateTime time) => ShiftAt(time) == StaffShift.Day
        ? time.Date.AddHours(DayShiftStartHour)
        : time.Hour >= NightShiftStartHour ? time.Date.AddHours(NightShiftStartHour) : time.Date.AddDays(-1).AddHours(NightShiftStartHour);

    /// <summary>When someone on <paramref name="shift"/> next starts work (now's shift start if it is running).</summary>
    public DateTime NextStart(StaffShift shift)
    {
        if (CurrentShift == shift)
        {
            return CurrentShiftStart;
        }

        return CurrentShiftEnd;
    }

    public static string ShiftName(StaffShift shift) => shift == StaffShift.Day ? "day" : "night";

    public static string ShiftHoursText(StaffShift shift) => shift == StaffShift.Day ? "08:00 to 20:00" : "20:00 to 08:00";

    /// <summary>The shift with fewer people of this role at this place, so a new hire evens things up (ties go to the shift running now).</summary>
    public StaffShift ShorterShift(StaffRole role, string? roomId)
    {
        if (roomId is { } id && FindSpace(id) is { Kind: CareSpaceKind.Clinic or CareSpaceKind.OutpatientReception })
        {
            return StaffShift.Day;
        }

        var day = _staff.Count(s => s.Role == role && s.BaseRoomId == roomId && s.Shift == StaffShift.Day);
        var night = _staff.Count(s => s.Role == role && s.BaseRoomId == roomId && s.Shift == StaffShift.Night);
        return day == night ? CurrentShift : day < night ? StaffShift.Day : StaffShift.Night;
    }

    // ---------------------------------------------------------------- player actions

    /// <summary>Moves someone to the day or night team. They go home or come in straight away (after finishing any task).</summary>
    public bool SetShift(int staffId, StaffShift shift)
    {
        if (FindStaff(staffId) is not { } member || member.Shift == shift)
        {
            return false;
        }

        member.Shift = shift;
        var now = shift == CurrentShift ? "comes in now" : member.IsOnDuty ? "goes home once they're free" : $"starts at {NextStart(shift):HH:mm}";
        Notifications.Add(Now, NotificationLevel.Info, $"{member.Name} is now on the {ShiftName(shift)} team ({ShiftHoursText(shift)}) and {now}.");
        if (!member.IsOnDuty)
        {
            member.StatusText = OffDutyText(member);
        }

        return true;
    }

    /// <summary>
    /// Moves someone to work somewhere else: A&amp;E, a ward, a clinic, or another room of their kind.
    /// Someone busy with a patient finishes that task first.
    /// </summary>
    public bool MoveStaff(int staffId, string? roomId)
    {
        if (FindStaff(staffId) is not { } member || !CanMoveTo(member, roomId))
        {
            return false;
        }

        if (member.BaseRoomId == roomId)
        {
            // Staying put: cancels a move that hadn't happened yet.
            var cancelled = member.HasPendingMove;
            member.HasPendingMove = false;
            member.PendingRoomId = null;
            return cancelled;
        }

        if (member.CurrentTaskId is not null && member.IsOnDuty)
        {
            member.HasPendingMove = true;
            member.PendingRoomId = roomId;
            Notifications.Add(Now, NotificationLevel.Info, $"{member.Name} will move to {PlaceName(member.Role, roomId)} once they finish with their patient.");
            return true;
        }

        ApplyMove(member, roomId);
        return true;
    }

    /// <summary>Whether someone in this role can be based at <paramref name="roomId"/>.</summary>
    public bool CanMoveTo(StaffMember member, string? roomId) =>
        PostingsFor(member.Role).Any(p => p.RoomId == roomId && (roomId is not null || !IsSpecialist(member.Role)));

    /// <summary>The name of a place someone can be based, e.g. "A&amp;E", "Whole hospital" or a room's name.</summary>
    public string PlaceName(StaffRole role, string? roomId) =>
        roomId is { } id && FindSpace(id) is { } room ? room.Name : PostingsFor(role)[0].Name;

    /// <summary>Sends someone on a 15-minute break as soon as they're free, whatever the team is doing.</summary>
    public bool SendOnBreak(int staffId)
    {
        if (FindStaff(staffId) is not { } member || !member.IsOnDuty || member.IsOnBreak || member.Breaks.Any(b => b.Requested && !b.Done))
        {
            return false;
        }

        member.Breaks.Add(new PlannedBreak { Start = Now, Minutes = TeaBreakMinutes, Requested = true });
        member.Breaks.Sort((a, b) => a.Start.CompareTo(b.Start));
        return true;
    }

    private void ApplyMove(StaffMember member, string? roomId)
    {
        member.HasPendingMove = false;
        member.PendingRoomId = null;
        member.BaseRoomId = roomId;
        member.Caseload.Clear();
        Notifications.Add(Now, NotificationLevel.Info, $"{member.Name} ({member.Grade}) now works in {PlaceName(member.Role, roomId)}.");
        if (member.IsOnDuty && !member.IsOnBreak && member.CurrentTaskId is null)
        {
            member.Activity = StaffActivity.Idle;
            member.StatusText = "Available";
            WalkTo(member, HomeTile(member));
        }
    }

    // ---------------------------------------------------------------- each tick

    /// <summary>
    /// Sends people home at the end of their shift and brings the next team in. Returns true when the
    /// member has nothing else to do this tick (off duty, or walking out).
    /// </summary>
    private bool UpdateDuty(StaffMember member)
    {
        var theirShift = member.Shift == CurrentShift;
        switch (member.Activity)
        {
            case StaffActivity.OffDuty:
                if (!theirShift)
                {
                    return true;
                }

                StartShift(member);
                return false;

            case StaffActivity.GoingHome:
                if (theirShift)
                {
                    member.Activity = StaffActivity.Idle;
                    member.StatusText = "Available";
                    PlanBreaks(member, CurrentShiftStart);
                    WalkTo(member, HomeTile(member));
                    return false;
                }

                if (!member.IsMoving)
                {
                    member.Activity = StaffActivity.OffDuty;
                    member.StatusText = OffDutyText(member);
                }

                return true;
        }

        var free = member.CurrentTaskId is null && !member.IsOnBreak && member.PushingPatientId is null
                   && !_consults.Values.Any(c => c.StaffId == member.Id);
        if (free && member.HasPendingMove)
        {
            ApplyMove(member, member.PendingRoomId);
        }

        if (free && !theirShift)
        {
            GoHome(member);
            return true;
        }

        return false;
    }

    private void StartShift(StaffMember member)
    {
        member.Fatigue = Math.Min(member.Fatigue, RotaRandom.Range(0.0, 0.1));
        member.Activity = StaffActivity.Idle;
        member.StatusText = "Arriving for the shift";
        member.CurrentTaskId = null;
        member.PlaceAt(WalkInSpawn);
        PlanBreaks(member, CurrentShiftStart);
        WalkTo(member, HomeTile(member));
    }

    private void GoHome(StaffMember member)
    {
        member.Activity = StaffActivity.GoingHome;
        member.StatusText = "Shift over: going home";
        member.BreakEndsAt = null;
        member.Breaks.Clear();
        member.Caseload.Clear();
        foreach (var room in _spaces.Values.Where(r => r.ReservedByStaffId == member.Id))
        {
            room.ReservedByStaffId = null;
        }

        WalkTo(member, WalkInSpawn);
    }

    private string OffDutyText(StaffMember member) => $"Off duty · in at {NextStart(member.Shift):HH:mm}";

    /// <summary>
    /// A tea break in the first third of the shift, a meal break in the middle and a tea break in the
    /// last third, at random times and never in the first or last hour. Breaks already past are dropped.
    /// </summary>
    private void PlanBreaks(StaffMember member, DateTime shiftStart)
    {
        member.Breaks.Clear();
        foreach (var (from, to, minutes) in new[] { (60, 225, TeaBreakMinutes), (270, 450, MealBreakMinutes), (510, 645, TeaBreakMinutes) })
        {
            var start = shiftStart.AddMinutes(RotaRandom.Range(from, to));
            if (start >= Now)
            {
                member.Breaks.Add(new PlannedBreak { Start = start, Minutes = minutes });
            }
        }
    }

    /// <summary>A break that's due now and not yet taken.</summary>
    private PlannedBreak? DueBreak(StaffMember member) =>
        member.Breaks.FirstOrDefault(b => b.TakenAt is null && !b.Done && Now >= b.Start);

    /// <summary>
    /// Whether a member of staff can go on their due break: not while a Red patient is waiting for
    /// them, and not while a third of their team is already away (unless it's been put off an hour).
    /// </summary>
    private bool CanTakeBreak(StaffMember member, PlannedBreak due, ClinicalTask? bestTask)
    {
        if (due.Requested || Now - due.Start >= MaxBreakDelay)
        {
            return true;
        }

        if (bestTask is not null && bestTask.Priority >= RedOngoing)
        {
            return false;
        }

        var team = _staff.Where(s => s.IsOnDuty && s.Role == member.Role && s.BaseRoomId == member.BaseRoomId).ToList();
        var away = team.Count(s => s.IsOnBreak);
        return away < Math.Max(1, team.Count / 3);
    }

    /// <summary>Whether this break has waited long enough to show as put off.</summary>
    public bool IsBreakLate(PlannedBreak b) => b.TakenAt is null && !b.Done && Now - b.Start >= BreakLateAfter;

    /// <summary>Marks the break they were on as done when they come back.</summary>
    private static void FinishBreak(StaffMember member)
    {
        foreach (var b in member.Breaks.Where(b => b.TakenAt is not null && !b.Done))
        {
            b.Done = true;
        }
    }

    /// <summary>Gives everyone a team for an old save: each place's staff are split between day and night.</summary>
    private void SplitIntoTeams()
    {
        foreach (var group in _staff.GroupBy(s => (s.Role, s.BaseRoomId)))
        {
            var i = 0;
            foreach (var member in group.OrderBy(s => s.Id))
            {
                var clinic = member.BaseRoomId is { } id && FindSpace(id) is { Kind: CareSpaceKind.Clinic or CareSpaceKind.OutpatientReception };
                member.Shift = clinic || i++ % 2 == 0 ? StaffShift.Day : StaffShift.Night;
            }
        }

        foreach (var member in _staff.Where(s => s.Shift == CurrentShift))
        {
            PlanBreaks(member, CurrentShiftStart);
        }

        foreach (var member in _staff.Where(s => s.Shift != CurrentShift && s.CurrentTaskId is null && !s.IsOnBreak))
        {
            member.Activity = StaffActivity.OffDuty;
            member.StatusText = OffDutyText(member);
        }
    }
}
