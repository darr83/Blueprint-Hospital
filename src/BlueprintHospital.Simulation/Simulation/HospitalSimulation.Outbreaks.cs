using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>Norovirus on one ward: who has it, whether the bay has been deep-cleaned, and whether the ward is closed.</summary>
public sealed class WardOutbreak
{
    public string WardId { get; init; } = string.Empty;

    /// <summary>Everyone on the ward who has had it this time, first case first.</summary>
    public List<int> CaseIds { get; init; } = new();

    public DateTime StartedAt { get; init; }

    /// <summary>When the latest patient caught it.</summary>
    public DateTime LastCaseAt { get; set; }

    /// <summary>When a cleaner last finished a deep clean of the ward.</summary>
    public DateTime? DeepCleanedAt { get; set; }

    /// <summary>When it next spreads to someone else on the ward, unless the ward is deep-cleaned first.</summary>
    public DateTime SpreadsAt { get; set; }

    /// <summary>Closed to new admissions until then (3 or more cases), or null while it is open.</summary>
    public DateTime? ClosedUntil { get; set; }

    /// <summary>The ward has been deep-cleaned since the latest case, so it won't spread any further.</summary>
    public bool Cleaned => DeepCleanedAt is { } at && at >= LastCaseAt;

    public bool IsClosedAt(DateTime time) => ClosedUntil is { } until && time < until;
}

/// <summary>
/// Norovirus outbreaks on the wards in winter. While norovirus is going round, a ward patient may catch it
/// (or a patient admitted with gastroenteritis brings it in). Unless a cleaner deep-cleans the ward within
/// a few hours it spreads to another patient, and a ward with 3 or more cases closes to new admissions for
/// 48 hours. Patients with it stay a day longer. Comes with seasons, from the level that unlocks it.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>Cases on one ward that close it to new admissions.</summary>
    public const int OutbreakCasesToClose = 3;

    /// <summary>How long a ward stays closed.</summary>
    public static readonly TimeSpan OutbreakClosure = TimeSpan.FromHours(48);

    /// <summary>How long the cleaners have to deep-clean the ward before it spreads.</summary>
    public static readonly TimeSpan DeepCleanWithin = TimeSpan.FromHours(4);

    /// <summary>How long a deep clean of a ward takes.</summary>
    public static readonly TimeSpan DeepCleanTime = TimeSpan.FromMinutes(30);

    /// <summary>The extra time in hospital for a patient who catches it (48 hours clear of symptoms, in real life).</summary>
    public static readonly TimeSpan NorovirusExtraStay = TimeSpan.FromHours(24);

    /// <summary>At the peak of a typical norovirus wave, the chance each hour that a ward of six gets a case.</summary>
    public const double OutbreakChancePerHour = 0.012;

    private const string NorovirusWave = "norovirus";

    private readonly List<WardOutbreak> _outbreaks = new();
    private DateTime _outbreaksCheckedAt;
    private GameRandom? _outbreakRandom;

    private GameRandom OutbreakRandom => _outbreakRandom ??= new GameRandom(_options.Seed + 524_287);

    /// <summary>Norovirus outbreaks on the wards now (including closed wards that haven't reopened).</summary>
    public IReadOnlyList<WardOutbreak> Outbreaks => _outbreaks;

    /// <summary>Whether norovirus can break out on the wards in this game now.</summary>
    public bool OutbreaksActive => SeasonsActive && IsFeatureOn(LevelFeatures.Outbreaks);

    /// <summary>How strong the norovirus wave is today (0 when it isn't going round, or seasons are off).</summary>
    public double NorovirusLevel => SeasonsActive ? _seasonCalendar!.WaveLevel(NorovirusWave, Now) : 0;

    public WardOutbreak? OutbreakOn(string wardId) => _outbreaks.FirstOrDefault(o => o.WardId == wardId);

    /// <summary>Closed to new admissions because of norovirus.</summary>
    public bool IsWardClosed(string wardId) => OutbreakOn(wardId)?.IsClosedAt(Now) == true;

    /// <summary>A ward with norovirus that a cleaner should deep-clean now (not already being done).</summary>
    private CareSpace? WardNeedingDeepClean(StaffMember cleaner) =>
        _outbreaks.Where(o => !o.Cleaned && !_cleanerJobs.Any(j => j.Key != cleaner && j.Value.DeepCleanWardId == o.WardId))
            .Select(o => FindSpace(o.WardId))
            .Where(w => w is not null)
            .OrderBy(w => Distance(w!.StaffSpot, cleaner.Tile))
            .FirstOrDefault();

    /// <summary>A cleaner has finished deep-cleaning a ward: it won't spread from the cases there now.</summary>
    private void FinishDeepClean(string wardId)
    {
        if (OutbreakOn(wardId) is not { } outbreak || FindSpace(wardId) is not { } ward)
        {
            return;
        }

        outbreak.DeepCleanedAt = Now;
        var closed = outbreak.IsClosedAt(Now) ? $" It stays closed to new admissions until {outbreak.ClosedUntil:ddd HH:mm}." : string.Empty;
        Notifications.Add(Now, NotificationLevel.Info, $"{ward.Name} has been deep-cleaned, so the norovirus shouldn't spread any further.{closed}", null, ward.Id);
    }

    /// <summary>Every 10 minutes: new cases, spreading, closing and reopening wards.</summary>
    private void UpdateOutbreaks()
    {
        if (Now - _outbreaksCheckedAt < TimeSpan.FromMinutes(10) && Now >= _outbreaksCheckedAt)
        {
            return;
        }

        var hours = Math.Clamp((Now - _outbreaksCheckedAt).TotalHours, 0, 1);
        _outbreaksCheckedAt = Now;

        foreach (var outbreak in _outbreaks.ToList())
        {
            var ward = FindSpace(outbreak.WardId);
            var onWard = ward is null ? new List<Patient>() : PatientsOnWard(ward).ToList();
            if (ward is null || (!outbreak.IsClosedAt(Now) && !onWard.Any(p => p.Norovirus)))
            {
                // Everyone who had it has gone home (or the ward has gone), and it isn't closed: it's over.
                _outbreaks.Remove(outbreak);
                if (ward is not null && outbreak.ClosedUntil is not null)
                {
                    Notifications.Add(Now, NotificationLevel.Info, $"{ward.Name} has reopened to admissions after its norovirus outbreak ({outbreak.CaseIds.Count} cases).", null, ward.Id);
                }

                continue;
            }

            if (!outbreak.Cleaned && Now >= outbreak.SpreadsAt)
            {
                outbreak.SpreadsAt = Now + DeepCleanWithin;
                if (onWard.Where(p => !p.Norovirus).ToList() is { Count: > 0 } well)
                {
                    CatchNorovirus(ward, OutbreakRandom.Pick(well), "Caught norovirus from another patient on the ward");
                }
            }
        }

        if (!OutbreaksActive)
        {
            return;
        }

        var level = NorovirusLevel;
        foreach (var ward in Wards.Where(w => w.DepartmentId is not null).ToList())
        {
            var onWard = PatientsOnWard(ward).ToList();

            // Admitted with a tummy bug: they bring it onto the ward.
            foreach (var patient in onWard.Where(p => !p.Norovirus && p.WorkingDiagnosis is { } d && IsNorovirusIllness(d.ConditionId)))
            {
                CatchNorovirus(ward, patient, "Has norovirus: barrier nursed in their bed");
            }

            if (OutbreakOn(ward.Id) is null && level > 0 && onWard.Count > 0
                && OutbreakRandom.Chance(OutbreakChancePerHour * level * hours * onWard.Count / 6.0))
            {
                CatchNorovirus(ward, OutbreakRandom.Pick(onWard), "Caught norovirus on the ward");
            }
        }
    }

    private IEnumerable<Patient> PatientsOnWard(CareSpace ward) =>
        _patients.Values.Where(p => p.IsAlive && p.Stage == PatientStage.OnWard && p.CareSpaceId == ward.Id && !p.Maternity);

    /// <summary>The illnesses that come in more when the norovirus wave is about (from the seasons data).</summary>
    private bool IsNorovirusIllness(string conditionId) =>
        _seasonCalendar?.Profile.Waves.FirstOrDefault(w => w.Id == NorovirusWave)?.Conditions.ContainsKey(conditionId) == true;

    /// <summary>A patient on a ward has norovirus: they stay longer, and the ward has an outbreak until a cleaner deep-cleans it.</summary>
    private void CatchNorovirus(CareSpace ward, Patient patient, string how)
    {
        patient.Norovirus = true;
        if (patient.DischargeDueAt is { } due)
        {
            patient.DischargeDueAt = due + NorovirusExtraStay;
        }

        patient.Log(Now, $"{how}: diarrhoea and vomiting. Staying an extra day.", important: true);

        var outbreak = OutbreakOn(ward.Id);
        if (outbreak is null)
        {
            outbreak = new WardOutbreak { WardId = ward.Id, StartedAt = Now };
            _outbreaks.Add(outbreak);
            Notifications.Add(Now, NotificationLevel.Warning,
                $"Norovirus on {ward.Name}: {patient.FullName} has it. A cleaner needs to deep-clean the ward within {DeepCleanWithin.TotalHours:0} hours or it will spread"
                + (CleaningRooms.Any() && _staff.Any(s => s.Role == StaffRole.Cleaner) ? "." : " (hire a cleaner and build a cleaning room)."),
                patient.Id, ward.Id);
        }
        else if (!outbreak.CaseIds.Contains(patient.Id))
        {
            Notifications.Add(Now, NotificationLevel.Warning, $"Norovirus has spread on {ward.Name}: {patient.FullName} has it too ({outbreak.CaseIds.Count + 1} cases).", patient.Id, ward.Id);
        }

        if (!outbreak.CaseIds.Contains(patient.Id))
        {
            outbreak.CaseIds.Add(patient.Id);
        }

        outbreak.LastCaseAt = Now;
        outbreak.SpreadsAt = Now + DeepCleanWithin;
        if (outbreak.CaseIds.Count >= OutbreakCasesToClose && outbreak.ClosedUntil is null)
        {
            outbreak.ClosedUntil = Now + OutbreakClosure;
            Notifications.Add(Now, NotificationLevel.Critical,
                $"{ward.Name} is closed to new admissions for {OutbreakClosure.TotalHours:0} hours: {outbreak.CaseIds.Count} patients have norovirus. Admissions go to other wards or wait in A&E.",
                null, ward.Id);
        }
    }

    /// <summary>The "norovirus" cheat: starts an outbreak on a ward (the busiest, or the one named), "close" to close the busiest now, or "off" to clear them all.</summary>
    private CheatResult NorovirusCheat(string argument)
    {
        if (argument.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var patient in _patients.Values.Where(p => p.Norovirus))
            {
                patient.Norovirus = false;
            }

            var count = _outbreaks.Count;
            _outbreaks.Clear();
            return new CheatResult(true, $"Cleared {count} norovirus outbreak{(count == 1 ? string.Empty : "s")}.");
        }

        var close = argument.Equals("close", StringComparison.OrdinalIgnoreCase);
        var named = close || argument.Length == 0 ? null : argument;
        var ward = Wards.Where(w => named is null || w.Name.Equals(named, StringComparison.OrdinalIgnoreCase) || w.Id == named)
            .OrderByDescending(w => PatientsOnWard(w).Count())
            .ThenBy(w => w.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (ward is null)
        {
            return new CheatResult(false, "No ward to put norovirus on.");
        }

        var patients = PatientsOnWard(ward).Where(p => !p.Norovirus).ToList();
        if (patients.Count == 0 && !close)
        {
            return new CheatResult(false, $"Nobody on {ward.Name} to catch it.");
        }

        if (close)
        {
            foreach (var patient in patients.Take(Math.Max(0, OutbreakCasesToClose - (OutbreakOn(ward.Id)?.CaseIds.Count ?? 0))))
            {
                CatchNorovirus(ward, patient, "Caught norovirus on the ward");
            }

            return new CheatResult(IsWardClosed(ward.Id), IsWardClosed(ward.Id) ? $"{ward.Name} closed for norovirus." : $"Not enough patients on {ward.Name} to close it.");
        }

        CatchNorovirus(ward, patients[0], "Caught norovirus on the ward");
        return new CheatResult(true, $"Norovirus on {ward.Name}.");
    }

    private List<WardOutbreak> SaveOutbreaks() => _outbreaks.ToList();

    private void RestoreOutbreaks(List<WardOutbreak>? outbreaks)
    {
        _outbreaks.Clear();
        _outbreaks.AddRange(outbreaks ?? new List<WardOutbreak>());
        _outbreaksCheckedAt = Now;
    }
}
