using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>What sort of thing is on a patient's mind; it sets the colour of their thought bubble.</summary>
public enum ThoughtKind
{
    /// <summary>What they are up to: having an X-ray, getting a drink, playing.</summary>
    Doing,

    /// <summary>Something they want: a drink, the toilet, something for the pain, to go home.</summary>
    Wanting,

    /// <summary>Just thinking: how much longer, what's for lunch.</summary>
    Thinking,

    /// <summary>Pleased: feeling better, family visiting, going home happy.</summary>
    Happy,

    /// <summary>Fed up: a long wait, stuck in the corridor. These are the people who complain.</summary>
    Unhappy,
}

/// <summary>A short line for a thought bubble over someone's head.</summary>
public sealed record Thought(ThoughtKind Kind, string Text);

/// <summary>
/// What patients are thinking, doing and wanting, read from what is happening to them, for the thought
/// bubbles over their heads. And the PALS office: patients who have waited a long time and are fed up walk
/// there now and then when a PALS officer is on duty, are listened to, and leave happier: they rate their
/// stay higher and are much less likely to complain.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>A waiting patient this fed up (waiting this long) may go to PALS.</summary>
    public static readonly TimeSpan PalsAfterWaiting = TimeSpan.FromHours(2);

    /// <summary>On average an eligible fed-up patient goes to PALS about once in this many minutes.</summary>
    private const double PalsEveryMinutes = 40;

    /// <summary>How many people a PALS office takes at once (talking, or waiting just inside).</summary>
    private const int PalsAtOnce = 2;

    /// <summary>Written apologies are this much more likely to settle a complaint with a PALS officer on staff.</summary>
    public const double PalsApologyBonus = 0.15;

    private readonly Dictionary<Agent, string> _palsVisits = new();

    public IEnumerable<CareSpace> PalsOffices => SpacesOfKind(CareSpaceKind.PalsOffice);

    /// <summary>A PALS officer is on duty in this office now.</summary>
    public bool IsPalsOpen(CareSpace office) =>
        office.InUse && _staff.Any(s => s.BaseRoomId == office.Id && s.Role == StaffRole.PalsOfficer && s.IsOnDuty && !s.IsOnBreak);

    /// <summary>A PALS officer works for the hospital at all (for answering complaints).</summary>
    public bool HasPalsOfficer => _staff.Any(s => s.Role == StaffRole.PalsOfficer);

    /// <summary>People at (or on their way to) a PALS office right now.</summary>
    public IEnumerable<Agent> PalsVisitors(CareSpace office) => _palsVisits.Where(v => v.Value == office.Id).Select(v => v.Key);

    /// <summary>Whether someone is on their way to, or talking at, PALS.</summary>
    public bool AtPals(Agent person) => _palsVisits.ContainsKey(person);

    // ---------------------------------------------------------------- PALS visits

    /// <summary>Fed-up waiting patients set off for a staffed PALS office now and then; visits that end are put away.</summary>
    private void UpdatePals(double dt)
    {
        foreach (var (person, _) in _palsVisits.ToList())
        {
            if (!_strolls.TryGetValue(person, out var stroll) || stroll.Purpose != StrollPurpose.Pals || stroll.Stage == StrollStage.Back)
            {
                _palsVisits.Remove(person);
                foreach (var officer in _staff.Where(s => s.Role == StaffRole.PalsOfficer && s.StatusText.EndsWith(FullNameOf(person), StringComparison.Ordinal)))
                {
                    officer.StatusText = "Available";
                }
            }
        }

        if (!_options.LivelyPeople || !FeedbackOn)
        {
            return;
        }

        var open = PalsOffices.Where(IsPalsOpen).Where(o => PalsVisitors(o).Count() < PalsAtOnce).ToList();
        if (open.Count == 0)
        {
            return;
        }

        var chance = dt / (PalsEveryMinutes * 60);
        foreach (var patient in _patients.Values.Where(CanGoToPals).ToList())
        {
            if (!StrollRandom.Chance(chance))
            {
                continue;
            }

            var office = open.Where(o => Site.FloorOf(o.StaffSpot) == Site.FloorOf(patient.Tile)).OrderBy(o => Chebyshev(o.StaffSpot, patient.Tile)).FirstOrDefault()
                         ?? open.OrderBy(o => Chebyshev(o.StaffSpot, patient.Tile)).First();
            if (PalsSpot(office, patient) is not { } spot)
            {
                continue;
            }

            _strolls[patient] = new Stroll { Person = patient, To = spot, Home = patient.Seat ?? patient.Tile, Purpose = StrollPurpose.Pals };
            _lastStrollAt[patient] = Now;
            _palsVisits[patient] = office.Id;
            StrollMove(patient, spot);
            patient.Log(Now, $"Went to the PALS office to raise a concern after waiting {FormatWait(Now - patient.ArrivedAt)}.");
            open.RemoveAll(o => PalsVisitors(o).Count() >= PalsAtOnce);
            if (open.Count == 0)
            {
                return;
            }
        }
    }

    /// <summary>A walking waiting A&amp;E patient who has waited a long time, hasn't been to PALS yet and could get up for a walk.</summary>
    private bool CanGoToPals(Patient p) =>
        !p.PalsListened && Now - p.ArrivedAt >= PalsAfterWaiting && CanStrollIgnoringLastWalk(p);

    private bool CanStrollIgnoringLastWalk(Patient p) =>
        p.Stage is PatientStage.WaitingForTriage or PatientStage.WaitingForSpace
        && p.Triage != TriageCategory.Red
        && p.Mobility == Mobility.Walking
        && (p.Seat is not { } seat || p.Tile == seat) && !p.IsMoving
        && p.BusyWithTaskId is null && p.CareSpaceId is null && p.PendingMove is null
        && !Away(p) && !_strolls.ContainsKey(p);

    /// <summary>A free floor tile in the office near the PALS officer's desk.</summary>
    private GridCoord? PalsSpot(CareSpace office, Agent person)
    {
        var taken = TakenSpots(person);
        taken.UnionWith(PalsVisitors(office).Select(v => _strolls.TryGetValue(v, out var s) ? s.To : v.Tile));
        return office.Rect.Tiles()
            .Where(t => !office.Rect.IsOnEdge(t) && Nav.IsWalkable(t) && t != office.StaffSpot && !taken.Contains(t))
            .OrderBy(t => Chebyshev(t, office.StaffSpot))
            .ThenBy(t => t.X).ThenBy(t => t.Y)
            .Cast<GridCoord?>()
            .FirstOrDefault();
    }

    private static string FullNameOf(Agent person) => person is Patient p ? p.FullName : "\u0000";

    /// <summary>How long a talk at PALS takes.</summary>
    private TimeSpan PalsTalk() => TimeSpan.FromMinutes(StrollRandom.Range(8, 18));

    /// <summary>The talk is over: they feel listened to.</summary>
    private void FinishPalsTalk(Agent person)
    {
        if (person is not Patient p)
        {
            return;
        }

        var officer = _palsVisits.TryGetValue(p, out var officeId)
            ? _staff.FirstOrDefault(s => s.BaseRoomId == officeId && s.Role == StaffRole.PalsOfficer && s.IsOnDuty)
            : null;
        p.PalsListened = true;
        p.Happiness = Math.Min(1, p.Happiness + 0.25);
        p.Log(Now, $"{(officer is null ? "PALS" : officer.Name + " from PALS")} listened to their concerns and explained what was happening.");
        Tally(FeedbackKeys.PalsListened);
        if (officer is not null && officer.StatusText.StartsWith("Listening to", StringComparison.Ordinal))
        {
            officer.StatusText = "Available";
        }
    }

    /// <summary>The PALS officer is listening to someone in their office.</summary>
    private void StartPalsTalk(Agent person)
    {
        if (person is Patient p && _palsVisits.TryGetValue(p, out var officeId)
            && _staff.FirstOrDefault(s => s.BaseRoomId == officeId && s.Role == StaffRole.PalsOfficer && s.IsOnDuty) is { } officer)
        {
            officer.StatusText = $"Listening to {p.FullName}";
        }
    }

    // ---------------------------------------------------------------- thoughts

    /// <summary>What a patient is thinking, doing or wanting right now, or null when there is nothing to show (asleep under anaesthetic, in cardiac arrest, gone).</summary>
    public Thought? ThoughtOf(Patient p)
    {
        if (!p.IsAlive || p.InArrest || p.Stage == PatientStage.Gone)
        {
            return null;
        }

        if (p.Stage == PatientStage.Leaving)
        {
            return p.Stars switch
            {
                >= 4 => new Thought(ThoughtKind.Happy, "Going home!"),
                <= 2 => new Thought(ThoughtKind.Unhappy, "Never again..."),
                _ => new Thought(ThoughtKind.Thinking, "Home at last"),
            };
        }

        if (_strolls.TryGetValue(p, out var stroll))
        {
            return stroll.Purpose == StrollPurpose.Pals
                ? new Thought(ThoughtKind.Doing, stroll.Stage == StrollStage.Pausing ? "Telling PALS" : "Off to PALS")
                : new Thought(ThoughtKind.Doing, stroll.FromBed ? "A walk round the ward" : "Stretching my legs");
        }

        if (_trips.ContainsKey(p))
        {
            return new Thought(ThoughtKind.Doing, "Getting a drink");
        }

        if (_toiletTrips.ContainsKey(p))
        {
            return new Thought(ThoughtKind.Doing, "Off to the loo");
        }

        if (_playVisits.TryGetValue(p, out var play))
        {
            return new Thought(ThoughtKind.Happy, play.Playing ? (p.Age >= TeenRoomAge ? "Gaming!" : "Playing!") : "Going to play");
        }

        if (p.OnFloorAfterFall)
        {
            return new Thought(ThoughtKind.Wanting, "Help!");
        }

        if (p.BusyWithTaskId is { } taskId && _tasks.TryGetValue(taskId, out var task))
        {
            return new Thought(ThoughtKind.Doing, Shorten(task.Label));
        }

        var obs = p.LatestObservations?.Obs;
        var inPain = obs is { PainScore: >= 6 } && p.Analgesia < 0.3;
        var unwell = p.MonitorRed || p.Triage == TriageCategory.Red;

        if (p.CorridorSpot is not null)
        {
            return inPain ? new Thought(ThoughtKind.Wanting, "Something for the pain") : new Thought(ThoughtKind.Unhappy, "Stuck in a corridor");
        }

        if (p.Stage == PatientStage.OnWard)
        {
            return WardThought(p, inPain, unwell);
        }

        if (!p.IsInEmergency)
        {
            return null;
        }

        if (unwell)
        {
            return new Thought(ThoughtKind.Thinking, p.Age < 12 ? "I want Mummy" : "I feel awful");
        }

        if (inPain)
        {
            return new Thought(ThoughtKind.Wanting, "Something for the pain");
        }

        var waited = Now - p.ArrivedAt;
        if (p.BedRequestedAt is not null)
        {
            return waited > TimeSpan.FromHours(4) ? new Thought(ThoughtKind.Unhappy, "Still no bed?") : new Thought(ThoughtKind.Wanting, "A bed on the ward");
        }

        if (waited > TimeSpan.FromHours(4))
        {
            return new Thought(ThoughtKind.Unhappy, $"{(int)waited.TotalHours} hours!");
        }

        if (waited > TimeSpan.FromHours(2) && (p.LastRefreshmentAt is not { } drank || Now - drank > TimeSpan.FromHours(2)))
        {
            return new Thought(ThoughtKind.Wanting, Pick(p, "A cup of tea", "Something to eat", "A drink"));
        }

        if (p.PalsListened)
        {
            return new Thought(ThoughtKind.Thinking, "At least someone listened");
        }

        if (p.FirstSeenByDoctorAt is null && waited > TimeSpan.FromHours(1))
        {
            return new Thought(ThoughtKind.Thinking, "When will I see a doctor?");
        }

        if (p.CareSpaceId is not null)
        {
            return new Thought(ThoughtKind.Thinking, p.WorkingDiagnosis is null ? "What's wrong with me?" : Pick(p, "Can I go home soon?", "Waiting for results", "Nice nurse"));
        }

        if (p.Triage is null)
        {
            return new Thought(ThoughtKind.Thinking, "Waiting to be seen");
        }

        return new Thought(ThoughtKind.Thinking, Pick(p, "How much longer?", "So busy today", "Is it my turn?", "Should've gone to my GP"));
    }

    private Thought WardThought(Patient p, bool inPain, bool unwell)
    {
        if (unwell)
        {
            return new Thought(ThoughtKind.Thinking, "I feel awful");
        }

        if (inPain)
        {
            return new Thought(ThoughtKind.Wanting, "Something for the pain");
        }

        if (_visitors.Any(v => v.PatientId == p.Id && v.Activity == VisitorActivity.Visiting))
        {
            return new Thought(ThoughtKind.Happy, p.Maternity ? "Meet the baby!" : "Lovely to see family");
        }

        if (Now.Hour is >= 23 or < 6)
        {
            return new Thought(ThoughtKind.Doing, "Zzz...");
        }

        if (p.DischargeDueAt is { } due && Now >= due)
        {
            return new Thought(ThoughtKind.Wanting, p.MedicinesReadyAt is null ? "Waiting for my tablets" : "To go home");
        }

        if (p.PreOp)
        {
            return new Thought(ThoughtKind.Thinking, p.LastAteAt is { } ate && Now - ate > TimeSpan.FromHours(6) ? "Starving..." : "Nervous about my op");
        }

        if (p.Happiness < 0.45 && p.VisitsReceived == 0)
        {
            return new Thought(ThoughtKind.Wanting, "A visitor");
        }

        if (p.Happiness >= 0.75)
        {
            return new Thought(ThoughtKind.Happy, "Feeling better");
        }

        if (Now.Hour is 12 or 17)
        {
            return new Thought(ThoughtKind.Thinking, "What's for dinner?");
        }

        return new Thought(ThoughtKind.Thinking, Pick(p, "Nice nurses here", "Watching TV", "Reading my book", "Bit bored", "Missing home", "Feeling a bit better"));
    }

    /// <summary>A clinic patient's thought.</summary>
    public Thought? ThoughtOf(Outpatient o)
    {
        if (_strolls.ContainsKey(o))
        {
            return new Thought(ThoughtKind.Doing, "Stretching my legs");
        }

        if (_trips.ContainsKey(o))
        {
            return new Thought(ThoughtKind.Doing, "Getting a drink");
        }

        if (_toiletTrips.ContainsKey(o))
        {
            return new Thought(ThoughtKind.Doing, "Off to the loo");
        }

        var appointment = FindAppointment(o.AppointmentId);
        return o.Stage switch
        {
            OutpatientStage.Waiting when appointment is not null && Now - appointment.Time > TimeSpan.FromMinutes(30) => new Thought(ThoughtKind.Unhappy, $"{(int)(Now - appointment.Time).TotalMinutes} min late!"),
            OutpatientStage.Waiting => new Thought(ThoughtKind.Thinking, "Waiting for my appointment"),
            OutpatientStage.BeingSeen => new Thought(ThoughtKind.Doing, Shorten(o.StatusText)),
            OutpatientStage.Leaving => new Thought(ThoughtKind.Happy, "All done"),
            _ => null,
        };
    }

    /// <summary>The same patient keeps the same idle thought for a few minutes, then moves on to another.</summary>
    private string Pick(Agent person, params string[] options) =>
        options[(int)((((long)person.Id * 7919) + (Now.Ticks / TimeSpan.TicksPerMinute / 6)) % options.Length)];

    private static string Shorten(string text)
    {
        var trimmed = text.Length > 24 ? text[..23].TrimEnd() + "…" : text;
        return trimmed.Length == 0 ? trimmed : char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }
}
