using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>The rota's "Move someone from a team with a spare" suggestion.</summary>
public class RotaSpareTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    private static SimulationHarness WithEmptyWard()
    {
        var h = new SimulationHarness();
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_ward", new GridRect(B.X - 30, B.Y + 17, 26, 15))).CanBuild);
        return h;
    }

    private static StaffingNeed Need(SimulationHarness h, string? roomId, StaffRole role, StaffShift shift) =>
        h.Sim.RecommendedStaffing().Single(n => n.RoomId == roomId && n.Role == role && n.Shift == shift);

    private static void HireNursesForAe(SimulationHarness h, StaffShift shift, int extra)
    {
        var target = Need(h, null, StaffRole.Nurse, shift).Recommended + extra;
        foreach (var surplus in h.Sim.Staff.Where(s => s.Role == StaffRole.Nurse && s.BaseRoomId is null && s.Shift == shift).Skip(target).ToList())
        {
            h.Sim.Fire(surplus.Id);
        }

        for (var guard = 0; Need(h, null, StaffRole.Nurse, shift).Have < target && guard < 200; guard++)
        {
            if (h.Sim.Candidates.FirstOrDefault(c => c.Role == StaffRole.Nurse) is not { } candidate)
            {
                h.RunFor(TimeSpan.FromHours(HospitalSimulation.CandidatesEveryHours));
                continue;
            }

            Assert.NotNull(h.Sim.Hire(candidate.Id, null, shift));
        }

        Assert.Equal(target, Need(h, null, StaffRole.Nurse, shift).Have);
    }

    [Fact]
    public void A_spare_nurse_in_AandE_is_offered_to_an_empty_ward_on_the_same_shift()
    {
        var h = WithEmptyWard();
        var ward = h.Sim.Wards.Single();
        HireNursesForAe(h, StaffShift.Night, extra: 1);

        var spare = h.Sim.SpareFor(Need(h, ward.Id, StaffRole.Nurse, StaffShift.Night));

        Assert.NotNull(spare);
        Assert.Equal(StaffRole.Nurse, spare!.Role);
        Assert.Equal(StaffShift.Night, spare.Shift);
        Assert.Null(spare.BaseRoomId);
    }

    [Fact]
    public void Nobody_is_offered_when_moving_them_would_leave_their_own_team_short()
    {
        var h = WithEmptyWard();
        var ward = h.Sim.Wards.Single();
        HireNursesForAe(h, StaffShift.Night, extra: 0);

        Assert.Null(h.Sim.SpareFor(Need(h, ward.Id, StaffRole.Nurse, StaffShift.Night)));

        // A spare on the other shift doesn't help either.
        HireNursesForAe(h, StaffShift.Day, extra: 1);
        Assert.Null(h.Sim.SpareFor(Need(h, ward.Id, StaffRole.Nurse, StaffShift.Night)));
    }
}
