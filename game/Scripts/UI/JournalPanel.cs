using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The illness journal (J): every illness in the game, by department. Illnesses the hospital has not seen yet
/// stay locked as "???". Each seen illness has a page: how many patients were treated, how they came in, where
/// they went, how often it was diagnosed right, and its symptoms, tests and treatment.
/// </summary>
public partial class JournalPanel : PanelContainer
{
    public enum Order
    {
        ByDepartment,
        MostTreated,
        AToZ,
    }

    private readonly Label _summary = UiStyle.Caption(string.Empty, UiStyle.SizeSmall);
    private readonly ProgressBar _progress = new() { MinValue = 0, MaxValue = 1, Step = 0.001, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10) };
    private readonly VBoxContainer _list = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly VBoxContainer _page = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Dictionary<Order, Button> _orderButtons = new();
    private readonly List<Button> _rows = new();
    private HospitalSimulation _sim = null!;
    private Order _order = Order.ByDepartment;
    private string? _selected;
    private string _listKey = string.Empty;
    private double _refresh;

    public string? Selected => _selected;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", UiStyle.Card(0));

        var split = new HBoxContainer();
        split.AddThemeConstantOverride("separation", 0);
        AddChild(split);

        // The list of illnesses.
        var left = new PanelContainer { CustomMinimumSize = new Vector2(400, 0) };
        var leftBox = UiStyle.Box(UiStyle.Surface, UiStyle.RadiusPanel, null, UiStyle.PanelPadding);
        leftBox.CornerRadiusTopRight = leftBox.CornerRadiusBottomRight = 0;
        leftBox.BorderWidthRight = 1;
        leftBox.BorderColor = UiStyle.Hairline;
        left.AddThemeStyleboxOverride("panel", leftBox);
        var leftColumn = new VBoxContainer();
        leftColumn.AddThemeConstantOverride("separation", 10);
        left.AddChild(leftColumn);
        var header = UiStyle.PanelHeader("Illness journal", null, () => Visible = false);
        var key = UiStyle.Caption("J", UiStyle.SizeSmall);
        key.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        header.Actions.AddChild(key);
        leftColumn.AddChild(header.Row);
        leftColumn.AddChild(_summary);
        _progress.AddThemeStyleboxOverride("background", UiStyle.Box(UiStyle.Ground, 5));
        _progress.AddThemeStyleboxOverride("fill", UiStyle.Box(UiStyle.Green, 5));
        leftColumn.AddChild(_progress);
        var orders = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var (order, text) in new[] { (Order.ByDepartment, "By department"), (Order.MostTreated, "Most treated"), (Order.AToZ, "A–Z") })
        {
            var button = UiStyle.AsTab(new Button { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 34) });
            button.Pressed += () => SetOrder(order);
            _orderButtons[order] = button;
            orders.AddChild(button);
        }

        leftColumn.AddChild(UiStyle.TabTrack(orders));
        var listScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _list.AddThemeConstantOverride("separation", 2);
        listScroll.AddChild(_list);
        leftColumn.AddChild(listScroll);
        split.AddChild(left);

        // The selected illness's page.
        var right = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var side in new[] { "left", "right", "top", "bottom" })
        {
            right.AddThemeConstantOverride($"margin_{side}", 26);
        }

        var pageScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _page.AddThemeConstantOverride("separation", 14);
        pageScroll.AddChild(_page);
        right.AddChild(pageScroll);
        split.AddChild(right);
    }

    /// <summary>Opens the journal (on the most treated illness the first time).</summary>
    public void Open()
    {
        Visible = true;
        _selected ??= _sim.Journal.Values.Where(e => e.Treated > 0).OrderByDescending(e => e.Treated).FirstOrDefault()?.ConditionId;
        _listKey = string.Empty;
        Refresh();
    }

    public void SetOrder(Order order)
    {
        _order = order;
        _listKey = string.Empty;
        Refresh();
    }

    public void Select(string conditionId)
    {
        _selected = conditionId;
        _listKey = string.Empty;
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

    private static bool Seen(JournalEntry? e) => e is not null && e.Treated > 0;

    private void Refresh()
    {
        _refresh = 2;
        var conditions = _sim.Database.Conditions.Values.ToList();
        var seen = conditions.Count(c => Seen(_sim.JournalFor(c.Id)));
        var treated = _sim.Journal.Values.Sum(e => e.Treated);
        _summary.Text = $"{seen} of {conditions.Count} illnesses seen · {treated:N0} patient{(treated == 1 ? string.Empty : "s")} treated";
        _progress.Value = conditions.Count == 0 ? 0 : seen / (double)conditions.Count;
        foreach (var (order, button) in _orderButtons)
        {
            button.SetPressedNoSignal(order == _order);
        }

        var key = $"{_order}|{_selected}|{string.Join(",", _sim.Journal.Values.Select(e => $"{e.ConditionId}:{e.Treated}"))}";
        if (key != _listKey)
        {
            _listKey = key;
            BuildList(conditions);
            BuildPage();
        }
    }

    private string DepartmentName(string id) => _sim.Database.Departments.TryGetValue(id, out var d) ? d.Name : id;

    private void BuildList(List<ConditionDefinition> conditions)
    {
        foreach (var child in _list.GetChildren())
        {
            child.QueueFree();
        }

        _rows.Clear();
        int Count(ConditionDefinition c) => _sim.JournalFor(c.Id)?.Treated ?? 0;
        switch (_order)
        {
            case Order.ByDepartment:
                foreach (var group in conditions.GroupBy(c => c.Department).OrderBy(g => g.Key == "emergency" ? 0 : 1).ThenBy(g => DepartmentName(g.Key), StringComparer.Ordinal))
                {
                    var heading = new HBoxContainer();
                    var name = UiStyle.Caption(DepartmentName(group.Key).ToUpperInvariant(), 12);
                    UiStyle.Strong(name);
                    name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    heading.AddChild(name);
                    heading.AddChild(UiStyle.Caption($"{group.Count(c => Count(c) > 0)} of {group.Count()}", 12));
                    var spaced = new MarginContainer();
                    spaced.AddThemeConstantOverride("margin_top", _list.GetChildCount() == 0 ? 0 : 10);
                    spaced.AddThemeConstantOverride("margin_right", 10);
                    spaced.AddChild(heading);
                    _list.AddChild(spaced);
                    foreach (var c in group.OrderByDescending(Count).ThenBy(c => c.Name, StringComparer.Ordinal))
                    {
                        AddRow(c, Count(c));
                    }
                }

                break;
            case Order.MostTreated:
                foreach (var c in conditions.OrderByDescending(Count).ThenBy(c => c.Name, StringComparer.Ordinal))
                {
                    AddRow(c, Count(c));
                }

                break;
            default:
                foreach (var c in conditions.OrderBy(c => Count(c) > 0 ? 0 : 1).ThenBy(c => c.Name, StringComparer.Ordinal))
                {
                    AddRow(c, Count(c));
                }

                break;
        }
    }

    private void AddRow(ConditionDefinition c, int count)
    {
        var known = count > 0;
        var selected = c.Id == _selected;
        var button = new Button
        {
            Text = known ? c.Name : "???  · not seen yet",
            Alignment = HorizontalAlignment.Left,
            FocusMode = FocusModeEnum.None,
            ClipText = true,
            CustomMinimumSize = new Vector2(0, 34),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = known ? $"{c.Name}: {count} treated" : "Treat a patient with this illness to unlock its page",
        };
        var back = selected ? UiStyle.Blue : new Color(0, 0, 0, 0);
        var ink = selected ? Colors.White : known ? UiStyle.TextDark : new Color("#9AA6B1");
        foreach (var (state, colour) in new[] { ("normal", back), ("hover", selected ? UiStyle.BlueHover : UiStyle.Ground), ("pressed", selected ? UiStyle.BluePressed : UiStyle.BlueTint), ("focus", back) })
        {
            var box = UiStyle.Box(colour, UiStyle.RadiusControl);
            box.ContentMarginLeft = 10;
            box.ContentMarginRight = 46;
            button.AddThemeStyleboxOverride(state, box);
        }

        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
        {
            button.AddThemeColorOverride(state, ink);
        }

        button.AddThemeFontSizeOverride("font_size", UiStyle.SizeBody);
        var number = new Label { Text = count.ToString(), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        UiStyle.Strong(number, UiStyle.SizeBody);
        number.AddThemeColorOverride("font_color", ink);
        number.SetAnchorsPreset(LayoutPreset.FullRect);
        number.OffsetRight = -12;
        button.AddChild(number);
        var id = c.Id;
        button.Pressed += () => Select(id);
        _rows.Add(button);
        _list.AddChild(button);
    }

    private void BuildPage()
    {
        foreach (var child in _page.GetChildren())
        {
            child.QueueFree();
        }

        if (_selected is null || !_sim.Database.Conditions.TryGetValue(_selected, out var c))
        {
            _page.AddChild(UiStyle.Heading("Illness journal"));
            _page.AddChild(UiStyle.Body("Every illness your hospital treats is written up here. Pick one on the left to see how your patients with it got on. Illnesses you haven't seen yet stay locked until the first patient with one leaves.", muted: true));
            return;
        }

        var entry = _sim.JournalFor(c.Id);
        var known = Seen(entry);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 14);
        var avatar = HudWidgets.Avatar(60);
        HudWidgets.SetAvatar(avatar, known ? c.Name : "?", known ? DepartmentColour(c.Department) : new Color("#B9C4CE"));
        head.AddChild(avatar.Badge);
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        titles.AddThemeConstantOverride("separation", 0);
        titles.AddChild(UiStyle.Heading(known ? c.Name : "???", UiStyle.SizeDisplay + 4));
        var first = entry?.FirstSeen is { } at ? $" · first seen {at:ddd d MMMM, HH:mm}" : string.Empty;
        titles.AddChild(UiStyle.Caption($"{DepartmentName(c.Department)}{first}", UiStyle.SizeBody));
        head.AddChild(titles);
        _page.AddChild(head);

        if (!known)
        {
            _page.AddChild(UiStyle.Body($"Not seen yet. Treat a patient with this {DepartmentName(c.Department).ToLowerInvariant()} illness to unlock its page.", muted: true));
            return;
        }

        var e = entry!;
        var description = UiStyle.Body(c.Description);
        description.AddThemeFontSizeOverride("font_size", UiStyle.SizeCardTitle);
        _page.AddChild(description);

        var tiles = new HBoxContainer();
        tiles.AddThemeConstantOverride("separation", 10);
        foreach (var (value, caption, colour) in new[]
        {
            (e.Treated.ToString("N0"), "patients treated", UiStyle.TextDark),
            ((e.WentHome + e.Transferred).ToString("N0"), "survived", UiStyle.Green),
            (e.Died.ToString("N0"), "died", e.Died > 0 ? UiStyle.Red : UiStyle.TextDark),
            (e.Diagnosed == 0 ? "–" : $"{e.DiagnosedRightShare * 100:0}%", "diagnosed right in A&E", UiStyle.TextDark),
            (e.AverageStayDays >= 1 ? $"{e.AverageStayDays:0.0} d" : $"{e.AverageStayDays * 24:0.0} h", "average stay", UiStyle.TextDark),
        })
        {
            var (tile, label) = HudWidgets.StatTile(caption);
            tile.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Ground, UiStyle.RadiusCard, null, 12));
            label.Text = value;
            label.AddThemeFontSizeOverride("font_size", UiStyle.SizeFigure + 6);
            label.AddThemeColorOverride("font_color", colour);
            tiles.AddChild(tile);
        }

        _page.AddChild(tiles);

        var men = e.Treated == 0 ? 0 : e.Male * 100.0 / e.Treated;
        var walkIns = e.Treated - e.Ambulance;
        var cameIn = $"Ambulance {e.Ambulance} · walk-in {walkIns}\nTriage: Red {e.Red} · Amber {e.Amber} · Green {e.Green}\nAverage age {e.AverageAge:0} · {men:0}% men";
        var wentTo = $"Home {e.WentHome} · transferred {e.Transferred} · died {e.Died}\nAdmitted to a ward first: {e.Admitted}\nAverage time in A&E {ClinicalText.Duration(TimeSpan.FromMinutes(e.AverageEmergencyMinutes))}"
                     + (e.LeftUnseen > 0 ? $"\nLeft before being seen: {e.LeftUnseen}" : string.Empty);
        _page.AddChild(Cards(("HOW THEY CAME IN", cameIn), ("WHERE THEY WENT", wentTo)));

        var symptoms = c.Symptoms.OrderByDescending(s => s.Probability).Select(s => _sim.Database.Symptoms.TryGetValue(s.Symptom, out var d) ? d.Name : s.Symptom);
        var tests = c.KeyInvestigations.Select(t => _sim.Database.Investigations.TryGetValue(t, out var d) ? d.Name : t);
        var treatment = c.Treatment.Select(t => t.Type == TreatmentStepType.Medication && t.Medication is { } m && _sim.Database.Medications.TryGetValue(m, out var med) ? med.Name : t.Name ?? t.Medication ?? "Treatment");
        if (c.Operation is { } operation)
        {
            treatment = treatment.Append(operation.Name);
        }

        _page.AddChild(Cards(("SYMPTOMS", Pills(symptoms)), ("KEY TESTS", Pills(tests)), ("TREATMENT", Pills(treatment))));

        // Treated per week, the last eight weeks.
        var week = _sim.GameWeek(_sim.Now);
        // Always eight weeks wide, so one early week doesn't fill the chart; weeks still to come show empty.
        var fromWeek = Math.Max(0, week - 7);
        var data = Enumerable.Range(fromWeek, 8).Select(w => ($"Wk {w + 1}", (double)e.PerWeek.GetValueOrDefault(w))).ToList();
        var chartCard = new PanelContainer();
        chartCard.AddThemeStyleboxOverride("panel", UiStyle.Inset(14));
        var chartColumn = new VBoxContainer();
        chartColumn.AddChild(CardHeading("TREATED PER WEEK"));
        var chart = new StatsBarChart(UiStyle.Blue, UiStyle.Red) { CustomMinimumSize = new Vector2(0, 130) };
        chart.SetData(data, v => v.ToString("0"));
        chartColumn.AddChild(chart);
        chartCard.AddChild(chartColumn);
        _page.AddChild(chartCard);
    }

    private static string Pills(IEnumerable<string> items)
    {
        var list = items.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().ToList();
        return list.Count == 0 ? "[color=#5B6B79]None[/color]" : string.Join("   ", list.Select(i => $"[bgcolor=#F2F5F8] {ClinicalText.Escape(i)} [/bgcolor]"));
    }

    private static Label CardHeading(string text)
    {
        var heading = UiStyle.Caption(text, 12);
        heading.AddThemeColorOverride("font_color", UiStyle.Blue);
        UiStyle.Strong(heading);
        return heading;
    }

    private static Control Cards(params (string Title, string Body)[] cards)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        foreach (var (title, body) in cards)
        {
            var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            card.AddThemeStyleboxOverride("panel", UiStyle.Inset(14));
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 4);
            column.AddChild(CardHeading(title));
            var text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, Text = body, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            text.AddThemeColorOverride("default_color", UiStyle.TextDark);
            text.AddThemeConstantOverride("line_separation", 6);
            column.AddChild(text);
            card.AddChild(column);
            row.AddChild(card);
        }

        return row;
    }

    private static Color DepartmentColour(string department) => department switch
    {
        "emergency" => new Color("#C4373D"),
        "trauma_orthopaedics" => new Color("#5D4037"),
        "critical_care" => new Color("#AD1457"),
        "cardiology" => new Color("#D32F2F"),
        "general_medicine" => new Color("#1F5FBF"),
        "general_surgery" => new Color("#0E8A8A"),
        "gynaecology" or "maternity" => new Color("#B0457A"),
        "paediatrics" => new Color("#EF6C00"),
        "neurology" => new Color("#6B4FBB"),
        "gastroenterology" => new Color("#8D6E63"),
        _ => new Color("#5B6B79"),
    };
}
