namespace BlueprintHospital.Simulation.Tests;

internal static class TestData
{
    /// <summary>Finds the game's real Data folder by walking up from the test output directory.</summary>
    public static string GameDataDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Data");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find game/Data from " + AppContext.BaseDirectory);
    }
}
