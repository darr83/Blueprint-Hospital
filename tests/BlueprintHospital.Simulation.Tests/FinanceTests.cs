using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

public class FinanceTests
{
    [Fact]
    public void Wages_and_upkeep_are_charged_through_the_day()
    {
        var h = new SimulationHarness();
        var start = h.Sim.Balance;
        var perDay = h.Sim.StaffCostPerDay + h.Sim.UpkeepPerDay;
        Assert.True(perDay > 1000, $"Running costs look too low: £{perDay}/day");

        h.RunFor(TimeSpan.FromHours(12));
        var spent = start - h.Sim.Balance;
        Assert.InRange(spent, (perDay / 2) - 30, (perDay / 2) + 30);
        Assert.True(h.Sim.Today[MoneyCategory.Staff] < 0);
        Assert.True(h.Sim.Today[MoneyCategory.Upkeep] < 0);
    }

    [Fact]
    public void Each_attendance_is_paid_by_triage_category()
    {
        var h = new SimulationHarness(seed: 11);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration");
        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromHours(6), "patient to leave");

        var paid = h.Sim.Accounts.Sum(d => d[MoneyCategory.Attendances]);
        Assert.Equal(HospitalSimulation.AttendanceTariff[patient.Triage!.Value], paid);
        Assert.True(h.Sim.Accounts.Sum(d => d[MoneyCategory.Medicines] + d[MoneyCategory.Tests]) <= 0);
    }

    [Fact]
    public void Building_is_booked_as_construction()
    {
        var h = new SimulationHarness();
        var b = StarterSite.MainBuildingFootprint;
        var check = h.Sim.ExtendBuilding(new GridRect(b.X - 10, b.Y, 10, 20));
        Assert.True(check.CanBuild);
        Assert.Equal(-check.Cost, h.Sim.Today[MoneyCategory.Construction]);
    }
}
