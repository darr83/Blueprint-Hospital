using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Core;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// Everything on screen while you are being a doctor: who you are along the top, the Be A Doctor
/// panel on the right (your tasks, or the patient you are with), the Hint card and the key prompts.
/// </summary>
public partial class DoctorHud : CanvasLayer
{
    public const int PanelWidth = 460;
    private const int Gap = 16;
    private const int TopBarHeight = 64;

    private static readonly string[] Tabs = { "History", "Tests", "Diagnosis", "Treatment", "Notes" };

    private GameSession _session = null!;
    private HospitalSimulation _sim = null!;
    private Control _root = null!;

    // Top bar.
    private (PanelContainer Badge, Label Initials) _avatar;
    private readonly Label _name = new();
    private readonly Label _role = UiStyle.Caption(string.Empty, 14);
    private readonly Label _clock = new();
    private readonly Dictionary<SimulationSpeed, Button> _speeds = new();

    // The panel: a header, a scrolling body and a footer, each rebuilt only when what it shows changes.
    private readonly VBoxContainer _header = new();
    private readonly VBoxContainer _body = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    private readonly VBoxContainer _footer = new();
    private ScrollContainer _scroll = null!;
    private string _headerSignature = "\0";
    private string _bodySignature = "\0";
    private string _footerSignature = "\0";
    private double _refresh;

    // Prompts over the view.
    private PanelContainer _prompt = null!;
    private readonly Label _promptText = new();
    private PanelContainer _toast = null!;
    private readonly Label _toastText = new();
    private double _toastLeft;
    private PanelContainer _tag = null!;
    private readonly Label _tagTitle = new();
    private readonly Label _tagDetail = UiStyle.Caption(string.Empty, 13);
    private PanelContainer _keys = null!;
    private Control? _hint;

    // What the player is looking at in the panel.
    private bool _showPatients;
    private int? _openPatientId;
    private int? _dismissedPatientId;
    private string _tab = "History";
    private readonly HashSet<string> _pickedTests = new();
    private string? _pickedDiagnosis;
    private string _search = string.Empty;
    private ItemList? _diagnosisList;
    private bool _confirmDecide;

    /// <summary>Raised when the player presses "Leave doctor mode".</summary>
    public event Action? LeaveRequested;

    /// <summary>Raised when the player asks to be walked to where they are going.</summary>
    public event Action? WalkRequested;

    /// <summary>Raised when a card opens or closes that needs the mouse pointer.</summary>
    public event Action<bool>? PointerNeeded;

    /// <summary>The patient whose card is open, if any.</summary>
    public Patient? OpenPatient => _openPatientId is { } id && _sim.FindPatient(id) is { IsInDepartment: true } p ? p : null;

    /// <summary>True while the Hint card is showing.</summary>
    public bool HintOpen => _hint is not null;

    /// <summary>True while the player is typing in a box, so keys shouldn't walk the doctor about.</summary>
    public bool Typing => GetViewport().GuiGetFocusOwner() is LineEdit;

    /// <summary>Which tab of the patient card is showing (for tests).</summary>
    public string Tab => _tab;

    public void Initialise(GameSession session)
    {
        _session = session;
        _sim = session.Simulation;
        Layer = 2;

        _root = new Control { Name = "Root", Theme = UiStyle.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        BuildTopBar();
        BuildPanel();
        BuildPrompts();
        Visible = false;
    }

    // ------------------------------------------------------------------ building the fixed parts

    private void BuildTopBar()
    {
        var bar = new PanelContainer { Name = "TopBar", MouseFilter = Control.MouseFilterEnum.Stop };
        bar.AddThemeStyleboxOverride("panel", UiStyle.Card(14));
        bar.AnchorRight = 1;
        bar.OffsetLeft = Gap;
        bar.OffsetRight = -Gap;
        bar.OffsetTop = 12;
        bar.OffsetBottom = 12 + TopBarHeight;
        _root.AddChild(bar);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        bar.AddChild(row);
        _avatar = HudWidgets.Avatar(38);
        row.AddChild(_avatar.Badge);
        var who = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        who.AddThemeConstantOverride("separation", -2);
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 10);
        _name.AddThemeFontSizeOverride("font_size", 19);
        nameRow.AddChild(_name);
        var pill = HudWidgets.Chip();
        HudWidgets.SetChip(pill, "You are this doctor", UiStyle.BlueTint);
        pill.Text.AddThemeColorOverride("font_color", UiStyle.BluePressed);
        pill.Chip.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        nameRow.AddChild(pill.Chip);
        who.AddChild(nameRow);
        who.AddChild(_role);
        row.AddChild(who);
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

        _clock.AddThemeFontSizeOverride("font_size", 16);
        _clock.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_clock);
        foreach (var (speed, text, tip) in new[]
                 {
                     (SimulationSpeed.Paused, "II", "Pause (Space)"),
                     (SimulationSpeed.Normal, "1x", "Normal speed (1)"),
                     (SimulationSpeed.Fast, "2x", "2x speed (2)"),
                     (SimulationSpeed.VeryFast, "4x", "4x speed (3)"),
                     (SimulationSpeed.Fastest, "8x", "8x speed (4)"),
                 })
        {
            var button = UiStyle.SecondaryButton(text, () => _session.Clock.SetSpeed(speed));
            button.ToggleMode = true;
            button.TooltipText = tip;
            button.FocusMode = Control.FocusModeEnum.None;
            button.CustomMinimumSize = new Vector2(42, 36);
            button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            _speeds[speed] = button;
            row.AddChild(button);
        }

        var leave = UiStyle.SecondaryButton("Leave doctor mode  (Esc)", () => LeaveRequested?.Invoke());
        leave.Name = "Leave";
        leave.FocusMode = Control.FocusModeEnum.None;
        leave.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(leave);
    }

    private void BuildPanel()
    {
        var panel = new PanelContainer { Name = "Panel", MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", UiStyle.Card(18));
        panel.AnchorLeft = panel.AnchorRight = 1;
        panel.AnchorBottom = 1;
        panel.OffsetLeft = -PanelWidth - Gap;
        panel.OffsetRight = -Gap;
        panel.OffsetTop = 12 + TopBarHeight + 12;
        panel.OffsetBottom = -Gap;
        _root.AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        panel.AddChild(column);
        _header.AddThemeConstantOverride("separation", 8);
        column.AddChild(_header);
        _scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _body.AddThemeConstantOverride("separation", 6);
        _scroll.AddChild(_body);
        column.AddChild(_scroll);
        _footer.AddThemeConstantOverride("separation", 8);
        column.AddChild(_footer);
    }

    private void BuildPrompts()
    {
        _keys = Card(10);
        _keys.Name = "Keys";
        _keys.AnchorTop = _keys.AnchorBottom = 1;
        _keys.OffsetLeft = Gap;
        _keys.OffsetBottom = -Gap;
        _keys.OffsetTop = -Gap - 44;
        _keys.GrowVertical = Control.GrowDirection.Begin;
        var keys = UiStyle.Caption(
            BlueprintHospital.Core.GameView.ThreeD
                ? "W A S D  walk     Mouse  look     Shift  hurry     E  use     G  walk me there     H  hint     Tab  free the mouse"
                : "Arrow keys  walk     Shift  hurry     E  use     G  walk me there     H  hint     Wheel  zoom     Esc  leave",
            14);
        _keys.AddChild(keys);
        _root.AddChild(_keys);

        _prompt = Card(12);
        _prompt.Name = "Prompt";
        _prompt.AnchorLeft = _prompt.AnchorRight = 0.5f;
        _prompt.AnchorTop = _prompt.AnchorBottom = 1;
        _prompt.OffsetLeft = -PanelWidth / 2 - 260;
        _prompt.OffsetRight = -PanelWidth / 2 + 260;
        _prompt.OffsetBottom = -Gap - 64;
        _prompt.OffsetTop = -Gap - 64 - 48;
        _prompt.GrowVertical = Control.GrowDirection.Begin;
        _promptText.HorizontalAlignment = HorizontalAlignment.Center;
        _promptText.AddThemeFontSizeOverride("font_size", 17);
        _prompt.AddChild(_promptText);
        _root.AddChild(_prompt);

        _toast = new PanelContainer { Name = "Toast", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _toast.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.TextDark, 10, null, 12));
        _toast.AnchorLeft = _toast.AnchorRight = 0.5f;
        _toast.OffsetLeft = -PanelWidth / 2 - 300;
        _toast.OffsetRight = -PanelWidth / 2 + 300;
        _toast.OffsetTop = 12 + TopBarHeight + 14;
        _toastText.HorizontalAlignment = HorizontalAlignment.Center;
        _toastText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _toastText.AddThemeColorOverride("font_color", Colors.White);
        _toast.AddChild(_toastText);
        _root.AddChild(_toast);

        _tag = Card(8);
        _tag.Name = "Tag";
        var tag = new VBoxContainer();
        tag.AddThemeConstantOverride("separation", -2);
        _tagTitle.AddThemeFontSizeOverride("font_size", 15);
        tag.AddChild(_tagTitle);
        tag.AddChild(_tagDetail);
        _tag.AddChild(tag);
        _root.AddChild(_tag);
    }

    private static PanelContainer Card(int margin)
    {
        var card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        card.AddThemeStyleboxOverride("panel", UiStyle.Card(margin, 12));
        return card;
    }

    // ------------------------------------------------------------------ opening and closing

    /// <summary>Called when the player takes over a doctor.</summary>
    public void Open()
    {
        Visible = true;
        _showPatients = false;
        _openPatientId = null;
        _dismissedPatientId = null;
        _tab = "History";
        ResetPicks();
        CloseHint();
        _toast.Visible = false;
        Invalidate();
        Refresh();
    }

    public void Close()
    {
        CloseHint();
        Visible = false;
    }

    private void Invalidate() => _headerSignature = _bodySignature = _footerSignature = "\0";

    private void ResetPicks()
    {
        _pickedTests.Clear();
        _pickedDiagnosis = null;
        _search = string.Empty;
        _confirmDecide = false;
    }

    /// <summary>Shows a short message over the view: what just happened, or why something can't be done.</summary>
    public void Toast(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _toastText.Text = text;
        _toast.Visible = true;
        _toastLeft = 5;
    }

    private void Say(PlayerResult result)
    {
        Toast(result.Message);
        Invalidate();
    }

    /// <summary>Opens a patient's card on the given tab.</summary>
    public void OpenCard(Patient patient, string? tab = null)
    {
        if (_openPatientId != patient.Id)
        {
            ResetPicks();
            _tab = tab ?? (patient.HistoryTaken && patient.WorkingDiagnosis is null ? "Tests" : "History");
        }
        else if (tab is not null)
        {
            _tab = tab;
        }

        _openPatientId = patient.Id;
        _dismissedPatientId = null;
        Invalidate();
    }

    /// <summary>Back to the task list.</summary>
    public void CloseCard()
    {
        _dismissedPatientId = _openPatientId;
        _openPatientId = null;
        ResetPicks();
        Invalidate();
    }

    // ------------------------------------------------------------------ where the player is heading

    /// <summary>The job the player has picked, if it is still there.</summary>
    public ClinicalTask? PickedTask =>
        _sim.PlayerDoctor is { CurrentTaskId: { } id } ? _sim.Tasks.FirstOrDefault(t => t.Id == id) : null;

    /// <summary>The patient the player is heading for: the open card's, or the picked job's.</summary>
    public Patient? TargetPatient => OpenPatient ?? (PickedTask is { } t ? _sim.FindPatient(t.PatientId) : null);

    /// <summary>The tile the arrows on the floor lead to.</summary>
    public GridCoord? Target
    {
        get
        {
            if (PickedTask is { } task && (OpenPatient is null || OpenPatient.Id == task.PatientId))
            {
                return _sim.PlayerTaskSpot(task);
            }

            return OpenPatient is { } p ? _sim.PlayerBedsideSpot(p) : null;
        }
    }

    /// <summary>The job E would start now: the picked one, or the first of the open patient's that is within reach.</summary>
    public ClinicalTask? Startable()
    {
        if (PickedTask is { Started: false } picked && _sim.PlayerCanReach(picked))
        {
            return picked;
        }

        if (PickedTask is { Started: true })
        {
            return null;
        }

        return OpenPatient is { } p ? _sim.PlayerTasks().FirstOrDefault(t => t.PatientId == p.Id && _sim.PlayerCanReach(t)) : null;
    }

    /// <summary>E: starts the job at hand. Returns false when there was nothing to start.</summary>
    public bool StartJob()
    {
        if (Startable() is not { } task)
        {
            return false;
        }

        Say(_sim.PlayerStartTask(task.Id));
        if (_sim.FindPatient(task.PatientId) is { } patient)
        {
            OpenCard(patient);
        }

        return true;
    }

    // ------------------------------------------------------------------ every frame

    public override void _Process(double delta)
    {
        if (!Visible || _sim.PlayerDoctor is not { } doctor)
        {
            return;
        }

        if (_toastLeft > 0)
        {
            _toastLeft -= delta;
            _toast.Visible = _toastLeft > 0;
        }

        _clock.Text = _sim.Now.ToString("ddd HH:mm");
        foreach (var (speed, button) in _speeds)
        {
            button.SetPressedNoSignal(_session.Clock.Speed == speed);
        }

        UpdatePrompt(doctor);
        _refresh -= delta;
        if (_refresh <= 0)
        {
            _refresh = 0.25;
            Refresh();
        }
    }

    private void UpdatePrompt(StaffMember doctor)
    {
        string text;
        if (_sim.PlayerWorkProgress() is { } progress && PickedTask is { } working)
        {
            text = $"{working.Label}…  {progress:P0}";
        }
        else if (_session.Clock.IsPaused)
        {
            text = "Paused. Press Space to carry on.";
        }
        else if (Startable() is { } task)
        {
            text = $"E   {task.Label}: {_sim.FindPatient(task.PatientId)?.FullName}";
        }
        else if (_sim.PlayerFloorLinks() is { Count: > 0 } links)
        {
            text = $"E   Go to the {SiteMap.FloorName(_sim.FloorOf(links[0])).ToLowerInvariant()}";
        }
        else if (doctor.IsMoving)
        {
            text = BlueprintHospital.Core.GameView.ThreeD ? "Walking there…  press W A S D to take over" : "Walking there…  press an arrow key to take over";
        }
        else
        {
            text = string.Empty;
        }

        _promptText.Text = text;
        _prompt.Visible = text.Length > 0 && !HintOpen;
        _keys.Visible = !HintOpen;
    }

    /// <summary>Puts the name tag over the patient the player is heading for. Called by the doctor mode with where they are on screen.</summary>
    public void PlaceTag(Vector2? screen, string title, string detail)
    {
        _tag.Visible = screen is not null && !HintOpen;
        if (screen is not { } at)
        {
            return;
        }

        _tagTitle.Text = title;
        _tagDetail.Text = detail;
        _tag.ResetSize();
        var size = _tag.Size;
        var limit = _root.Size - new Vector2(PanelWidth + (Gap * 2), 0);
        var x = Mathf.Clamp(at.X - (size.X / 2), Gap, Math.Max(Gap, limit.X - size.X));
        var y = Mathf.Clamp(at.Y - size.Y - 8, TopBarHeight + 24, Math.Max(TopBarHeight + 24, limit.Y - size.Y - 120));
        _tag.Position = new Vector2(x, y);
    }

    // ------------------------------------------------------------------ the panel

    private void Refresh()
    {
        if (_sim.PlayerDoctor is not { } doctor)
        {
            return;
        }

        _name.Text = doctor.Name;
        var place = doctor.BaseRoomId is { } roomId && _sim.FindSpace(roomId) is { } room ? room.Name : "A&E";
        _role.Text = $"{doctor.Grade} · {place} · {HospitalSimulation.ShiftName(doctor.Shift)} shift, {HospitalSimulation.ShiftHoursText(doctor.Shift)}";
        var uniform = Looks.UniformColour(doctor);
        HudWidgets.SetAvatar(_avatar, doctor.Name, uniform.Luminance > 0.85f ? new Color("#5A6B7A") : uniform);

        // The card closes itself when its patient leaves; arriving at a picked patient's bedside opens theirs.
        if (_openPatientId is not null && OpenPatient is null)
        {
            _openPatientId = null;
            ResetPicks();
        }

        if (_openPatientId is null && PickedTask is { } picked && _sim.FindPatient(picked.PatientId) is { } at && at.Id != _dismissedPatientId && _sim.PlayerCanReach(picked))
        {
            OpenCard(at);
        }

        if (_dismissedPatientId is { } dismissed && (_sim.FindPatient(dismissed) is not { } gone || !_sim.PlayerIsWith(gone)))
        {
            _dismissedPatientId = null;
        }

        if (OpenPatient is { } patient)
        {
            RefreshCard(patient);
        }
        else
        {
            RefreshList(doctor);
        }
    }

    private static void Clear(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    private bool Changed(ref string signature, string now)
    {
        if (signature == now)
        {
            return false;
        }

        signature = now;
        return true;
    }

    // ---------------------------------------------------------------- the task list

    private void RefreshList(StaffMember doctor)
    {
        var tasks = _sim.PlayerTasks();
        var decisions = _sim.PlayerDecisionsWaiting();
        var patients = _sim.PlayerPatients();

        if (Changed(ref _headerSignature, $"list|{_showPatients}|{tasks.Count + decisions.Count}|{patients.Count}"))
        {
            Clear(_header);
            var top = new HBoxContainer();
            var title = UiStyle.Heading("Be A Doctor", 22);
            title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            top.AddChild(title);
            var count = tasks.Count + decisions.Count;
            top.AddChild(UiStyle.Caption(count == 1 ? "1 doctor task waiting" : $"{count} doctor tasks waiting", 14));
            _header.AddChild(top);
            _header.AddChild(TabRow(new[] { ("My tasks", !_showPatients), ($"My patients ({patients.Count})", _showPatients) }, i =>
            {
                _showPatients = i == 1;
                Invalidate();
            }));
        }

        if (_showPatients)
        {
            var signature = "patients|" + string.Join("|", patients.Select(p => $"{p.Id}{p.Triage}{p.WorkingDiagnosis?.ConditionId}{_sim.NeedsDecision(p)}{p.CareSpaceId}"));
            if (Changed(ref _bodySignature, signature))
            {
                Clear(_body);
                if (patients.Count == 0)
                {
                    _body.AddChild(UiStyle.Body("Nobody yet. Patients you assess stay on this list until they leave.", muted: true));
                }

                foreach (var p in patients)
                {
                    var status = p.WorkingDiagnosis is { } d ? _session.Database.Conditions[d.ConditionId].Name
                        : _sim.NeedsDecision(p) ? "Needs your decision"
                        : "Waiting for results";
                    var captured = p;
                    _body.AddChild(Row(ClinicalText.TriageColour(p.Triage), $"{p.FullName}, {p.Age}", $"{ClinicalText.Whereabouts(_sim, p)} · {status}", string.Empty, string.Empty, false, () => OpenCard(captured)));
                }
            }
        }
        else
        {
            var rows = tasks.Select(t => (Task: t, Patient: _sim.FindPatient(t.PatientId)!)).ToList();
            var signature = "tasks|" + string.Join("|", rows.Select(r => $"{r.Task.Id}{r.Task.AssignedStaffId}{r.Task.Started}{r.Patient.Triage}{(int)(_sim.Now - r.Task.CreatedAt).TotalMinutes}{Metres(r.Task) / 3}"))
                            + "|d|" + string.Join("|", decisions.Select(p => p.Id));
            if (Changed(ref _bodySignature, signature))
            {
                Clear(_body);
                foreach (var p in decisions)
                {
                    var captured = p;
                    _body.AddChild(Row(ClinicalText.TriageColour(p.Triage), "Decide what happens next", $"{p.FullName}, {p.Age} · {ClinicalText.Whereabouts(_sim, p)}", "results are back", string.Empty, false, () => OpenCard(captured, "Tests")));
                }

                foreach (var (task, patient) in rows)
                {
                    var captured = task;
                    var mine = task.AssignedStaffId == doctor.Id;
                    var wait = ClinicalText.Duration(_sim.Now - task.CreatedAt);
                    _body.AddChild(Row(ClinicalText.TriageColour(patient.Triage), task.Label,
                        $"{patient.FullName}, {patient.Age} · {ClinicalText.Whereabouts(_sim, patient)}",
                        _sim.OnTheirWay(task) is { } colleague ? $"{colleague.Name} is on the way" : $"waiting {wait}", $"{Metres(task)} m away", mine, () => Pick(captured)));
                }

                if (rows.Count == 0 && decisions.Count == 0)
                {
                    _body.AddChild(UiStyle.Body("Nothing is waiting for a doctor right now. New jobs appear here as patients are triaged and results come back.", muted: true));
                }
            }
        }

        var pickedTask = PickedTask;
        if (Changed(ref _footerSignature, $"list|{pickedTask?.Id}|{pickedTask?.Started}"))
        {
            Clear(_footer);
            if (pickedTask is not null)
            {
                _footer.AddChild(Wrapped("Blue arrows on the floor lead to the job you picked."));
                var buttons = new HBoxContainer();
                buttons.AddThemeConstantOverride("separation", 8);
                var walk = UiStyle.Button("Walk me there  (G)", () => WalkRequested?.Invoke());
                walk.Name = "Walk";
                walk.FocusMode = Control.FocusModeEnum.None;
                walk.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                walk.CustomMinimumSize = new Vector2(0, 46);
                buttons.AddChild(walk);
                var drop = UiStyle.SecondaryButton("Put back", () =>
                {
                    _sim.PlayerDropTask();
                    Invalidate();
                });
                drop.TooltipText = "Leave this job for another doctor";
                drop.FocusMode = Control.FocusModeEnum.None;
                drop.CustomMinimumSize = new Vector2(0, 46);
                buttons.AddChild(drop);
                _footer.AddChild(buttons);
            }
            else
            {
                _footer.AddChild(Wrapped("Pick a job to see the way there. Other doctors take whatever you leave."));
            }
        }
    }

    private void Pick(ClinicalTask task)
    {
        var result = _sim.PlayerClaim(task.Id);
        if (!result.Ok)
        {
            Toast(result.Message);
        }

        _dismissedPatientId = null;
        Invalidate();
    }

    /// <summary>How far the walk to a job is, in metres.</summary>
    private int Metres(ClinicalTask task)
    {
        if (_sim.PlayerTaskSpot(task) is not { } spot)
        {
            return 0;
        }

        var route = _sim.PlayerRouteTo(spot);
        double tiles = 0;
        for (var i = 0; i + 1 < route.Count; i++)
        {
            var (dx, dy) = (route[i + 1].X - route[i].X, route[i + 1].Y - route[i].Y);
            tiles += Math.Min(Math.Sqrt((dx * dx) + (dy * dy)), 12);
        }

        return (int)Math.Round(tiles * Units.MetresPerTile);
    }

    /// <summary>A clickable row: a coloured dot, two lines of text and two small lines on the right.</summary>
    private static Button Row(Color dot, string title, string detail, string right1, string right2, bool selected, Action onPressed)
    {
        var button = new Button { ThemeTypeVariation = UiStyle.Secondary, CustomMinimumSize = new Vector2(0, 62), FocusMode = Control.FocusModeEnum.None, ClipContents = true };
        if (selected)
        {
            var box = UiStyle.Box(new Color("#F3F7FD"), 10, UiStyle.Blue);
            box.BorderWidthBottom = box.BorderWidthTop = box.BorderWidthLeft = box.BorderWidthRight = 2;
            foreach (var state in new[] { "normal", "hover", "pressed" })
            {
                button.AddThemeStyleboxOverride(state, box);
            }
        }

        button.Pressed += onPressed;
        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        line.OffsetLeft = 12;
        line.OffsetRight = -12;
        line.AddThemeConstantOverride("separation", 10);
        var blob = new Panel { CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        blob.AddThemeStyleboxOverride("panel", UiStyle.Box(dot, 7));
        line.AddChild(blob);
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", -2);
        var heading = new Label { Text = title, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = Control.MouseFilterEnum.Ignore };
        heading.AddThemeFontSizeOverride("font_size", 16);
        heading.AddThemeColorOverride("font_color", UiStyle.TextDark);
        text.AddChild(heading);
        var sub = UiStyle.Caption(detail, 13);
        sub.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        text.AddChild(sub);
        line.AddChild(text);
        if (right1.Length + right2.Length > 0)
        {
            var right = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            right.AddThemeConstantOverride("separation", -2);
            foreach (var value in new[] { right1, right2 }.Where(v => v.Length > 0))
            {
                var label = UiStyle.Caption(value, 13);
                label.HorizontalAlignment = HorizontalAlignment.Right;
                right.AddChild(label);
            }

            line.AddChild(right);
        }

        button.AddChild(line);
        return button;
    }

    private static HBoxContainer TabRow((string Text, bool On)[] tabs, Action<int> onPressed)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        for (var i = 0; i < tabs.Length; i++)
        {
            var index = i;
            var tab = UiStyle.SecondaryButton(tabs[i].Text, () => onPressed(index));
            tab.ToggleMode = true;
            tab.SetPressedNoSignal(tabs[i].On);
            tab.FocusMode = Control.FocusModeEnum.None;
            tab.CustomMinimumSize = new Vector2(0, 36);
            tab.AddThemeFontSizeOverride("font_size", 14);
            row.AddChild(tab);
        }

        return row;
    }

    // ---------------------------------------------------------------- the patient card

    private void RefreshCard(Patient patient)
    {
        var obs = patient.LatestObservations;
        var with = _sim.PlayerIsWith(patient);
        if (Changed(ref _headerSignature, $"card|{patient.Id}|{patient.Triage}|{obs?.Obs}|{obs?.News2.Total}|{_tab}|{ClinicalText.Whereabouts(_sim, patient)}"))
        {
            Clear(_header);
            var top = new HBoxContainer();
            top.AddThemeConstantOverride("separation", 10);
            var blob = new Panel { CustomMinimumSize = new Vector2(18, 18), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            blob.AddThemeStyleboxOverride("panel", UiStyle.Box(ClinicalText.TriageColour(patient.Triage), 9));
            top.AddChild(blob);
            var who = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            who.AddThemeConstantOverride("separation", -2);
            var name = UiStyle.Heading($"{patient.FullName}, {patient.Age}", 21);
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            who.AddChild(name);
            var arrived = patient.ArrivalMethod == ArrivalMethod.Ambulance ? "by ambulance" : "on foot";
            var line = UiStyle.Caption($"{ClinicalText.Whereabouts(_sim, patient)} · arrived {patient.ArrivedAt:HH:mm} {arrived} · {patient.HospitalNumber}", 13);
            line.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            who.AddChild(line);
            top.AddChild(who);
            var back = UiStyle.SecondaryButton("← Tasks", CloseCard);
            back.Name = "Back";
            back.FocusMode = Control.FocusModeEnum.None;
            back.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            top.AddChild(back);
            _header.AddChild(top);

            var tiles = new HBoxContainer();
            tiles.AddThemeConstantOverride("separation", 6);
            void Tile(string caption, string value, bool bad)
            {
                var tile = HudWidgets.StatTile(caption);
                tile.Value.Text = value;
                if (bad)
                {
                    tile.Value.AddThemeColorOverride("font_color", UiStyle.Red);
                }

                tiles.AddChild(tile.Tile);
            }

            if (obs is { } o)
            {
                Tile("Heart rate", o.Obs.HeartRate.ToString(), o.News2.HeartRate >= 2);
                Tile("BP", o.Obs.BloodPressure, o.News2.SystolicBp >= 2);
                Tile("Sats", o.Obs.OxygenSaturation + "%", o.News2.OxygenSaturation >= 2);
                Tile("NEWS2", o.News2.Total.ToString(), o.News2.Total >= 5 || o.News2.HasRedScore);
            }
            else
            {
                Tile("Observations", "none yet", false);
            }

            _header.AddChild(tiles);
            _header.AddChild(TabRow(Tabs.Select(t => (t, t == _tab)).ToArray(), i =>
            {
                _tab = Tabs[i];
                _confirmDecide = false;
                Invalidate();
            }));
        }

        var canDecide = _sim.PlayerCanDecide(patient);
        var results = string.Join(",", patient.Investigations.Select(i => $"{i.InvestigationId}{i.Status}{i.Abnormal}"));
        var plan = string.Join(",", patient.TreatmentPlan.Select(r => $"{r.Done}{r.Withheld}")) + string.Join(",", _sim.PlayerExtraTasksFor(patient).Select(t => t.Id));
        var bodyNow = $"card|{patient.Id}|{_tab}|{patient.HistoryTaken}|{patient.WorkingDiagnosis?.ConditionId}|{canDecide.Ok}|{canDecide.Message}|{results}|"
                      + _tab switch
                      {
                          "History" => string.Join(",", patient.KnownSymptoms),
                          "Treatment" => plan,
                          "Notes" => patient.Timeline.Count.ToString(),
                          _ => string.Empty,
                      };
        if (Changed(ref _bodySignature, bodyNow))
        {
            Clear(_body);
            _diagnosisList = null;
            _scroll.ScrollVertical = 0;
            switch (_tab)
            {
                case "History":
                    BuildHistory(patient);
                    break;
                case "Tests":
                    BuildTests(patient, canDecide);
                    break;
                case "Diagnosis":
                    BuildDiagnosis(patient, canDecide);
                    break;
                case "Treatment":
                    BuildTreatment(patient);
                    break;
                default:
                    BuildNotes(patient);
                    break;
            }
        }

        var job = PickedTask is { } pickedTask && pickedTask.PatientId == patient.Id ? pickedTask : _sim.PlayerTasks().FirstOrDefault(t => t.PatientId == patient.Id);
        var progress = job is { Started: true } ? _sim.PlayerWorkProgress() : null;
        var footerNow = $"card|{patient.Id}|{_tab}|{job?.Id}|{job?.Started}|{(progress is { } pr ? (int)(pr * 50) : -1)}|{canDecide.Ok}|{with}|{_pickedTests.Count}|{_pickedDiagnosis}|{_confirmDecide}|{patient.WorkingDiagnosis is null}";
        if (Changed(ref _footerSignature, footerNow))
        {
            BuildCardFooter(patient, job, progress, canDecide, with);
        }
    }

    private void AddSection(string text) => _body.AddChild(HudWidgets.Section(text));

    private void AddLine(string label, string value, Color? colour = null, bool bold = false)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var left = new Label { Text = label, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        left.AddThemeFontSizeOverride("font_size", 15);
        row.AddChild(left);
        if (value.Length > 0)
        {
            var right = new Label { Text = value, HorizontalAlignment = HorizontalAlignment.Right, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(190, 0) };
            right.AddThemeFontSizeOverride("font_size", bold ? 15 : 14);
            right.AddThemeColorOverride("font_color", colour ?? UiStyle.TextMuted);
            row.AddChild(right);
        }
        else if (colour is { } c)
        {
            left.AddThemeColorOverride("font_color", c);
        }

        _body.AddChild(row);
    }

    private void AddNote(string text)
    {
        var note = new PanelContainer();
        note.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.AmberTint, 10, UiStyle.AmberBorder, 10));
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color", UiStyle.AmberText);
        note.AddChild(label);
        _body.AddChild(note);
    }

    private void BuildHistory(Patient patient)
    {
        var db = _session.Database;
        if (!patient.HistoryTaken)
        {
            AddNote("Not assessed by a doctor yet. Start the assessment at the bedside to take the full history and examine them.");
        }

        AddSection(patient.HistoryTaken ? (patient.Sex == Sex.Female ? "She tells you" : "He tells you") : "Known so far");
        var symptoms = patient.HistoryTaken ? patient.Symptoms : patient.KnownSymptoms.OrderBy(s => s, StringComparer.Ordinal).ToList();
        foreach (var s in symptoms)
        {
            var symptom = db.Symptoms[s];
            AddLine(symptom.Name, symptom.IsRedFlag ? "red flag" : string.Empty, symptom.IsRedFlag ? UiStyle.Red : null);
        }

        if (symptoms.Count == 0)
        {
            _body.AddChild(UiStyle.Body("Nothing recorded yet.", muted: true));
        }

        if (patient.TriageReason.Length > 0)
        {
            AddSection("Triage");
            _body.AddChild(UiStyle.Body($"{ClinicalText.TriageName(patient.Triage)}: {patient.TriageReason}"));
        }

        if (patient.AmbulanceHandover is { Length: > 0 } handover)
        {
            AddSection("Ambulance handover");
            _body.AddChild(UiStyle.Body(handover));
        }

        AddSection("Background");
        if (patient.HistoryTaken)
        {
            AddLine("Past history", patient.PastHistory.Count == 0 ? "nothing significant" : string.Join(", ", patient.PastHistory));
            AddLine("Regular medicines", patient.RegularMedications.Count == 0 ? "none" : string.Join(", ", patient.RegularMedications));
        }

        AddLine("Allergies", patient.Allergies.Count == 0 ? "none known" : string.Join(", ", patient.Allergies), patient.Allergies.Count == 0 ? null : UiStyle.Red, bold: patient.Allergies.Count > 0);
        if (patient.LatestObservations is { } o)
        {
            AddSection("Latest observations");
            AddLine("Breathing rate", o.Obs.RespiratoryRate.ToString(), o.News2.RespiratoryRate >= 2 ? UiStyle.Red : null);
            AddLine("Temperature", $"{o.Obs.Temperature:0.0}°C");
            AddLine("Conscious level", o.Obs.Consciousness.ToString(), o.News2.Consciousness >= 3 ? UiStyle.Red : null);
            AddLine("Pain", $"{o.Obs.PainScore}/10");
        }
    }

    private void AddResults(Patient patient)
    {
        var db = _session.Database;
        var back = patient.Investigations.Where(i => i.Status == InvestigationStatus.Resulted).ToList();
        var waiting = patient.Investigations.Where(i => i.Status != InvestigationStatus.Resulted).ToList();
        AddSection(patient.Investigations.Count == 0 ? "Results" : $"Results back ({back.Count} of {patient.Investigations.Count})");
        foreach (var r in back)
        {
            var row = new VBoxContainer();
            row.AddThemeConstantOverride("separation", -2);
            var top = new HBoxContainer();
            var name = new Label { Text = db.Investigations[r.InvestigationId].Name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            name.AddThemeFontSizeOverride("font_size", 15);
            top.AddChild(name);
            var verdict = new Label { Text = r.Abnormal ? "Abnormal" : "Normal" };
            verdict.AddThemeFontSizeOverride("font_size", 14);
            verdict.AddThemeColorOverride("font_color", r.Abnormal ? UiStyle.Red : UiStyle.Green);
            top.AddChild(verdict);
            row.AddChild(top);
            var summary = UiStyle.Caption(r.Summary, 13);
            summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            row.AddChild(summary);
            _body.AddChild(row);
        }

        if (waiting.Count > 0)
        {
            var text = UiStyle.Caption("Still to come: " + string.Join(", ", waiting.Select(w => db.Investigations[w.InvestigationId].Name)), 13);
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _body.AddChild(text);
        }
        else if (back.Count == 0)
        {
            _body.AddChild(UiStyle.Body("No tests requested yet.", muted: true));
        }
    }

    private void BuildTests(Patient patient, PlayerResult canDecide)
    {
        AddResults(patient);
        if (patient.WorkingDiagnosis is not null)
        {
            return;
        }

        AddSection("Request tests");
        if (!canDecide.Ok)
        {
            AddNote(canDecide.Message);
        }

        var options = _sim.TestOptionsFor(patient);
        _pickedTests.RemoveWhere(id => options.All(o => o.Test.Id != id || !o.Available));
        var shownOther = false;
        foreach (var option in options)
        {
            if (!option.Relevant && !shownOther)
            {
                shownOther = true;
                _body.AddChild(UiStyle.Caption("Other tests", 13));
            }

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var id = option.Test.Id;
            var box = new Button
            {
                Text = option.Test.Name,
                ToggleMode = true,
                Alignment = HorizontalAlignment.Left,
                Disabled = !option.Available || !canDecide.Ok,
                ButtonPressed = option.AlreadyRequested || _pickedTests.Contains(id),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                FocusMode = Control.FocusModeEnum.None,
                ClipText = true,
                CustomMinimumSize = new Vector2(0, 38),
            };
            StyleTick(box);
            box.Toggled += on =>
            {
                if (on)
                {
                    _pickedTests.Add(id);
                }
                else
                {
                    _pickedTests.Remove(id);
                }
            };
            row.AddChild(box);
            var note = UiStyle.Caption(option.Note, 12);
            note.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(note);
            _body.AddChild(row);
        }
    }

    private void BuildDiagnosis(Patient patient, PlayerResult canDecide)
    {
        var db = _session.Database;
        if (patient.WorkingDiagnosis is { } made)
        {
            AddSection("Diagnosis");
            _body.AddChild(UiStyle.Heading(db.Conditions[made.ConditionId].Name, 19));
            var by = _sim.FindStaff(made.DoctorId)?.Name ?? "a doctor";
            _body.AddChild(UiStyle.Body($"Made by {by} at {made.MadeAt:HH:mm}. The treatment is on the Treatment tab.", muted: true));
            AddResults(patient);
            return;
        }

        AddResults(patient);
        AddSection("Your diagnosis");
        if (!canDecide.Ok)
        {
            AddNote(canDecide.Message);
        }

        var search = new LineEdit { PlaceholderText = $"Search all {_sim.DiagnosisOptionsFor(patient).Count} conditions…", Text = _search, ClearButtonEnabled = true };
        search.TextChanged += text =>
        {
            _search = text;
            FillDiagnoses(patient);
        };
        _body.AddChild(search);
        _diagnosisList = new ItemList { CustomMinimumSize = new Vector2(0, 250), SizeFlagsVertical = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None, AllowReselect = true };
        _diagnosisList.AddThemeFontSizeOverride("font_size", 15);
        _diagnosisList.ItemSelected += index =>
        {
            _pickedDiagnosis = _diagnosisList.GetItemMetadata((int)index).AsString();
        };
        _body.AddChild(_diagnosisList);
        FillDiagnoses(patient);
    }

    private void FillDiagnoses(Patient patient)
    {
        if (_diagnosisList is not { } list)
        {
            return;
        }

        list.Clear();
        var term = _search.Trim();
        var options = _sim.DiagnosisOptionsFor(patient);
        var shown = term.Length == 0
            ? options.Where(o => o.SameComplaint).ToList()
            : options.Where(o => o.Condition.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        if (term.Length == 0 && shown.Count == 0)
        {
            shown = options.ToList();
        }

        foreach (var (condition, _) in shown)
        {
            var index = list.AddItem(condition.Name);
            list.SetItemMetadata(index, condition.Id);
            list.SetItemTooltip(index, condition.Description);
            if (condition.Id == _pickedDiagnosis)
            {
                list.Select(index);
            }
        }
    }

    private void BuildTreatment(Patient patient)
    {
        var db = _session.Database;
        if (patient.WorkingDiagnosis is not { } made)
        {
            _body.AddChild(UiStyle.Body("Treatment is prescribed as soon as a diagnosis is made. Nurses give the medicines, or you can give them yourself.", muted: true));
        }
        else
        {
            AddSection($"Prescribed for {db.Conditions[made.ConditionId].Name}");
        }

        var extras = _sim.PlayerExtraTasksFor(patient);
        for (var i = 0; i < patient.TreatmentPlan.Count; i++)
        {
            var record = patient.TreatmentPlan[i];
            var name = record.Step.Type == BlueprintHospital.Simulation.Data.Models.TreatmentStepType.Medication
                ? db.Medications[record.Step.Medication!].Name
                : record.Step.Name ?? "Procedure";
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            text.AddThemeConstantOverride("separation", -2);
            var title = new Label { Text = name, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            title.AddThemeFontSizeOverride("font_size", 15);
            text.AddChild(title);
            var status = record.Withheld ? record.Note : record.Done ? $"Done · {record.Note}" : "Waiting to be given";
            var caption = UiStyle.Caption(status, 13);
            caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (record.Withheld)
            {
                caption.AddThemeColorOverride("font_color", UiStyle.Red);
            }
            else if (record.Done)
            {
                caption.AddThemeColorOverride("font_color", UiStyle.Green);
            }

            text.AddChild(caption);
            row.AddChild(text);
            var index = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var mine = _sim.PlayerTasks().Concat(extras).FirstOrDefault(t => t.PatientId == patient.Id && t.Detail == index && t.Kind is TaskKind.GiveMedication or TaskKind.Procedure);
            if (!record.Done && mine is not null)
            {
                var task = mine;
                var give = UiStyle.SecondaryButton(task.Role == StaffRole.Doctor ? "Do it" : "Do it myself", () => Say(_sim.PlayerStartTask(task.Id)));
                give.FocusMode = Control.FocusModeEnum.None;
                give.AddThemeFontSizeOverride("font_size", 13);
                give.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                row.AddChild(give);
            }

            _body.AddChild(row);
        }

        var others = extras.Where(t => t.Kind is not (TaskKind.GiveMedication or TaskKind.Procedure)).ToList();
        if (others.Count > 0)
        {
            AddSection("Nursing jobs you could do yourself");
            foreach (var task in others)
            {
                var captured = task;
                var row = new HBoxContainer();
                var label = new Label { Text = task.Label, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                label.AddThemeFontSizeOverride("font_size", 15);
                row.AddChild(label);
                var go = UiStyle.SecondaryButton("Do it myself", () => Say(_sim.PlayerStartTask(captured.Id)));
                go.FocusMode = Control.FocusModeEnum.None;
                go.AddThemeFontSizeOverride("font_size", 13);
                row.AddChild(go);
                _body.AddChild(row);
            }
        }
    }

    private void BuildNotes(Patient patient)
    {
        foreach (var entry in patient.Timeline.Reverse().Take(40))
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var time = UiStyle.Caption(entry.Time.ToString("HH:mm"), 13);
            time.CustomMinimumSize = new Vector2(42, 0);
            row.AddChild(time);
            var text = new Label { Text = entry.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            text.AddThemeFontSizeOverride("font_size", 14);
            if (entry.Important)
            {
                text.AddThemeColorOverride("font_color", UiStyle.BluePressed);
            }

            row.AddChild(text);
            _body.AddChild(row);
        }
    }

    /// <summary>A test to tick: white until picked, then tinted blue with a tick in front of its name.</summary>
    private static void StyleTick(Button box)
    {
        var name = box.Text;
        void Mark() => box.Text = (box.ButtonPressed ? "☑  " : "☐  ") + name;
        Mark();
        box.Toggled += _ => Mark();
        box.AddThemeFontSizeOverride("font_size", 15);
        box.AddThemeStyleboxOverride("normal", UiStyle.Box(UiStyle.Surface, 8, UiStyle.Border, 10));
        box.AddThemeStyleboxOverride("hover", UiStyle.Box(UiStyle.Ground, 8, UiStyle.Border, 10));
        box.AddThemeStyleboxOverride("pressed", UiStyle.Box(UiStyle.BlueTint, 8, UiStyle.Blue, 10));
        box.AddThemeStyleboxOverride("hover_pressed", UiStyle.Box(UiStyle.BlueTint, 8, UiStyle.Blue, 10));
        box.AddThemeStyleboxOverride("disabled", UiStyle.Box(UiStyle.Ground, 8, UiStyle.Border, 10));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_focus_color" })
        {
            box.AddThemeColorOverride(state, UiStyle.TextDark);
        }

        box.AddThemeColorOverride("font_pressed_color", UiStyle.BluePressed);
        box.AddThemeColorOverride("font_hover_pressed_color", UiStyle.BluePressed);
        box.AddThemeColorOverride("font_disabled_color", UiStyle.TextMuted);
    }

    /// <summary>A small caption that wraps instead of pushing the panel wider than the screen.</summary>
    private static Label Wrapped(string text)
    {
        var label = UiStyle.Caption(text, 14);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }

    private void BuildCardFooter(Patient patient, ClinicalTask? job, double? progress, PlayerResult canDecide, bool with)
    {
        Clear(_footer);
        if (job is { Started: true } && progress is { } done)
        {
            var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = done, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10) };
            _footer.AddChild(bar);
            var minutes = Math.Max(1, (int)Math.Ceiling((_sim.PlayerDoctor?.WorkRemainingSeconds ?? 0) / 60));
            _footer.AddChild(Wrapped($"{job.Label}: about {minutes} min to go. Stay at the bedside until it finishes."));
        }
        else if (job is not null)
        {
            var captured = job;
            _footer.AddChild(Wrapped(with ? $"Next job: {job.Label}" : $"Next job: {job.Label}. Walk to the bedside first."));
            var start = UiStyle.Button("Start  (E)", () =>
            {
                Say(_sim.PlayerStartTask(captured.Id));
            });
            start.Name = "Start";
            start.FocusMode = Control.FocusModeEnum.None;
            start.CustomMinimumSize = new Vector2(0, 46);
            start.Disabled = !with;
            _footer.AddChild(start);
            if (!with)
            {
                var walk = UiStyle.SecondaryButton("Walk me there  (G)", () =>
                {
                    _sim.PlayerClaim(captured.Id);
                    WalkRequested?.Invoke();
                });
                walk.FocusMode = Control.FocusModeEnum.None;
                _footer.AddChild(walk);
            }
        }
        else if (!with)
        {
            var walk = UiStyle.SecondaryButton("Walk me there  (G)", () => WalkRequested?.Invoke());
            walk.FocusMode = Control.FocusModeEnum.None;
            _footer.AddChild(walk);
        }

        var deciding = patient.WorkingDiagnosis is null && patient.HistoryTaken;
        if (deciding && _tab == "Tests")
        {
            var request = UiStyle.Button(_pickedTests.Count == 0 ? "Tick the tests to request" : _pickedTests.Count == 1 ? "Request 1 test" : $"Request {_pickedTests.Count} tests", () =>
            {
                var result = _sim.PlayerOrderTests(patient.Id, _pickedTests.ToList());
                if (result.Ok)
                {
                    _pickedTests.Clear();
                }

                Say(result);
            });
            request.Name = "Request";
            request.Disabled = !canDecide.Ok || _pickedTests.Count == 0;
            request.FocusMode = Control.FocusModeEnum.None;
            request.CustomMinimumSize = new Vector2(0, 46);
            _footer.AddChild(request);
        }
        else if (deciding && _tab == "Diagnosis")
        {
            var confirm = UiStyle.Button("Confirm diagnosis", () =>
            {
                if (_pickedDiagnosis is not { } id)
                {
                    Toast("Choose a diagnosis from the list first.");
                    return;
                }

                var result = _sim.PlayerDiagnose(patient.Id, id);
                if (result.Ok)
                {
                    _tab = "Treatment";
                    ResetPicks();
                }

                Say(result);
            });
            confirm.Name = "Confirm";
            confirm.Disabled = !canDecide.Ok;
            confirm.FocusMode = Control.FocusModeEnum.None;
            confirm.CustomMinimumSize = new Vector2(0, 46);
            _footer.AddChild(confirm);
        }

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        var hint = new Button { Text = "💡 Hint  (H)", Name = "Hint", CustomMinimumSize = new Vector2(0, 44), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        foreach (var state in new[] { "normal", "hover", "pressed" })
        {
            var box = UiStyle.Box(state == "normal" ? UiStyle.AmberTint : new Color("#FFEFCF"), 10, UiStyle.AmberBorder);
            box.ContentMarginLeft = box.ContentMarginRight = 16;
            box.ContentMarginTop = box.ContentMarginBottom = 8;
            hint.AddThemeStyleboxOverride(state, box);
        }

        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
        {
            hint.AddThemeColorOverride(state, UiStyle.AmberText);
        }

        hint.Pressed += () => ToggleHint();
        buttons.AddChild(hint);
        if (deciding)
        {
            var decide = UiStyle.SecondaryButton(_confirmDecide ? "Confirm: decide for me" : "Decide for me", () =>
            {
                if (!_confirmDecide)
                {
                    _confirmDecide = true;
                    Invalidate();
                    return;
                }

                _confirmDecide = false;
                var result = _sim.PlayerDecideForMe(patient.Id);
                if (result.Ok && patient.WorkingDiagnosis is not null)
                {
                    _tab = "Treatment";
                }

                Say(result);
            });
            decide.Name = "Decide";
            decide.TooltipText = "Let this doctor's own skill choose the next step: more tests, or a diagnosis.";
            decide.Disabled = !canDecide.Ok;
            decide.CustomMinimumSize = new Vector2(0, 44);
            decide.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            decide.FocusMode = Control.FocusModeEnum.None;
            buttons.AddChild(decide);
        }

        _footer.AddChild(buttons);
    }

    // ------------------------------------------------------------------ the Hint card

    /// <summary>H: opens the Hint card for the open patient (or the one being walked to), or closes it.</summary>
    public bool ToggleHint()
    {
        if (HintOpen)
        {
            CloseHint();
            return true;
        }

        if (TargetPatient is not { } patient)
        {
            Toast("Pick a patient first: hints are about one patient.");
            return false;
        }

        ShowHint(patient, _sim.HintFor(patient));
        return true;
    }

    public void CloseHint()
    {
        if (_hint is null)
        {
            return;
        }

        _hint.QueueFree();
        _hint = null;
        PointerNeeded?.Invoke(false);
    }

    private void ShowHint(Patient patient, ClinicalHint hint)
    {
        var scrim = new ColorRect { Name = "Hint", Color = new Color(0.09f, 0.14f, 0.18f, 0.45f), MouseFilter = Control.MouseFilterEnum.Stop };
        scrim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(scrim);
        _hint = scrim;

        var card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        card.AddThemeStyleboxOverride("panel", UiStyle.Card(24));
        card.AnchorRight = 1;
        card.AnchorBottom = 1;
        card.OffsetLeft = 60;
        card.OffsetRight = -PanelWidth - (Gap * 2) - 20;
        card.OffsetTop = 12 + TopBarHeight + 16;
        card.OffsetBottom = -24;
        scrim.AddChild(card);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        card.AddChild(column);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 12);
        var bulb = new Label { Text = "💡" };
        bulb.AddThemeFontSizeOverride("font_size", 28);
        top.AddChild(bulb);
        var titles = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", -2);
        titles.AddChild(UiStyle.Heading($"Hint: {patient.FullName}, {patient.Age}, {(patient.Sex == Sex.Female ? "female" : "male")}", 22));
        titles.AddChild(UiStyle.Caption("Everything you know so far, and what it points towards. The diagnosis is still your call.", 14));
        top.AddChild(titles);
        var close = UiStyle.SecondaryButton("Close  (Esc)", CloseHint);
        close.FocusMode = Control.FocusModeEnum.None;
        close.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        top.AddChild(close);
        column.AddChild(top);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(content);
        column.AddChild(scroll);

        var boxes = new HBoxContainer();
        boxes.AddThemeConstantOverride("separation", 12);
        boxes.AddChild(HintBox(hint.HistoryTaken ? (patient.Sex == Sex.Female ? "She tells you" : "He tells you") : "Known so far", hint.History));
        var when = patient.ObservationHistory.Count > 0 ? $" · {patient.ObservationHistory[^1].Time:HH:mm}" : string.Empty;
        boxes.AddChild(HintBox("Observations" + when, hint.Observations));
        boxes.AddChild(HintBox("Tests", hint.Tests));
        content.AddChild(boxes);

        var clues = Boxed("What stands out");
        if (hint.Clues.Count == 0)
        {
            clues.AddChild(UiStyle.Body("Nothing stands out yet. A full history and a few tests will give you more to go on.", muted: true));
        }

        for (var i = 0; i < hint.Clues.Count; i++)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var number = new PanelContainer { CustomMinimumSize = new Vector2(24, 24), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
            number.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.BlueTint, 12));
            var digit = new Label { Text = (i + 1).ToString(), HorizontalAlignment = HorizontalAlignment.Center };
            digit.AddThemeFontSizeOverride("font_size", 13);
            digit.AddThemeColorOverride("font_color", UiStyle.BluePressed);
            number.AddChild(digit);
            row.AddChild(number);
            var text = new Label { Text = hint.Clues[i], AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            text.AddThemeFontSizeOverride("font_size", 15);
            row.AddChild(text);
            clues.AddChild(row);
        }

        content.AddChild(clues.GetParent());

        var thinking = Boxed("Worth thinking about");
        foreach (var candidate in hint.Candidates)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 14);
            var name = new Label { Text = candidate.Condition.Name, CustomMinimumSize = new Vector2(230, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
            name.AddThemeFontSizeOverride("font_size", 15);
            row.AddChild(name);
            var pips = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, CustomMinimumSize = new Vector2(0, 22), Alignment = BoxContainer.AlignmentMode.Center };
            pips.AddThemeConstantOverride("separation", 4);
            for (var i = 0; i < 4; i++)
            {
                var pip = new Panel { CustomMinimumSize = new Vector2(24, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
                pip.AddThemeStyleboxOverride("panel", UiStyle.Box(i < candidate.Strength ? UiStyle.Blue : new Color("#E1E7EC"), 5));
                pips.AddChild(pip);
            }

            row.AddChild(pips);
            var why = UiStyle.Caption(candidate.Reason, 14);
            why.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            why.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(why);
            thinking.AddChild(row);
        }

        content.AddChild(thinking.GetParent());

        foreach (var warning in hint.Warnings)
        {
            var note = new PanelContainer();
            note.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.AmberTint, 10, UiStyle.AmberBorder, 12));
            var label = new Label { Text = "Before you prescribe: " + warning, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            label.AddThemeFontSizeOverride("font_size", 15);
            label.AddThemeColorOverride("font_color", UiStyle.AmberText);
            note.AddChild(label);
            content.AddChild(note);
        }

        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 14);
        var promise = UiStyle.Caption($"Hints never choose for you. Use Decide for me on the panel if you want {_sim.PlayerDoctor?.Name ?? "the doctor"}'s own judgement instead.", 14);
        promise.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        promise.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bottom.AddChild(promise);
        var back = UiStyle.Button("Back to the patient", CloseHint, 200);
        back.FocusMode = Control.FocusModeEnum.None;
        bottom.AddChild(back);
        column.AddChild(bottom);

        PointerNeeded?.Invoke(true);
    }

    /// <summary>A bordered box with a small heading; returns the column to fill.</summary>
    private static VBoxContainer Boxed(string heading)
    {
        var box = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Surface, 12, UiStyle.Border, 14));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        column.AddChild(HudWidgets.Section(heading));
        box.AddChild(column);
        return column;
    }

    private static Control HintBox(string heading, IReadOnlyList<HintLine> lines)
    {
        var column = Boxed(heading);
        foreach (var line in lines)
        {
            var colour = line.Tone switch
            {
                HintTone.Bad => UiStyle.Red,
                HintTone.Good => UiStyle.Green,
                HintTone.Muted => UiStyle.TextMuted,
                _ => UiStyle.TextDark,
            };
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var label = new Label { Text = line.Label, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            label.AddThemeFontSizeOverride("font_size", 14);
            if (line.Value.Length == 0)
            {
                label.AddThemeColorOverride("font_color", colour);
            }

            row.AddChild(label);
            if (line.Value.Length > 0)
            {
                var value = new Label { Text = line.Value, HorizontalAlignment = HorizontalAlignment.Right };
                value.AddThemeFontSizeOverride("font_size", 14);
                value.AddThemeColorOverride("font_color", colour);
                row.AddChild(value);
            }

            column.AddChild(row);
        }

        return (Control)column.GetParent();
    }
}
