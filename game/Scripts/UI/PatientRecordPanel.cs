using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The selected patient's record. The top always shows who they are, their journey through the
/// department, their latest vital signs (each coloured by its NEWS2 score) and the NEWS2 total;
/// below, five tabs: Overview, Obs, Tests, Treatment and History.
/// Shows only what the clinical team knows, never the patient's true condition.
/// </summary>
public partial class PatientRecordPanel : PanelContainer
{
    public enum Tab
    {
        Overview,
        Obs,
        Tests,
        Treatment,
        History,
    }

    // Colours for the cards and pills.
    private static readonly Color BlueCard = new("#F2F6FC"), BlueLine = new("#D6E2F3");
    private static readonly Color PurpleText = new("#6B4FBB"), PurpleCard = new("#EFEBFA"), PurpleLine = new("#D9CFF3");
    private static readonly Color RedText = new("#C4373D"), RedCard = new("#FDECEC"), RedLine = new("#F5C2C2");
    private static readonly Color AmberText = new("#8A5300"), AmberCard = new("#FFF6E6"), AmberLine = new("#F0C878");
    private static readonly Color GreenText = new("#2E7D4F"), GreenCard = new("#E9F6EC"), GreenLine = new("#BFE3C6");
    private static readonly Color TealText = new("#0E7C7C"), PinkText = new("#B0457A"), PinkCard = new("#F9EAF1"), PinkLine = new("#EFC9DA");
    private static readonly Color Plain = Colors.White, PlainLine = new("#E3E9EE"), Grey = new("#F2F5F8");

    /// <summary>Background for a vital sign by its NEWS2 part score (0-3).</summary>
    private static readonly Color[] ScoreFill = { new("#F2F5F8"), new("#FFF3CC"), new("#FFE0BD"), new("#FBD0D0") };

    private readonly ScrollContainer _scroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly VBoxContainer _page = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Label _title = new();
    private readonly Label _subtitle = UiStyle.Caption(string.Empty, 13);
    private readonly JourneyBar _journey = new();
    private readonly List<(PanelContainer Tile, Label Value, Label Caption)> _vitals = new();
    private readonly PanelContainer _newsBar = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly RichTextLabel _newsText = new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false, AutowrapMode = TextServer.AutowrapMode.Off, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _newsTime = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly List<(Button Button, PanelIcon Icon, Label Text, PanelContainer Badge, Label BadgeText)> _tabs = new();
    private HospitalSimulation _sim = null!;
    private Patient? _patient;
    private string _lastPage = string.Empty;
    private double _refresh;
    private (PanelContainer Badge, Label Initials) _avatar;
    private (PanelContainer Chip, Label Text) _triageChip;
    private (PanelContainer Chip, Label Text) _placeChip;
    private (PanelContainer Chip, Label Text) _arrivalChip;
    private (PanelContainer Chip, Label Text) _timeChip;

    /// <summary>Raised when the player closes the record.</summary>
    public event Action? CloseRequested;

    /// <summary>Raised when the player asks to see the patient on the map.</summary>
    public event Action<Patient>? FocusRequested;

    /// <summary>Raised when a finished test is clicked, to open it as a readable result document.</summary>
    public event Action<Patient, InvestigationOrder>? ResultRequested;

    public Patient? Patient => _patient;

    /// <summary>The tab showing.</summary>
    public Tab CurrentTab { get; private set; } = Tab.Overview;

    /// <summary>The journey steps as drawn, for tests.</summary>
    public JourneyBar Journey => _journey;

    /// <summary>How many cards the current tab shows, for tests.</summary>
    public int CardCount => _page.GetChildCount();

    /// <summary>The NEWS2 band colour for a total: green 0, amber 1-4, orange 5-6, red 7+.</summary>
    public static string News2Band(int total) => total >= 7 ? ClinicalText.RedHex : total >= 5 ? "#EF6C00" : total >= 1 ? ClinicalText.AmberHex : ClinicalText.GreenHex;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        CustomMinimumSize = new Vector2(400, 0);
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", UiStyle.Card(18));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        AddChild(column);

        column.AddChild(BuildHeader());
        column.AddChild(BuildChips());

        var journeyBox = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        journeyBox.AddThemeStyleboxOverride("panel", Pad(UiStyle.Box(new Color("#F4FAF5"), 12, new Color("#D7EEDB")), 6, 8));
        journeyBox.AddChild(_journey);
        column.AddChild(journeyBox);

        column.AddChild(BuildVitals());

        var newsRow = new HBoxContainer();
        newsRow.AddThemeConstantOverride("separation", 8);
        _newsText.AddThemeFontSizeOverride("normal_font_size", 13);
        _newsText.AddThemeFontSizeOverride("bold_font_size", 20);
        _newsTime.AddThemeFontSizeOverride("font_size", 12);
        newsRow.AddChild(_newsText);
        newsRow.AddChild(_newsTime);
        _newsBar.AddChild(newsRow);
        column.AddChild(_newsBar);

        column.AddChild(BuildTabs());

        _page.AddThemeConstantOverride("separation", 8);
        _scroll.AddChild(_page);
        column.AddChild(_scroll);

        var find = UiStyle.Button("Find on map", () =>
        {
            if (_patient is not null)
            {
                FocusRequested?.Invoke(_patient);
            }
        });
        find.TooltipText = "Centre the view on this patient";
        find.CustomMinimumSize = new Vector2(0, 42);
        column.AddChild(find);
    }

    // ------------------------------------------------------------------ building the fixed top part

    private Control BuildHeader()
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        _avatar = HudWidgets.Avatar(50);
        var ring = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        ring.AddThemeStyleboxOverride("panel", UiStyle.Box(Colors.White, 30, null, 3));
        ring.AddChild(_avatar.Badge);
        header.AddChild(ring);

        var who = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        who.AddThemeConstantOverride("separation", 0);
        _title.AddThemeFontSizeOverride("font_size", 20);
        _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _subtitle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        who.AddChild(_title);
        who.AddChild(_subtitle);
        header.AddChild(who);

        var rename = NameEditing.Attach(who, _title, () => _patient?.FullName, name =>
        {
            if (_patient is { } p)
            {
                _sim.RenamePatient(p.Id, name);
                Refresh();
            }
        });
        rename.Text = string.Empty;
        rename.CustomMinimumSize = new Vector2(40, 40);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.AddChild(new PanelIcon(PanelIcon.Kind.Pencil, 18) { Colour = UiStyle.TextDark });
        rename.AddChild(centre);
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        header.AddChild(rename);

        var close = UiStyle.SecondaryButton("✕", () => CloseRequested?.Invoke());
        close.TooltipText = "Close (Esc)";
        close.CustomMinimumSize = new Vector2(40, 40);
        header.AddChild(close);
        return header;
    }

    private Control BuildChips()
    {
        var chips = new HBoxContainer();
        chips.AddThemeConstantOverride("separation", 6);
        _triageChip = HudWidgets.Chip();
        _placeChip = HudWidgets.Chip();
        _arrivalChip = HudWidgets.Chip();
        _timeChip = HudWidgets.Chip();
        chips.AddChild(_triageChip.Chip);
        chips.AddChild(_placeChip.Chip);
        chips.AddChild(_arrivalChip.Chip);
        chips.AddChild(_timeChip.Chip);
        return chips;
    }

    private Control BuildVitals()
    {
        var grid = new HBoxContainer();
        grid.AddThemeConstantOverride("separation", 5);
        foreach (var caption in new[] { "HR", "BP", "RR", "SPO₂", "TEMP", "PAIN" })
        {
            var tile = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = caption == "BP" ? 1.35f : 1f, MouseFilter = MouseFilterEnum.Ignore };
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", -3);
            var value = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            value.AddThemeFontSizeOverride("font_size", 15);
            var label = new Label { Text = caption, HorizontalAlignment = HorizontalAlignment.Center };
            label.AddThemeFontSizeOverride("font_size", 10);
            label.AddThemeColorOverride("font_color", UiStyle.TextMuted);
            box.AddChild(value);
            box.AddChild(label);
            tile.AddChild(box);
            grid.AddChild(tile);
            _vitals.Add((tile, value, label));
        }

        return grid;
    }

    private Control BuildTabs()
    {
        var track = new PanelContainer();
        track.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color("#E9EEF2"), 12, null, 4));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        track.AddChild(row);
        var group = new ButtonGroup();
        var tabs = new[] { (Tab.Overview, PanelIcon.Kind.Person), (Tab.Obs, PanelIcon.Kind.Heart), (Tab.Tests, PanelIcon.Kind.Flask), (Tab.Treatment, PanelIcon.Kind.Pill), (Tab.History, PanelIcon.Kind.Clock) };
        foreach (var (tab, kind) in tabs)
        {
            var button = new Button
            {
                ToggleMode = true,
                ButtonGroup = group,
                FocusMode = FocusModeEnum.None,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 50),
                TooltipText = tab switch
                {
                    Tab.Overview => "Where they are, the diagnosis, allergies and what they are waiting for",
                    Tab.Obs => "Observations, NEWS2 and symptoms",
                    Tab.Tests => "Tests and results",
                    Tab.Treatment => "The treatment plan",
                    _ => "Background and everything that has happened",
                },
            };
            var normal = UiStyle.Box(Colors.White, 9, new Color("#D9E1E8"));
            var hover = UiStyle.Box(new Color("#F5F8FB"), 9, new Color("#B9C4CE"));
            var on = UiStyle.Box(UiStyle.Blue, 9, UiStyle.Blue);
            on.ShadowColor = new Color(0.12f, 0.37f, 0.75f, 0.3f);
            on.ShadowSize = 4;
            on.ShadowOffset = new Vector2(0, 2);
            button.AddThemeStyleboxOverride("normal", normal);
            button.AddThemeStyleboxOverride("hover", hover);
            button.AddThemeStyleboxOverride("pressed", on);
            button.AddThemeStyleboxOverride("hover_pressed", on);
            button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

            var inside = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
            inside.AddThemeConstantOverride("separation", 1);
            var icon = new PanelIcon(kind, 18) { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
            var text = new Label { Text = tab.ToString(), HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            text.AddThemeFontSizeOverride("font_size", 12);
            inside.AddChild(icon);
            inside.AddChild(text);
            button.AddChild(inside);
            inside.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

            var badge = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
            var badgeText = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            badgeText.AddThemeFontSizeOverride("font_size", 10);
            badge.AddChild(badgeText);
            button.AddChild(badge);
            badge.SetAnchorsPreset(LayoutPreset.TopRight);
            badge.Position = new Vector2(-15, -5);

            var which = tab;
            button.Toggled += pressed =>
            {
                if (pressed)
                {
                    ShowTab(which);
                }
            };
            row.AddChild(button);
            _tabs.Add((button, icon, text, badge, badgeText));
        }

        _tabs[0].Button.SetPressedNoSignal(true);
        StyleTabs();
        return track;
    }

    /// <summary>Switches to a tab.</summary>
    public void ShowTab(Tab tab)
    {
        CurrentTab = tab;
        for (var i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].Button.SetPressedNoSignal(i == (int)tab);
        }

        StyleTabs();
        _lastPage = string.Empty;
        _scroll.ScrollVertical = 0;
        Refresh();
    }

    private void StyleTabs()
    {
        foreach (var (button, icon, text, _, _) in _tabs)
        {
            var colour = button.ButtonPressed ? Colors.White : new Color("#3E4C59");
            icon.Colour = colour;
            text.AddThemeColorOverride("font_color", colour);
        }
    }

    private void SetBadge(Tab tab, string text, Color colour)
    {
        var (_, _, _, badge, label) = _tabs[(int)tab];
        badge.Visible = text.Length > 0;
        label.Text = text;
        var box = UiStyle.Box(colour, 9, Colors.White);
        box.BorderWidthBottom = box.BorderWidthTop = box.BorderWidthLeft = box.BorderWidthRight = 2;
        box.ContentMarginLeft = box.ContentMarginRight = 5;
        box.ContentMarginTop = box.ContentMarginBottom = 0;
        badge.AddThemeStyleboxOverride("panel", box);
        label.AddThemeColorOverride("font_color", colour.Luminance > 0.6f ? UiStyle.TextDark : Colors.White);
        badge.ResetSize();
        badge.Position = new Vector2(_tabs[(int)tab].Button.Size.X - badge.Size.X + 4, -6);
    }

    // ------------------------------------------------------------------ refreshing

    public void Show(Patient patient)
    {
        var changed = !ReferenceEquals(patient, _patient);
        _patient = patient;
        Visible = true;
        if (changed)
        {
            _lastPage = string.Empty;
            Refresh();
            _scroll.ScrollVertical = 0;
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible || _patient is null)
        {
            return;
        }

        _refresh -= delta;
        if (_refresh <= 0)
        {
            _refresh = 0.4;
            Refresh();
        }
    }

    private void Refresh()
    {
        if (_patient is not { } p)
        {
            return;
        }

        RefreshHeader(p);
        RefreshJourney(p);
        RefreshVitals(p);
        RefreshBadges(p);

        var cards = CurrentTab switch
        {
            Tab.Obs => ObsPage(p),
            Tab.Tests => TestsPage(p),
            Tab.Treatment => TreatmentPage(p),
            Tab.History => HistoryPage(p),
            _ => OverviewPage(p),
        };
        var signature = string.Join("\u001f", cards.Select(c => c.Signature));
        if (signature == _lastPage)
        {
            return;
        }

        _lastPage = signature;
        var scroll = _scroll.ScrollVertical;
        foreach (var child in _page.GetChildren())
        {
            _page.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var card in cards)
        {
            _page.AddChild(BuildCard(card));
        }

        _scroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, scroll);
    }

    /// <summary>Who they are, triage, where they are and how long they have been here.</summary>
    private void RefreshHeader(Patient p)
    {
        var now = _sim.Now;
        _title.Text = $"{p.FullName}, {p.Age}";
        _subtitle.Text = $"{p.Sex} · {_sim.Database.Symptoms[p.PresentingComplaint].Name}";
        var triage = ClinicalText.TriageColour(p.Triage);
        HudWidgets.SetAvatar(_avatar, p.FullName, triage);
        HudWidgets.SetChip(_triageChip, p.Triage is null ? "Not triaged" : p.Triage.ToString()!, triage);
        var tile = p.Tile;
        var place = p.IsInDepartment ? _sim.Spaces.Where(s => s.Rect.Contains(tile) && s.Kind != BlueprintHospital.Simulation.Hospital.CareSpaceKind.Corridor).Select(s => s.Name).FirstOrDefault() : null;
        HudWidgets.SetChip(_placeChip, place ?? string.Empty, UiStyle.Ground);
        HudWidgets.SetChip(_arrivalChip, p.Maternity ? "Maternity" : p.ArrivalMethod == ArrivalMethod.Ambulance ? "Ambulance" : "Walked in", UiStyle.Ground);

        // Once admitted the A&E clock stops: the chip shows their length of stay instead.
        var time = p.Maternity ? now - p.ArrivedAt : p.LengthOfStay(now) ?? p.TimeInEmergency(now);
        var counting = p.EmergencyClockRunning;
        var where = p.Maternity ? "in hospital" : p.AdmissionType is { } type ? $"{type.ToString().ToLowerInvariant()} stay" : "in A&E";
        var late = counting && time > Patient.FourHours;
        var warn = counting && time > TimeSpan.FromHours(2);
        HudWidgets.SetChip(_timeChip, $"{ClinicalText.Duration(time)} {where}", late ? RedCard : warn ? AmberCard : UiStyle.Ground);
        _timeChip.Text.AddThemeColorOverride("font_color", late ? RedText : warn ? AmberText : UiStyle.TextDark);
    }

    private void RefreshJourney(Patient p)
    {
        var steps = new List<(string, bool)>();
        steps.Add(("Arrived", true));
        if (p.Maternity)
        {
            steps.Add(("Seen", p.FirstSeenByDoctorAt is not null || p.Delivered));
            steps.Add(("Delivered", p.Delivered));
        }
        else
        {
            steps.Add(("Triaged", p.Triage is not null));
            steps.Add(("Seen", p.FirstSeenByDoctorAt is not null));
            steps.Add(("Diagnosed", p.WorkingDiagnosis is not null));
            steps.Add(("Treated", p.TreatmentPlan.Count > 0 && p.TreatmentPlan.All(t => t.Done || t.Withheld)));
        }

        var onWard = p.Stage == PatientStage.OnWard;
        var finished = !p.IsInDepartment || onWard;
        var last = !p.IsInDepartment
            ? p.Outcome switch
            {
                PatientOutcome.Discharged => "Home",
                PatientOutcome.Transferred => "Transferred",
                PatientOutcome.Admitted => "Admitted",
                PatientOutcome.LeftWithoutBeingSeen => "Left",
                PatientOutcome.Died => "Died",
                _ => "Left",
            }
            : onWard ? "On ward" : "Ward / home";
        steps.Add((last, finished));

        var result = new List<(string, JourneyBar.Step)>();
        var current = p.IsInDepartment && !onWard ? steps.FindIndex(s => !s.Item2) : -1;
        for (var i = 0; i < steps.Count; i++)
        {
            var (label, done) = steps[i];
            var state = done ? JourneyBar.Step.Done : i == current ? JourneyBar.Step.Current : JourneyBar.Step.Todo;
            if (i == steps.Count - 1 && p.Outcome is PatientOutcome.Died or PatientOutcome.LeftWithoutBeingSeen && !p.IsInDepartment)
            {
                state = JourneyBar.Step.Bad;
            }

            // "Treated" reads "Treating" while it is under way.
            if (label == "Treated" && state == JourneyBar.Step.Current && p.TreatmentPlan.Count > 0)
            {
                label = "Treating";
            }

            result.Add((label, state));
        }

        _journey.Set(result);
    }

    private void RefreshVitals(Patient p)
    {
        if (p.LatestObservations is not { } latest)
        {
            foreach (var (tile, value, _) in _vitals)
            {
                value.Text = "–";
                value.AddThemeColorOverride("font_color", UiStyle.TextMuted);
                tile.AddThemeStyleboxOverride("panel", Pad(UiStyle.Box(ScoreFill[0], 9), 4, 2));
            }

            _newsBar.AddThemeStyleboxOverride("panel", Pad(UiStyle.Box(UiStyle.Ground, 10), 7, 12));
            _newsText.Text = "[color=#5B6B79]NEWS2  No observations taken yet[/color]";
            _newsTime.Text = string.Empty;
            return;
        }

        var (o, n) = latest;
        var pain = o.PainScore >= 7 ? 3 : o.PainScore >= 4 ? 1 : 0;
        var values = new (string Text, int Score)[]
        {
            (o.HeartRate.ToString(), n.HeartRate),
            (o.BloodPressure, n.SystolicBp),
            (o.RespiratoryRate.ToString(), n.RespiratoryRate),
            ($"{o.OxygenSaturation}%", Math.Max(n.OxygenSaturation, n.SupplementalOxygen)),
            ($"{o.Temperature:0.0}", n.Temperature),
            ($"{o.PainScore}/10", pain),
        };
        for (var i = 0; i < _vitals.Count; i++)
        {
            var (tile, value, _) = _vitals[i];
            var score = Math.Clamp(values[i].Score, 0, 3);
            value.Text = values[i].Text;
            value.AddThemeColorOverride("font_color", score == 3 ? new Color("#A01818") : UiStyle.TextDark);
            tile.AddThemeStyleboxOverride("panel", Pad(UiStyle.Box(ScoreFill[score], 9), 4, 2));
        }

        var band = new Color(News2Band(n.Total));
        var dark = band.Luminance > 0.6f;
        var ink = dark ? "#17232E" : "#FFFFFF";
        _newsBar.AddThemeStyleboxOverride("panel", Pad(UiStyle.Box(band, 10), 5, 12));
        var risk = n.Risk switch
        {
            News2Risk.High => "High risk",
            News2Risk.Medium => "Medium risk",
            News2Risk.LowMedium => "Low-medium risk",
            _ => "Low risk",
        };
        var trend = string.Empty;
        if (p.ObservationHistory.Count >= 2)
        {
            var (before, _, prev) = p.ObservationHistory[^2];
            trend = prev.Total < n.Total ? $" · up from {prev.Total} at {before:HH:mm}"
                : prev.Total > n.Total ? $" · down from {prev.Total} at {before:HH:mm}"
                : " · steady";
        }

        var alert = o.Consciousness != Consciousness.Alert ? $" · {ClinicalText.Escape(o.Consciousness.ToString())}" : string.Empty;
        _newsText.Text = $"[color={ink}]NEWS2  [b]{n.Total}[/b]  {risk}{alert}{trend}[/color]";
        _newsTime.Text = p.ObservationHistory[^1].Time.ToString("HH:mm");
        _newsTime.AddThemeColorOverride("font_color", dark ? UiStyle.TextDark : Colors.White);
    }

    private void RefreshBadges(Patient p)
    {
        var news = p.LatestObservations?.News2;
        SetBadge(Tab.Obs, news is { } s && (s.Total >= 5 || s.HasRedScore) ? "!" : string.Empty, new Color(ClinicalText.RedHex));
        var waiting = p.Investigations.Count(i => i.Status != InvestigationStatus.Resulted);
        SetBadge(Tab.Tests, waiting > 0 ? waiting.ToString() : string.Empty, new Color(ClinicalText.AmberHex));
        var todo = p.TreatmentPlan.Count(t => !t.Done && !t.Withheld);
        SetBadge(Tab.Treatment, todo > 0 ? todo.ToString() : string.Empty, new Color(ClinicalText.AmberHex));
    }

    // ------------------------------------------------------------------ the tab pages

    private sealed record Pill(string Text, Color Back, Color Ink);

    private sealed record Row(string Text, Pill? Pill = null);

    private sealed record Card(string? Heading, Color Ink, Color Back, Color Line, string? Body = null, IReadOnlyList<Row>? Rows = null, Pill? HeadPill = null, IReadOnlyList<(string, int)>? Trend = null)
    {
        public string Signature => $"{Heading}|{Ink}|{Back}|{Body}|{HeadPill}|"
            + (Rows is null ? string.Empty : string.Join("¦", Rows.Select(r => $"{r.Text}~{r.Pill}")))
            + (Trend is null ? string.Empty : string.Join(",", Trend.Select(t => $"{t.Item1}={t.Item2}")));
    }

    private static string E(string s) => ClinicalText.Escape(s);

    private const string Muted = "#5B6B79";

    private static Pill Waiting(string text) => new(text, AmberCard, AmberText);

    private static Pill Quiet(string text) => new(text, Grey, UiStyle.TextMuted);

    private List<Card> OverviewPage(Patient p)
    {
        var db = _sim.Database;
        var now = _sim.Now;
        var cards = new List<Card>();

        // Right now.
        var detail = new List<string>();
        if (p.MonitorIsRed)
        {
            var sitter = _sim.CarerAtBedside(p);
            var with = sitter is not null ? $"{E(sitter.Name)} is sitting with them" : "nobody sitting with them yet";
            var review = p.RedReviewed ? "doctor has reviewed" : "waiting for a doctor's review";
            var trend = sitter is not null && p.RedReviewed ? "improving" : "getting worse";
            detail.Add($"[color={ClinicalText.RedHex}]Red monitor (NEWS2 {p.LiveNews2}): {with}, {review}; {trend}[/color]");
        }

        if (p.Stage == PatientStage.OnWard)
        {
            var mood = p.Happiness >= 0.75 ? "cheerful" : p.Happiness >= 0.55 ? "settled" : p.Happiness >= 0.4 ? "a bit low" : "fed up";
            var visitors = _sim.VisitorsOf(p).Where(v => v.Activity is VisitorActivity.Visiting or VisitorActivity.Away).Select(v => $"{E(v.FirstName)} ({v.Relation})").ToList();
            detail.Add($"Mood: {mood} · {p.VisitsReceived} visit{(p.VisitsReceived == 1 ? string.Empty : "s")} so far" + (visitors.Count > 0 ? $" · with them now: {string.Join(", ", visitors)}" : string.Empty));
        }

        if (p.AdmissionType is { } admission && p.AdmittedAt is { } admittedAt)
        {
            var line = $"{admission} · admitted {admittedAt:HH:mm} · length of stay {ClinicalText.Duration(p.LengthOfStay(now)!.Value)}";
            if (!p.Maternity)
            {
                line += p.TimeInEmergency(now) <= Patient.FourHours ? " · left A&E within 4 hours" : $" [color={ClinicalText.RedHex}]· left A&E after 4 hours[/color]";
            }

            detail.Add(line);
        }

        var nowBody = $"{E(ClinicalText.Whereabouts(_sim, p))}" + (detail.Count > 0 ? $"\n[font_size=13][color={Muted}]{string.Join("\n", detail)}[/color][/font_size]" : string.Empty);
        cards.Add(new Card(p.IsInDepartment ? "RIGHT NOW" : "LEFT THE DEPARTMENT", UiStyle.Blue, BlueCard, BlueLine, nowBody));

        if (!string.IsNullOrEmpty(p.OutcomeNote) && !p.IsInDepartment)
        {
            cards.Add(new Card("OUTCOME", UiStyle.TextDark, Plain, PlainLine, E(p.OutcomeNote)));
        }

        // Diagnosis.
        if (p.WorkingDiagnosis is { } dx)
        {
            var doctor = _sim.FindStaff(dx.DoctorId)?.Name ?? "a doctor";
            cards.Add(new Card("WORKING DIAGNOSIS", PurpleText, PurpleCard, PurpleLine,
                $"{E(db.Conditions[dx.ConditionId].Name)}\n[font_size=13][color={Muted}]{E(doctor)} at {dx.MadeAt:HH:mm} · {dx.Confidence * 100:0}% sure[/color][/font_size]"));
        }
        else if (!p.Maternity)
        {
            cards.Add(new Card("WORKING DIAGNOSIS", PurpleText, Plain, PlainLine,
                $"[color={Muted}]{(p.FirstSeenByDoctorAt is null ? "Not yet seen by a doctor" : "Not made yet")}[/color]"));
        }

        // Allergies.
        cards.Add(p.Allergies.Count > 0
            ? new Card("ALLERGIES", RedText, RedCard, RedLine, $"[color=#C4373D]{E(string.Join(", ", p.Allergies))}[/color]")
            : new Card("ALLERGIES", GreenText, GreenCard, GreenLine, "No known drug allergies"));

        if (p.Maternity)
        {
            cards.Add(MaternityCard(p));
        }

        // What they are waiting for.
        var waiting = new List<Row>();
        foreach (var inv in p.Investigations.Where(i => i.Status != InvestigationStatus.Resulted))
        {
            var name = E(db.Investigations[inv.InvestigationId].Name);
            waiting.Add(inv.Status == InvestigationStatus.SampleTaken
                ? new Row(name, Waiting(inv.ResultDueAt is { } due ? $"due {due:HH:mm}" : "at the lab"))
                : new Row(name, Quiet("requested")));
        }

        foreach (var t in p.TreatmentPlan.Where(t => !t.Done && !t.Withheld))
        {
            waiting.Add(new Row(E(TreatmentName(t)), new Pill("to do", GreenCard, GreenText)));
        }

        if (waiting.Count > 0)
        {
            cards.Add(new Card("WAITING FOR", TealText, Plain, PlainLine, Rows: waiting));
        }

        // How they came in.
        var how = p.Maternity
            ? $"Came to the maternity unit at {p.ArrivedAt:HH:mm}"
            : $"Arrived {p.ArrivedAt:HH:mm} {(p.ArrivalMethod == ArrivalMethod.Ambulance ? "by ambulance" : "on foot")} with {E(db.Symptoms[p.PresentingComplaint].Name.ToLowerInvariant())}";
        var arrival = new StringBuilder(how);
        if (p.Triage is not null)
        {
            arrival.Append($"\n[font_size=13][color={Muted}]Triage: {E(p.TriageReason)}[/color][/font_size]");
        }

        if (p.AmbulanceHandover is { } handover)
        {
            arrival.Append($"\n[font_size=13]{E(handover)}[/font_size]");
            cards.Add(new Card("AMBULANCE HANDOVER", AmberText, new Color("#FFF9EC"), AmberLine, arrival.ToString()));
        }
        else
        {
            cards.Add(new Card("ARRIVAL", UiStyle.TextMuted, Plain, PlainLine, arrival.ToString()));
        }

        return cards;
    }

    private List<Card> ObsPage(Patient p)
    {
        var db = _sim.Database;
        var cards = new List<Card>();
        if (p.LatestObservations is not { } latest)
        {
            cards.Add(new Card("OBSERVATIONS", UiStyle.Blue, Plain, PlainLine, $"[color={Muted}]No observations recorded yet.[/color]"));
        }
        else
        {
            var (obs, news) = latest;
            var sb = new StringBuilder();
            sb.Append($"[font_size=13]NEWS2 [color={ClinicalText.News2Hex(news.Total)}][b]{news.Total}[/b][/color]");
            if (news.HasRedScore)
            {
                sb.Append($" [color={ClinicalText.RedHex}](a single score of 3)[/color]");
            }

            sb.Append($" · pain {obs.PainScore}/10 · coloured by NEWS2 score[/font_size]\n");
            sb.Append("[font_size=12][table=8]");
            foreach (var h in new[] { "Time", "HR", "BP", "RR", "SpO₂", "Temp", "ACVPU", "NEWS2" })
            {
                sb.Append($"[cell padding=3,3,3,3][color={Muted}]{h}[/color][/cell]");
            }

            static string Cell(string text, int score) => score <= 0
                ? $"[cell padding=3,2,3,2]{text}[/cell]"
                : $"[cell bg={ScoreFill[Math.Min(score, 3)].ToHtml(false)} padding=3,2,3,2]{(score >= 3 ? $"[color=#A01818]{text}[/color]" : text)}[/cell]";
            foreach (var (time, o, n) in p.ObservationHistory.Reverse().Take(6))
            {
                sb.Append($"[cell padding=3,2,3,2][color={Muted}]{time:HH:mm}[/color][/cell]");
                sb.Append(Cell(o.HeartRate.ToString(), n.HeartRate));
                sb.Append(Cell(o.BloodPressure, n.SystolicBp));
                sb.Append(Cell(o.RespiratoryRate.ToString(), n.RespiratoryRate));
                sb.Append(Cell($"{o.OxygenSaturation}%", Math.Max(n.OxygenSaturation, n.SupplementalOxygen)));
                sb.Append(Cell($"{o.Temperature:0.0}", n.Temperature));
                sb.Append(Cell(o.Consciousness.ToString()[..1], n.Consciousness));
                sb.Append($"[cell padding=3,2,3,2][bgcolor={News2Band(n.Total)}][color={(n.Total is >= 1 and < 5 ? "#17232E" : "#FFFFFF")}][b] {n.Total} [/b][/color][/bgcolor][/cell]");
            }

            sb.Append("[/table][/font_size]");
            cards.Add(new Card("OBSERVATIONS", UiStyle.Blue, Plain, PlainLine, sb.ToString()));

            var trend = p.ObservationHistory.TakeLast(6).Select(h => (h.Time.ToString("HH:mm"), h.News2.Total)).ToList();
            if (trend.Count >= 2)
            {
                cards.Add(new Card("NEWS2 TREND", UiStyle.Blue, Plain, PlainLine, Trend: trend));
            }
        }

        var symptoms = p.HistoryTaken ? p.Symptoms : p.KnownSymptoms.ToList();
        cards.Add(new Card("SYMPTOMS", PinkText, Plain, PlainLine, symptoms.Count == 0
            ? $"[color={Muted}]Not yet assessed.[/color]"
            : string.Join(", ", symptoms.Select(s => db.Symptoms.TryGetValue(s, out var d) ? E(d.Name) : s)) + (p.HistoryTaken ? string.Empty : $"\n[font_size=13][color={Muted}]Full history not yet taken[/color][/font_size]")));
        return cards;
    }

    private List<Card> TestsPage(Patient p)
    {
        var db = _sim.Database;
        var cards = new List<Card>();
        if (p.Investigations.Count == 0)
        {
            cards.Add(new Card("TESTS", TealText, Plain, PlainLine, $"[color={Muted}]None requested yet.[/color]"));
            return cards;
        }

        for (var index = 0; index < p.Investigations.Count; index++)
        {
            var inv = p.Investigations[index];
            var name = E(db.Investigations[inv.InvestigationId].Name);
            switch (inv.Status)
            {
                case InvestigationStatus.Resulted:
                    cards.Add(new Card(null, UiStyle.TextDark, inv.Abnormal ? RedCard : Plain, inv.Abnormal ? RedLine : PlainLine,
                        $"{name}\n[font_size=13][color={(inv.Abnormal ? "#C4373D" : "#2E7D4F")}]{E(inv.Summary)}[/color][color={Muted}] · {inv.ResultedAt:HH:mm}[/color]\n[url=result:{index}][color=#1F5FBF]Open result ›[/color][/url][/font_size]",
                        HeadPill: inv.Abnormal ? new Pill("Abnormal", Colors.White, RedText) : new Pill("Normal", GreenCard, GreenText)));
                    break;
                case InvestigationStatus.SampleTaken:
                    cards.Add(new Card(null, UiStyle.TextDark, Plain, PlainLine,
                        $"{name}\n[font_size=13][color={Muted}]Sample at the lab{(inv.ResultDueAt is { } d ? $", due about {d:HH:mm}" : string.Empty)}[/color][/font_size]",
                        HeadPill: Waiting("At the lab")));
                    break;
                default:
                    cards.Add(new Card(null, UiStyle.TextDark, Plain, PlainLine,
                        $"{name}\n[font_size=13][color={Muted}]Requested {inv.RequestedAt:HH:mm}, not yet done[/color][/font_size]",
                        HeadPill: Quiet("Requested")));
                    break;
            }
        }

        return cards;
    }

    private string TreatmentName(TreatmentRecord t) =>
        t.Step.Type == TreatmentStepType.Medication && t.Step.Medication is { } m && _sim.Database.Medications.TryGetValue(m, out var med)
            ? med.Name
            : t.Step.Describe();

    private List<Card> TreatmentPage(Patient p)
    {
        var cards = new List<Card>();
        if (p.TreatmentPlan.Count == 0)
        {
            cards.Add(new Card("TREATMENT", GreenText, Plain, PlainLine, $"[color={Muted}]No treatment planned yet.[/color]"));
        }
        else
        {
            var rows = new List<Row>();
            foreach (var t in p.TreatmentPlan)
            {
                var mark = t.Withheld ? $"[color={ClinicalText.RedHex}][b]✗[/b][/color]" : t.Done ? $"[color={ClinicalText.GreenHex}][b]✓[/b][/color]" : "[color=#9AA6B1]○[/color]";
                var note = string.IsNullOrEmpty(t.Note) ? string.Empty : $"\n[font_size=12][color={Muted}]{E(t.Note)}[/color][/font_size]";
                var pill = t.Withheld ? new Pill("withheld", RedCard, RedText) : t.Done ? new Pill("given", GreenCard, GreenText) : Quiet("to do");
                rows.Add(new Row($"{mark}  {E(TreatmentName(t))}{note}", pill));
            }

            var done = p.TreatmentPlan.Count(t => t.Done);
            cards.Add(new Card("TREATMENT", GreenText, Plain, PlainLine, Rows: rows, HeadPill: Quiet($"{done} of {p.TreatmentPlan.Count} done")));
        }

        if (p.WorkingDiagnosis is { } dx)
        {
            cards.Add(new Card("PLAN FOR", PurpleText, PurpleCard, PurpleLine, E(_sim.Database.Conditions[dx.ConditionId].Name)));
        }

        return cards;
    }

    private List<Card> HistoryPage(Patient p)
    {
        var cards = new List<Card>();
        var sb = new StringBuilder();
        string Line(string key, string value) => $"[color={Muted}]{key}[/color]  {value}\n";
        sb.Append(Line("Patient", $"{p.Sex}, {p.Age} · {p.HospitalNumber} · DOB {p.DateOfBirth:dd/MM/yyyy}"));
        sb.Append(Line("Past history", p.PastHistory.Count == 0 ? "none of note" : E(string.Join(", ", p.PastHistory))));
        sb.Append(Line("Regular medication", p.RegularMedications.Count == 0 ? "none" : E(string.Join(", ", p.RegularMedications))));
        sb.Append(Line("Allergies", p.Allergies.Count == 0 ? "no known drug allergies" : $"[color={ClinicalText.RedHex}]{E(string.Join(", ", p.Allergies))}[/color]"));
        sb.Append(Line("Previous A&E visits", p.PreviousVisits.ToString()).TrimEnd('\n'));
        cards.Add(new Card("BACKGROUND", UiStyle.TextMuted, Plain, PlainLine, $"[font_size=13]{sb}[/font_size]"));

        var timeline = new StringBuilder("[font_size=13]");
        foreach (var entry in p.Timeline.Reverse())
        {
            var text = E(entry.Text);
            var dot = entry.Important ? ClinicalText.RedHex : "#9AA6B1";
            timeline.Append($"[color={dot}]●[/color] [color={Muted}]{entry.Time:HH:mm}[/color]  {(entry.Important ? $"[b]{text}[/b]" : text)}\n");
        }

        cards.Add(new Card("TIMELINE", UiStyle.Blue, Plain, PlainLine, timeline.ToString().TrimEnd('\n') + "[/font_size]"));
        return cards;
    }

    /// <summary>The pregnancy and the babies, each with a link to rename them.</summary>
    private Card MaternityCard(Patient p)
    {
        var sb = new StringBuilder();
        sb.Append($"[b]{p.GestationWeeks} weeks[/b] pregnant{(p.Delivered ? " at delivery" : "")}");
        foreach (var baby in _sim.BabiesOf(p))
        {
            var place = baby.Place switch
            {
                BabyPlace.WithMother => "with mum",
                BabyPlace.Carried => "being carried to or from the neonatal unit",
                BabyPlace.NeonatalUnit => $"on the neonatal unit{(baby.CotIndex is { } i ? $", incubator {i + 1}" : "")}",
                BabyPlace.Transferred => "transferred to another hospital's neonatal unit",
                _ => "gone home",
            };
            var colour = baby.Sex == Sex.Female ? "#B0457A" : "#2F6FB0";
            sb.Append($"\n[color={colour}]●[/color] [b]{E(baby.FullName)}[/b] [url=rename-baby:{baby.Id}][color=#1F5FBF](rename)[/color][/url]");
            sb.Append($"\n[font_size=13]   {(baby.Sex == Sex.Female ? "Girl" : "Boy")}, {baby.WeightKg:0.00} kg, born {baby.BornAt:HH:mm} ({E(baby.Delivery.ToLowerInvariant())}), {place}");
            if (baby.NeonatalReason is { } reason)
            {
                sb.Append($"\n   [color={Muted}]Neonatal care: {E(reason)}[/color]");
            }

            foreach (var entry in baby.Timeline.Where(t => t.Important).Reverse().Take(3))
            {
                sb.Append($"\n   [color={Muted}]{entry.Time:HH:mm}  {E(entry.Text)}[/color]");
            }

            sb.Append("[/font_size]");
        }

        return new Card(p.Delivered ? "MOTHER AND BABY" : "PREGNANCY", PinkText, PinkCard, PinkLine, sb.ToString());
    }

    // ------------------------------------------------------------------ drawing a card

    private Control BuildCard(Card card)
    {
        var box = new PanelContainer { MouseFilter = MouseFilterEnum.Pass };
        box.AddThemeStyleboxOverride("panel", Pad(UiStyle.Box(card.Back, 12, card.Line), 9, 12));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 3);
        box.AddChild(column);

        if (card.Heading is not null || (card.HeadPill is not null && card.Body is null))
        {
            var head = new HBoxContainer();
            var title = new Label { Text = card.Heading ?? string.Empty, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            title.AddThemeFontSizeOverride("font_size", 12);
            title.AddThemeColorOverride("font_color", card.Ink);
            head.AddChild(title);
            if (card.HeadPill is { } pill)
            {
                head.AddChild(BuildPill(pill));
            }

            column.AddChild(head);
        }

        if (card.Body is { } body)
        {
            if (card.Heading is null && card.HeadPill is { } pill)
            {
                // A test: the name and its result on the left, the status pill on the right.
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 8);
                row.AddChild(Text(body));
                var holder = new VBoxContainer();
                holder.AddChild(BuildPill(pill));
                row.AddChild(holder);
                column.AddChild(row);
            }
            else
            {
                column.AddChild(Text(body));
            }
        }

        if (card.Rows is { } rows)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (i > 0)
                {
                    column.AddChild(HudWidgets.Divider());
                }

                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 8);
                row.AddChild(Text(rows[i].Text));
                if (rows[i].Pill is { } pill)
                {
                    var holder = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
                    holder.AddChild(BuildPill(pill));
                    row.AddChild(holder);
                }

                column.AddChild(row);
            }
        }

        if (card.Trend is { } trend)
        {
            var chart = new News2Trend();
            chart.Set(trend);
            column.AddChild(chart);
        }

        return box;
    }

    private RichTextLabel Text(string bbcode)
    {
        var text = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SelectionEnabled = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Text = bbcode,
        };
        text.AddThemeColorOverride("default_color", UiStyle.TextDark);
        text.AddThemeFontSizeOverride("normal_font_size", 14);
        text.AddThemeFontSizeOverride("bold_font_size", 14);
        text.MetaClicked += OnLink;
        return text;
    }

    private static Control BuildPill(Pill pill)
    {
        var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkBegin };
        var box = UiStyle.Box(pill.Back, 6, pill.Back == Colors.White ? RedLine : null);
        box.ContentMarginLeft = box.ContentMarginRight = 7;
        box.ContentMarginTop = box.ContentMarginBottom = 1;
        chip.AddThemeStyleboxOverride("panel", box);
        var label = new Label { Text = pill.Text };
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", pill.Ink);
        chip.AddChild(label);
        return chip;
    }

    private static StyleBoxFlat Pad(StyleBoxFlat box, int vertical, int horizontal)
    {
        box.ContentMarginTop = box.ContentMarginBottom = vertical;
        box.ContentMarginLeft = box.ContentMarginRight = horizontal;
        return box;
    }

    private void OnLink(Variant meta)
    {
        var link = meta.AsString();
        if (link.StartsWith("rename-baby:", StringComparison.Ordinal) && int.TryParse(link["rename-baby:".Length..], out var id))
        {
            RenameBaby(id);
        }
        else if (link.StartsWith("result:", StringComparison.Ordinal) && int.TryParse(link["result:".Length..], out var index)
                 && _patient is { } patient && index >= 0 && index < patient.Investigations.Count)
        {
            ResultRequested?.Invoke(patient, patient.Investigations[index]);
        }
    }

    /// <summary>Asks for a baby's new name.</summary>
    private void RenameBaby(int babyId)
    {
        if (_sim.FindBaby(babyId) is not { } baby)
        {
            return;
        }

        var dialog = new AcceptDialog { Title = "Rename baby", OkButtonText = "Save", Theme = UiStyle.Theme };
        var edit = new LineEdit { Text = baby.FullName, CustomMinimumSize = new Vector2(320, 0) };
        dialog.AddChild(edit);
        dialog.RegisterTextEnter(edit);
        dialog.AddCancelButton("Cancel");
        dialog.Confirmed += () =>
        {
            if (!string.IsNullOrWhiteSpace(edit.Text))
            {
                _sim.RenameBaby(babyId, edit.Text.Trim());
                _lastPage = string.Empty;
                Refresh();
            }
        };
        dialog.VisibilityChanged += () =>
        {
            if (!dialog.Visible)
            {
                dialog.QueueFree();
            }
        };
        AddChild(dialog);
        dialog.PopupCentered();
        edit.GrabFocus();
        edit.SelectAll();
    }
}
