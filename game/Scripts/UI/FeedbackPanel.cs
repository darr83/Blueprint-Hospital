using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// Patient feedback (L, or the star rating in the top bar): complaints to answer, thank-you cards,
/// and the star ratings patients give as they leave. One panel for both the 2D and 3D views.
/// </summary>
public partial class FeedbackPanel : PanelContainer
{
    public enum Page
    {
        Complaints,
        ThankYous,
        Ratings,
    }

    public static readonly Color Gold = new("#E8A317");
    private static readonly Color GoldTint = new("#FDF3DC");

    private readonly Dictionary<Page, Button> _tabs = new();
    private readonly Dictionary<StatsPeriod, Button> _periods = new();
    private readonly VBoxContainer _body = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private HBoxContainer _periodRow = null!;
    private HospitalSimulation _sim = null!;
    private Page _page = Page.Complaints;
    private StatsPeriod _period = StatsPeriod.Last7Days;
    private string _signature = string.Empty;
    private double _refresh;

    /// <summary>The player asked to see a patient's record.</summary>
    public event Action<Patient>? PatientClicked;

    public Page Showing => _page;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", UiStyle.Card());

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        AddChild(column);
        column.AddChild(UiStyle.PanelHeader("Patient feedback", "Ratings, complaints and thank-yous", () => Visible = false).Row);

        var tabs = new HBoxContainer();
        foreach (var (page, text) in new[] { (Page.Complaints, "Complaints"), (Page.ThankYous, "Thank-yous"), (Page.Ratings, "Ratings") })
        {
            var tab = UiStyle.AsTab(new Button { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill });
            tab.Pressed += () => ShowPage(page);
            _tabs[page] = tab;
            tabs.AddChild(tab);
        }

        column.AddChild(UiStyle.TabTrack(tabs));

        _periodRow = new HBoxContainer();
        foreach (var (period, text) in new[] { (StatsPeriod.Last7Days, "Last 7 days"), (StatsPeriod.Last28Days, "28 days"), (StatsPeriod.AllTime, "All time") })
        {
            var tab = UiStyle.AsTab(new Button { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill });
            tab.AddThemeFontSizeOverride("font_size", UiStyle.SizeSmall);
            tab.Pressed += () =>
            {
                _period = period;
                Rebuild();
            };
            _periods[period] = tab;
            _periodRow.AddChild(tab);
        }

        var periodTrack = UiStyle.TabTrack(_periodRow);
        column.AddChild(periodTrack);
        _periodRow.VisibilityChanged += () => periodTrack.Visible = _periodRow.Visible;

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _body.AddThemeConstantOverride("separation", 10);
        var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddChild(_body);
        scroll.AddChild(margin);
        column.AddChild(scroll);
        ShowPage(Page.Complaints);
    }

    public void ShowPage(Page page)
    {
        _page = page;
        Visible = true;
        Rebuild();
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
            _refresh = 0.5;
            Refresh();
        }
    }

    /// <summary>Rebuilds only when something on the page changed.</summary>
    public void Refresh()
    {
        var signature = Signature();
        if (signature != _signature)
        {
            Rebuild();
        }
    }

    private string Signature() => _page switch
    {
        Page.Complaints => string.Join("|", _sim.Complaints.Select(c => $"{c.Id}:{c.Status}:{c.Severity}:{c.Tried.Count}:{c.CauseFound}:{c.DueAt.Ticks}:{_sim.CanAfford(HospitalSimulation.InvestigationCost)}")) + $"|{_sim.Now:yyyyMMddHH}",
        Page.ThankYous => string.Join("|", _sim.ThankYous.Select(t => t.Id)),
        _ => $"{_period}|{_sim.FeedbackFor(_period).Count}|{_sim.FeedbackComments.Count}|{_sim.FeedbackComments.FirstOrDefault()?.At.Ticks}|{_sim.ReputationAdjustment:0.00}",
    };

    private void Rebuild()
    {
        _signature = Signature();
        foreach (var (page, tab) in _tabs)
        {
            tab.SetPressedNoSignal(page == _page);
        }

        var open = _sim.OpenComplaints.Count();
        _tabs[Page.Complaints].Text = open > 0 ? $"Complaints ({open})" : "Complaints";
        _periodRow.Visible = _page == Page.Ratings;
        foreach (var (period, tab) in _periods)
        {
            tab.SetPressedNoSignal(period == _period);
        }

        foreach (var child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }

        switch (_page)
        {
            case Page.Complaints:
                BuildComplaints();
                break;
            case Page.ThankYous:
                BuildThankYous();
                break;
            default:
                BuildRatings();
                break;
        }
    }

    // ---------------------------------------------------------------- complaints

    private void BuildComplaints()
    {
        var open = _sim.OpenComplaints.ToList();
        var dueToday = open.Count(c => c.DueAt - _sim.Now < TimeSpan.FromHours(24));
        var week = _sim.History.Total(StatsPeriod.Last7Days, _sim.Now, FeedbackKeys.Complaints);
        var spent = -_sim.History.Total(StatsPeriod.Last7Days, _sim.Now, StatKeys.Money(MoneyCategory.Complaints));
        var tiles = new HBoxContainer();
        tiles.AddThemeConstantOverride("separation", 8);
        tiles.AddChild(Tile("To answer", open.Count.ToString(), dueToday > 0 ? $"{dueToday} due within a day" : "none due today", open.Count > 0 ? Tone.Amber : Tone.Plain));
        tiles.AddChild(Tile("This week", week.ToString("0"), "complaints", Tone.Plain));
        tiles.AddChild(Tile("Cost this week", $"£{spent:N0}", "on complaints", spent > 0 ? Tone.Red : Tone.Plain));
        _body.AddChild(tiles);

        if (open.Count == 0)
        {
            _body.AddChild(Note("No complaints to answer. Patients complain after a bad stay: a long wait, care in the corridor, an accident, a cancelled operation or a missed diagnosis."));
        }

        foreach (var complaint in open)
        {
            _body.AddChild(ComplaintCard(complaint));
        }

        var closed = _sim.Complaints.Where(c => !c.IsOpen).OrderByDescending(c => c.ClosedAt).Take(12).ToList();
        if (closed.Count > 0)
        {
            _body.AddChild(UiStyle.SectionHeading("Answered"));
            foreach (var complaint in closed)
            {
                _body.AddChild(ClosedCard(complaint));
            }
        }

        _body.AddChild(Note($"Reply in time or it goes further: a concern becomes a formal complaint after a day; a formal complaint (3 days) or serious incident (2 days) goes to the Ombudsman, who fines £{HospitalSimulation.OmbudsmanFine(ComplaintSeverity.Formal):N0} or £{HospitalSimulation.OmbudsmanFine(ComplaintSeverity.Serious):N0} and the star rating drops."));
    }

    private Control ComplaintCard(Complaint complaint)
    {
        var card = new PanelContainer();
        var border = complaint.Severity == ComplaintSeverity.Serious ? new Color("#F2C4C6") : UiStyle.Border;
        card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Surface, UiStyle.RadiusCard, border, 12));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        card.AddChild(column);

        var top = new HBoxContainer();
        top.AddChild(SeverityPill(complaint.Severity));
        top.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var left = complaint.DueAt - _sim.Now;
        var due = UiStyle.Caption(complaint.Status == ComplaintStatus.Waiting && complaint.PendingUntil is { } until
            ? $"{(complaint.Pending == ComplaintResponse.Investigate ? "report" : "meeting ends")} {until:ddd HH:mm}"
            : left < TimeSpan.FromHours(24) ? $"reply by {complaint.DueAt:HH:mm} ({Hours(left)} left)" : $"reply by {complaint.DueAt:ddd HH:mm}", UiStyle.SizeCaption);
        if (complaint.Status == ComplaintStatus.Open && left < TimeSpan.FromHours(12))
        {
            UiStyle.Strong(due);
            due.AddThemeColorOverride("font_color", UiStyle.Red);
        }

        top.AddChild(due);
        column.AddChild(top);

        var from = new Label { Text = complaint.From, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        UiStyle.Strong(from, UiStyle.SizeCardTitle);
        column.AddChild(from);
        column.AddChild(UiStyle.Caption(complaint.Department + " · received " + complaint.ReceivedAt.ToString("ddd HH:mm"), UiStyle.SizeCaption));
        var quote = UiStyle.Body($"“{complaint.Quote}”");
        quote.AddThemeFontSizeOverride("font_size", UiStyle.SizeSmall);
        column.AddChild(quote);

        if (complaint.Facts.Count > 0)
        {
            var facts = new PanelContainer();
            facts.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Subtle, UiStyle.RadiusControl, null, 8));
            var lines = new VBoxContainer();
            lines.AddThemeConstantOverride("separation", 1);
            lines.AddChild(UiStyle.Caption("What the record shows", UiStyle.SizeCaption));
            foreach (var fact in complaint.Facts)
            {
                var line = UiStyle.Body("• " + fact);
                line.AddThemeFontSizeOverride("font_size", UiStyle.SizeCaption);
                lines.AddChild(line);
            }

            facts.AddChild(lines);
            column.AddChild(facts);
        }

        if (complaint.CauseFound)
        {
            column.AddChild(Banner("Investigation found: " + complaint.Cause, UiStyle.GreenTint, UiStyle.Green));
        }

        if (!string.IsNullOrEmpty(complaint.Outcome))
        {
            column.AddChild(Banner(complaint.Outcome, UiStyle.AmberTint, UiStyle.AmberText));
        }

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var option in _sim.OptionsFor(complaint))
        {
            var button = option.Response == ComplaintResponse.Apologise
                ? UiStyle.Button($"{option.Label}\n{option.Detail}", () => Answer(complaint, option.Response))
                : UiStyle.SecondaryButton($"{option.Label}\n{option.Detail}", () => Answer(complaint, option.Response));
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.CustomMinimumSize = new Vector2(0, 46);
            button.AddThemeFontSizeOverride("font_size", UiStyle.SizeCaption);
            button.ClipText = true;
            button.FocusMode = FocusModeEnum.None;
            button.Disabled = !option.Enabled;
            button.TooltipText = (option.Why is { } why ? why + ". " : string.Empty) + Explain(option.Response, complaint.Severity);
            grid.AddChild(button);
        }

        column.AddChild(grid);
        if (complaint.PatientId is { } id && PatientById(id) is { } patient)
        {
            var link = new LinkButton { Text = "Open their record", Underline = LinkButton.UnderlineMode.OnHover, FocusMode = FocusModeEnum.None };
            link.AddThemeColorOverride("font_color", UiStyle.Blue);
            link.AddThemeFontSizeOverride("font_size", UiStyle.SizeCaption);
            link.Pressed += () => PatientClicked?.Invoke(patient);
            column.AddChild(link);
        }

        return card;
    }

    private static string Explain(ComplaintResponse response, ComplaintSeverity severity)
    {
        var chance = HospitalSimulation.SatisfyChance(response, severity);
        var likely = chance >= 0.8 ? "Very likely" : chance >= 0.6 ? "Likely" : chance >= 0.4 ? "Might" : "Unlikely";
        return response switch
        {
            ComplaintResponse.Apologise => $"A written apology. {likely} to settle a {HospitalSimulation.SeverityName(severity).ToLowerInvariant()}.",
            ComplaintResponse.Meet => $"Your most senior doctor meets them for an hour. {likely} to settle it.",
            ComplaintResponse.Goodwill => $"A payment as a goodwill gesture. {likely} to settle it.",
            _ => $"A day-long investigation that tells you what went wrong, so you can fix it. {likely} to settle it.",
        };
    }

    private void Answer(Complaint complaint, ComplaintResponse response)
    {
        _sim.Respond(complaint.Id, response);
        Rebuild();
    }

    private Control ClosedCard(Complaint complaint)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", UiStyle.Inset(10));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 3);
        card.AddChild(column);
        var top = new HBoxContainer();
        var name = new Label { Text = complaint.From, SizeFlagsHorizontal = SizeFlags.ExpandFill, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        UiStyle.Strong(name, UiStyle.SizeBody);
        top.AddChild(name);
        top.AddChild(complaint.Status == ComplaintStatus.Resolved
            ? Pill("Resolved", UiStyle.GreenTint, UiStyle.Green)
            : Pill("Ombudsman", UiStyle.RedTint, UiStyle.Red));
        column.AddChild(top);
        var detail = UiStyle.Body(complaint.Outcome + (complaint.Spent > 0 ? $" Cost £{complaint.Spent:N0}." : string.Empty), muted: true);
        detail.AddThemeFontSizeOverride("font_size", UiStyle.SizeCaption);
        column.AddChild(detail);
        if (complaint.CauseFound)
        {
            var cause = UiStyle.Body("Found: " + complaint.Cause, muted: true);
            cause.AddThemeFontSizeOverride("font_size", UiStyle.SizeCaption);
            column.AddChild(cause);
        }

        return card;
    }

    // ---------------------------------------------------------------- thank-yous

    private void BuildThankYous()
    {
        var week = _sim.History.Total(StatsPeriod.Last7Days, _sim.Now, FeedbackKeys.ThankYous);
        var tiles = new HBoxContainer();
        tiles.AddThemeConstantOverride("separation", 8);
        tiles.AddChild(Tile("This week", week.ToString("0"), "thank-you cards", week > 0 ? Tone.Green : Tone.Plain));
        tiles.AddChild(Tile("All time", _sim.History.Total(StatsPeriod.AllTime, _sim.Now, FeedbackKeys.ThankYous).ToString("0"), "thank-you cards", Tone.Plain));
        _body.AddChild(tiles);
        if (_sim.ThankYous.Count == 0)
        {
            _body.AddChild(Note("No thank-you cards yet. Patients who rate their stay 5 stars sometimes send one."));
        }

        foreach (var thanks in _sim.ThankYous)
        {
            var card = new PanelContainer();
            card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.GreenTint, UiStyle.RadiusCard, new Color("#BFE3C6"), 12));
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 4);
            card.AddChild(column);
            var name = new Label { Text = thanks.From };
            UiStyle.Strong(name, UiStyle.SizeBody);
            column.AddChild(name);
            column.AddChild(UiStyle.Caption($"{thanks.Department} · {thanks.At:ddd HH:mm}", UiStyle.SizeCaption));
            var quote = UiStyle.Body($"“{thanks.Quote}”");
            quote.AddThemeFontSizeOverride("font_size", UiStyle.SizeSmall);
            quote.AddThemeColorOverride("font_color", UiStyle.Green);
            column.AddChild(quote);
            _body.AddChild(card);
        }
    }

    // ---------------------------------------------------------------- ratings

    private void BuildRatings()
    {
        var summary = _sim.FeedbackFor(_period);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        var score = new VBoxContainer { CustomMinimumSize = new Vector2(110, 0) };
        score.AddThemeConstantOverride("separation", 0);
        var big = new Label { Text = summary.Count == 0 ? "–" : summary.Average.ToString("0.0"), HorizontalAlignment = HorizontalAlignment.Center };
        UiStyle.Strong(big, 40);
        score.AddChild(big);
        var stars = new Label { Text = StarText(summary.Count == 0 ? 0 : summary.Average), HorizontalAlignment = HorizontalAlignment.Center };
        stars.AddThemeColorOverride("font_color", Gold);
        stars.AddThemeFontSizeOverride("font_size", 16);
        score.AddChild(stars);
        var from = UiStyle.Caption(summary.Count == 1 ? "from 1 patient" : $"from {summary.Count} patients", UiStyle.SizeCaption);
        from.HorizontalAlignment = HorizontalAlignment.Center;
        score.AddChild(from);
        row.AddChild(score);

        var bars = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        bars.AddThemeConstantOverride("separation", 3);
        for (var s = 5; s >= 1; s--)
        {
            var share = summary.Count == 0 ? 0 : summary.ByStars[s - 1] / (double)summary.Count;
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 6);
            line.AddChild(UiStyle.Caption($"{s}★", UiStyle.SizeCaption));
            var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = share, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            bar.AddThemeStyleboxOverride("background", UiStyle.Box(UiStyle.Hairline, 4));
            bar.AddThemeStyleboxOverride("fill", UiStyle.Box(Gold, 4));
            line.AddChild(bar);
            var pct = UiStyle.Caption($"{share:0%}", UiStyle.SizeCaption);
            pct.CustomMinimumSize = new Vector2(34, 0);
            pct.HorizontalAlignment = HorizontalAlignment.Right;
            line.AddChild(pct);
            bars.AddChild(line);
        }

        row.AddChild(bars);
        _body.AddChild(row);

        var rating = _sim.HospitalRating;
        var tiles = new HBoxContainer();
        tiles.AddThemeConstantOverride("separation", 8);
        tiles.AddChild(Tile("Would recommend", summary.Count == 0 ? "–" : $"{summary.Recommend:0%}", "Friends and Family Test", summary.Count == 0 ? Tone.Plain : summary.Recommend >= 0.8 ? Tone.Green : summary.Recommend >= 0.6 ? Tone.Amber : Tone.Red));
        tiles.AddChild(Tile("Hospital rating", rating is { } r ? $"{r:0.0} ★" : "–", Adjustment(), rating is null ? Tone.Plain : rating >= 4 ? Tone.Green : rating >= 3 ? Tone.Amber : Tone.Red));
        _body.AddChild(tiles);

        if (summary.Reasons.Count > 0)
        {
            _body.AddChild(UiStyle.SectionHeading("What patients mentioned"));
            foreach (var (reason, count) in summary.Reasons.Take(8))
            {
                var line = new HBoxContainer();
                line.AddThemeConstantOverride("separation", 8);
                line.AddChild(reason.Good ? Pill("+", UiStyle.GreenTint, UiStyle.Green) : Pill("−", UiStyle.RedTint, UiStyle.Red));
                var label = new Label { Text = reason.Label, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                label.AddThemeFontSizeOverride("font_size", UiStyle.SizeSmall);
                line.AddChild(label);
                line.AddChild(UiStyle.Strong(new Label { Text = count.ToString() }, UiStyle.SizeSmall));
                _body.AddChild(line);
            }
        }

        if (summary.Departments.Count > 0)
        {
            _body.AddChild(UiStyle.SectionHeading("By department"));
            foreach (var (department, count, average) in summary.Departments)
            {
                var line = new HBoxContainer();
                var label = new Label { Text = department, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                label.AddThemeFontSizeOverride("font_size", UiStyle.SizeSmall);
                line.AddChild(label);
                line.AddChild(UiStyle.Caption($"{count} rated  ", UiStyle.SizeCaption));
                var value = UiStyle.Strong(new Label { Text = $"★ {average:0.0}" }, UiStyle.SizeSmall);
                value.AddThemeColorOverride("font_color", average >= 4 ? UiStyle.Green : average >= 3 ? UiStyle.AmberText : UiStyle.Red);
                line.AddChild(value);
                _body.AddChild(line);
            }
        }

        if (_sim.FeedbackComments.Count > 0)
        {
            _body.AddChild(UiStyle.SectionHeading("Latest comments"));
            foreach (var comment in _sim.FeedbackComments.Take(8))
            {
                var box = new VBoxContainer();
                box.AddThemeConstantOverride("separation", 0);
                var head = new HBoxContainer();
                head.AddThemeConstantOverride("separation", 6);
                var starLabel = new Label { Text = new string('★', comment.Stars) + new string('☆', 5 - comment.Stars) };
                starLabel.AddThemeColorOverride("font_color", Gold);
                starLabel.AddThemeFontSizeOverride("font_size", UiStyle.SizeSmall);
                head.AddChild(starLabel);
                head.AddChild(UiStyle.Caption($"{comment.Name} · {comment.Department} · {comment.At:ddd HH:mm}", UiStyle.SizeCaption));
                box.AddChild(head);
                var text = UiStyle.Body($"“{comment.Quote}”");
                text.AddThemeFontSizeOverride("font_size", UiStyle.SizeSmall);
                box.AddChild(text);
                _body.AddChild(box);
            }
        }

        if (summary.Count == 0)
        {
            _body.AddChild(Note("No ratings yet. Every patient rates their stay from 1 to 5 stars as they leave."));
        }

        _body.AddChild(Note(Effect(rating)));
    }

    private string Adjustment()
    {
        var adjust = _sim.ReputationAdjustment;
        return Math.Abs(adjust) < 0.01 ? "last 7 days" : $"{adjust:+0.00;−0.00} from complaints";
    }

    private static string Effect(double? rating) => rating switch
    {
        null => "4★ and over: clinics get 10% more GP referrals. Under 3★: level grants are cut by a quarter.",
        >= 4 => "Rated 4★ or more: clinics get 10% more GP referrals.",
        < 3 => "Rated under 3★: clinics get fewer GP referrals and level grants are cut by a quarter.",
        _ => "Reach 4★ for 10% more GP referrals to your clinics. Under 3★, level grants are cut by a quarter.",
    };

    // ---------------------------------------------------------------- bits

    /// <summary>Five stars, filled to the nearest half (rounded to whole stars).</summary>
    public static string StarText(double rating)
    {
        var full = (int)Math.Clamp(Math.Round(rating), 0, 5);
        return new string('★', full) + new string('☆', 5 - full);
    }

    private Patient? PatientById(int id) => _sim.FindPatient(id) ?? _sim.DepartedPatients.FirstOrDefault(p => p.Id == id);

    private static string Hours(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} h" : $"{Math.Max(0, (int)t.TotalMinutes)} min";

    private enum Tone
    {
        Plain,
        Amber,
        Green,
        Red,
    }

    private static Control Tile(string caption, string value, string detail, Tone tone)
    {
        var (background, border, colour) = tone switch
        {
            Tone.Amber => (UiStyle.AmberTint, UiStyle.AmberBorder, UiStyle.AmberText),
            Tone.Green => (UiStyle.GreenTint, new Color("#BFE3C6"), UiStyle.Green),
            Tone.Red => (UiStyle.RedTint, new Color("#F2C4C6"), UiStyle.Red),
            _ => (UiStyle.Surface, UiStyle.Hairline, UiStyle.TextDark),
        };
        var tile = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        tile.AddThemeStyleboxOverride("panel", UiStyle.Box(background, UiStyle.RadiusCard, border, 10));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        tile.AddChild(column);
        column.AddChild(UiStyle.Caption(caption, UiStyle.SizeCaption));
        var figure = UiStyle.Strong(new Label { Text = value, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis }, UiStyle.SizeFigure - 2);
        figure.AddThemeColorOverride("font_color", colour);
        column.AddChild(figure);
        var small = UiStyle.Caption(detail, 11);
        small.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        column.AddChild(small);
        return tile;
    }

    private static Control SeverityPill(ComplaintSeverity severity) => severity switch
    {
        ComplaintSeverity.Serious => Pill("Serious incident", UiStyle.RedTint, UiStyle.Red),
        ComplaintSeverity.Formal => Pill("Formal complaint", UiStyle.AmberTint, UiStyle.AmberText),
        _ => Pill("Concern", UiStyle.Ground, UiStyle.TextMuted),
    };

    private static Control Pill(string text, Color background, Color colour)
    {
        var pill = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        var box = UiStyle.Box(background, UiStyle.RadiusChip);
        box.ContentMarginLeft = box.ContentMarginRight = 7;
        box.ContentMarginTop = box.ContentMarginBottom = 1;
        pill.AddThemeStyleboxOverride("panel", box);
        var label = UiStyle.Strong(new Label { Text = text }, 11);
        label.AddThemeColorOverride("font_color", colour);
        pill.AddChild(label);
        return pill;
    }

    private static Control Banner(string text, Color background, Color colour)
    {
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", UiStyle.Box(background, UiStyle.RadiusControl, null, 8));
        var label = UiStyle.Body(text);
        label.AddThemeFontSizeOverride("font_size", UiStyle.SizeCaption);
        label.AddThemeColorOverride("font_color", colour);
        box.AddChild(label);
        return box;
    }

    private static Label Note(string text)
    {
        var label = UiStyle.Body(text, muted: true);
        label.AddThemeFontSizeOverride("font_size", UiStyle.SizeCaption);
        return label;
    }
}
