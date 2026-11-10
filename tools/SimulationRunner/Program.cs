using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;

// Usage: dotnet run --project tools/SimulationRunner -- [hours=24] [seed=42] [arrival-multiplier=1] [journeys=3]
var hours = args.Length > 0 ? double.Parse(args[0]) : 24;
var seed = args.Length > 1 ? int.Parse(args[1]) : 42;
var multiplier = args.Length > 2 ? double.Parse(args[2]) : 1.0;
var journeys = args.Length > 3 ? int.Parse(args[3]) : 3;

var dataDir = Path.Combine(FindRepoRoot(), "game", "Data");
var db = GameDatabase.Load(new FileSystemDataSource(dataDir));
var events = new EventBus();
var clock = new SimulationClock(events, new DateTime(2027, 1, 4, 0, 0, 0));
var sim = new HospitalSimulation(db, clock, events, StarterSite.Create(), new SimulationOptions { Seed = seed, ArrivalRateMultiplier = multiplier, AutomaticArrivals = true });

// A new game has no waiting room; give A&E its usual fitted-out one so patients can arrive.
sim.BuildStarterWaitingRoom();
clock.Ticked += _ => sim.Tick();

var ticks = (long)(hours * 3600);
for (var i = 0; i < ticks; i++)
{
    clock.Step();
    if (i % 3600 == 0)
    {
        var s = sim.Stats;
        Console.WriteLine($"{clock.Now:ddd HH:mm}  in dept {s.PatientsInDepartment,3}  R/A/G {s.InDepartment.GetValueOrDefault(TriageCategory.Red)}/{s.InDepartment.GetValueOrDefault(TriageCategory.Amber)}/{s.InDepartment.GetValueOrDefault(TriageCategory.Green)}  " +
                          $"await triage {s.AwaitingTriage,2}  amb waiting {s.AmbulancesWaiting}  resus {s.Resus.Used}/{s.Resus.Total} majors {s.Majors.Used}/{s.Majors.Total} minors {s.Minors.Used}/{s.Minors.Total}  tasks {sim.Tasks.Count()}");
    }
}

var st = sim.Stats;
Console.WriteLine();
Console.WriteLine($"Arrivals {st.ArrivalsTotal} ({st.AmbulanceArrivalsTotal} by ambulance). Left {st.DepartedTotal}: discharged {st.Discharged}, transferred {st.Transferred}, died {st.Deaths}, LWBS {st.LeftWithoutBeingSeen}.");
Console.WriteLine($"4-hour performance {st.FourHourPerformance:P0}. Avg time to doctor {st.AverageTimeToDoctorMinutes:0} min. Avg handover {st.AverageHandoverMinutes:0} min.");
Console.WriteLine($"Diagnoses {st.DiagnosesMade}, accuracy {st.DiagnosticAccuracy:P0}, missed {st.MissedDiagnoses}. Triage R/A/G {st.TriagedTotal[TriageCategory.Red]}/{st.TriagedTotal[TriageCategory.Amber]}/{st.TriagedTotal[TriageCategory.Green]}.");
Console.WriteLine($"Investigation spend £{st.InvestigationCost}, medication spend £{st.MedicationCost}.");
Console.WriteLine($"Balance £{sim.Balance:N0} (started £{BlueprintHospital.Simulation.Simulation.HospitalSimulation.StartingBalance:N0}). Staff £{sim.StaffCostPerDay:N0}/day, upkeep £{sim.UpkeepPerDay:N0}/day.");
foreach (var day in sim.Accounts.Reverse())
{
    Console.WriteLine($"  {day.Date:ddd}: in £{day.Income:N0}, out £{-day.Spending:N0}, net £{day.Net:N0}  ({string.Join(", ", day.Amounts.Select(a => $"{a.Key} {a.Value:N0}"))})");
}
foreach (var s in sim.Staff)
{
    Console.WriteLine($"  {s.Grade,-30} {s.Name,-24} tasks {s.TasksCompleted,4} fatigue {s.Fatigue:0.00} {s.StatusText}");
}

foreach (var p in sim.DepartedPatients.OrderByDescending(p => p.Outcome == BlueprintHospital.Simulation.Patients.PatientOutcome.Died).Take(journeys))
{
    Console.WriteLine();
    Console.WriteLine($"=== {p.FullName}, {p.Age}{p.Sex.ToString()[0]}, {p.Condition.Name} -> {p.Outcome} ({p.TimeInDepartment(clock.Now).TotalMinutes:0} min)");
    foreach (var t in p.Timeline)
    {
        Console.WriteLine($"  {t.Time:HH:mm} {t.Text}");
    }
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "game", "Data")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? throw new DirectoryNotFoundException("Run from inside the repository.");
}
