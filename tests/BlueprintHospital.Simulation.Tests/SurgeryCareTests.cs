using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Recovery rooms, surgeons' specialties and grades, the weekly theatre sessions and complications.</summary>
public class SurgeryCareTests
{
    private static bool Done(Patient p) => p.Stage is PatientStage.OnWard or PatientStage.Gone or PatientStage.AwaitingTransfer;

    [Fact]
    public void After_the_operation_patients_wake_up_in_a_recovery_bay_with_a_nurse_and_the_theatre_moves_on()
    {
        var h = SurgeryTests.Wing(recovery: true);
        var room = h.Sim.SpacesOfKind(CareSpaceKind.TheatreRecovery).Single();
        Assert.True(room.Beds.Count >= 2, $"{room.Beds.Count} bays");

        // One recovery nurse for every two bays, on every shift.
        foreach (var shift in Enum.GetValues<StaffShift>())
        {
            Assert.Equal((room.Beds.Count + 1) / 2, h.Sim.Staff.Count(s => s.BaseRoomId == room.Id && s.Role == StaffRole.Nurse && s.Shift == shift));
        }

        h.Sim.NextComplication = "none";
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");
        var inBay = false;
        var held = false;
        h.RunUntil(() =>
        {
            if (h.Sim.RecoveryOf(patient) is { RecoveryRoomId: not null, Bay: { } bay } && !patient.IsMoving && patient.Tile == room.Beds[bay].PatientSpot)
            {
                inBay = true;
                Assert.Equal(patient.Id, room.Beds[bay].OccupantId);
            }

            held |= h.Sim.TheatreBoard().Single().State == TheatreState.WakingUp;
            return Done(patient);
        }, TimeSpan.FromHours(24), "appendicitis patient to reach a ward");

        var journey = h.Journey(patient);
        Assert.True(patient.HadSurgery, journey);
        Assert.True(inBay, journey);
        Assert.False(held, journey);
        Assert.Contains($"Wheeled through to {room.Name}", journey);
        Assert.Contains("In recovery", journey);
        Assert.Contains("Awake and comfortable", journey);
        Assert.Empty(h.Sim.TheatreRecovery);
        Assert.All(room.Beds, b => Assert.Null(b.OccupantId));
        Assert.Equal(0, h.Sim.SurgeryToday.Complications);
    }

    [Fact]
    public void Without_a_recovery_room_patients_wake_up_on_the_table_and_hold_up_the_theatre()
    {
        var h = SurgeryTests.Wing();
        h.Sim.NextComplication = "none";
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");
        var held = false;
        h.RunUntil(() =>
        {
            held |= h.Sim.TheatreBoard().Single().State == TheatreState.WakingUp;
            return Done(patient);
        }, TimeSpan.FromHours(24), "appendicitis patient to reach a ward");

        var journey = h.Journey(patient);
        Assert.True(patient.HadSurgery, journey);
        Assert.True(held, journey);
        Assert.Contains("Waking up on the table", journey);
        Assert.Contains("there is no recovery room", journey);
    }

    [Fact]
    public void Bleeding_after_an_operation_sends_the_patient_straight_back_to_theatre()
    {
        var h = SurgeryTests.Wing(recovery: true);
        h.Sim.NextComplication = "bleeding";
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");
        h.RunUntil(() => Done(patient), TimeSpan.FromHours(30), "bleeding patient to reach a ward");

        var journey = h.Journey(patient);
        Assert.Equal("bleeding", patient.Complication);
        Assert.False(patient.ReturningToTheatre, journey);
        Assert.Contains("going straight back to theatre", journey);
        Assert.Equal(2, h.Sim.TheatreLog.Count(l => l.PatientId == patient.Id));
        Assert.Equal(1, h.Sim.SurgeryToday.Complications + (h.Sim.SurgeryToday.Date.Date != h.Sim.Now.Date ? 1 : 0));
        Assert.Equal(PatientStage.OnWard, patient.Stage);
    }

    [Fact]
    public void A_wound_infection_means_a_longer_stay_on_the_ward()
    {
        var h = SurgeryTests.Wing(recovery: true);
        h.Sim.NextComplication = "infection";
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");
        h.RunUntil(() => Done(patient), TimeSpan.FromHours(24), "infected patient to reach a ward");

        var journey = h.Journey(patient);
        Assert.Equal("infection", patient.Complication);
        Assert.Equal(HospitalSimulation.InfectionExtraHours, patient.ExtraStayHours);
        Assert.Contains("The wound looks infected", journey);
    }

    [Fact]
    public void Surgeons_have_grades_and_specialties_and_juniors_are_slower_and_riskier()
    {
        var h = new SimulationHarness(seed: 3);
        var trainee = h.Sim.HireStaff(StaffRole.Surgeon, skill: 0.6);
        var registrar = h.Sim.HireStaff(StaffRole.Surgeon, skill: 0.75);
        var consultant = h.Sim.HireStaff(StaffRole.Surgeon);
        Assert.Equal("Core surgical trainee", trainee.Grade);
        Assert.Equal("Surgical registrar", registrar.Grade);
        Assert.Equal("Consultant surgeon", consultant.Grade);
        Assert.All(new[] { trainee, registrar, consultant }, s => Assert.Equal("general_surgery", s.Specialty));
        Assert.Equal("Surgical registrar · General surgeon", HospitalSimulation.JobTitle(registrar.Grade, registrar.Specialty));

        Assert.True(HospitalSimulation.OperatingPace(trainee, "general_surgery") > HospitalSimulation.OperatingPace(registrar, "general_surgery"));
        Assert.Equal(1.0, HospitalSimulation.OperatingPace(consultant, "general_surgery"));
        Assert.True(HospitalSimulation.OperatingPace(consultant, "trauma_orthopaedics") > 1.0);

        // Gynaecologists do caesareans too.
        consultant.Specialty = "gynaecology";
        Assert.True(HospitalSimulation.InSpecialty(consultant, "maternity"));
        Assert.False(HospitalSimulation.InSpecialty(consultant, "general_surgery"));
        consultant.Specialty = "general_surgery";

        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");
        var operation = h.Database.Conditions["acute_appendicitis"].Operation!;
        trainee.Fatigue = registrar.Fatigue = consultant.Fatigue = 0;
        Assert.True(h.Sim.ComplicationRisk(trainee, patient, operation) > h.Sim.ComplicationRisk(registrar, patient, operation));
        Assert.True(h.Sim.ComplicationRisk(registrar, patient, operation) > h.Sim.ComplicationRisk(consultant, patient, operation));
        var rested = h.Sim.ComplicationRisk(consultant, patient, operation);
        consultant.Fatigue = 1;
        Assert.True(h.Sim.ComplicationRisk(consultant, patient, operation) > rested);

        // Recruitment's surgeons each come with a specialty.
        var surgeons = new List<JobCandidate>();
        for (var i = 0; i < 6; i++)
        {
            surgeons.AddRange(h.Sim.Candidates.Where(c => c.Role == StaffRole.Surgeon));
            h.RunFor(TimeSpan.FromHours(HospitalSimulation.CandidatesEveryHours));
        }

        Assert.NotEmpty(surgeons);
        Assert.All(surgeons, c => Assert.Contains(c.Specialty, HospitalSimulation.SurgicalSpecialties));
    }

    [Fact]
    public void Theatre_sessions_can_be_given_to_a_department_and_its_session_only_takes_its_planned_list()
    {
        var h = SurgeryTests.Wing(departments: "gynaecology");
        var theatre = h.Sim.SpacesOfKind(CareSpaceKind.Theatre).Single();
        Assert.All(h.Sim.TheatreTimetable(theatre), s => Assert.Equal(TheatreSessions.Emergency, s));

        // A department the theatre doesn't take is refused.
        h.Sim.SetTheatreSession(theatre.Id, 0, "cardiology");
        Assert.Equal(TheatreSessions.Emergency, h.Sim.TheatreTimetable(theatre)[0]);

        for (var slot = 0; slot < TheatreSessions.PerWeek; slot++)
        {
            h.Sim.SetTheatreSession(theatre.Id, slot, "gynaecology");
        }

        var monday = new DateTime(2026, 11, 16);
        Assert.Equal(0, TheatreSessions.SlotOf(monday.AddHours(9)));
        Assert.Equal(1, TheatreSessions.SlotOf(monday.AddHours(14)));
        Assert.Equal(13, TheatreSessions.SlotOf(monday.AddDays(6).AddHours(17)));
        Assert.Equal(-1, TheatreSessions.SlotOf(monday.AddHours(20)));
        Assert.Equal("gynaecology", h.Sim.TheatreSessionAt(theatre, monday.AddHours(9)));
        Assert.Equal(TheatreSessions.Emergency, h.Sim.TheatreSessionAt(theatre, monday.AddHours(20)));

        // An urgent general surgery case waits for the evening, when the theatre is back on emergencies.
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");
        h.RunUntil(() => patient.HadSurgery || patient.Stage == PatientStage.Gone, TimeSpan.FromHours(40), "the appendicitis operation");
        var journey = h.Journey(patient);
        Assert.True(patient.HadSurgery, journey);
        var log = h.Sim.TheatreLog.First(l => l.PatientId == patient.Id);
        Assert.Equal(-1, TheatreSessions.SlotOf(log.Start));
    }

    [Fact]
    public void Recovery_sessions_and_specialties_survive_save_and_load()
    {
        var h = SurgeryTests.Wing(recovery: true, departments: "gynaecology");
        var theatre = h.Sim.SpacesOfKind(CareSpaceKind.Theatre).Single();
        h.Sim.SetTheatreSession(theatre.Id, 3, "gynaecology");
        h.Sim.NextComplication = "infection";
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");
        h.RunUntil(() => h.Sim.RecoveryOf(patient) is { Until: not null } || Done(patient), TimeSpan.FromHours(24), "the patient to be in recovery");
        var entry = h.Sim.RecoveryOf(patient);
        Assert.NotNull(entry);

        var json = SaveSerializer.Serialize(new SaveGame { Clock = h.Clock.CaptureState(), Layout = h.Sim.CaptureLayout(), People = h.Sim.CapturePeople() });
        var save = SaveSerializer.Deserialize(json);
        var restored = new SimulationHarness(seed: 7);
        restored.Sim.RestoreLayout(save.Layout!);
        restored.Sim.RestorePeople(save.People!);

        var again = restored.Sim.Patients.Single(p => p.Id == patient.Id);
        var restoredTheatre = restored.Sim.SpacesOfKind(CareSpaceKind.Theatre).Single();
        Assert.Equal("gynaecology", restored.Sim.TheatreTimetable(restoredTheatre)[3]);
        var back = restored.Sim.RecoveryOf(again);
        Assert.NotNull(back);
        Assert.Equal(entry!.RecoveryRoomId, back!.RecoveryRoomId);
        Assert.Equal(entry.Bay, back.Bay);
        Assert.Equal(entry.Until, back.Until);
        Assert.Equal("infection", back.Complication);
        Assert.Equal(
            h.Sim.Staff.Where(s => s.Role == StaffRole.Surgeon).Select(s => s.Specialty),
            restored.Sim.Staff.Where(s => s.Role == StaffRole.Surgeon).Select(s => s.Specialty));
    }

    [Fact]
    public void Every_theatre_needs_a_scrub_nurse_who_scrubs_in_for_the_whole_operation()
    {
        var h = SurgeryTests.Wing(recovery: true);
        var theatre = h.Sim.SpacesOfKind(CareSpaceKind.Theatre).Single();
        Assert.Contains(h.Sim.RecommendedStaffing(), n => n.RoomId == theatre.Id && n.Role == StaffRole.ScrubNurse);
        Assert.Contains(h.Sim.PostingsFor(StaffRole.ScrubNurse), p => p.RoomId == theatre.Id);
        Assert.True(h.Sim.IsStaffed(theatre));

        // Without a scrub nurse the theatre is shut.
        var nurses = h.Sim.Staff.Where(s => s.Role == StaffRole.ScrubNurse).ToList();
        Assert.NotEmpty(nurses);
        foreach (var nurse in nurses)
        {
            h.Sim.Fire(nurse.Id);
        }

        Assert.Contains("a scrub nurse", h.Sim.TheatreProblems(theatre));
        Assert.False(h.Sim.IsStaffed(theatre));
        h.StaffToRecommended();
        Assert.True(h.Sim.IsStaffed(theatre));

        h.Sim.NextComplication = "none";
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");
        var scrubbed = false;
        h.RunUntil(() =>
        {
            if (h.Sim.Tasks.FirstOrDefault(t => t.Kind == TaskKind.Surgery && t.PatientId == patient.Id && t.Started) is { } op
                && h.Sim.ScrubNurseOn(op) is { } scrub && !scrub.IsMoving && theatre.Rect.Contains(scrub.Tile))
            {
                scrubbed = true;
                Assert.Equal(theatre.Id, scrub.BaseRoomId);
                Assert.NotEqual(theatre.StaffSpot, scrub.Tile);
            }

            return patient.HadSurgery;
        }, TimeSpan.FromHours(24), "the operation");

        var journey = h.Journey(patient);
        Assert.True(scrubbed, journey);
        Assert.Contains("(scrub nurse) scrubbed in", journey);
        h.RunFor(TimeSpan.FromMinutes(5));
        Assert.All(h.Sim.Staff.Where(s => s.Role == StaffRole.ScrubNurse), s => Assert.Null(s.AssistingTaskId));
    }

    [Fact]
    public void There_are_about_45_operations_and_every_planned_theatre_list_has_a_surgical_department()
    {
        var db = new SimulationHarness().Database;
        var operations = db.Conditions.Values.Where(c => c.Operation is not null).ToList();
        Assert.True(operations.Count >= 45, $"{operations.Count} operations");
        var theatreLists = operations.Where(c => c.IsPlanned && c.Operation!.Room == CareSpaceKind.Theatre).ToList();
        Assert.True(theatreLists.Count >= 20, $"{theatreLists.Count} planned theatre lists");
        Assert.All(theatreLists, c => Assert.Contains(HospitalSimulation.SpecialtyFor(c.Department), HospitalSimulation.SurgicalSpecialties));
        Assert.Equal(theatreLists.Count, theatreLists.Select(c => c.Operation!.Name).Distinct().Count());
    }
}
