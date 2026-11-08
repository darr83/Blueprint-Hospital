using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// A small command line for cheats, opened and closed with Ctrl+C. "money 500000" adds that much to
/// the balance; the money shows as a Cheats line in Finances. Esc also closes it.
/// </summary>
public partial class CheatConsole : PanelContainer
{
    private const int MaxLogLines = 8;

    private readonly LineEdit _input = new()
    {
        PlaceholderText = "Type a command, e.g. money 500000",
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        ClearButtonEnabled = true,
        CaretBlink = true,
    };

    private readonly RichTextLabel _log = new()
    {
        BbcodeEnabled = true,
        FitContent = true,
        ScrollActive = false,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };

    private readonly System.Collections.Generic.List<string> _lines = new();
    private readonly System.Collections.Generic.List<string> _history = new();
    private int _historyIndex;
    private HospitalSimulation _sim = null!;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
        AddThemeStyleboxOverride("panel", UiStyle.Card(16));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        var header = new HBoxContainer();
        var title = new Label { Text = "Cheat console", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeColorOverride("font_color", UiStyle.TextDark);
        header.AddChild(title);
        var hint = new Label { Text = "Ctrl+C or Esc to close", VerticalAlignment = VerticalAlignment.Center };
        hint.AddThemeFontSizeOverride("font_size", 13);
        hint.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        header.AddChild(hint);
        column.AddChild(header);

        _log.AddThemeColorOverride("default_color", UiStyle.TextDark);
        _log.AddThemeFontSizeOverride("normal_font_size", 14);
        _log.AddThemeFontSizeOverride("bold_font_size", 14);
        column.AddChild(_log);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var prompt = new Label { Text = ">", VerticalAlignment = VerticalAlignment.Center };
        prompt.AddThemeColorOverride("font_color", UiStyle.Blue);
        prompt.AddThemeFontSizeOverride("font_size", 18);
        row.AddChild(prompt);
        _input.TextSubmitted += Submit;
        _input.GuiInput += OnInputKey;
        row.AddChild(_input);
        var run = new Button { Text = "Run", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(64, 0) };
        run.Pressed += () => Submit(_input.Text);
        row.AddChild(run);
        column.AddChild(row);

        Write("[color=#5B6B79]Type help for the list of commands.[/color]");
    }

    /// <summary>The last line the console printed, for tests.</summary>
    public string LastLine => _lines.Count == 0 ? string.Empty : _lines[^1];

    public void Toggle()
    {
        if (Visible)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    public void Open()
    {
        Visible = true;
        _input.Clear();
        _input.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close()
    {
        _input.ReleaseFocus();
        Visible = false;
    }

    /// <summary>Runs a command as if it had been typed and Enter pressed.</summary>
    public CheatResult Run(string command)
    {
        var result = _sim.RunCheat(command);
        Write($"[color=#5B6B79]> {Escape(command.Trim())}[/color]");
        var colour = result.Succeeded ? ClinicalText.GreenHex : ClinicalText.RedHex;
        Write($"[color={colour}]{Escape(result.Message)}[/color]");
        return result;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        if (key.Keycode == Key.C && key.CtrlPressed && !key.AltPressed && !key.ShiftPressed)
        {
            // Leave Ctrl+C alone in other text boxes (copy), unless the console is already open.
            var focus = GetViewport().GuiGetFocusOwner();
            if (Visible || focus is not (LineEdit or TextEdit))
            {
                Toggle();
                GetViewport().SetInputAsHandled();
            }
        }
        else if (Visible && key.Keycode == Key.Escape)
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Submit(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Run(text);
        _history.Add(text.Trim());
        _historyIndex = _history.Count;
        _input.Clear();
        _input.GrabFocus();
    }

    /// <summary>Up and Down step through earlier commands.</summary>
    private void OnInputKey(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key || _history.Count == 0)
        {
            return;
        }

        if (key.Keycode == Key.Up)
        {
            _historyIndex = System.Math.Max(0, _historyIndex - 1);
        }
        else if (key.Keycode == Key.Down)
        {
            _historyIndex = System.Math.Min(_history.Count, _historyIndex + 1);
        }
        else
        {
            return;
        }

        _input.Text = _historyIndex < _history.Count ? _history[_historyIndex] : string.Empty;
        _input.CaretColumn = _input.Text.Length;
        _input.AcceptEvent();
    }

    private void Write(string line)
    {
        _lines.Add(line);
        if (_lines.Count > MaxLogLines)
        {
            _lines.RemoveAt(0);
        }

        _log.Text = string.Join("\n", _lines);
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");
}
