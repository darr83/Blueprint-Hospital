using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The Surgery panel (U): a theatre board like the whiteboard in a real theatre suite (one card per theatre
/// with its status, team, the case in progress and who is next, and today's timeline under them), and the
/// waiting list of everyone booked for an operation as the second tab, and the week's theatre sessions as the third.
/// </summary>
public partial class SurgeryPanel : PanelContainer
{
    private static readonly Color Hair = new("#E6ECF1");

    private readonly Label _caption = UiStyle.Caption(string.Empty, 13);
    private readonly Button _boardTab = UiStyle.SecondaryButton("Theatres", () => { });
    private readonly Button _listTab = UiStyle.SecondaryButton("Waiting list", () => { });
    private readonly Button _sessionsTab = UiStyle.SecondaryButton("Sessions", () => { });
    private readonly List<(PanelContainer Tile, Label Value, Label Note)> _tiles = new();
    private readonly VBoxContainer _body = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private HospitalSimulation _sim = null!;
    private double _refresh;
    private int _tab;

    /// <summary>A patient's name was clicked: open their record.</summary>
    public event Action<Patient>? PatientClicked;

    /// <summary>True while the waiting list tab is showing; false for the theatre board.</summary>
    public bool ShowingWaitingList => _tab == 1;

    /// <summary>True while the weekly sessions timetable is showing.</summary>
    public bool ShowingSessions => _tab == 2;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", UiStyle.Card(20));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        AddChild(column);
        column.AddChild(BuildHeader());

        var tiles = new HBoxContainer();
        tiles.AddThemeConstantOverride("separation", 10);
        foreach (var (caption, tip) in new[]
        {
            ("Waiting", "Patients booked for an operation who haven't been operated on yet"),
            ("Over target", "Waiting longer than their urgency allows: Immediate 1 hour, Urgent 6 hours, Soon 24 hours"),
            ("Operations today", "Operations finished since midnight"),
            ("Theatre income today", "What today's operations were paid"),
            ("Complications today", "Bleeding, wound infections and patients who needed critical care after today's operations. More likely with a junior or tired surgeon, a surgeon outside their specialty, a long wait or a major operation"),
        })
        {
            tiles.AddChild(Tile(caption, tip));
        }

        column.AddChild(tiles);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 14);
        scroll.AddChild(_body);
        column.AddChild(scroll);
        Refresh();
    }

    /// <summary>Shows the theatre board (false) or the waiting list (true).</summary>
    public void ShowTab(bool waitingList) => ShowPage(waitingList ? 1 : 0);

    /// <summary>Shows the weekly theatre sessions timetable.</summary>
    public void ShowSessions() => ShowPage(2);

    private void ShowPage(int tab)
    {
        _tab = tab;
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
            _refresh = 1;
            Refresh();
        }
    }

    // ---------------------------------------------------------------- layout

    private Control BuildHeader()
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 0);
        var title = new Label { Text = "Surgery" };
        title.AddThemeFontSizeOverride("font_size", 22);
        titles.AddChild(title);
        titles.AddChild(_caption);
        header.AddChild(titles);

        var tabs = new HBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        tabs.AddThemeConstantOverride("separation", 6);
        foreach (var (button, tab) in new[] { (_boardTab, 0), (_listTab, 1), (_sessionsTab, 2) })
        {
            button.ToggleMode = true;
            button.FocusMode = FocusModeEnum.None;
            button.Pressed += () => ShowPage(tab);
            tabs.AddChild(button);
        }

        _boardTab.TooltipText = "What each theatre is doing, its team and who is next";
        _listTab.TooltipText = "Everyone waiting for an operation, most urgent first";
        _sessionsTab.TooltipText = "The week's morning and afternoon sessions: give each to a department's planned list, or keep it for emergencies";
        header.AddChild(tabs);
        var close = UiStyle.SecondaryButton("✕", () => Visible = false);
        close.TooltipText = "Close (Esc)";
        close.CustomMinimumSize = new Vector2(40, 40);
        header.AddChild(close);
        return header;
    }

    private Control Tile(string caption, string tooltip)
    {
        var tile = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = tooltip, MouseFilter = MouseFilterEnum.Stop };
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 0);
        column.AddChild(UiStyle.Caption(caption, 12));
        var value = new Label { MouseFilter = MouseFilterEnum.Ignore };
        value.AddThemeFontSizeOverride("font_size", 24);
        column.AddChild(value);
        var note = UiStyle.Caption(string.Empty, 11);
        column.AddChild(note);
        tile.AddChild(column);
        _tiles.Add((tile, value, note));
        SetTone(_tiles[^1], false);
        return tile;
    }

    private static void SetTone((PanelContainer Tile, Label Value, Label Note) tile, bool danger)
    {
        var (background, border, text) = danger
            ? (new Color("#FDECEC"), new Color("#EBA5A8"), UiStyle.Red)
            : (UiStyle.Surface, Hair, UiStyle.TextDark);
        var box = UiStyle.Box(background, 12, border);
        box.ContentMarginLeft = box.ContentMarginRight = 14;
        box.ContentMarginTop = box.ContentMarginBottom = 9;
        tile.Tile.AddThemeStyleboxOverride("panel", box);
        tile.Value.AddThemeColorOverride("font_color", text);
    }

    // ---------------------------------------------------------------- refresh

    private void Refresh()
    {
        if (_sim is null)
        {
            return;
        }

        var waiting = _sim.SurgeryWaitingList();
        var queued = waiting.Where(c => !c.InProgress).ToList();
        var over = waiting.Count(Overdue);
        var today = _sim.SurgeryToday;

        _tiles[0].Value.Text = queued.Count.ToString();
        _tiles[0].Note.Text = waiting.Count > queued.Count ? $"{waiting.Count - queued.Count} in theatre now" : string.Empty;
        _tiles[1].Value.Text = over.ToString();
        _tiles[1].Note.Text = today.Breaches > 0 ? $"{today.Breaches} operated on late today" : string.Empty;
        SetTone(_tiles[1], over > 0);
        _tiles[2].Value.Text = today.Operations.ToString();
        _tiles[2].Note.Text = today.Postponed > 0 ? $"{today.Postponed} postponed" : string.Empty;
        _tiles[3].Value.Text = $"£{today.Income:N0}";
        _tiles[4].Value.Text = today.Complications.ToString();
        _tiles[4].Note.Text = today.Operations > 0 ? $"{100.0 * today.Complications / today.Operations:0}% of operations" : string.Empty;
        SetTone(_tiles[4], today.Complications > 0);

        var theatres = _sim.TheatreBoard();
        var open = theatres.Count(t => t.State != TheatreState.Closed);
        _caption.Text = theatres.Count == 0
            ? "No operating theatres yet: build one in Build mode, with an anaesthetic room against it"
            : $"{theatres.Count} theatre{(theatres.Count == 1 ? string.Empty : "s")}, {open} open · {_sim.Now:HH:mm}";
        _listTab.Text = $"Waiting list · {queued.Count}";
        _boardTab.SetPressedNoSignal(_tab == 0);
        _listTab.SetPressedNoSignal(_tab == 1);
        _sessionsTab.SetPressedNoSignal(_tab == 2);

        foreach (var child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }

        switch (_tab)
        {
            case 1:
                BuildWaitingList(waiting);
                break;
            case 2:
                BuildSessions(theatres);
                break;
            default:
                BuildBoard(theatres);
                break;
        }
    }

    private static bool Overdue(SurgeryCase c) => c.Target is { } target && c.Waited > target;

    // ---------------------------------------------------------------- theatre board

    private void BuildBoard(IReadOnlyList<TheatreBoardEntry> theatres)
    {
        if (theatres.Count == 0)
        {
            _body.AddChild(UiStyle.Body("Build an operating theatre, then an anaesthetic room against one of its walls: the two are joined by automatic sliding doors. Each theatre needs a surgeon, an anaesthetist and a scrub nurse.", muted: true));
            return;
        }

        var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 14);
        foreach (var entry in theatres)
        {
            grid.AddChild(TheatreCard(entry));
        }

        // One or two theatres still take a third of the width each, like columns on a whiteboard, with recovery beside them.
        var recoveryBeside = theatres.Count < 3;
        if (recoveryBeside)
        {
            var recovery = RecoveryCard();
            recovery.SizeFlagsVertical = SizeFlags.ShrinkBegin;
            grid.AddChild(recovery);
        }

        for (var i = theatres.Count + (recoveryBeside ? 1 : 0); i < 3; i++)
        {
            grid.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        }

        _body.AddChild(grid);
        if (!recoveryBeside)
        {
            _body.AddChild(RecoveryCard());
        }

        var timeline = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        timeline.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Surface, 12, Hair, 14));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        column.AddChild(HudWidgets.Section("Today in theatre"));
        column.AddChild(new TheatreTimeline(_sim, theatres));
        column.AddChild(UiStyle.Caption("Each block is an operation, coloured by urgency. The red line is now.", 11));
        timeline.AddChild(column);
        _body.AddChild(timeline);
    }

    private Control TheatreCard(TheatreBoardEntry entry)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var closed = entry.State == TheatreState.Closed;
        card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Surface, 12, closed ? new Color("#EBA5A8") : Hair, 14));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        card.AddChild(column);

        var top = new HBoxContainer();
        var name = new Label { Text = entry.Theatre.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        name.AddThemeFontSizeOverride("font_size", 17);
        top.AddChild(name);
        var chip = HudWidgets.Chip();
        HudWidgets.SetChip(chip, StateName(entry.State), StateColour(entry.State));
        top.AddChild(chip.Chip);
        column.AddChild(top);

        var session = _sim.TheatreSessionAt(entry.Theatre, _sim.Now);
        var depts = entry.Theatre.AllowedDepartments.Select(_sim.DepartmentName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var sub = $"{(depts.Count == 0 ? "No departments ticked" : string.Join(", ", depts))} · {entry.AnaestheticRoom?.Name ?? "no anaesthetic room"}";
        var subLabel = UiStyle.Caption(sub, 12);
        subLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(subLabel);
        var sessionLabel = UiStyle.Caption(session == TheatreSessions.Emergency ? "This session: emergencies" : $"This session: {_sim.SessionName(session)} planned list", 12);
        sessionLabel.AddThemeColorOverride("font_color", session == TheatreSessions.Emergency ? UiStyle.TextMuted : SessionColour(session));
        column.AddChild(sessionLabel);
        column.AddChild(PhaseStrip(entry.State));

        // The team.
        foreach (var member in entry.Team)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var avatar = HudWidgets.Avatar(26);
            HudWidgets.SetAvatar(avatar, member.Name, member.Role switch
            {
                StaffRole.Anaesthetist => new Color("#6A4FB3"),
                StaffRole.ScrubNurse => new Color("#7A3B69"),
                _ => UiStyle.Blue,
            });
            row.AddChild(avatar.Badge);
            var who = new Label { Text = member.Name, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            who.AddThemeFontSizeOverride("font_size", 13);
            row.AddChild(who);
            row.AddChild(UiStyle.Caption(HospitalSimulation.JobTitle(member.Grade, member.Specialty), 11));
            column.AddChild(row);
        }

        foreach (var problem in entry.Problems)
        {
            var needs = UiStyle.Body($"Needs {problem}", muted: false);
            needs.AddThemeColorOverride("font_color", UiStyle.Red);
            needs.AddThemeFontSizeOverride("font_size", 13);
            column.AddChild(needs);
        }

        column.AddChild(HudWidgets.Divider());

        // Now.
        column.AddChild(UiStyle.Caption("NOW", 11));
        if (entry.Current is { } current)
        {
            column.AddChild(CaseLine(current, entry.State switch
            {
                TheatreState.Operating when entry.Remaining is { } left => $"about {ClinicalText.Duration(left)} left",
                TheatreState.Anaesthetic when entry.Remaining is { } left => $"asleep in {ClinicalText.Duration(left)}",
                TheatreState.Anaesthetic => "in the anaesthetic room",
                TheatreState.IntoTheatre => "going through to theatre",
                _ => "on the way to the anaesthetic room",
            }));
            if (entry.State == TheatreState.Operating)
            {
                var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0.01, Value = entry.Progress, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 6) };
                bar.AddThemeStyleboxOverride("background", UiStyle.Box(Hair, 3));
                bar.AddThemeStyleboxOverride("fill", UiStyle.Box(UiStyle.Blue, 3));
                column.AddChild(bar);
            }
        }
        else
        {
            column.AddChild(UiStyle.Caption(entry.State switch
            {
                TheatreState.WakingUp => "Last patient waking up on the table: no free recovery bay",
                TheatreState.Cleaning when entry.CleanUntil is { } until => $"Being cleaned and set up, ready {until:HH:mm}",
                TheatreState.Closed => "Closed",
                _ => "Empty, ready for the next patient",
            }, 13));
        }

        // Next.
        column.AddChild(UiStyle.Caption("NEXT", 11));
        if (entry.Next.Count == 0)
        {
            column.AddChild(UiStyle.Caption("Nobody waiting for this theatre", 13));
        }

        foreach (var next in entry.Next)
        {
            column.AddChild(CaseLine(next, next.Problem ?? (next.Target is { } target ? $"waited {ClinicalText.Duration(next.Waited)} of {ClinicalText.Duration(target)}" : $"waited {ClinicalText.Duration(next.Waited)}")));
        }

        return card;
    }

    private static readonly (TheatreState State, string Name)[] Phases =
    {
        (TheatreState.Anaesthetic, "Sleep"), (TheatreState.IntoTheatre, "Into theatre"), (TheatreState.Operating, "Operating"), (TheatreState.WakingUp, "Wake"), (TheatreState.Cleaning, "Clean"),
    };

    private static Control PhaseStrip(TheatreState state)
    {
        var strip = new HBoxContainer();
        strip.AddThemeConstantOverride("separation", 4);
        foreach (var (phase, name) in Phases)
        {
            var on = phase == state || (phase == TheatreState.Anaesthetic && state == TheatreState.Preparing);
            var cell = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var box = UiStyle.Box(on ? StateColour(phase) : UiStyle.Ground, 6);
            box.ContentMarginTop = box.ContentMarginBottom = 2;
            cell.AddThemeStyleboxOverride("panel", box);
            var label = new Label { Text = name, HorizontalAlignment = HorizontalAlignment.Center };
            label.AddThemeFontSizeOverride("font_size", 11);
            label.AddThemeColorOverride("font_color", on ? Colors.White : UiStyle.TextMuted);
            cell.AddChild(label);
            strip.AddChild(cell);
        }

        return strip;
    }

    private Control CaseLine(SurgeryCase c, string note)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(UrgencyPill(c.Urgency));
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);
        text.AddChild(PatientButton(c.Patient, $"{c.Patient.FullName} · {c.Operation}"));
        var noteLabel = UiStyle.Caption(note, 11);
        if (Overdue(c))
        {
            noteLabel.AddThemeColorOverride("font_color", UiStyle.Red);
        }

        text.AddChild(noteLabel);
        row.AddChild(text);
        return row;
    }

    private Button PatientButton(Patient patient, string text)
    {
        var button = new Button
        {
            Text = text,
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.None,
            TooltipText = "Open their record",
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        button.AddThemeFontSizeOverride("font_size", 13);
        button.AddThemeColorOverride("font_color", UiStyle.TextDark);
        button.AddThemeColorOverride("font_hover_color", UiStyle.Blue);
        var empty = new StyleBoxEmpty();
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" })
        {
            button.AddThemeStyleboxOverride(state, empty);
        }

        button.Pressed += () => PatientClicked?.Invoke(patient);
        return button;
    }

    // ---------------------------------------------------------------- waiting list

    private void BuildWaitingList(IReadOnlyList<SurgeryCase> waiting)
    {
        if (waiting.Count == 0)
        {
            _body.AddChild(UiStyle.Body("Nobody is waiting for an operation.", muted: true));
            return;
        }

        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Surface, 12, Hair, 14));
        var table = new GridContainer { Columns = 7, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        table.AddThemeConstantOverride("h_separation", 16);
        table.AddThemeConstantOverride("v_separation", 8);
        card.AddChild(table);

        foreach (var heading in new[] { "Urgency", "Patient", "Operation", "Waited", "Ready for theatre", "Where they are", "Theatre" })
        {
            table.AddChild(UiStyle.Caption(heading.ToUpperInvariant(), 11));
        }

        foreach (var c in waiting)
        {
            table.AddChild(UrgencyPill(c.Urgency));
            var who = PatientButton(c.Patient, $"{c.Patient.FullName}, {c.Patient.AgeAt(_sim.Now)}");
            who.CustomMinimumSize = new Vector2(180, 0);
            table.AddChild(who);
            var op = new Label { Text = c.Operation, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            op.AddThemeFontSizeOverride("font_size", 13);
            table.AddChild(op);

            var waited = new Label { Text = c.Target is { } target ? $"{ClinicalText.Duration(c.Waited)} of {ClinicalText.Duration(target)}" : ClinicalText.Duration(c.Waited) };
            waited.AddThemeFontSizeOverride("font_size", 13);
            waited.AddThemeColorOverride("font_color", Overdue(c) ? UiStyle.Red : UiStyle.TextDark);
            table.AddChild(waited);

            var ready = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, AutowrapMode = TextServer.AutowrapMode.Off, CustomMinimumSize = new Vector2(250, 0), MouseFilter = MouseFilterEnum.Ignore };
            ready.AddThemeFontSizeOverride("normal_font_size", 13);
            ready.Text = $"{Tick(c.Consented)} Consent  {Tick(c.BloodsBack)} Bloods  {Tick(c.Fasted)} Nil by mouth {ClinicalText.Duration(c.FastedFor)}";
            table.AddChild(ready);

            var where = UiStyle.Caption(ClinicalText.Whereabouts(_sim, c.Patient), 12);
            where.CustomMinimumSize = new Vector2(220, 0);
            where.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            table.AddChild(where);

            var theatre = c.InProgress && c.TheatreId is { } id
                ? $"{_sim.FindSpace(id)?.Name ?? "Theatre"}: {PhaseName(c.Phase)}"
                : c.Problem ?? "Waiting for a theatre";
            var theatreLabel = UiStyle.Caption(theatre, 12);
            if (!c.InProgress && c.Problem is not null && c.Problem != "Getting ready")
            {
                theatreLabel.AddThemeColorOverride("font_color", UiStyle.Red);
            }

            table.AddChild(theatreLabel);
        }

        _body.AddChild(card);
        _body.AddChild(UiStyle.Caption("Before an operation every patient needs consent, a group and save blood test, and 6 hours nil by mouth (Immediate cases go straight in). Immediate cases jump the queue, and can push a planned case back.", 11));
    }

    private static string Tick(bool done) => done ? "[color=#2E8B57]✓[/color]" : "[color=#B0BAC4]○[/color]";

    private static string PhaseName(SurgeryPhase phase) => phase switch
    {
        SurgeryPhase.Anaesthetic => "being put to sleep",
        SurgeryPhase.IntoTheatre => "going into theatre",
        SurgeryPhase.Operating => "operating",
        _ => "on the way",
    };

    // ---------------------------------------------------------------- recovery

    private Control RecoveryCard()
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Surface, 12, Hair, 14));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        card.AddChild(column);
        column.AddChild(HudWidgets.Section("Recovery"));

        var rooms = _sim.SpacesOfKind(CareSpaceKind.TheatreRecovery).OrderBy(r => r.Name, StringComparer.Ordinal).ToList();
        if (rooms.Count == 0)
        {
            var none = UiStyle.Body("No recovery room: patients wake up on the theatre table, and the theatre can't start its next case until they have. Build one from the Theatres build icons.", muted: false);
            none.AddThemeColorOverride("font_color", UiStyle.AmberText);
            none.AddThemeFontSizeOverride("font_size", 13);
            column.AddChild(none);
        }

        foreach (var room in rooms)
        {
            var open = _sim.RecoveryBaysOpen(room);
            var used = room.Beds.Count(b => b.OccupantId is not null);
            var line = UiStyle.Caption($"{room.Name}: {used} of {room.Beds.Count} bays in use, {open} open", 12);
            line.TooltipText = $"One recovery nurse on duty opens every {HospitalSimulation.BaysPerRecoveryNurse} bays";
            line.MouseFilter = MouseFilterEnum.Pass;
            line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (open < room.Beds.Count)
            {
                line.AddThemeColorOverride("font_color", open == 0 ? UiStyle.Red : UiStyle.AmberText);
            }

            column.AddChild(line);
        }

        var waking = _sim.TheatreRecovery.OrderBy(e => e.Until ?? DateTime.MaxValue).ToList();
        if (waking.Count == 0)
        {
            column.AddChild(UiStyle.Caption("Nobody waking up", 13));
        }

        if (waking.Count > 0)
        {
            column.AddChild(HudWidgets.Divider());
        }

        foreach (var entry in waking)
        {
            if (_sim.FindPatient(entry.PatientId) is not { } patient)
            {
                continue;
            }

            var row = new VBoxContainer();
            row.AddThemeConstantOverride("separation", 0);
            var where = entry.RecoveryRoomId is { } roomId && _sim.FindSpace(roomId) is { } recovery
                ? $"{recovery.Name}, bay {(entry.Bay ?? 0) + 1}"
                : $"On the table in {_sim.FindSpace(entry.TheatreId)?.Name ?? "theatre"}";
            var state = entry.Returning
                ? "Bleeding: going back to theatre"
                : entry.Until is { } until && until > _sim.Now ? $"awake in {ClinicalText.Duration(until - _sim.Now)}" : "waking up";
            row.AddChild(PatientButton(patient, patient.FullName));
            var stateLabel = UiStyle.Caption($"{where} · {state}", 12);
            stateLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (entry.Returning)
            {
                stateLabel.AddThemeColorOverride("font_color", UiStyle.Red);
            }

            row.AddChild(stateLabel);
            var note = UiStyle.Caption(entry.Complication is { } c ? HospitalSimulation.ComplicationName(c) : entry.Note, 12);
            note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (entry.Complication is not null)
            {
                note.AddThemeColorOverride("font_color", UiStyle.Red);
            }

            row.AddChild(note);
            column.AddChild(row);
        }

        return card;
    }

    // ---------------------------------------------------------------- sessions

    private static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    private void BuildSessions(IReadOnlyList<TheatreBoardEntry> theatres)
    {
        if (theatres.Count == 0)
        {
            _body.AddChild(UiStyle.Body("Build an operating theatre first: its morning and afternoon sessions show here.", muted: true));
            return;
        }

        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Surface, 12, Hair, 14));
        var grid = new GridContainer { Columns = 1 + TheatreSessions.PerWeek, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 6);
        card.AddChild(grid);

        var nowSlot = TheatreSessions.SlotOf(_sim.Now);
        grid.AddChild(UiStyle.Caption("THEATRE", 11));
        for (var slot = 0; slot < TheatreSessions.PerWeek; slot++)
        {
            var head = UiStyle.Caption($"{DayNames[slot / 2]} {(slot % 2 == 0 ? "AM" : "PM")}", 11);
            head.HorizontalAlignment = HorizontalAlignment.Center;
            head.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            if (slot == nowSlot)
            {
                head.AddThemeColorOverride("font_color", UiStyle.Red);
            }

            grid.AddChild(head);
        }

        foreach (var entry in theatres)
        {
            var theatre = entry.Theatre;
            var name = new Label { Text = theatre.Name, CustomMinimumSize = new Vector2(150, 0), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, VerticalAlignment = VerticalAlignment.Center };
            name.AddThemeFontSizeOverride("font_size", 13);
            grid.AddChild(name);
            var timetable = _sim.TheatreTimetable(theatre);
            var choices = new[] { TheatreSessions.Emergency }.Concat(theatre.AllowedDepartments.OrderBy(d => d, StringComparer.Ordinal)).ToList();
            for (var slot = 0; slot < TheatreSessions.PerWeek; slot++)
            {
                grid.AddChild(SessionCell(theatre.Id, slot, timetable[slot], choices, slot == nowSlot));
            }
        }

        _body.AddChild(card);
        _body.AddChild(UiStyle.Caption("Click a session to give it to the next department, or back to emergencies. Sessions run 08:00 to 13:00 and 13:00 to 18:00; nights are always for emergencies. A department's session only takes that department's planned cases, but an Immediate case can still use it.", 11));
    }

    private Control SessionCell(string theatreId, int slot, string use, IReadOnlyList<string> choices, bool now)
    {
        var colour = SessionColour(use);
        var button = new Button
        {
            Text = use switch
            {
                TheatreSessions.Emergency => "Emergency",
                "general_surgery" => "Gen surgery",
                "trauma_orthopaedics" => "Orthopaedics",
                "gynaecology" => "Gynae",
                _ => _sim.SessionName(use),
            },
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 40),
            ClipText = true,
            FocusMode = FocusModeEnum.None,
            TooltipText = $"{DayNames[slot / 2]} {(slot % 2 == 0 ? "morning" : "afternoon")}: {_sim.SessionName(use)}. Click to change.",
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        button.AddThemeFontSizeOverride("font_size", 11);
        button.AddThemeColorOverride("font_color", use == TheatreSessions.Emergency ? UiStyle.TextMuted : Colors.White);
        button.AddThemeColorOverride("font_hover_color", use == TheatreSessions.Emergency ? UiStyle.TextDark : Colors.White);
        var background = use == TheatreSessions.Emergency ? UiStyle.Ground : colour;
        foreach (var (state, tint) in new[] { ("normal", 0f), ("hover", 0.12f), ("pressed", 0.2f) })
        {
            var box = UiStyle.Box(background.Darkened(tint), 6, now ? UiStyle.Red : null);
            if (now)
            {
                box.BorderWidthLeft = box.BorderWidthRight = box.BorderWidthTop = box.BorderWidthBottom = 2;
            }

            box.ContentMarginLeft = box.ContentMarginRight = 4;
            button.AddThemeStyleboxOverride(state, box);
        }

        button.Pressed += () =>
        {
            var next = choices[(choices.ToList().IndexOf(use) + 1) % choices.Count];
            _sim.SetTheatreSession(theatreId, slot, next);
            Refresh();
        };
        return button;
    }

    private static Color SessionColour(string use) => use switch
    {
        TheatreSessions.Emergency => UiStyle.Red,
        "general_surgery" => UiStyle.Blue,
        "trauma_orthopaedics" => new Color("#00897B"),
        "gynaecology" => new Color("#6A4FB3"),
        "maternity" => new Color("#C2185B"),
        _ => new Color("#5D7080"),
    };

    // ---------------------------------------------------------------- shared bits

    public static Color UrgencyColour(SurgeryUrgency urgency) => urgency switch
    {
        SurgeryUrgency.Immediate => UiStyle.Red,
        SurgeryUrgency.Urgent => new Color("#E07B00"),
        SurgeryUrgency.Soon => UiStyle.Blue,
        _ => new Color("#6B7C8A"),
    };

    private static Control UrgencyPill(SurgeryUrgency urgency)
    {
        var chip = HudWidgets.Chip();
        HudWidgets.SetChip(chip, HospitalSimulation.UrgencyName(urgency), UrgencyColour(urgency));
        chip.Text.AddThemeFontSizeOverride("font_size", 11);
        chip.Chip.CustomMinimumSize = new Vector2(84, 0);
        chip.Text.HorizontalAlignment = HorizontalAlignment.Center;
        chip.Chip.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return chip.Chip;
    }

    private static string StateName(TheatreState state) => state switch
    {
        TheatreState.Closed => "Closed",
        TheatreState.Ready => "Ready",
        TheatreState.Preparing => "Patient on the way",
        TheatreState.Anaesthetic => "Putting to sleep",
        TheatreState.IntoTheatre => "Into theatre",
        TheatreState.Operating => "Operating",
        TheatreState.Cleaning => "Cleaning",
        TheatreState.WakingUp => "Waking up",
        _ => state.ToString(),
    };

    private static Color StateColour(TheatreState state) => state switch
    {
        TheatreState.Closed => UiStyle.Red,
        TheatreState.Ready => UiStyle.Green,
        TheatreState.Preparing or TheatreState.Anaesthetic => new Color("#6A4FB3"),
        TheatreState.IntoTheatre or TheatreState.Operating => UiStyle.Blue,
        TheatreState.Cleaning => new Color("#B58A00"),
        TheatreState.WakingUp => new Color("#00897B"),
        _ => UiStyle.TextMuted,
    };
}

/// <summary>Today's operations as blocks on a line per theatre, over the last 10 hours and the next 2.</summary>
public partial class TheatreTimeline : Control
{
    private const float LabelWidth = 150;
    private const float RowHeight = 30;
    private const float AxisHeight = 22;
    private readonly HospitalSimulation _sim;
    private readonly IReadOnlyList<TheatreBoardEntry> _theatres;

    public TheatreTimeline(HospitalSimulation sim, IReadOnlyList<TheatreBoardEntry> theatres)
    {
        _sim = sim;
        _theatres = theatres;
        CustomMinimumSize = new Vector2(0, (theatres.Count * RowHeight) + AxisHeight);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        var now = _sim.Now;
        var start = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(-10);
        var end = start.AddHours(12);
        var width = Size.X - LabelWidth;
        float X(DateTime t) => LabelWidth + (float)((t - start).TotalHours / 12.0 * width);

        // Hour lines and labels.
        for (var hour = 0; hour <= 12; hour += 2)
        {
            var t = start.AddHours(hour);
            var x = X(t);
            DrawLine(new Vector2(x, 0), new Vector2(x, _theatres.Count * RowHeight), new Color("#E6ECF1"), 1);
            DrawString(font, new Vector2(x - 16, (_theatres.Count * RowHeight) + 16), t.ToString("HH:mm"), HorizontalAlignment.Left, -1, 11, UiStyle.TextMuted);
        }

        for (var i = 0; i < _theatres.Count; i++)
        {
            var theatre = _theatres[i].Theatre;
            var y = i * RowHeight;
            DrawString(font, new Vector2(0, y + 19), theatre.Name, HorizontalAlignment.Left, LabelWidth - 8, 12, UiStyle.TextDark);
            foreach (var log in _sim.TheatreLog.Where(l => l.TheatreId == theatre.Id))
            {
                var from = log.Start < start ? start : log.Start;
                var to = log.End ?? now;
                if (to < start || from > end)
                {
                    continue;
                }

                var x0 = X(from);
                var x1 = Math.Max(x0 + 3, X(to));
                var rect = new Rect2(x0, y + 4, x1 - x0, RowHeight - 8);
                DrawRect(rect, SurgeryPanel.UrgencyColour(log.Urgency));
                if (rect.Size.X > 40)
                {
                    DrawString(font, new Vector2(x0 + 4, y + 19), log.Operation, HorizontalAlignment.Left, rect.Size.X - 8, 11, Colors.White);
                }
            }
        }

        var nx = X(now);
        DrawLine(new Vector2(nx, 0), new Vector2(nx, _theatres.Count * RowHeight), UiStyle.Red, 2);
    }
}
