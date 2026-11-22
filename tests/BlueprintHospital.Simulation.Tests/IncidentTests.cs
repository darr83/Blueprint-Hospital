using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using Xunit;

namespace BlueprintHospital.Simulation.Tests;

public class IncidentTests
{
    [Fact]
    public void Every_kind_of_incident_has_casualties_with_known_conditions()
    {
        var db = new SimulationHarness().Database;
        Assert.True(db.Incidents.Count >= 8);
        Assert.Contains("road_collision", db.Incidents.Keys);
        foreach (var incident in db.Incidents.Values)
        {
            Assert.All(incident.Casualties.SelectMany(c => c.Conditions), id => Assert.True(db.Conditions.ContainsKey(id), $"{incident.Id}: {id}"));
        }

        foreach (var id in new[] { "multiple_trauma", "traumatic_pneumothorax", "ruptured_spleen", "pelvic_fracture", "major_head_injury", "major_burns", "smoke_inhalation", "carbon_monoxide_poisoning", "crush_injury", "chemical_eye_injury" })
        {
            Assert.True(db.Conditions.ContainsKey(id), id);
        }
    }

    [Fact]
    public void A_road_collision_brings_five_casualties_after_a_standby()
    {
        var h = new SimulationHarness(seed: 31);
        var incident = h.Sim.StartIncident("road_collision")!;
        Assert.Equal(5, incident.Casualties.Count);
        Assert.Equal(1, incident.Count(TriageCategory.Red));
        Assert.Equal(2, incident.Count(TriageCategory.Amber));
        Assert.Equal(2, incident.Count(TriageCategory.Green));
        Assert.InRange((incident.FirstArrival - h.Clock.Now).TotalMinutes, 15, 30);
        Assert.True(incident.OnStandby(h.Clock.Now));
        Assert.Contains(h.Sim.Notifications.Recent, n => n.Level == NotificationLevel.Critical && n.Text.StartsWith("Major incident standby", StringComparison.Ordinal));

        h.RunUntil(() => incident.Casualties.All(c => c.PatientId is not null), TimeSpan.FromHours(2), "every casualty here");
        var red = incident.Casualties.Single(c => c.Triage == TriageCategory.Red);
        var patient = h.Sim.FindPatient(red.PatientId!.Value)!;
        Assert.Equal(red.ConditionId, patient.Condition.Id);
        Assert.Equal(ArrivalMethod.Ambulance, patient.ArrivalMethod);
        Assert.Contains(patient.Timeline, e => e.Text.StartsWith("Casualty", StringComparison.Ordinal));
    }

    [Fact]
    public void The_trauma_team_waits_in_Resus_until_the_first_casualty_is_there()
    {
        var h = new SimulationHarness(seed: 32);
        h.StaffToRecommended();
        var incident = h.Sim.StartIncident("road_collision")!;
        var result = h.Sim.DoIncidentAction(HospitalSimulation.TraumaTeamAction);
        Assert.StartsWith("Trauma team called", result);
        Assert.NotEmpty(incident.TraumaTeamIds);
        var team = incident.TraumaTeamIds.Select(id => h.Sim.FindStaff(id)!).ToList();
        Assert.Contains(team, m => m.Role == StaffRole.Doctor);

        h.RunUntil(() => incident.TraumaTeamReadyAt is not null, TimeSpan.FromMinutes(14), "the team in Resus");
        Assert.True(incident.TraumaTeamReadyAt < incident.FirstArrival);
        Assert.All(team, m => Assert.Equal("Trauma team: waiting in Resus for the major incident", m.StatusText));

        // Doing it again does nothing.
        Assert.Equal("Already done.", h.Sim.DoIncidentAction(HospitalSimulation.TraumaTeamAction));
    }

    [Fact]
    public void Off_duty_staff_called_in_come_to_work_on_overtime_and_go_home_after()
    {
        var h = new SimulationHarness(seed: 33);
        h.StaffToRecommended();
        h.RunFor(TimeSpan.FromMinutes(5));
        var incident = h.Sim.StartIncident("house_fire")!;
        var info = h.Sim.IncidentActionsNow().Single(a => a.Id == HospitalSimulation.CallInStaffAction);
        Assert.True(info.Available);
        h.Sim.DoIncidentAction(HospitalSimulation.CallInStaffAction);
        Assert.InRange(incident.CalledInStaffIds.Count, 1, HospitalSimulation.MaxCalledIn);
        var called = incident.CalledInStaffIds.Select(id => h.Sim.FindStaff(id)!).ToList();
        Assert.All(called, m => Assert.NotEqual(h.Sim.CurrentShift, m.Shift));
        h.RunFor(TimeSpan.FromMinutes(30));
        Assert.All(called, m => Assert.True(m.IsOnDuty, $"{m.Name} is {m.Activity}"));
        Assert.True(incident.OvertimeCost > 0);

        h.Sim.RunCheat("incident end");
        Assert.False(incident.IsActive);
        Assert.NotNull(incident.Debrief);
        h.RunUntil(() => called.All(m => !m.IsOnDuty || m.Activity == StaffActivity.GoingHome), TimeSpan.FromHours(2), "the called-in staff going home");
    }

    [Fact]
    public void Fit_ward_patients_can_go_home_early()
    {
        var h = new SimulationHarness(seed: 34);
        h.StaffToRecommended();
        h.Sim.StartIncident("road_collision");
        var info = h.Sim.IncidentActionsNow().Single(a => a.Id == HospitalSimulation.DischargeEarlyAction);
        Assert.False(info.Done);
        Assert.Equal("Nothing to do.", h.Sim.DoIncidentAction(HospitalSimulation.DischargeEarlyAction));
    }

    [Fact]
    public void The_incident_stands_down_with_a_debrief_when_the_casualties_have_left_AandE()
    {
        var h = new SimulationHarness(seed: 35);
        h.StaffToRecommended();
        var incident = h.Sim.StartIncident("carbon_monoxide_family")!;
        h.Sim.DoIncidentAction(HospitalSimulation.TraumaTeamAction);
        h.RunUntil(() => !incident.IsActive, TimeSpan.FromHours(30), "the incident to stand down");
        var debrief = incident.Debrief!;
        Assert.Equal(incident.Casualties.Count, debrief.Total);
        Assert.InRange(debrief.Stars, 1, 5);
        Assert.False(string.IsNullOrWhiteSpace(debrief.Verdict));
        Assert.Contains(h.Sim.Notifications.Recent, n => n.Text.StartsWith("Major incident stood down", StringComparison.Ordinal));
    }

    [Fact]
    public void Incidents_only_come_when_they_are_turned_on()
    {
        Assert.False(new SimulationHarness(seed: 36).Sim.IncidentsPossible);
        var rare = new SimulationHarness(seed: 36, incidents: MajorIncidentFrequency.Rare).Sim;
        Assert.True(rare.IncidentsPossible);
        Assert.Equal(30, rare.DaysBetweenIncidents);
        Assert.Equal(10, new SimulationHarness(seed: 36, incidents: MajorIncidentFrequency.Realistic).Sim.DaysBetweenIncidents);

        // On Realistic, about one every ten days.
        var realistic = new SimulationHarness(seed: 37, incidents: MajorIncidentFrequency.Realistic).Sim;
        Assert.InRange(realistic.IncidentChancePerHour(), 0.5 / 240, 2.0 / 240);
        Assert.InRange(rare.IncidentChancePerHour(), 0.5 / 720, 2.0 / 720);
    }

    [Fact]
    public void An_incident_on_standby_survives_save_and_load()
    {
        var from = new SimulationHarness(seed: 38, incidents: MajorIncidentFrequency.Realistic);
        var incident = from.Sim.StartIncident("road_collision")!;
        var json = SaveSerializer.Serialize(new SaveGame { Clock = from.Clock.CaptureState(), Layout = from.Sim.CaptureLayout(), People = from.Sim.CapturePeople() });
        var save = SaveSerializer.Deserialize(json);
        var to = new SimulationHarness(seed: 99);
        to.Clock.RestoreState(save.Clock);
        to.Sim.RestoreLayout(save.Layout!);
        to.Sim.RestorePeople(save.People!);
        Assert.Equal(MajorIncidentFrequency.Realistic, to.Sim.MajorIncidents);
        var loaded = to.Sim.ActiveIncident!;
        Assert.Equal(incident.Name, loaded.Name);
        Assert.Equal(incident.Casualties.Select(c => c.ConditionId), loaded.Casualties.Select(c => c.ConditionId));
        Assert.Equal(incident.FirstArrival, loaded.FirstArrival);
    }
}
