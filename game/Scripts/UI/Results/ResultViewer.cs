using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Medical.Results;
using BlueprintHospital.Simulation.Patients;
using Godot;

namespace BlueprintHospital.UI.Results;

/// <summary>
/// A test result opened as a document. Blood tests, bedside tests and ECGs show as a white hospital report sheet
/// (values against their ranges, abnormal ones flagged); X-rays, scans, echo, EEG and endoscopy open in a dark
/// imaging viewer with zoom, brightness and the finding marked. Closes with Escape, the close button or a click
/// outside. Other results for the same patient can be opened from inside it.
/// </summary>
public partial class ResultViewer : Control
{
    private static readonly Color DarkGround = new("#0E1318");
    private static readonly Color DarkPanel = new("#161D24");
    private static readonly Color DarkLine = new("#2B3946");
    private static readonly Color DarkText = new("#E6ECF1");
    private static readonly Color DarkMuted = new("#8FA0AE");

    private readonly GameDatabase _db;
    private readonly Patient _patient;
    private readonly string _hospital;
    private readonly bool _asDoctor;
    private readonly PanelContainer _panel = new();
    private InvestigationOrder _order;
    private Input.MouseModeEnum _mouseBefore;
    private bool _closed;

    private PictureView? _picture;
    private TraceView? _trace;
    private Control? _sheetBody;

    /// <summary>Whether finding markers are drawn, remembered for the session. Off at first in Be A Doctor.</summary>
    public static bool MarkFindings { get; set; } = true;

    /// <summary>The same choice while playing as a doctor, where finding the problem yourself is the point.</summary>
    public static bool MarkFindingsAsDoctor { get; set; }

    /// <summary>True while a result is open, so the camera and the doctor do not move under it.</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>The viewer open now, if any, so a second click replaces it rather than stacking.</summary>
    public static ResultViewer? Current { get; private set; }

    private ResultViewer(GameDatabase db, Patient patient, InvestigationOrder order, string hospital, bool asDoctor)
    {
        _db = db;
        _patient = patient;
        _order = order;
        _hospital = string.IsNullOrWhiteSpace(hospital) ? "Blueprint Hospital" : hospital;
        _asDoctor = asDoctor;
        Name = "ResultViewer";
        Theme = UiStyle.Theme;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;

        var dim = new ColorRect { Color = new Color(0.05f, 0.08f, 0.11f, 0.62f), AnchorRight = 1, AnchorBottom = 1 };
        dim.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                Close();
            }
        };
        AddChild(dim);

        _panel.AnchorLeft = 0.5f;
        _panel.AnchorRight = 0.5f;
        _panel.AnchorTop = 0.5f;
        _panel.AnchorBottom = 0.5f;
        AddChild(_panel);
    }

    /// <summary>Opens <paramref name="order"/> for <paramref name="patient"/> over everything else on <paramref name="parent"/>.</summary>
    public static ResultViewer? Open(Node parent, GameDatabase db, Patient patient, InvestigationOrder order, string hospital, bool asDoctor = false)
    {
        if (order.Status != InvestigationStatus.Resulted || !db.Investigations.ContainsKey(order.InvestigationId))
        {
            return null;
        }

        Current?.Close();
        // Its own layer, above the hospital HUD and the Be A Doctor HUD alike.
        var layer = new CanvasLayer { Name = "ResultLayer", Layer = 60, ProcessMode = ProcessModeEnum.Always };
        var viewer = new ResultViewer(db, patient, order, hospital, asDoctor);
        layer.AddChild(viewer);
        parent.AddChild(layer);
        return viewer;
    }

    public override void _Ready()
    {
        _mouseBefore = Input.MouseMode;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        IsOpen = true;
        Current = this;
        GetViewport().SizeChanged += Fit;
        Show(_order);
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Fit;
        if (Current == this)
        {
            Current = null;
            IsOpen = false;
            Input.MouseMode = _mouseBefore;
        }
    }

    /// <summary>Raised when the viewer closes.</summary>
    public event Action? Closed;

    public InvestigationOrder Order => _order;

    public ResultDocument? Document { get; private set; }

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        Closed?.Invoke();
        if (GetParent() is CanvasLayer layer && layer.Name == "ResultLayer")
        {
            layer.QueueFree();
        }
        else
        {
            QueueFree();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Close();
        }

        // Nothing typed while reading a result should reach the hospital or the doctor underneath.
        if (@event is InputEventKey)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    private bool Markers
    {
        get => _asDoctor ? MarkFindingsAsDoctor : MarkFindings;
        set
        {
            if (_asDoctor)
            {
                MarkFindingsAsDoctor = value;
            }
            else
            {
                MarkFindings = value;
            }
        }
    }

    private IEnumerable<InvestigationOrder> Others => _patient.Investigations.Where(i => i.Status == InvestigationStatus.Resulted);

    /// <summary>Shows another of this patient's results in the same viewer.</summary>
    public void Show(InvestigationOrder order)
    {
        _order = order;
        var test = _db.Investigations[order.InvestigationId];
        Document = ResultDocument.Build(test, _patient, order);
        foreach (var child in _panel.GetChildren())
        {
            _panel.RemoveChild(child);
            child.QueueFree();
        }

        _picture = null;
        _trace = null;
        _sheetBody = null;
        if (Document is null)
        {
            return;
        }

        if (Document.Layout == ResultLayout.Imaging)
        {
            BuildImaging(Document);
        }
        else
        {
            BuildSheet(Document);
        }

        Fit();
        Callable.From(Fit).CallDeferred();
    }

    private void Fit()
    {
        var screen = GetViewportRect().Size;
        var imaging = Document?.Layout == ResultLayout.Imaging;
        var width = Mathf.Min(screen.X - 48, imaging ? 1320 : 980);
        var height = Mathf.Min(screen.Y - 48, imaging ? 820 : 900);
        if (!imaging && _sheetBody is not null)
        {
            // A short sheet (a few blood values) shrinks to fit rather than leaving a white gap.
            var want = _panel.GetCombinedMinimumSize().Y + _sheetBody.GetCombinedMinimumSize().Y + 40;
            height = Mathf.Min(height, Mathf.Max(360, want));
        }
        _panel.OffsetLeft = -width / 2;
        _panel.OffsetRight = width / 2;
        _panel.OffsetTop = -height / 2;
        _panel.OffsetBottom = height / 2;
    }

    // ---------------------------------------------------------------- report sheet

    private void BuildSheet(ResultDocument doc)
    {
        _panel.AddThemeStyleboxOverride("panel", UiStyle.Card(0, 14));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        _panel.AddChild(column);

        // Header band: hospital and department, the test, close.
        var header = new PanelContainer();
        var band = UiStyle.Box(UiStyle.Surface, 0, null, 0);
        band.ContentMarginLeft = band.ContentMarginRight = 24;
        band.ContentMarginTop = 18;
        band.ContentMarginBottom = 14;
        band.CornerRadiusTopLeft = band.CornerRadiusTopRight = 14;
        band.BorderColor = UiStyle.Blue;
        band.BorderWidthBottom = 3;
        header.AddThemeStyleboxOverride("panel", band);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 14);
        header.AddChild(top);
        top.AddChild(Logo());
        var who = new VBoxContainer();
        who.AddThemeConstantOverride("separation", 0);
        who.AddChild(UiStyle.Heading(_hospital, 20));
        who.AddChild(UiStyle.Caption(doc.Department, 14));
        who.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(who);
        var what = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        what.AddThemeConstantOverride("separation", 0);
        var title = UiStyle.Heading(doc.Test.Name, 20);
        title.HorizontalAlignment = HorizontalAlignment.Right;
        what.AddChild(title);
        var status = UiStyle.Caption(doc.Abnormal ? "Final report · ABNORMAL" : "Final report · normal", 13);
        status.HorizontalAlignment = HorizontalAlignment.Right;
        status.AddThemeColorOverride("font_color", doc.Abnormal ? UiStyle.Red : UiStyle.Green);
        what.AddChild(status);
        top.AddChild(what);
        top.AddChild(CloseButton(false));
        column.AddChild(header);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(scroll);
        var pad = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        pad.AddThemeConstantOverride("margin_left", 24);
        pad.AddThemeConstantOverride("margin_right", 24);
        pad.AddThemeConstantOverride("margin_top", 16);
        pad.AddThemeConstantOverride("margin_bottom", 16);
        scroll.AddChild(pad);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 14);
        pad.AddChild(body);

        body.AddChild(PatientStrip(false));

        if (doc.Rows.Count > 0)
        {
            body.AddChild(ValuesTable(doc));
        }

        if (doc.PictureId is { } id)
        {
            if (ResultPictures.KindOf(id) == PictureKind.Image)
            {
                var frame = new PanelContainer();
                frame.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Ground, 10, UiStyle.Border, 8));
                _picture = new PictureView { CustomMinimumSize = new Vector2(0, 220), Backdrop = UiStyle.Ground, ShowMarkers = Markers, MouseFilter = MouseFilterEnum.Ignore };
                _picture.SetPicture(id, doc.Seed);
                frame.AddChild(_picture);
                body.AddChild(frame);
            }
            else
            {
                // An ECG report reads its interpretation first, then the trace.
                body.AddChild(ConclusionBox(doc, false));
                _trace = new TraceView { ShowMarkers = Markers, MouseFilter = MouseFilterEnum.Ignore };
                _trace.SetTrace(id, doc.Seed);
                _trace.Resized += () => _trace.CustomMinimumSize = new Vector2(0, Mathf.Round(_trace.Size.X * 0.44f));
                body.AddChild(_trace);
                if (_trace.Measurements.Count > 0)
                {
                    var measures = UiStyle.Caption(string.Join("   ·   ", _trace.Measurements), 14);
                    measures.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    body.AddChild(measures);
                }

                if (_trace.Statement.Length > 0)
                {
                    var statement = UiStyle.Body("Machine reading: " + _trace.Statement, muted: true);
                    body.AddChild(statement);
                }
            }

            body.AddChild(MarkerToggle(false));
        }

        if (_trace is null)
        {
            body.AddChild(ConclusionBox(doc, false));
        }

        body.AddChild(AlsoForPatient(false));
        _sheetBody = body;

        // Footer.
        var footer = new PanelContainer();
        var foot = UiStyle.Box(UiStyle.Ground, 0, null, 0);
        foot.ContentMarginLeft = foot.ContentMarginRight = 24;
        foot.ContentMarginTop = foot.ContentMarginBottom = 10;
        foot.CornerRadiusBottomLeft = foot.CornerRadiusBottomRight = 14;
        footer.AddThemeStyleboxOverride("panel", foot);
        var row = new HBoxContainer();
        footer.AddChild(row);
        var reported = UiStyle.Caption($"Reported {doc.Order.ResultedAt:dd MMM yyyy HH:mm} · {doc.Department} · {_hospital}", 13);
        reported.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(reported);
        row.AddChild(UiStyle.SecondaryButton("Close", Close, 110));
        column.AddChild(footer);
    }

    private static Control Logo()
    {
        var badge = new PanelContainer { CustomMinimumSize = new Vector2(44, 44) };
        badge.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Blue, 10));
        var plus = new Label { Text = "+", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        plus.AddThemeFontSizeOverride("font_size", 30);
        plus.AddThemeColorOverride("font_color", Colors.White);
        badge.AddChild(plus);
        return badge;
    }

    /// <summary>Name, hospital number, date of birth, sex, and when the test was requested and reported.</summary>
    private Control PatientStrip(bool dark)
    {
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", dark ? UiStyle.Box(DarkPanel, 8, DarkLine, 10) : UiStyle.Box(UiStyle.Ground, 10, UiStyle.Border, 12));
        var grid = new GridContainer { Columns = dark ? 2 : 3 };
        grid.AddThemeConstantOverride("h_separation", 28);
        grid.AddThemeConstantOverride("v_separation", 6);
        box.AddChild(grid);
        void Field(string label, string value)
        {
            var cell = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            cell.AddThemeConstantOverride("separation", -2);
            var l = UiStyle.Caption(label.ToUpperInvariant(), 11);
            var v = new Label { Text = value };
            v.AddThemeFontSizeOverride("font_size", 15);
            v.AddThemeColorOverride("font_color", dark ? DarkText : UiStyle.TextDark);
            if (dark)
            {
                l.AddThemeColorOverride("font_color", DarkMuted);
            }

            cell.AddChild(l);
            cell.AddChild(v);
            grid.AddChild(cell);
        }

        Field("Patient", _patient.FullName);
        Field("Hospital no.", _patient.HospitalNumber);
        Field("Born", $"{_patient.DateOfBirth:dd MMM yyyy} ({_patient.Age})");
        Field("Sex", _patient.Sex == Sex.Female ? "Female" : "Male");
        Field("Requested", $"{_order.RequestedAt:dd MMM HH:mm}");
        Field("Reported", _order.ResultedAt is { } r ? $"{r:dd MMM HH:mm}" : "-");
        return box;
    }

    private static Control ValuesTable(ResultDocument doc)
    {
        var table = new VBoxContainer();
        table.AddThemeConstantOverride("separation", 0);
        var grid = new GridContainer { Columns = 6 };
        grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", 8);
        string[] heads = { "Test", "Result", "", "Units", "Reference range", "" };
        foreach (var h in heads)
        {
            var c = UiStyle.Caption(h.ToUpperInvariant(), 11);
            grid.AddChild(c);
        }

        foreach (var r in doc.Rows)
        {
            var colour = r.Flag switch { "H" or "*" => UiStyle.Red, "L" => UiStyle.Blue, _ => UiStyle.TextDark };
            var name = new Label { Text = r.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            name.AddThemeColorOverride("font_color", UiStyle.TextDark);
            grid.AddChild(name);
            var value = new Label { Text = r.Value, HorizontalAlignment = HorizontalAlignment.Right, CustomMinimumSize = new Vector2(70, 0) };
            value.AddThemeColorOverride("font_color", colour);
            value.AddThemeFontSizeOverride("font_size", r.IsAbnormal ? 16 : 15);
            if (r.IsAbnormal)
            {
                value.AddThemeConstantOverride("outline_size", 1);
                value.AddThemeColorOverride("font_outline_color", colour);
            }

            grid.AddChild(value);
            var flag = new Label { Text = r.Flag, CustomMinimumSize = new Vector2(16, 0) };
            flag.AddThemeColorOverride("font_color", colour);
            grid.AddChild(flag);
            grid.AddChild(UiStyle.Caption(r.Unit ?? string.Empty, 14));
            grid.AddChild(UiStyle.Caption(r.RangeText, 14));
            grid.AddChild(r.Numeric is null ? new Control() : new RangeBar(r.Numeric, r.Low, r.High, r.Flag) { SizeFlagsVertical = SizeFlags.ShrinkCenter });
        }

        table.AddChild(grid);
        var key = UiStyle.Caption("H above the reference range · L below it · * abnormal finding", 12);
        key.AddThemeConstantOverride("line_spacing", 0);
        var keyPad = new MarginContainer();
        keyPad.AddThemeConstantOverride("margin_top", 8);
        keyPad.AddChild(key);
        table.AddChild(keyPad);
        return table;
    }

    private Control ConclusionBox(ResultDocument doc, bool dark)
    {
        var box = new PanelContainer();
        var bg = dark
            ? (doc.Abnormal ? new Color("#3A2A12") : new Color("#16301F"))
            : (doc.Abnormal ? UiStyle.AmberTint : new Color("#EAF5EE"));
        var border = dark
            ? (doc.Abnormal ? new Color("#8A5A12") : new Color("#2E6B47"))
            : (doc.Abnormal ? UiStyle.AmberBorder : new Color("#B6DCC4"));
        box.AddThemeStyleboxOverride("panel", UiStyle.Box(bg, 10, border, 14));
        var inner = new VBoxContainer();
        inner.AddThemeConstantOverride("separation", 4);
        box.AddChild(inner);
        var head = UiStyle.Caption(doc.Abnormal ? "CONCLUSION · ABNORMAL" : "CONCLUSION · NORMAL", 12);
        head.AddThemeColorOverride("font_color", dark ? (doc.Abnormal ? new Color("#FFC765") : new Color("#7FD6A0")) : (doc.Abnormal ? UiStyle.AmberText : UiStyle.Green));
        inner.AddChild(head);
        var text = UiStyle.Body(doc.Conclusion);
        text.AddThemeFontSizeOverride("font_size", 16);
        text.AddThemeColorOverride("font_color", dark ? DarkText : UiStyle.TextDark);
        inner.AddChild(text);
        return box;
    }

    /// <summary>Buttons for the patient's other results, so they can be read one after another.</summary>
    private Control AlsoForPatient(bool dark)
    {
        var others = Others.Where(o => o != _order).ToList();
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        if (others.Count == 0)
        {
            return column;
        }

        var head = UiStyle.Caption("ALSO FOR THIS PATIENT", 11);
        if (dark)
        {
            head.AddThemeColorOverride("font_color", DarkMuted);
        }

        column.AddChild(head);
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 8);
        flow.AddThemeConstantOverride("v_separation", 8);
        foreach (var o in others)
        {
            var name = _db.Investigations[o.InvestigationId].Name;
            var chip = UiStyle.SecondaryButton((o.Abnormal ? "● " : "") + name, () => Show(o));
            chip.CustomMinimumSize = new Vector2(0, 34);
            chip.TooltipText = o.Summary;
            if (o.Abnormal)
            {
                chip.AddThemeColorOverride("font_color", UiStyle.Red);
            }

            flow.AddChild(chip);
        }

        column.AddChild(flow);
        return column;
    }

    private Control MarkerToggle(bool dark)
    {
        var check = new CheckBox { Text = "Mark the finding", ButtonPressed = Markers, TooltipText = "Circle what the result shows", SizeFlagsHorizontal = SizeFlags.ShrinkBegin, FocusMode = FocusModeEnum.None };
        foreach (var style in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
        {
            check.AddThemeStyleboxOverride(style, new StyleBoxEmpty());
        }

        foreach (var colour in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
        {
            check.AddThemeColorOverride(colour, UiStyle.TextDark);
        }

        check.AddThemeFontSizeOverride("font_size", 14);
        if (dark)
        {
            check.AddThemeColorOverride("font_color", DarkText);
            check.AddThemeColorOverride("font_hover_color", Colors.White);
            check.AddThemeColorOverride("font_pressed_color", DarkText);
            check.AddThemeColorOverride("font_hover_pressed_color", Colors.White);
        }

        check.Toggled += on =>
        {
            Markers = on;
            if (_picture is not null)
            {
                _picture.ShowMarkers = on;
                _picture.QueueRedraw();
            }

            if (_trace is not null)
            {
                _trace.ShowMarkers = on;
                _trace.QueueRedraw();
            }
        };
        return check;
    }

    private Button CloseButton(bool dark)
    {
        var close = new Button { Text = "✕", CustomMinimumSize = new Vector2(40, 40), TooltipText = "Close (Esc)", Flat = true };
        close.AddThemeFontSizeOverride("font_size", 20);
        close.AddThemeColorOverride("font_color", dark ? DarkText : UiStyle.TextMuted);
        close.AddThemeColorOverride("font_hover_color", dark ? Colors.White : UiStyle.TextDark);
        close.Pressed += Close;
        return close;
    }

    // ---------------------------------------------------------------- imaging viewer

    private void BuildImaging(ResultDocument doc)
    {
        _panel.AddThemeStyleboxOverride("panel", UiStyle.Box(DarkGround, 14, DarkLine, 0));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        _panel.AddChild(column);

        // Top bar.
        var bar = new PanelContainer();
        var barBox = UiStyle.Box(DarkPanel, 0, null, 0);
        barBox.CornerRadiusTopLeft = barBox.CornerRadiusTopRight = 14;
        barBox.ContentMarginLeft = barBox.ContentMarginRight = 18;
        barBox.ContentMarginTop = barBox.ContentMarginBottom = 10;
        barBox.BorderColor = DarkLine;
        barBox.BorderWidthBottom = 1;
        bar.AddThemeStyleboxOverride("panel", barBox);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 16);
        bar.AddChild(top);
        var title = new Label { Text = doc.Test.Name };
        title.AddThemeFontSizeOverride("font_size", 19);
        title.AddThemeColorOverride("font_color", Colors.White);
        top.AddChild(title);
        var who = new Label { Text = $"{_patient.FullName} · {_patient.HospitalNumber} · {_patient.Age}{(_patient.Sex == Sex.Female ? "F" : "M")} · {doc.Order.ResultedAt:dd MMM yyyy HH:mm}", SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
        who.AddThemeColorOverride("font_color", DarkMuted);
        who.AddThemeFontSizeOverride("font_size", 14);
        who.ClipText = true;
        top.AddChild(who);
        var dept = new Label { Text = doc.Department, VerticalAlignment = VerticalAlignment.Center };
        dept.AddThemeColorOverride("font_color", DarkMuted);
        dept.AddThemeFontSizeOverride("font_size", 14);
        top.AddChild(dept);
        top.AddChild(CloseButton(true));
        column.AddChild(bar);

        var main = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        main.AddThemeConstantOverride("separation", 0);
        column.AddChild(main);

        main.AddChild(StudyList());

        // Centre: tools and the picture.
        var centre = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        centre.AddThemeConstantOverride("separation", 0);
        main.AddChild(centre);
        var tools = new PanelContainer();
        var toolBox = UiStyle.Box(DarkGround, 0, null, 0);
        toolBox.ContentMarginLeft = toolBox.ContentMarginRight = 12;
        toolBox.ContentMarginTop = toolBox.ContentMarginBottom = 8;
        tools.AddThemeStyleboxOverride("panel", toolBox);
        var toolRow = new HBoxContainer();
        toolRow.AddThemeConstantOverride("separation", 6);
        tools.AddChild(toolRow);
        centre.AddChild(tools);

        var id = doc.PictureId!;
        var view = new MarginContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        view.AddThemeConstantOverride("margin_left", 8);
        view.AddThemeConstantOverride("margin_right", 8);
        view.AddThemeConstantOverride("margin_bottom", 8);
        centre.AddChild(view);
        if (ResultPictures.KindOf(id) == PictureKind.Image)
        {
            _picture = new PictureView { ShowMarkers = Markers, SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true };
            _picture.SetPicture(id, doc.Seed);
            _picture.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true } wheel)
                {
                    if (wheel.ButtonIndex == MouseButton.WheelUp)
                    {
                        ZoomBy(1.15f);
                    }
                    else if (wheel.ButtonIndex == MouseButton.WheelDown)
                    {
                        ZoomBy(1 / 1.15f);
                    }
                }
            };
            view.AddChild(_picture);
            toolRow.AddChild(Tool("Zoom −", () => ZoomBy(1 / 1.25f)));
            toolRow.AddChild(Tool("Zoom +", () => ZoomBy(1.25f)));
            toolRow.AddChild(Tool("Darker", () => Brighten(-0.12f)));
            toolRow.AddChild(Tool("Brighter", () => Brighten(0.12f)));
            toolRow.AddChild(Tool("Reset", () =>
            {
                _picture.Zoom = 1;
                _picture.Brightness = 1;
                _picture.QueueRedraw();
            }));
        }
        else
        {
            var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            var paper = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            paper.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color("#F7F9FB"), 6, null, 10));
            _trace = new TraceView { ShowMarkers = Markers, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            _trace.SetTrace(id, doc.Seed);
            paper.AddChild(_trace);
            scroll.AddChild(paper);
            view.AddChild(scroll);
        }

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        toolRow.AddChild(spacer);
        toolRow.AddChild(MarkerToggle(true));

        main.AddChild(ReportPane(doc));
    }

    private Button Tool(string text, Action pressed)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 32) };
        var normal = UiStyle.Box(DarkPanel, 8, DarkLine, 0);
        normal.ContentMarginLeft = normal.ContentMarginRight = 12;
        var hover = UiStyle.Box(new Color("#22303B"), 8, new Color("#3E5466"), 0);
        hover.ContentMarginLeft = hover.ContentMarginRight = 12;
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeColorOverride("font_color", DarkText);
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        b.AddThemeColorOverride("font_pressed_color", Colors.White);
        b.AddThemeFontSizeOverride("font_size", 14);
        b.Pressed += pressed;
        return b;
    }

    private void ZoomBy(float factor)
    {
        if (_picture is null)
        {
            return;
        }

        _picture.Zoom = Mathf.Clamp(_picture.Zoom * factor, 0.5f, 4f);
        _picture.QueueRedraw();
    }

    private void Brighten(float by)
    {
        if (_picture is null)
        {
            return;
        }

        _picture.Brightness = Mathf.Clamp(_picture.Brightness + by, 0.5f, 1.8f);
        _picture.QueueRedraw();
    }

    /// <summary>The patient's results down the left, imaging first; the open one highlighted.</summary>
    private Control StudyList()
    {
        var pane = new PanelContainer { CustomMinimumSize = new Vector2(210, 0) };
        var box = UiStyle.Box(DarkPanel, 0, null, 0);
        box.CornerRadiusBottomLeft = 14;
        box.ContentMarginLeft = box.ContentMarginRight = 10;
        box.ContentMarginTop = 12;
        box.BorderColor = DarkLine;
        box.BorderWidthRight = 1;
        pane.AddThemeStyleboxOverride("panel", box);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        pane.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(list);
        var head = UiStyle.Caption("THIS PATIENT'S RESULTS", 11);
        head.AddThemeColorOverride("font_color", DarkMuted);
        list.AddChild(head);
        foreach (var o in Others.OrderBy(o => _db.Investigations[o.InvestigationId].Category == Simulation.Data.Models.InvestigationCategory.Imaging ? 0 : 1).ThenBy(o => o.ResultedAt))
        {
            var test = _db.Investigations[o.InvestigationId];
            var open = o == _order;
            var item = new Button { Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 50), ClipText = true, TooltipText = o.Summary };
            item.Text = $"{test.Name}\n{(o.Abnormal ? "Abnormal" : "Normal")} · {o.ResultedAt:HH:mm}";
            var normal = UiStyle.Box(open ? new Color("#1F3550") : DarkGround, 8, open ? UiStyle.Blue : DarkLine, 0);
            normal.ContentMarginLeft = normal.ContentMarginRight = 10;
            var hover = UiStyle.Box(open ? new Color("#1F3550") : new Color("#1C2630"), 8, open ? UiStyle.Blue : new Color("#3E5466"), 0);
            hover.ContentMarginLeft = hover.ContentMarginRight = 10;
            item.AddThemeStyleboxOverride("normal", normal);
            item.AddThemeStyleboxOverride("hover", hover);
            item.AddThemeStyleboxOverride("pressed", hover);
            item.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            item.AddThemeFontSizeOverride("font_size", 13);
            var colour = o.Abnormal ? new Color("#FF8A80") : DarkText;
            item.AddThemeColorOverride("font_color", colour);
            item.AddThemeColorOverride("font_hover_color", colour.Lightened(0.2f));
            item.AddThemeColorOverride("font_pressed_color", colour);
            if (!open)
            {
                item.Pressed += () => Show(o);
            }

            list.AddChild(item);
        }

        return pane;
    }

    private Control ReportPane(ResultDocument doc)
    {
        var pane = new PanelContainer { CustomMinimumSize = new Vector2(330, 0) };
        var box = UiStyle.Box(DarkPanel, 0, null, 0);
        box.CornerRadiusBottomRight = 14;
        box.ContentMarginLeft = box.ContentMarginRight = 16;
        box.ContentMarginTop = box.ContentMarginBottom = 14;
        box.BorderColor = DarkLine;
        box.BorderWidthLeft = 1;
        pane.AddThemeStyleboxOverride("panel", box);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        pane.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(column);

        column.AddChild(PatientStrip(true));

        var head = UiStyle.Caption("REPORT", 11);
        head.AddThemeColorOverride("font_color", DarkMuted);
        column.AddChild(head);
        var findingsHead = new Label { Text = "Findings" };
        findingsHead.AddThemeColorOverride("font_color", Colors.White);
        findingsHead.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(findingsHead);
        var findings = UiStyle.Body(doc.Findings ?? doc.Conclusion);
        findings.AddThemeColorOverride("font_color", DarkText);
        findings.AddThemeFontSizeOverride("font_size", 15);
        column.AddChild(findings);
        if (_trace is not null && _trace.Measurements.Count > 0)
        {
            var m = UiStyle.Caption(string.Join("\n", _trace.Measurements), 13);
            m.AddThemeColorOverride("font_color", DarkMuted);
            column.AddChild(m);
        }

        column.AddChild(ConclusionBox(doc, true));
        var by = UiStyle.Caption($"Reported by {doc.Department}, {_hospital}", 12);
        by.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        by.AddThemeColorOverride("font_color", DarkMuted);
        column.AddChild(by);
        return pane;
    }
}
