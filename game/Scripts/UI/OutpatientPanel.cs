using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// A clinic patient's card, in the same style as the A&amp;E patient record: who they are, where they have got to
/// (Booked → Arrived → Checked in → Waiting → With doctor → Home), what is happening right now and their appointment.
/// </summary>
public partial class OutpatientPanel : PanelContainer
{
    private readonly (PanelContainer Badge, Label Initials) _avatar = HudWidgets.Avatar(52);
    private readonly Label _name = new() { TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
    private readonly Label _sub = UiStyle.Caption(string.Empty, UiStyle.SizeSmall);
    private readonly HFlowContainer _chips = new();
    private readonly List<(PanelContainer Chip, Label Text)> _chipParts = new();
    private readonly JourneyBar _journey = new();
    private readonly (PanelContainer Box, Label Heading, Label Text, Label Detail) _now = HudWidgets.FocusCard();
    private readonly RichTextLabel _facts = new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private HospitalSimulation _sim = null!;
    private double _refresh;

    /// <summary>The clinic patient showing.</summary>
    public int? OutpatientId { get; private set; }

    /// <summary>Raised for "Show in clinic calendar".</summary>
    public event Action<Outpatient>? CalendarRequested;

    /// <summary>Raised for "Find on map".</summary>
    public event Action<Outpatient>? FindRequested;

    public JourneyBar Journey => _journey;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", HudStyle.DarkPanel());

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        header.AddChild(_avatar.Badge);
        var names = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        names.AddThemeConstantOverride("separation", 0);
        UiStyle.Strong(_name, UiStyle.SizeTitle + 2);
        names.AddChild(_name);
        names.AddChild(_sub);
        header.AddChild(names);
        var rename = NameEditing.Attach(names, _name, () => Appointment()?.FullName, Rename);
        header.AddChild(rename);
        header.AddChild(UiStyle.CloseButton(Close));
        column.AddChild(header);

        _chips.AddThemeConstantOverride("h_separation", 6);
        _chips.AddThemeConstantOverride("v_separation", 6);
        for (var i = 0; i < 3; i++)
        {
            var chip = HudWidgets.Chip();
            _chipParts.Add(chip);
            _chips.AddChild(chip.Chip);
        }

        column.AddChild(_chips);

        var journeyBox = new PanelContainer();
        journeyBox.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.GreenTint, UiStyle.RadiusCard, null, 8));
        journeyBox.AddChild(_journey);
        column.AddChild(journeyBox);

        column.AddChild(_now.Box);

        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", UiStyle.Inset(14));
        var cardColumn = new VBoxContainer();
        cardColumn.AddThemeConstantOverride("separation", 4);
        var heading = UiStyle.Caption("APPOINTMENT", 12);
        heading.AddThemeColorOverride("font_color", UiStyle.Blue);
        UiStyle.Strong(heading);
        cardColumn.AddChild(heading);
        _facts.AddThemeColorOverride("default_color", UiStyle.TextDark);
        cardColumn.AddChild(_facts);
        card.AddChild(cardColumn);
        column.AddChild(card);

        column.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        var calendar = UiStyle.SecondaryButton("Show in clinic calendar", () =>
        {
            if (Outpatient() is { } o)
            {
                CalendarRequested?.Invoke(o);
            }
        });
        calendar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttons.AddChild(calendar);
        var find = UiStyle.Button("Find on map", () =>
        {
            if (Outpatient() is { } o)
            {
                FindRequested?.Invoke(o);
            }
        });
        find.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttons.AddChild(find);
        column.AddChild(buttons);
    }

    private Outpatient? Outpatient() => OutpatientId is { } id ? _sim.FindOutpatient(id) : null;

    private Appointment? Appointment() => Outpatient() is { } o ? _sim.FindAppointment(o.AppointmentId) : null;

    private void Rename(string name)
    {
        if (Appointment() is not { } a)
        {
            return;
        }

        var parts = name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return;
        }

        a.FirstName = parts[0];
        if (parts.Length > 1)
        {
            a.Surname = parts[1];
        }

        Refresh();
    }

    public void Show(Outpatient outpatient)
    {
        OutpatientId = outpatient.Id;
        Visible = true;
        Refresh();
    }

    public void Close()
    {
        Visible = false;
        OutpatientId = null;
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

    private void Refresh()
    {
        _refresh = 0.5;
        var o = Outpatient();
        var a = Appointment();
        if (o is null || a is null)
        {
            Close();
            return;
        }

        var clinic = _sim.FindSpace(a.ClinicId);
        var department = clinic is not null && _sim.Database.RoomTemplates.TryGetValue(clinic.TemplateId, out var template) && _sim.Database.Departments.TryGetValue(template.Department, out var dept) ? dept.Name : "Outpatients";
        _name.Text = $"{a.FullName}, {a.Age}";
        _sub.Text = $"{(a.Sex == Sex.Female ? "Female" : "Male")} · Clinic patient";
        HudWidgets.SetAvatar(_avatar, a.FullName, DetailColours.Purple);

        var now = _sim.Now;
        var waited = a.CheckedInAt is { } inAt ? (int)((a.CalledInAt ?? now) - inAt).TotalMinutes : 0;
        HudWidgets.SetChip(_chipParts[0], department, DetailColours.PurpleTint);
        _chipParts[0].Text.AddThemeColorOverride("font_color", DetailColours.Purple);
        HudWidgets.SetChip(_chipParts[1], $"{a.Time:HH:mm} appointment", UiStyle.Ground);
        HudWidgets.SetChip(_chipParts[2], a.CheckedInAt is null ? string.Empty : $"Waited {waited} min", waited > 20 ? UiStyle.AmberTint : UiStyle.Ground);
        if (waited > 20)
        {
            _chipParts[2].Text.AddThemeColorOverride("font_color", UiStyle.AmberText);
        }

        // The journey.
        var current = o.Stage switch
        {
            OutpatientStage.WalkingIn => 1,
            OutpatientStage.Queueing or OutpatientStage.CheckingIn => 2,
            OutpatientStage.Waiting => 3,
            OutpatientStage.GoingIn or OutpatientStage.BeingSeen => 4,
            _ => 5,
        };
        var labels = new[] { "Booked", "Arrived", "Checked in", "Waiting", "With doctor", "Home" };
        _journey.Set(labels.Select((label, i) => (label, i < current ? JourneyBar.Step.Done : i == current ? JourneyBar.Step.Current : JourneyBar.Step.Todo)).ToList());

        // Right now.
        var consult = _sim.Consults.FirstOrDefault(c => c.OutpatientId == o.Id);
        var clinician = consult is not null ? _sim.FindStaff(consult.StaffId) : a.SeenByStaffId is { } seenBy ? _sim.FindStaff(seenBy) : clinic is null ? null : _sim.Staff.FirstOrDefault(s => s.BaseRoomId == clinic.Id);
        var who = clinician is null ? "the clinic team" : $"{clinician.Name} ({clinician.Grade})";
        _now.Heading.Text = "RIGHT NOW";
        _now.Text.Text = o.Stage switch
        {
            OutpatientStage.BeingSeen => $"In {clinic?.Name ?? "clinic"} with {who}",
            OutpatientStage.GoingIn => $"Going in to {clinic?.Name ?? "clinic"}",
            _ => o.StatusText,
        };
        _now.Detail.Text = o.Stage switch
        {
            OutpatientStage.BeingSeen when o.BusyUntil is { } until => $"Called in at {a.CalledInAt:HH:mm} · {a.Minutes} min slot · about {Math.Max(1, (int)Math.Ceiling((until - now).TotalMinutes))} min left",
            OutpatientStage.Waiting => now > a.Time ? $"{(int)(now - a.Time).TotalMinutes} min past their appointment time" : $"Their appointment is at {a.Time:HH:mm}",
            OutpatientStage.Leaving when a.SeenAt is { } seen => $"Seen by {who}, finished at {seen:HH:mm}",
            _ => string.Empty,
        };
        _now.Detail.Visible = _now.Detail.Text.Length > 0;

        var fee = a.Tariff ?? (clinic is not null && _sim.ClinicOf(clinic) is { } definition ? definition.Tariff : 0);
        var lines = new List<string>
        {
            $"[color=#5B6B79]Reason[/color]  {ClinicalText.Escape(a.Reason)}",
            $"[color=#5B6B79]Type[/color]  {(a.IsProcedure ? "Procedure" : "Consultation")}",
            $"[color=#5B6B79]Booked[/color]  {a.Time:ddd HH:mm}, {ClinicalText.Escape(clinic?.Name ?? "clinic")}",
            $"[color=#5B6B79]Clinician[/color]  {ClinicalText.Escape(who)}",
        };
        if (a.CheckedInAt is { } checkedIn)
        {
            lines.Add($"[color=#5B6B79]Checked in[/color]  {checkedIn:HH:mm}");
        }

        if (a.CalledInAt is { } called)
        {
            lines.Add($"[color=#5B6B79]Called in[/color]  {called:HH:mm}");
        }

        lines.Add($"[color=#5B6B79]Fee[/color]  £{fee:N0}");
        _facts.Text = UiStyle.Facts(string.Join("\n", lines));
    }
}
