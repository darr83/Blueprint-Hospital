using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Simulation;

public enum AnnouncementKind
{
    /// <summary>"Registrar is required at Resus 2." Played before the doctor sets off.</summary>
    StaffCall,

    /// <summary>"Adult cardiac arrest, Resus 1."</summary>
    CardiacArrest,

    /// <summary>"Trauma team to Resus 3."</summary>
    TraumaCall,

    /// <summary>"Ambulance due in five minutes. Assessment: potential heart attack."</summary>
    AmbulancePreAlert,

    /// <summary>Atmosphere: visiting times, fire alarm tests, capacity.</summary>
    General,
}

/// <summary>
/// A tannoy announcement. <see cref="Clips"/> are the recorded lines to play in order (after the chime);
/// <see cref="Text"/> is what is said, for the notification feed and subtitles.
/// </summary>
public sealed record Announcement(AnnouncementKind Kind, string Text, IReadOnlyList<string> Clips, int? PatientId = null) : IGameEvent;

/// <summary>An ambulance that has phoned ahead and hasn't arrived yet.</summary>
public sealed record PendingPreAlert(string ConditionId, DateTime DueAt);

/// <summary>Tannoy announcements: staff calls before staff go to resus, ambulance pre-alerts and general ones.</summary>
public sealed partial class HospitalSimulation
{
    /// <summary>How long a paged doctor waits for the announcement to finish before setting off (3 s at normal speed).</summary>
    public static readonly TimeSpan PageDelay = TimeSpan.FromSeconds(45);

    /// <summary>Recorded bay numbers. Bays past this are called as "Resus" without a number.</summary>
    public const int AnnouncedBays = 6;

    /// <summary>Lead times the crews phone ahead with, in game minutes. Each has a recorded line.</summary>
    public static readonly int[] PreAlertMinutes = { 5, 10 };

    private readonly List<PendingPreAlert> _preAlerts = new();
    private readonly HashSet<int> _traumaCalled = new();
    private DateTime _lastGeneralAnnouncement = DateTime.MinValue;

    public IReadOnlyList<PendingPreAlert> PreAlerts => _preAlerts;

    /// <summary>Red/Amber-looking ambulances phone ahead: anything that can kill, or with a NEWS2 of 5 or more.</summary>
    internal static bool NeedsPreAlert(Patient patient) =>
        patient.ArrivalMethod == ArrivalMethod.Ambulance
        && (patient.Condition.CanBeFatal || News2.Calculate(patient.BaselineObservations).Total >= 5);

    private void PreAlert(Patient patient)
    {
        var minutes = PreAlertMinutes[Random.Range(0, PreAlertMinutes.Length - 1)];
        _preAlerts.Add(new PendingPreAlert(patient.Condition.Id, Now.AddMinutes(minutes)));
        var assessment = string.IsNullOrWhiteSpace(patient.Condition.CrewAssessment)
            ? "unwell patient"
            : patient.Condition.CrewAssessment;
        var text = $"Ambulance due in {NumberWord(minutes)} minutes. Assessment: {assessment}.";
        Notifications.Add(Now, NotificationLevel.Info, $"Pre-alert: {text}");
        Events.Publish(new Announcement(AnnouncementKind.AmbulancePreAlert, text, new[] { $"amb_due_{minutes}", $"assess_{patient.Condition.Id}" }));
    }

    private void DispatchDuePreAlerts()
    {
        for (var i = _preAlerts.Count - 1; i >= 0; i--)
        {
            if (Now >= _preAlerts[i].DueAt)
            {
                // The patient is made when they arrive, so their wait starts at the door, not at the phone call.
                var conditionId = _preAlerts[i].ConditionId;
                _preAlerts.RemoveAt(i);
                AdmitNewPatient(_generator.Generate(Now, ArrivalMethod.Ambulance, conditionId));
            }
        }
    }

    /// <summary>"Doctor is required at Resus 2": doctors called to a first assessment in resus are paged before they go.</summary>
    private bool PageForResus(StaffMember member, ClinicalTask task, Patient patient)
    {
        if (member.Role != StaffRole.Doctor || task.Kind != TaskKind.DoctorAssessment
            || patient.CareSpaceId is not { } spaceId || FindSpace(spaceId) is not { Kind: CareSpaceKind.Resus } bay)
        {
            return false;
        }

        var who = CallerFor(member);
        var number = BayNumber(bay);
        var place = number is { } n ? $"Resus {n}" : "Resus";
        Events.Publish(new Announcement(AnnouncementKind.StaffCall, $"{who} is required at {place}.",
            new[] { $"call_{who.ToLowerInvariant()}_resus_{number?.ToString() ?? "x"}" }, patient.Id));
        member.PagedUntil = Now + PageDelay;
        member.StatusText = $"Called to {bay.Name}";
        return true;
    }

    /// <summary>Serious trauma arriving in resus gets the trauma team (cardiac arrests put out their own crash call).</summary>
    private void UpdateEmergencyCalls()
    {
        foreach (var patient in _patients.Values)
        {
            if (!patient.IsAlive || patient.CareSpaceId is not { } spaceId || FindSpace(spaceId) is not { } space || !space.IsTreatmentSpace)
            {
                continue;
            }

            if (space.Kind == CareSpaceKind.Resus && patient.Condition.Department == "trauma" && patient.ArrivalMethod == ArrivalMethod.Ambulance
                && _traumaCalled.Add(patient.Id))
            {
                var place = PlaceName(space);
                Events.Publish(new Announcement(AnnouncementKind.TraumaCall, $"Trauma team to {place.Spoken}.", new[] { $"trauma_{place.Key}" }, patient.Id));
            }
        }
    }

    /// <summary>Flavour announcements at set times of day.</summary>
    private void UpdateGeneralAnnouncements()
    {
        if (Now.Second != 0 || Now - _lastGeneralAnnouncement < TimeSpan.FromMinutes(30))
        {
            return;
        }

        (string Clip, string Text)? line = (Now.Hour, Now.Minute) switch
        {
            (10, 0) when Now.DayOfWeek == DayOfWeek.Wednesday => ("gen_fire_test", "This is a test of the fire alarm system. No action is required."),
            (20, 0) => ("gen_visiting_over", "Visiting time is now over. Thank you for your cooperation."),
            (19, 45) => ("gen_main_entrance", "The main entrance will close at eight p.m. Please use the A and E entrance after this time."),
            (14, 0) => ("gen_visiting_open", "Afternoon visiting has now started. Please use the hand gel as you enter the wards."),
            (8, 0) => ("gen_morning", "Good morning. The hospital shop and café are now open on the ground floor."),
            (23, 0) => ("gen_quiet", "Please keep noise to a minimum. Patients on the wards are sleeping."),
            _ => null,
        };

        if (line is { } l)
        {
            _lastGeneralAnnouncement = Now;
            Events.Publish(new Announcement(AnnouncementKind.General, l.Text, new[] { l.Clip }));
        }
    }

    internal void AnnounceFullCapacity()
    {
        if (Now - _lastGeneralAnnouncement < TimeSpan.FromMinutes(60))
        {
            return;
        }

        _lastGeneralAnnouncement = Now;
        Events.Publish(new Announcement(AnnouncementKind.General, "A and E is now at full capacity.", new[] { "gen_full_capacity" }));
    }

    /// <summary>The spoken caller for a doctor: "Doctor" for juniors and doctors, otherwise their grade.</summary>
    public static string CallerFor(StaffMember member) => member.Grade switch
    {
        "Registrar" => "Registrar",
        "Consultant" => "Consultant",
        _ => "Doctor",
    };

    /// <summary>The bay's number from its name ("Resus 2"), or its order among resus bays if renamed.</summary>
    internal int? BayNumber(CareSpace bay)
    {
        var digits = new string(bay.Name.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        var n = int.TryParse(digits, out var parsed) && bay.Name.StartsWith("Resus", StringComparison.OrdinalIgnoreCase)
            ? parsed
            : SpacesOfKind(CareSpaceKind.Resus).OrderBy(s => s.Id, StringComparer.Ordinal).ToList().IndexOf(bay) + 1;
        return n is >= 1 and <= AnnouncedBays ? n : null;
    }

    private (string Spoken, string Key) PlaceName(CareSpace space)
    {
        if (space.Kind == CareSpaceKind.Resus)
        {
            return BayNumber(space) is { } n ? ($"Resus {n}", $"resus_{n}") : ("Resus", "resus_x");
        }

        return space.Kind == CareSpaceKind.Majors ? ("Majors", "majors") : ("A and E", "ae");
    }

    private static string NumberWord(int n) => n switch
    {
        5 => "five",
        10 => "ten",
        _ => n.ToString(),
    };
}
