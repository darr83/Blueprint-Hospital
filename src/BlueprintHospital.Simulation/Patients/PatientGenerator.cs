using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Medical;

namespace BlueprintHospital.Simulation.Patients;

/// <summary>
/// Creates believable patients: a real condition chosen by prevalence, symptoms and observations
/// consistent with it, and a background (history, medications, allergies) consistent with their age.
/// </summary>
public sealed class PatientGenerator(GameDatabase database, GameRandom random)
{
    private int _nextId = 1;

    public int NextId => _nextId;

    /// <summary>Lets loaded games continue numbering where they left off.</summary>
    public void SetNextId(int next) => _nextId = Math.Max(_nextId, next);

    /// <summary>Generates a patient. Pass <paramref name="forcedMethod"/> or <paramref name="conditionId"/> to control the scenario.</summary>
    public Patient Generate(DateTime now, ArrivalMethod? forcedMethod = null, string? conditionId = null)
    {
        var condition = conditionId is null
            ? PickCondition(forcedMethod)
            : database.Conditions[conditionId];

        var sex = condition.IsMaternity ? Sex.Female : random.Chance(condition.FemaleFraction) ? Sex.Female : Sex.Male;
        var age = SampleAge(condition);
        var dob = now.Date.AddYears(-age).AddDays(-random.Range(1, 364));
        var names = database.Names.Values.First();
        var first = random.Pick(sex == Sex.Female ? names.FemaleFirstNames : names.MaleFirstNames);
        var surname = random.Pick(names.Surnames);

        var (history, medications, allergies, allergyGroups) = GenerateBackground(age);

        var symptoms = new List<string> { condition.PresentingComplaint };
        foreach (var s in condition.Symptoms.Where(s => s.Symptom != condition.PresentingComplaint))
        {
            if (random.Chance(s.Probability))
            {
                symptoms.Add(s.Symptom);
            }
        }

        var method = forcedMethod ?? (random.Chance(condition.AmbulanceProbability) ? ArrivalMethod.Ambulance : ArrivalMethod.WalkIn);
        var severity = random.Range(condition.InitialSeverity[0], condition.InitialSeverity[1]);
        var o = condition.Observations;

        var baseline = new Observations(
            HeartRate: (int)Math.Round(Math.Clamp(random.Normal(o.HeartRate[0], o.HeartRate[1]), 40, 180)),
            SystolicBp: (int)Math.Round(Math.Clamp(random.Normal(o.SystolicBp[0], o.SystolicBp[1]), 80, 210)),
            DiastolicBp: (int)Math.Round(Math.Clamp(random.Normal(o.DiastolicBp[0], o.DiastolicBp[1]), 40, 120)),
            RespiratoryRate: (int)Math.Round(Math.Clamp(random.Normal(o.RespiratoryRate[0], o.RespiratoryRate[1]), 10, 40)),
            OxygenSaturation: (int)Math.Round(Math.Clamp(random.Normal(o.OxygenSaturation[0], o.OxygenSaturation[1]), 85, 100)),
            Temperature: Math.Round(Math.Clamp(random.Normal(o.Temperature[0], o.Temperature[1]), 35, 40.5), 1),
            Consciousness: Consciousness.Alert,
            PainScore: random.Range((int)o.PainScore[0], (int)o.PainScore[1]));

        var id = _nextId++;
        var patient = new Patient
        {
            Id = id,
            HospitalNumber = $"BH{id:000000}",
            FirstName = first,
            Surname = surname,
            Sex = sex,
            Age = age,
            DateOfBirth = dob,
            PastHistory = history,
            RegularMedications = medications,
            Allergies = allergies,
            AllergyGroups = allergyGroups,
            PreviousVisits = random.Chance(0.35) ? random.Range(1, 4) : 0,
            ArrivalMethod = method,
            ArrivedAt = now,
            PresentingComplaint = condition.PresentingComplaint,
            Symptoms = symptoms,
            Condition = condition,
            BaselineObservations = baseline,
            InitialSeverity = severity,
            Severity = severity,
            QueuedSince = now,
        };

        if (condition.Maternity is { } maternity)
        {
            patient.Maternity = true;
            patient.GestationWeeks = random.Range(maternity.GestationWeeks[0], maternity.GestationWeeks[1]);
        }

        patient.KnownSymptoms.Add(condition.PresentingComplaint);
        return patient;
    }

    /// <summary>A woman coming to the maternity unit: a maternity condition chosen by how common it is.</summary>
    public Patient GenerateMaternity(DateTime now, string? conditionId = null)
    {
        var condition = conditionId ?? random.PickWeighted(
            database.Conditions.Values.Where(c => c.Maternity is { Arrives: true }).OrderBy(c => c.Id, StringComparer.Ordinal).ToList(),
            c => c.Prevalence).Id;
        return Generate(now, ArrivalMethod.WalkIn, condition);
    }

    private ConditionDefinition PickCondition(ArrivalMethod? method)
    {
        // Women in labour go to the maternity unit, and planned patients are booked in: neither comes to A&E.
        var conditions = database.Conditions.Values.Where(c => !c.IsMaternity && !c.IsPlanned).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        return method switch
        {
            ArrivalMethod.Ambulance => random.PickWeighted(conditions, c => c.Prevalence * c.AmbulanceProbability),
            ArrivalMethod.WalkIn => random.PickWeighted(conditions, c => c.Prevalence * (1 - c.AmbulanceProbability)),
            _ => random.PickWeighted(conditions, c => c.Prevalence),
        };
    }

    private int SampleAge(ConditionDefinition condition)
    {
        // Triangular distribution peaking two-thirds of the way through the range, so older
        // patients dominate heart conditions and young adults dominate injuries.
        var min = condition.AgeRange[0];
        var max = condition.AgeRange[1];
        var mode = condition.CanBeFatal ? min + ((max - min) * 0.6) : min + ((max - min) * 0.35);
        var u = random.NextDouble();
        var f = (mode - min) / Math.Max(1, max - min);
        var value = u < f
            ? min + Math.Sqrt(u * (max - min) * (mode - min))
            : max - Math.Sqrt((1 - u) * (max - min) * (max - mode));
        return (int)Math.Round(value);
    }

    private (List<string> History, List<string> Medications, List<string> Allergies, List<string> Groups) GenerateBackground(int age)
    {
        var history = new List<string>();
        var medications = new List<string>();
        var allergies = new List<string>();
        var groups = new List<string>();

        var background = database.PatientBackgrounds.Values.FirstOrDefault();
        if (background is null)
        {
            return (history, medications, allergies, groups);
        }

        foreach (var entry in background.PastHistory)
        {
            if (random.Chance(entry.ProbabilityAt(age)))
            {
                history.Add(entry.Name);
                medications.AddRange(entry.RegularMedications);
            }
        }

        foreach (var allergy in background.Allergies)
        {
            if (random.Chance(allergy.Probability))
            {
                allergies.Add(allergy.Name);
                groups.Add(allergy.Group);
            }
        }

        return (history, medications.Distinct().ToList(), allergies, groups);
    }
}
