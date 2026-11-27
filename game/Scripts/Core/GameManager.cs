using System;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Autoloaded singleton that owns the long-lived game services: data, settings and the
/// current session. Scenes reach it through <see cref="Instance"/>.
/// </summary>
public partial class GameManager : Node
{
    public const string MainMenuScene = "res://Scenes/Main/MainMenu.tscn";
    public const string HospitalScene = "res://Scenes/Hospital/Hospital.tscn";

    public static GameManager Instance { get; private set; } = null!;

    /// <summary>All validated game data, or null if loading failed (see <see cref="DataLoadError"/>).</summary>
    public GameDatabase? Database { get; private set; }

    public string? DataLoadError { get; private set; }

    public GameSettings Settings { get; private set; } = new();

    /// <summary>The game currently being played. Null while on the main menu.</summary>
    public GameSession? Session { get; private set; }

    /// <summary>True when launched with "-- --smoke-test": runs the automated launch check and quits.</summary>
    public bool IsSmokeTest { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        IsSmokeTest = Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke-test") >= 0;

        Settings = GameSettings.Load();
        Settings.Apply(GetTree());

        // Smooth (Linear) texture filtering for every 2D and HUD texture. Set here as well as in project.godot,
        // because the Godot editor drops that line on a re-save (Linear is its default value).
        GetTree().Root.CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Linear;

        try
        {
            Database = GameDatabase.Load(new GodotDataSource());
            GD.Print($"Loaded game data: {Database.Departments.Count} departments, {Database.Symptoms.Count} symptoms, {Database.Conditions.Count} conditions, {Database.Investigations.Count} investigations, {Database.Medications.Count} medications.");
        }
        catch (DataLoadException ex)
        {
            DataLoadError = ex.Message;
            GD.PushError(ex.Message);
        }
    }

    public void StartNewGame(string hospitalName, Simulation.Simulation.StartingHospital start = Simulation.Simulation.StartingHospital.SmallEmergency, bool unlimitedMoney = false, Simulation.Simulation.WardEmergencyFrequency wardEmergencies = Simulation.Simulation.WardEmergencyFrequency.Realistic, Simulation.Simulation.GameType type = Simulation.Simulation.GameType.Sandbox, Simulation.World.SitePlan? plan = null, bool chooseSite = false, SeasonChoice? seasons = null)
    {
        if (Database is null)
        {
            return;
        }

        Session = GameSession.CreateNew(Database, hospitalName, start, unlimitedMoney, wardEmergencies, type, plan, seasons);
        SitePlanning = chooseSite ? new NewGameChoices(hospitalName, start, unlimitedMoney, wardEmergencies, type, seasons ?? SeasonChoice.Default) : null;
        SitePlanStep = Hospital.SitePlanStep.MainEntrance;
        ChangeScene(HospitalScene);
    }

    /// <summary>
    /// Starts the guided tutorial: an empty building on the usual site, money that can't run out, no emergencies,
    /// seasons or major incidents, and only green and amber adults walking in once A&amp;E opens.
    /// </summary>
    public void StartTutorial() =>
        StartNewGame("Tutorial Hospital", Simulation.Simulation.StartingHospital.EmptyBuilding, unlimitedMoney: true, Simulation.Simulation.WardEmergencyFrequency.Off,
            Simulation.Simulation.GameType.Tutorial, seasons: new SeasonChoice(false, SeasonChoice.Default.StartMonth, Simulation.Simulation.MajorIncidentFrequency.Off));

    /// <summary>
    /// While a new game's site plan is being chosen: what the player picked on the New Game panel. The hospital
    /// is built again for each choice of entrance, so the picture always shows the real layout.
    /// </summary>
    public NewGameChoices? SitePlanning { get; private set; }

    /// <summary>Which entrance the site plan is placing next.</summary>
    public Hospital.SitePlanStep SitePlanStep { get; set; }

    /// <summary>Builds the new hospital again around <paramref name="plan"/>, staying in the site plan.</summary>
    public void Replan(Simulation.World.SitePlan plan, Hospital.SitePlanStep next)
    {
        if (Database is null || SitePlanning is not { } c)
        {
            return;
        }

        Session = GameSession.CreateNew(Database, c.HospitalName, c.Start, c.UnlimitedMoney, c.WardEmergencies, c.Type, plan, c.Seasons);
        SitePlanStep = next;
        ChangeScene(HospitalScene);
    }

    /// <summary>The site plan is done: the game starts.</summary>
    public void FinishSitePlan() => SitePlanning = null;

    /// <summary>Creates a new session without changing scene (used when the hospital scene is run directly).</summary>
    public void StartNewGameInPlace()
    {
        if (Database is not null)
        {
            Session = GameSession.CreateNew(Database, "Blueprint General Hospital");
        }
    }

    public void LoadGame(string slotName)
    {
        SitePlanning = null;
        if (Database is null)
        {
            return;
        }

        var save = SaveManager.Read(slotName);
        Session = GameSession.FromSave(Database, save);
        ChangeScene(HospitalScene);
    }

    /// <summary>Where the ready-built example hospital ships, beside the game data.</summary>
    public const string ExampleHospitalPath = "res://Data/Examples/" + Simulation.Simulation.ExampleHospital.FileName;

    /// <summary>Loads the example hospital: three floors, every department, fully staffed.</summary>
    public void LoadExampleHospital()
    {
        SitePlanning = null;
        if (Database is null)
        {
            return;
        }

        using var file = FileAccess.Open(ExampleHospitalPath, FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Could not open {ExampleHospitalPath}: {FileAccess.GetOpenError()}");
        var save = SaveSerializer.Deserialize(file.GetAsText());
        Session = GameSession.FromSave(Database, save);
        ChangeScene(HospitalScene);
    }

    public void SaveGame(string displayName, CameraState camera)
    {
        if (Session is null)
        {
            throw new InvalidOperationException("There is no game in progress to save.");
        }

        SaveManager.Write(SaveSerializer.ToSlotName(displayName), Session.CreateSave(camera));
    }

    public void ReturnToMainMenu()
    {
        Session = null;
        SitePlanning = null;
        ChangeScene(MainMenuScene);
    }

    private void ChangeScene(string path)
    {
        GetTree().Paused = false;
        var error = GetTree().ChangeSceneToFile(path);
        if (error != Error.Ok)
        {
            GD.PushError($"Could not open scene {path}: {error}");
        }
    }

    public void SaveSettings()
    {
        var error = Settings.Save();
        if (error != Error.Ok)
        {
            GD.PushWarning($"Could not save settings: {error}");
        }

        Settings.Apply(GetTree());
    }
}

/// <summary>The New Game panel's choices, kept while the player plans the site.</summary>
public sealed record NewGameChoices(string HospitalName, Simulation.Simulation.StartingHospital Start, bool UnlimitedMoney, Simulation.Simulation.WardEmergencyFrequency WardEmergencies, Simulation.Simulation.GameType Type, SeasonChoice Seasons);
