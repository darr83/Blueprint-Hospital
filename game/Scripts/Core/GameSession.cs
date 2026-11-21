using System;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Core;

/// <summary>One game in progress: its clock, event bus, hospital site and the simulation running on them.</summary>
public sealed class GameSession
{
    private GameSession(GameDatabase database, string hospitalName, SaveGame? save, StartingHospital start = StartingHospital.SmallEmergency, bool unlimitedMoney = false, WardEmergencyFrequency wardEmergencies = WardEmergencyFrequency.Realistic, GameType type = GameType.Sandbox, SitePlan? plan = null, SeasonChoice? seasons = null)
    {
        Database = database;
        HospitalName = hospitalName;
        Events = new EventBus();
        var seasonChoice = seasons ?? SeasonChoice.Default;
        Clock = new SimulationClock(Events, SimulationClock.StartIn(seasonChoice.StartMonth));
        Site = StarterSite.Create(save?.SitePlan ?? plan ?? SitePlan.Standard);

        if (save is not null)
        {
            Clock.RestoreState(save.Clock);
            InitialCamera = save.Camera;
        }

        // Each clock tick advances the hospital by one game second.
        Simulation = new HospitalSimulation(database, Clock, Events, Site, new SimulationOptions { Seed = Environment.TickCount, Start = start, UnlimitedMoney = unlimitedMoney, WardEmergencies = wardEmergencies, Type = type, Seasons = seasonChoice.Seasons });
        if (save?.Layout is { } layout)
        {
            Simulation.RestoreLayout(layout);
        }

        if (save?.People is { } people)
        {
            Simulation.RestorePeople(people);
        }

        Clock.Ticked += _ => Simulation.Tick();
    }

    public GameDatabase Database { get; }

    public string HospitalName { get; }

    public EventBus Events { get; }

    public SimulationClock Clock { get; }

    public SiteMap Site { get; }

    /// <summary>Patients, staff and everything else happening in the hospital.</summary>
    public HospitalSimulation Simulation { get; }

    /// <summary>Camera to restore when the hospital scene opens, if the game was loaded.</summary>
    public CameraState? InitialCamera { get; }

    public static GameSession CreateNew(GameDatabase database, string hospitalName, StartingHospital start = StartingHospital.SmallEmergency, bool unlimitedMoney = false, WardEmergencyFrequency wardEmergencies = WardEmergencyFrequency.Realistic, GameType type = GameType.Sandbox, SitePlan? plan = null, SeasonChoice? seasons = null) =>
        new(database, string.IsNullOrWhiteSpace(hospitalName) ? "Blueprint General Hospital" : hospitalName.Trim(), null, start, unlimitedMoney, wardEmergencies, type, plan, seasons);

    public static GameSession FromSave(GameDatabase database, SaveGame save) => new(database, save.HospitalName, save);

    public SaveGame CreateSave(CameraState camera) => new()
    {
        HospitalName = HospitalName,
        SavedAtUtc = DateTime.UtcNow,
        Clock = Clock.CaptureState(),
        Camera = camera,
        SitePlan = Site.Layout?.Plan,
        Layout = Simulation.CaptureLayout(),
        People = Simulation.CapturePeople(),
    };
}

/// <summary>The New Game choices about the year: whether seasons change who comes in, and the month the game starts.</summary>
public sealed record SeasonChoice(bool Seasons, int StartMonth)
{
    /// <summary>Seasons on, starting in April, so winter is something to build up to.</summary>
    public static readonly SeasonChoice Default = new(true, 4);
}
