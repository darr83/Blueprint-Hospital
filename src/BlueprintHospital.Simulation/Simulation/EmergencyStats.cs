using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Transport;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>Live A&amp;E figures for the dashboard.</summary>
public sealed class EmergencyStats
{
    private readonly Queue<double> _recentTimeToDoctor = new();
    private readonly Queue<double> _recentHandoverMinutes = new();

    internal Queue<double> RecentTimeToDoctor => _recentTimeToDoctor;

    internal Queue<double> RecentHandoverMinutes => _recentHandoverMinutes;

    public int ArrivalsTotal { get; internal set; }

    public int AmbulanceArrivalsTotal { get; internal set; }

    public int DepartedTotal { get; internal set; }

    public int DepartedWithinFourHours { get; internal set; }

    public int Discharged { get; internal set; }

    public int Transferred { get; internal set; }

    /// <summary>Patients who left A&amp;E for a ward bed.</summary>
    public int Admitted { get; internal set; }

    /// <summary>Patients discharged home from a ward.</summary>
    public int WardDischarges { get; internal set; }

    public int WardDeaths { get; internal set; }

    public int WardDiagnosesCorrected { get; set; }

    /// <summary>X-rays and CT scans done on site.</summary>
    public int ScansDone { get; internal set; }

    /// <summary>Operations done in the hospital's theatres.</summary>
    public int OperationsDone { get; internal set; }

    /// <summary>Patients in A&amp;E waiting to go to theatre.</summary>
    public int AwaitingSurgery { get; internal set; }

    /// <summary>Patients in A&amp;E waiting for a ward bed.</summary>
    public int AwaitingBed { get; internal set; }

    public int OnWards { get; internal set; }

    public (int Used, int Total) WardBeds { get; internal set; }

    public int Deaths { get; internal set; }

    public int LeftWithoutBeingSeen { get; internal set; }

    public int DiagnosesMade { get; internal set; }

    public int DiagnosesCorrect { get; internal set; }

    public int MissedDiagnoses { get; set; }

    public int ReputationPenalties { get; set; }

    public long InvestigationCost { get; set; }

    public long MedicationCost { get; set; }

    public Dictionary<TriageCategory, int> TriagedTotal { get; } = new() { [TriageCategory.Red] = 0, [TriageCategory.Amber] = 0, [TriageCategory.Green] = 0 };

    // Live counts, refreshed every tick.
    public Dictionary<TriageCategory, int> InDepartment { get; } = new();

    public Dictionary<TriageCategory, int> WaitingForSpace { get; } = new();

    public Dictionary<TriageCategory, double> LongestWaitMinutes { get; } = new();

    public int PatientsInDepartment { get; internal set; }

    public int AwaitingTriage { get; internal set; }

    public int AmbulancesWaiting { get; internal set; }

    public (int Used, int Total) Resus { get; internal set; }

    public (int Used, int Total) Majors { get; internal set; }

    public (int Used, int Total) Minors { get; internal set; }

    public double AverageTimeToDoctorMinutes => _recentTimeToDoctor.Count == 0 ? 0 : _recentTimeToDoctor.Average();

    public double AverageHandoverMinutes => _recentHandoverMinutes.Count == 0 ? 0 : _recentHandoverMinutes.Average();

    /// <summary>Share of patients leaving within four hours (the NHS A&amp;E standard).</summary>
    public double FourHourPerformance => DepartedTotal == 0 ? 1 : (double)DepartedWithinFourHours / DepartedTotal;

    public double DiagnosticAccuracy => DiagnosesMade == 0 ? 1 : (double)DiagnosesCorrect / DiagnosesMade;

    internal void RecordArrival(Patient patient)
    {
        ArrivalsTotal++;
        if (patient.ArrivalMethod == ArrivalMethod.Ambulance)
        {
            AmbulanceArrivalsTotal++;
        }
    }

    internal void AmbulanceArrived()
    {
    }

    internal void RecordTriage(TriageCategory category) => TriagedTotal[category]++;

    internal void RecordDiagnosis(bool correct)
    {
        DiagnosesMade++;
        if (correct)
        {
            DiagnosesCorrect++;
        }
    }

    internal void RecordTimeToDoctor(TimeSpan time) => Push(_recentTimeToDoctor, time.TotalMinutes);

    internal void RecordHandoverTime(TimeSpan time) => Push(_recentHandoverMinutes, time.TotalMinutes);

    /// <summary>Leaving A&amp;E for a ward stops the 4-hour clock.</summary>
    internal void RecordAdmission(Patient patient, DateTime now)
    {
        DepartedTotal++;
        Admitted++;
        if (now - patient.ArrivedAt <= TimeSpan.FromHours(4))
        {
            DepartedWithinFourHours++;
        }
    }

    internal void RecordDeparture(Patient patient, DateTime now)
    {
        if (patient.LeftEmergencyAt is not null)
        {
            // Already counted when they went up to the ward.
            if (patient.Outcome == PatientOutcome.Died)
            {
                WardDeaths++;
            }
            else
            {
                WardDischarges++;
            }

            return;
        }

        DepartedTotal++;
        if (now - patient.ArrivedAt <= TimeSpan.FromHours(4))
        {
            DepartedWithinFourHours++;
        }

        switch (patient.Outcome)
        {
            case PatientOutcome.Discharged:
                Discharged++;
                break;
            case PatientOutcome.Transferred:
            case PatientOutcome.Admitted:
                Transferred++;
                break;
            case PatientOutcome.Died:
                Deaths++;
                break;
            case PatientOutcome.LeftWithoutBeingSeen:
                LeftWithoutBeingSeen++;
                break;
        }
    }

    internal void Update(HospitalSimulation sim)
    {
        var present = sim.Patients.Where(p => p.IsInEmergency).ToList();
        AwaitingBed = present.Count(p => p.Stage == PatientStage.AwaitingBed);
        AwaitingSurgery = present.Count(p => p.Stage == PatientStage.AwaitingSurgery);
        OnWards = sim.Patients.Count(p => p.Stage == PatientStage.OnWard && p.IsAlive);
        var beds = sim.Spaces.Where(s => s.IsWard).SelectMany(s => s.Beds).ToList();
        WardBeds = (beds.Count(b => b.OccupantId is not null), beds.Count);
        PatientsInDepartment = present.Count;
        AwaitingTriage = present.Count(p => p.Triage is null);

        foreach (var category in Enum.GetValues<TriageCategory>())
        {
            InDepartment[category] = present.Count(p => p.Triage == category);
            var waiting = present.Where(p => p.Triage == category && p.Stage is PatientStage.WaitingForSpace or PatientStage.InAmbulanceQueue && p.Triage == category).ToList();
            WaitingForSpace[category] = waiting.Count;
            LongestWaitMinutes[category] = waiting.Count == 0 ? 0 : waiting.Max(p => (sim.Now - p.ArrivedAt).TotalMinutes);
        }

        AmbulancesWaiting = sim.Ambulances.Count(a => a.Purpose == AmbulancePurpose.Arrival && a.State is AmbulanceState.AtBay or AmbulanceState.WaitingForBay);

        (int, int) Usage(CareSpaceKind kind)
        {
            var spaces = sim.Spaces.Where(s => s.Kind == kind).ToList();
            return (spaces.Count(s => !s.IsFree), spaces.Count);
        }

        Resus = Usage(CareSpaceKind.Resus);
        Majors = Usage(CareSpaceKind.Majors);
        Minors = Usage(CareSpaceKind.Minors);
    }

    private static void Push(Queue<double> queue, double value)
    {
        queue.Enqueue(value);
        while (queue.Count > 20)
        {
            queue.Dequeue();
        }
    }
}
