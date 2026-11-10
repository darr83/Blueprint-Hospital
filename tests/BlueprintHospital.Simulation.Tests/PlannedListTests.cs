using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Planned lists: day-case operations, angiograms and endoscopies booked each weekday.</summary>
public class PlannedListTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A west wing with a day surgery unit, an operating theatre (open to everyone) and an endoscopy room, fully staffed.</summary>
    private static SimulationHarness Wing(int seed = 7, DateTime? start = null, bool daySurgery = true)
    {
        var h = new SimulationHarness(seed, start: start);
        h.Sim.AdjustBalance(5_000_000);
        foreach (var department in new[] { "general_surgery", "gastroenterology" })
        {
            Build(h.Sim.UnlockDepartment(department), department);
        }

        Build(h.Sim.ExtendBuilding(new GridRect(B.X - 40, B.Y, 40, 34)), "extension");
        Build(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 40, B.Y + 12, 44, 6), WallSide.South)), "corridor");
        if (daySurgery)
        {
            Build(h.Sim.BuildRoom(h.Sim.AutoPlan("day_surgery_unit", new GridRect(B.X - 40, B.Y + 2, 16, 10))), "day surgery unit");
        }

        Build(h.Sim.BuildRoom(h.Sim.AutoPlan("operating_theatre", new GridRect(B.X - 40, B.Y + 18, 12, 12))), "theatre");
        Build(h.Sim.BuildRoom(h.Sim.AutoPlan("endoscopy_room", new GridRect(B.X - 28, B.Y + 18, 12, 10))), "endoscopy room");
        foreach (var theatre in h.Sim.Spaces.Where(s => s.Kind == CareSpaceKind.Theatre).ToList())
        {
            foreach (var department in h.Sim.SurgicalDepartments)
            {
                h.Sim.SetTheatreAccess(theatre.Id, department.Id, true);
            }
        }

        h.StaffToRecommended();
        return h;
    }

    private static void Build(BuildCheck result, string what) => Assert.True(result.CanBuild, $"{what}: {string.Join("; ", result.Problems)}");

    [Fact]
    public void Planned_procedures_never_come_through_AE()
    {
        var h = new SimulationHarness();
        var planned = h.Database.Conditions.Values.Where(c => c.IsPlanned).ToList();
        Assert.True(planned.Count >= 8);
        Assert.All(planned, c => Assert.True(c.Operation!.DayCase, c.Id));
        Assert.Contains(planned, c => c.Operation!.Room == CareSpaceKind.Endoscopy);
        Assert.Contains(planned, c => c.Operation!.Room == CareSpaceKind.CathLab);

        for (var i = 0; i < 300; i++)
        {
            Assert.False(h.Sim.SpawnPatient(i % 2 == 0 ? ArrivalMethod.WalkIn : ArrivalMethod.Ambulance).Condition.IsPlanned);
        }
    }

    [Fact]
    public void Weekday_lists_are_booked_and_patients_come_in_have_their_procedure_and_go_home()
    {
        var h = Wing(start: new DateTime(2027, 1, 4, 6, 0, 0));
        h.RunFor(TimeSpan.FromMinutes(1));
        var today = h.Sim.PlannedListFor(h.Clock.Now).ToList();
        Assert.Contains(today, b => b.ConditionId == "planned_hernia_repair" || b.ConditionId == "planned_cholecystectomy");
        Assert.Contains(today, b => b.ConditionId is "planned_ogd" or "planned_colonoscopy");
        Assert.DoesNotContain(today, b => b.ConditionId == "planned_coronary_angiogram");
        Assert.All(today, b => Assert.True(b.ArrivesAt.TimeOfDay >= HospitalSimulation.PlannedFirstArrival && b.ArrivesAt.TimeOfDay <= HospitalSimulation.PlannedLastArrival));

        h.RunUntil(() => h.Sim.PlannedBookings.Any(b => b.Status == PlannedStatus.Admitted), TimeSpan.FromHours(8), "a planned patient to come in");
        var booking = h.Sim.PlannedBookings.First(b => b.Status == PlannedStatus.Admitted);
        var patient = h.Sim.FindPatient(booking.PatientId!.Value)!;
        Assert.Equal("day_surgery_unit", h.Sim.FindSpace(patient.CareSpaceId!)!.TemplateId);
        Assert.Null(patient.TriagedAt is null ? "not triaged" : null);
        Assert.False(patient.IsInEmergency);

        h.RunUntil(() => patient.Stage == PatientStage.Gone || patient.Stage == PatientStage.Leaving, TimeSpan.FromHours(14), "the planned patient to go home");
        var journey = h.Journey(patient);
        Assert.Equal(PlannedStatus.Done, booking.Status);
        Assert.Equal(PatientOutcome.Discharged, patient.Outcome);
        Assert.Contains("Came in for a planned", journey);
        Assert.True(patient.HadSurgery, journey);
        Assert.True(h.Sim.Stats.PlannedDone >= 1);
        Assert.Equal(0, h.Sim.Stats.DepartedTotal);
    }

    [Fact]
    public void An_endoscopy_list_patient_is_done_by_the_endoscopist_in_the_endoscopy_room()
    {
        var h = Wing();
        var patient = h.Sim.SpawnPlannedPatient("planned_colonoscopy")!;
        Assert.NotNull(patient);
        var room = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.Endoscopy);
        var visited = false;
        h.RunUntil(() =>
        {
            visited |= h.Sim.Staff.Any(s => s.Role == StaffRole.Endoscopist && s.BaseRoomId == room.Id && s.Activity == StaffActivity.Working) && patient.Tile.X >= room.Rect.X && patient.Tile.X < room.Rect.Right && patient.Tile.Y >= room.Rect.Y && patient.Tile.Y < room.Rect.Bottom;
            return patient.HadSurgery;
        }, TimeSpan.FromHours(4), "the colonoscopy");
        Assert.True(visited, h.Journey(patient));
        Assert.Contains("Colonoscopy performed by", h.Journey(patient));
    }

    [Fact]
    public void Without_a_day_surgery_unit_no_list_is_booked()
    {
        var h = Wing(start: new DateTime(2027, 1, 4, 6, 0, 0), daySurgery: false);
        h.RunFor(TimeSpan.FromMinutes(1));
        Assert.Empty(h.Sim.PlannedBookings);
        Assert.Contains("a day surgery unit", h.Sim.PlannedListProblems(h.Database.Conditions["planned_hernia_repair"]));
    }

    [Fact]
    public void Lists_are_not_booked_at_weekends()
    {
        var h = Wing(start: new DateTime(2027, 1, 9, 6, 0, 0));
        h.RunFor(TimeSpan.FromMinutes(1));
        Assert.Empty(h.Sim.PlannedBookings);
    }

    [Fact]
    public void A_planned_patient_whose_room_closes_is_cancelled_and_sent_home()
    {
        var h = Wing();
        var patient = h.Sim.SpawnPlannedPatient("planned_hernia_repair")!;
        var penalties = h.Sim.Stats.ReputationPenalties;
        foreach (var surgeon in h.Sim.Staff.Where(s => s.Role == StaffRole.Surgeon).ToList())
        {
            Assert.True(h.Sim.Fire(surgeon.Id));
        }

        h.RunUntil(() => patient.Stage is PatientStage.Gone or PatientStage.Leaving, TimeSpan.FromHours(10), "the cancelled patient to go home");
        Assert.Equal(PlannedStatus.Cancelled, h.Sim.PlannedBookings.Single(b => b.PatientId == patient.Id).Status);
        Assert.False(patient.HadSurgery);
        Assert.True(h.Sim.Stats.ReputationPenalties > penalties);
        Assert.Contains(h.Sim.Notifications.Recent, n => n.Text.Contains("cancelled"));
    }

    [Fact]
    public void The_day_surgery_unit_keeps_beds_free_for_emergencies()
    {
        var h = Wing(start: new DateTime(2027, 1, 9, 9, 0, 0));
        var unit = h.Sim.DaySurgeryUnits.Single();
        var admitted = Enumerable.Range(0, unit.Beds.Count).Select(_ => h.Sim.SpawnPlannedPatient("planned_ogd")).ToList();
        var reserved = Math.Max(1, unit.Beds.Count / 4);
        Assert.Equal(unit.Beds.Count - reserved, admitted.Count(p => p is not null));
        Assert.Equal(reserved, h.Sim.PlannedBookings.Count(b => b.Status == PlannedStatus.Cancelled));
    }
}
