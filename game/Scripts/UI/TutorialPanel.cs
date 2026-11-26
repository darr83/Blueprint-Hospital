using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Core;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The tutorial's guide card: the step, what to do, a tick for each task done, and Back / Skip / Next. A pulsing ring
/// points at the next thing to click (a button, a build bar icon or tile, or the first patient). The same card works in
/// 2D and 3D: it only reads the HUD and the simulation. The suggested place to build is drawn on the map by
/// <see cref="TutorialMapHint"/>.
/// </summary>
public partial class TutorialPanel : Control
{
    private const float CardWidth = 372;

    /// <summary>A finished step stays on screen with its ticks this long before the next one shows.</summary>
    private const double DoneHold = 1.1;

    private HospitalScene _scene = null!;
    private TutorialGuide _guide = null!;
    private TutorialScreen _screen = null!;
    private readonly PanelContainer _card = new() { Name = "TutorialCard" };
    private readonly PanelContainer _pill = new() { Name = "TutorialPill", Visible = false };
    private readonly Label _pillText = new();
    private readonly Label _stepCaption = UiStyle.Caption(string.Empty, UiStyle.SizeCaption);
    private readonly Label _part = new();
    private readonly Label _title = UiStyle.Heading(string.Empty, UiStyle.SizeTitle);
    private readonly Label _text = UiStyle.Body(string.Empty);
    private readonly VBoxContainer _tasks = new();
    private readonly Label _tip = UiStyle.Body(string.Empty, muted: true);
    private readonly ProgressBar _progress = new() { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 6), MaxValue = 1 };
    private readonly HBoxContainer _buttons = new();
    private readonly HBoxContainer _confirm = new() { Visible = false };
    private Button _back = null!, _skip = null!, _next = null!, _keepPlaying = null!, _menu = null!;
    private readonly PointerRing _ring = new() { Name = "TutorialPointer" };
    private int _shownIndex = -1;
    private string _shownTasks = string.Empty;
    private double _doneTimer = -1;
    private double _clock;
    private int _shownSpotFor = -1;

    /// <summary>The guide behind the card (for the smoke test and review shots).</summary>
    public TutorialGuide Guide => _guide;

    /// <summary>The screen the tutorial reads (for the smoke test and review shots).</summary>
    public ITutorialScreen Screen => _screen;

    /// <summary>The card's area on screen (for the smoke test).</summary>
    public Rect2 CardRect => _card.GetGlobalRect();

    /// <summary>What the pulsing ring is pointing at, or null (for the smoke test).</summary>
    public string? PointingAt { get; private set; }

    /// <summary>True while the card is folded down to a small pill.</summary>
    public bool Minimised => _pill.Visible;

    public void Initialise(HospitalScene scene)
    {
        _scene = scene;
        _guide = new TutorialGuide(scene.Session.Simulation);
        _screen = new TutorialScreen(scene);
        Name = "Tutorial";
        AnchorRight = AnchorBottom = 1;
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = UiStyle.Theme;
        ProcessMode = ProcessModeEnum.Always;

        // A step part-way through the hospital (a loaded game) starts from where it got to.
        _guide.Update(_screen);

        AddChild(_ring);
        BuildCard();
        BuildPill();
        Refresh(force: true);
    }

    // ---------------------------------------------------------------- layout

    private void BuildCard()
    {
        _card.AddThemeStyleboxOverride("panel", UiStyle.Card(18));
        _card.CustomMinimumSize = new Vector2(CardWidth, 0);
        _card.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_card);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        _card.AddChild(column);

        // TUTORIAL · STEP 5 OF 19                    [–] [✕]
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 6);
        _stepCaption.AddThemeFontOverride("font", UiStyle.SemiBold);
        _stepCaption.AddThemeColorOverride("font_color", UiStyle.Blue);
        _stepCaption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _stepCaption.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        top.AddChild(_stepCaption);
        var fold = SmallButton("–", "Fold the tutorial away (it keeps going)", () => SetMinimised(true));
        top.AddChild(fold);
        top.AddChild(SmallButton("✕", "End the tutorial", () => _confirm.Visible = !_confirm.Visible));
        column.AddChild(top);

        // End the tutorial? [End tutorial] [Carry on]
        _confirm.AddThemeConstantOverride("separation", 8);
        var question = UiStyle.Body("End the tutorial? You can keep playing this hospital.");
        question.AddThemeFontSizeOverride("font_size", UiStyle.SizeSmall);
        question.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _confirm.AddChild(question);
        var end = UiStyle.Button("End", EndTutorial, 64);
        end.CustomMinimumSize = new Vector2(64, 32);
        _confirm.AddChild(end);
        var carryOn = UiStyle.SecondaryButton("Carry on", () => _confirm.Visible = false);
        carryOn.CustomMinimumSize = new Vector2(0, 32);
        _confirm.AddChild(carryOn);
        column.AddChild(_confirm);

        _progress.AddThemeStyleboxOverride("background", UiStyle.Box(UiStyle.Hairline, 3));
        _progress.AddThemeStyleboxOverride("fill", UiStyle.Box(UiStyle.Blue, 3));
        column.AddChild(_progress);

        // The part, as a small blue chip.
        var chip = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        var chipBox = UiStyle.Box(UiStyle.BlueTint, UiStyle.RadiusChip);
        chipBox.ContentMarginLeft = chipBox.ContentMarginRight = 8;
        chipBox.ContentMarginTop = chipBox.ContentMarginBottom = 2;
        chip.AddThemeStyleboxOverride("panel", chipBox);
        _part.AddThemeFontSizeOverride("font_size", UiStyle.SizeCaption);
        _part.AddThemeFontOverride("font", UiStyle.Medium);
        _part.AddThemeColorOverride("font_color", UiStyle.BluePressed);
        chip.AddChild(_part);
        column.AddChild(chip);

        _title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_title);
        _text.AddThemeColorOverride("font_color", UiStyle.TextDark);
        _text.CustomMinimumSize = new Vector2(CardWidth - 36, 0);
        column.AddChild(_text);

        _tasks.AddThemeConstantOverride("separation", 6);
        column.AddChild(_tasks);

        _tip.AddThemeFontSizeOverride("font_size", UiStyle.SizeSmall);
        _tip.CustomMinimumSize = new Vector2(CardWidth - 36, 0);
        column.AddChild(_tip);

        _buttons.AddThemeConstantOverride("separation", 8);
        _back = UiStyle.SecondaryButton("Back", () => { _guide.Back(); _doneTimer = -1; Refresh(force: true); });
        _skip = UiStyle.SecondaryButton("Skip step", () => { _guide.Skip(); _doneTimer = -1; Refresh(force: true); });
        _skip.TooltipText = "Move on without doing this step";
        _next = UiStyle.Button("Next", () => { _guide.Next(); _doneTimer = -1; Refresh(force: true); }, 96);
        _keepPlaying = UiStyle.Button("Keep playing", EndTutorial, 140);
        _menu = UiStyle.SecondaryButton("Main menu", () => GameManager.Instance.ReturnToMainMenu());
        foreach (var button in new[] { _back, _skip, _next, _keepPlaying, _menu })
        {
            button.FocusMode = FocusModeEnum.None;
            button.CustomMinimumSize = new Vector2(button.CustomMinimumSize.X, 36);
        }

        _buttons.AddChild(_back);
        _buttons.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        _buttons.AddChild(_menu);
        _buttons.AddChild(_skip);
        _buttons.AddChild(_next);
        _buttons.AddChild(_keepPlaying);
        column.AddChild(_buttons);
    }

    private void BuildPill()
    {
        var box = UiStyle.Box(UiStyle.Blue, 18);
        box.ContentMarginLeft = box.ContentMarginRight = 14;
        box.ContentMarginTop = box.ContentMarginBottom = 8;
        box.ShadowColor = new Color(0.09f, 0.14f, 0.18f, 0.2f);
        box.ShadowSize = 8;
        _pill.AddThemeStyleboxOverride("panel", box);
        _pill.MouseFilter = MouseFilterEnum.Stop;
        _pill.MouseDefaultCursorShape = CursorShape.PointingHand;
        _pill.TooltipText = "Open the tutorial";
        _pillText.AddThemeColorOverride("font_color", Colors.White);
        _pillText.AddThemeFontOverride("font", UiStyle.SemiBold);
        _pillText.AddThemeFontSizeOverride("font_size", UiStyle.SizeSmall);
        _pillText.MouseFilter = MouseFilterEnum.Ignore;
        _pill.AddChild(_pillText);
        _pill.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                SetMinimised(false);
            }
        };
        AddChild(_pill);
    }

    private static Button SmallButton(string text, string tooltip, Action pressed)
    {
        var button = UiStyle.SecondaryButton(text, pressed);
        button.CustomMinimumSize = new Vector2(28, 28);
        button.TooltipText = tooltip;
        button.FocusMode = FocusModeEnum.None;
        foreach (var state in new[] { "normal", "hover", "pressed" })
        {
            var box = UiStyle.Box(state == "hover" ? UiStyle.Ground : UiStyle.Surface, UiStyle.RadiusChip, UiStyle.Border);
            box.ContentMarginLeft = box.ContentMarginRight = 4;
            button.AddThemeStyleboxOverride(state, box);
        }

        return button;
    }

    /// <summary>
    /// Points the camera at the whole main building, sized to fit the space right of the card and above the build bar.
    /// </summary>
    public void FrameBuilding()
    {
        var b = Simulation.World.StarterSite.MainBuildingFootprint;
        var corners = new[] { IsoDraw.ToScreenGround(b.X, b.Y), IsoDraw.ToScreenGround(b.Right, b.Y), IsoDraw.ToScreenGround(b.X, b.Bottom), IsoDraw.ToScreenGround(b.Right, b.Bottom) };
        var min = new Vector2(corners.Min(c => c.X), corners.Min(c => c.Y));
        var max = new Vector2(corners.Max(c => c.X), corners.Max(c => c.Y));
        var screen = GetViewportRect().Size;
        var free = new Rect2(new Vector2(MainHUD.NavWidth + CardWidth + 36, MainHUD.TopBarHeight + 20), Vector2.Zero);
        free.End = new Vector2(screen.X - 80, screen.Y - BuildBar.Reserve - 20);
        var size = max - min;
        var zoom = Mathf.Clamp(Mathf.Min(free.Size.X / size.X, free.Size.Y / size.Y) * 0.92f, IsoCamera.MinZoom, IsoCamera.MaxZoom);
        var centre = (min + max) / 2;
        _scene.Camera.SnapTo(centre - ((free.GetCenter() - (screen / 2)) / zoom), zoom);
    }

    public void SetMinimised(bool minimised)
    {
        _pill.Visible = minimised;
        _card.Visible = !minimised;
        Refresh(force: true);
    }

    /// <summary>Ends the tutorial: the card goes, and the hospital carries on as it is.</summary>
    public void EndTutorial()
    {
        _scene.Session.Simulation.TutorialStep = TutorialGuide.Ended;
        _ring.Target = null;
        QueueFree();
        _scene.Session.Simulation.Notifications.Add(_scene.Session.Simulation.Now, Simulation.Simulation.NotificationLevel.Info,
            "Tutorial ended. The hospital is yours: patients keep coming as they did in the tutorial.");
    }

    // ---------------------------------------------------------------- each frame

    public override void _Process(double delta)
    {
        _clock += delta;
        _screen.Update();

        // A finished step shows its ticks for a moment, then the next step comes up.
        if (_guide.StepDone(_screen))
        {
            if (_doneTimer < 0)
            {
                _doneTimer = DoneHold;
            }

            _doneTimer -= delta;
            if (_doneTimer <= 0)
            {
                _doneTimer = -1;
                _guide.Update(_screen);
            }
        }
        else
        {
            _doneTimer = -1;
            _guide.Update(_screen);
        }

        Refresh(force: false);
        Place();
        PointAtNext();

        // Once the player has picked what to build, bring the suggested place into view, clear of the card and the pop-up.
        if (PointingAt == "map" && _guide.Current.Suggested is { } spot && _shownSpotFor != _guide.Index)
        {
            _shownSpotFor = _guide.Index;
            ShowSpot(spot);
        }
    }

    /// <summary>Glides the camera so <paramref name="spot"/> sits in the open part of the screen (zooming out if it won't fit).</summary>
    public void ShowSpot(Simulation.World.GridRect spot)
    {
        var corners = new[] { IsoDraw.ToScreenGround(spot.X, spot.Y), IsoDraw.ToScreenGround(spot.Right, spot.Y), IsoDraw.ToScreenGround(spot.X, spot.Bottom), IsoDraw.ToScreenGround(spot.Right, spot.Bottom) };
        var min = new Vector2(corners.Min(c => c.X), corners.Min(c => c.Y));
        var max = new Vector2(corners.Max(c => c.X), corners.Max(c => c.Y));
        var screen = GetViewportRect().Size;
        var bottom = _scene.Hud.Build.Visible && _scene.Hud.Build.PopupOpen ? _scene.Hud.Build.PopupRect.Position.Y : screen.Y - BuildBar.Reserve;
        var open = new Rect2(new Vector2(_card.Visible ? _card.GetGlobalRect().End.X + 24 : MainHUD.NavWidth + 24, MainHUD.TopBarHeight + 24), Vector2.Zero);
        open.End = new Vector2(screen.X - 80, Mathf.Max(open.Position.Y + 120, bottom - 24));
        var zoom = _scene.Camera.Zoom.X;
        var size = max - min;
        var fits = Mathf.Min(open.Size.X / Mathf.Max(size.X, 1), open.Size.Y / Mathf.Max(size.Y, 1)) * 0.9f;
        var target = (min + max) / 2;
        if (zoom > fits)
        {
            zoom = Mathf.Max(fits, IsoCamera.MinZoom);
            _scene.Camera.SnapTo(target - ((open.GetCenter() - (screen / 2)) / zoom), zoom);
        }
        else
        {
            _scene.Camera.GlideTo(target - ((open.GetCenter() - (screen / 2)) / zoom));
        }
    }

    /// <summary>Puts the card at the top left, beside the A&amp;E board when it is showing.</summary>
    private void Place()
    {
        var left = MainHUD.NavWidth + 12;
        if (_scene.Hud.BoardRect is { } board && board.Size.X > 0)
        {
            left = board.End.X + 12;
        }

        var top = MainHUD.TopBarHeight + 12;
        _card.Size = new Vector2(CardWidth, 0);
        _card.Position = new Vector2(left, top);
        _pill.Size = Vector2.Zero;
        _pill.Position = new Vector2(left, top);
    }

    private void Refresh(bool force)
    {
        var step = _guide.Current;
        var states = _guide.TaskStates(_screen);
        var key = string.Join("|", states.Select(s => s.Done ? "1" : "0"));
        _progress.Value = _guide.Steps.Count <= 1 ? 1 : (double)_guide.Index / (_guide.Steps.Count - 1);
        _pillText.Text = _guide.Finished ? "Tutorial: done!  Click to open" : $"Tutorial  {_guide.Index + 1}/{_guide.Steps.Count}: {step.Title}";
        if (!force && _shownIndex == _guide.Index && key == _shownTasks)
        {
            return;
        }

        _shownIndex = _guide.Index;
        _shownTasks = key;
        _stepCaption.Text = $"TUTORIAL · STEP {_guide.Index + 1} OF {_guide.Steps.Count}";
        _part.Text = step.Part;
        _title.Text = step.Title;
        _text.Text = step.Text;

        foreach (var child in _tasks.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var (task, done) in states)
        {
            _tasks.AddChild(TaskRow(task.Text, done));
        }

        _tasks.Visible = states.Count > 0;
        _tip.Text = step.Tip is { } tip ? "Tip: " + tip : string.Empty;
        _tip.Visible = step.Tip is not null;

        var finished = _guide.Finished;
        _back.Visible = _guide.Index > 0 && !finished;
        _skip.Visible = states.Count > 0 && !finished;
        _next.Visible = states.Count == 0 && !finished;
        _next.Text = _guide.Index == 0 ? "Let's start" : "Next";
        _keepPlaying.Visible = finished;
        _menu.Visible = finished;
    }

    private static Control TaskRow(string text, bool done)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var mark = new PanelContainer { CustomMinimumSize = new Vector2(20, 20), SizeFlagsVertical = SizeFlags.ShrinkBegin };
        var box = done ? UiStyle.Box(UiStyle.Green, 10) : UiStyle.Box(UiStyle.Surface, 10, UiStyle.FieldBorder);
        box.BorderWidthBottom = box.BorderWidthTop = box.BorderWidthLeft = box.BorderWidthRight = done ? 0 : 2;
        mark.AddThemeStyleboxOverride("panel", box);
        if (done)
        {
            var tick = new Label { Text = "✓", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            tick.AddThemeColorOverride("font_color", Colors.White);
            tick.AddThemeFontSizeOverride("font_size", 13);
            tick.AddThemeFontOverride("font", UiStyle.Bold);
            mark.AddChild(tick);
        }

        row.AddChild(mark);
        var label = UiStyle.Body(text);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        label.CustomMinimumSize = new Vector2(CardWidth - 76, 0);
        label.AddThemeFontSizeOverride("font_size", UiStyle.SizeSmall + 1);
        label.AddThemeColorOverride("font_color", done ? UiStyle.Green : UiStyle.TextDark);
        row.AddChild(label);
        return row;
    }

    // ---------------------------------------------------------------- pointing at the next thing to click

    private void PointAtNext()
    {
        var hud = _scene.Hud;
        var id = _guide.StepDone(_screen) ? null : _guide.Current.Pointer(_scene.Session.Simulation, _screen);
        PointingAt = id;
        Rect2? target = null;
        var round = false;
        if (id is not null && hud.Visible)
        {
            var colon = id.IndexOf(':');
            var (kind, arg) = colon < 0 ? (id, string.Empty) : (id[..colon], id[(colon + 1)..]);
            Control? control = kind switch
            {
                "nav" => hud.NavButtonFor(arg),
                "bar" => hud.Build.Visible ? hud.Build.IconFor(arg) : null,
                "tile" => hud.Build.Visible ? hud.Build.TileFor(arg) : null,
                "mode" => hud.ModeButtonFor(Enum.Parse<GameMode>(arg)),
                "speed" => hud.SpeedButtonFor(Enum.Parse<SimulationSpeed>(arg)),
                "button" when arg == "hire_team" && hud.Recruitment.Visible => hud.Recruitment.HireTeamButton,
                _ => null,
            };

            if (control is not null && control.IsVisibleInTree())
            {
                target = control.GetGlobalRect();
            }
            else if (kind == "patient" && _guide.FirstPatient is { } patient && _scene.World.IsOnMap(patient) && _scene.World.OnViewFloor(patient))
            {
                var foot = GetViewport().GetCanvasTransform() * _scene.World.FootPosition(patient);
                var zoom = GetViewport().GetCanvasTransform().Scale.Y;
                var height = (IsoDraw.Plan ? 22 : 70) * zoom;
                target = new Rect2(foot.X - (height * 0.45f), foot.Y - height, height * 0.9f, height * 1.1f);
                round = true;
            }
        }

        _ring.Target = target;
        _ring.Round = round;
        _ring.Phase = (float)_clock;
    }

    /// <summary>The pulsing ring drawn round whatever to click next.</summary>
    private sealed partial class PointerRing : Control
    {
        public Rect2? Target { get; set; }

        public bool Round { get; set; }

        public float Phase { get; set; }

        public PointerRing()
        {
            AnchorRight = AnchorBottom = 1;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Process(double delta) => QueueRedraw();

        public override void _Draw()
        {
            if (Target is not { } rect)
            {
                return;
            }

            var pulse = (Mathf.Sin(Phase * 5f) + 1f) / 2f;
            var grow = 4 + (pulse * 5);
            var r = rect.Grow(grow);
            var colour = new Color(UiStyle.Blue, 0.55f + (0.45f * pulse));
            if (Round)
            {
                var centre = r.GetCenter();
                DrawArc(centre, Mathf.Max(r.Size.X, r.Size.Y) / 2, 0, Mathf.Tau, 40, colour, 3f, true);
            }
            else
            {
                var box = UiStyle.Box(new Color(0, 0, 0, 0), 10);
                box.BorderColor = colour;
                box.BorderWidthBottom = box.BorderWidthTop = box.BorderWidthLeft = box.BorderWidthRight = 3;
                box.AntiAliasing = true;
                DrawStyleBox(box, r);
            }

            // A small "Click here" tag beside it, kept on screen.
            var font = UiStyle.SemiBold;
            const int size = 13;
            const string text = "Click here";
            var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X + 16;
            var screen = GetViewportRect().Size;
            var at = r.GetCenter().Y > screen.Y / 2
                ? new Vector2(r.GetCenter().X - (width / 2), r.Position.Y - 30)
                : new Vector2(r.End.X + 8, r.GetCenter().Y - 12);
            if (at.X + width > screen.X - 8)
            {
                at = new Vector2(r.Position.X - width - 8, at.Y);
            }

            if (at.Y + 24 > screen.Y - 4)
            {
                at = new Vector2(r.GetCenter().X - (width / 2), r.Position.Y - 32);
            }

            DrawStyleBox(UiStyle.Box(UiStyle.Blue, 8), new Rect2(at, new Vector2(width, 24)));
            DrawString(font, at + new Vector2(8, 17), text, HorizontalAlignment.Left, -1, size, Colors.White);
        }
    }

    /// <summary>The screen as the tutorial sees it, read from the hospital scene each frame.</summary>
    private sealed class TutorialScreen : ITutorialScreen
    {
        private readonly HospitalScene _scene;
        private readonly Vector2 _startPosition;
        private readonly float _startZoom;
        private readonly int _startRotation;

        public TutorialScreen(HospitalScene scene)
        {
            _scene = scene;
            _startPosition = scene.Camera.Position;
            _startZoom = scene.Camera.Zoom.X;
            _startRotation = IsoDraw.Rotation;
        }

        public bool BuildMode => _scene.Mode == GameMode.Build;

        public string? OpenBuildIcon => BuildMode && _scene.Hud.Build.PopupOpen ? _scene.Hud.Build.OpenIcon : null;

        public string? BuildItem => BuildMode ? _scene.Hud.Build.SelectedItem : null;

        public bool RecruitmentOpen => _scene.Hud.Recruitment.Visible;

        public bool PatientRecordOpen => _scene.Hud.Record.Visible;

        public bool CameraMoved { get; private set; }

        public bool CameraZoomed { get; private set; }

        public bool CameraTurned { get; private set; }

        public bool ClockRunning => !_scene.Session.Clock.IsPaused;

        /// <summary>Notes the view having moved, zoomed or turned (once done, it stays done).</summary>
        public void Update()
        {
            var zoom = _scene.Camera.Zoom.X;
            CameraMoved |= _scene.Camera.Position.DistanceTo(_startPosition) > 120 / Mathf.Max(zoom, 0.1f);
            CameraZoomed |= Mathf.Abs(zoom - _startZoom) > _startZoom * 0.15f;
            CameraTurned |= IsoDraw.Rotation != _startRotation;
        }
    }
}

/// <summary>
/// The tutorial's suggested place to build, on the map: a pulsing blue outline with its name. Drawn with the same
/// ground projection as the build tool, so it sits on the floor in both 2D and 3D, at every turn of the view.
/// </summary>
public partial class TutorialMapHint : Node2D
{
    private TutorialPanel? _panel;
    private double _clock;

    public void Initialise(TutorialPanel panel)
    {
        _panel = panel;
        Name = "TutorialMapHint";
        ZIndex = 40;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(_panel))
        {
            QueueFree();
            return;
        }

        _clock += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_panel is null || !IsInstanceValid(_panel) || _panel.Guide.StepDone(_panel.Screen) || _panel.Guide.Current.Suggested is not { } rect || IsoDraw.ViewFloor != 0)
        {
            return;
        }

        var pulse = (float)((Math.Sin(_clock * 4) + 1) / 2);
        var blue = UiStyle.Blue;
        IsoDraw.FloorQuad(this, rect.X, rect.Y, rect.Width, rect.Length, new Color(blue, 0.10f + (0.10f * pulse)));
        IsoDraw.FloorOutline(this, rect.X, rect.Y, rect.Width, rect.Length, new Color(blue, 0.6f + (0.4f * pulse)), 3f);

        if (_panel.Guide.Current.SuggestedLabel is { } label)
        {
            var zoom = Math.Max(0.2f, GetViewport().GetCanvasTransform().X.X);
            var centre = IsoDraw.ToScreen(rect.X + (rect.Width / 2.0), rect.Y + (rect.Length / 2.0));
            var font = UiStyle.SemiBold;
            var size = (int)Math.Round(14 / zoom);
            var width = font.GetStringSize(label, HorizontalAlignment.Left, -1, size).X;
            var pad = 8 / zoom;
            var box = new Rect2(centre - new Vector2((width / 2) + pad, size * 0.95f), new Vector2(width + (2 * pad), size * 1.5f));
            DrawStyleBox(UiStyle.Box(new Color(blue, 0.92f), (int)Math.Round(8 / zoom)), box);
            DrawString(font, new Vector2(centre.X - (width / 2), centre.Y + (size * 0.25f)), label, HorizontalAlignment.Left, -1, size, Colors.White);
        }
    }
}
