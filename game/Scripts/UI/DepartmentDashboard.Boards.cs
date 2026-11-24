using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The Inpatients and Clinics boards on the A&amp;E board's card: patient numbers, red/amber/green (by live NEWS2
/// for inpatients; waiting, in clinic and seen for clinics), each ward's or clinic's progress, and stays and waits.
/// </summary>
public partial class DepartmentDashboard
{
    public enum Board
    {
        Emergency,
        Inpatients,
        Clinics,
    }

    private static readonly Color PurpleTile = new("#6B4FBB");

    private readonly VBoxContainer _inpatientPage = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly VBoxContainer _clinicPage = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly (RichTextLabel Count, RichTextLabel Detail)[] _inpatientTiles = new (RichTextLabel, RichTextLabel)[3];
    private readonly (RichTextLabel Count, RichTextLabel Detail)[] _clinicTiles = new (RichTextLabel, RichTextLabel)[3];
    private readonly VBoxContainer _wardRows = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly VBoxContainer _clinicRows = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly Dictionary<string, RichTextLabel> _boardValues = new();

    /// <summary>The board showing.</summary>
    public Board Showing => _board;

    public void ShowBoard(Board board)
    {
        _board = board;
        Refresh();
    }

    private void BuildInpatientPage()
    {
        _inpatientPage.AddThemeConstantOverride("separation", 10);
        _inpatientPage.AddChild(TileRow(_inpatientTiles, ("RED", RedTile, Colors.White), ("AMBER", AmberTile, AmberInk), ("GREEN", GreenTile, Colors.White)));
        _inpatientPage.AddChild(RowsSection("WARDS", "BEDS · AVG STAY", _wardRows));
        _inpatientPage.AddChild(LineBox("bedWait", "Waiting in A&E for a ward bed"));
        _inpatientPage.AddChild(LineBox("goingHome", "Going home today"));
        var stats = new GridContainer { Columns = 3, MouseFilter = MouseFilterEnum.Ignore };
        stats.AddThemeConstantOverride("h_separation", 8);
        stats.AddChild(BoardStat("avgStay", "Average stay", "on the wards now"));
        stats.AddChild(BoardStat("longest", "Longest stay", "so far"));
        stats.AddChild(BoardStat("theatre", "For theatre", "booked or waiting"));
        _inpatientPage.AddChild(stats);
    }

    private void BuildClinicPage()
    {
        _clinicPage.AddThemeConstantOverride("separation", 10);
        _clinicPage.AddChild(TileRow(_clinicTiles, ("WAITING", UiStyle.Blue, Colors.White), ("IN CLINIC", PurpleTile, Colors.White), ("SEEN", GreenTile, Colors.White)));
        _clinicPage.AddChild(RowsSection("CLINICS", "SEEN · RUNNING", _clinicRows));
        _clinicPage.AddChild(LineBox("dna", "Did not attend"));
        _clinicPage.AddChild(LineBox("toArrive", "Still to arrive"));
        var stats = new GridContainer { Columns = 3, MouseFilter = MouseFilterEnum.Ignore };
        stats.AddThemeConstantOverride("h_separation", 8);
        stats.AddChild(BoardStat("avgWait", "Average wait", "checked in to called"));
        stats.AddChild(BoardStat("onTime", "Seen on time", "within 10 min"));
        stats.AddChild(BoardStat("income", "Income", "today"));
        _clinicPage.AddChild(stats);
    }

    private void RefreshInpatients()
    {
        var board = _sim.BuildInpatientBoard();
        Set(_title, "[b]Inpatients board[/b]");
        Set(_inDepartment, $"Admitted [b][color=#17232E]{board.Admitted}[/color][/b]");
        _closed.Visible = false;
        SetTile(_inpatientTiles[0], board.Red, "NEWS2 7+");
        SetTile(_inpatientTiles[1], board.Amber, "NEWS2 5–6");
        SetTile(_inpatientTiles[2], board.Green, "NEWS2 0–4");

        Clear(_wardRows);
        if (board.Wards.Count == 0)
        {
            _wardRows.AddChild(Text("[color=#5B6B79]No wards yet. Build one in Build › Wards.[/color]", 13, wrap: true));
        }

        foreach (var line in board.Wards.Take(7))
        {
            var full = line.BedsUsed >= line.Beds;
            var figure = line.Closed ? $"[color={ClinicalText.RedHex}]closed[/color]" : $"[b]{line.BedsUsed}/{line.Beds}[/b] · {Days(line.AverageStayDays)}";
            _wardRows.AddChild(BarRow(line.Ward.Name, line.BedsUsed, line.Beds, full ? RedTile : UiStyle.Blue, figure));
        }

        if (board.Wards.Count > 7)
        {
            _wardRows.AddChild(Text($"[color=#5B6B79]and {board.Wards.Count - 7} more[/color]", 12));
        }

        Set(_boardValues["bedWait"], board.AwaitingBed > 0 ? $"[b][color={ClinicalText.RedHex}]{board.AwaitingBed}[/color][/b]" : "[b]0[/b]");
        Set(_boardValues["goingHome"], $"[b]{board.GoingHomeToday}[/b]");
        Set(_boardValues["avgStay"], $"[b]{Days(board.AverageStayDays)}[/b]");
        Set(_boardValues["longest"], $"[b]{Days(board.LongestStayDays)}[/b]");
        Set(_boardValues["theatre"], $"[b]{board.TheatreToday}[/b]");
    }

    private void RefreshClinics()
    {
        var board = _sim.BuildClinicBoard();
        Set(_title, "[b]Clinics board[/b]");
        Set(_inDepartment, $"Booked today [b][color=#17232E]{board.BookedToday}[/color][/b]");
        _closed.Visible = false;
        SetTile(_clinicTiles[0], board.Waiting, board.Waiting == 0 ? "none waiting" : $"longest {board.LongestWaitMinutes} min");
        SetTile(_clinicTiles[1], board.InClinic, "with a doctor");
        SetTile(_clinicTiles[2], board.Seen, "gone home");

        Clear(_clinicRows);
        if (board.Clinics.Count == 0)
        {
            _clinicRows.AddChild(Text("[color=#5B6B79]No clinics yet. Build one in Build › Outpatients.[/color]", 13, wrap: true));
        }

        foreach (var line in board.Clinics.Take(7))
        {
            string figure;
            if (!line.Running)
            {
                figure = $"[color={ClinicalText.RedHex}]not running[/color]";
            }
            else
            {
                var late = line.MinutesLate >= Simulation.Simulation.HospitalSimulation.ClinicLateMinutes ? $"[color={ClinicalText.AmberTextHex}]{line.MinutesLate} m late[/color]" : "on time";
                figure = $"[b]{line.Seen}/{line.Booked}[/b] · {late}";
            }

            var colour = !line.Running ? EmptySlot : line.MinutesLate >= Simulation.Simulation.HospitalSimulation.ClinicLateMinutes ? AmberTile : GreenTile;
            var row = BarRow(line.Clinic.Name, line.Seen, Math.Max(1, line.Booked), colour, figure);
            row.TooltipText = line.Running ? string.Empty : $"{line.Clinic.Name} isn't running: {line.Problem}";
            _clinicRows.AddChild(row);
        }

        if (board.Clinics.Count > 7)
        {
            _clinicRows.AddChild(Text($"[color=#5B6B79]and {board.Clinics.Count - 7} more[/color]", 12));
        }

        Set(_boardValues["dna"], $"[b]{board.DidNotAttend}[/b]");
        Set(_boardValues["toArrive"], $"[b]{board.StillToArrive}[/b]");
        Set(_boardValues["avgWait"], $"[b]{board.AverageWaitMinutes:0} min[/b]");
        var onTimeHex = board.SeenOnTimeShare >= 0.85 ? "#2E7D32" : board.SeenOnTimeShare >= 0.6 ? ClinicalText.AmberTextHex : ClinicalText.RedHex;
        Set(_boardValues["onTime"], $"[b][color={onTimeHex}]{board.SeenOnTimeShare * 100:0}%[/color][/b]");
        Set(_boardValues["income"], $"[b]£{board.IncomeToday:N0}[/b]");
    }

    private static string Days(double days) => days <= 0 ? "–" : days >= 1 ? $"{days:0.0} d" : $"{days * 24:0} h";

    private void SetTile((RichTextLabel Count, RichTextLabel Detail) tile, int count, string detail)
    {
        Set(tile.Count, $"[b]{count}[/b]");
        Set(tile.Detail, detail);
    }

    private static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    private Control TileRow((RichTextLabel, RichTextLabel)[] store, params (string Title, Color Colour, Color Ink)[] tiles)
    {
        var grid = new GridContainer { Columns = 3, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 8);
        for (var i = 0; i < tiles.Length; i++)
        {
            var (title, colour, ink) = tiles[i];
            var tile = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            var box = UiStyle.Box(colour, 12);
            box.ContentMarginLeft = box.ContentMarginRight = 10;
            box.ContentMarginTop = 6;
            box.ContentMarginBottom = 8;
            tile.AddThemeStyleboxOverride("panel", box);
            var stack = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            stack.AddThemeConstantOverride("separation", -2);
            stack.AddChild(Text($"[b]{title}[/b]", 12, ink));
            var count = Text("0", 30, ink);
            stack.AddChild(count);
            var detail = Text(string.Empty, 11, ink);
            stack.AddChild(detail);
            tile.AddChild(stack);
            grid.AddChild(tile);
            store[i] = (count, detail);
        }

        return grid;
    }

    private static Control RowsSection(string title, string right, VBoxContainer rows)
    {
        var box = SectionBox();
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 6);
        var titles = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        titles.AddChild(Text($"[b]{title}[/b]", 11, UiStyle.TextMuted, expand: true));
        titles.AddChild(Text($"[b]{right}[/b]", 11, UiStyle.TextMuted));
        column.AddChild(titles);
        rows.AddThemeConstantOverride("separation", 6);
        column.AddChild(rows);
        box.AddChild(column);
        return box;
    }

    private Control BarRow(string name, int used, int total, Color fill, string figure)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        row.AddThemeConstantOverride("separation", 8);
        var label = new Label
        {
            Text = name,
            TooltipText = name,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            ClipText = true,
            CustomMinimumSize = new Vector2(150, 0),
            MouseFilter = MouseFilterEnum.Pass,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", UiStyle.TextDark);
        row.AddChild(label);
        var bar = new CapacityBar(fill, EmptySlot) { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        bar.Set(used, total);
        row.AddChild(bar);
        var value = Text(figure, 13, align: HorizontalAlignment.Right);
        value.CustomMinimumSize = new Vector2(84, 0);
        row.AddChild(value);
        return row;
    }

    private Control LineBox(string key, string caption)
    {
        var box = SectionBox();
        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.AddChild(Text(caption, 14, expand: true));
        var value = Text(string.Empty, 14, align: HorizontalAlignment.Right);
        line.AddChild(value);
        box.AddChild(line);
        _boardValues[key] = value;
        return box;
    }

    private Control BoardStat(string key, string title, string note)
    {
        var box = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        var style = UiStyle.Box(UiStyle.Surface, 12, UiStyle.Border);
        style.ContentMarginLeft = style.ContentMarginRight = 9;
        style.ContentMarginTop = 6;
        style.ContentMarginBottom = 7;
        box.AddThemeStyleboxOverride("panel", style);
        var stack = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        stack.AddThemeConstantOverride("separation", 0);
        stack.AddChild(Text(title, 11, UiStyle.TextMuted));
        var value = Text(string.Empty, 19);
        stack.AddChild(value);
        stack.AddChild(Text(note, 11, UiStyle.TextMuted));
        box.AddChild(stack);
        _boardValues[key] = value;
        return box;
    }
}
