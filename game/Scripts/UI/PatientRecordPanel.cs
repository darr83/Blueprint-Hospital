using System;
using System.Linq;
using System.Text;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The selected patient's A&amp;E record: demographics, triage, observations and NEWS2, symptoms,
/// investigations and results, diagnosis, treatment, background and a timeline of everything that happened.
/// Shows only what the clinical team knows, never the patient's true condition.
/// </summary>
public partial class PatientRecordPanel : PanelContainer
{
    private readonly RichTextLabel _text = new()
    {
        BbcodeEnabled = true,
        FitContent = true,
        ScrollActive = false,
        SelectionEnabled = true,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };

    private readonly ScrollContainer _scroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly Label _title = new();
    private HospitalSimulation _sim = null!;
    private Patient? _patient;
    private string _lastText = string.Empty;
    private double _refresh;

    /// <summary>Raised when the player closes the record.</summary>
    public event Action? CloseRequested;

    /// <summary>Raised when the player asks to see the patient on the map.</summary>
    public event Action<Patient>? FocusRequested;

    public Patient? Patient => _patient;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        CustomMinimumSize = new Vector2(400, 0);
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", UiStyle.Card(20));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 14);
        _avatar = HudWidgets.Avatar();
        header.AddChild(_avatar.Badge);
        var who = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        who.AddThemeConstantOverride("separation", 0);
        _title.AddThemeFontSizeOverride("font_size", 20);
        _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        who.AddChild(_title);
        who.AddChild(_subtitle);
        header.AddChild(who);
        var close = UiStyle.SecondaryButton("✕", () => CloseRequested?.Invoke());
        close.TooltipText = "Close (Esc)";
        close.CustomMinimumSize = new Vector2(40, 40);
        header.AddChild(close);
        column.AddChild(header);

        var chips = new HBoxContainer();
        chips.AddThemeConstantOverride("separation", 6);
        _triageChip = HudWidgets.Chip();
        _placeChip = HudWidgets.Chip();
        _arrivalChip = HudWidgets.Chip();
        chips.AddChild(_triageChip.Chip);
        chips.AddChild(_placeChip.Chip);
        chips.AddChild(_arrivalChip.Chip);
        column.AddChild(chips);

        _now = HudWidgets.FocusCard();
        column.AddChild(_now.Box);

        var tiles = new HBoxContainer();
        tiles.AddThemeConstantOverride("separation", 8);
        _timeTile = HudWidgets.StatTile("In A&E");
        _painTile = HudWidgets.StatTile("Pain");
        _newsTile = HudWidgets.StatTile("NEWS2");
        tiles.AddChild(_timeTile.Tile);
        tiles.AddChild(_painTile.Tile);
        tiles.AddChild(_newsTile.Tile);
        column.AddChild(tiles);
        column.AddChild(HudWidgets.Divider());

        _text.AddThemeColorOverride("default_color", UiStyle.TextDark);
        _text.AddThemeFontSizeOverride("normal_font_size", 14);
        _text.AddThemeFontSizeOverride("bold_font_size", 14);
        _scroll.AddChild(_text);
        column.AddChild(_scroll);

        var find = UiStyle.Button("Find on map", () =>
        {
            if (_patient is not null)
            {
                FocusRequested?.Invoke(_patient);
            }
        });
        find.TooltipText = "Centre the view on this patient";
        find.CustomMinimumSize = new Vector2(0, 44);
        column.AddChild(find);
    }

    private readonly Label _subtitle = UiStyle.Caption(string.Empty, 14);
    private (PanelContainer Badge, Label Initials) _avatar;
    private (PanelContainer Chip, Label Text) _triageChip;
    private (PanelContainer Chip, Label Text) _placeChip;
    private (PanelContainer Chip, Label Text) _arrivalChip;
    private (PanelContainer Box, Label Heading, Label Text, Label Detail) _now;
    private (PanelContainer Tile, Label Value) _timeTile;
    private (PanelContainer Tile, Label Value) _painTile;
    private (PanelContainer Tile, Label Value) _newsTile;

    /// <summary>The header: who they are, triage, where they are and the headline figures.</summary>
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
        HudWidgets.SetChip(_arrivalChip, p.ArrivalMethod == ArrivalMethod.Ambulance ? "Ambulance" : "Walked in", UiStyle.Ground);

        _now.Heading.Text = p.IsInDepartment ? "Right now" : "Left the department";
        _now.Text.Text = ClinicalText.Whereabouts(_sim, p);
        _now.Detail.Text = p.WorkingDiagnosis is { } dx ? $"Working diagnosis: {_sim.Database.Conditions[dx.ConditionId].Name}" : p.Triage is null ? string.Empty : "No diagnosis yet";
        _now.Detail.Visible = _now.Detail.Text.Length > 0;

        var time = p.TimeInDepartment(now);
        _timeTile.Value.Text = ClinicalText.Duration(time);
        _timeTile.Value.AddThemeColorOverride("font_color", time > TimeSpan.FromHours(4) && p.IsInDepartment ? UiStyle.Red : time > TimeSpan.FromHours(2) && p.IsInDepartment ? UiStyle.AmberText : UiStyle.TextDark);
        if (p.LatestObservations is { } latest)
        {
            _painTile.Value.Text = $"{latest.Obs.PainScore}/10";
            _newsTile.Value.Text = latest.News2.Total.ToString();
            _newsTile.Value.AddThemeColorOverride("font_color", latest.News2.Total >= 5 ? UiStyle.Red : UiStyle.TextDark);
        }
        else
        {
            _painTile.Value.Text = "–";
            _newsTile.Value.Text = "–";
            _newsTile.Value.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        }
    }

    public void Show(Patient patient)
    {
        var changed = !ReferenceEquals(patient, _patient);
        _patient = patient;
        Visible = true;
        if (changed)
        {
            _lastText = string.Empty;
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
        if (_patient is null)
        {
            return;
        }

        RefreshHeader(_patient);
        var text = Build(_patient);
        if (text == _lastText)
        {
            return;
        }

        var scroll = _scroll.ScrollVertical;
        _text.Text = text;
        _lastText = text;
        _scroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, scroll);
    }

    private string Build(Patient p)
    {
        var db = _sim.Database;
        var now = _sim.Now;
        var sb = new StringBuilder();
        string E(string s) => ClinicalText.Escape(s);
        void Section(string name) => sb.Append($"\n[color=#1F5FBF][b]{name}[/b][/color]\n");

        sb.Append($"{p.Sex}, {p.Age} · {p.HospitalNumber} · DOB {p.DateOfBirth:dd/MM/yyyy}\n");
        var how = p.ArrivalMethod == ArrivalMethod.Ambulance ? "by ambulance" : "walked in";
        sb.Append($"Arrived {p.ArrivedAt:HH:mm} ({how}) · [b]{ClinicalText.Duration(p.TimeInDepartment(now))}[/b] in A&E");
        if (p.TimeInDepartment(now) > TimeSpan.FromHours(4) && p.IsInDepartment)
        {
            sb.Append($" [color={ClinicalText.RedHex}][b]· over 4 hours[/b][/color]");
        }

        sb.Append('\n');

        if (p.Triage is not null)
        {
            sb.Append($"[b]Triage:[/b] {E(p.TriageReason)}\n");
        }

        if (!string.IsNullOrEmpty(p.OutcomeNote) && !p.IsInDepartment)
        {
            sb.Append($"[b]Outcome:[/b] {E(p.OutcomeNote)}\n");
        }

        sb.Append($"[b]Presenting complaint:[/b] {E(db.Symptoms[p.PresentingComplaint].Name)}\n");
        if (p.AmbulanceHandover is { } handover)
        {
            sb.Append($"[b]Ambulance handover:[/b] [i]{E(handover)}[/i]\n");
        }

        // Observations.
        Section("OBSERVATIONS");
        if (p.LatestObservations is { } latest)
        {
            var (obs, news) = latest;
            var lastTime = p.ObservationHistory[^1].Time;
            sb.Append($"NEWS2 [color={ClinicalText.News2Hex(news.Total)}][b]{news.Total}[/b][/color]");
            if (news.HasRedScore)
            {
                sb.Append($" [color={ClinicalText.RedHex}](single parameter scoring 3)[/color]");
            }

            sb.Append($" · taken {lastTime:HH:mm}\n");
            sb.Append("[table=8]");
            foreach (var h in new[] { "Time", "HR", "BP", "RR", "SpO₂", "Temp", "ACVPU", "NEWS2" })
            {
                sb.Append($"[cell][color=#5B6B79]{h}  [/color][/cell]");
            }

            foreach (var (time, o, n) in p.ObservationHistory.Reverse().Take(6))
            {
                sb.Append($"[cell]{time:HH:mm}  [/cell][cell]{o.HeartRate}  [/cell][cell]{o.BloodPressure}  [/cell][cell]{o.RespiratoryRate}  [/cell]");
                sb.Append($"[cell]{o.OxygenSaturation}%  [/cell][cell]{o.Temperature:0.0}  [/cell][cell]{o.Consciousness.ToString()[0]}  [/cell]");
                sb.Append($"[cell][color={ClinicalText.News2Hex(n.Total)}][b]{n.Total}[/b][/color][/cell]");
            }

            sb.Append("[/table]\n");
            sb.Append($"Pain {obs.PainScore}/10\n");
        }
        else
        {
            sb.Append("[color=#5B6B79]No observations recorded yet.[/color]\n");
        }

        // Symptoms the team knows about.
        Section("SYMPTOMS");
        var symptoms = p.HistoryTaken ? p.Symptoms : p.KnownSymptoms.ToList();
        sb.Append(symptoms.Count == 0
            ? "[color=#5B6B79]Not yet assessed.[/color]\n"
            : string.Join(", ", symptoms.Select(s => db.Symptoms.TryGetValue(s, out var d) ? E(d.Name) : s)) + (p.HistoryTaken ? "" : " [color=#5B6B79](full history not yet taken)[/color]") + "\n");

        // Investigations.
        Section("INVESTIGATIONS");
        if (p.Investigations.Count == 0)
        {
            sb.Append("[color=#5B6B79]None requested.[/color]\n");
        }

        foreach (var inv in p.Investigations)
        {
            var name = db.Investigations[inv.InvestigationId].Name;
            switch (inv.Status)
            {
                case InvestigationStatus.Resulted:
                    var colour = inv.Abnormal ? ClinicalText.RedHex : GreenHex;
                    sb.Append($"[b]{E(name)}[/b] [color=#5B6B79]{inv.ResultedAt:HH:mm}[/color]\n");
                    sb.Append($"   [color={colour}]{(inv.Abnormal ? "ABNORMAL: " : "")}{E(inv.Summary)}[/color]\n");
                    break;
                case InvestigationStatus.SampleTaken:
                    var due = inv.ResultDueAt is { } d ? $", due about {d:HH:mm}" : string.Empty;
                    sb.Append($"[b]{E(name)}[/b] [color=#5B6B79]sample sent to lab{due}[/color]\n");
                    break;
                default:
                    sb.Append($"[b]{E(name)}[/b] [color=#5B6B79]requested {inv.RequestedAt:HH:mm}, not yet done[/color]\n");
                    break;
            }
        }

        // Diagnosis.
        Section("DIAGNOSIS");
        if (p.WorkingDiagnosis is { } dx)
        {
            var doctor = _sim.FindStaff(dx.DoctorId)?.Name ?? "a doctor";
            sb.Append($"[b]{E(db.Conditions[dx.ConditionId].Name)}[/b] ({dx.Confidence:P0} confident)\n");
            sb.Append($"[color=#5B6B79]Made by {E(doctor)} at {dx.MadeAt:HH:mm}[/color]\n");
        }
        else
        {
            sb.Append(p.FirstSeenByDoctorAt is null ? "[color=#5B6B79]Not yet seen by a doctor.[/color]\n" : "[color=#5B6B79]Working diagnosis not yet made.[/color]\n");
        }

        // Treatment.
        if (p.TreatmentPlan.Count > 0)
        {
            Section("TREATMENT");
            foreach (var t in p.TreatmentPlan)
            {
                var label = t.Step.Type == TreatmentStepType.Medication && t.Step.Medication is { } m && db.Medications.TryGetValue(m, out var med)
                    ? med.Name
                    : t.Step.Describe();
                var mark = t.Withheld ? $"[color={ClinicalText.RedHex}]✗[/color]" : t.Done ? $"[color={GreenHex}]✓[/color]" : "[color=#5B6B79]○[/color]";
                sb.Append($"{mark} {E(label)}");
                if (!string.IsNullOrEmpty(t.Note))
                {
                    sb.Append($" [color=#5B6B79]{E(t.Note)}[/color]");
                }

                sb.Append('\n');
            }
        }

        // Background.
        Section("BACKGROUND");
        sb.Append($"[b]Past history:[/b] {(p.PastHistory.Count == 0 ? "none of note" : E(string.Join(", ", p.PastHistory)))}\n");
        sb.Append($"[b]Regular medication:[/b] {(p.RegularMedications.Count == 0 ? "none" : E(string.Join(", ", p.RegularMedications)))}\n");
        sb.Append(p.Allergies.Count == 0
            ? "[b]Allergies:[/b] no known drug allergies\n"
            : $"[b]Allergies:[/b] [color={ClinicalText.RedHex}][b]{E(string.Join(", ", p.Allergies))}[/b][/color]\n");
        sb.Append($"[b]Previous A&E visits:[/b] {p.PreviousVisits}\n");

        // Timeline, newest first.
        Section("TIMELINE");
        foreach (var entry in p.Timeline.Reverse())
        {
            var text = E(entry.Text);
            sb.Append($"[color=#5B6B79]{entry.Time:HH:mm}[/color]  {(entry.Important ? $"[b]{text}[/b]" : text)}\n");
        }

        return sb.ToString();
    }

    private const string GreenHex = ClinicalText.GreenHex;
}
