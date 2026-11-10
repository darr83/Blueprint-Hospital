using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Patients;

namespace BlueprintHospital.Simulation.Medical;

/// <summary>One possible diagnosis and how likely the evidence makes it.</summary>
public sealed record DifferentialEntry(ConditionDefinition Condition, double Probability);

/// <summary>What the doctor decides after reviewing the patient.</summary>
public abstract record DiagnosticDecision
{
    /// <summary>Order these investigations, then review again.</summary>
    public sealed record OrderTests(IReadOnlyList<string> InvestigationIds, IReadOnlyList<DifferentialEntry> Differential) : DiagnosticDecision;

    /// <summary>Results are still outstanding.</summary>
    public sealed record AwaitResults(IReadOnlyList<DifferentialEntry> Differential) : DiagnosticDecision;

    /// <summary>A diagnosis is made. <see cref="IsCorrect"/> is known only to the simulation.</summary>
    public sealed record Diagnose(ConditionDefinition Condition, double Confidence, IReadOnlyList<DifferentialEntry> Differential, bool IsCorrect) : DiagnosticDecision;
}

/// <summary>
/// Clinical reasoning. Builds a differential diagnosis from the evidence a doctor actually has
/// (symptoms elicited, observations, test results) using Bayes' rule, decides which tests to order,
/// and commits to a diagnosis. Inexperienced, tired or overloaded doctors make more mistakes.
/// </summary>
public sealed class DiagnosisEngine(GameDatabase database)
{
    /// <summary>Confidence a doctor wants before diagnosing without further tests.</summary>
    public const double ConfidenceThreshold = 0.8;

    /// <summary>Evidence this strong (e.g. ST elevation on the ECG) is acted on without waiting for every test.</summary>
    public const double ActNowThreshold = 0.95;

    /// <summary>Other diagnoses at least this likely get their key tests ordered too.</summary>
    public const double RuleOutThreshold = 0.12;

    /// <summary>Chance of a symptom appearing in a condition that doesn't list it.</summary>
    private const double UnlistedSymptomChance = 0.02;

    /// <summary>Whether a test can be done here (an X-ray needs an X-ray room). Doctors diagnose without the tests they can't get.</summary>
    public Func<InvestigationDefinition, bool> CanDo { get; init; } = _ => true;

    public IReadOnlyList<DifferentialEntry> Differential(Patient patient)
    {
        var known = patient.HistoryTaken ? patient.Symptoms.ToHashSet() : patient.KnownSymptoms;
        var results = patient.Investigations.Where(i => i.Status == InvestigationStatus.Resulted).ToList();
        var age = patient.Age;

        var scores = new List<(ConditionDefinition Condition, double LogScore)>();
        // Labour and pregnancy emergencies are assessed by the maternity unit, and planned procedures are
        // booked in advance: neither is ever diagnosed in A&E.
        foreach (var condition in database.Conditions.Values.Where(c => !c.IsMaternity && !c.IsPlanned).OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            var log = Math.Log(condition.Prevalence);

            // Demographics.
            if (age < condition.AgeRange[0] || age > condition.AgeRange[1])
            {
                log += Math.Log(0.05);
            }

            var sexFit = patient.Sex == Sex.Female ? condition.FemaleFraction : 1 - condition.FemaleFraction;
            log += Math.Log(Math.Max(sexFit, 0.001) * 2);

            // Symptoms present and, once a full history is taken, typical symptoms that are absent.
            foreach (var symptom in known)
            {
                var p = condition.ChanceOf(symptom);
                log += Math.Log(p > 0 ? p : UnlistedSymptomChance);
            }

            if (patient.HistoryTaken)
            {
                foreach (var s in condition.Symptoms.Where(s => !known.Contains(s.Symptom)))
                {
                    log += Math.Log(Math.Max(1 - s.Probability, 0.02));
                }
            }

            // Investigation results.
            foreach (var result in results)
            {
                var test = database.Investigations[result.InvestigationId];
                var pAbnormal = Math.Clamp(test.ProbabilityAbnormal(condition.Id), 0.005, 0.995);
                log += Math.Log(result.Abnormal ? pAbnormal : 1 - pAbnormal);
            }

            scores.Add((condition, log));
        }

        var max = scores.Max(s => s.LogScore);
        var weights = scores.Select(s => (s.Condition, Weight: Math.Exp(s.LogScore - max))).ToList();
        var total = weights.Sum(w => w.Weight);

        return weights
            .Select(w => new DifferentialEntry(w.Condition, w.Weight / total))
            .OrderByDescending(d => d.Probability)
            .ToList();
    }

    /// <summary>
    /// Decides the next step for a patient. <paramref name="skill"/> (0..1) and <paramref name="fatigue"/> (0..1)
    /// describe the doctor; <paramref name="workload"/> is how many patients they are juggling.
    /// </summary>
    public DiagnosticDecision Decide(Patient patient, double skill, double fatigue, int workload, GameRandom random)
    {
        var differential = Differential(patient);
        var top = differential[0];

        var ordered = patient.Investigations.Select(i => i.InvestigationId).ToHashSet();
        var toOrder = new List<string>();

        void Want(ConditionDefinition condition, bool scans = true)
        {
            foreach (var id in condition.KeyInvestigations)
            {
                var test = database.Investigations[id];
                if (!ordered.Contains(id) && !toOrder.Contains(id) && InvestigationSystem.AppliesTo(test, patient) && CanDo(test)
                    && (scans || test.Category != InvestigationCategory.Imaging))
                {
                    toOrder.Add(id);
                }
            }
        }

        // Always complete the work-up for the leading diagnosis, and rule out serious alternatives.
        Want(top.Condition);
        foreach (var alternative in differential.Skip(1).Where(d => d.Probability >= RuleOutThreshold || (d.Condition.CanBeFatal && d.Probability >= 0.03)))
        {
            Want(alternative.Condition);
        }

        // Must-not-miss: a life-threatening cause of this presenting complaint is always ruled out in
        // patients of an age where it occurs (e.g. troponin for chest pain), however unlikely it looks.
        // Scans are kept for when the condition is a real possibility, not ordered on every patient.
        foreach (var mustNotMiss in differential.Skip(1).Select(d => d.Condition).Where(c => IsMustNotMiss(c, patient)))
        {
            Want(mustNotMiss, scans: false);
        }

        var anyResults = patient.Investigations.Any(i => i.Status == InvestigationStatus.Resulted);
        if (anyResults && top.Probability >= ActNowThreshold)
        {
            // Treat now; outstanding tests still come back and are recorded.
            return Commit(patient, differential, skill, fatigue, workload, random);
        }

        if (toOrder.Count > 0)
        {
            return new DiagnosticDecision.OrderTests(toOrder, differential);
        }

        if (patient.Investigations.Any(i => i.Status != InvestigationStatus.Resulted))
        {
            return new DiagnosticDecision.AwaitResults(differential);
        }

        return Commit(patient, differential, skill, fatigue, workload, random);
    }

    private static bool IsMustNotMiss(ConditionDefinition condition, Patient patient) =>
        condition.CanBeFatal
        && condition.PresentingComplaint == patient.PresentingComplaint
        && patient.Age >= condition.AgeRange[0] - 10
        && patient.Age <= condition.AgeRange[1];

    private static DiagnosticDecision Commit(Patient patient, IReadOnlyList<DifferentialEntry> differential, double skill, double fatigue, int workload, GameRandom random)
    {
        var top = differential[0];

        // Mistakes are more likely when the picture is unclear and the doctor is junior, tired or busy.
        var errorChance = (0.02 + ((1 - skill) * 0.12) + (fatigue * 0.10) + (Math.Max(0, workload - 4) * 0.01))
                          * (1.2 - top.Probability);
        var chosen = top;
        if (differential.Count > 1 && differential[1].Probability > 0.01 && random.Chance(errorChance))
        {
            chosen = differential[1];
        }

        return new DiagnosticDecision.Diagnose(chosen.Condition, chosen.Probability, differential, chosen.Condition.Id == patient.Condition.Id);
    }
}
