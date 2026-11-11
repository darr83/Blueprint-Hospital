using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>One person to a chair, for staff and patients alike.</summary>
public class SeatingTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    private static void Build(SimulationHarness h, BuildCheck result, string what) => Assert.True(result.CanBuild, $"{what}: {string.Join("; ", result.Problems)}");

    /// <summary>Everyone standing still on a chair, grouped by chair, where more than one is on it.</summary>
    private static List<string> Shared(HospitalSimulation sim)
    {
        var everyone = sim.Staff.Where(s => s.IsInBuilding).Cast<Agent>()
            .Concat(sim.Patients.Where(p => p.Stage != PatientStage.Gone))
            .Concat(sim.Outpatients);
        return everyone
            .Where(a => !a.IsMoving && sim.IsSeat(a.Tile) && a is not StaffMember { PushingPatientId: not null })
            .GroupBy(a => a.Tile)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} [{string.Join("/", sim.Spaces.Where(r => r.Rect.Contains(g.Key)).Select(r => $"{r.Kind} staff {r.StaffSpot} pt {r.PatientSpot}"))}]: {string.Join(", ", g.Select(a => a is StaffMember m ? $"{m.Role} {m.Id} ({m.Activity}, task {sim.Tasks.FirstOrDefault(t => t.Id == m.CurrentTaskId)?.Kind} for {sim.Tasks.FirstOrDefault(t => t.Id == m.CurrentTaskId)?.PatientId} at {sim.Patients.FirstOrDefault(p => p.Id == sim.Tasks.FirstOrDefault(t => t.Id == m.CurrentTaskId)?.PatientId)?.Tile}, pushing {m.PushingPatientId}, {m.StatusText})" : a is Patient pp ? $"Patient {pp.Id} ({pp.Stage}, seat {pp.Seat}, {pp.Mobility})" : $"{a.GetType().Name} {a.Id}"))}")
            .ToList();
    }

    [Fact]
    public void Two_pharmacists_never_share_the_counter_chair()
    {
        var h = new SimulationHarness(5, arrivals: true);
        h.Sim.AdjustBalance(2_000_000);
        Build(h, h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)), "extension");
        Build(h, h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 10, 34, 6), WallSide.South)), "corridor");
        Build(h, h.Sim.BuildRoom(h.Sim.AutoPlan("pharmacy", new GridRect(B.X - 30, B.Y + 2, 10, 8))), "pharmacy");
        Build(h, h.Sim.BuildRoom(h.Sim.AutoPlan("staff_room", new GridRect(B.X - 20, B.Y + 16, 8, 8))), "staff room");
        h.StaffToRecommended();
        var pharmacy = h.Sim.SpacesOfKind(CareSpaceKind.Pharmacy).Single();
        foreach (var shift in new[] { StaffShift.Day, StaffShift.Night })
        {
            h.Sim.HireStaff(StaffRole.Pharmacist, pharmacy.Id, shift: shift);
            h.Sim.HireStaff(StaffRole.Pharmacist, pharmacy.Id, shift: shift);
        }

        // Plenty of nurses and doctors for the few seats in the nurses' station and doctors' office.
        for (var i = 0; i < 6; i++)
        {
            h.Sim.HireStaff(StaffRole.Nurse, shift: StaffShift.Day);
            h.Sim.HireStaff(StaffRole.Doctor, shift: StaffShift.Day);
            h.Sim.HireStaff(StaffRole.Porter, shift: StaffShift.Day);
        }

        var problems = new List<string>();
        for (var minute = 0; minute < 8 * 60 && problems.Count == 0; minute++)
        {
            h.RunFor(TimeSpan.FromMinutes(1));
            problems.AddRange(Shared(h.Sim).Select(p => $"{h.Sim.Now:HH:mm} {p}"));
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems) + string.Join(Environment.NewLine + Environment.NewLine, h.Sim.Patients.Where(p => p.Id <= 2).Select(h.Journey)));
    }

    [Fact]
    public void A_busy_AE_day_never_puts_two_people_on_one_chair()
    {
        var h = new SimulationHarness(11, arrivals: true, arrivalMultiplier: 2.0);
        for (var i = 0; i < 4; i++)
        {
            h.Sim.HireStaff(StaffRole.Nurse, shift: StaffShift.Day);
            h.Sim.HireStaff(StaffRole.Doctor, shift: StaffShift.Day);
        }

        var problems = new List<string>();
        for (var minute = 0; minute < 10 * 60 && problems.Count == 0; minute++)
        {
            h.RunFor(TimeSpan.FromMinutes(1));
            problems.AddRange(Shared(h.Sim).Select(p => $"{h.Sim.Now:HH:mm} {p}"));
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void A_staff_member_whose_seat_is_taken_sits_on_another_free_one()
    {
        var h = new SimulationHarness();
        var station = h.Sim.SpacesOfKind(CareSpaceKind.NursesStation).First();
        var nurses = h.Sim.Staff.Where(s => s.Role == StaffRole.Nurse && s.IsOnDuty).ToList();
        var first = nurses[0];
        var usual = station.Seats[0];
        first.PlaceAt(usual);
        foreach (var other in nurses.Skip(1))
        {
            var spot = h.Sim.HomeTile(other);
            Assert.NotEqual(usual, spot);
            other.PlaceAt(spot);
        }

        Assert.Empty(Shared(h.Sim));
    }
}
