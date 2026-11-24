using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>The illness journal, room logs, the Inpatients and Clinics boards and the big monitor's rhythm.</summary>
public class DetailsTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    private static (SimulationHarness H, CareSpace Ward) WithWard(int seed = 5)
    {
        var h = new SimulationHarness(seed);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_ward", new GridRect(B.X - 30, B.Y + 17, 26, 15))).CanBuild);
        h.StaffToRecommended();
        return (h, h.Sim.Wards.Single());
    }

    [Fact]
    public void A_ward_patient_is_logged_in_and_out_counted_in_the_journal_and_shown_on_the_inpatients_board()
    {
        var (h, ward) = WithWard();
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        h.RunUntil(() => patient.Stage is PatientStage.OnWard or PatientStage.Gone, TimeSpan.FromHours(12), "pneumonia patient to reach the ward");
        Assert.True(patient.Stage == PatientStage.OnWard, h.Journey(patient));

        Assert.Contains(h.Sim.RoomLog(ward.Id), l => l.Kind == RoomLogKind.Admitted && l.Text.Contains(patient.FullName));
        Assert.Contains(patient, h.Sim.PatientsIn(ward));
        Assert.NotEmpty(h.Sim.StaffIn(ward));
        var (_, status) = h.Sim.BedsideStatus(patient);
        Assert.False(string.IsNullOrWhiteSpace(status));

        var board = h.Sim.BuildInpatientBoard();
        Assert.Equal(1, board.Admitted);
        Assert.Equal(1, board.Red + board.Amber + board.Green);
        var line = Assert.Single(board.Wards);
        Assert.Equal(1, line.BedsUsed);
        Assert.Equal(ward.Beds.Count, line.Beds);

        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromDays(6), "pneumonia patient to go home");
        Assert.Contains(h.Sim.RoomLog(ward.Id), l => l.Kind == RoomLogKind.WentHome && l.Text.Contains(patient.FullName));

        var entry = h.Sim.JournalFor("community_acquired_pneumonia");
        Assert.NotNull(entry);
        Assert.Equal(1, entry!.Treated);
        Assert.Equal(1, entry.WentHome);
        Assert.Equal(1, entry.Admitted);
        Assert.Equal(1, entry.Ambulance);
        Assert.True(entry.AverageStayDays > 0.1);
        Assert.Equal(1, entry.PerWeek.Values.Sum());
        Assert.True(entry.Diagnosed >= 1);
    }

    [Fact]
    public void The_journal_and_room_logs_survive_a_save()
    {
        var (from, ward) = WithWard();
        var patient = from.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        from.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromDays(6), "pneumonia patient to go home");

        var json = SaveSerializer.Serialize(new SaveGame { Clock = from.Clock.CaptureState(), Layout = from.Sim.CaptureLayout(), People = from.Sim.CapturePeople() });
        var save = SaveSerializer.Deserialize(json);
        var to = new SimulationHarness(seed: 99);
        to.Clock.RestoreState(save.Clock);
        to.Sim.RestoreLayout(save.Layout!);
        to.Sim.RestorePeople(save.People!);

        Assert.Equal(1, to.Sim.JournalFor("community_acquired_pneumonia")!.Treated);
        Assert.Equal(from.Sim.RoomLog(ward.Id).Count, to.Sim.RoomLog(ward.Id).Count);
    }

    [Fact]
    public void Room_logs_keep_only_the_latest_lines()
    {
        var h = new SimulationHarness();
        var room = h.Sim.Spaces.First(s => s.IsTreatmentSpace);
        for (var i = 0; i < HospitalSimulation.RoomLogLength + 15; i++)
        {
            h.Sim.LogRoom(room.Id, RoomLogKind.Note, $"line {i}");
        }

        var log = h.Sim.RoomLog(room.Id);
        Assert.Equal(HospitalSimulation.RoomLogLength, log.Count);
        Assert.Equal($"line {HospitalSimulation.RoomLogLength + 14}", log[0].Text);
    }

    [Fact]
    public void The_clinics_board_counts_todays_clinic_patients()
    {
        var h = new SimulationHarness(3, start: new DateTime(2027, 4, 6, 8, 0, 0));
        h.Sim.AdjustBalance(2_000_000);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("outpatient_reception", new GridRect(B.X - 30, B.Y + 17, 14, 10))).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_clinic", new GridRect(B.X - 14, B.Y + 17, 8, 7))).CanBuild);
        h.Sim.HireStaff(Staff.StaffRole.Receptionist, h.Sim.Spaces.Single(s => s.TemplateId == "outpatient_reception").Id, skill: 0.7);
        h.Sim.HireStaff(Staff.StaffRole.Doctor, h.Sim.Spaces.Single(s => s.TemplateId == "medical_clinic").Id, skill: 0.7);
        h.RunFor(TimeSpan.FromHours(5));

        var board = h.Sim.BuildClinicBoard();
        Assert.True(board.BookedToday > 0);
        Assert.True(board.Seen > 0, $"booked {board.BookedToday}, waiting {board.Waiting}, in clinic {board.InClinic}");
        Assert.True(board.IncomeToday > 0);
        Assert.Single(board.Clinics);
        Assert.True(board.Clinics[0].Running);
        Assert.Contains(h.Sim.Appointments, a => a.CalledInAt is not null);
    }

    [Theory]
    [InlineData("atrial_fibrillation", 130, MonitorRhythm.AtrialFibrillation)]
    [InlineData("complete_heart_block", 38, MonitorRhythm.HeartBlock)]
    [InlineData("supraventricular_tachycardia", 180, MonitorRhythm.Svt)]
    [InlineData("community_acquired_pneumonia", 112, MonitorRhythm.SinusTachycardia)]
    [InlineData("ankle_sprain", 78, MonitorRhythm.Sinus)]
    [InlineData("hypothermia", 48, MonitorRhythm.SinusBradycardia)]
    public void Monitor_rhythm_follows_the_illness_and_heart_rate(string condition, int heartRate, MonitorRhythm expected) =>
        Assert.Equal(expected, HospitalSimulation.RhythmFor(condition, heartRate));
}
