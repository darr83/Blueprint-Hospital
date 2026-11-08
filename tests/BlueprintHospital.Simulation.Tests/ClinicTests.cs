using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

public class ClinicTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A west wing with a corridor, an Outpatients reception and a medical clinic south of it.</summary>
    private static (SimulationHarness H, CareSpace Reception, CareSpace Clinic) Outpatients(bool staffed = true, int seed = 3, DateTime? start = null)
    {
        var h = new SimulationHarness(seed, start: start);
        h.Sim.AdjustBalance(2_000_000);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        var reception = Build(h, "outpatient_reception", new GridRect(B.X - 30, B.Y + 17, 14, 10));
        var clinic = Build(h, "medical_clinic", new GridRect(B.X - 14, B.Y + 17, 8, 7));
        if (staffed)
        {
            h.Sim.HireStaff(StaffRole.Receptionist, reception.Id, skill: 0.7);
            h.Sim.HireStaff(StaffRole.Doctor, clinic.Id, skill: 0.7);
        }

        return (h, reception, clinic);
    }

    private static CareSpace Build(SimulationHarness h, string template, GridRect rect)
    {
        var check = h.Sim.BuildRoom(h.Sim.AutoPlan(template, rect));
        Assert.True(check.CanBuild, string.Join("; ", check.Problems));
        return h.Sim.Spaces.Last(s => s.TemplateId == template);
    }

    [Fact]
    public void A_staffed_clinic_books_a_calendar_for_today_and_tomorrow()
    {
        var (h, _, clinic) = Outpatients();
        Assert.True(h.Sim.IsClinicRunning(clinic));
        h.RunFor(TimeSpan.FromSeconds(10));

        var today = h.Sim.CalendarFor(clinic.Id, h.Clock.Now).ToList();
        var tomorrow = h.Sim.CalendarFor(clinic.Id, h.Clock.Now.AddDays(1)).ToList();
        Assert.NotEmpty(today);
        Assert.NotEmpty(tomorrow);
        Assert.All(today, a => Assert.True(a.Time >= h.Clock.Now.AddMinutes(44), $"{a.Time} is too soon to book"));
        Assert.All(tomorrow.Concat(today), a =>
            Assert.True(a.Time.TimeOfDay is { TotalHours: >= 9 and < 12 } or { TotalHours: >= 13.5 and < 16.5 }, $"{a.Time} is outside clinic hours"));
        Assert.Contains(tomorrow, a => a.Time.TimeOfDay == TimeSpan.FromHours(9) || a.Time.TimeOfDay == TimeSpan.FromMinutes(9 * 60 + 20));
        Assert.Equal(today.Count, today.Select(a => a.Time).Distinct().Count());
    }

    [Fact]
    public void Clinic_patients_check_in_wait_and_are_seen_and_the_hospital_is_paid()
    {
        var (h, reception, clinic) = Outpatients();
        h.RunUntil(() => h.Sim.Outpatients.Any(o => o.Stage == OutpatientStage.CheckingIn), TimeSpan.FromHours(2), "a clinic patient to check in");
        Assert.Contains(h.Sim.Staff, s => s.BaseRoomId == reception.Id && s.StatusText.StartsWith("Checking in"));

        h.RunUntil(() => h.Sim.Appointments.Any(a => a.Status == AppointmentStatus.Seen), TimeSpan.FromHours(3), "a clinic patient to be seen");
        var seen = h.Sim.Appointments.First(a => a.Status == AppointmentStatus.Seen);
        Assert.NotNull(seen.CheckedInAt);
        Assert.True(seen.SeenAt > seen.CheckedInAt);
        Assert.Equal(h.Sim.Staff.Single(s => s.BaseRoomId == clinic.Id).Id, seen.SeenByStaffId);
        Assert.True(h.Sim.Today[MoneyCategory.Clinics] > 0);

        h.RunFor(TimeSpan.FromHours(8));
        var day = h.Sim.CalendarFor(clinic.Id, new DateTime(2027, 1, 4)).ToList();
        Assert.True(day.Count(a => a.Status == AppointmentStatus.Seen) >= day.Count / 2, string.Join(", ", day.Select(a => a.Status)));
        Assert.DoesNotContain(day, a => a.Status is AppointmentStatus.Booked or AppointmentStatus.Arriving or AppointmentStatus.Waiting or AppointmentStatus.InClinic);
        Assert.DoesNotContain(h.Sim.Outpatients, o => o.Stage != OutpatientStage.Leaving);
    }

    [Fact]
    public void A_clinic_without_a_doctor_or_receptionist_books_nobody_and_lists_what_it_needs()
    {
        var (h, reception, clinic) = Outpatients(staffed: false);
        Assert.Contains("a doctor posted to it", h.Sim.ClinicProblems(clinic));
        Assert.Contains("a receptionist at Outpatients reception", h.Sim.ClinicProblems(clinic));
        h.RunFor(TimeSpan.FromMinutes(5));
        Assert.Empty(h.Sim.Appointments);

        Assert.Contains(h.Sim.PostingsFor(StaffRole.Doctor), p => p.RoomId == clinic.Id);
        Assert.Contains(h.Sim.PostingsFor(StaffRole.Receptionist), p => p.RoomId == reception.Id);
        Assert.Contains(h.Sim.RecommendedStaffing(), n => n.RoomId == clinic.Id && n.Role == StaffRole.Doctor && n.Short == 1);
        Assert.Contains(h.Sim.RecommendedStaffing(), n => n.RoomId == reception.Id && n.Role == StaffRole.Receptionist && n.Short == 1);
    }

    [Fact]
    public void Demolishing_a_clinic_cancels_its_appointments_and_sends_patients_home()
    {
        var (h, _, clinic) = Outpatients();
        h.RunUntil(() => h.Sim.Outpatients.Any(o => o.Stage == OutpatientStage.BeingSeen), TimeSpan.FromHours(2), "a clinic patient being seen");
        Assert.True(h.Sim.DemolishRoom(clinic.Id).CanBuild);
        h.RunFor(TimeSpan.FromMinutes(1));
        Assert.All(h.Sim.Outpatients, o => Assert.Equal(OutpatientStage.Leaving, o.Stage));
        Assert.Contains(h.Sim.Notifications.Recent, n => n.Text.Contains("cancelling appointments"));
    }

    [Fact]
    public void Chairs_can_be_placed_in_corridors_for_clinic_patients()
    {
        var (h, reception, _) = Outpatients();
        var corridorTile = new GridCoord(B.X - 25, B.Y + 12);
        var inRoom = new GridCoord(reception.Rect.X + 3, reception.Rect.Y + 3);

        var before = h.Sim.Balance;
        Assert.True(h.Sim.PlaceCorridorChair(corridorTile).CanBuild);
        Assert.Equal(before - h.Sim.CorridorChairCost, h.Sim.Balance);
        Assert.Contains(corridorTile, h.Sim.OutpatientSeats);
        Assert.Contains(h.Sim.CheckCorridorChair(corridorTile).Problems, p => p.Contains("already a chair"));
        Assert.Contains(h.Sim.CheckCorridorChair(inRoom).Problems, p => p.Contains("corridors"));

        var door = reception.Doors.First();
        var doorway = reception.DoorSide == WallSide.North
            ? new GridCoord(reception.Rect.X + door.Offset, reception.Rect.Y - 1)
            : new GridCoord(-1, -1);
        if (doorway.X >= 0)
        {
            Assert.Contains(h.Sim.CheckCorridorChair(doorway).Problems, p => p.Contains("doorways"));
        }

        Assert.True(h.Sim.RemoveCorridorChair(corridorTile));
        Assert.Equal(before - h.Sim.CorridorChairCost + (h.Sim.CorridorChairCost / 2), h.Sim.Balance);
        Assert.Empty(h.Sim.CorridorChairs);
    }

    [Fact]
    public void Clinics_and_corridor_chairs_are_saved()
    {
        var (h, _, clinic) = Outpatients();
        h.Sim.PlaceCorridorChair(new GridCoord(B.X - 25, B.Y + 12));
        h.RunUntil(() => h.Sim.Outpatients.Any(o => o.Stage == OutpatientStage.BeingSeen), TimeSpan.FromHours(2), "a clinic patient being seen");

        var json = SaveSerializer.Serialize(new SaveGame { Clock = h.Clock.CaptureState(), Layout = h.Sim.CaptureLayout(), People = h.Sim.CapturePeople() });
        var save = SaveSerializer.Deserialize(json);
        var to = new SimulationHarness(seed: 99);
        to.Clock.RestoreState(save.Clock!);
        to.Sim.RestoreLayout(save.Layout!);
        to.Sim.RestorePeople(save.People!);

        Assert.Single(to.Sim.CorridorChairs);
        Assert.Equal(h.Sim.Appointments.Count, to.Sim.Appointments.Count);
        Assert.Equal(h.Sim.Outpatients.Count, to.Sim.Outpatients.Count);
        var seen = h.Sim.Outpatients.First(o => o.Stage == OutpatientStage.BeingSeen);
        Assert.Equal(OutpatientStage.BeingSeen, to.Sim.FindOutpatient(seen.Id)!.Stage);
        Assert.Single(to.Sim.Consults);
        Assert.Equal(
            h.Sim.CalendarFor(clinic.Id, h.Clock.Now).Select(a => (a.Id, a.FullName, a.Time, a.Status)),
            to.Sim.CalendarFor(clinic.Id, h.Clock.Now).Select(a => (a.Id, a.FullName, a.Time, a.Status)));

        to.RunUntil(() => to.Sim.Appointments.Any(a => a.Status == AppointmentStatus.Seen), TimeSpan.FromHours(3), "a patient seen after loading");
    }
}
