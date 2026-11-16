using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Patients;

namespace BlueprintHospital.Simulation.Medical;

/// <summary>How a line of a hint should be coloured.</summary>
public enum HintTone
{
    Plain,
    Bad,
    Good,
    Muted,
}

/// <summary>One fact on the hint card, e.g. "Heart rate" / "134, very fast".</summary>
public sealed record HintLine(string Label, string Value, HintTone Tone = HintTone.Plain);

/// <summary>A diagnosis worth thinking about: how well it fits (1 to 4) and why, in a sentence or two.</summary>
public sealed record HintCandidate(ConditionDefinition Condition, int Strength, string Reason);

/// <summary>
/// Everything the hint card shows for one patient. It lays out what is known and what that points
/// towards, but never names the answer: the player still chooses the diagnosis.
/// </summary>
public sealed record ClinicalHint(
    bool HistoryTaken,
    IReadOnlyList<HintLine> History,
    IReadOnlyList<HintLine> Observations,
    IReadOnlyList<HintLine> Tests,
    IReadOnlyList<string> Clues,
    IReadOnlyList<HintCandidate> Candidates,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Builds hints from the medical data alone (how often each condition shows each symptom, how often
/// each test is abnormal in it), so they work for every condition and follow any edit to the data.
/// </summary>
public static class ClinicalHints
{
    /// <summary>A symptom this common in a condition is a hallmark: its absence counts against the condition.</summary>
    private const double Hallmark = 0.75;

    /// <summary>A test abnormal this often in a condition supports it when abnormal.</summary>
    private const double Supports = 0.5;

    public static ClinicalHint Build(Patient patient, GameDatabase db, DiagnosisEngine engine, Func<InvestigationDefinition, bool> canDo)
    {
        var known = (patient.HistoryTaken ? patient.Symptoms.AsEnumerable() : patient.KnownSymptoms.OrderBy(s => s, StringComparer.Ordinal)).ToList();
        var results = patient.Investigations.Where(o => o.Status == InvestigationStatus.Resulted).ToList();
        var waiting = patient.Investigations.Where(o => o.Status != InvestigationStatus.Resulted).ToList();
        var differential = engine.Differential(patient);
        var shortlist = differential.Take(6).Where(d => d.Probability >= 0.002).Select(d => d.Condition).ToList();
        if (shortlist.Count == 0)
        {
            shortlist.Add(differential[0].Condition);
        }

        string Symptom(string id) => db.Symptoms[id].Name.ToLowerInvariant();
        string Test(string id) => db.Investigations[id].Name;

        // ------------------------------------------------------------ what is known
        var history = new List<HintLine>();
        foreach (var s in known)
        {
            history.Add(new HintLine(db.Symptoms[s].Name, db.Symptoms[s].IsRedFlag ? "red flag" : string.Empty, db.Symptoms[s].IsRedFlag ? HintTone.Bad : HintTone.Plain));
        }

        var missing = patient.HistoryTaken
            ? shortlist.SelectMany(c => c.Symptoms.Where(s => s.Probability >= Hallmark && !known.Contains(s.Symptom)).Select(s => s.Symptom)).Distinct().Take(4).ToList()
            : new List<string>();
        if (missing.Count > 0)
        {
            history.Add(new HintLine("No " + JoinOr(missing.Select(Symptom)), string.Empty, HintTone.Muted));
        }

        if (!patient.HistoryTaken)
        {
            history.Add(new HintLine("Full history not taken yet", string.Empty, HintTone.Muted));
        }
        else
        {
            history.Add(new HintLine("Past history", patient.PastHistory.Count == 0 ? "nothing significant" : string.Join(", ", patient.PastHistory), HintTone.Muted));
        }

        history.Add(patient.Allergies.Count == 0
            ? new HintLine("Allergies", "none known", HintTone.Muted)
            : new HintLine("Allergy", string.Join(", ", patient.Allergies), HintTone.Bad));

        var observations = new List<HintLine>();
        if (patient.LatestObservations is { } latest)
        {
            var (o, n) = latest;
            observations.Add(Vital("Heart rate", o.HeartRate.ToString(), n.HeartRate, o.HeartRate > 90 ? "fast" : "slow"));
            observations.Add(Vital("Breathing rate", o.RespiratoryRate.ToString(), n.RespiratoryRate, o.RespiratoryRate > 20 ? "fast" : "slow"));
            observations.Add(Vital("Oxygen saturation", o.OxygenSaturation + "%", n.OxygenSaturation, "low"));
            observations.Add(Vital("Blood pressure", o.BloodPressure, n.SystolicBp, o.SystolicBp > 200 ? "high" : "low"));
            observations.Add(Vital("Temperature", o.Temperature.ToString("0.0") + "°C", n.Temperature, o.Temperature > 37.5 ? "raised" : "low", o.Temperature >= 37.8 ? "raised" : null));
            if (o.Consciousness != Consciousness.Alert)
            {
                observations.Add(new HintLine("Conscious level", o.Consciousness.ToString().ToLowerInvariant(), HintTone.Bad));
            }

            observations.Add(new HintLine("NEWS2", n.Total + (n.Total >= Patient.RedMonitorNews2 ? " · high risk" : n.Total >= 5 || n.HasRedScore ? " · medium risk" : " · low risk"),
                n.Total >= 5 || n.HasRedScore ? HintTone.Bad : HintTone.Plain));
        }
        else
        {
            observations.Add(new HintLine("No observations taken yet", string.Empty, HintTone.Muted));
        }

        var tests = new List<HintLine>();
        foreach (var r in results)
        {
            tests.Add(new HintLine(Test(r.InvestigationId), r.Abnormal ? "abnormal" : "normal", r.Abnormal ? HintTone.Bad : HintTone.Good));
        }

        foreach (var w in waiting)
        {
            tests.Add(new HintLine(Test(w.InvestigationId), w.Status == InvestigationStatus.SampleTaken ? "at the lab" : "not done yet", HintTone.Muted));
        }

        if (tests.Count == 0)
        {
            tests.Add(new HintLine("No tests requested yet", string.Empty, HintTone.Muted));
        }

        // ------------------------------------------------------------ what stands out
        var clues = new List<string>();
        foreach (var r in results.Where(r => r.Abnormal))
        {
            var test = db.Investigations[r.InvestigationId];
            var seenIn = shortlist.Where(c => test.ProbabilityAbnormal(c.Id) >= Supports).Select(c => c.Name).Take(3).ToList();
            clues.Add($"{test.Name}: {TrimStop(r.Summary)}."
                      + (seenIn.Count > 0 ? $" Usually abnormal in {JoinAnd(seenIn)}." : " Not typical of the commoner causes here."));
        }

        foreach (var r in results.Where(r => !r.Abnormal))
        {
            var test = db.Investigations[r.InvestigationId];
            var against = shortlist.Where(c => test.ProbabilityAbnormal(c.Id) >= Hallmark).Select(c => c.Name).Take(3).ToList();
            if (against.Count > 0)
            {
                clues.Add($"A normal {test.Name} makes {JoinOr(against)} less likely.");
            }
        }

        foreach (var s in missing)
        {
            var usual = shortlist.Where(c => c.ChanceOf(s) >= Hallmark).Select(c => c.Name).Take(3).ToList();
            clues.Add($"No {Symptom(s)}, which is usual in {JoinAnd(usual)}.");
        }

        if (patient.LatestObservations is { News2: var score } && (score.Total >= 5 || score.HasRedScore))
        {
            clues.Add(score.Total >= Patient.RedMonitorNews2
                ? $"NEWS2 is {score.Total}: seriously unwell. If the picture is clear, don't wait for every result."
                : $"NEWS2 is {score.Total}: unwell enough to keep a close eye on.");
        }

        var ordered = patient.Investigations.Select(o => o.InvestigationId).ToHashSet();
        var wanted = shortlist.Take(3).SelectMany(c => c.KeyInvestigations).Distinct()
            .Where(id => !ordered.Contains(id) && InvestigationSystem.AppliesTo(db.Investigations[id], patient)).ToList();
        var canRequest = wanted.Where(id => canDo(db.Investigations[id])).Select(Test).Take(4).ToList();
        var cannot = wanted.Where(id => !canDo(db.Investigations[id])).Select(Test).Take(3).ToList();
        if (canRequest.Count > 0)
        {
            clues.Add($"Not requested yet and likely to help: {JoinAnd(canRequest)}.");
        }

        if (cannot.Count > 0)
        {
            clues.Add($"{JoinAnd(cannot)} would help, but this hospital has no staffed room for {(cannot.Count == 1 ? "it" : "them")}.");
        }

        if (waiting.Count > 0)
        {
            clues.Add($"Still to come back: {JoinAnd(waiting.Select(w => Test(w.InvestigationId)).Take(4))}.");
        }

        // ------------------------------------------------------------ worth thinking about
        var candidates = new List<HintCandidate>();
        foreach (var entry in differential.Take(4).Where((d, i) => i < 2 || d.Probability >= 0.002))
        {
            var c = entry.Condition;
            var strength = entry.Probability >= 0.6 ? 4 : entry.Probability >= 0.25 ? 3 : entry.Probability >= 0.08 ? 2 : 1;
            var fits = known.Where(s => c.ChanceOf(s) >= 0.3).Select(Symptom).ToList();
            var support = results.Where(r => r.Abnormal && db.Investigations[r.InvestigationId].ProbabilityAbnormal(c.Id) >= Supports).Select(r => "abnormal " + Test(r.InvestigationId)).ToList();
            var absent = patient.HistoryTaken ? c.Symptoms.Where(s => s.Probability >= Hallmark && !known.Contains(s.Symptom)).Select(s => "no " + Symptom(s.Symptom)).ToList() : new List<string>();
            var normal = results.Where(r => !r.Abnormal && db.Investigations[r.InvestigationId].ProbabilityAbnormal(c.Id) >= Hallmark).Select(r => "normal " + Test(r.InvestigationId)).ToList();
            var pending = waiting.Where(w => c.KeyInvestigations.Contains(w.InvestigationId)).Select(w => Test(w.InvestigationId)).ToList();

            var parts = new List<string>();
            if (strength == 1 && c.CanBeFatal)
            {
                parts.Add("Unlikely, but dangerous if missed.");
            }

            var forIt = fits.Concat(support).Take(4).ToList();
            if (forIt.Count > 0)
            {
                parts.Add($"Fits the {JoinAnd(forIt)}.");
            }

            var againstIt = absent.Concat(normal).Take(3).ToList();
            if (againstIt.Count > 0)
            {
                parts.Add($"Against it: {JoinAnd(againstIt)}.");
            }

            if (pending.Count > 0)
            {
                parts.Add($"{JoinAnd(pending.Take(2))} {(pending.Count == 1 ? "is" : "are")} not back yet.");
            }

            if (parts.Count == 0)
            {
                parts.Add("Little for or against it so far.");
            }

            candidates.Add(new HintCandidate(c, strength, string.Join(" ", parts)));
        }

        // ------------------------------------------------------------ before you prescribe
        var warnings = new List<string>();
        foreach (var c in candidates.Select(k => k.Condition))
        {
            foreach (var step in c.Treatment.Where(t => t.Type == TreatmentStepType.Medication && t.Medication is not null))
            {
                var med = db.Medications[step.Medication!];
                var clash = med.AllergyGroups.FirstOrDefault(g => patient.AllergyGroups.Contains(g));
                if (clash is not null)
                {
                    var allergy = patient.Allergies.FirstOrDefault(a => a.Contains(clash, StringComparison.OrdinalIgnoreCase)) ?? clash;
                    var text = $"Allergy to {StripDetail(allergy).ToLowerInvariant()}: the usual treatment for {c.Name} includes {med.Name.ToLowerInvariant()}.";
                    if (!warnings.Contains(text))
                    {
                        warnings.Add(text);
                    }
                }
            }
        }

        return new ClinicalHint(patient.HistoryTaken, history, observations, tests, clues.Take(7).ToList(), candidates, warnings.Take(3).ToList());
    }

    private static HintLine Vital(string label, string value, int score, string direction, string? mild = null) => score switch
    {
        >= 3 => new HintLine(label, $"{value}, very {direction}", HintTone.Bad),
        2 => new HintLine(label, $"{value}, {direction}", HintTone.Bad),
        1 => new HintLine(label, $"{value}, a little {direction}"),
        _ => new HintLine(label, mild is null ? $"{value}, normal" : $"{value}, {mild}", mild is null ? HintTone.Good : HintTone.Plain),
    };

    private static string TrimStop(string text) => text.Trim().TrimEnd('.');

    /// <summary>"Penicillin (rash)" → "Penicillin".</summary>
    private static string StripDetail(string allergy)
    {
        var bracket = allergy.IndexOf('(');
        return (bracket > 0 ? allergy[..bracket] : allergy).Trim();
    }

    private static string JoinAnd(IEnumerable<string> items) => Join(items, "and");

    private static string JoinOr(IEnumerable<string> items) => Join(items, "or");

    private static string Join(IEnumerable<string> items, string last)
    {
        var list = items.ToList();
        return list.Count switch
        {
            0 => string.Empty,
            1 => list[0],
            _ => $"{string.Join(", ", list.Take(list.Count - 1))} {last} {list[^1]}",
        };
    }
}
