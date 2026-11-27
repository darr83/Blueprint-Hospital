using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>How serious a complaint is: it sets the reply deadline and what an Ombudsman case costs.</summary>
public enum ComplaintSeverity
{
    /// <summary>An informal concern: reply within a day or it becomes a formal complaint.</summary>
    Concern,

    /// <summary>A formal written complaint: reply within 3 days.</summary>
    Formal,

    /// <summary>A death or a missed serious illness: reply within 2 days.</summary>
    Serious,
}

public enum ComplaintStatus
{
    /// <summary>Waiting for the player to reply.</summary>
    Open,

    /// <summary>A meeting or investigation is under way.</summary>
    Waiting,

    /// <summary>The complainant was satisfied.</summary>
    Resolved,

    /// <summary>Nobody replied in time and the Ombudsman fined the hospital.</summary>
    Ombudsman,
}

/// <summary>The ways the player can answer a complaint. Each one can be tried once.</summary>
public enum ComplaintResponse
{
    Apologise,
    Meet,
    Goodwill,
    Investigate,
}

/// <summary>A complaint from a patient (or their family) about their stay.</summary>
public sealed class Complaint
{
    public required int Id { get; init; }

    public int? PatientId { get; init; }

    /// <summary>Who wrote it, e.g. "Margaret Hughes, 78" or "Family of Daniel Okafor, 45".</summary>
    public required string From { get; init; }

    public required string Department { get; init; }

    public required ComplaintSeverity Severity { get; set; }

    public required DateTime ReceivedAt { get; init; }

    public required DateTime DueAt { get; set; }

    /// <summary>What they wrote, in their own words.</summary>
    public required string Quote { get; init; }

    /// <summary>What their record shows happened.</summary>
    public List<string> Facts { get; init; } = new();

    /// <summary>What an investigation finds went wrong (hidden until one is done).</summary>
    public required string Cause { get; init; }

    public bool CauseFound { get; set; }

    public ComplaintStatus Status { get; set; } = ComplaintStatus.Open;

    public List<ComplaintResponse> Tried { get; init; } = new();

    public ComplaintResponse? Pending { get; set; }

    public DateTime? PendingUntil { get; set; }

    /// <summary>The latest news on it, e.g. "Not satisfied with the apology".</summary>
    public string Outcome { get; set; } = string.Empty;

    public DateTime? ClosedAt { get; set; }

    /// <summary>Money spent on it so far (goodwill, investigation, fine).</summary>
    public long Spent { get; set; }

    public bool IsOpen => Status is ComplaintStatus.Open or ComplaintStatus.Waiting;
}

/// <summary>A thank-you card from a patient who had a good stay.</summary>
public sealed record ThankYou(int Id, int? PatientId, string From, string Department, DateTime At, string Quote);

/// <summary>A comment left with a star rating.</summary>
public sealed record FeedbackComment(int? PatientId, string Name, string Department, int Stars, string Quote, DateTime At);

/// <summary>A short-lived nudge to the hospital's star rating (answered complaints, Ombudsman cases).</summary>
public sealed record ReputationChange(DateTime At, double Amount, string Why);

/// <summary>One thing patients mention when they rate their stay.</summary>
public sealed record FeedbackReason(string Id, bool Good, string Label);

/// <summary>A reply the player can give, with what it costs and whether it can be used now.</summary>
public sealed record ComplaintOption(ComplaintResponse Response, string Label, string Detail, long Cost, bool Enabled, string? Why);

/// <summary>Ratings over a period, for the Feedback panel.</summary>
public sealed record FeedbackSummary(
    int Count,
    double Average,
    IReadOnlyList<int> ByStars,
    double Recommend,
    IReadOnlyList<(FeedbackReason Reason, int Count)> Reasons,
    IReadOnlyList<(string Department, int Count, double Average)> Departments);

public sealed class FeedbackState
{
    public List<Complaint> Complaints { get; init; } = new();

    public List<ThankYou> ThankYous { get; init; } = new();

    public List<FeedbackComment> Comments { get; init; } = new();

    public List<ReputationChange> Changes { get; init; } = new();

    public int NextId { get; init; } = 1;
}

/// <summary>
/// Patient feedback: everyone rates their stay (1 to 5 stars) as they leave, from what actually
/// happened to them. Unhappy patients sometimes complain, and the player answers each complaint
/// before its deadline or the Ombudsman fines the hospital. Very happy patients send thank-yous.
/// The hospital's star rating changes how many GP referrals the clinics get and the size of level grants.
/// </summary>
public sealed partial class HospitalSimulation
{
    public const int ApologyCost = 0;
    public const int InvestigationCost = 1_500;
    public static readonly TimeSpan MeetingTakes = TimeSpan.FromHours(1);
    public static readonly TimeSpan InvestigationTakes = TimeSpan.FromHours(24);

    /// <summary>How long a reputation nudge lasts; it fades evenly to nothing.</summary>
    public static readonly TimeSpan ReputationFades = TimeSpan.FromDays(14);

    private const int OpenComplaintsKept = 12;
    private const int ClosedKept = 40;
    private const int CommentsKept = 30;

    public static readonly IReadOnlyList<FeedbackReason> FeedbackReasons = new FeedbackReason[]
    {
        new("caring", true, "Kind, caring staff"),
        new("quick", true, "Seen quickly"),
        new("visitors", true, "Family could visit"),
        new("food", true, "Food and drink"),
        new("cheerful", true, "Felt well looked after"),
        new("wait", false, "Long wait in A&E"),
        new("doctor_wait", false, "Long wait to see a doctor"),
        new("corridor", false, "Care in the corridor"),
        new("bed_wait", false, "Waited for a ward bed"),
        new("no_drink", false, "Nothing to eat or drink"),
        new("accident", false, "Couldn't get to a toilet"),
        new("cancelled", false, "Operation cancelled"),
        new("op_late", false, "Operation later than promised"),
        new("complication", false, "Complication after surgery"),
        new("lonely", false, "No visitors, felt low"),
        new("unseen", false, "Left without being seen"),
        new("clinic_wait", false, "Kept waiting in clinic"),
    };

    private readonly List<Complaint> _complaints = new();
    private readonly List<ThankYou> _thankYous = new();
    private readonly List<FeedbackComment> _comments = new();
    private readonly List<ReputationChange> _reputation = new();
    private int _nextFeedbackId = 1;
    private GameRandom? _feedbackRandom;

    /// <summary>Its own random numbers, so feedback never changes what happens to patients.</summary>
    private GameRandom FeedbackRandom => _feedbackRandom ??= new GameRandom(_options.Seed + 271_828);

    /// <summary>Ratings, complaints and thank-yous are on (off in the tutorial, or when the options turn them off).</summary>
    public bool FeedbackOn => _options.PatientFeedback && !IsTutorial;

    /// <summary>Every complaint, newest first: open ones and the last few closed.</summary>
    public IReadOnlyList<Complaint> Complaints => _complaints;

    public IEnumerable<Complaint> OpenComplaints => _complaints.Where(c => c.IsOpen).OrderBy(c => c.DueAt);

    public IReadOnlyList<ThankYou> ThankYous => _thankYous;

    /// <summary>The latest comments left with ratings, newest first.</summary>
    public IReadOnlyList<FeedbackComment> FeedbackComments => _comments;

    /// <summary>The current nudge to the star rating from answered complaints and Ombudsman cases.</summary>
    public double ReputationAdjustment => _reputation.Sum(r => r.Amount * Math.Max(0, 1 - ((Now - r.At) / ReputationFades)));

    public IReadOnlyList<ReputationChange> ReputationChanges => _reputation;

    public static FeedbackReason ReasonById(string id) => FeedbackReasons.First(r => r.Id == id);

    /// <summary>
    /// The hospital's star rating: patients' average over the last 7 days (all time while there are
    /// only a few), plus any nudge from complaints. Null until anyone has rated a stay.
    /// </summary>
    public double? HospitalRating
    {
        get
        {
            var week = FeedbackFor(StatsPeriod.Last7Days);
            var summary = week.Count >= 5 ? week : FeedbackFor(StatsPeriod.AllTime);
            if (summary.Count == 0)
            {
                return null;
            }

            return Math.Clamp(summary.Average + ReputationAdjustment, 1, 5);
        }
    }

    /// <summary>GP referrals to clinics: more when patients rate the hospital well, fewer when they don't.</summary>
    public double ReferralFactor => HospitalRating switch
    {
        >= 4 => 1.1,
        < 3 => 0.85,
        _ => 1.0,
    };

    /// <summary>Level grants shrink while the hospital's rating is under 3 stars.</summary>
    public double GrantFactor => HospitalRating is < 3 ? 0.75 : 1.0;

    public FeedbackSummary FeedbackFor(StatsPeriod period)
    {
        var history = HistoryStore;
        double T(string key) => history.Total(period, Now, key);
        var count = (int)T(FeedbackKeys.Ratings);
        var byStars = Enumerable.Range(1, 5).Select(s => (int)T(FeedbackKeys.Stars(s))).ToList();
        var reasons = FeedbackReasons.Select(r => (r, (int)T(FeedbackKeys.Reason(r.Id)))).Where(r => r.Item2 > 0)
            .OrderByDescending(r => r.Item2).ToList();
        var departments = history.Buckets(period, Now).SelectMany(b => b.Totals.Keys).Where(k => k.StartsWith(FeedbackKeys.DepartmentPrefix, StringComparison.Ordinal))
            .Distinct().Select(k => k[FeedbackKeys.DepartmentPrefix.Length..])
            .Select(d => (Department: d, Count: (int)T(FeedbackKeys.DepartmentCount(d)), Total: T(FeedbackKeys.DepartmentStars(d))))
            .Where(d => d.Count > 0)
            .Select(d => (FeedbackDepartmentName(d.Department), d.Count, d.Total / d.Count))
            .OrderByDescending(d => d.Count).ToList();
        return new FeedbackSummary(count, count == 0 ? 0 : T(FeedbackKeys.StarTotal) / count, byStars,
            count == 0 ? 0 : T(FeedbackKeys.Recommend) / count, reasons, departments);
    }

    private string FeedbackDepartmentName(string id) => id switch
    {
        EmergencyDepartmentLayout.Department => "A&E",
        FeedbackKeys.ClinicsDepartment => "Clinics",
        FeedbackKeys.MaternityDepartment => "Maternity",
        _ => Database.Departments.TryGetValue(id, out var d) ? (string.IsNullOrEmpty(d.ShortName) ? d.Name : d.ShortName) : id,
    };

    // ---------------------------------------------------------------- rating a stay

    /// <summary>
    /// What a patient makes of their stay, from what happened to them: 1 to 5 stars and the things
    /// they mention. Waiting, the corridor, no drink, accidents and cancelled operations pull it down;
    /// being seen quickly, visitors and a good mood lift it.
    /// </summary>
    public (int Stars, List<string> Reasons, List<string> Facts) JudgeStay(Patient p, PatientOutcome outcome, double luck = 0)
    {
        var score = 4.6;
        var bad = new List<string>();
        var good = new List<string>();
        var facts = new List<string>();
        var viaEmergency = !p.Condition.IsPlanned && !(p.Maternity && p.ArrivedAt == p.AdmittedAt);
        var inEmergency = p.TimeInEmergency(Now);
        if (viaEmergency && inEmergency > TimeSpan.FromHours(4))
        {
            var hours = inEmergency.TotalHours;
            score -= hours > 12 ? 3 : hours > 8 ? 2 : 1;
            bad.Add("wait");
            facts.Add($"{FormatWait(inEmergency)} in A&E (4-hour target missed)");
        }

        if (viaEmergency && p.FirstSeenByDoctorAt is { } seen)
        {
            var toDoctor = seen - p.ArrivedAt;
            if (toDoctor > TimeSpan.FromHours(2))
            {
                score -= 0.5;
                bad.Add("doctor_wait");
                facts.Add($"Waited {FormatWait(toDoctor)} to see a doctor");
            }
            else if (toDoctor <= TimeSpan.FromMinutes(30))
            {
                score += 0.3;
                good.Add("quick");
            }
        }

        if (p.CorridorMinutes >= 30)
        {
            score -= 1;
            bad.Add("corridor");
            facts.Add($"{FormatWait(TimeSpan.FromMinutes(p.CorridorMinutes))} of corridor care");
        }

        if (p.BedRequestedAt is { } requested && p.LeftEmergencyAt is { } left && left - requested > TimeSpan.FromHours(4))
        {
            score -= 0.5;
            bad.Add("bed_wait");
            facts.Add($"Waited {FormatWait(left - requested)} for a ward bed");
        }

        if (viaEmergency && inEmergency > TimeSpan.FromHours(4) && p.LastRefreshmentAt is null)
        {
            score -= 0.5;
            bad.Add("no_drink");
            facts.Add("Nothing to eat or drink while waiting");
        }
        else if (p.LastRefreshmentAt is not null)
        {
            good.Add("food");
        }

        if (p.HadToiletAccident)
        {
            score -= 1;
            bad.Add("accident");
            facts.Add("Had an accident: couldn't get to a toilet");
        }

        if (p.OperationCancelledWhy is { } why)
        {
            score -= 1.5;
            bad.Add("cancelled");
            facts.Add($"Operation cancelled on the day: {why}");
        }

        if (p.SurgeryBreached)
        {
            score -= 0.5;
            bad.Add("op_late");
            facts.Add("Operation later than the target for its urgency");
        }

        if (p.Complication is { } complication)
        {
            score -= 0.5;
            bad.Add("complication");
            facts.Add($"Complication after surgery: {complication.ToLowerInvariant()}");
        }

        if (p.LengthOfStay(Now) is { } stay && stay > TimeSpan.FromDays(2) && p.VisitsReceived == 0 && p.Happiness < 0.5)
        {
            score -= 0.5;
            bad.Add("lonely");
            facts.Add($"No visitors in {stay.TotalDays:0} days on the ward");
        }
        else if (p.VisitsReceived >= 2)
        {
            score += 0.2;
            good.Add("visitors");
        }

        if (p.Happiness >= 0.75)
        {
            score += 0.4;
            good.Add("cheerful");
        }
        else if (p.Happiness < 0.4)
        {
            score -= 0.4;
        }

        if (outcome == PatientOutcome.LeftWithoutBeingSeen)
        {
            score = Math.Min(score, 2.2);
            bad.Insert(0, "unseen");
            facts.Insert(0, $"Left without being seen after {FormatWait(inEmergency)}");
        }

        score += luck;
        var stars = (int)Math.Clamp(Math.Round(score), 1, 5);
        if (stars >= 4)
        {
            good.Insert(0, "caring");
        }

        return (stars, stars >= 4 ? good.Concat(bad.Take(1)).ToList() : bad.Concat(good.Take(stars >= 3 ? 1 : 0)).ToList(), facts);
    }

    /// <summary>Where a patient rates: A&amp;E, the ward's department, maternity or surgery.</summary>
    private string FeedbackDepartmentOf(Patient p)
    {
        if (p.Maternity)
        {
            return FeedbackKeys.MaternityDepartment;
        }

        if (p.AdmittedAt is not null && p.CareSpaceId is { } id && FindSpace(id) is { IsWard: true } ward)
        {
            return ward.DepartmentId;
        }

        return EmergencyDepartmentLayout.Department;
    }

    /// <summary>A patient leaving rates their stay; some complain and some send a thank-you.</summary>
    private void RateStay(Patient p, PatientOutcome outcome)
    {
        if (!FeedbackOn || outcome == PatientOutcome.Died || p.Stars is not null || p.AgeAt(Now) < 1)
        {
            return;
        }

        var random = FeedbackRandom;
        var (stars, reasons, facts) = JudgeStay(p, outcome, random.Range(-0.6, 0.6));
        var department = FeedbackDepartmentOf(p);
        p.Stars = stars;
        RecordRating(stars, reasons, department);
        var quote = CommentFor(stars, reasons, random);
        AddComment(new FeedbackComment(p.Id, p.FullName, FeedbackDepartmentName(department), stars, quote, Now));
        p.Log(Now, $"Rated their stay {stars} out of 5" + (reasons.Count > 0 ? $": {string.Join(", ", reasons.Take(3).Select(r => ReasonById(r).Label.ToLowerInvariant()))}." : "."));

        if (stars <= 2 && random.Chance(stars == 1 ? 0.55 : 0.25))
        {
            var primary = reasons.FirstOrDefault(r => !ReasonById(r).Good) ?? "wait";
            var severity = stars == 1 || primary is "cancelled" or "accident" || p.TimeInEmergency(Now) > TimeSpan.FromHours(12)
                ? ComplaintSeverity.Formal
                : ComplaintSeverity.Concern;
            AddComplaint(p, $"{p.FullName}, {p.AgeAt(Now)}", FeedbackDepartmentName(department), severity, ComplaintQuote(primary, p, random), facts, CauseOf(primary, p));
        }
        else if (stars == 5 && random.Chance(0.12))
        {
            var thanks = new ThankYou(_nextFeedbackId++, p.Id, $"{p.FullName}, {p.AgeAt(Now)}", FeedbackDepartmentName(department), Now, ThankYouQuote(p, department, random));
            _thankYous.Insert(0, thanks);
            Trim(_thankYous, ClosedKept);
            Tally(FeedbackKeys.ThankYous);
            NudgeReputation(0.01, "Thank-you card");
            Notifications.Add(Now, NotificationLevel.Info, $"Thank-you card from {p.FullName}: \"{thanks.Quote}\"", p.Id);
        }
    }

    /// <summary>An outpatient rates their clinic visit as they leave: mostly on how long they were kept waiting.</summary>
    private void RateClinicVisit(Appointment appointment)
    {
        if (!FeedbackOn)
        {
            return;
        }

        var random = FeedbackRandom;
        var due = appointment.CheckedInAt is { } checkedIn && checkedIn > appointment.Time ? checkedIn : appointment.Time;
        var waited = (appointment.CalledInAt ?? Now) - due;
        var score = 4.7 + random.Range(-0.6, 0.6);
        var reasons = new List<string>();
        if (waited > TimeSpan.FromMinutes(30))
        {
            score -= waited > TimeSpan.FromMinutes(90) ? 2 : 1;
            reasons.Add("clinic_wait");
        }
        else if (waited < TimeSpan.FromMinutes(10))
        {
            reasons.Add("quick");
        }

        var stars = (int)Math.Clamp(Math.Round(score), 1, 5);
        if (stars >= 4)
        {
            reasons.Insert(0, "caring");
        }

        RecordRating(stars, reasons, FeedbackKeys.ClinicsDepartment);
        AddComment(new FeedbackComment(null, appointment.FullName, "Clinics", stars, CommentFor(stars, reasons, random), Now));
    }

    private void RecordRating(int stars, IEnumerable<string> reasons, string department)
    {
        Tally(FeedbackKeys.Ratings);
        Tally(FeedbackKeys.StarTotal, stars);
        Tally(FeedbackKeys.Stars(stars));
        if (stars >= 4)
        {
            Tally(FeedbackKeys.Recommend);
        }

        foreach (var reason in reasons)
        {
            Tally(FeedbackKeys.Reason(reason));
        }

        Tally(FeedbackKeys.DepartmentCount(department));
        Tally(FeedbackKeys.DepartmentStars(department), stars);
    }

    private void AddComment(FeedbackComment comment)
    {
        _comments.Insert(0, comment);
        Trim(_comments, CommentsKept);
    }

    private static void Trim<T>(List<T> list, int keep)
    {
        if (list.Count > keep)
        {
            list.RemoveRange(keep, list.Count - keep);
        }
    }

    // ---------------------------------------------------------------- complaints

    private Complaint? AddComplaint(Patient? p, string from, string department, ComplaintSeverity severity, string quote, List<string> facts, string cause)
    {
        if (_complaints.Count(c => c.IsOpen) >= OpenComplaintsKept)
        {
            return null;
        }

        var complaint = new Complaint
        {
            Id = _nextFeedbackId++,
            PatientId = p?.Id,
            From = from,
            Department = department,
            Severity = severity,
            ReceivedAt = Now,
            DueAt = Now + ReplyWithin(severity),
            Quote = quote,
            Facts = facts.Take(4).ToList(),
            Cause = cause,
        };
        _complaints.Insert(0, complaint);
        Tally(FeedbackKeys.Complaints);
        p?.Log(Now, $"Made a {SeverityName(severity).ToLowerInvariant()}: \"{quote}\"", important: true);
        Notifications.Add(Now, severity == ComplaintSeverity.Serious ? NotificationLevel.Critical : NotificationLevel.Warning,
            $"New {SeverityName(severity).ToLowerInvariant()} from {from}. Reply by {complaint.DueAt:ddd HH:mm} (Feedback, L).", p?.Id);
        return complaint;
    }

    public static TimeSpan ReplyWithin(ComplaintSeverity severity) => severity switch
    {
        ComplaintSeverity.Concern => TimeSpan.FromDays(1),
        ComplaintSeverity.Serious => TimeSpan.FromDays(2),
        _ => TimeSpan.FromDays(3),
    };

    public static string SeverityName(ComplaintSeverity severity) => severity switch
    {
        ComplaintSeverity.Concern => "Concern",
        ComplaintSeverity.Serious => "Serious incident",
        _ => "Formal complaint",
    };

    /// <summary>A missed diagnosis that came back: the patient's family complain.</summary>
    private void ComplainAboutMissedDiagnosis(Patient p, bool serious)
    {
        if (!FeedbackOn || (!serious && !FeedbackRandom.Chance(0.4)))
        {
            return;
        }

        var facts = new List<string>
        {
            $"Discharged at {p.LeftAt:HH:mm} as {(p.WorkingDiagnosis is { } d && Database.Conditions.TryGetValue(d.ConditionId, out var c) ? c.Name.ToLowerInvariant() : "unknown")}",
            $"Actually had {p.Condition.Name.ToLowerInvariant()}",
        };
        var from = serious ? $"Family of {p.FullName}, {p.AgeAt(Now)}" : $"{p.FullName}, {p.AgeAt(Now)}";
        var quote = serious
            ? $"You sent them home and they came back with {LowerFirst(p.Condition.Name)}. We want to know how this happened."
            : $"I was told it was nothing, but it turned out to be {LowerFirst(p.Condition.Name)}.";
        AddComplaint(p, from, "A&E", serious ? ComplaintSeverity.Serious : ComplaintSeverity.Formal, quote, facts,
            "The doctor discharged them without the tests that would have shown it. A senior review before discharge (and more experienced doctors) would have caught it.");
    }

    /// <summary>A patient who died after a long wait or time in the corridor: the family may complain.</summary>
    private void MaybeFamilyComplaint(Patient p)
    {
        if (!FeedbackOn)
        {
            return;
        }

        var inEmergency = p.TimeInEmergency(Now);
        var waitedLong = !p.Condition.IsPlanned && inEmergency > TimeSpan.FromHours(4);
        if (!(waitedLong || p.CorridorMinutes >= 30) || !FeedbackRandom.Chance(0.5))
        {
            return;
        }

        var facts = new List<string>();
        if (waitedLong)
        {
            facts.Add($"{FormatWait(inEmergency)} in A&E before they died");
        }

        if (p.CorridorMinutes >= 30)
        {
            facts.Add($"{FormatWait(TimeSpan.FromMinutes(p.CorridorMinutes))} of corridor care");
        }

        facts.Add($"Underlying condition: {p.Condition.Name.ToLowerInvariant()}");
        AddComplaint(p, $"Family of {p.FullName}, {p.AgeAt(Now)}", FeedbackDepartmentName(FeedbackDepartmentOf(p)), ComplaintSeverity.Serious,
            "They were left waiting when they needed help. We want a full explanation.", facts, CauseOf(waitedLong ? "wait" : "corridor", p));
    }

    /// <summary>What an investigation would find went wrong, judged from the hospital as it is when the complaint arrives.</summary>
    private string CauseOf(string reason, Patient p)
    {
        var wardBedsFree = Wards.Sum(w => w.Places - w.PlacesUsed);
        var doctors = _staff.Count(s => s.Role == StaffRole.Doctor && s.IsOnDuty);
        var waiting = _patients.Values.Count(x => x.IsInEmergency);
        var hasCafe = SpacesOfKind(CareSpaceKind.Cafeteria).Any() || SpacesOfKind(CareSpaceKind.VendingPoint).Any();
        return reason switch
        {
            "wait" or "doctor_wait" or "unseen" when doctors * 4 < waiting => $"Not enough doctors in A&E for the number of patients ({doctors} on shift for {waiting} in the department). Hire more doctors.",
            "wait" or "corridor" or "bed_wait" when wardBedsFree <= 1 => "The wards were full, so A&E couldn't move admitted patients up. Build more ward beds or discharge sooner.",
            "wait" or "doctor_wait" or "unseen" => "A&E cubicles were full at busy times, so patients queued. Build more cubicles or open a minors room.",
            "corridor" => "Every cubicle was full when the ambulance arrived. Build more majors or resus cubicles.",
            "bed_wait" => "Ward beds were slow to free up. Discharge earlier in the day or add beds.",
            "no_drink" when !hasCafe => "There is no cafe or vending point for people waiting. Build one.",
            "no_drink" => "Patients on trolleys couldn't get to the cafe. Put a vending point near A&E.",
            "accident" when !HasPublicToilets => "The hospital has no public toilets. Build some near the waiting room.",
            "accident" => "The toilets were too busy or too far away. Build more near where people wait.",
            "cancelled" => $"The list ran out of time or room: {p.OperationCancelledWhy ?? "emergencies came first"}. Add theatre time or a recovery room.",
            "op_late" => "Theatres were too busy to operate in time. Add theatre sessions or surgeons.",
            "complication" => "Complications are more likely with less experienced surgeons. Hire senior surgeons.",
            "lonely" => "Visiting hours on their ward are short. Lengthen them in the room panel.",
            _ => "Staff were stretched at the time. Check the Staff rota (T) for gaps.",
        };
    }

    private static string ComplaintQuote(string reason, Patient p, GameRandom random)
    {
        var hours = p.TimeInEmergency(p.LeftAt ?? p.ArrivedAt).TotalHours;
        string[] options = reason switch
        {
            "wait" => new[] { $"I waited nearly {Math.Ceiling(hours):0} hours in A&E. Nobody told me what was happening.", $"{Math.Ceiling(hours):0} hours on a hard chair. It isn't good enough.", "Hours and hours waiting, and I was in a lot of pain." },
            "doctor_wait" => new[] { "It took hours before a doctor even looked at me.", "I sat there for ages without seeing a doctor." },
            "corridor" => new[] { "I was left on a trolley in a corridor with people walking past.", "No privacy, no dignity: I was treated in the corridor." },
            "bed_wait" => new[] { "I was told I needed a bed, then waited hours for one.", "They said I was being admitted, then I sat in A&E for half the night." },
            "no_drink" => new[] { "Nobody offered me a drink in all that time.", "I was there for hours with nothing to eat or drink." },
            "accident" => new[] { "I couldn't find a toilet in time. It was humiliating.", "There was nowhere to go to the toilet and I had an accident." },
            "cancelled" => new[] { "My operation was cancelled on the day, after I'd fasted all morning.", "I'd taken time off work and arranged care, and then they cancelled my operation." },
            "op_late" => new[] { "I waited far too long for my operation.", "They kept putting my operation back." },
            "complication" => new[] { "Something went wrong in my operation and nobody explained it properly." },
            "lonely" => new[] { "I was on the ward for days and hardly anyone came to see me. I felt forgotten." },
            "unseen" => new[] { "I gave up and went home. I never even saw a doctor.", "After all that waiting I left without being seen." },
            _ => new[] { "I wasn't happy with how I was looked after." },
        };
        return random.Pick(options);
    }

    private string ThankYouQuote(Patient p, string department, GameRandom random)
    {
        var where = department == EmergencyDepartmentLayout.Department ? "A&E" : p.CareSpaceId is { } id && FindSpace(id) is { IsWard: true } ward ? ward.Name : FeedbackDepartmentName(department);
        return random.Pick(new[]
        {
            $"Thank you to everyone in {where}. You were wonderful.",
            $"The team in {where} were so kind. Thank you for looking after me.",
            $"I can't thank the staff in {where} enough.",
            $"Thank you {where}: you made a scary day so much easier.",
        });
    }

    private static string CommentFor(int stars, IReadOnlyList<string> reasons, GameRandom random)
    {
        var first = reasons.Count > 0 ? reasons[0] : stars >= 4 ? "caring" : "wait";
        return (stars, first) switch
        {
            (>= 4, "quick") => random.Pick(new[] { "Seen really quickly. Thank you.", "In and out in no time, and everyone was lovely." }),
            (>= 4, _) => random.Pick(new[] { "The nurses were wonderful.", "Everyone was kind and explained everything.", "Brilliant care from start to finish.", "Busy, but the staff were lovely." }),
            (3, _) => random.Pick(new[] { "Good care, but a long wait.", "Staff were nice, it was just very busy.", "Okay. Could have been quicker." }),
            (_, "wait") => random.Pick(new[] { "Far too long a wait.", "Hours on a plastic chair.", "Waited forever and nobody told me anything." }),
            (_, "corridor") => "Left on a trolley in the corridor.",
            (_, "accident") => "Couldn't get to a toilet. Humiliating.",
            (_, "cancelled") => "Operation cancelled on the day. Furious.",
            (_, "unseen") => "Gave up waiting and went home.",
            (_, "clinic_wait") => "Kept waiting an hour past my appointment.",
            (_, "no_drink") => "Nobody offered me a drink.",
            _ => random.Pick(new[] { "Not a good experience.", "Disappointed with my care." }),
        };
    }

    // ---------------------------------------------------------------- answering

    /// <summary>The replies the player can give to a complaint, with costs and whether each can be used now.</summary>
    public IReadOnlyList<ComplaintOption> OptionsFor(Complaint complaint)
    {
        bool Can(ComplaintResponse r) => complaint.Status == ComplaintStatus.Open && !complaint.Tried.Contains(r);
        string? Why(ComplaintResponse r) => complaint.Tried.Contains(r) ? "Already tried" : complaint.Status == ComplaintStatus.Waiting ? "Waiting for the last reply" : !complaint.IsOpen ? "Closed" : null;
        var senior = SeniorDoctor();
        var goodwill = GoodwillCost(complaint.Severity);
        return new[]
        {
            new ComplaintOption(ComplaintResponse.Apologise, "Apologise", "free", 0, Can(ComplaintResponse.Apologise), Why(ComplaintResponse.Apologise)),
            new ComplaintOption(ComplaintResponse.Meet, "Meet them", senior is null ? "needs a consultant or registrar" : $"{senior.Grade} · 1 hour", 0,
                Can(ComplaintResponse.Meet) && senior is not null, Why(ComplaintResponse.Meet) ?? (senior is null ? "Hire a consultant or registrar first" : null)),
            new ComplaintOption(ComplaintResponse.Goodwill, "Goodwill gesture", $"£{goodwill:N0}", goodwill, Can(ComplaintResponse.Goodwill) && CanAfford(goodwill), Why(ComplaintResponse.Goodwill) ?? (CanAfford(goodwill) ? null : "Not enough money")),
            new ComplaintOption(ComplaintResponse.Investigate, "Investigate", $"£{InvestigationCost:N0} · 1 day", InvestigationCost,
                Can(ComplaintResponse.Investigate) && CanAfford(InvestigationCost), Why(ComplaintResponse.Investigate) ?? (CanAfford(InvestigationCost) ? null : "Not enough money")),
        };
    }

    public static long GoodwillCost(ComplaintSeverity severity) => severity switch
    {
        ComplaintSeverity.Concern => 150,
        ComplaintSeverity.Serious => 1_500,
        _ => 400,
    };

    private StaffMember? SeniorDoctor() =>
        _staff.Where(s => s.Role == StaffRole.Doctor && s.Grade is "Consultant" or "Registrar")
            .OrderByDescending(s => s.Grade == "Consultant").ThenBy(s => s.Id).FirstOrDefault();

    /// <summary>The player answers a complaint. False when that reply can't be used now.</summary>
    public bool Respond(int complaintId, ComplaintResponse response)
    {
        var complaint = _complaints.FirstOrDefault(c => c.Id == complaintId);
        if (complaint is null || OptionsFor(complaint).FirstOrDefault(o => o.Response == response) is not { Enabled: true } option)
        {
            return false;
        }

        complaint.Tried.Add(response);
        if (option.Cost > 0)
        {
            Book(MoneyCategory.Complaints, -option.Cost);
            complaint.Spent += option.Cost;
        }

        switch (response)
        {
            case ComplaintResponse.Meet:
                complaint.Status = ComplaintStatus.Waiting;
                complaint.Pending = response;
                complaint.PendingUntil = Now + MeetingTakes;
                complaint.Outcome = $"{SeniorDoctor()!.Name} is meeting them.";
                break;
            case ComplaintResponse.Investigate:
                complaint.Status = ComplaintStatus.Waiting;
                complaint.Pending = response;
                complaint.PendingUntil = Now + InvestigationTakes;
                complaint.Outcome = $"Investigating: report due {complaint.PendingUntil:ddd HH:mm}.";
                break;
            default:
                Settle(complaint, response);
                break;
        }

        return true;
    }

    /// <summary>The chance a reply satisfies the complainant, by how serious it is.</summary>
    public static double SatisfyChance(ComplaintResponse response, ComplaintSeverity severity) => (response, severity) switch
    {
        (ComplaintResponse.Apologise, ComplaintSeverity.Concern) => 0.85,
        (ComplaintResponse.Apologise, ComplaintSeverity.Formal) => 0.45,
        (ComplaintResponse.Apologise, _) => 0.15,
        (ComplaintResponse.Meet, ComplaintSeverity.Concern) => 0.95,
        (ComplaintResponse.Meet, ComplaintSeverity.Formal) => 0.75,
        (ComplaintResponse.Meet, _) => 0.5,
        (ComplaintResponse.Goodwill, ComplaintSeverity.Concern) => 0.9,
        (ComplaintResponse.Goodwill, ComplaintSeverity.Formal) => 0.8,
        (ComplaintResponse.Goodwill, _) => 0.4,
        (ComplaintResponse.Investigate, ComplaintSeverity.Concern) => 0.95,
        (ComplaintResponse.Investigate, ComplaintSeverity.Formal) => 0.9,
        _ => 0.85,
    };

    private void Settle(Complaint complaint, ComplaintResponse response)
    {
        complaint.Pending = null;
        complaint.PendingUntil = null;
        if (response == ComplaintResponse.Investigate)
        {
            complaint.CauseFound = true;
            Notifications.Add(Now, NotificationLevel.Info, $"Investigation into {complaint.From}'s complaint: {complaint.Cause}", complaint.PatientId);
        }

        var what = response switch
        {
            ComplaintResponse.Apologise => "the apology",
            ComplaintResponse.Meet => "the meeting",
            ComplaintResponse.Goodwill => "the goodwill gesture",
            _ => "the investigation",
        };
        if (FeedbackRandom.Chance(SatisfyChance(response, complaint.Severity)))
        {
            complaint.Status = ComplaintStatus.Resolved;
            complaint.ClosedAt = Now;
            complaint.Outcome = $"Satisfied with {what}.";
            Tally(FeedbackKeys.ComplaintsResolved);
            NudgeReputation(complaint.Severity switch { ComplaintSeverity.Concern => 0.02, ComplaintSeverity.Formal => 0.05, _ => 0.08 }, $"Complaint resolved: {complaint.From}");
            TrimClosed();
            return;
        }

        complaint.Status = ComplaintStatus.Open;
        var extra = Now + TimeSpan.FromDays(1);
        if (complaint.DueAt < extra)
        {
            complaint.DueAt = extra;
        }

        complaint.Outcome = complaint.Tried.Count >= Enum.GetValues<ComplaintResponse>().Length
            ? $"Still not satisfied after {what}. Nothing more to offer: it will go to the Ombudsman."
            : $"Not satisfied with {what}. They want more.";
    }

    private void NudgeReputation(double amount, string why)
    {
        _reputation.Add(new ReputationChange(Now, amount, why));
    }

    private void TrimClosed()
    {
        var closed = _complaints.Where(c => !c.IsOpen).OrderByDescending(c => c.ClosedAt).Skip(ClosedKept).ToList();
        foreach (var old in closed)
        {
            _complaints.Remove(old);
        }
    }

    /// <summary>Meetings and investigations finish, and complaints past their deadline go further.</summary>
    private void UpdateFeedback()
    {
        if (_complaints.Count == 0 && _reputation.Count == 0)
        {
            return;
        }

        foreach (var complaint in _complaints.Where(c => c.Status == ComplaintStatus.Waiting && c.PendingUntil <= Now).ToList())
        {
            Settle(complaint, complaint.Pending ?? ComplaintResponse.Apologise);
        }

        foreach (var complaint in _complaints.Where(c => c.Status == ComplaintStatus.Open && c.DueAt <= Now).ToList())
        {
            if (complaint.Severity == ComplaintSeverity.Concern)
            {
                complaint.Severity = ComplaintSeverity.Formal;
                complaint.DueAt = Now + ReplyWithin(ComplaintSeverity.Formal);
                complaint.Outcome = "No reply in time, so it is now a formal complaint.";
                Notifications.Add(Now, NotificationLevel.Warning, $"{complaint.From}'s concern had no reply and is now a formal complaint. Reply by {complaint.DueAt:ddd HH:mm}.", complaint.PatientId);
                continue;
            }

            var fine = OmbudsmanFine(complaint.Severity);
            Book(MoneyCategory.Complaints, -fine);
            complaint.Spent += fine;
            complaint.Status = ComplaintStatus.Ombudsman;
            complaint.ClosedAt = Now;
            complaint.Outcome = $"Went to the Ombudsman, who upheld it: £{fine:N0} fine.";
            Tally(FeedbackKeys.Ombudsman);
            NudgeReputation(complaint.Severity == ComplaintSeverity.Serious ? -0.5 : -0.25, $"Ombudsman: {complaint.From}");
            Notifications.Add(Now, NotificationLevel.Critical, $"The Ombudsman upheld {complaint.From}'s complaint: £{fine:N0} fine and the hospital's rating drops.", complaint.PatientId);
            TrimClosed();
        }

        _reputation.RemoveAll(r => Now - r.At >= ReputationFades);
    }

    public static long OmbudsmanFine(ComplaintSeverity severity) => severity == ComplaintSeverity.Serious ? 10_000 : 5_000;

    // ---------------------------------------------------------------- saving

    private FeedbackState CaptureFeedback() => new()
    {
        Complaints = _complaints.ToList(),
        ThankYous = _thankYous.ToList(),
        Comments = _comments.ToList(),
        Changes = _reputation.ToList(),
        NextId = _nextFeedbackId,
    };

    private void RestoreFeedback(FeedbackState? state)
    {
        _complaints.Clear();
        _thankYous.Clear();
        _comments.Clear();
        _reputation.Clear();
        _nextFeedbackId = 1;
        if (state is null)
        {
            return;
        }

        _complaints.AddRange(state.Complaints);
        _thankYous.AddRange(state.ThankYous);
        _comments.AddRange(state.Comments);
        _reputation.AddRange(state.Changes);
        _nextFeedbackId = Math.Max(state.NextId, _complaints.Select(c => c.Id).Concat(_thankYous.Select(t => t.Id)).DefaultIfEmpty(0).Max() + 1);
    }

    /// <summary>For tests and the cheat console: a complaint from a made-up patient.</summary>
    public Complaint? AddTestComplaint(ComplaintSeverity severity = ComplaintSeverity.Formal) =>
        AddComplaint(null, "Margaret Hughes, 78", "A&E", severity, "I waited nearly ten hours for a bed, three of them on a trolley in the corridor.",
            new List<string> { "9 h 40 min in A&E (4-hour target missed)", "3 h 5 min of corridor care" }, "The wards were full, so A&E couldn't move admitted patients up. Build more ward beds or discharge sooner.");
}

/// <summary>Names of the feedback figures kept in the stats history.</summary>
public static class FeedbackKeys
{
    public const string Ratings = "feedback.ratings";
    public const string StarTotal = "feedback.stars";
    public const string Recommend = "feedback.recommend";
    public const string Complaints = "feedback.complaints";
    public const string ComplaintsResolved = "feedback.complaints_resolved";
    public const string Ombudsman = "feedback.ombudsman";
    public const string ThankYous = "feedback.thank_yous";
    public const string DepartmentPrefix = "feedback.dept_count.";
    public const string ClinicsDepartment = "clinics";
    public const string MaternityDepartment = "maternity_unit";

    public static string Stars(int stars) => $"feedback.stars_{stars}";

    public static string Reason(string id) => $"feedback.reason.{id}";

    public static string DepartmentCount(string department) => DepartmentPrefix + department;

    public static string DepartmentStars(string department) => $"feedback.dept_stars.{department}";
}
