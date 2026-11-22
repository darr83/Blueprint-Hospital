using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The Calendar (K): the season and what is going round, the hospital pressure badge's reasons, the
/// week ahead (how busy each day should be) and the year at a glance (busyness and how many need a bed).
/// </summary>
public partial class CalendarPanel : PanelContainer
{
    private readonly RichTextLabel _now = Text();
    private readonly RichTextLabel _week = Text();
    private readonly RichTextLabel _commoner = Text();
    private readonly RichTextLabel _events = Text();
    private readonly YearChart _year = new() { CustomMinimumSize = new Vector2(0, 150) };
    private HospitalSimulation _sim = null!;
    private double _refresh;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        _year.Simulation = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", HudStyle.DarkPanel());

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        var header = new HBoxContainer();
        var title = new Label { Text = "Calendar", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", UiStyle.TextDark);
        header.AddChild(title);
        var close = new Button { Text = "✕", ThemeTypeVariation = UiStyle.Secondary, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(40, 40) };
        close.Pressed += () => Visible = false;
        header.AddChild(close);
        column.AddChild(header);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        body.AddChild(_now);
        body.AddChild(Heading("The week ahead"));
        body.AddChild(_week);
        body.AddChild(Heading("Coming up"));
        body.AddChild(_events);
        body.AddChild(Heading("The year at a glance"));
        var key = UiStyle.Caption("Top: how many come to A&E. Bottom: how many of them need a bed. Summer is busiest at the door; winter fills the beds.", 12);
        key.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(key);
        body.AddChild(_year);
        body.AddChild(Heading("Commoner than usual right now"));
        body.AddChild(_commoner);
        scroll.AddChild(body);
        column.AddChild(scroll);
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

    public void Refresh()
    {
        var now = _sim.Now;
        var season = SeasonCalendar.SeasonOf(now);
        var sb = new StringBuilder();
        sb.Append($"[font_size=17][b]{SeasonCalendar.Name(season)}[/b][/font_size]   [color=#5B6B79]{now:dddd d MMMM yyyy}[/color]\n");
        if (!_sim.SeasonsEnabled)
        {
            sb.Append("[color=#5B6B79]Seasons are off in this game, so every day is like an average day. They are chosen on the New Game screen.[/color]\n");
        }
        else if (!_sim.SeasonsActive)
        {
            sb.Append("[color=#5B6B79]Seasons start at level 2. Until then every day is like an average day.[/color]\n");
        }
        else
        {
            sb.Append(SeasonNote(season)).Append('\n');
            foreach (var wave in _sim.Seasons!.Profile.Waves)
            {
                var stage = _sim.Seasons.Stage(wave.Id, now);
                var rising = _sim.Seasons.Rising(wave.Id, now);
                var colour = stage switch
                {
                    WaveStage.High => ClinicalText.RedHex,
                    WaveStage.Moderate => ClinicalText.AmberTextHex,
                    WaveStage.Low => "#5B6B79",
                    _ => ClinicalText.GreenHex,
                };
                var word = stage == WaveStage.None ? "not about" : stage.ToString().ToLowerInvariant() + (rising == true ? ", rising" : rising == false ? ", falling" : string.Empty);
                sb.Append($"{wave.Name}: [color={colour}][b]{word}[/b][/color]   ");
            }

            sb.Append('\n');
        }

        var pressure = _sim.Pressure;
        sb.Append($"\nHospital pressure: [color={PressureHex(pressure.Level)}][b]{pressure.Level} of 4, {pressure.Name}[/b][/color]\n");
        sb.Append(pressure.Reasons.Count == 0
            ? "[color=#5B6B79]Beds free, A&E flowing, no ambulances waiting.[/color]"
            : "[color=#5B6B79]" + string.Join(" · ", pressure.Reasons) + "[/color]");
        _now.Text = sb.ToString();

        var weather = _sim.WeatherActive;
        var week = new StringBuilder(weather ? "[table=5]" : "[table=4]");
        foreach (var day in _sim.WeekOutlook())
        {
            var change = (day.Busyness - 1) * 100;
            var hex = change >= 12 ? ClinicalText.RedHex : change >= 4 ? ClinicalText.AmberTextHex : change <= -4 ? ClinicalText.GreenHex : "#17232E";
            var bar = new string('▮', Math.Clamp((int)Math.Round(day.Busyness * 8), 1, 14));
            week.Append($"[cell][b]{(day.Date == now.Date ? "Today" : day.Date.ToString("ddd d"))}[/b]   [/cell]");
            if (weather)
            {
                week.Append($"[cell]{day.Weather?.Brief() ?? string.Empty}   [/cell]");
            }

            week.Append($"[cell][color={hex}]{bar}[/color]  [/cell]");
            week.Append($"[cell][color={hex}]{(Math.Abs(change) < 2 ? "normal" : (change > 0 ? "+" : string.Empty) + change.ToString("0") + "%")}[/color]   [/cell]");
            week.Append($"[cell][color=#5B6B79]{day.BedShare * 100:0}% need a bed{(day.Notes.Count > 0 ? " · " + string.Join(", ", day.Notes) : string.Empty)}[/color][/cell]");
        }

        _week.Text = week.Append("[/table]").ToString();

        _events.Text = EventsText(now);

        var commoner = _sim.SeasonsActive
            ? _sim.Database.Conditions.Values
                .Where(c => !c.IsMaternity && !c.IsPlanned)
                .Select(c => (c.Name, Weight: _sim.SeasonalWeight(c, now)))
                .Where(x => x.Weight >= 1.25)
                .OrderByDescending(x => x.Weight)
                .Take(8)
                .ToList()
            : new List<(string Name, double Weight)>();
        _commoner.Text = commoner.Count == 0
            ? "[color=#5B6B79]Nothing out of the ordinary.[/color]"
            : string.Join("\n", commoner.Select(x => $"{x.Name}  [color=#5B6B79]×{x.Weight:0.0}[/color]"));
        _year.QueueRedraw();
    }

    /// <summary>The next couple of months' events, with what each brings, and who is off sick or on leave this shift.</summary>
    private string EventsText(DateTime now)
    {
        if (!_sim.CalendarEventsActive)
        {
            return "[color=#5B6B79]Bank holidays, New Year's Eve, big matches and the like come with seasons.[/color]";
        }

        var sb = new StringBuilder();
        var on = _sim.EventsNow.Where(d => d.Event.Show).Select(d => d.Event.Id).ToHashSet();
        foreach (var day in _sim.CalendarEvents!.Upcoming(now, 60).Where(d => d.Event.Show).Take(6))
        {
            var e = day.Event;
            var when = on.Contains(e.Id) ? "[color=#C0392B][b]On now[/b][/color]" : day.Day.Date == now.Date ? "[b]Today[/b]" : $"[b]{day.Day:ddd d MMM}[/b]";
            var hours = e.FromHour > 0 || e.ToHour < 24 ? $" · {Hour(e.FromHour)} to {Hour(e.ToHour)}" : e.Days > 1 ? $" · {e.Days} days" : string.Empty;
            sb.Append($"{when}  {e.Name}[color=#5B6B79]{hours}[/color]\n[color=#5B6B79]{e.Description}[/color]\n");
        }

        var away = _sim.Staff.Where(s => s.Shift == _sim.CurrentShift).Select(s => _sim.AbsenceOf(s, _sim.CurrentShiftStart)).ToList();
        var sick = away.Count(a => a == StaffAbsence.Sick);
        var leave = away.Count(a => a == StaffAbsence.Leave);
        sb.Append(sick + leave == 0
            ? "[color=#5B6B79]Everyone on this shift is in.[/color]"
            : $"This shift: [color={ClinicalText.AmberTextHex}][b]{sick} off sick, {leave} on leave[/b][/color] [color=#5B6B79](Staff rota, T)[/color]");
        return sb.ToString();
    }

    private static string Hour(double hour) => $"{(int)hour % 24:00}:{(int)Math.Round((hour % 1) * 60):00}";

    public static string PressureHex(int level) => level switch
    {
        1 => ClinicalText.GreenHex,
        2 => ClinicalText.AmberTextHex,
        3 => "#D35400",
        _ => ClinicalText.RedHex,
    };

    private static string SeasonNote(Season season) => season switch
    {
        Season.Winter => "The sickest time of year: chest infections, flu, heart failure and falls on ice. Fewer come in, but more need a bed.",
        Season.Spring => "Pollen makes asthma worse; more people out on bikes and in the garden.",
        Season.Summer => "The busiest time at the front door: sprains, cuts, children's injuries, sunburn, heat and stings.",
        _ => "Schools go back (an asthma peak in September), the evenings draw in, and croup and RSV start.",
    };

    private static RichTextLabel Text()
    {
        var label = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        label.AddThemeColorOverride("default_color", UiStyle.TextDark);
        label.AddThemeFontSizeOverride("normal_font_size", 14);
        label.AddThemeFontSizeOverride("bold_font_size", 14);
        return label;
    }

    private static Label Heading(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AddThemeColorOverride("font_color", UiStyle.TextDark);
        return label;
    }

    /// <summary>Twelve bars, January to December: height is how many come to A&amp;E, the dark part how many need a bed.</summary>
    private sealed partial class YearChart : Control
    {
        public HospitalSimulation? Simulation { get; set; }

        public override void _Draw()
        {
            if (Simulation is null)
            {
                return;
            }

            var font = ThemeDB.FallbackFont;
            var months = Simulation.YearOutlook(Simulation.Now.Year);
            const float label = 16;
            const float gap = 10;
            var rowHeight = (Size.Y - label - gap) / 2f;
            var width = Size.X / 12f;

            // Each row starts from 75% of its own busiest month, so the differences show.
            void Row(Func<MonthOutlook, double> value, float top, Color colour, Color current, string caption)
            {
                var most = months.Max(value);
                for (var i = 0; i < months.Count; i++)
                {
                    var share = (float)Math.Clamp((value(months[i]) - (most * 0.75)) / (most * 0.25), 0.06, 1);
                    var h = (rowHeight - 14) * share;
                    var x = (i * width) + 3;
                    DrawRect(new Rect2(x, top + rowHeight - h, width - 6, h), i == Simulation.Now.Month - 1 ? current : colour);
                }

                DrawString(font, new Vector2(0, top + 11), caption, HorizontalAlignment.Left, -1, 12, UiStyle.TextMuted);
            }

            Row(m => m.Volume, 0, new Color("#9DBBE6"), UiStyle.Blue, "Coming to A&E");
            Row(m => m.Volume * m.BedShare, rowHeight + gap, new Color("#E8B48A"), new Color("#C0602A"), "Needing a bed");
            for (var i = 0; i < months.Count; i++)
            {
                var name = new DateTime(2001, i + 1, 1).ToString("MMM");
                DrawString(font, new Vector2(i * width, Size.Y - 2), name, HorizontalAlignment.Center, width, 12, i == Simulation.Now.Month - 1 ? UiStyle.TextDark : UiStyle.TextMuted);
            }
        }
    }
}
