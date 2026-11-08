using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Critical Care (ICU, HDU, outreach), anaesthetists, and General Surgery (SAU, day surgery).</summary>
public class CriticalCareTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A west wing with a corridor and rooms north and south of it, theatres open to every department, and the recommended staff hired.</summary>
    private static SimulationHarness Wing(int seed, (string Template, int W, int D)[] north, (string Template, int W, int D)[] south, bool staff = true)
    {
        var h = new SimulationHarness(seed);
        h.Sim.AdjustBalance(5_000_000);
        foreach (var department in new[] { "general_surgery", "critical_care" })
        {
            Build(h.Sim.UnlockDepartment(department), department);
        }

        Build(h.Sim.ExtendBuilding(new GridRect(B.X - 40, B.Y, 40, 34)), "extension");
        Build(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 40, B.Y + 12, 44, 6), WallSide.South)), "corridor");
        var x = B.X - 40;
        foreach (var (template, w, d) in north)
        {
            Build(h.Sim.BuildRoom(h.Sim.AutoPlan(template, new GridRect(x, B.Y + 12 - d, w, d))), template);
            x += w;
        }

        x = B.X - 40;
        foreach (var (template, w, d) in south)
        {
            Build(h.Sim.BuildRoom(h.Sim.AutoPlan(template, new GridRect(x, B.Y + 18, w, d))), template);
            x += w;
        }

        foreach (var theatre in h.Sim.Spaces.Where(s => s.Kind == CareSpaceKind.Theatre).ToList())
        {
            foreach (var department in h.Sim.SurgicalDepartments)
            {
                h.Sim.SetTheatreAccess(theatre.Id, department.Id, true);
            }
        }

        if (staff)
        {
            h.StaffToRecommended();
        }

        return h;
    }

    private static void Build(BuildCheck result, string what) => Assert.True(result.CanBuild, $"{what}: {string.Join("; ", result.Problems)}");

    private static CareSpace Room(SimulationHarness h, string template) => h.Sim.Spaces.Single(s => s.TemplateId == template);

    // ---------------------------------------------------------------- data

    [Fact]
    public void General_Surgery_owns_the_surgical_conditions_and_can_use_theatres()
    {
        var h = new SimulationHarness();
        foreach (var id in new[] { "acute_appendicitis", "acute_cholecystitis", "biliary_colic", "acute_pancreatitis", "small_bowel_obstruction", "perforated_peptic_ulcer", "strangulated_inguinal_hernia", "perianal_abscess" })
        {
            Assert.Equal("general_surgery", h.Database.Conditions[id].Department);
        }

        Assert.Equal("gastroenterology", h.Database.Conditions["upper_gi_bleed"].Department);
        Assert.Contains(h.Sim.SurgicalDepartments, d => d.Id == "general_surgery");
        Assert.True(h.Database.Conditions["perforated_peptic_ulcer"].Operation!.Major);
        Assert.True(h.Database.Conditions["perianal_abscess"].Operation!.DayCase);
        Assert.True(h.Database.Conditions["septic_shock"].NeedsIntensiveCare);
        Assert.Equal(WardUnit.IntensiveCare, h.Database.RoomTemplates["intensive_care_unit"].Unit);
        Assert.Equal(WardUnit.Assessment, h.Database.RoomTemplates["surgical_assessment_unit"].Unit);
    }

    [Fact]
    public void Intensive_care_beds_each_have_a_ventilator_and_monitor()
    {
        var h = Wing(1, Array.Empty<(string, int, int)>(), new[] { ("intensive_care_unit", 23, 11), ("high_dependency_unit", 16, 11) }, staff: false);
        var icu = Room(h, "intensive_care_unit");
        var hdu = Room(h, "high_dependency_unit");
        Assert.True(icu.Beds.Count >= 3, $"{icu.Beds.Count} beds");
        Assert.Equal(icu.Beds.Count, icu.Furniture.Count(f => f.ItemId == "ventilator"));
        Assert.True(icu.Furniture.Count(f => f.ItemId == "obs_monitor") >= icu.Beds.Count);
        Assert.True(hdu.Furniture.Count(f => f.ItemId == "obs_monitor") >= hdu.Beds.Count);

        var needs = h.Sim.RecommendedStaffing().Where(n => n.Shift == StaffShift.Day).ToList();
        Assert.Contains(needs, n => n.RoomId == icu.Id && n.Role == StaffRole.Nurse && n.Recommended == icu.Beds.Count);
        Assert.Contains(needs, n => n.RoomId == icu.Id && n.Role == StaffRole.Anaesthetist);
        Assert.Contains(needs, n => n.RoomId == hdu.Id && n.Role == StaffRole.Nurse && n.Recommended == (hdu.Beds.Count + 1) / 2);
        Assert.Contains(needs, n => n.RoomId == hdu.Id && n.Role == StaffRole.Doctor);
        Assert.Contains(h.Sim.PostingsFor(StaffRole.Anaesthetist), p => p.RoomId == icu.Id);
    }

    // ---------------------------------------------------------------- anaesthetists

    [Fact]
    public void Theatres_need_an_anaesthetist_as_well_as_a_surgeon()
    {
        var h = Wing(7, Array.Empty<(string, int, int)>(), new[] { ("operating_theatre", 12, 12), ("surgical_ward", 16, 10) });
        var theatre = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.Theatre);
        Assert.Contains(h.Sim.RecommendedStaffing(), n => n.RoomId == theatre.Id && n.Role == StaffRole.Anaesthetist);
        Assert.True(h.Sim.IsStaffed(theatre));
        Assert.True(h.Sim.HasTheatreFor("general_surgery"));

        foreach (var anaesthetist in h.Sim.Staff.Where(s => s.Role == StaffRole.Anaesthetist).ToList())
        {
            h.Sim.Fire(anaesthetist.Id);
        }

        Assert.False(h.Sim.IsStaffed(theatre));
        Assert.False(h.Sim.HasTheatreFor("general_surgery"));
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");
        h.RunUntil(() => patient.Stage is PatientStage.AwaitingTransfer or PatientStage.Gone or PatientStage.OnWard, TimeSpan.FromHours(16), "appendicitis to be dealt with");
        if (patient.WorkingDiagnosis?.ConditionId == "acute_appendicitis")
        {
            Assert.Contains("needs both a surgeon and an anaesthetist", h.Journey(patient));
        }
    }

    [Fact]
    public void The_anaesthetist_stays_in_theatre_for_the_whole_operation()
    {
        var h = Wing(7, Array.Empty<(string, int, int)>(), new[] { ("operating_theatre", 12, 12), ("surgical_ward", 16, 10) });
        var theatre = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.Theatre);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");

        var assisted = false;
        h.RunUntil(() =>
        {
            var op = h.Sim.Tasks.FirstOrDefault(t => t.Kind == TaskKind.Surgery && t.PatientId == patient.Id && t.AssignedStaffId is not null);
            if (op is not null && h.Sim.Staff.Any(s => s.Role == StaffRole.Anaesthetist && s.AssistingTaskId == op.Id && s.BaseRoomId == theatre.Id))
            {
                assisted = true;
            }

            return patient.HadSurgery || patient.Stage is PatientStage.Gone or PatientStage.AwaitingTransfer;
        }, TimeSpan.FromHours(16), "the operation");

        if (!patient.HadSurgery)
        {
            return; // Misdiagnosed in A&E: covered by the diagnosis tests.
        }

        Assert.True(assisted, h.Journey(patient));
        Assert.Contains("Anaesthetic given by", h.Journey(patient));
        h.RunFor(TimeSpan.FromMinutes(1));
        Assert.DoesNotContain(h.Sim.Staff, s => s.AssistingTaskId is not null);
    }

    // ---------------------------------------------------------------- General Surgery

    [Fact]
    public void Appendicitis_waits_on_the_surgical_assessment_unit_then_recovers_on_the_surgical_ward()
    {
        var h = Wing(7, new[] { ("surgical_assessment_unit", 16, 10) }, new[] { ("operating_theatre", 12, 12), ("surgical_ward", 16, 10) });
        var sau = Room(h, "surgical_assessment_unit");
        var ward = Room(h, "surgical_ward");
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");

        var waitedOnSau = false;
        h.RunUntil(() =>
        {
            waitedOnSau |= patient.PreOp && patient.Stage == PatientStage.OnWard && patient.CareSpaceId == sau.Id;
            return (patient.HadSurgery && patient.CareSpaceId == ward.Id && patient.WardMoveRequestedAt is null && !h.Sim.Tasks.Any(t => t.PatientId == patient.Id && t.Kind == TaskKind.EscortToWard)) || patient.Stage is PatientStage.Gone or PatientStage.AwaitingTransfer;
        }, TimeSpan.FromHours(20), "appendicitis to reach the surgical ward");

        if (patient.WorkingDiagnosis?.ConditionId != "acute_appendicitis" && !patient.HadSurgery)
        {
            return;
        }

        var journey = h.Journey(patient);
        Assert.True(waitedOnSau, journey);
        Assert.Contains("waits on Surgical assessment unit", journey);
        Assert.True(journey.Contains("Moved to Surgical ward"), journey);
        Assert.Equal(1, h.Sim.Stats.OperationsDone);
        Assert.All(sau.Beds, b => Assert.NotEqual(patient.Id, b.OccupantId));
    }

    [Fact]
    public void A_perforated_ulcer_recovers_on_HDU_then_steps_down_to_the_surgical_ward()
    {
        var h = Wing(3, new[] { ("high_dependency_unit", 16, 10) }, new[] { ("operating_theatre", 12, 12), ("surgical_ward", 16, 10) });
        var hdu = Room(h, "high_dependency_unit");
        var ward = Room(h, "surgical_ward");
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "perforated_peptic_ulcer");

        var onHdu = false;
        h.RunUntil(() =>
        {
            onHdu |= patient.HadSurgery && patient.Stage == PatientStage.OnWard && patient.CareSpaceId == hdu.Id;
            return (onHdu && patient.CareSpaceId == ward.Id) || patient.Stage is PatientStage.Gone or PatientStage.AwaitingTransfer;
        }, TimeSpan.FromHours(40), "the ulcer patient to step down to the ward");

        if (!patient.HadSurgery)
        {
            return;
        }

        var journey = h.Journey(patient);
        Assert.True(onHdu, journey);
        Assert.Contains("needs high dependency care to recover", journey);
        Assert.Contains("ready to step down", journey);
        Assert.Equal(ward.Id, patient.CareSpaceId);
        Assert.True(patient.HighDependencyHours > 0);
    }

    [Fact]
    public void An_abscess_is_drained_as_a_day_case_and_goes_home_the_same_day()
    {
        var h = Wing(4, new[] { ("day_surgery_unit", 16, 10) }, new[] { ("operating_theatre", 12, 12), ("surgical_ward", 16, 10) });
        var unit = Room(h, "day_surgery_unit");
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "perianal_abscess");

        var onUnit = false;
        h.RunUntil(() =>
        {
            onUnit |= patient.Stage == PatientStage.OnWard && patient.CareSpaceId == unit.Id;
            return patient.Stage is PatientStage.Gone or PatientStage.Leaving or PatientStage.AwaitingTransfer;
        }, TimeSpan.FromHours(30), "the abscess patient to go home");

        if (!patient.HadSurgery)
        {
            return;
        }

        var journey = h.Journey(patient);
        Assert.True(onUnit, journey);
        Assert.Equal(PatientOutcome.Discharged, patient.Outcome);
        Assert.True(patient.LeftAt - patient.AdmittedAt < TimeSpan.FromHours(12), journey);
        Assert.Contains("well enough to go home today", journey);
    }

    // ---------------------------------------------------------------- critical care

    [Fact]
    public void Septic_shock_goes_to_intensive_care_once_it_has_an_anaesthetist()
    {
        var h = Wing(5, new[] { ("intensive_care_unit", 23, 11) }, new[] { ("medical_ward", 16, 10) });
        var icu = Room(h, "intensive_care_unit");
        Assert.True(h.Sim.IsIntensiveCareOpen(icu));
        Assert.Contains(h.Sim.Staff, s => s.Role == StaffRole.Anaesthetist && s.BaseRoomId == icu.Id);

        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "septic_shock");
        h.RunUntil(() => patient.Stage is PatientStage.OnWard or PatientStage.Gone or PatientStage.AwaitingTransfer, TimeSpan.FromHours(16), "septic shock to be admitted");
        if (patient.WorkingDiagnosis?.ConditionId != "septic_shock")
        {
            return;
        }

        Assert.Equal(PatientStage.OnWard, patient.Stage);
        Assert.Equal(icu.Id, patient.CareSpaceId);
        Assert.Equal(3, patient.CareLevel);
        h.RunUntil(() => h.Sim.Tasks.All(t => t.PatientId != patient.Id || t.Kind != TaskKind.WardClerking) || patient.Stage == PatientStage.Gone, TimeSpan.FromHours(4), "ICU clerking");
        Assert.Contains("Clerked by", h.Journey(patient));
    }

    [Fact]
    public void Without_an_anaesthetist_intensive_care_patients_are_transferred()
    {
        var h = Wing(5, new[] { ("intensive_care_unit", 23, 11) }, new[] { ("medical_ward", 16, 10) });
        var icu = Room(h, "intensive_care_unit");
        foreach (var anaesthetist in h.Sim.Staff.Where(s => s.Role == StaffRole.Anaesthetist).ToList())
        {
            h.Sim.Fire(anaesthetist.Id);
        }

        Assert.False(h.Sim.IsIntensiveCareOpen(icu));
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "septic_shock");
        h.RunUntil(() => patient.Stage is PatientStage.OnWard or PatientStage.Gone or PatientStage.AwaitingTransfer, TimeSpan.FromHours(16), "septic shock to be dealt with");
        if (patient.WorkingDiagnosis?.ConditionId != "septic_shock")
        {
            return;
        }

        Assert.Equal(PatientStage.AwaitingTransfer, patient.Stage);
        Assert.Contains("intensive care unit has no anaesthetist", h.Journey(patient));
    }

    [Fact]
    public void Outreach_moves_a_ward_patient_who_gets_worse_up_to_intensive_care_and_back_down()
    {
        var h = Wing(5, new[] { ("intensive_care_unit", 23, 11), ("outreach_office", 8, 8) }, new[] { ("medical_ward", 16, 10) });
        var icu = Room(h, "intensive_care_unit");
        var ward = Room(h, "medical_ward");
        Assert.Contains(h.Sim.Staff, s => s.Role == StaffRole.OutreachNurse);

        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        h.RunUntil(() => patient.Stage is PatientStage.OnWard or PatientStage.Gone or PatientStage.AwaitingTransfer, TimeSpan.FromHours(16), "pneumonia to be admitted");
        if (patient.Stage != PatientStage.OnWard || patient.CareSpaceId != ward.Id)
        {
            return;
        }

        // They take a turn for the worse on the ward.
        h.RunUntil(() => !patient.IsMoving && patient.BusyWithTaskId is null, TimeSpan.FromHours(1), "patient to settle in bed");
        patient.Severity = 0.8;
        h.RunUntil(() => patient.CareSpaceId == icu.Id && !patient.IsMoving || patient.Stage == PatientStage.Gone, TimeSpan.FromHours(6), "outreach to move them to ICU");
        var journey = h.Journey(patient);
        Assert.Contains("Outreach review by", journey);
        Assert.Contains("needs intensive care", journey);
        Assert.Equal(icu.Id, patient.CareSpaceId);

        // Better again: the ICU round steps them down to the ward.
        patient.Severity = 0.3;
        patient.EffectiveTreatmentGiven = true;
        h.RunUntil(() => patient.CareSpaceId == ward.Id || patient.Stage is PatientStage.Gone or PatientStage.Leaving, TimeSpan.FromHours(16), "step down to the ward");
        Assert.Contains("ready to step down", h.Journey(patient));
        Assert.True(patient.IntensiveCareHours > 0);
    }

    [Fact]
    public void Critical_care_and_surgery_details_survive_save_and_load()
    {
        var h = Wing(5, new[] { ("intensive_care_unit", 23, 11) }, new[] { ("medical_ward", 16, 10) });
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "septic_shock");
        patient.CareLevel = 3;
        patient.DayCase = true;
        patient.IntensiveCareHours = 5.5;
        patient.WardMoveRequestedAt = h.Sim.Now;
        var save = SaveSerializer.Deserialize(SaveSerializer.Serialize(new SaveGame
        {
            Clock = h.Clock.CaptureState(),
            Layout = h.Sim.CaptureLayout(),
            People = h.Sim.CapturePeople(),
        }));
        var loaded = new SimulationHarness(seed: 99);
        loaded.Clock.RestoreState(save.Clock);
        loaded.Sim.RestoreLayout(save.Layout!);
        loaded.Sim.RestorePeople(save.People!);
        var back = loaded.Sim.Patients.Single(p => p.Id == patient.Id);
        Assert.Equal(3, back.CareLevel);
        Assert.True(back.DayCase);
        Assert.Equal(5.5, back.IntensiveCareHours);
        Assert.NotNull(back.WardMoveRequestedAt);
    }
}
