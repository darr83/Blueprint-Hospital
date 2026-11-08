using BlueprintHospital.Simulation.Medical;

namespace BlueprintHospital.Simulation.Tests;

public class TriageCategoryTests
{
    [Fact]
    public void Colours_match_the_specification()
    {
        Assert.Equal("#E53935", TriageCategoryInfo.HexColour(TriageCategory.Red));
        Assert.Equal("#F9A825", TriageCategoryInfo.HexColour(TriageCategory.Amber));
        Assert.Equal("#43A047", TriageCategoryInfo.HexColour(TriageCategory.Green));
    }

    [Fact]
    public void Red_is_most_urgent()
    {
        Assert.True(TriageCategory.Red.IsMoreUrgentThan(TriageCategory.Amber));
        Assert.True(TriageCategory.Amber.IsMoreUrgentThan(TriageCategory.Green));
        Assert.False(TriageCategory.Green.IsMoreUrgentThan(TriageCategory.Red));
        Assert.Equal(3, Enum.GetValues<TriageCategory>().Length);
    }
}
