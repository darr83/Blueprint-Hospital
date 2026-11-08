using System.Globalization;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Patients;

namespace BlueprintHospital.Simulation.Medical;

/// <summary>Produces investigation results from the patient's true condition, not at random.</summary>
public static class InvestigationSystem
{
    public static bool AppliesTo(InvestigationDefinition test, Patient patient) => test.AppliesToSex switch
    {
        SexRestriction.Female => patient.Sex == Sex.Female,
        SexRestriction.Male => patient.Sex == Sex.Male,
        _ => true,
    };

    /// <summary>Fills in the result of <paramref name="order"/> for <paramref name="patient"/>.</summary>
    public static void GenerateResult(InvestigationDefinition test, Patient patient, InvestigationOrder order, GameRandom random)
    {
        var specific = test.Abnormal.FirstOrDefault(a => a.Condition == patient.Condition.Id);
        ResultTemplate template;
        bool abnormal;

        if (specific is not null && random.Chance(specific.Probability))
        {
            template = specific.AsTemplate();
            abnormal = true;
        }
        else if (specific is null && test.FalsePositive is not null && random.Chance(test.FalsePositiveRate))
        {
            template = test.FalsePositive;
            abnormal = true;
        }
        else
        {
            template = test.Normal;
            abnormal = false;
        }

        double? value = template.ValueRange is { } range ? random.Range(range[0], range[1]) : null;
        var summary = random.Pick(template.Summaries);
        if (value is { } v)
        {
            summary = summary.Replace("{value}", FormatValue(v), StringComparison.Ordinal);
        }

        order.Abnormal = abnormal;
        order.Value = value;
        order.Summary = summary;
    }

    public static string FormatValue(double value) =>
        value >= 20 ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.0", CultureInfo.InvariantCulture);
}
