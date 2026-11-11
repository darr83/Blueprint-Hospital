using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// Starting a game and opening A&amp;E. A new game starts either with a small staffed A&amp;E, or with an
/// empty building and the value of the starter rooms in the bank. Neither has a waiting room: the player builds one.
/// Patients only come once A&amp;E can see them: a fitted-out waiting room, a triage room, a cubicle or resus bay,
/// a nurse and a doctor.
/// </summary>
public sealed partial class HospitalSimulation
{
    private bool _wasOpen;

    /// <summary>True when A&amp;E has what it needs to take patients.</summary>
    public bool IsEmergencyOpen => ClosedReasons.Count == 0;

    /// <summary>What A&amp;E still needs before patients can come, in plain words (empty when open).</summary>
    public IReadOnlyList<string> ClosedReasons
    {
        get
        {
            var missing = WaitingRoomReasons().ToList();
            if (!SpacesOfKind(CareSpaceKind.Triage).Any())
            {
                missing.Add("a triage room");
            }

            if (!_spaces.Values.Any(s => s.IsTreatmentSpace))
            {
                missing.Add("a cubicle or resus bay");
            }

            if (!_staff.Any(s => s.Role == StaffRole.Nurse && s.BaseRoomId is null))
            {
                missing.Add("an A&E nurse");
            }

            if (!_staff.Any(s => s.Role == StaffRole.Doctor && s.BaseRoomId is null))
            {
                missing.Add("an A&E doctor");
            }

            return missing;
        }
    }

    /// <summary>Takes out every starter room, and puts their value in the bank.</summary>
    private void ClearToEmptyBuilding()
    {
        var value = 0L;
        foreach (var space in _spaces.Values.ToList())
        {
            value += space.BuildCost;
            _spaces.Remove(space.Id);
        }

        Balance += value;
        Nav = EmergencyDepartmentLayout.BuildNavGrid(Site, _spaces.Values);
        Notifications.Add(Now, NotificationLevel.Info, "An empty hospital. A&E opens to patients once it has " + Join(ClosedReasons) + ".");
    }

    /// <summary>
    /// A Career game's start: the starter A&amp;E without the rooms level 1 hasn't unlocked (majors and
    /// resus), a ready-made waiting room, and a small team of a receptionist, two nurses and a doctor
    /// on each shift, so patients can start coming straight away.
    /// </summary>
    private void SetUpWalkInCentre()
    {
        foreach (var space in _spaces.Values.Where(s => !IsRoomUnlocked(s.TemplateId)).ToList())
        {
            _spaces.Remove(space.Id);
        }

        Nav = EmergencyDepartmentLayout.BuildNavGrid(Site, _spaces.Values);
        BuildStarterWaitingRoom();
        foreach (var shift in new[] { CurrentShift, CurrentShift == StaffShift.Day ? StaffShift.Night : StaffShift.Day })
        {
            HireStaff(StaffRole.Receptionist, skill: 0.5, shift: shift);
            HireStaff(StaffRole.Nurse, skill: 0.65, shift: shift);
            HireStaff(StaffRole.Nurse, skill: 0.55, shift: shift);
            HireStaff(StaffRole.Doctor, skill: 0.65, shift: shift);
        }

        foreach (var member in _staff)
        {
            member.PlaceAt(HomeTile(member));
        }

        var level = CurrentLevel!;
        Notifications.Add(Now, NotificationLevel.Info, $"Level 1: {level.Name}. {level.Description} Your goals are in the level badge at the top.");
    }

    /// <summary>Tells the player when A&amp;E opens or has to close.</summary>
    private void AnnounceOpening()
    {
        var open = IsEmergencyOpen;
        if (open == _wasOpen)
        {
            return;
        }

        _wasOpen = open;
        if (open)
        {
            Notifications.Add(Now, NotificationLevel.Info, "A&E is open: patients are on their way.");
        }
        else
        {
            Notifications.Add(Now, NotificationLevel.Warning, $"A&E has closed to new patients: it needs {Join(ClosedReasons)}.");
        }
    }

    private static string Join(IReadOnlyList<string> items) =>
        items.Count <= 1 ? string.Join(string.Empty, items) : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];
}
