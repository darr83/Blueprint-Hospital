using System;
using System.Linq;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The crash call banner under the top bar, and the crash panel it opens: who is in the crash team and how far
/// away, time since the arrest, CPR, shocks and the chance of survival right now. Stays up for a few game minutes
/// after the arrest ends, showing how it ended.
/// </summary>
public partial class CrashPanel : VBoxContainer
{
    /// <summary>How long (game time) the banner stays after an arrest ends.</summary>
    private static readonly TimeSpan ShowEndedFor = TimeSpan.FromMinutes(10);

    private readonly Button _banner = new() { Flat = false, FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand };
    private readonly PanelContainer _card = new() { Visible = false };
    private readonly Label _status = new();
    private readonly Label _title = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _subtitle = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _clock = new();
    private readonly Label _cpr = new();
    private readonly Label _shocks = new();
    private readonly VBoxContainer _team = new();
    private readonly ProgressBar _chance = new() { MinValue = 0, MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10) };
    private readonly Label _chanceText = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private HospitalSimulation _sim = null!;
    private CardiacArrest? _shown;
    private double _refresh;

    /// <summary>Raised by "Follow the team": centre on the patient and open their record.</summary>
    public event Action<int>? FollowRequested;

    /// <summary>The arrest the banner is about, if any.</summary>
    public CardiacArrest? Showing => _shown;

    public bool Expanded
    {
        get => _card.Visible;
        set => _card.Visible = value;
    }

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 6);
        Visible = false;

        _banner.AddThemeFontSizeOverride("font_size", 15);
        _banner.AddThemeColorOverride("font_color", Colors.White);
        _banner.AddThemeColorOverride("font_hover_color", Colors.White);
        _banner.AddThemeColorOverride("font_pressed_color", Colors.White);
        _banner.Pressed += () => Expanded = !Expanded;
        AddChild(_banner);

        _card.AddThemeStyleboxOverride("panel", UiStyle.Card(16, 12));
        AddChild(_card);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        _card.AddChild(body);

        _status.AddThemeFontSizeOverride("font_size", 12);
        body.AddChild(_status);
        _title.AddThemeFontSizeOverride("font_size", 17);
        _title.AddThemeColorOverride("font_color", UiStyle.TextDark);
        body.AddChild(_title);
        _subtitle.AddThemeFontSizeOverride("font_size", 13);
        _subtitle.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        body.AddChild(_subtitle);

        var figures = new HBoxContainer();
        figures.AddThemeConstantOverride("separation", 24);
        figures.AddChild(Figure("Time since arrest", _clock));
        figures.AddChild(Figure("CPR", _cpr));
        figures.AddChild(Figure("Shocks", _shocks));
        body.AddChild(figures);

        body.AddChild(Section("CRASH TEAM"));
        _team.AddThemeConstantOverride("separation", 4);
        body.AddChild(_team);

        body.AddChild(Section("CHANCE OF SURVIVAL RIGHT NOW"));
        var fill = UiStyle.Box(UiStyle.Green, 5);
        _chance.AddThemeStyleboxOverride("fill", fill);
        _chance.AddThemeStyleboxOverride("background", UiStyle.Box(new Color("#EEF1F4"), 5));
        body.AddChild(_chance);
        _chanceText.AddThemeFontSizeOverride("font_size", 12);
        _chanceText.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        body.AddChild(_chanceText);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        buttons.AddChild(UiStyle.Button("Follow the team", () =>
        {
            if (_shown is { } a)
            {
                FollowRequested?.Invoke(a.PatientId);
            }
        }));
        buttons.AddChild(UiStyle.SecondaryButton("Close", () => Expanded = false));
        body.AddChild(buttons);
    }

    private static Control Figure(string caption, Label value)
    {
        var box = new VBoxContainer();
        box.AddChild(UiStyle.Caption(caption, 12));
        value.AddThemeFontSizeOverride("font_size", 26);
        value.AddThemeColorOverride("font_color", UiStyle.TextDark);
        box.AddChild(value);
        return box;
    }

    private static Label Section(string text)
    {
        var label = UiStyle.Caption(text, 12);
        label.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        return label;
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh > 0)
        {
            return;
        }

        _refresh = 0.2;
        Refresh();
    }

    /// <summary>Brings the banner and the panel up to date (also called by the smoke test).</summary>
    public void Refresh()
    {
        var now = _sim.Now;
        var arrest = _sim.ActiveArrests.Where(a => a.CrashCalled).OrderBy(a => a.StartedAt).FirstOrDefault()
            ?? _sim.Arrests.Where(a => !a.IsActive && a.CrashCalled && now - a.EndedAt < ShowEndedFor).OrderByDescending(a => a.EndedAt).FirstOrDefault();
        _shown = arrest;
        Visible = arrest is not null;
        if (arrest is null)
        {
            Expanded = false;
            return;
        }

        var patient = _sim.FindPatient(arrest.PatientId);
        var name = patient?.FullName ?? "Patient";
        var kind = patient is { Maternity: true } ? "Obstetric emergency" : patient is { Age: < 16 } ? "Paediatric cardiac arrest" : "Adult cardiac arrest";
        var others = _sim.ActiveArrests.Count(a => a.CrashCalled) - (arrest.IsActive ? 1 : 0);
        var colour = arrest.Outcome switch
        {
            ArrestOutcome.Rosc => UiStyle.Green,
            ArrestOutcome.Died => new Color("#5B6B79"),
            _ => UiStyle.Red,
        };
        foreach (var state in new[] { "normal", "hover", "pressed" })
        {
            _banner.AddThemeStyleboxOverride(state, UiStyle.Box(state == "hover" ? colour.Lightened(0.08f) : colour, 8, margin: 8));
        }

        _banner.Text = arrest.Outcome switch
        {
            ArrestOutcome.Rosc => $"●  Heart restarted · {name}, {arrest.Place} · going to intensive care",
            ArrestOutcome.Died => $"●  Resuscitation stopped · {name}, {arrest.Place}",
            _ => $"●  CRASH CALL · {kind}, {arrest.Place}{(others > 0 ? $" (+{others} more)" : string.Empty)} · click for the crash team",
        };

        if (!Expanded)
        {
            return;
        }

        var (statusText, statusColour) = arrest.Outcome switch
        {
            ArrestOutcome.Rosc => ("Return of circulation · heart restarted", UiStyle.Green),
            ArrestOutcome.Died => ("Resuscitation stopped", UiStyle.TextMuted),
            _ => ("Cardiac arrest · in progress", UiStyle.Red),
        };
        _status.Text = statusText;
        _status.AddThemeColorOverride("font_color", statusColour);
        _title.Text = patient is null ? arrest.Place : $"{name}, {patient.Age} · {arrest.Place}";
        _subtitle.Text = patient is null ? string.Empty
            : $"Admitted with {(patient.WorkingDiagnosis is { } d && _sim.Database.Conditions.TryGetValue(d.ConditionId, out var c) ? c.Name.ToLowerInvariant() : "an illness not yet diagnosed")} · arrest at {arrest.StartedAt:HH:mm}"
              + (arrest.Witnessed ? string.Empty : " · found unresponsive");
        var since = (arrest.EndedAt ?? now) - arrest.StartedAt;
        _clock.Text = $"{(int)since.TotalMinutes:00}:{since.Seconds:00}";
        var cprOn = arrest.CprStartedAt is not null && arrest.IsActive;
        _cpr.Text = arrest.IsActive ? (cprOn ? "On" : "Not yet") : "Done";
        _cpr.AddThemeColorOverride("font_color", cprOn ? UiStyle.Green : arrest.IsActive ? UiStyle.Red : UiStyle.TextMuted);
        _shocks.Text = arrest.Rhythm == ArrestRhythm.Shockable || arrest.Shocks > 0 ? arrest.Shocks.ToString() : "–";

        foreach (var child in _team.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var role in Enum.GetValues<CrashRole>())
        {
            var responder = arrest.Team.FirstOrDefault(r => r.Role == role);
            var member = responder is null ? null : _sim.FindStaff(responder.StaffId);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var who = new Label
            {
                Text = member is null ? $"{RoleName(role)} · nobody free" : $"{member.Name} · {RoleName(role)}",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ClipText = true,
            };
            who.AddThemeFontSizeOverride("font_size", 13);
            who.AddThemeColorOverride("font_color", member is null ? UiStyle.TextMuted : UiStyle.TextDark);
            row.AddChild(who);
            var state = new Label { HorizontalAlignment = HorizontalAlignment.Right };
            state.AddThemeFontSizeOverride("font_size", 12);
            if (member is null)
            {
                state.Text = "–";
                state.AddThemeColorOverride("font_color", UiStyle.TextMuted);
            }
            else if (responder!.ArrivedAt is { } arrived)
            {
                var took = arrived - arrest.StartedAt;
                state.Text = $"here at {(int)took.TotalMinutes}:{took.Seconds:00}";
                state.AddThemeColorOverride("font_color", UiStyle.Green);
            }
            else
            {
                var distance = Math.Max(Math.Abs(member.Tile.X - (patient?.Tile.X ?? 0)), Math.Abs(member.Tile.Y - (patient?.Tile.Y ?? 0)));
                state.Text = role == CrashRole.Trolley && !_sim.ResusTrolleys.Any(t => t.PushedByStaffId == member.Id) ? "fetching trolley" : $"running · {distance / 2} m away";
                state.AddThemeColorOverride("font_color", UiStyle.AmberText);
            }

            row.AddChild(state);
            _team.AddChild(row);
        }

        _chance.Value = arrest.IsActive ? arrest.Chance : arrest.Outcome == ArrestOutcome.Rosc ? 1 : 0;
        _chanceText.Text = arrest.IsActive
            ? $"About {Words(arrest.Chance)} · {Reasons(arrest, patient?.Age ?? 60)}"
            : arrest.Outcome == ArrestOutcome.Rosc ? "Their heart is beating again. Intensive care decides what happens next." : "The team did everything they could.";
    }

    private static string RoleName(CrashRole role) => role switch
    {
        CrashRole.Compressions => "first on scene, CPR",
        CrashRole.Trolley => "resus trolley",
        CrashRole.Leader => "team leader",
        CrashRole.Airway => "anaesthetist, airway",
        _ => "second nurse",
    };

    private static string Words(double chance) => chance switch
    {
        >= 0.45 => "1 in 2",
        >= 0.3 => "1 in 3",
        >= 0.22 => "1 in 4",
        >= 0.15 => "1 in 6",
        >= 0.08 => "1 in 10",
        >= 0.03 => "1 in 20",
        _ => "almost none",
    };

    private string Reasons(CardiacArrest arrest, int age)
    {
        var parts = new System.Collections.Generic.List<string>();
        var noFlow = arrest.NoFlowMinutes(_sim.Now);
        parts.Add(arrest.CprStartedAt is null ? "no CPR yet (−)" : noFlow < 1.5 ? "fast CPR (+)" : "slow to start CPR (−)");
        if (arrest.Rhythm == ArrestRhythm.Shockable)
        {
            parts.Add(arrest.TrolleyArrivedAt is null ? "waiting for the defibrillator (−)" : "shockable rhythm, defib here (+)");
        }
        else
        {
            parts.Add("not a shockable rhythm (−)");
        }

        parts.Add(arrest.LeaderArrivedAt is null ? "no doctor yet (−)" : "doctor leading (+)");
        if (arrest.AirwayArrivedAt is not null)
        {
            parts.Add("anaesthetist here (+)");
        }

        if (age >= 75)
        {
            parts.Add($"age {age} (−)");
        }

        return string.Join(", ", parts);
    }
}
