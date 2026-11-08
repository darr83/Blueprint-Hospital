using BlueprintHospital.Simulation.Data;

namespace BlueprintHospital.Simulation.Medical;

/// <summary>Result of a triage assessment, with the reason so the player can see why.</summary>
public sealed record TriageDecision(TriageCategory Category, string Reason, News2Score News2);

/// <summary>
/// Assigns Red / Amber / Green from what the triage nurse can see: observations (NEWS2),
/// consciousness, pain and the reported symptoms. It never looks at the hidden diagnosis.
/// </summary>
public static class TriageSystem
{
    public static TriageDecision Assess(Observations observations, IEnumerable<string> reportedSymptoms, GameDatabase database)
    {
        var news = News2.Calculate(observations);
        var redFlags = reportedSymptoms
            .Where(id => database.Symptoms.TryGetValue(id, out var s) && s.IsRedFlag)
            .Select(id => database.Symptoms[id].Name.ToLowerInvariant())
            .ToList();

        if (observations.Consciousness != Consciousness.Alert)
        {
            return new TriageDecision(TriageCategory.Red, $"Reduced consciousness ({observations.Consciousness}).", news);
        }

        if (news.Total >= 7)
        {
            return new TriageDecision(TriageCategory.Red, $"NEWS2 {news.Total}: high clinical risk.", news);
        }

        if (observations.OxygenSaturation < 90 || observations.SystolicBp <= 90)
        {
            return new TriageDecision(TriageCategory.Red, "Critically abnormal observations.", news);
        }

        if (redFlags.Count >= 2 && news.Total >= 3)
        {
            return new TriageDecision(TriageCategory.Red, $"{Capitalise(string.Join(" with ", redFlags))} and abnormal observations (NEWS2 {news.Total}).", news);
        }

        if (news.Total >= 5 || news.HasRedScore)
        {
            return new TriageDecision(TriageCategory.Amber, $"NEWS2 {news.Total}{(news.HasRedScore ? " with a single red score" : string.Empty)}.", news);
        }

        if (redFlags.Count > 0)
        {
            return new TriageDecision(TriageCategory.Amber, $"Red-flag symptom: {string.Join(", ", redFlags)}.", news);
        }

        if (observations.PainScore >= 7)
        {
            return new TriageDecision(TriageCategory.Amber, $"Severe pain ({observations.PainScore}/10).", news);
        }

        if (news.Total >= 3)
        {
            return new TriageDecision(TriageCategory.Amber, $"NEWS2 {news.Total}.", news);
        }

        return new TriageDecision(TriageCategory.Green, $"Stable observations (NEWS2 {news.Total}).", news);
    }

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
