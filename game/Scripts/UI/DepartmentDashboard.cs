using System;
using System.Collections.Generic;
using System.Text;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The A&amp;E board: who is in the department by triage category, waits, ambulances,
/// cubicle capacity and the headline performance figures.
/// </summary>
public partial class DepartmentDashboard : PanelContainer
{
    private readonly RichTextLabel _text = new()
    {
        BbcodeEnabled = true,
        FitContent = true,
        ScrollActive = false,
        MouseFilter = MouseFilterEnum.Ignore,
        CustomMinimumSize = new Vector2(290, 0),
    };

    private HospitalSimulation _sim = null!;
    private double _refresh;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", HudStyle.DarkPanel());
        _text.AddThemeColorOverride("default_color", new Color("#17232E"));
        _text.AddThemeFontSizeOverride("normal_font_size", 15);
        _text.AddThemeFontSizeOverride("bold_font_size", 15);
        AddChild(_text);
        Refresh();
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh <= 0)
        {
            _refresh = 0.5;
            Refresh();
        }
    }

    private void Refresh()
    {
        var s = _sim.Stats;
        var sb = new StringBuilder();
        sb.Append("[b][font_size=16]A&E board[/font_size][/b]\n");
        if (_sim.ClosedReasons is { Count: > 0 } missing)
        {
            sb.Append($"[color={ClinicalText.AmberTextHex}][b]Closed to new patients.[/b] Needs {string.Join(", ", missing)}.[/color]\n");
        }


        sb.Append($"In department [b]{s.PatientsInDepartment}[/b]");
        if (s.AwaitingTriage > 0)
        {
            sb.Append($" · awaiting triage [b]{s.AwaitingTriage}[/b]");
        }

        sb.Append('\n');

        foreach (var category in new[] { TriageCategory.Red, TriageCategory.Amber, TriageCategory.Green })
        {
            var hex = ClinicalText.TriageHex(category);
            var count = s.InDepartment.GetValueOrDefault(category);
            var waiting = s.WaitingForSpace.GetValueOrDefault(category);
            var longest = s.LongestWaitMinutes.GetValueOrDefault(category);
            sb.Append($"[bgcolor={hex}][color=#FFFFFF][b] {count,2} [/b][/color][/bgcolor] {category,-6}");
            if (waiting > 0)
            {
                var warn = category == TriageCategory.Red || longest > 120 ? ClinicalText.RedHex : "#17232E";
                sb.Append($"  [color={warn}]{waiting} waiting, longest {ClinicalText.Duration(TimeSpan.FromMinutes(longest))}[/color]");
            }

            sb.Append('\n');
        }

        var ambulanceColour = s.AmbulancesWaiting >= 3 ? ClinicalText.RedHex : "#17232E";
        sb.Append($"\n[color={ambulanceColour}]Ambulances waiting [b]{s.AmbulancesWaiting}[/b][/color]");
        if (s.AverageHandoverMinutes > 0)
        {
            sb.Append($" · handover {s.AverageHandoverMinutes:0} min");
        }

        sb.Append('\n');
        sb.Append(Capacity("Resus", s.Resus));
        sb.Append(Capacity("Majors", s.Majors));
        sb.Append(Capacity("Minors", s.Minors));
        if (s.WardBeds.Total > 0 || s.AwaitingBed > 0)
        {
            var wardFull = s.WardBeds.Used >= s.WardBeds.Total;
            var wardHex = wardFull ? ClinicalText.AmberTextHex : "#17232E";
            sb.Append($"[color={wardHex}]Ward beds {s.WardBeds.Used}/{s.WardBeds.Total}[/color]");
            if (s.AwaitingBed > 0)
            {
                sb.Append($" [color={ClinicalText.AmberTextHex}]· {s.AwaitingBed} waiting in A&E for a bed[/color]");
            }

            sb.Append('\n');
        }

        var four = s.FourHourPerformance;
        var fourHex = four >= 0.95 ? ClinicalText.GreenHex : four >= 0.76 ? ClinicalText.AmberTextHex : ClinicalText.RedHex;
        sb.Append($"\n4-hour target [color={fourHex}][b]{four:P0}[/b][/color] [color=#5B6B79](95% standard)[/color]\n");
        sb.Append($"Time to doctor [b]{s.AverageTimeToDoctorMinutes:0} min[/b]\n");
        sb.Append($"Diagnostic accuracy [b]{s.DiagnosticAccuracy:P0}[/b]\n");
        sb.Append($"\n[color=#5B6B79]Since opening: {s.ArrivalsTotal} arrivals ({s.AmbulanceArrivalsTotal} by ambulance)\n");
        sb.Append($"{s.Discharged} discharged · {s.Admitted} admitted · {s.Transferred} transferred · {s.LeftWithoutBeingSeen} left unseen · {s.Deaths} died");
        if (s.Admitted > 0)
        {
            sb.Append($"\nWards: {s.WardDischarges} home · {s.WardDeaths} died");
        }

        sb.Append("[/color]");

        _text.Text = sb.ToString();
    }

    private static string Capacity(string name, (int Used, int Total) usage)
    {
        var full = usage.Used >= usage.Total;
        var blocks = new StringBuilder();
        for (var i = 0; i < usage.Total; i++)
        {
            blocks.Append(i < usage.Used ? "■" : "□");
        }

        var colour = full ? ClinicalText.AmberTextHex : "#17232E";
        return $"[color={colour}]{name,-7} {blocks}  {usage.Used}/{usage.Total}[/color]\n";
    }
}

/// <summary>Styles for the in-game panels: white cards and clickable list rows.</summary>
public static class HudStyle
{
    /// <summary>The card every in-game panel sits on (kept under its old name).</summary>
    public static StyleBoxFlat DarkPanel() => UiStyle.Card(16);

    public static StyleBoxFlat RowBox(Color colour) => new()
    {
        BgColor = colour,
        CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6,
    };

    /// <summary>A flat, left-aligned button for clickable list rows.</summary>
    public static Button RowButton(string text, Action onPressed)
    {
        var button = new Button
        {
            Text = text,
            Alignment = HorizontalAlignment.Left,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        button.AddThemeStyleboxOverride("normal", RowBox(new Color("#F7F9FB")));
        button.AddThemeStyleboxOverride("hover", RowBox(UiStyle.BlueTint));
        button.AddThemeStyleboxOverride("pressed", RowBox(new Color("#D6E2F3")));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
        {
            button.AddThemeColorOverride(state, UiStyle.TextDark);
        }

        button.AddThemeFontSizeOverride("font_size", 14);
        button.Pressed += onPressed;
        return button;
    }
}
