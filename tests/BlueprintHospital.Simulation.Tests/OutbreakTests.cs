using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Norovirus on the wards, deep cleans, closures, and the scripted major incident of a Career level.</summary>
public class OutbreakTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A west wing with a medical ward (and, if asked, a cleaning room and so a cleaner).</summary>
    private static (SimulationHarness H, CareSpace Ward) WithWard(bool cleaner, int seed = 61)
    {
        var h = new SimulationHarness(seed);
        h.Sim.AdjustBalance(5_000_000);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_ward", new GridRect(B.X - 30, B.Y + 17, 26, 15))).CanBuild);
        if (cleaner)
        {
            var check = h.Sim.BuildRoom(h.Sim.AutoPlan("cleaning_room", new GridRect(B.X - 11, B.Y + 3, 5, 6)));
            Assert.True(check.CanBuild, string.Join("; ", check.Problems));
        }

        h.StaffToRecommended();
        Assert.Equal(cleaner, h.Sim.Staff.Any(s => s.Role == StaffRole.Cleaner));
        return (h, h.Sim.Wards.Single(w => w.TemplateId == "medical_ward"));
    }

    private static List<Patient> OnWard(SimulationHarness h, CareSpace ward, int count)
    {
        var patients = Enumerable.Range(0, count).Select(_ => h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia")).ToList();
        h.RunUntil(() => patients.All(p => p.Stage is PatientStage.OnWard or PatientStage.Gone), TimeSpan.FromHours(12), "the patients to reach the ward");
        var there = patients.Where(p => p.Stage == PatientStage.OnWard && p.CareSpaceId == ward.Id).ToList();
        Assert.True(there.Count == count, string.Join(Environment.NewLine + Environment.NewLine, patients.Select(h.Journey)));
        foreach (var p in there)
        {
            // Long enough that nobody goes home during the test.
            p.DischargeDueAt = h.Clock.Now.AddDays(5);
        }

        return there;
    }

    [Fact]
    public void Without_a_deep_clean_norovirus_spreads_and_three_cases_close_the_ward_to_admissions()
    {
        var (h, ward) = WithWard(cleaner: false);
        var patients = OnWard(h, ward, 4);
        Assert.True(h.Sim.RunCheat("norovirus").Succeeded);
        var outbreak = h.Sim.OutbreakOn(ward.Id)!;
        Assert.Single(outbreak.CaseIds);
        Assert.False(h.Sim.IsWardClosed(ward.Id));
        Assert.Contains(h.Sim.Notifications.Recent, n => n.Text.StartsWith($"Norovirus on {ward.Name}", StringComparison.Ordinal));

        // Nobody deep-cleans it, so it spreads every few hours until the ward closes.
        h.RunUntil(() => h.Sim.IsWardClosed(ward.Id), TimeSpan.FromHours(10), "the ward to close");
        Assert.Equal(HospitalSimulation.OutbreakCasesToClose, outbreak.CaseIds.Count);
        Assert.Equal(HospitalSimulation.OutbreakCasesToClose, patients.Count(p => p.Norovirus));
        Assert.Contains(patients.First(p => p.Norovirus).Timeline, e => e.Text.Contains("norovirus", StringComparison.Ordinal));
        Assert.Contains(h.Sim.Notifications.Recent, n => n.Text.StartsWith($"{ward.Name} is closed to new admissions", StringComparison.Ordinal));
        Assert.Contains(h.Sim.ReadPressure().Reasons, r => r.Contains("norovirus", StringComparison.Ordinal));

        // A new admission doesn't go to the closed ward.
        var next = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        h.RunFor(TimeSpan.FromHours(3));
        Assert.False(next.CareSpaceId == ward.Id && next.Stage == PatientStage.OnWard, h.Journey(next));
        Assert.NotEqual(ward.Id, next.ReservedWardId);
    }

    [Fact]
    public void A_cleaner_deep_cleans_the_ward_in_time_and_it_goes_no_further()
    {
        var (h, ward) = WithWard(cleaner: true);
        var patients = OnWard(h, ward, 4);
        Assert.True(h.Sim.RunCheat("norovirus").Succeeded);
        var outbreak = h.Sim.OutbreakOn(ward.Id)!;
        h.RunUntil(() => outbreak.Cleaned, HospitalSimulation.DeepCleanWithin, "the cleaner to deep-clean the ward");
        Assert.Contains(h.Sim.Notifications.Recent, n => n.Text.StartsWith($"{ward.Name} has been deep-cleaned", StringComparison.Ordinal));
        h.RunFor(TimeSpan.FromHours(10));
        Assert.Single(outbreak.CaseIds);
        Assert.Single(patients, p => p.Norovirus);
        Assert.False(h.Sim.IsWardClosed(ward.Id));
    }

    [Fact]
    public void An_outbreak_and_who_has_it_survive_save_and_load()
    {
        var (from, ward) = WithWard(cleaner: false);
        OnWard(from, ward, 3);
        Assert.True(from.Sim.RunCheat("norovirus close").Succeeded);
        Assert.True(from.Sim.IsWardClosed(ward.Id));

        var json = SaveSerializer.Serialize(new SaveGame { Clock = from.Clock.CaptureState(), Layout = from.Sim.CaptureLayout(), People = from.Sim.CapturePeople() });
        var save = SaveSerializer.Deserialize(json);
        var to = new SimulationHarness(seed: 99, waitingRoom: false);
        to.Clock.RestoreState(save.Clock);
        to.Sim.RestoreLayout(save.Layout!);
        to.Sim.RestorePeople(save.People!);
        Assert.True(to.Sim.IsWardClosed(ward.Id));
        Assert.Equal(from.Sim.OutbreakOn(ward.Id)!.CaseIds, to.Sim.OutbreakOn(ward.Id)!.CaseIds);
        Assert.Equal(3, to.Sim.Patients.Count(p => p.Norovirus));
    }

    [Fact]
    public void Norovirus_only_breaks_out_by_itself_with_seasons_and_from_acute_medicine_in_a_career()
    {
        Assert.Equal(4, new SimulationHarness(type: GameType.Career).Sim.LevelOfFeature(LevelFeatures.Outbreaks));
        Assert.False(new SimulationHarness().Sim.OutbreaksActive);
        Assert.True(new SimulationHarness(seasons: true).Sim.OutbreaksActive);
    }

    [Fact]
    public void Level_3_brings_a_scripted_road_crash_that_counts_towards_its_goals()
    {
        var h = new SimulationHarness(seed: 62, type: GameType.Career, incidents: MajorIncidentFrequency.Rare);
        h.Sim.SetLevel(3);
        h.Sim.AdjustBalance(100_000_000);
        new CareerHospitalBuilder(h).BuildUpTo(3);
        Assert.Contains(h.Sim.Spaces, s => s.Kind == CareSpaceKind.Resus);
        var goal = h.Database.Levels[2].Goals.Single(g => g.Stat == StatKeys.IncidentsHandled);
        Assert.False(h.Sim.Goals.Single(g => g.Goal == goal).Done);

        h.RunUntil(() => h.Sim.ActiveIncident is not null, TimeSpan.FromHours(14), "the scripted major incident");
        Assert.Equal("road_collision", h.Sim.ActiveIncident!.IncidentId);
        Assert.True(h.Sim.RunCheat("incident end").Succeeded);
        h.RunFor(TimeSpan.FromMinutes(2));
        Assert.True(h.Sim.Goals.Single(g => g.Goal == goal).Done, $"{h.Sim.Goals.Single(g => g.Goal == goal).Detail} total {h.Sim.History.Total(StatsPeriod.AllTime, h.Clock.Now, StatKeys.IncidentsHandled)} debrief {h.Sim.Incident?.Debrief is not null} level {h.Sim.Level}");

        // It happens once only.
        h.RunFor(TimeSpan.FromHours(14));
        Assert.False(h.Sim.ActiveIncident is { IncidentId: "road_collision" });
    }

    [Fact]
    public void With_major_incidents_off_the_level_3_incident_goal_is_already_met()
    {
        var h = new SimulationHarness(seed: 63, type: GameType.Career, incidents: MajorIncidentFrequency.Off);
        h.Sim.SetLevel(3);
        var goal = h.Database.Levels[2].Goals.Single(g => g.Stat == StatKeys.IncidentsHandled);
        Assert.True(h.Sim.Goals.Single(g => g.Goal == goal).Done);
    }
}
