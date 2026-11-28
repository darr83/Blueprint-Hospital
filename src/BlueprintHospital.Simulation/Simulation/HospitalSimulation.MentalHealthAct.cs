using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// The Mental Health Act, kept simple (stage 3 of the mental health department):
/// <list type="bullet">
/// <item>Section 136: the police bring in someone they have detained in a public place. With a Section 136 suite they
/// hand over there and leave, and the person waits in the suite rather than in an A&amp;E cubicle. Without one the
/// officer waits in A&amp;E with them. A Section 136 lasts up to 24 hours.</item>
/// <item>A Mental Health Act assessment: when someone needs admitting to a mental health ward but may not agree, an
/// approved mental health professional (AMHP) from the council comes in, and with a psychiatrist (or an A&amp;E doctor
/// when no psychiatrist is on) decides whether to detain them under Section 2.</item>
/// <item>No mental health bed: the patient waits in A&amp;E. At 12 hours (or sooner, if the player pays) the hospital pays
/// for a bed in another area.</item>
/// </list>
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>How long a Section 136 lasts.</summary>
    public static readonly TimeSpan Section136Limit = TimeSpan.FromHours(24);

    /// <summary>How long a mental health patient waits in A&amp;E for a bed before one is found in another area.</summary>
    public static readonly TimeSpan MentalHealthBedWait = TimeSpan.FromHours(12);

    /// <summary>What the hospital pays for a mental health bed in another area.</summary>
    public const int OutOfAreaBedCost = 4_500;

    /// <summary>The chance an adult with one of these conditions is brought in by the police under Section 136.</summary>
    private static readonly IReadOnlyDictionary<string, double> Section136Chance = new Dictionary<string, double>
    {
        ["low_mood_suicidal_thoughts"] = 0.15,
        ["first_episode_psychosis"] = 0.35,
        ["mania"] = 0.35,
        ["drug_induced_psychosis"] = 0.3,
    };

    /// <summary>The chance someone who needs admitting (and was not brought in on a Section 136) needs a Mental Health Act assessment.</summary>
    private static readonly IReadOnlyDictionary<string, double> AssessmentChance = new Dictionary<string, double>
    {
        ["severe_depression"] = 0.2,
        ["first_episode_psychosis"] = 0.45,
        ["mania"] = 0.5,
    };

    /// <summary>Of those assessed, the share detained under Section 2 (the rest agree to come in).</summary>
    private const double DetainChance = 0.85;

    /// <summary>How long the Mental Health Act assessment takes, in minutes.</summary>
    private const double AssessmentMinutes = 45;

    private static readonly string[] AmhpFirstNames = { "Helen", "Marcus", "Priya", "Gareth", "Sinead", "Tom", "Ayesha", "Rob", "Claire", "Femi", "Joanna", "Imran" };

    /// <summary>AMHPs who are here for an assessment, or walking back out after one, by their patient's id.</summary>
    private readonly Dictionary<int, Parent> _amhps = new();

    /// <summary>The AMHPs in the building now.</summary>
    public IEnumerable<Parent> Amhps => _amhps.Values;

    /// <summary>The AMHP seeing this patient, if they are here.</summary>
    public Parent? AmhpFor(Patient patient) => _amhps.GetValueOrDefault(patient.Id) is { Leaving: false } amhp ? amhp : null;

    // ---------------------------------------------------------------- numbers for the Stats tab

    public int Section136Arrivals => (int)HistoryStore.Total(StatsPeriod.AllTime, Now, StatKeys.Section136Arrivals);

    public int Section136HandedOverInSuite => (int)HistoryStore.Total(StatsPeriod.AllTime, Now, StatKeys.Section136InSuite);

    public double PoliceHoursInEmergency => HistoryStore.Total(StatsPeriod.AllTime, Now, StatKeys.PoliceMinutes) / 60.0;

    public int MentalHealthActAssessments => (int)HistoryStore.Total(StatsPeriod.AllTime, Now, StatKeys.MentalHealthActAssessments);

    public int Detentions => (int)HistoryStore.Total(StatsPeriod.AllTime, Now, StatKeys.Detentions);

    public int OutOfAreaPlacements => (int)HistoryStore.Total(StatsPeriod.AllTime, Now, StatKeys.OutOfAreaBeds);

    public int MentalHealthTwelveHourWaits => (int)HistoryStore.Total(StatsPeriod.AllTime, Now, StatKeys.MentalHealthTwelveHourWaits);

    // ---------------------------------------------------------------- Section 136

    /// <summary>A Section 136 suite with its own staff.</summary>
    public IEnumerable<CareSpace> Section136Suites => SpacesOfKind(CareSpaceKind.Section136Suite);

    /// <summary>Whether a Section 136 suite is open and nobody is in it (or this patient already is).</summary>
    private CareSpace? FreeSection136Suite(Patient patient) =>
        Section136Suites.Where(s => s.InUse && IsStaffed(s) && (s.OccupantId is null || s.OccupantId == patient.Id)).OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>Some adults in a mental health crisis are brought in by the police under Section 136 (once the department is open).</summary>
    private void MaybeSection136(Patient patient)
    {
        if (patient.Age < AdultAge || patient.Maternity || !IsUnlocked(MentalHealthDepartmentId)
            || !Section136Chance.TryGetValue(patient.Condition.Id, out var chance) || !MentalHealthRandom.Chance(chance))
        {
            return;
        }

        patient.Section136 = true;
        patient.PoliceEscort = true;
    }

    /// <summary>Brings in someone the police have detained under Section 136 (for tests and review shots).</summary>
    public Patient SpawnSection136Patient(string conditionId, int age = 30)
    {
        var patient = _generator.Generate(Now, ArrivalMethod.WalkIn, conditionId, age);
        patient.Section136 = true;
        patient.PoliceEscort = true;
        NoteSection136Arrival(patient);
        AdmitNewPatient(patient);
        return patient;
    }

    /// <summary>Logs and counts a Section 136 arrival, once they are in the building.</summary>
    private void NoteSection136Arrival(Patient patient)
    {
        if (!patient.Section136)
        {
            return;
        }

        Tally(StatKeys.Section136Arrivals);
        patient.Log(Now, "Detained by the police under Section 136 of the Mental Health Act and brought to a place of safety.", important: true);
    }

    /// <summary>The treatment step's room: a Section 136 patient has their liaison assessment in a free Section 136 suite.</summary>
    private CareSpaceKind? Section136Room(Patient patient, CareSpaceKind? room) =>
        room == CareSpaceKind.MentalHealthAssessment && patient.Section136 && (patient.InSection136Suite || FreeSection136Suite(patient) is not null)
            ? CareSpaceKind.Section136Suite
            : room;

    /// <summary>
    /// After their assessment in the Section 136 suite the patient stays there: the police hand over and leave, and their
    /// A&amp;E cubicle is freed for someone else.
    /// </summary>
    private void StayInSection136Suite(Patient patient, CareSpace suite)
    {
        if (patient.Stage is PatientStage.Gone or PatientStage.Leaving)
        {
            return;
        }

        var first = !patient.InSection136Suite;
        ReleaseSpace(patient);
        patient.CareSpaceId = suite.Id;
        suite.OccupantId = patient.Id;
        patient.InSection136Suite = true;
        if (patient.Tile != suite.PatientSpot && !patient.IsMoving)
        {
            WalkTo(patient, suite.PatientSpot);
        }

        if (!first)
        {
            return;
        }

        Tally(StatKeys.Section136InSuite);
        patient.Log(Now, $"Staying in {suite.Name}; the police have handed over to the mental health team.");
    }

    /// <summary>The officer waited with someone on a Section 136 until now: counts their time in A&amp;E.</summary>
    private void NotePoliceLeft(Patient patient)
    {
        if (patient.Section136)
        {
            Tally(StatKeys.PoliceMinutes, Math.Max(0, (Now - patient.ArrivedAt).TotalMinutes));
        }
    }

    // ---------------------------------------------------------------- the Mental Health Act assessment

    /// <summary>
    /// When someone needs admitting to a mental health ward, decides whether they need a Mental Health Act assessment
    /// first and, if so, calls the AMHP. Returns true if the admission waits for it.
    /// </summary>
    private bool StartMentalHealthAct(Patient patient)
    {
        if (patient.MentalHealthAct != MentalHealthActStatus.None || !IsMentalHealthPatient(patient) || !MentalHealthWards.Any())
        {
            return false;
        }

        var chance = patient.Section136 ? 0.9 : AssessmentChance.GetValueOrDefault(patient.WorkingDiagnosis!.ConditionId);
        if (!MentalHealthRandom.Chance(chance))
        {
            return false;
        }

        var night = Now.Hour is >= 21 or < 7;
        var minutes = MentalHealthRandom.Range(60, 180) * (night ? 1.5 : 1);
        patient.MentalHealthAct = MentalHealthActStatus.AwaitingAmhp;
        patient.AmhpDueAt = Now.AddMinutes(minutes);
        patient.Log(Now, $"Needs admitting but may not agree, so a Mental Health Act assessment has been asked for. An approved mental health professional (AMHP) is coming in (about {minutes / 60:0.#} hours).", important: true);
        Notifications.Add(Now, NotificationLevel.Info, $"Mental Health Act assessment requested for {patient.FullName}; the AMHP is on the way.", patient.Id, patient.CareSpaceId, patient.Triage);
        return true;
    }

    /// <summary>A psychiatrist does the assessment; an A&amp;E doctor does it when no psychiatrist is on.</summary>
    private bool MentalHealthActDoctorCanCover(ClinicalTask task, StaffMember member) =>
        task.Kind == TaskKind.MentalHealthActAssessment && member.Role == StaffRole.Doctor
        && !_staff.Any(s => s.Role == StaffRole.Psychiatrist && s.IsOnDuty);

    private void CompleteMentalHealthActAssessment(StaffMember member, Patient patient)
    {
        var amhp = AmhpFor(patient);
        var who = amhp is null ? "the AMHP" : $"AMHP {amhp.FullName}";
        Tally(StatKeys.MentalHealthActAssessments);
        if (MentalHealthRandom.Chance(DetainChance))
        {
            patient.MentalHealthAct = MentalHealthActStatus.Detained;
            Tally(StatKeys.Detentions);
            patient.Log(Now, $"Mental Health Act assessment by {member.Name}, a second doctor and {who}: detained under Section 2 for assessment in hospital.", important: true);
        }
        else
        {
            patient.MentalHealthAct = MentalHealthActStatus.Informal;
            patient.Log(Now, $"Mental Health Act assessment by {member.Name} and {who}: agreed to come into hospital, so not detained.", important: true);
        }

        if (patient.Section136)
        {
            patient.Log(Now, "Section 136 ended with the assessment.");
        }

        SendAmhpHome(patient);
        if (patient.WorkingDiagnosis is { } diagnosis && Database.Conditions.TryGetValue(diagnosis.ConditionId, out var diagnosed))
        {
            RequestWardBed(patient, diagnosed);
        }
    }

    /// <summary>Calls the AMHP in when they are due, starts the assessment when they arrive, and walks them out after.</summary>
    private void UpdateMentalHealthAct(double dt)
    {
        foreach (var patient in _patients.Values.Where(p => p.MentalHealthAct is MentalHealthActStatus.AwaitingAmhp or MentalHealthActStatus.Assessing).OrderBy(p => p.Id).ToList())
        {
            if (!patient.IsAlive || patient.Stage is PatientStage.Gone or PatientStage.Leaving or PatientStage.AwaitingTransfer)
            {
                patient.MentalHealthAct = MentalHealthActStatus.None;
                RemoveTasks(patient, TaskKind.MentalHealthActAssessment);
                SendAmhpHome(patient);
                continue;
            }

            if (Now < patient.AmhpDueAt)
            {
                continue;
            }

            if (!_amhps.TryGetValue(patient.Id, out var amhp))
            {
                amhp = NewAmhp(patient);
                _amhps[patient.Id] = amhp;
                patient.Log(Now, $"{amhp.FullName}, the AMHP, has arrived for the Mental Health Act assessment.");
            }

            var spot = ParentSpotBeside(patient.Tile);
            if (!amhp.IsMoving && amhp.Tile != spot && amhp.Heading != spot)
            {
                var path = PathFor(amhp, spot);
                if (path.Count > 0)
                {
                    amhp.FollowPath(path);
                }
                else
                {
                    amhp.PlaceAt(spot);
                }

                amhp.Heading = spot;
            }

            if (patient.MentalHealthAct == MentalHealthActStatus.AwaitingAmhp && !amhp.IsMoving && amhp.Tile == spot)
            {
                patient.MentalHealthAct = MentalHealthActStatus.Assessing;
                if (!HasTask(patient, TaskKind.MentalHealthActAssessment))
                {
                    AddTask(TaskKind.MentalHealthActAssessment, StaffRole.Psychiatrist, patient, AssessmentMinutes, "Mental Health Act assessment");
                }
            }
        }

        foreach (var (id, amhp) in _amhps.ToList())
        {
            amhp.Move(dt);
            if (amhp.Leaving && !amhp.IsMoving)
            {
                _amhps.Remove(id);
            }
        }

        foreach (var patient in _patients.Values.Where(p => p.Section136 && !p.Section136Overran && p.MentalHealthAct is not (MentalHealthActStatus.Detained or MentalHealthActStatus.Informal)
                                                            && p.Stage is not (PatientStage.Gone or PatientStage.Leaving) && p.LeftEmergencyAt is null && Now - p.ArrivedAt > Section136Limit))
        {
            patient.Section136Overran = true;
            Tally(StatKeys.Section136Overruns);
            patient.Log(Now, "Their Section 136 has run past 24 hours without a Mental Health Act assessment.", important: true);
            Notifications.Add(Now, NotificationLevel.Warning, $"{patient.FullName}'s Section 136 has run past 24 hours. Hire a psychiatrist and keep mental health beds free.", patient.Id, patient.CareSpaceId, patient.Triage);
        }
    }

    private Parent NewAmhp(Patient patient)
    {
        var roll = (patient.Id * 3301) + patient.Surname.Length;
        var amhp = new Parent
        {
            Id = AmhpIdOffset + patient.Id,
            ChildId = patient.Id,
            FirstName = AmhpFirstNames[roll % AmhpFirstNames.Length],
            Surname = PoliceSurnames[(roll / 5) % PoliceSurnames.Length],
            Sex = roll % 5 < 3 ? Sex.Female : Sex.Male,
            Age = 32 + (roll / 7 % 24),
            Escort = EscortKind.Amhp,
        };
        amhp.PlaceAt(EntranceTile);
        return amhp;
    }

    /// <summary>Ids for AMHPs: their patient's id plus this, clear of everyone else's.</summary>
    public const int AmhpIdOffset = 8_000_000;

    /// <summary>The AMHP's work is done: they walk back out.</summary>
    private void SendAmhpHome(Patient patient)
    {
        if (!_amhps.TryGetValue(patient.Id, out var amhp) || amhp.Leaving)
        {
            return;
        }

        amhp.Leaving = true;
        var path = PathFor(amhp, EntranceTile);
        if (path.Count > 0)
        {
            amhp.FollowPath(path);
        }
        else
        {
            _amhps.Remove(patient.Id);
        }
    }

    /// <summary>Puts a visiting AMHP back beside their patient after a load.</summary>
    private void RestoreAmhps()
    {
        _amhps.Clear();
        foreach (var patient in _patients.Values.Where(p => p.MentalHealthAct == MentalHealthActStatus.Assessing))
        {
            var amhp = NewAmhp(patient);
            amhp.PlaceAt(ParentSpotBeside(patient.Tile));
            _amhps[patient.Id] = amhp;
        }
    }

    // ---------------------------------------------------------------- no mental health bed

    /// <summary>Whether a mental health patient waiting in A&amp;E for a bed could be sent to a bed in another area.</summary>
    public bool CanBuyOutOfAreaBed(Patient patient) =>
        patient.Stage == PatientStage.AwaitingBed && patient.ReservedWardId is null && patient.IsAlive && IsMentalHealthPatient(patient) && MentalHealthWards.Any();

    /// <summary>The player pays for a mental health bed in another area now, rather than wait for one here.</summary>
    public bool BuyOutOfAreaBed(Patient patient)
    {
        if (!CanBuyOutOfAreaBed(patient))
        {
            return false;
        }

        SendOutOfArea(patient, $"A mental health bed in another area was paid for (£{OutOfAreaBedCost:N0}), so they are being transferred there.");
        return true;
    }

    /// <summary>
    /// A mental health patient still waiting for a bed after 12 hours: the hospital pays for a bed in another area.
    /// Returns true if it dealt with them.
    /// </summary>
    private bool MentalHealthBedOverdue(Patient patient)
    {
        if (!IsMentalHealthPatient(patient) || !MentalHealthWards.Any() || Now - patient.BedRequestedAt <= MentalHealthBedWait)
        {
            return false;
        }

        Tally(StatKeys.MentalHealthTwelveHourWaits);
        SendOutOfArea(patient, $"No mental health bed after 12 hours in A&E, so a bed in another area has been paid for (£{OutOfAreaBedCost:N0}).");
        Notifications.Add(Now, NotificationLevel.Warning, $"{patient.FullName} waited 12 hours in A&E for a mental health bed; one was paid for in another area.", patient.Id, patient.CareSpaceId, patient.Triage);
        return true;
    }

    private void SendOutOfArea(Patient patient, string note)
    {
        Book(MoneyCategory.OutOfAreaBeds, -OutOfAreaBedCost);
        Tally(StatKeys.OutOfAreaBeds);
        ArrangeTransfer(patient, Database.Conditions[patient.WorkingDiagnosis!.ConditionId], note);
    }

    /// <summary>For the patient record: where they are with the Mental Health Act, or null.</summary>
    public string? MentalHealthActLine(Patient patient)
    {
        var parts = new List<string>();
        if (patient.Section136 && patient.MentalHealthAct is not (MentalHealthActStatus.Detained or MentalHealthActStatus.Informal) && patient.LeftEmergencyAt is null)
        {
            var left = Section136Limit - (Now - patient.ArrivedAt);
            parts.Add(left > TimeSpan.Zero ? $"Section 136 · {Math.Floor(left.TotalHours):0} h {left.Minutes:00} m left" : "Section 136 · over 24 hours");
        }

        switch (patient.MentalHealthAct)
        {
            case MentalHealthActStatus.AwaitingAmhp:
                parts.Add(patient.AmhpDueAt is { } due && due > Now ? $"AMHP due at {due:HH:mm}" : "AMHP arriving");
                break;
            case MentalHealthActStatus.Assessing:
                parts.Add("Mental Health Act assessment under way");
                break;
            case MentalHealthActStatus.Detained:
                parts.Add("Detained under Section 2");
                break;
            case MentalHealthActStatus.Informal:
                parts.Add("Informal (agreed to come in)");
                break;
        }

        if (patient.Stage == PatientStage.AwaitingBed && IsMentalHealthPatient(patient) && patient.BedRequestedAt is { } asked && MentalHealthWards.Any())
        {
            var waited = Now - asked;
            parts.Add($"Waiting for a mental health bed {Math.Floor(waited.TotalHours):0} h {waited.Minutes:00} m of 12 h");
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
