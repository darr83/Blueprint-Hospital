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
        CustomMinimumSize = new Vector2(440, 0);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(UiStyle.PanelBackground, 0.97f),
            CornerRadiusTopLeft = 8, CornerRadiusBottomLeft = 8,
            ContentMarginLeft = 16, ContentMarginRight = 12, ContentMarginTop = 12, ContentMarginBottom = 12,
            ShadowColor = new Color(0, 0, 0, 0.3f), ShadowSize = 8,
        });

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        var header = new HBoxContainer();
        _title.AddThemeFontSizeOverride("font_size", 15);
        _title.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        _title.Text = "PATIENT RECORD";
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(_title);
        var find = UiStyle.Button("Find", () =>
        {
            if (_patient is not null)
            {
                FocusRequested?.Invoke(_patient);
            }
        });
        find.TooltipText = "Centre the view on this patient";
        find.CustomMinimumSize = new Vector2(0, 32);
        header.AddChild(find);
        var close = UiStyle.Button("✕", () => CloseRequested?.Invoke());
        close.TooltipText = "Close (Esc)";
        close.CustomMinimumSize = new Vector2(36, 32);
        header.AddChild(close);
        column.AddChild(header);

        _text.AddThemeColorOverride("default_color", UiStyle.TextDark);
        _text.AddThemeFontSizeOverride("normal_font_size", 15);
        _text.AddThemeFontSizeOverride("bold_font_size", 15);
        _scroll.AddChild(_text);
        column.AddChild(_scroll);
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
        void Section(string name) => sb.Append($"\n[color=#005EB8][b]{name}[/b][/color]\n");

        sb.Append($"[font_size=24][b]{E(p.FullName)}[/b][/font_size]\n");
        sb.Append($"{p.Sex}, {p.Age} · {p.HospitalNumber} · DOB {p.DateOfBirth:dd/MM/yyyy}\n");
        var how = p.ArrivalMethod == ArrivalMethod.Ambulance ? "by ambulance" : "walked in";
        sb.Append($"Arrived {p.ArrivedAt:HH:mm} ({how}) · [b]{ClinicalText.Duration(p.TimeInDepartment(now))}[/b] in A&E");
        if (p.TimeInDepartment(now) > TimeSpan.FromHours(4) && p.IsInDepartment)
        {
            sb.Append($" [color={ClinicalText.RedHex}][b]· over 4 hours[/b][/color]");
        }

        sb.Append('\n');

        var hex = ClinicalText.TriageHex(p.Triage);
        sb.Append($"\n[bgcolor={hex}][color=#FFFFFF][b]  {ClinicalText.TriageName(p.Triage)}  [/b][/color][/bgcolor]  ");
        sb.Append(p.Triage is null ? "Awaiting triage" : E(p.TriageReason));
        sb.Append('\n');
        sb.Append($"[b]Location:[/b] {E(ClinicalText.Whereabouts(_sim, p))}\n");
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
                sb.Append($"[cell][color=#4C6272]{h}  [/color][/cell]");
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
            sb.Append("[color=#4C6272]No observations recorded yet.[/color]\n");
        }

        // Symptoms the team knows about.
        Section("SYMPTOMS");
        var symptoms = p.HistoryTaken ? p.Symptoms : p.KnownSymptoms.ToList();
        sb.Append(symptoms.Count == 0
            ? "[color=#4C6272]Not yet assessed.[/color]\n"
            : string.Join(", ", symptoms.Select(s => db.Symptoms.TryGetValue(s, out var d) ? E(d.Name) : s)) + (p.HistoryTaken ? "" : " [color=#4C6272](full history not yet taken)[/color]") + "\n");

        // Investigations.
        Section("INVESTIGATIONS");
        if (p.Investigations.Count == 0)
        {
            sb.Append("[color=#4C6272]None requested.[/color]\n");
        }

        foreach (var inv in p.Investigations)
        {
            var name = db.Investigations[inv.InvestigationId].Name;
            switch (inv.Status)
            {
                case InvestigationStatus.Resulted:
                    var colour = inv.Abnormal ? ClinicalText.RedHex : GreenHex;
                    sb.Append($"[b]{E(name)}[/b] [color=#4C6272]{inv.ResultedAt:HH:mm}[/color]\n");
                    sb.Append($"   [color={colour}]{(inv.Abnormal ? "ABNORMAL: " : "")}{E(inv.Summary)}[/color]\n");
                    break;
                case InvestigationStatus.SampleTaken:
                    var due = inv.ResultDueAt is { } d ? $", due about {d:HH:mm}" : string.Empty;
                    sb.Append($"[b]{E(name)}[/b] [color=#4C6272]sample sent to lab{due}[/color]\n");
                    break;
                default:
                    sb.Append($"[b]{E(name)}[/b] [color=#4C6272]requested {inv.RequestedAt:HH:mm}, not yet done[/color]\n");
                    break;
            }
        }

        // Diagnosis.
        Section("DIAGNOSIS");
        if (p.WorkingDiagnosis is { } dx)
        {
            var doctor = _sim.FindStaff(dx.DoctorId)?.Name ?? "a doctor";
            sb.Append($"[b]{E(db.Conditions[dx.ConditionId].Name)}[/b] ({dx.Confidence:P0} confident)\n");
            sb.Append($"[color=#4C6272]Made by {E(doctor)} at {dx.MadeAt:HH:mm}[/color]\n");
        }
        else
        {
            sb.Append(p.FirstSeenByDoctorAt is null ? "[color=#4C6272]Not yet seen by a doctor.[/color]\n" : "[color=#4C6272]Working diagnosis not yet made.[/color]\n");
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
                var mark = t.Withheld ? $"[color={ClinicalText.RedHex}]✗[/color]" : t.Done ? $"[color={GreenHex}]✓[/color]" : "[color=#4C6272]○[/color]";
                sb.Append($"{mark} {E(label)}");
                if (!string.IsNullOrEmpty(t.Note))
                {
                    sb.Append($" [color=#4C6272]{E(t.Note)}[/color]");
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
            sb.Append($"[color=#4C6272]{entry.Time:HH:mm}[/color]  {(entry.Important ? $"[b]{text}[/b]" : text)}\n");
        }

        return sb.ToString();
    }

    private const string GreenHex = ClinicalText.GreenHex;
}
