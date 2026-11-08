using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Data.Models;

namespace BlueprintHospital.Simulation.Tests;

public class MedicalDataLoadingTests
{
    private const string ValidDepartment = """
        { "id": "emergency", "name": "A and E", "short_name": "A&E", "description": "", "colour": "#D84343", "starts_unlocked": true, "opening_cost": 0 }
        """;

    [Fact]
    public void Shipped_game_data_loads_without_errors()
    {
        var db = GameDatabase.Load(new FileSystemDataSource(TestData.GameDataDirectory()));

        Assert.Equal(5, db.Departments.Count);
        Assert.Equal("emergency", db.StartingDepartment.Id);
        foreach (var id in new[] { "cardiology", "gynaecology", "neurology", "gastroenterology" })
        {
            Assert.Contains(id, db.Departments.Keys);
        }

        Assert.True(db.Symptoms.Count >= 20);
        Assert.Equal(BodySystem.Cardiovascular, db.Symptoms["chest_pain"].BodySystem);
        Assert.True(db.Symptoms["chest_pain"].IsRedFlag);
    }

    [Fact]
    public void Records_can_be_split_across_files()
    {
        var source = new InMemoryDataSource()
            .Add("Departments/a.json", $"[{ValidDepartment}]")
            .Add("Symptoms/a.json", """[{ "id": "fever", "name": "Fever", "patient_description": "Hot" }]""")
            .Add("Symptoms/b.json", """[{ "id": "cough", "name": "Cough", "patient_description": "Coughing", "body_system": "respiratory" }]""");

        var db = GameDatabase.Load(source);

        Assert.Equal(2, db.Symptoms.Count);
        Assert.Equal(BodySystem.Respiratory, db.Symptoms["cough"].BodySystem);
    }

    [Fact]
    public void Missing_field_error_names_file_record_and_field()
    {
        var source = new InMemoryDataSource()
            .Add("Departments/a.json", $"[{ValidDepartment}]")
            .Add("Symptoms/bad.json", """[{ "id": "fever", "name": "", "patient_description": "Hot" }]""");

        var ex = Assert.Throws<DataLoadException>(() => GameDatabase.Load(source));

        var error = Assert.Single(ex.Errors);
        Assert.Equal("Symptoms/bad.json", error.File);
        Assert.Equal("fever", error.RecordId);
        Assert.Equal("name", error.Field);
        Assert.Contains("Symptoms/bad.json", ex.Message);
    }

    [Fact]
    public void Misspelt_field_is_rejected()
    {
        var source = new InMemoryDataSource()
            .Add("Departments/a.json", $"[{ValidDepartment}]")
            .Add("Symptoms/a.json", """[{ "id": "fever", "name": "Fever", "patient_descripton": "Hot" }]""");

        var ex = Assert.Throws<DataLoadException>(() => GameDatabase.Load(source));

        Assert.Contains(ex.Errors, e => e.File == "Symptoms/a.json" && e.RecordId == "fever");
    }

    [Fact]
    public void Unknown_enum_value_is_rejected_with_field()
    {
        var source = new InMemoryDataSource()
            .Add("Departments/a.json", $"[{ValidDepartment}]")
            .Add("Symptoms/a.json", """[{ "id": "fever", "name": "Fever", "patient_description": "Hot", "body_system": "kidneys" }]""");

        var ex = Assert.Throws<DataLoadException>(() => GameDatabase.Load(source));

        var error = Assert.Single(ex.Errors);
        Assert.Equal("body_system", error.Field);
    }

    [Fact]
    public void Duplicate_ids_across_files_are_rejected()
    {
        var source = new InMemoryDataSource()
            .Add("Departments/a.json", $"[{ValidDepartment}]")
            .Add("Symptoms/a.json", """[{ "id": "fever", "name": "Fever", "patient_description": "Hot" }]""")
            .Add("Symptoms/b.json", """[{ "id": "fever", "name": "Fever again", "patient_description": "Hot" }]""");

        var ex = Assert.Throws<DataLoadException>(() => GameDatabase.Load(source));

        var error = Assert.Single(ex.Errors);
        Assert.Equal("Symptoms/b.json", error.File);
        Assert.Contains("Symptoms/a.json", error.Message);
    }

    [Theory]
    [InlineData("Chest Pain")]
    [InlineData("1st")]
    [InlineData("chest-pain")]
    public void Bad_ids_are_rejected(string id)
    {
        var source = new InMemoryDataSource()
            .Add("Departments/a.json", $"[{ValidDepartment}]")
            .Add("Symptoms/a.json", $$"""[{ "id": "{{id}}", "name": "X", "patient_description": "Y" }]""");

        var ex = Assert.Throws<DataLoadException>(() => GameDatabase.Load(source));

        Assert.Contains(ex.Errors, e => e.Field == "id");
    }

    [Fact]
    public void Broken_json_reports_the_file()
    {
        var source = new InMemoryDataSource()
            .Add("Departments/a.json", $"[{ValidDepartment}]")
            .Add("Symptoms/a.json", "[{ \"id\": ");

        var ex = Assert.Throws<DataLoadException>(() => GameDatabase.Load(source));

        Assert.Contains(ex.Errors, e => e.File == "Symptoms/a.json" && e.Field == "(file)");
    }

    [Fact]
    public void Exactly_one_starting_department_is_required()
    {
        var source = new InMemoryDataSource()
            .Add("Departments/a.json", """[{ "id": "cardiology", "name": "Cardiology", "short_name": "CARD", "colour": "#C2185B" }]""");

        var ex = Assert.Throws<DataLoadException>(() => GameDatabase.Load(source));

        Assert.Contains(ex.Errors, e => e.Field == "starts_unlocked");
    }

    [Fact]
    public void All_errors_are_reported_together()
    {
        var source = new InMemoryDataSource()
            .Add("Departments/a.json", """[{ "id": "emergency", "name": "", "short_name": "", "colour": "red", "starts_unlocked": true }]""");

        var ex = Assert.Throws<DataLoadException>(() => GameDatabase.Load(source));

        Assert.Equal(3, ex.Errors.Count);
    }
}
