using System;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// Lets the player rename a person from their panel: a pencil button swaps the name for a text
/// box; Enter (or clicking away) saves it and Esc cancels.
/// </summary>
public static class NameEditing
{
    /// <summary>
    /// Adds a text box beside <paramref name="label"/> (in <paramref name="parent"/>, straight after it)
    /// and returns the Rename button to put in the panel's header.
    /// </summary>
    public static Button Attach(VBoxContainer parent, Label label, Func<string?> current, Action<string> apply)
    {
        var edit = new LineEdit { Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Type a new name and press Enter (Esc cancels)" };
        edit.AddThemeFontSizeOverride("font_size", 18);
        parent.AddChild(edit);
        parent.MoveChild(edit, label.GetIndex() + 1);

        // A small square pencil button, the same size and style as the close button beside it.
        var button = UiStyle.CloseButton(() => { });
        button.Text = string.Empty;
        button.TooltipText = "Rename";
        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.AddChild(new PanelIcon(PanelIcon.Kind.Pencil, 16) { Colour = UiStyle.TextMuted });
        button.AddChild(centre);
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        void Close(bool save)
        {
            if (!edit.Visible)
            {
                return;
            }

            if (save && !string.IsNullOrWhiteSpace(edit.Text) && edit.Text.Trim() != current())
            {
                apply(edit.Text.Trim());
            }

            edit.Visible = false;
            label.Visible = true;
        }

        button.Pressed += () =>
        {
            if (current() is not { } name)
            {
                return;
            }

            edit.Text = name;
            label.Visible = false;
            edit.Visible = true;
            edit.GrabFocus();
            edit.SelectAll();
        };
        edit.TextSubmitted += _ => Close(save: true);
        edit.FocusExited += () => Close(save: true);
        edit.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                Close(save: false);
                edit.AcceptEvent();
            }
        };
        return button;
    }
}
