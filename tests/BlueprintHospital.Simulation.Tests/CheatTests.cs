using BlueprintHospital.Simulation.Simulation;

namespace BlueprintHospital.Simulation.Tests;

public class CheatTests
{
    [Fact]
    public void Money_adds_to_the_balance_and_books_a_cheat_line()
    {
        var h = new SimulationHarness();
        var start = h.Sim.Balance;

        var result = h.Sim.RunCheat("money 500000");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(start + 500_000, h.Sim.Balance);
        Assert.Equal(500_000, h.Sim.Today[MoneyCategory.Cheats]);
    }

    [Theory]
    [InlineData("500000", 500_000L)]
    [InlineData("£1,250,000", 1_250_000L)]
    [InlineData("250k", 250_000L)]
    [InlineData("1.5m", 1_500_000L)]
    [InlineData("-10k", -10_000L)]
    [InlineData("2bn", null)]
    [InlineData("lots", null)]
    [InlineData("", null)]
    public void Amounts_are_read_with_shorthand(string text, long? expected)
    {
        var ok = HospitalSimulation.TryParseCheatAmount(text, out var amount);
        Assert.Equal(expected is not null, ok);
        if (expected is { } value)
        {
            Assert.Equal(value, amount);
        }
    }

    [Theory]
    [InlineData("money")]
    [InlineData("money abc")]
    [InlineData("money 0")]
    [InlineData("bananas 5")]
    [InlineData("   ")]
    public void Bad_commands_change_nothing(string command)
    {
        var h = new SimulationHarness();
        var start = h.Sim.Balance;

        var result = h.Sim.RunCheat(command);

        Assert.False(result.Succeeded);
        Assert.Equal(start, h.Sim.Balance);
    }

    [Fact]
    public void Commands_ignore_case_and_extra_spaces()
    {
        var h = new SimulationHarness();
        var start = h.Sim.Balance;

        Assert.True(h.Sim.RunCheat("  MONEY   1000 ").Succeeded);
        Assert.Equal(start + 1000, h.Sim.Balance);
    }
}
