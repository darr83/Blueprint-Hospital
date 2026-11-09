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
        header.AddChild(NameEditing.Attach(who, _title, () => _patient?.FullName, name =>
        {
            if (_patient is { } p)
            {
                _sim.RenamePatient(p.Id, name);
                Refresh();
            }
        }));
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
        _text.MetaClicked += meta =>
        {
            var link = meta.AsString();
            if (link.StartsWith("rename-baby:", StringComparison.Ordinal) && int.TryParse(link["rename-baby:".Length..], out var id))
            {
                RenameBaby(id);
            }
        };
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
        HudWidgets.SetChip(_arrivalChip, p.Maternity ? "Maternity" : p.ArrivalMethod == ArrivalMethod.Ambulance ? "Ambulance" : "Walked in", UiStyle.Ground);

        _now.Heading.Text = p.IsInDepartment ? "Right now" : "Left the department";
        _now.Text.Text = ClinicalText.Whereabouts(_sim, p);
        _now.Detail.Text = p.WorkingDiagnosis is { } dx ? $"Working diagnosis: {_sim.Database.Conditions[dx.ConditionId].Name}" : p.Triage is null ? string.Empty : "No diagnosis yet";
        if (p.MonitorIsRed)
        {
            var sitter = _sim.CarerAtBedside(p);
            var with = sitter is not null ? $"{sitter.Name} is sitting with them" : "nobody sitting with them yet";
            var review = p.RedReviewed ? "doctor has reviewed" : "waiting for a doctor's review";
            var trend = sitter is not null && p.RedReviewed ? "improving" : "getting worse";
            var redLine = $"Red monitor (NEWS2 {p.LiveNews2}): {with}, {review}; {trend}";
            _now.Detail.Text = _now.Detail.Text.Length > 0 ? $"{_now.Detail.Text}\n{redLine}" : redLine;
        }

        _now.Detail.Visible = _now.Detail.Text.Length > 0;

        // Once admitted the A&E clock stops: the tile shows their length of stay instead.
        var time = p.Maternity ? now - p.ArrivedAt : p.LengthOfStay(now) ?? p.TimeInEmergency(now);
        var counting = p.EmergencyClockRunning;
        ((Label)_timeTile.Value.GetParent().GetChild(1)).Text = p.Maternity ? "In hospital" : p.AdmissionType is { } type ? $"{type} stay" : "In A&E";
        _timeTile.Value.Text = ClinicalText.Duration(time);
        _timeTile.Value.AddThemeColorOverride("font_color", time > Patient.FourHours && counting ? UiStyle.Red : time > TimeSpan.FromHours(2) && counting ? UiStyle.AmberText : UiStyle.TextDark);
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
        if (p.Maternity)
        {
            sb.Append($"Came to the maternity unit at {p.ArrivedAt:HH:mm} · [b]{ClinicalText.Duration(now - p.ArrivedAt)}[/b] in hospital");
        }
        else
        {
            sb.Append($"Arrived {p.ArrivedAt:HH:mm} ({how}) · [b]{ClinicalText.Duration(p.TimeInEmergency(now))}[/b] in A&E");
        }

        if (p.OverFourHours(now))
        {
            sb.Append($" [color={ClinicalText.RedHex}][b]· over 4 hours[/b][/color]");
        }

        sb.Append('\n');
        if (p.AdmissionType is { } admission && p.AdmittedAt is { } admittedAt)
        {
            var stay = ClinicalText.Duration(p.LengthOfStay(now)!.Value);
            sb.Append($"[b]{admission}[/b] · admitted {admittedAt:HH:mm} · length of stay [b]{stay}[/b]");
            if (!p.Maternity)
            {
                sb.Append(p.TimeInEmergency(now) <= Patient.FourHours ? " · left A&E within 4 hours" : $" [color={ClinicalText.RedHex}]· left A&E after 4 hours[/color]");
            }

            sb.Append('\n');
        }

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

        if (p.Maternity)
        {
            MaternitySection(p, sb, Section);
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

    /// <summary>The pregnancy and the babies, each with a link to rename them.</summary>
    private void MaternitySection(Patient p, StringBuilder sb, Action<string> section)
    {
        string E(string s) => ClinicalText.Escape(s);
        section(p.Delivered ? "MOTHER AND BABY" : "PREGNANCY");
        sb.Append($"[b]{p.GestationWeeks} weeks[/b] pregnant{(p.Delivered ? " at delivery" : "")}\n");
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
            sb.Append($"[color={colour}]●[/color] [b]{E(baby.FullName)}[/b] [url=rename-baby:{baby.Id}][color=#1F5FBF](rename)[/color][/url]\n");
            sb.Append($"   {(baby.Sex == Sex.Female ? "Girl" : "Boy")}, {baby.WeightKg:0.00} kg, born {baby.BornAt:HH:mm} ({E(baby.Delivery.ToLowerInvariant())}), {place}\n");
            if (baby.NeonatalReason is { } reason)
            {
                sb.Append($"   [color=#5B6B79]Neonatal care: {E(reason)}[/color]\n");
            }

            foreach (var entry in baby.Timeline.Where(t => t.Important).Reverse().Take(3))
            {
                sb.Append($"   [color=#5B6B79]{entry.Time:HH:mm}  {E(entry.Text)}[/color]\n");
            }
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
                _lastText = string.Empty;
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
