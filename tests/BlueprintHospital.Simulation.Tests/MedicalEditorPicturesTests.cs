using System.Text.RegularExpressions;
using BlueprintHospital.Simulation.Medical.Results;
using Xunit;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>The Medical Editor's list of pictures matches the pictures the game can draw.</summary>
public class MedicalEditorPicturesTests
{
    [Fact]
    public void Editor_picture_list_matches_the_game()
    {
        var html = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(TestData.GameDataDirectory()))!, "tools", "MedicalEditor", "MedicalEditor.html"));
        var match = Regex.Match(html, @"const RESULT_PICTURES = \[([^\]]*)\];");
        Assert.True(match.Success, "RESULT_PICTURES not found in the editor");
        var listed = Regex.Matches(match.Groups[1].Value, "\"([a-z0-9_]+)\"").Select(m => m.Groups[1].Value).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(ResultPictures.Ids, listed);
    }
}
