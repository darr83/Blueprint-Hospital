using System;
using BlueprintHospital.Core;
using BlueprintHospital.Simulation.Core;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>Asks for a save name and writes the current game.</summary>
public partial class SaveGamePanel : ModalPanel
{
    private Func<CameraState> _captureCamera = () => new CameraState(0, 0, 1);

    /// <summary>Creates the panel. <paramref name="captureCamera"/> supplies the current view when saving.</summary>
    public static SaveGamePanel Create(Func<CameraState> captureCamera, string defaultName)
    {
        var panel = new SaveGamePanel { _captureCamera = captureCamera };
        panel.Build(defaultName);
        return panel;
    }

    private void Build(string defaultName)
    {
        Content.AddChild(UiStyle.Heading("Save Game"));
        Content.AddChild(UiStyle.Body("Save name"));
        var name = new LineEdit { Text = defaultName, CustomMinimumSize = new Vector2(380, 0), MaxLength = 48 };
        Content.AddChild(name);
        var status = UiStyle.Body(string.Empty, muted: true);
        Content.AddChild(status);

        void DoSave()
        {
            try
            {
                GameManager.Instance.SaveGame(name.Text, _captureCamera());
                Close();
            }
            catch (Exception ex)
            {
                status.Text = $"Could not save: {ex.Message}";
            }
        }

        name.TextSubmitted += _ => DoSave();

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        buttons.AddChild(UiStyle.Button("Cancel", Close, 120));
        buttons.AddChild(UiStyle.Button("Save", DoSave, 120));
        Content.AddChild(buttons);

        Ready += () =>
        {
            name.GrabFocus();
            name.SelectAll();
        };
    }
}
