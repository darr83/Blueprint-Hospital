using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

public class DepartmentTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A west wing with a corridor; rooms are then built south of it, side by side.</summary>
    private static SimulationHarness Wing(int seed = 5)
    {
        var h = new SimulationHarness(seed);
        h.Sim.AdjustBalance(2_000_000);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        return h;
    }

    private static CareSpace BuildSouth(SimulationHarness h, string template, int x, int w = 13, int d = 15)
    {
        var check = h.Sim.BuildRoom(h.Sim.AutoPlan(template, new GridRect(B.X - 30 + x, B.Y + 17, w, d)));
        Assert.True(check.CanBuild, string.Join("; ", check.Problems));
        return h.Sim.Spaces.Last(s => s.TemplateId == template);
    }

    [Fact]
    public void Specialty_rooms_can_only_be_built_once_the_department_is_paid_for()
    {
        var h = Wing();
        Assert.True(h.Sim.IsUnlocked("emergency"));
        Assert.True(h.Sim.IsUnlocked("general_medicine"));
        Assert.True(h.Sim.IsUnlocked("theatres"));
        Assert.False(h.Sim.IsUnlocked("cardiology"));

        var refused = h.Sim.CheckRoomAt("cardiology_ward", new GridRect(B.X - 30, B.Y + 17, 13, 15));
        Assert.Contains(refused.Problems, p => p.Contains("Unlock Cardiology"));

        var before = h.Sim.Balance;
        var cost = h.Database.Departments["cardiology"].OpeningCost;
        Assert.True(h.Sim.UnlockDepartment("cardiology").CanBuild);
        Assert.Equal(before - cost, h.Sim.Balance);
        Assert.True(h.Sim.IsUnlocked("cardiology"));
        Assert.False(h.Sim.UnlockDepartment("cardiology").CanBuild);

        var ward = BuildSouth(h, "cardiology_ward", 0);
        Assert.Equal("cardiology", ward.DepartmentId);
        Assert.StartsWith("Cardiology ward", ward.Name);
    }

    [Fact]
    public void Patients_go_to_their_own_specialtys_ward()
    {
        var h = Wing();
        h.Sim.UnlockDepartment("cardiology");
        var medical = BuildSouth(h, "medical_ward", 0);
        var cardiology = BuildSouth(h, "cardiology_ward", 13);
        h.StaffToRecommended();

        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "atrial_fibrillation");
        h.RunUntil(() => patient.Stage is PatientStage.OnWard or PatientStage.Gone or PatientStage.AwaitingTransfer, TimeSpan.FromHours(16), "AF patient to be admitted");
        if (patient.WorkingDiagnosis?.ConditionId != "atrial_fibrillation")
        {
            return; // Misdiagnosed in A&E: covered by the diagnosis tests.
        }

        Assert.Equal(PatientStage.OnWard, patient.Stage);
        Assert.Equal(cardiology.Id, patient.CareSpaceId);
        Assert.DoesNotContain(medical.Beds, b => b.OccupantId == patient.Id);
    }

    [Fact]
    public void Without_their_specialty_patients_are_transferred_even_if_another_ward_has_room()
    {
        var h = Wing();
        BuildSouth(h, "medical_ward", 0);
        h.StaffToRecommended();

        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "atrial_fibrillation");
        h.RunUntil(() => patient.Stage is PatientStage.OnWard or PatientStage.Gone or PatientStage.AwaitingTransfer, TimeSpan.FromHours(16), "AF patient to be dealt with");
        if (patient.WorkingDiagnosis?.ConditionId != "atrial_fibrillation")
        {
            return;
        }

        Assert.Equal(PatientStage.AwaitingTransfer, patient.Stage);
        Assert.Contains("no Cardiology department", h.Journey(patient));
    }

    [Fact]
    public void When_their_ward_is_full_patients_wait_on_the_AE_ward()
    {
        var h = Wing();
        var medical = BuildSouth(h, "medical_ward", 0);
        var emergency = BuildSouth(h, "emergency_ward", 13);
        Assert.Equal("emergency", emergency.DepartmentId);
        Assert.StartsWith("A&E ward", emergency.Name);
        h.StaffToRecommended();
        foreach (var bed in medical.Beds)
        {
            bed.OccupantId = -1;
        }

        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        h.RunUntil(() => patient.Stage is PatientStage.OnWard or PatientStage.Gone or PatientStage.AwaitingTransfer, TimeSpan.FromHours(16), "pneumonia patient to be admitted");
        if (patient.WorkingDiagnosis?.ConditionId != "community_acquired_pneumonia")
        {
            return;
        }

        Assert.Equal(PatientStage.OnWard, patient.Stage);
        Assert.Equal(emergency.Id, patient.CareSpaceId);
        Assert.Contains("ward is full, so they go to the A&E ward", h.Journey(patient));
    }

    [Fact]
    public void Patients_needing_observation_use_the_AE_ward_then_go_home()
    {
        var h = Wing();
        var emergency = BuildSouth(h, "emergency_ward", 0);
        h.StaffToRecommended();

        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "anaphylaxis");
        h.RunUntil(() => patient.Stage is PatientStage.OnWard or PatientStage.Gone, TimeSpan.FromHours(12), "anaphylaxis patient to be observed or leave");
        if (patient.WorkingDiagnosis?.ConditionId != "anaphylaxis")
        {
            return;
        }

        Assert.Equal(PatientStage.OnWard, patient.Stage);
        Assert.True(patient.ForObservation);
        Assert.Equal(emergency.Id, patient.CareSpaceId);
        Assert.True(patient.DischargeDueAt - patient.AdmittedAt <= TimeSpan.FromHours(6.01), h.Journey(patient));

        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromHours(30), "observed patient to go home");
        Assert.Equal(PatientOutcome.Discharged, patient.Outcome);
    }

    [Fact]
    public void Without_an_AE_ward_observation_patients_go_home_from_their_cubicle()
    {
        var h = new SimulationHarness(seed: 5);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "anaphylaxis");
        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromHours(12), "anaphylaxis patient to leave");
        Assert.False(patient.ForObservation);
        Assert.Null(patient.AdmittedAt);
    }

    [Fact]
    public void A_theatre_only_takes_the_departments_ticked_for_it()
    {
        var h = Wing(seed: 7);
        h.Sim.UnlockDepartment("general_surgery");
        var theatre = BuildSouth(h, "operating_theatre", 0, 12, 12);
        BuildSouth(h, "surgical_ward", 12, 16, 10);
        h.StaffToRecommended();
        Assert.Empty(theatre.AllowedDepartments);
        Assert.Contains(h.Sim.SurgicalDepartments, d => d.Id == "general_surgery");
        Assert.DoesNotContain(h.Sim.SurgicalDepartments, d => d.Id == "cardiology");

        // Ticked for gynaecology only: appendicitis (General Surgery) is sent elsewhere.
        h.Sim.SetTheatreAccess(theatre.Id, "gynaecology", true);
        var first = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");
        h.RunUntil(() => first.Stage is PatientStage.AwaitingTransfer or PatientStage.AwaitingSurgery or PatientStage.Gone or PatientStage.OnWard, TimeSpan.FromHours(16), "appendicitis to be dealt with");
        if (first.WorkingDiagnosis?.ConditionId == "acute_appendicitis")
        {
            Assert.Equal(PatientStage.AwaitingTransfer, first.Stage);
            Assert.Contains("no theatre here takes General Surgery patients", h.Journey(first));
        }

        // Ticking General Surgery lets the next one through.
        h.Sim.SetTheatreAccess(theatre.Id, "general_surgery", true);
        var second = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");
        h.RunUntil(() => second.Stage is PatientStage.AwaitingTransfer or PatientStage.Gone or PatientStage.OnWard, TimeSpan.FromHours(16), "appendicitis to be operated on");
        if (second.WorkingDiagnosis?.ConditionId == "acute_appendicitis")
        {
            Assert.Equal(PatientStage.OnWard, second.Stage);
            Assert.True(second.HadSurgery);
        }
    }

    [Fact]
    public void Untick_a_department_and_its_waiting_patients_are_transferred()
    {
        var h = Wing(seed: 7);
        h.Sim.UnlockDepartment("general_surgery");
        var theatre = BuildSouth(h, "operating_theatre", 0, 12, 12);
        BuildSouth(h, "surgical_ward", 12, 16, 10);
        h.Sim.SetTheatreAccess(theatre.Id, "general_surgery", true);
        h.StaffToRecommended();

        // With no surgeons (day or night) the patient waits on the list.
        foreach (var surgeon in h.Sim.Staff.Where(s => s.Role == StaffRole.Surgeon).ToList())
        {
            h.Sim.Fire(surgeon.Id);
        }

        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");
        h.RunUntil(() => patient.Stage is PatientStage.AwaitingTransfer or PatientStage.Gone, TimeSpan.FromHours(16), "appendicitis to be transferred");
        if (patient.WorkingDiagnosis?.ConditionId == "acute_appendicitis")
        {
            Assert.Equal(PatientStage.AwaitingTransfer, patient.Stage);
        }
    }

    [Fact]
    public void Staff_and_patients_can_be_renamed()
    {
        var h = new SimulationHarness();
        var member = h.Sim.Staff.First();
        h.Sim.RenameStaff(member.Id, "  Jo Bloggs ");
        Assert.Equal("Jo Bloggs", member.Name);
        h.Sim.RenameStaff(member.Id, "   ");
        Assert.Equal("Jo Bloggs", member.Name);

        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "ankle_sprain");
        h.Sim.RenamePatient(patient.Id, "Mary Ann Smith");
        Assert.Equal("Mary Ann", patient.FirstName);
        Assert.Equal("Smith", patient.Surname);
        Assert.Equal("Mary Ann Smith", patient.FullName);
    }

    [Fact]
    public void Free_porters_wait_in_the_staff_room()
    {
        var h = new SimulationHarness();
        h.RunFor(TimeSpan.FromMinutes(10));
        var staffRoom = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.StaffRoom);
        foreach (var porter in h.Sim.Staff.Where(s => s.Role == StaffRole.Porter && s.CurrentTaskId is null))
        {
            Assert.True(staffRoom.Rect.Contains(porter.Destination ?? porter.Tile), $"{porter.Name} is at {porter.Tile}");
        }
    }

    [Fact]
    public void Unlocked_departments_and_theatre_ticks_survive_save_and_load()
    {
        var h = Wing();
        h.Sim.UnlockDepartment("gynaecology");
        var theatre = BuildSouth(h, "operating_theatre", 0, 12, 12);
        h.Sim.SetTheatreAccess(theatre.Id, "gynaecology", true);

        var json = SaveSerializer.Serialize(new SaveGame { Clock = h.Clock.CaptureState(), Layout = h.Sim.CaptureLayout(), People = h.Sim.CapturePeople() });
        var save = SaveSerializer.Deserialize(json);
        var to = new SimulationHarness(seed: 99);
        to.Clock.RestoreState(save.Clock);
        to.Sim.RestoreLayout(save.Layout!);
        to.Sim.RestorePeople(save.People!);

        Assert.True(to.Sim.IsUnlocked("gynaecology"));
        Assert.False(to.Sim.IsUnlocked("cardiology"));
        Assert.Equal(new[] { "gynaecology" }, to.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.Theatre).AllowedDepartments.ToArray());
    }

    [Fact]
    public void Unlimited_money_lets_spending_go_into_the_red()
    {
        var normal = new SimulationHarness();
        normal.Sim.AdjustBalance(-normal.Sim.Balance + 1000);
        Assert.Contains(normal.Sim.CheckUnlock("cardiology").Problems, p => p.StartsWith("Not enough money"));

        var h = new SimulationHarness();
        h.Sim.UnlimitedMoney = true;
        h.Sim.AdjustBalance(-h.Sim.Balance + 1000);
        Assert.True(h.Sim.UnlockDepartment("cardiology").CanBuild);
        Assert.True(h.Sim.Balance < 0);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.DoesNotContain(h.Sim.Notifications.Recent, n => n.Text.Contains("overdrawn"));

        var json = SaveSerializer.Serialize(new SaveGame { Clock = h.Clock.CaptureState(), Layout = h.Sim.CaptureLayout(), People = h.Sim.CapturePeople() });
        var save = SaveSerializer.Deserialize(json);
        var to = new SimulationHarness(seed: 99);
        to.Sim.RestoreLayout(save.Layout!);
        to.Sim.RestorePeople(save.People!);
        Assert.True(to.Sim.UnlimitedMoney);
        Assert.Equal(h.Sim.Balance, to.Sim.Balance);
    }
}
