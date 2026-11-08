using System.Text.RegularExpressions;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

public class ShiftTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    [Theory]
    [InlineData(10, StaffShift.Day)]
    [InlineData(8, StaffShift.Day)]
    [InlineData(19, StaffShift.Day)]
    [InlineData(20, StaffShift.Night)]
    [InlineData(2, StaffShift.Night)]
    [InlineData(7, StaffShift.Night)]
    public void Shifts_run_eight_to_eight(int hour, StaffShift expected)
    {
        var time = new DateTime(2027, 1, 4, hour, 30, 0);
        Assert.Equal(expected, HospitalSimulation.ShiftAt(time));
        var start = HospitalSimulation.ShiftStart(time);
        Assert.True(start <= time && time < start.AddHours(12));
        Assert.Equal(expected == StaffShift.Day ? 8 : 20, start.Hour);
    }

    [Fact]
    public void The_starting_team_has_a_day_team_at_work_and_a_night_team_at_home()
    {
        var h = new SimulationHarness();
        var day = h.Sim.Staff.Where(s => s.Shift == StaffShift.Day).ToList();
        var night = h.Sim.Staff.Where(s => s.Shift == StaffShift.Night).ToList();

        Assert.Equal(day.Count, night.Count);
        Assert.All(day, s => Assert.True(s.IsOnDuty));
        Assert.All(night, s => Assert.False(s.IsInBuilding));
        Assert.All(night, s => Assert.Contains("in at 20:00", s.StatusText));
    }

    [Fact]
    public void At_eight_in_the_evening_the_day_team_goes_home_and_the_night_team_comes_in()
    {
        var h = new SimulationHarness(seed: 3, arrivals: true, start: new DateTime(2027, 1, 4, 18, 0, 0));
        h.RunFor(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(1));

        var night = h.Sim.Staff.Where(s => s.Shift == StaffShift.Night).ToList();
        Assert.All(night, s => Assert.True(s.IsOnDuty));
        Assert.Contains(h.Sim.Notifications.Recent, n => n.Text.StartsWith("Night shift has started"));

        // The day team finish what they're doing and walk out.
        h.RunUntil(() => h.Sim.Staff.Where(s => s.Shift == StaffShift.Day).All(s => s.Activity == StaffActivity.OffDuty), TimeSpan.FromHours(1), "the day team to leave");
        Assert.All(h.Sim.Staff.Where(s => s.Shift == StaffShift.Day), s => Assert.Null(s.CurrentTaskId));

        // Patients keep being looked after overnight, by the night team only.
        int Done(StaffShift shift) => h.Sim.Staff.Where(s => s.Shift == shift).Sum(s => s.TasksCompleted);
        var night0 = Done(StaffShift.Night);
        var day0 = Done(StaffShift.Day);
        h.RunFor(TimeSpan.FromHours(3));
        Assert.True(Done(StaffShift.Night) > night0);
        Assert.Equal(day0, Done(StaffShift.Day));
    }

    [Fact]
    public void Everyone_gets_a_meal_break_and_two_tea_breaks_away_from_the_start_and_end_of_the_shift()
    {
        var h = new SimulationHarness(seed: 5, start: new DateTime(2027, 1, 4, 7, 59, 0));
        h.RunFor(TimeSpan.FromMinutes(2));
        var shiftStart = new DateTime(2027, 1, 4, 8, 0, 0);

        foreach (var member in h.Sim.OnDutyStaff)
        {
            Assert.Equal(3, member.Breaks.Count);
            Assert.Equal(1, member.Breaks.Count(b => b.Minutes == HospitalSimulation.MealBreakMinutes));
            Assert.Equal(2, member.Breaks.Count(b => b.Minutes == HospitalSimulation.TeaBreakMinutes));
            Assert.All(member.Breaks, b => Assert.InRange(b.Start, shiftStart.AddHours(1), shiftStart.AddHours(11)));
        }
    }

    [Fact]
    public void Staff_on_a_break_take_no_work_and_come_back_afterwards()
    {
        var h = new SimulationHarness(seed: 6, arrivals: true, start: new DateTime(2027, 1, 4, 8, 0, 0), arrivalMultiplier: 1.5);
        var seenOnBreak = new HashSet<int>();
        h.RunUntil(() =>
        {
            foreach (var member in h.Sim.Staff.Where(s => s.IsOnBreak))
            {
                Assert.Null(member.CurrentTaskId);
                seenOnBreak.Add(member.Id);
            }

            return h.Clock.Now.Hour >= 19;
        }, TimeSpan.FromHours(12), "the end of the day");

        var dayTeam = h.Sim.Staff.Where(s => s.Shift == StaffShift.Day).ToList();
        Assert.All(dayTeam, s => Assert.Contains(s.Id, seenOnBreak));
        Assert.All(dayTeam, s => Assert.True(s.Breaks.Count(b => b.Done) >= 2, $"{s.Name} only had {s.Breaks.Count(b => b.Done)} breaks"));
    }

    [Fact]
    public void No_more_than_a_third_of_a_team_go_on_a_planned_break_at_once()
    {
        var h = new SimulationHarness(seed: 2);
        var nurses = h.Sim.OnDutyStaff.Where(s => s.Role == StaffRole.Nurse && s.BaseRoomId is null).ToList();
        Assert.Equal(4, nurses.Count);
        foreach (var nurse in nurses)
        {
            nurse.Breaks.Clear();
            nurse.Breaks.Add(new PlannedBreak { Start = h.Clock.Now, Minutes = 30 });
        }

        h.RunFor(TimeSpan.FromMinutes(5));
        Assert.Equal(1, nurses.Count(n => n.IsOnBreak));

        // The others go in turn.
        h.RunFor(TimeSpan.FromMinutes(40));
        Assert.Equal(1, nurses.Count(n => n.Breaks[0].Done));
        Assert.Equal(1, nurses.Count(n => n.IsOnBreak));
    }

    [Fact]
    public void A_break_waits_while_a_red_patient_needs_them()
    {
        var h = new SimulationHarness(seed: 4);
        var doctors = h.Sim.OnDutyStaff.Where(s => s.Role == StaffRole.Doctor).ToList();
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_myocardial_infarction");
        h.RunUntil(() => patient.Triage == Medical.TriageCategory.Red && h.Sim.Tasks.Any(t => t.Role == StaffRole.Doctor && t.PatientId == patient.Id), TimeSpan.FromHours(1), "a Red patient waiting for a doctor");

        foreach (var doctor in doctors)
        {
            doctor.Breaks.Clear();
            doctor.Breaks.Add(new PlannedBreak { Start = h.Clock.Now, Minutes = 30 });
        }

        h.RunUntil(() => patient.FirstSeenByDoctorAt is not null, TimeSpan.FromHours(1), "a doctor to see the Red patient");
        Assert.NotNull(patient.FirstSeenByDoctorAt);
    }

    [Fact]
    public void The_player_can_send_someone_on_a_break()
    {
        var h = new SimulationHarness();
        var porter = h.Sim.OnDutyStaff.First(s => s.Role == StaffRole.Porter);
        Assert.True(h.Sim.SendOnBreak(porter.Id));
        Assert.False(h.Sim.SendOnBreak(porter.Id));
        h.RunFor(TimeSpan.FromMinutes(1));
        Assert.True(porter.IsOnBreak);
        h.RunFor(TimeSpan.FromMinutes(30));
        Assert.False(porter.IsOnBreak);
        Assert.True(porter.Breaks.Single(b => b.Requested).Done);
    }

    [Fact]
    public void Moving_a_nurse_to_the_night_team_sends_them_home_and_back_at_eight()
    {
        var h = new SimulationHarness(start: new DateTime(2027, 1, 4, 15, 0, 0));
        var nurse = h.Sim.OnDutyStaff.First(s => s.Role == StaffRole.Nurse);
        Assert.True(h.Sim.SetShift(nurse.Id, StaffShift.Night));

        h.RunUntil(() => nurse.Activity == StaffActivity.OffDuty, TimeSpan.FromHours(1), "the nurse to go home");
        Assert.Contains("in at 20:00", nurse.StatusText);
        h.RunFor(TimeSpan.FromHours(5));
        Assert.True(nurse.IsOnDuty);

        // In the morning they go home; moved back to days, they come straight in again.
        h.RunUntil(() => nurse.Activity == StaffActivity.OffDuty, TimeSpan.FromHours(14), "the nurse to go home after the night");
        Assert.True(h.Sim.SetShift(nurse.Id, StaffShift.Day));
        h.RunFor(TimeSpan.FromMinutes(1));
        Assert.True(nurse.IsOnDuty);
    }

    [Fact]
    public void Staff_can_move_between_departments()
    {
        var h = new SimulationHarness(seed: 9);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_ward", new GridRect(B.X - 30, B.Y + 17, 26, 15))).CanBuild);
        var ward = h.Sim.Wards.Single();

        var nurse = h.Sim.OnDutyStaff.First(s => s.Role == StaffRole.Nurse && s.CurrentTaskId is null);
        Assert.True(h.Sim.MoveStaff(nurse.Id, ward.Id));
        Assert.Equal(ward.Id, nurse.BaseRoomId);
        Assert.Contains(h.Sim.RecommendedStaffing(), n => n.RoomId == ward.Id && n.Role == StaffRole.Nurse && n.Have == 1 && n.Shift == StaffShift.Day);

        // A porter works across the hospital; they can't be based on a ward.
        var porter = h.Sim.Staff.First(s => s.Role == StaffRole.Porter);
        Assert.False(h.Sim.MoveStaff(porter.Id, ward.Id));
        Assert.Null(porter.BaseRoomId);

        // Back to A&E.
        Assert.True(h.Sim.MoveStaff(nurse.Id, null));
        h.RunFor(TimeSpan.FromMinutes(1));
        Assert.Null(nurse.BaseRoomId);
    }

    [Fact]
    public void Someone_busy_with_a_patient_moves_once_they_have_finished()
    {
        var h = new SimulationHarness(seed: 3);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_ward", new GridRect(B.X - 30, B.Y + 17, 26, 15))).CanBuild);
        var ward = h.Sim.Wards.Single();
        h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "ankle_sprain");
        h.RunUntil(() => h.Sim.Staff.Any(s => s.Role == StaffRole.Nurse && s.CurrentTaskId is not null), TimeSpan.FromHours(1), "a nurse to start work");
        var nurse = h.Sim.Staff.First(s => s.Role == StaffRole.Nurse && s.CurrentTaskId is not null);

        Assert.True(h.Sim.MoveStaff(nurse.Id, ward.Id));
        Assert.True(nurse.HasPendingMove);
        Assert.Null(nurse.BaseRoomId);
        h.RunUntil(() => nurse.BaseRoomId == ward.Id, TimeSpan.FromHours(1), "the nurse to move");
        Assert.False(nurse.HasPendingMove);
    }

    [Fact]
    public void New_hires_join_the_shorter_shift()
    {
        var h = new SimulationHarness(start: new DateTime(2027, 1, 4, 10, 0, 0));
        foreach (var member in h.Sim.Staff.Where(s => s.Role == StaffRole.Nurse && s.Shift == StaffShift.Night).ToList())
        {
            h.Sim.Fire(member.Id);
        }

        var candidate = h.Sim.Candidates.First(c => c.Role == StaffRole.Nurse || c.Role == StaffRole.Doctor);
        var hired = h.Sim.Hire(candidate.Id, null);
        Assert.NotNull(hired);
        if (hired!.Role == StaffRole.Nurse)
        {
            Assert.Equal(StaffShift.Night, hired.Shift);
            Assert.False(hired.IsInBuilding);
        }

        var next = h.Sim.Candidates.First();
        var onDays = h.Sim.Hire(next.Id, h.Sim.PostingsFor(next.Role)[0].RoomId, StaffShift.Day);
        Assert.Equal(StaffShift.Day, onDays!.Shift);
        Assert.True(onDays.IsOnDuty);
    }

    [Fact]
    public void A_hire_costs_half_a_round_the_clock_post()
    {
        var h = new SimulationHarness();
        var salaries = h.Sim.Staff.Sum(s => s.Salary);
        Assert.Equal((long)Math.Round(salaries * 2.25 / 365.0), h.Sim.StaffCostPerDay);
    }

    [Fact]
    public void Shifts_and_breaks_survive_save_and_load()
    {
        var h = new SimulationHarness(seed: 8, arrivals: true);
        h.RunFor(TimeSpan.FromHours(3));
        var json = Save(h);
        var loaded = Load(json);

        foreach (var member in h.Sim.Staff)
        {
            var copy = loaded.Sim.FindStaff(member.Id)!;
            Assert.Equal(member.Shift, copy.Shift);
            Assert.Equal(member.Activity, copy.Activity);
            Assert.Equal(member.Breaks.Select(b => (b.Start, b.Minutes, b.Done)), copy.Breaks.Select(b => (b.Start, b.Minutes, b.Done)));
        }
    }

    [Fact]
    public void Old_saves_split_each_team_between_days_and_nights()
    {
        var h = new SimulationHarness(seed: 8);
        foreach (var member in h.Sim.Staff.Where(s => s.Shift == StaffShift.Night).ToList())
        {
            h.Sim.Fire(member.Id);
        }

        // A save from before shifts: no shift or breaks recorded, everyone at work.
        var json = Regex.Replace(Save(h), "\"shift\": \"(day|night)\",?", string.Empty);
        json = Regex.Replace(json, "\"activity\": \"off_duty\"", "\"activity\": \"idle\"");
        var loaded = Load(json);

        var nurses = loaded.Sim.Staff.Where(s => s.Role == StaffRole.Nurse).ToList();
        Assert.Equal(2, nurses.Count(s => s.Shift == StaffShift.Day));
        Assert.Equal(2, nurses.Count(s => s.Shift == StaffShift.Night));
        Assert.All(nurses.Where(s => s.Shift == StaffShift.Night), s => Assert.False(s.IsOnDuty));
    }

    private static string Save(SimulationHarness h) => SaveSerializer.Serialize(new SaveGame
    {
        Clock = h.Clock.CaptureState(),
        Layout = h.Sim.CaptureLayout(),
        People = h.Sim.CapturePeople(),
    });

    private static SimulationHarness Load(string json)
    {
        var save = SaveSerializer.Deserialize(json);
        var to = new SimulationHarness(seed: 99);
        to.Clock.RestoreState(save.Clock);
        to.Sim.RestoreLayout(save.Layout!);
        to.Sim.RestorePeople(save.People!);
        return to;
    }
}
