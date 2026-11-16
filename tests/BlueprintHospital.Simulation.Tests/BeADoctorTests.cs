using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>
/// Be A Doctor: the player takes over one doctor, walks them about, does their tasks and makes the
/// clinical decisions, with "Decide for me" and hints to fall back on.
/// </summary>
public class BeADoctorTests
{
    /// <summary>A hospital whose only doctor on shift is the one the player takes over.</summary>
    private static (SimulationHarness H, StaffMember Doctor) OnlyDoctor(int seed = 1234)
    {
        var h = new SimulationHarness(seed);
        var doctors = h.Sim.Staff.Where(s => s.Role == StaffRole.Doctor && s.Shift == h.Sim.CurrentShift).ToList();
        foreach (var other in doctors.Skip(1))
        {
            Assert.True(h.Sim.Fire(other.Id));
        }

        Assert.True(h.Sim.TakeControl(doctors[0].Id).Ok);
        return (h, doctors[0]);
    }

    /// <summary>Brings a patient in and waits until they are in a cubicle with a doctor's assessment waiting.</summary>
    private static (Patient Patient, ClinicalTask Assessment) PatientReadyForDoctor(SimulationHarness h, string condition = "community_acquired_pneumonia")
    {
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, condition);
        ClinicalTask? Waiting() => h.Sim.PlayerTasks().FirstOrDefault(t => t.PatientId == patient.Id && t.Kind == TaskKind.DoctorAssessment);
        h.RunUntil(() => Waiting() is not null && patient.Stage == PatientStage.InTreatmentSpace && !patient.IsMoving, TimeSpan.FromHours(3), "the patient to be in a cubicle waiting for the doctor");
        return (patient, Waiting()!);
    }

    /// <summary>Somewhere well away from the cubicles: the triage room.</summary>
    private static World.GridCoord FarAway(SimulationHarness h) => h.Sim.Spaces.First(s => s.Kind == Hospital.CareSpaceKind.Triage).StaffSpot;

    private static void WalkToAndDo(SimulationHarness h, StaffMember doctor, ClinicalTask task)
    {
        Assert.True(h.Sim.PlayerClaim(task.Id).Ok);
        Assert.True(h.Sim.PlayerWalkTo(h.Sim.PlayerTaskSpot(task)!.Value));
        h.RunUntil(() => !doctor.IsMoving, TimeSpan.FromMinutes(30), "the doctor to walk to the bedside");
        PlayerResult started = PlayerResult.Fail("not tried");
        h.RunUntil(() => (started = h.Sim.PlayerStartTask(task.Id)).Ok, TimeSpan.FromMinutes(30), $"the task to start ({started.Message})");
        h.RunUntil(() => h.Sim.Tasks.All(t => t.Id != task.Id), TimeSpan.FromHours(1), "the task to finish");
    }

    [Fact]
    public void Only_a_doctor_on_shift_can_be_played()
    {
        var h = new SimulationHarness();
        var nurse = h.Sim.Staff.First(s => s.Role == StaffRole.Nurse);
        var offDuty = h.Sim.Staff.First(s => s.Role == StaffRole.Doctor && s.Shift != h.Sim.CurrentShift);
        Assert.False(h.Sim.TakeControl(nurse.Id).Ok);
        Assert.False(h.Sim.TakeControl(offDuty.Id).Ok);
        Assert.Null(h.Sim.PlayerDoctorId);
        Assert.DoesNotContain(offDuty, h.Sim.PlayableDoctors);
    }

    [Fact]
    public void The_played_doctor_waits_for_the_player_while_the_others_carry_on()
    {
        var h = new SimulationHarness(arrivals: true, arrivalMultiplier: 2);
        var doctors = h.Sim.Staff.Where(s => s.Role == StaffRole.Doctor && s.Shift == h.Sim.CurrentShift).ToList();
        Assert.True(doctors.Count >= 2);
        Assert.True(h.Sim.TakeControl(doctors[0].Id).Ok);
        var done = doctors[0].TasksCompleted;

        h.RunFor(TimeSpan.FromHours(4));

        Assert.Equal(done, doctors[0].TasksCompleted);
        Assert.Null(doctors[0].CurrentTaskId);
        Assert.True(doctors[1].TasksCompleted > 0, "the other doctor should have kept working");
    }

    [Fact]
    public void A_task_cannot_be_started_from_across_the_department()
    {
        var (h, doctor) = OnlyDoctor();
        var (_, assessment) = PatientReadyForDoctor(h);
        doctor.PlaceAt(FarAway(h));

        var result = h.Sim.PlayerStartTask(assessment.Id);

        Assert.False(result.Ok);
        Assert.Contains("Walk to", result.Message);
        Assert.False(assessment.Started);
    }

    [Fact]
    public void Assessing_a_patient_leaves_the_tests_and_diagnosis_to_the_player()
    {
        var (h, doctor) = OnlyDoctor();
        var (patient, assessment) = PatientReadyForDoctor(h);
        var requestedBefore = patient.Investigations.Count;

        WalkToAndDo(h, doctor, assessment);

        Assert.True(patient.HistoryTaken);
        Assert.Null(patient.WorkingDiagnosis);
        Assert.Equal(requestedBefore, patient.Investigations.Count);
        Assert.Contains(patient, h.Sim.PlayerPatients());

        // The player requests tests...
        var options = h.Sim.TestOptionsFor(patient);
        var pick = options.Where(o => o.Available && o.Relevant).Take(2).Select(o => o.Test.Id).ToList();
        Assert.NotEmpty(pick);
        Assert.True(h.Sim.PlayerOrderTests(patient.Id, pick).Ok);
        Assert.All(pick, id => Assert.Contains(patient.Investigations, o => o.InvestigationId == id));
        Assert.False(h.Sim.PlayerOrderTests(patient.Id, pick).Ok, "the same tests can't be requested twice");

        // ...and makes the diagnosis, which plans the treatment.
        var made = h.Sim.Stats.DiagnosesMade;
        var diagnosed = h.Sim.PlayerDiagnose(patient.Id, patient.Condition.Id);
        Assert.True(diagnosed.Ok, diagnosed.Message);
        Assert.Equal(patient.Condition.Id, patient.WorkingDiagnosis!.ConditionId);
        Assert.Equal(doctor.Id, patient.WorkingDiagnosis.DoctorId);
        Assert.Equal(made + 1, h.Sim.Stats.DiagnosesMade);
        Assert.NotEmpty(patient.TreatmentPlan);
        Assert.False(h.Sim.PlayerDiagnose(patient.Id, patient.Condition.Id).Ok, "a second diagnosis isn't allowed");
    }

    [Fact]
    public void Decisions_need_the_doctor_at_the_bedside_and_a_history_first()
    {
        var (h, doctor) = OnlyDoctor();
        var (patient, assessment) = PatientReadyForDoctor(h);

        Assert.False(h.Sim.PlayerDiagnose(patient.Id, patient.Condition.Id).Ok, "no diagnosis before the patient has been assessed");

        WalkToAndDo(h, doctor, assessment);
        doctor.PlaceAt(FarAway(h));

        Assert.False(h.Sim.PlayerDiagnose(patient.Id, patient.Condition.Id).Ok, "no diagnosis from the front door");
        Assert.False(h.Sim.PlayerDecideForMe(patient.Id).Ok);
        Assert.Null(patient.WorkingDiagnosis);
    }

    [Fact]
    public void Decide_for_me_uses_the_doctors_own_judgement()
    {
        var (h, doctor) = OnlyDoctor();
        var (patient, assessment) = PatientReadyForDoctor(h);
        WalkToAndDo(h, doctor, assessment);
        var requested = patient.Investigations.Count;

        var result = h.Sim.PlayerDecideForMe(patient.Id);

        Assert.True(result.Ok, result.Message);
        Assert.True(patient.WorkingDiagnosis is not null || patient.Investigations.Count > requested, "the doctor should have diagnosed or requested tests");
    }

    [Fact]
    public void Walking_away_stops_the_job_until_the_doctor_comes_back()
    {
        var (h, doctor) = OnlyDoctor();
        var (patient, assessment) = PatientReadyForDoctor(h);
        Assert.True(h.Sim.PlayerClaim(assessment.Id).Ok);
        Assert.True(h.Sim.PlayerWalkTo(h.Sim.PlayerTaskSpot(assessment)!.Value));
        h.RunUntil(() => !doctor.IsMoving, TimeSpan.FromMinutes(30), "the doctor to reach the bedside");
        h.RunUntil(() => h.Sim.PlayerStartTask(assessment.Id).Ok, TimeSpan.FromMinutes(30), "the assessment to start");
        h.RunFor(TimeSpan.FromMinutes(1));
        Assert.NotNull(h.Sim.PlayerWorkProgress());

        doctor.PlaceAt(FarAway(h));
        h.RunFor(TimeSpan.FromMinutes(30));

        Assert.False(assessment.Started);
        Assert.False(patient.HistoryTaken);
        Assert.Null(patient.BusyWithTaskId);
        Assert.Null(h.Sim.PlayerWorkProgress());
    }

    [Fact]
    public void Handing_the_doctor_back_gets_undecided_patients_reviewed()
    {
        var (h, doctor) = OnlyDoctor();
        var (patient, assessment) = PatientReadyForDoctor(h);
        WalkToAndDo(h, doctor, assessment);
        h.RunUntil(() => patient.Investigations.All(o => o.Status == InvestigationStatus.Resulted), TimeSpan.FromHours(3), "any triage tests to come back");
        Assert.Null(patient.WorkingDiagnosis);

        h.Sim.ReleaseControl();

        Assert.Null(h.Sim.PlayerDoctorId);
        h.RunUntil(() => patient.WorkingDiagnosis is not null || !patient.IsInDepartment, TimeSpan.FromHours(6), "the doctor to pick the patient back up and diagnose them");
        Assert.True(doctor.TasksCompleted > 1);
    }

    [Fact]
    public void The_player_goes_back_to_managing_when_the_doctors_shift_ends()
    {
        var (h, doctor) = OnlyDoctor();
        h.RunFor(TimeSpan.FromHours(10.5));
        Assert.NotEqual(doctor.Shift, h.Sim.CurrentShift);
        Assert.Null(h.Sim.PlayerDoctorId);
    }

    [Fact]
    public void Free_walking_never_leaves_the_floor_or_passes_through_a_wall()
    {
        var (h, doctor) = OnlyDoctor();
        var random = new Random(7);
        for (var i = 0; i < 4000; i++)
        {
            // Long pushes in one direction, as a player holding a key down would.
            var angle = (i / 80) * 2.39996 + (random.NextDouble() * 0.2);
            var before = doctor.Tile;
            h.Sim.PlayerMove(Math.Cos(angle) * 0.35, Math.Sin(angle) * 0.35);
            var after = doctor.Tile;
            Assert.True(h.Sim.Nav.IsWalkable(after), $"walked onto {after}, which nobody can stand on");
            if (after != before && (after.X == before.X || after.Y == before.Y) && Math.Abs(after.X - before.X) + Math.Abs(after.Y - before.Y) == 1)
            {
                Assert.True(h.Sim.Nav.CanStep(before, after), $"stepped through a wall from {before} to {after}");
            }
        }
    }

    [Fact]
    public void A_hint_lays_out_the_evidence_and_ranks_likely_causes_without_percentages()
    {
        var (h, doctor) = OnlyDoctor();
        var (patient, assessment) = PatientReadyForDoctor(h);
        WalkToAndDo(h, doctor, assessment);

        var hint = h.Sim.HintFor(patient);

        Assert.True(hint.HistoryTaken);
        Assert.Contains(hint.History, l => l.Label.StartsWith("Allerg", StringComparison.Ordinal));
        Assert.Contains(hint.Observations, l => l.Label == "NEWS2");
        Assert.NotEmpty(hint.Candidates);
        Assert.All(hint.Candidates, c => Assert.InRange(c.Strength, 1, 4));
        Assert.All(hint.Candidates, c => Assert.False(string.IsNullOrWhiteSpace(c.Reason)));
        Assert.True(hint.Candidates.Select(c => c.Strength).SequenceEqual(hint.Candidates.Select(c => c.Strength).OrderByDescending(s => s)), "strongest fit first");
        var everything = string.Join(" ", hint.Clues.Concat(hint.Candidates.Select(c => c.Reason)).Concat(hint.Warnings));
        Assert.DoesNotContain("%", everything.Replace("SpO2", string.Empty));
    }

    [Fact]
    public void A_hint_warns_when_the_usual_treatment_clashes_with_an_allergy()
    {
        var h = new SimulationHarness();
        Patient? allergic = null;
        for (var i = 0; i < 400 && allergic is null; i++)
        {
            var p = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "community_acquired_pneumonia");
            if (p.AllergyGroups.Any(g => p.Condition.Treatment.Any(t => t.Medication is not null && h.Database.Medications[t.Medication].AllergyGroups.Contains(g))))
            {
                allergic = p;
            }
        }

        Assert.NotNull(allergic);
        allergic!.HistoryTaken = true;
        var hint = h.Sim.HintFor(allergic);
        Assert.Contains(hint.Candidates, c => c.Condition.Id == allergic.Condition.Id);
        Assert.NotEmpty(hint.Warnings);
    }

    [Fact]
    public void Hints_can_be_built_for_every_emergency_condition_at_every_stage()
    {
        var h = new SimulationHarness();
        foreach (var condition in h.Database.Conditions.Values.Where(c => !c.IsMaternity && !c.IsPlanned))
        {
            var p = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, condition.Id);
            Assert.NotEmpty(h.Sim.HintFor(p).Candidates);
            foreach (var s in p.Symptoms)
            {
                p.KnownSymptoms.Add(s);
            }

            p.HistoryTaken = true;
            p.RecordObservations(h.Clock.Now, p.CurrentObservations());
            foreach (var id in condition.KeyInvestigations)
            {
                var order = new InvestigationOrder { InvestigationId = id, RequestedAt = h.Clock.Now };
                p.Investigations.Add(order);
                order.Status = InvestigationStatus.Resulted;
                order.ResultedAt = h.Clock.Now;
                order.Abnormal = p.Investigations.Count % 2 == 0;
                order.Summary = order.Abnormal ? "Abnormal." : "Normal.";
            }

            var hint = h.Sim.HintFor(p);
            Assert.NotEmpty(hint.Candidates);
            Assert.NotEmpty(hint.Tests);
        }
    }

    [Fact]
    public void The_player_can_take_a_job_a_colleague_is_still_walking_to()
    {
        var h = new SimulationHarness(4321);
        var doctors = h.Sim.Staff.Where(s => s.Role == StaffRole.Doctor && s.Shift == h.Sim.CurrentShift).ToList();
        Assert.True(doctors.Count >= 2, "the starter team should have two doctors on shift");
        var me = doctors[0];
        Assert.True(h.Sim.TakeControl(me.Id).Ok);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");

        // A colleague picks the assessment up and sets off; it still shows on the player's list.
        ClinicalTask? Theirs() => h.Sim.Tasks.FirstOrDefault(t => t.PatientId == patient.Id && t.Kind == TaskKind.DoctorAssessment && h.Sim.OnTheirWay(t) is not null);
        h.RunUntil(() => Theirs() is not null, TimeSpan.FromHours(3), "a colleague to set off for the assessment");
        var task = Theirs()!;
        var colleague = h.Sim.OnTheirWay(task)!;
        Assert.Contains(task, h.Sim.PlayerTasks());

        Assert.True(h.Sim.PlayerClaim(task.Id).Ok);
        Assert.Equal(me.Id, task.AssignedStaffId);
        Assert.Null(colleague.CurrentTaskId);
        Assert.False(colleague.IsMoving);

        // Once the player has it, nobody takes it back.
        h.RunFor(TimeSpan.FromMinutes(10));
        Assert.Equal(me.Id, task.AssignedStaffId);
    }

    [Fact]
    public void The_players_patients_wait_for_the_player_then_a_colleague_steps_in()
    {
        var h = new SimulationHarness(4321);
        var doctors = h.Sim.Staff.Where(s => s.Role == StaffRole.Doctor && s.Shift == h.Sim.CurrentShift).ToList();
        Assert.True(doctors.Count >= 2);
        var me = doctors[0];
        Assert.True(h.Sim.TakeControl(me.Id).Ok);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        ClinicalTask? Assessment() => h.Sim.PlayerTasks().FirstOrDefault(t => t.PatientId == patient.Id && t.Kind == TaskKind.DoctorAssessment);
        h.RunUntil(() => Assessment() is not null && patient.Stage == PatientStage.InTreatmentSpace && !patient.IsMoving, TimeSpan.FromHours(3), "the patient to be waiting for a doctor");
        WalkToAndDo(h, me, Assessment()!);
        Assert.True(h.Sim.IsPlayersPatient(patient));

        var tests = h.Sim.TestOptionsFor(patient).Where(o => o.Available).Take(2).Select(o => o.Test.Id).ToList();
        Assert.True(h.Sim.PlayerOrderTests(patient.Id, tests).Ok);
        h.RunUntil(() => h.Sim.PlayerDecisionsWaiting().Contains(patient), TimeSpan.FromHours(4), "the results to come back to the player");

        // The other doctors leave the decision alone...
        Assert.DoesNotContain(h.Sim.Tasks, t => t.PatientId == patient.Id && t.Kind == TaskKind.DoctorReview);
        h.RunFor(TimeSpan.FromMinutes(30));
        Assert.Null(patient.WorkingDiagnosis);

        // ...until the patient has waited too long, when one of them reviews and decides.
        h.RunUntil(() => patient.WorkingDiagnosis is not null || !patient.IsInDepartment, HospitalSimulation.PlayerDecisionPatience + TimeSpan.FromHours(3), "a colleague to step in");
        Assert.False(h.Sim.IsPlayersPatient(patient));
    }
}
