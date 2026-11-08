using System;
using BlueprintHospital.Core;
using BlueprintHospital.Simulation.Core;

namespace BlueprintHospital.UI;

/// <summary>In-game menu. The simulation is paused while it is open.</summary>
public partial class PauseMenu : ModalPanel
{
    private Func<CameraState> _captureCamera = () => new CameraState(0, 0, 1);

    public static PauseMenu Create(Func<CameraState> captureCamera, string hospitalName)
    {
        var menu = new PauseMenu { _captureCamera = captureCamera };
        menu.Build(hospitalName);
        return menu;
    }

    private void Build(string hospitalName)
    {
        Content.AddChild(UiStyle.Heading("Paused"));
        Content.AddChild(UiStyle.Button("Resume", Close));
        Content.AddChild(UiStyle.SecondaryButton("Save Game", () => AddChild(SaveGamePanel.Create(_captureCamera, hospitalName))));
        Content.AddChild(UiStyle.SecondaryButton("Load Game", () => AddChild(new LoadGamePanel())));
        Content.AddChild(UiStyle.SecondaryButton("Settings", () => AddChild(new SettingsPanel())));
        Content.AddChild(UiStyle.SecondaryButton("Exit to Main Menu", () =>
        {
            GetTree().Paused = false;
            GameManager.Instance.ReturnToMainMenu();
        }));
        Content.AddChild(UiStyle.SecondaryButton("Quit to Desktop", () => GetTree().Quit()));
    }

    public override void _Ready()
    {
        GetTree().Paused = true;
        Closed += () => GetTree().Paused = false;
    }
}
