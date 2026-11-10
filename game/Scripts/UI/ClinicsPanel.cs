using System;
using System.Linq;
using System.Text;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// Outpatient clinics: the Outpatients reception desk, then each clinic's calendar of booked
/// patients for today or tomorrow, showing who has arrived, who is waiting and who has been seen.
/// Clicking a patient who is in the hospital finds them on the map.
/// </summary>
public partial class ClinicsPanel : PanelContainer
{
    private readonly RichTextLabel _text = new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Button _today = new() { Text = "Today", ToggleMode = true, FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Button _tomorrow = new() { Text = "Tomorrow", ToggleMode = true, FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private HospitalSimulation _sim = null!;
    private double _refresh;
    private int _dayOffset;
    private string _lastText = string.Empty;

    /// <summary>Raised when the player clicks a clinic patient who is in the hospital.</summary>
    public event Action<Outpatient>? OutpatientClicked;

    /// <summary>The clinic patient picked on the map, picked out in the calendar.</summary>
    public int? SelectedOutpatientId { get; set; }

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", HudStyle.DarkPanel());

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        var header = new HBoxContainer();
        var title = new Label { Text = "Clinics", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", UiStyle.TextDark);
        header.AddChild(title);
        var close = new Button { Text = "✕", ThemeTypeVariation = UiStyle.Secondary, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(40, 40), TooltipText = "Close (Esc)" };
        close.Pressed += () => Visible = false;
        header.AddChild(close);
        column.AddChild(header);

        var days = new HBoxContainer();
        days.AddThemeConstantOverride("separation", 6);
        var group = new ButtonGroup();
        foreach (var (button, offset) in new[] { (_today, 0), (_tomorrow, 1) })
        {
            button.ButtonGroup = group;
            button.ThemeTypeVariation = UiStyle.Secondary;
            button.CustomMinimumSize = new Vector2(0, 34);
            button.Pressed += () =>
            {
                _dayOffset = offset;
                Refresh();
            };
            days.AddChild(button);
        }

        _today.ButtonPressed = true;
        column.AddChild(days);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _text.AddThemeColorOverride("default_color", UiStyle.TextDark);
        _text.AddThemeFontSizeOverride("normal_font_size", 14);
        _text.AddThemeFontSizeOverride("bold_font_size", 14);
        _text.MetaUnderlined = false;
        _text.MetaClicked += meta =>
        {
            if (int.TryParse(meta.AsString(), out var id) && _sim.FindOutpatient(id) is { } outpatient)
            {
                OutpatientClicked?.Invoke(outpatient);
            }
        };
        scroll.AddChild(_text);
        column.AddChild(scroll);
    }

    /// <summary>Opens the calendar on today.</summary>
    public void ShowToday()
    {
        _dayOffset = 0;
        _today.ButtonPressed = true;
        Visible = true;
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        _refresh -= delta;
        if (_refresh <= 0)
        {
            Refresh();
        }
    }

    private const string Muted = "[color=#5B6B79]";

    public void Refresh()
    {
        _refresh = 0.5;
        var sb = new StringBuilder();
        var clinics = _sim.ClinicRooms.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
        var receptions = _sim.SpacesOfKind(CareSpaceKind.OutpatientReception).ToList();

        if (clinics.Count == 0 && receptions.Count == 0)
        {
            sb.Append($"{Muted}No clinics yet.\n\nIn Build mode, open the Clinics tab and build an Outpatients reception and a clinic room. " +
                      "Then hire a doctor posted to the clinic and a receptionist posted to the reception. " +
                      "The clinic books its patients two days ahead, Monday to Friday, 09:00 to 12:00 and 13:30 to 16:30.[/color]");
            SetText(sb);
            return;
        }

        // The reception desk and the waiting area.
        var queueing = _sim.Outpatients.Count(o => o.Stage is OutpatientStage.WalkingIn or OutpatientStage.Queueing or OutpatientStage.CheckingIn);
        var waiting = _sim.Outpatients.Count(o => o.Stage == OutpatientStage.Waiting);
        var chairs = _sim.OutpatientSeats.Count();
        sb.Append("[b]Outpatients reception[/b]\n");
        if (receptions.Count == 0)
        {
            sb.Append($"[color={ClinicalText.RedHex}]Not built. Clinic patients have nowhere to check in.[/color]\n");
        }
        else
        {
            foreach (var reception in receptions)
            {
                var desk = _sim.Staff.Where(s => s.BaseRoomId == reception.Id).Select(s => s.Name).ToList();
                sb.Append(desk.Count > 0
                    ? $"{reception.Name}: {string.Join(", ", desk)} on the desk\n"
                    : $"{reception.Name}: [color={ClinicalText.RedHex}]no receptionist[/color]\n");
            }
        }

        sb.Append($"{Muted}{queueing} checking in · {waiting} waiting · {chairs} corridor chairs[/color]\n");
        if (waiting > chairs)
        {
            sb.Append($"[color={ClinicalText.AmberTextHex}]Not enough chairs: some patients are standing. Place chairs along the corridors near the clinics from Build mode.[/color]\n");
        }

        var date = _sim.Now.Date.AddDays(_dayOffset);
        var dayName = _dayOffset == 0 ? "Today" : "Tomorrow";
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            sb.Append($"\n{Muted}{dayName} is {date:dddd}: clinics don't run at weekends.[/color]\n");
        }

        foreach (var clinic in clinics)
        {
            AppendClinic(sb, clinic, date);
        }

        SetText(sb);
    }

    private void SetText(StringBuilder sb)
    {
        var text = sb.ToString();
        if (text != _lastText)
        {
            _lastText = text;
            _text.Text = text;
        }
    }

    private void AppendClinic(StringBuilder sb, CareSpace clinic, DateTime date)
    {
        var doctors = _sim.Staff.Where(s => s.BaseRoomId == clinic.Id && s.Role == StaffRole.Doctor).ToList();
        var definition = _sim.ClinicOf(clinic);
        sb.Append($"\n[b]{clinic.Name}[/b]");
        if (definition is not null)
        {
            sb.Append($"{Muted}  {definition.AppointmentMinutes} min slots · £{definition.Tariff:N0} each");
            if (definition.Procedures.Count > 0)
            {
                sb.Append($" · procedures £{definition.Procedures.Min(p => p.Tariff):N0} to £{definition.Procedures.Max(p => p.Tariff):N0}");
            }

            sb.Append("[/color]");
        }

        sb.Append('\n');
        var problems = _sim.ClinicProblems(clinic);
        if (problems.Count > 0)
        {
            sb.Append($"[color={ClinicalText.RedHex}]Not running: needs {string.Join(" and ", problems)}.[/color]\n");
        }
        else
        {
            sb.Append($"{Muted}{string.Join(", ", doctors.Select(d => d.Name))}[/color]\n");
        }

        var calendar = _sim.CalendarFor(clinic.Id, date).ToList();
        if (calendar.Count == 0)
        {
            sb.Append($"{Muted}No appointments booked.[/color]\n");
            return;
        }

        int Count(AppointmentStatus status) => calendar.Count(a => a.Status == status);
        sb.Append($"{Muted}{calendar.Count} booked · {Count(AppointmentStatus.Seen)} seen · {Count(AppointmentStatus.DidNotAttend)} did not attend");
        if (Count(AppointmentStatus.Cancelled) > 0)
        {
            sb.Append($" · {Count(AppointmentStatus.Cancelled)} cancelled");
        }

        sb.Append("[/color]\n");
        sb.Append("[table=4]");
        foreach (var a in calendar)
        {
            var here = a.OutpatientId is { } oid && _sim.FindOutpatient(oid) is { Stage: not OutpatientStage.Leaving };
            var selected = a.OutpatientId is { } sid && sid == SelectedOutpatientId;
            var name = $"{a.FullName} ({a.Age}{(a.Sex == Sex.Female ? "F" : "M")})";
            if (here)
            {
                name = $"[url={a.OutpatientId}][color=#1F5FBF]{name}[/color][/url]";
            }

            if (selected)
            {
                name = $"[bgcolor=#FFF3B0]{name}[/bgcolor]";
            }

            Cell(sb, $"{a.Time:HH:mm}");
            Cell(sb, name);
            Cell(sb, a.IsProcedure ? $"[color=#1F5FBF]Procedure:[/color] {Muted}{a.Reason} ({a.Minutes} min)[/color]" : $"{Muted}{a.Reason}[/color]");
            Cell(sb, Status(a), right: true);
        }

        sb.Append("[/table]\n");
    }

    private string Status(Appointment a) => a.Status switch
    {
        AppointmentStatus.Booked => $"{Muted}Booked[/color]",
        AppointmentStatus.Arriving when a.OutpatientId is { } id && _sim.FindOutpatient(id) is { Stage: OutpatientStage.CheckingIn } => $"[color={ClinicalText.AmberTextHex}]Checking in[/color]",
        AppointmentStatus.Arriving => $"[color={ClinicalText.AmberTextHex}]Arriving[/color]",
        AppointmentStatus.Waiting => $"[color={ClinicalText.AmberTextHex}]Waiting {Math.Max(0, (int)(_sim.Now - (a.CheckedInAt ?? _sim.Now)).TotalMinutes)} min[/color]",
        AppointmentStatus.InClinic => "[color=#1F5FBF]In clinic[/color]",
        AppointmentStatus.Seen => $"[color={ClinicalText.GreenHex}]Seen {a.SeenAt:HH:mm}[/color]",
        AppointmentStatus.DidNotAttend => $"[color={ClinicalText.RedHex}]Did not attend[/color]",
        AppointmentStatus.Cancelled => $"{Muted}[s]Cancelled[/s][/color]",
        _ => a.Status.ToString(),
    };

    private static void Cell(StringBuilder sb, string text, bool right = false) =>
        sb.Append(right ? $"[cell][right]{text}[/right][/cell]" : $"[cell]{text}  [/cell]");
}
