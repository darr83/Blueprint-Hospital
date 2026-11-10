using BlueprintHospital.Simulation.Medical;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>Names of the figures kept in <see cref="HospitalSimulation.History"/>.</summary>
public static class StatKeys
{
    public const string Arrivals = "ae.arrivals";
    public const string AmbulanceArrivals = "ae.ambulance_arrivals";
    public const string TriagedRed = "ae.triaged.red";
    public const string TriagedAmber = "ae.triaged.amber";
    public const string TriagedGreen = "ae.triaged.green";
    public const string Departed = "ae.departed";
    public const string WithinFourHours = "ae.within_4h";
    public const string MinutesInDepartment = "ae.minutes_in_department";
    public const string Discharged = "ae.discharged";
    public const string Admitted = "ae.admitted";
    public const string Transferred = "ae.transferred";
    public const string LeftUnseen = "ae.left_unseen";
    public const string Deaths = "ae.deaths";
    public const string SeenByDoctor = "ae.seen_by_doctor";
    public const string MinutesToDoctor = "ae.minutes_to_doctor";
    public const string Handovers = "ae.handovers";
    public const string HandoverMinutes = "ae.handover_minutes";
    public const string Diagnoses = "ae.diagnoses";
    public const string DiagnosesCorrect = "ae.diagnoses_correct";
    public const string MissedDiagnoses = "ae.missed";
    public const string WardDischarges = "ward.discharges";
    public const string WardDeaths = "ward.deaths";
    public const string WardCorrections = "ward.corrections";
    public const string Scans = "services.scans";
    public const string PrescriptionsCollected = "services.prescriptions";
    public const string Operations = "services.operations";
    public const string Births = "maternity.births";
    public const string Caesareans = "maternity.caesareans";
    public const string NeonatalAdmissions = "maternity.neonatal";
    public const string NeonatalTransfers = "maternity.neonatal_transfers";
    public const string MaternityDiverted = "maternity.diverted";
    public const string ClinicSeen = "clinics.seen";
    public const string ClinicDidNotAttend = "clinics.dna";
    public const string ToiletAccidents = "facilities.toilet_accidents";
    public const string Hired = "staff.hired";
    public const string Left = "staff.left";

    // Gauges, read every quarter of an hour.
    public const string InDepartment = "ae.in_department";
    public const string Waiting = "ae.waiting";
    public const string LongestWait = "ae.longest_wait";
    public const string AmbulancesWaiting = "ae.ambulances_waiting";
    public const string AwaitingBed = "ae.awaiting_bed";
    public const string WardOccupancy = "ward.occupancy";
    public const string OnWards = "ward.patients";
    public const string StaffOnDuty = "staff.on_duty";
    public const string Balance = "money.balance";

    public static string Money(MoneyCategory category) => $"money.{category.ToString().ToLowerInvariant()}";

    public static string Triaged(TriageCategory category) => category switch
    {
        TriageCategory.Red => TriagedRed,
        TriageCategory.Amber => TriagedAmber,
        _ => TriagedGreen,
    };
}

/// <summary>
/// The figures behind the Stats screen. Running totals the simulation already keeps (A&amp;E, wards,
/// maternity) are copied in as they change; money, clinics, toilets and staff are counted where they
/// happen; and the gauges are read every 15 minutes.
/// </summary>
public sealed partial class HospitalSimulation
{
    private static readonly TimeSpan GaugeInterval = TimeSpan.FromMinutes(15);
    private readonly Dictionary<string, double> _syncedTotals = new();
    private DateTime _nextGaugeReading;
    private StatsHistory? _history;

    /// <summary>The hospital's figures over the last 24 hours, 7 days, 28 days and since opening.</summary>
    public StatsHistory History
    {
        get
        {
            UpdateHistory();
            return _history!;
        }
    }

    private StatsHistory HistoryStore => _history ??= new StatsHistory(Now);

    /// <summary>Counts something into the history now.</summary>
    internal void Tally(string key, double amount = 1) => HistoryStore.Add(Now, key, amount);

    /// <summary>The running totals kept elsewhere in the simulation, by history key.</summary>
    private IEnumerable<(string Key, double Value)> RunningTotals()
    {
        var s = Stats;
        yield return (StatKeys.Arrivals, s.ArrivalsTotal);
        yield return (StatKeys.AmbulanceArrivals, s.AmbulanceArrivalsTotal);
        foreach (var (category, count) in s.TriagedTotal)
        {
            yield return (StatKeys.Triaged(category), count);
        }

        yield return (StatKeys.Departed, s.DepartedTotal);
        yield return (StatKeys.WithinFourHours, s.DepartedWithinFourHours);
        yield return (StatKeys.MinutesInDepartment, s.MinutesInDepartmentTotal);
        yield return (StatKeys.Discharged, s.Discharged);
        yield return (StatKeys.Admitted, s.Admitted);
        yield return (StatKeys.Transferred, s.Transferred);
        yield return (StatKeys.LeftUnseen, s.LeftWithoutBeingSeen);
        yield return (StatKeys.Deaths, s.Deaths);
        yield return (StatKeys.SeenByDoctor, s.SeenByDoctorTotal);
        yield return (StatKeys.MinutesToDoctor, s.MinutesToDoctorTotal);
        yield return (StatKeys.Handovers, s.HandoversTotal);
        yield return (StatKeys.HandoverMinutes, s.HandoverMinutesTotal);
        yield return (StatKeys.Diagnoses, s.DiagnosesMade);
        yield return (StatKeys.DiagnosesCorrect, s.DiagnosesCorrect);
        yield return (StatKeys.MissedDiagnoses, s.MissedDiagnoses);
        yield return (StatKeys.WardDischarges, s.WardDischarges);
        yield return (StatKeys.WardDeaths, s.WardDeaths);
        yield return (StatKeys.WardCorrections, s.WardDiagnosesCorrected);
        yield return (StatKeys.Scans, s.ScansDone);
        yield return (StatKeys.Operations, s.OperationsDone);
        var m = MaternityStats;
        yield return (StatKeys.Births, m.Births);
        yield return (StatKeys.Caesareans, m.Caesareans);
        yield return (StatKeys.NeonatalAdmissions, m.NeonatalAdmissions);
        yield return (StatKeys.NeonatalTransfers, m.NeonatalTransfers);
        yield return (StatKeys.MaternityDiverted, m.Diverted);
    }

    /// <summary>Copies in whatever the running totals have gained since last time, and reads the gauges when due.</summary>
    private void UpdateHistory()
    {
        var history = HistoryStore;
        foreach (var (key, value) in RunningTotals())
        {
            var change = value - _syncedTotals.GetValueOrDefault(key);
            if (change != 0)
            {
                history.Add(Now, key, change);
                _syncedTotals[key] = value;
            }
        }

        if (Now < _nextGaugeReading)
        {
            return;
        }

        // Readings fall on the quarter hour.
        var minute = Now.Minute / 15 * 15;
        _nextGaugeReading = new DateTime(Now.Year, Now.Month, Now.Day, Now.Hour, minute, 0, Now.Kind) + GaugeInterval;
        var s = Stats;
        history.Sample(Now, StatKeys.InDepartment, s.PatientsInDepartment);
        history.Sample(Now, StatKeys.Waiting, s.AwaitingTriage + s.WaitingForSpace.Values.Sum());
        history.Sample(Now, StatKeys.LongestWait, s.LongestWaitMinutes.Values.DefaultIfEmpty(0).Max());
        history.Sample(Now, StatKeys.AmbulancesWaiting, s.AmbulancesWaiting);
        history.Sample(Now, StatKeys.AwaitingBed, s.AwaitingBed);
        if (s.WardBeds.Total > 0)
        {
            history.Sample(Now, StatKeys.WardOccupancy, (double)s.WardBeds.Used / s.WardBeds.Total);
        }

        history.Sample(Now, StatKeys.OnWards, s.OnWards);
        history.Sample(Now, StatKeys.StaffOnDuty, OnDutyStaff.Count());
        history.Sample(Now, StatKeys.Balance, Balance);
    }

    /// <summary>
    /// After loading: puts back the saved history, or for a save from before the Stats screen starts it
    /// afresh with the old running totals as the figures since opening.
    /// </summary>
    private void RestoreHistory(StatsHistoryState? state)
    {
        _syncedTotals.Clear();
        var totals = RunningTotals().ToList();
        foreach (var (key, value) in totals)
        {
            _syncedTotals[key] = value;
        }

        if (state is not null)
        {
            HistoryStore.Restore(state);
        }
        else
        {
            HistoryStore.Reset(Now);
            foreach (var (key, value) in totals)
            {
                _history!.SeedAllTime(key, value);
            }
        }

        // The last reading was taken before the save; the next falls on the coming quarter hour.
        _nextGaugeReading = new DateTime(Now.Year, Now.Month, Now.Day, Now.Hour, Now.Minute / 15 * 15, 0, Now.Kind) + GaugeInterval;
    }
}
