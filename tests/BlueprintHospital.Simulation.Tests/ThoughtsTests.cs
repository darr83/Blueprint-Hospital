using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>What patients think, do and want, and the PALS office they take their worries to.</summary>
public class ThoughtsTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A busy A&amp;E (arrivals at three times the usual rate) with a staffed PALS office in a west wing.</summary>
    private static (SimulationHarness H, CareSpace Office) WithPals(int seed)
    {
        var h = new SimulationHarness(seed, arrivals: true, arrivalMultiplier: 3, start: new DateTime(2027, 4, 6, 7, 0, 0));
        h.Sim.AdjustBalance(2_000_000);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        var check = h.Sim.BuildRoom(h.Sim.AutoPlan("pals_office", new GridRect(B.X - 30, B.Y + 17, 9, 9)));
        Assert.True(check.CanBuild, string.Join("; ", check.Problems));
        var office = h.Sim.PalsOffices.Single();
        Assert.Contains(h.Sim.RecommendedStaffing(), n => n.RoomId == office.Id && n.Role == StaffRole.PalsOfficer && n.Short > 0);
        h.StaffToRecommended();
        return (h, office);
    }

    [Fact]
    public void Every_waiting_patient_has_something_on_their_mind()
    {
        var h = new SimulationHarness(61);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration");
        h.RunFor(TimeSpan.FromMinutes(2));
        var thought = h.Sim.ThoughtOf(patient);
        Assert.NotNull(thought);
        Assert.False(string.IsNullOrWhiteSpace(thought!.Text));
        Assert.True(thought.Text.Length <= 34, thought.Text);
    }

    [Fact]
    public void Patients_in_a_busy_department_think_doing_wanting_and_unhappy_thoughts()
    {
        var h = new SimulationHarness(62, arrivals: true, arrivalMultiplier: 3);
        var kinds = new HashSet<ThoughtKind>();
        for (var i = 0; i < 40; i++)
        {
            h.RunFor(TimeSpan.FromMinutes(15));
            foreach (var p in h.Sim.Patients)
            {
                if (h.Sim.ThoughtOf(p) is { } thought)
                {
                    kinds.Add(thought.Kind);
                    Assert.False(string.IsNullOrWhiteSpace(thought.Text));
                }
            }
        }

        Assert.Contains(ThoughtKind.Doing, kinds);
        Assert.Contains(ThoughtKind.Wanting, kinds);
        Assert.Contains(ThoughtKind.Unhappy, kinds);
    }

    [Fact]
    public void A_PALS_office_needs_a_PALS_officer_and_fed_up_patients_go_and_talk_to_them()
    {
        var (h, office) = WithPals(63);
        Assert.Contains(h.Sim.Staff, s => s.Role == StaffRole.PalsOfficer && s.BaseRoomId == office.Id);
        Assert.True(h.Sim.HasPalsOfficer);
        h.RunUntil(() => h.Sim.Patients.Concat(h.Sim.DepartedPatients).Any(p => p.PalsListened), TimeSpan.FromHours(10), "a patient to be listened to by PALS");

        var listened = h.Sim.Patients.Concat(h.Sim.DepartedPatients).First(p => p.PalsListened);
        Assert.Contains(listened.Timeline, t => t.Text.StartsWith("Went to the PALS office", StringComparison.Ordinal));
        Assert.Contains(listened.Timeline, t => t.Text.Contains("from PALS listened", StringComparison.Ordinal) || t.Text.StartsWith("PALS listened", StringComparison.Ordinal));
    }

    [Fact]
    public void Being_listened_to_by_PALS_lifts_a_rating_and_makes_an_apology_more_likely_to_land()
    {
        var h = new SimulationHarness(64);
        var p = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration");
        p.LeftEmergencyAt = p.ArrivedAt.AddHours(5);
        p.FirstSeenByDoctorAt = p.ArrivedAt.AddHours(2);
        var (without, _, _) = h.Sim.JudgeStay(p, PatientOutcome.Discharged);
        p.PalsListened = true;
        var (with, reasons, _) = h.Sim.JudgeStay(p, PatientOutcome.Discharged);
        Assert.True(with >= without, $"{with} vs {without}");
        Assert.Contains("pals", reasons);

        var plain = h.Sim.ChanceToSettle(ComplaintResponse.Apologise, ComplaintSeverity.Formal);
        Assert.Equal(HospitalSimulation.SatisfyChance(ComplaintResponse.Apologise, ComplaintSeverity.Formal), plain);
    }

    [Fact]
    public void A_PALS_officer_helps_apologies_land()
    {
        var (h, _) = WithPals(65);
        Assert.Equal(HospitalSimulation.SatisfyChance(ComplaintResponse.Apologise, ComplaintSeverity.Formal) + HospitalSimulation.PalsApologyBonus,
            h.Sim.ChanceToSettle(ComplaintResponse.Apologise, ComplaintSeverity.Formal), 6);
    }

    [Fact]
    public void Having_been_to_PALS_is_saved()
    {
        var h = new SimulationHarness(66);
        var p = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration");
        p.PalsListened = true;
        var json = SaveSerializer.Serialize(new SaveGame { Clock = h.Clock.CaptureState(), Layout = h.Sim.CaptureLayout(), People = h.Sim.CapturePeople() });
        var save = SaveSerializer.Deserialize(json);
        var to = new SimulationHarness(seed: 99);
        to.Clock.RestoreState(save.Clock);
        to.Sim.RestoreLayout(save.Layout!);
        to.Sim.RestorePeople(save.People!);
        Assert.True(to.Sim.Patients.Single(x => x.Id == p.Id).PalsListened);
    }
}
