using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Medical.Results;
using BlueprintHospital.Simulation.Patients;
using Xunit;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>
/// Every test result opens as a readable document that agrees with its one-line summary: lab sheets flag the
/// values that are out of range (and only for abnormal results), and imaging shows a picture of the finding.
/// </summary>
public class ResultDocumentTests
{
    private static readonly GameDatabase Db = GameDatabase.Load(new FileSystemDataSource(TestData.GameDataDirectory()));
    private static readonly DateTime Now = new(2026, 3, 2, 10, 0, 0);

    private static IEnumerable<(InvestigationDefinition Test, ResultTemplate Template, bool Abnormal, string? Condition, string Kind)> Variants()
    {
        foreach (var test in Db.Investigations.Values.OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            yield return (test, test.Normal, false, null, "normal");
            if (test.FalsePositive is not null)
            {
                yield return (test, test.FalsePositive, true, null, "false positive");
            }

            foreach (var a in test.Abnormal)
            {
                yield return (test, a, true, a.Condition, a.Condition);
            }
        }
    }

    private static Patient PatientFor(InvestigationDefinition test, string? conditionId, Sex? sex, int seed)
    {
        var generator = new PatientGenerator(Db, new GameRandom(seed));
        for (var i = 0; i < 400; i++)
        {
            var id = conditionId ?? Db.Conditions.Values
                .Where(c => !c.IsMaternity && !c.IsPlanned && test.Abnormal.All(a => a.Condition != c.Id))
                .OrderBy(c => c.Id, StringComparer.Ordinal)
                .ElementAt((seed + i) % 5).Id;
            var p = generator.Generate(Now, conditionId: id);
            if ((sex is null || p.Sex == sex) && InvestigationSystem.AppliesTo(test, p))
            {
                return p;
            }
        }

        throw new InvalidOperationException($"No patient for {test.Id} / {conditionId} / {sex}.");
    }

    private static InvestigationOrder Resulted(InvestigationDefinition test, ResultTemplate template, int summary, bool abnormal, double? value)
    {
        var text = template.Summaries[summary];
        if (value is { } v)
        {
            text = text.Replace("{value}", InvestigationSystem.FormatValue(v), StringComparison.Ordinal);
        }

        return new InvestigationOrder
        {
            InvestigationId = test.Id,
            RequestedAt = Now,
            Status = InvestigationStatus.Resulted,
            ResultedAt = Now.AddHours(1),
            Abnormal = abnormal,
            Value = value,
            Summary = text,
        };
    }

    [Fact]
    public void Every_result_of_every_test_has_a_readable_document()
    {
        var problems = new List<string>();
        var seed = 0;
        foreach (var (test, template, abnormal, condition, kind) in Variants())
        {
            foreach (var sex in new[] { Sex.Female, Sex.Male })
            {
                if ((test.AppliesToSex == SexRestriction.Female && sex == Sex.Male) || (test.AppliesToSex == SexRestriction.Male && sex == Sex.Female))
                {
                    continue;
                }

                Patient patient;
                try
                {
                    patient = PatientFor(test, condition, condition is null ? sex : null, ++seed);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                for (var s = 0; s < template.Summaries.Count; s++)
                {
                    foreach (var pick in new[] { 0.0, 0.37, 0.5, 0.83, 1.0 })
                    {
                        double? value = template.ValueRange is { } range ? range[0] + ((range[1] - range[0]) * pick) : null;
                        var order = Resulted(test, template, s, abnormal, value);
                        var doc = ResultDocument.Build(test, patient, order);
                        var where = $"{test.Id} [{kind}] summary {s} value {value} ({patient.Sex})";
                        if (doc is null)
                        {
                            problems.Add($"{where}: no document");
                            continue;
                        }

                        if (doc.Rows.Count == 0 && doc.PictureId is null)
                        {
                            problems.Add($"{where}: neither values nor a picture");
                        }

                        if (test.Category == InvestigationCategory.Imaging && (doc.PictureId is null || doc.Findings is null))
                        {
                            problems.Add($"{where}: imaging without a picture and findings");
                        }

                        if (doc.Rows.Count > 0)
                        {
                            var flagged = doc.Rows.Where(r => r.IsAbnormal).Select(r => $"{r.Name} {r.Value}").ToList();
                            if (!abnormal && flagged.Count > 0)
                            {
                                problems.Add($"{where}: normal result but flags {string.Join(", ", flagged)}");
                            }

                            if (abnormal && flagged.Count == 0)
                            {
                                problems.Add($"{where}: abnormal result but nothing is flagged");
                            }

                            foreach (var a in test.Analytes.Where(a => !a.IsText))
                            {
                                if (ResultDocument.FindInSummary(a, order.Summary) is { } stated)
                                {
                                    var row = doc.Rows.First(r => r.Id == a.Id);
                                    if (Math.Abs(row.Numeric!.Value - stated) > 0.051)
                                    {
                                        problems.Add($"{where}: summary says {a.Name} {stated} but the sheet shows {row.Value}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct().Take(60)));
    }

    [Fact]
    public void Every_picture_can_be_drawn()
    {
        foreach (var id in ResultPictures.Ids)
        {
            if (ResultPictures.KindOf(id) == PictureKind.Image)
            {
                var picture = ResultPictures.Render(id, 42);
                Assert.Equal(picture.Width * picture.Height * 4, picture.Rgba.Length);
                Assert.False(string.IsNullOrWhiteSpace(picture.Title));
                Assert.True(picture.Rgba.Where((b, i) => i % 4 == 0).Distinct().Count() > 20, $"{id} looks blank");
            }
            else
            {
                var trace = Waveforms.Build(id, 42);
                Assert.NotEmpty(trace.Channels);
                Assert.All(trace.Channels, c => Assert.Equal(trace.SampleRate * 10, c.Samples.Length));
                Assert.False(string.IsNullOrWhiteSpace(trace.Statement));
            }
        }
    }

    [Fact]
    public void Abnormal_pictures_mark_the_finding_and_normal_ones_do_not()
    {
        foreach (var id in ResultPictures.Ids.Where(i => ResultPictures.KindOf(i) == PictureKind.Image && !i.StartsWith("preg_", StringComparison.Ordinal)))
        {
            var p = ResultPictures.Render(id, 3);
            if (id.EndsWith("_normal", StringComparison.Ordinal))
            {
                Assert.True(p.Markers.Count == 0, $"{id} should have no finding marker");
            }
            else
            {
                Assert.True(p.Markers.Count > 0, $"{id} should mark its finding");
            }
        }
    }

    [Fact]
    public void A_fracture_X_ray_differs_from_a_normal_one()
    {
        var normal = ResultPictures.Render("ankle_normal", 5);
        var broken = ResultPictures.Render("ankle_weber_b", 5);
        var differing = normal.Rgba.Zip(broken.Rgba).Count(p => Math.Abs(p.First - p.Second) > 40);
        Assert.True(differing > 100, $"only {differing} bytes differ");
    }

    [Fact]
    public void The_document_is_the_same_every_time_it_is_opened()
    {
        var test = Db.Investigations["full_blood_count"];
        var patient = PatientFor(test, "community_acquired_pneumonia", null, 9);
        var order = Resulted(test, test.Abnormal.First(a => a.Condition == "community_acquired_pneumonia"), 0, true, 18.2);
        var a = ResultDocument.Build(test, patient, order)!;
        var b = ResultDocument.Build(test, patient, order)!;
        Assert.Equal(a.Rows.Select(r => r.Value), b.Rows.Select(r => r.Value));
        Assert.Equal("18.2", a.Rows.First(r => r.Id == "wcc").Value);
        Assert.Equal("H", a.Rows.First(r => r.Id == "neut").Flag);
    }

    [Fact]
    public void Values_written_in_a_summary_are_read_onto_the_sheet()
    {
        var ue = Db.Investigations["urea_electrolytes"];
        var k = ue.Analytes.First(a => a.Id == "k");
        Assert.Equal(5.6, ResultDocument.FindInSummary(k, "Creatinine 312 µmol/L (raised; baseline 85 last year). Potassium 5.6."));
        var creat = ue.Analytes.First(a => a.Id == "creat");
        Assert.Equal(131, ResultDocument.FindInSummary(creat, "Creatinine mildly raised at 131 µmol/L."));
        var lp = Db.Investigations["lumbar_puncture"].Analytes.First(a => a.Id == "lp_wcc");
        Assert.Equal(2400, ResultDocument.FindInSummary(lp, "Cloudy fluid, white cells 2,400 (mostly neutrophils), glucose low."));
    }

    [Theory]
    [InlineData("fall_older_person", "hip_normal")]
    [InlineData("gout", "knee_normal")]
    [InlineData("cellulitis", "tibia_normal")]
    [InlineData("ankle_sprain", "ankle_normal")]
    [InlineData("wrist_fracture", "wrist_normal")]
    [InlineData("shoulder_dislocation", "shoulder_normal")]
    public void A_normal_X_ray_shows_the_part_that_hurts(string condition, string picture)
    {
        var test = Db.Investigations["limb_xray"];
        var patient = new PatientGenerator(Db, new GameRandom(4)).Generate(Now, conditionId: condition);
        var order = Resulted(test, test.Normal, 0, false, null);
        Assert.Equal(picture, ResultDocument.Build(test, patient, order)!.PictureId);
    }

    [Fact]
    public void An_unknown_illness_or_symptom_for_a_normal_picture_is_a_data_error()
    {
        var test = new InvestigationDefinition
        {
            Id = "x",
            Name = "X",
            ProcedureMinutes = 5,
            Normal = new ResultTemplate { Summaries = new[] { "Fine." } },
            NormalPictures = new Dictionary<string, string> { ["no_such_thing"] = "no_such_picture" },
        };
        var errors = new List<DataValidationError>();
        test.Validate(new ValidationContext("Investigations/x.json", "x", errors));
        Assert.Contains(errors, e => e.Field == "normal_pictures");
    }

    [Fact]
    public void Unfinished_tests_have_no_document()
    {
        var test = Db.Investigations["ct_head"];
        var patient = PatientFor(test, null, null, 1);
        var order = new InvestigationOrder { InvestigationId = test.Id, RequestedAt = Now };
        Assert.Null(ResultDocument.Build(test, patient, order));
    }

    [Fact]
    public void An_unknown_picture_id_is_a_data_error()
    {
        var errors = new List<DataValidationError>();
        var context = new ValidationContext("Investigations/x.json", "x", errors);
        var test = new InvestigationDefinition
        {
            Id = "x",
            Name = "X",
            ProcedureMinutes = 5,
            Normal = new ResultTemplate { Summaries = new[] { "Fine." }, Pictures = new[] { "no_such_picture" } },
        };
        test.Validate(context);
        Assert.Contains(errors, e => e.Field == "normal.pictures");
    }
}
