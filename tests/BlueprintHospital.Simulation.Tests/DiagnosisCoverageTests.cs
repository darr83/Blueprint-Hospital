using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using Xunit;
using Xunit.Abstractions;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>
/// Every illness must be one a good doctor can work out: with a full history and the tests the
/// doctor asks for, an experienced, rested doctor should usually get it right. Catches new illnesses
/// whose symptoms and tests are too like another's to ever be told apart.
/// </summary>
public class DiagnosisCoverageTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_condition_is_usually_diagnosed_correctly_after_a_full_work_up()
    {
        var db = GameDatabase.Load(new FileSystemDataSource(TestData.GameDataDirectory()));
        var engine = new DiagnosisEngine(db);
        var random = new GameRandom(1234);
        var generator = new PatientGenerator(db, random);
        var now = new DateTime(2026, 1, 5, 10, 0, 0);
        var failures = new List<string>();

        foreach (var condition in db.Conditions.Values.Where(c => !c.IsMaternity).OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            const int patients = 80;
            var correct = 0;
            for (var i = 0; i < patients; i++)
            {
                var patient = generator.Generate(now, conditionId: condition.Id);
                patient.HistoryTaken = true;
                for (var round = 0; round < 6; round++)
                {
                    var decision = engine.Decide(patient, skill: 1, fatigue: 0, workload: 1, random);
                    if (decision is DiagnosticDecision.Diagnose diagnose)
                    {
                        correct += diagnose.Condition.Id == condition.Id ? 1 : 0;
                        break;
                    }

                    if (decision is DiagnosticDecision.OrderTests order)
                    {
                        foreach (var id in order.InvestigationIds)
                        {
                            var test = new InvestigationOrder { InvestigationId = id, RequestedAt = now, Status = InvestigationStatus.Resulted };
                            InvestigationSystem.GenerateResult(db.Investigations[id], patient, test, random);
                            patient.Investigations.Add(test);
                        }
                    }
                }
            }

            var accuracy = correct / (double)patients;
            output.WriteLine($"{condition.Id,-32} {accuracy:P0}");
            if (accuracy < 0.6)
            {
                failures.Add($"{condition.Id} {accuracy:P0}");
            }
        }

        Assert.True(failures.Count == 0, "Hard to diagnose: " + string.Join(", ", failures));
    }
}
