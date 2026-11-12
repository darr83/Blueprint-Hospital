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

    private readonly Queue<double> _recentCorridorMinutes = new();

    internal Queue<double> RecentCorridorMinutes => _recentCorridorMinutes;

    /// <summary>Cardiac arrests in the hospital (A&amp;E and the wards).</summary>
    public int CardiacArrests { get; internal set; }

    /// <summary>Arrests where the heart restarted (ROSC).</summary>
    public int ArrestsRosc { get; internal set; }

    /// <summary>Patients who had an arrest here and left hospital alive.</summary>
    public int ArrestSurvivors { get; internal set; }

    internal long SecondsToCprTotal { get; set; }

    internal int CprStarts { get; set; }

    internal long SecondsToShockTotal { get; set; }

    internal int FirstShocks { get; set; }

    /// <summary>Average minutes from the heart stopping to CPR starting.</summary>
    public double AverageMinutesToCpr => CprStarts == 0 ? 0 : SecondsToCprTotal / 60.0 / CprStarts;

    /// <summary>Average minutes from the heart stopping to the first shock (shockable rhythms only).</summary>
    public double AverageMinutesToShock => FirstShocks == 0 ? 0 : SecondsToShockTotal / 60.0 / FirstShocks;

    /// <summary>Sudden emergencies for patients already in a bed (heart attacks, falls, bleeds...).</summary>
    public int WardEmergencies { get; internal set; }

    /// <summary>Ward emergencies where the treatment went in.</summary>
    public int WardEmergenciesTreated { get; internal set; }

    /// <summary>Ward emergencies where the patient settled afterwards.</summary>
    public int WardEmergenciesSettled { get; internal set; }

    internal long SecondsToEmergencyTreatmentTotal { get; set; }

    /// <summary>Average minutes from a ward emergency starting to its treatment going in.</summary>
    public double AverageMinutesToEmergencyTreatment => WardEmergenciesTreated == 0 ? 0 : SecondsToEmergencyTreatmentTotal / 60.0 / WardEmergenciesTreated;

    internal void RecordTimeToEmergencyTreatment(double seconds) => SecondsToEmergencyTreatmentTotal += (long)Math.Round(seconds);

    internal void RecordTimeToCpr(double minutes)
    {
        SecondsToCprTotal += (long)Math.Round(minutes * 60);
        CprStarts++;
    }

    internal void RecordTimeToShock(double minutes)
    {
        SecondsToShockTotal += (long)Math.Round(minutes * 60);
        FirstShocks++;
    }

    /// <summary>Ambulance patients who have spent time in corridor care (no cubicle free after handover).</summary>
    public int CorridorCareTotal { get; internal set; }

    /// <summary>Patients on trolleys in the corridor right now.</summary>
    public int InCorridor { get; internal set; }

    /// <summary>Corridor spaces in the A&amp;E corridors, taken or not.</summary>
    public int CorridorSpaces { get; internal set; }

    public double AverageCorridorMinutes => _recentCorridorMinutes.Count == 0 ? 0 : _recentCorridorMinutes.Average();

    public int ArrivalsTotal { get; internal set; }

    public int AmbulanceArrivalsTotal { get; internal set; }

    public int DepartedTotal { get; internal set; }

    public int DepartedWithinFourHours { get; internal set; }

    /// <summary>Minutes spent in A&amp;E by everyone who has left it (home, to a ward, transferred, died or walked out).</summary>
    public long MinutesInDepartmentTotal { get; internal set; }

    /// <summary>Patients seen by a doctor, and the minutes they waited from arrival in total.</summary>
    public int SeenByDoctorTotal { get; internal set; }

    public long MinutesToDoctorTotal { get; internal set; }

    /// <summary>Ambulance handovers, and the minutes they took in total.</summary>
    public int HandoversTotal { get; internal set; }

    public long HandoverMinutesTotal { get; internal set; }

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

    /// <summary>Planned day-case procedures done.</summary>
    public int PlannedDone { get; internal set; }

    /// <summary>Planned patients cancelled on the day.</summary>
    public int PlannedCancelled { get; internal set; }

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

    internal void RecordTimeToDoctor(TimeSpan time)
    {
        Push(_recentTimeToDoctor, time.TotalMinutes);
        SeenByDoctorTotal++;
        MinutesToDoctorTotal += (long)Math.Round(time.TotalMinutes);
    }

    internal void RecordHandoverTime(TimeSpan time)
    {
        Push(_recentHandoverMinutes, time.TotalMinutes);
        HandoversTotal++;
        HandoverMinutesTotal += (long)Math.Round(time.TotalMinutes);
    }

    internal void RecordCorridorTime(TimeSpan time)
    {
        CorridorCareTotal++;
        Push(_recentCorridorMinutes, time.TotalMinutes);
    }

    /// <summary>Leaving A&amp;E for a ward stops the 4-hour clock.</summary>
    internal void RecordAdmission(Patient patient, DateTime now)
    {
        DepartedTotal++;
        Admitted++;
        MinutesInDepartmentTotal += (long)Math.Round((now - patient.ArrivedAt).TotalMinutes);
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
        MinutesInDepartmentTotal += (long)Math.Round((now - patient.ArrivedAt).TotalMinutes);
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

        InCorridor = present.Count(p => p.CorridorSpot.HasValue);
        CorridorSpaces = sim.CorridorCareSpots().Count;
        AmbulancesWaiting = sim.Ambulances.Count(a => a.Purpose == AmbulancePurpose.Arrival && a.State is AmbulanceState.AtBay or AmbulanceState.WaitingForBay);

        (int, int) Usage(CareSpaceKind kind)
        {
            var spaces = sim.Spaces.Where(s => s.Kind == kind).ToList();
            return (spaces.Sum(s => s.PlacesUsed), spaces.Sum(s => s.Places));
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
