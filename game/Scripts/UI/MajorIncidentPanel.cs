using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The major incident banner under the top bar and the card it opens: on standby, ambulance control's METHANE
/// message, the five get-ready actions and the casualties on their way; afterwards, the debrief. Opens by itself
/// when an incident is declared and when it stands down.
/// </summary>
public partial class MajorIncidentPanel : VBoxContainer
{
    private static readonly Color Dark = new("#5B6B79");

    private readonly Button _banner = new() { FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand };
    private readonly PanelContainer _card = new() { Visible = false };
    private readonly PanelContainer _header = new();
    private readonly Label _status = new();
    private readonly Label _title = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _clock = new();
    private readonly Label _lead = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly GridContainer _methane = new() { Columns = 2 };
    private readonly VBoxContainer _actions = new();
    private readonly VBoxContainer _casualties = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly ScrollContainer _casualtyScroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly Control _getReady = new VBoxContainer();
    private readonly HBoxContainer _free = new();
    private readonly VBoxContainer _left = new();
    private readonly VBoxContainer _debrief = new();
    private readonly Label _verdict = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly HBoxContainer _debriefFigures = new();
    private HospitalSimulation _sim = null!;
    private MajorIncident? _shown;
    private int _dismissed;
    private double _refresh;
    private string _actionsKey = string.Empty;

    /// <summary>The incident the banner is about, if any.</summary>
    public MajorIncident? Showing => _shown;

    public bool Expanded
    {
        get => _card.Visible;
        set => _card.Visible = value;
    }

    public void Initialise(HospitalSimulation simulation, Simulation.Core.EventBus events)
    {
        _sim = simulation;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 6);
        Visible = false;

        _banner.AddThemeFontSizeOverride("font_size", 15);
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color" })
        {
            _banner.AddThemeColorOverride(state, Colors.White);
        }

        _banner.Pressed += () => Expanded = !Expanded;
        AddChild(_banner);

        _card.AddThemeStyleboxOverride("panel", UiStyle.Card(0));
        AddChild(_card);
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 0);
        _card.AddChild(columns);

        // Left: what has happened and getting ready.
        _left.CustomMinimumSize = new Vector2(460, 0);
        _left.AddThemeConstantOverride("separation", 0);
        columns.AddChild(_left);
        _header.AddThemeStyleboxOverride("panel", HeaderBox(UiStyle.Red));
        _left.AddChild(_header);
        var headerRow = new HBoxContainer();
        _header.AddChild(headerRow);
        var headerText = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        headerText.AddThemeConstantOverride("separation", 2);
        headerRow.AddChild(headerText);
        _status.AddThemeFontSizeOverride("font_size", 12);
        _status.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.85f));
        headerText.AddChild(_status);
        UiStyle.Strong(_title, UiStyle.SizeTitle);
        _title.AddThemeColorOverride("font_color", Colors.White);
        headerText.AddChild(_title);
        _clock.AddThemeFontSizeOverride("font_size", 30);
        _clock.AddThemeColorOverride("font_color", Colors.White);
        _clock.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        headerRow.AddChild(_clock);

        var leftBody = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
        {
            leftBody.AddThemeConstantOverride(side, 14);
        }

        _left.AddChild(leftBody);
        var leftList = new VBoxContainer();
        leftList.AddThemeConstantOverride("separation", 6);
        leftBody.AddChild(leftList);
        _lead.AddThemeFontSizeOverride("font_size", 13);
        _lead.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        leftList.AddChild(_lead);
        _methane.AddThemeConstantOverride("h_separation", 14);
        _methane.AddThemeConstantOverride("v_separation", 2);
        leftList.AddChild(_methane);
        var ready = UiStyle.Caption("Get ready", 14);
        ready.AddThemeColorOverride("font_color", UiStyle.TextDark);
        ((VBoxContainer)_getReady).AddThemeConstantOverride("separation", 6);
        _getReady.AddChild(ready);
        _actions.AddThemeConstantOverride("separation", 5);
        _getReady.AddChild(_actions);
        leftList.AddChild(_getReady);

        // Right: the casualties, and afterwards the debrief.
        var right = new MarginContainer { CustomMinimumSize = new Vector2(480, 0) };
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
        {
            right.AddThemeConstantOverride(side, 16);
        }

        columns.AddChild(right);
        var rightList = new VBoxContainer();
        rightList.AddThemeConstantOverride("separation", 6);
        right.AddChild(rightList);
        var casualtiesTitle = UiStyle.Caption("Casualties", 17);
        casualtiesTitle.AddThemeColorOverride("font_color", UiStyle.TextDark);
        rightList.AddChild(casualtiesTitle);
        var casualtiesNote = UiStyle.Caption("Colours from the crews at the scene. Each comes in their own ambulance.", 12);
        casualtiesNote.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        casualtiesNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        rightList.AddChild(casualtiesNote);
        _casualties.AddThemeConstantOverride("separation", 4);
        _casualtyScroll.AddChild(_casualties);
        rightList.AddChild(_casualtyScroll);
        _free.AddThemeConstantOverride("separation", 8);
        rightList.AddChild(_free);

        _debrief.AddThemeConstantOverride("separation", 6);
        rightList.AddChild(_debrief);
        var debriefTitle = UiStyle.Caption("Afterwards: debrief", 17);
        debriefTitle.AddThemeColorOverride("font_color", UiStyle.TextDark);
        _debrief.AddChild(debriefTitle);
        _debriefFigures.AddThemeConstantOverride("separation", 8);
        _debrief.AddChild(_debriefFigures);
        _verdict.AddThemeFontSizeOverride("font_size", 13);
        _verdict.AddThemeColorOverride("font_color", UiStyle.TextDark);
        _debrief.AddChild(_verdict);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        rightList.AddChild(buttons);
        buttons.AddChild(UiStyle.SecondaryButton("Close", Close));

        events.Subscribe<MajorIncidentDeclared>(_ => CallDeferred(nameof(Open)));
        events.Subscribe<MajorIncidentStoodDown>(_ => CallDeferred(nameof(Open)));
    }

    private static StyleBoxFlat HeaderBox(Color colour)
    {
        var box = UiStyle.Box(colour, 0, margin: 14);
        box.CornerRadiusTopLeft = 14;
        box.CornerRadiusTopRight = 0;
        return box;
    }

    /// <summary>Opens the card (an incident has just been declared or stood down).</summary>
    public void Open()
    {
        Refresh();
        Expanded = _shown is not null;
    }

    /// <summary>Closes the card; once the incident is over, this puts the banner away too.</summary>
    public void Close()
    {
        Expanded = false;
        if (_shown is { IsActive: false } over)
        {
            _dismissed = over.Id;
            Refresh();
        }
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh > 0)
        {
            return;
        }

        _refresh = 0.25;
        Refresh();
    }

    /// <summary>Brings the banner and the card up to date (also called by the smoke test).</summary>
    public void Refresh()
    {
        var incident = _sim.Incident is { } i && (i.IsActive || i.Id != _dismissed) ? i : null;
        _shown = incident;
        Visible = incident is not null;
        if (incident is null)
        {
            Expanded = false;
            return;
        }

        var now = _sim.Now;
        var standby = incident.OnStandby(now);
        var colour = incident.IsActive ? UiStyle.Red : Dark;
        foreach (var state in new[] { "normal", "hover", "pressed" })
        {
            _banner.AddThemeStyleboxOverride(state, UiStyle.Box(state == "hover" ? colour.Lightened(0.08f) : colour, 8, margin: 8));
        }

        var here = incident.Casualties.Count(c => c.PatientId is not null);
        var minutes = Math.Max(0, (int)Math.Ceiling((incident.FirstArrival - now).TotalMinutes));
        _banner.Text = !incident.IsActive
            ? $"●  Major incident stood down · {incident.Name} · {incident.Debrief?.Survived ?? 0} of {incident.Casualties.Count} survived · click for the debrief"
            : standby
                ? $"●  MAJOR INCIDENT STANDBY · {incident.Name} · {incident.Casualties.Count} casualties, first in {minutes} min · click to get ready"
                : $"●  MAJOR INCIDENT · {incident.Name} · {here} of {incident.Casualties.Count} casualties here · click for details";

        if (!Expanded)
        {
            return;
        }

        _header.AddThemeStyleboxOverride("panel", HeaderBox(colour));
        _status.Text = !incident.IsActive ? "MAJOR INCIDENT · STOOD DOWN" : standby ? "MAJOR INCIDENT · STANDBY" : "MAJOR INCIDENT · DECLARED · CASUALTIES ARRIVING";
        _title.Text = $"{incident.Name}, {incident.Location}";
        _clock.Text = now.ToString("HH:mm");
        _lead.Text = !incident.IsActive
            ? $"Declared at {incident.DeclaredAt:HH:mm}, stood down at {incident.StoodDownAt:HH:mm}."
            : standby ? $"First casualties in {minutes} min. Ambulance control message (METHANE):" : $"Declared at {incident.DeclaredAt:HH:mm}. Ambulance control message (METHANE):";
        FillMethane(incident);
        _getReady.Visible = incident.IsActive;
        if (incident.IsActive)
        {
            FillActions(incident);
        }

        FillCasualties(incident, now);
        FillFree();
        _free.Visible = incident.IsActive;
        _debrief.Visible = incident.Debrief is not null;
        if (incident.Debrief is { } debrief)
        {
            FillDebrief(debrief);
        }
    }

    private void FillMethane(MajorIncident incident)
    {
        var kind = _sim.Database.Incidents.GetValueOrDefault(incident.IncidentId);
        var lines = new (string Letter, string Text, bool Strong)[]
        {
            ("M", "Major incident declared", false),
            ("E", $"Exact location: {incident.Location}", false),
            ("T", $"Type: {kind?.Type ?? incident.Name}", false),
            ("H", $"Hazards: {(string.IsNullOrEmpty(kind?.Hazards) ? "none known" : kind!.Hazards)}", false),
            ("A", $"Access: {(string.IsNullOrEmpty(kind?.Access) ? "no restriction" : kind!.Access)}", false),
            ("N", $"{incident.Casualties.Count} casualties: {incident.Count(TriageCategory.Red)} red, {incident.Count(TriageCategory.Amber)} amber, {incident.Count(TriageCategory.Green)} green", true),
            ("E", $"Emergency services: {(string.IsNullOrEmpty(kind?.Services) ? "ambulance" : kind!.Services)}", false),
        };
        if (_methane.GetChildCount() == lines.Length * 2)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                ((Label)_methane.GetChild((i * 2) + 1)).Text = lines[i].Text;
            }

            return;
        }

        foreach (var child in _methane.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var (letter, text, strong) in lines)
        {
            var l = new Label { Text = letter };
            l.AddThemeFontSizeOverride("font_size", 13);
            l.AddThemeColorOverride("font_color", UiStyle.Red);
            _methane.AddChild(l);
            var t = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };
            t.AddThemeFontSizeOverride("font_size", 13);
            t.AddThemeColorOverride("font_color", strong ? UiStyle.Red : UiStyle.TextDark);
            _methane.AddChild(t);
        }
    }

    private void FillActions(MajorIncident incident)
    {
        var actions = _sim.IncidentActionsNow();
        var key = string.Join("|", actions.Select(a => $"{a.Text}{a.Done}{a.Available}")) + incident.IsActive;
        if (key == _actionsKey)
        {
            return;
        }

        _actionsKey = key;
        foreach (var child in _actions.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var action in actions)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Surface, 8, UiStyle.Border, 8));
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            row.AddChild(line);
            var text = new Label { Text = action.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
            text.AddThemeFontSizeOverride("font_size", 13);
            text.AddThemeColorOverride("font_color", action.Available || action.Done ? UiStyle.TextDark : UiStyle.TextMuted);
            line.AddChild(text);
            if (action.Done)
            {
                var done = new Label { Text = "Done", VerticalAlignment = VerticalAlignment.Center, CustomMinimumSize = new Vector2(84, 30), HorizontalAlignment = HorizontalAlignment.Center };
                done.AddThemeColorOverride("font_color", Colors.White);
                done.AddThemeStyleboxOverride("normal", UiStyle.Box(UiStyle.Green, 6));
                line.AddChild(done);
            }
            else
            {
                var id = action.Id;
                var button = UiStyle.Button(action.Button, () =>
                {
                    _sim.DoIncidentAction(id);
                    _actionsKey = string.Empty;
                    Refresh();
                }, 84);
                button.Disabled = !action.Available || !incident.IsActive;
                line.AddChild(button);
            }

            _actions.AddChild(row);
        }
    }

    private void FillCasualties(MajorIncident incident, DateTime now)
    {
        foreach (var child in _casualties.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var c in incident.Casualties.OrderBy(c => c.Number))
        {
            var patient = c.PatientId is { } id ? _sim.FindPatient(id) : null;
            var item = new VBoxContainer();
            item.AddThemeConstantOverride("separation", 0);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            item.AddChild(row);
            var dot = new Label { Text = "●", CustomMinimumSize = new Vector2(12, 0) };
            dot.AddThemeColorOverride("font_color", c.Triage switch { TriageCategory.Red => UiStyle.Red, TriageCategory.Green => UiStyle.Green, _ => new Color("#E8A317") });
            row.AddChild(dot);
            var who = new Label { Text = $"Casualty {c.Number} · {c.Label}, {c.Age}{(patient is null ? string.Empty : $" · {patient.FullName}")}", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
            who.AddThemeFontSizeOverride("font_size", 13);
            who.AddThemeColorOverride("font_color", UiStyle.TextDark);
            row.AddChild(who);
            var how = c.Transport switch { CasualtyTransport.Air => "Air ambulance", CasualtyTransport.Walking => "Walking in", _ => "Ambulance" };
            var state = new Label { HorizontalAlignment = HorizontalAlignment.Right };
            state.AddThemeFontSizeOverride("font_size", 12);
            if (patient is null)
            {
                state.Text = $"{how} · {Math.Max(0, (int)Math.Ceiling((c.ArriveAt - now).TotalMinutes))} min";
                state.AddThemeColorOverride("font_color", UiStyle.AmberText);
            }
            else
            {
                state.Text = !patient.IsAlive ? "Died" : patient.LeftEmergencyAt is not null || !patient.IsInDepartment ? Left(patient) : Where(patient);
                state.AddThemeColorOverride("font_color", !patient.IsAlive ? UiStyle.Red : UiStyle.Green);
            }

            row.AddChild(state);
            var condition = _sim.Database.Conditions.GetValueOrDefault(c.ConditionId);
            var what = new Label { Text = Capitalise(condition?.CrewAssessment is { Length: > 0 } a ? a : condition?.Name ?? string.Empty), ClipText = true };
            what.AddThemeFontSizeOverride("font_size", 12);
            what.AddThemeColorOverride("font_color", UiStyle.TextMuted);
            var indent = new MarginContainer();
            indent.AddThemeConstantOverride("margin_left", 20);
            indent.AddChild(what);
            item.AddChild(indent);
            _casualties.AddChild(item);
        }

        _casualtyScroll.CustomMinimumSize = new Vector2(0, Math.Min(incident.Casualties.Count * 44, 280));
    }

    private string Where(Simulation.Patients.Patient patient) =>
        patient.CareSpaceId is { } id && _sim.FindSpace(id) is { } space ? space.Name
        : patient.Stage == Simulation.Patients.PatientStage.InAmbulanceQueue ? "At the ambulance bay"
        : "In the waiting room";

    private static string Left(Simulation.Patients.Patient patient) => patient.Outcome switch
    {
        Simulation.Patients.PatientOutcome.Admitted => "Admitted",
        Simulation.Patients.PatientOutcome.Transferred => "Transferred",
        Simulation.Patients.PatientOutcome.Discharged => "Home",
        _ => patient.Stage == Simulation.Patients.PatientStage.OnWard ? "On a ward" : "Left A&E",
    };

    private void FillFree()
    {
        foreach (var child in _free.GetChildren())
        {
            child.QueueFree();
        }

        int Free(CareSpaceKind kind) => _sim.Spaces.Count(s => s.Kind == kind && s.IsFree);
        _free.AddChild(Figure("Free Resus bays", Free(CareSpaceKind.Resus).ToString()));
        _free.AddChild(Figure("Free majors", Free(CareSpaceKind.Majors).ToString()));
        _free.AddChild(Figure("Free theatres", Free(CareSpaceKind.Theatre).ToString()));
    }

    private void FillDebrief(IncidentDebrief debrief)
    {
        foreach (var child in _debriefFigures.GetChildren())
        {
            child.QueueFree();
        }

        _debriefFigures.AddChild(Figure("Survived", $"{debrief.Survived} / {debrief.Total}", debrief.Survived == debrief.Total ? UiStyle.Green : UiStyle.Red));
        _debriefFigures.AddChild(Figure("Red seen by a doctor in", debrief.RedToDoctorMinutes is { } m ? $"{m:0} min" : "–"));
        _debriefFigures.AddChild(Figure("Overtime", debrief.OvertimeCost > 0 ? $"£{debrief.OvertimeCost:N0}" : "£0"));
        _verdict.Text = $"{new string('★', debrief.Stars)}{new string('☆', 5 - debrief.Stars)}  {debrief.Verdict}";
    }

    private static Control Figure(string caption, string value, Color? colour = null)
    {
        var box = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Ground, 10, margin: 10));
        var list = new VBoxContainer();
        box.AddChild(list);
        var c = UiStyle.Caption(caption, 12);
        c.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        c.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        list.AddChild(c);
        var v = new Label { Text = value };
        UiStyle.Strong(v, UiStyle.SizeFigure);
        v.AddThemeColorOverride("font_color", colour ?? UiStyle.TextDark);
        list.AddChild(v);
        return box;
    }

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
