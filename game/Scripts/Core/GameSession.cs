using System;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Core;

/// <summary>One game in progress: its clock, event bus and hospital site.</summary>
public sealed class GameSession
{
    private GameSession(GameDatabase database, string hospitalName, SaveGame? save)
    {
        Database = database;
        HospitalName = hospitalName;
        Events = new EventBus();
        Clock = new SimulationClock(Events);
        Site = StarterSite.Create();

        if (save is not null)
        {
            Clock.RestoreState(save.Clock);
            InitialCamera = save.Camera;
        }
    }

    public GameDatabase Database { get; }

    public string HospitalName { get; }

    public EventBus Events { get; }

    public SimulationClock Clock { get; }

    public SiteMap Site { get; }

    /// <summary>Camera to restore when the hospital scene opens, if the game was loaded.</summary>
    public CameraState? InitialCamera { get; }

    public static GameSession CreateNew(GameDatabase database, string hospitalName) =>
        new(database, string.IsNullOrWhiteSpace(hospitalName) ? "Blueprint General Hospital" : hospitalName.Trim(), null);

    public static GameSession FromSave(GameDatabase database, SaveGame save) => new(database, save.HospitalName, save);

    public SaveGame CreateSave(CameraState camera) => new()
    {
        HospitalName = HospitalName,
        SavedAtUtc = DateTime.UtcNow,
        Clock = Clock.CaptureState(),
        Camera = camera,
    };
}
