using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// Hiring: just the latest candidates, each with their grade, skill, experience and pay, where they
/// would work, and a Hire button. A new list, with at least one of every kind of staff, arrives every
/// few game hours. Who works where (and any gaps) lives on the Staff rota (T), not here.
/// </summary>
public partial class RecruitmentPanel : PanelContainer
{
    private readonly VBoxContainer _candidates = new();
    private readonly Label _candidatesNote = UiStyle.Caption(string.Empty, 13);
    private HospitalSimulation _sim = null!;
    private string _candidatesSignature = string.Empty;
    private double _refresh;
    private Button _hireTeam = null!;

    /// <summary>The "Hire minimum team" button, for the tutorial to point at.</summary>
    public Control HireTeamButton => _hireTeam;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", UiStyle.Card());

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        AddChild(column);

        var header = new HBoxContainer();
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 0);
        var title = new Label { Text = "Recruitment" };
        UiStyle.Strong(title, UiStyle.SizeTitle);
        title.AddThemeColorOverride("font_color", UiStyle.TextDark);
        titles.AddChild(title);
        titles.AddChild(UiStyle.Caption($"New candidates arrive every {HospitalSimulation.CandidatesEveryHours} hours", 13));
        header.AddChild(titles);
        var close = UiStyle.CloseButton(() => Visible = false);
        header.AddChild(close);
        column.AddChild(header);
        column.AddChild(HudWidgets.Divider());

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(body);
        column.AddChild(scroll);

        body.AddChild(HudWidgets.Section("New candidates"));
        _candidatesNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_candidatesNote);
        _hireTeam = UiStyle.SecondaryButton("Hire minimum team", HireTeam);
        _hireTeam.TooltipText = "Fill every empty post in the recommended team with the cheapest candidate of that kind. See who works where on the Staff rota (T).";
        _hireTeam.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _hireTeam.FocusMode = FocusModeEnum.None;
        body.AddChild(_hireTeam);
        _candidates.AddThemeConstantOverride("separation", 8);
        body.AddChild(_candidates);
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

    /// <summary>Rebuilds the lists straight away (after hiring, or when the panel opens).</summary>
    public void Refresh()
    {
        var hireable = _sim.ShortagesHireable();
        _hireTeam.Disabled = hireable == 0;
        _hireTeam.Text = hireable == 0 ? "Hire minimum team" : $"Hire minimum team ({hireable})";
        RefreshCandidates();
    }

    /// <summary>Hires the cheapest candidate for each empty post, then refreshes the lists.</summary>
    public void HireTeam()
    {
        _sim.HireForShortages();
        _candidatesSignature = string.Empty;
        Refresh();
    }

    private string NextCandidatesTime() =>
        ((_sim.CandidatesDate ?? _sim.Now).AddHours(HospitalSimulation.CandidatesEveryHours)).ToString("HH:mm");

    private void RefreshCandidates()
    {
        var candidates = _sim.Candidates.ToList();
        var postingsSignature = string.Join(",", _sim.Spaces.Where(s => s.IsWard || s.IsServiceRoom).Select(s => s.Id + s.Name));
        var signature = string.Join("|", candidates.Select(c => c.Id)) + postingsSignature;
        if (signature == _candidatesSignature)
        {
            return;
        }

        _candidatesSignature = signature;
        foreach (var child in _candidates.GetChildren())
        {
            child.QueueFree();
        }

        _candidatesNote.Text = candidates.Count == 0
            ? $"Everyone who applied has been hired. More arrive at {NextCandidatesTime()}."
            : $"More arrive at {NextCandidatesTime()}. Each hire works one 12-hour shift: days 08:00 to 20:00 or nights 20:00 to 08:00.";

        var needs = _sim.RecommendedStaffing();
        foreach (var candidate in candidates.OrderBy(c => c.Role).ThenByDescending(c => c.Skill))
        {
            _candidates.AddChild(CandidateCard(candidate, needs));
        }
    }

    private Control CandidateCard(JobCandidate candidate, IReadOnlyList<StaffingNeed> needs)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Surface, 12, UiStyle.Hairline, 12));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        card.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 10);
        var avatar = HudWidgets.Avatar(40);
        HudWidgets.SetAvatar(avatar, candidate.Name.Replace("Dr ", string.Empty), RoleColour(candidate.Role));
        header.AddChild(avatar.Badge);
        var who = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        who.AddThemeConstantOverride("separation", 0);
        var name = new Label { Text = candidate.Name, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        name.AddThemeFontSizeOverride("font_size", 16);
        who.AddChild(name);
        who.AddChild(UiStyle.Caption($"{Capitalise(candidate.Grade)} · age {candidate.Age}", 13));
        if (candidate.Specialty is { } specialty)
        {
            who.AddChild(UiStyle.Caption(HospitalSimulation.SpecialtyTitle(specialty), 13));
        }
        header.AddChild(who);
        var pay = new Label { Text = $"£{candidate.Salary:N0}", VerticalAlignment = VerticalAlignment.Center };
        pay.AddThemeFontSizeOverride("font_size", 15);
        header.AddChild(pay);
        column.AddChild(header);

        var facts = new HFlowContainer();
        facts.AddThemeConstantOverride("h_separation", 6);
        facts.AddThemeConstantOverride("v_separation", 6);
        foreach (var text in new[]
                 {
                     $"Skill {candidate.Skill:P0}",
                     candidate.YearsExperience == 1 ? "1 year" : $"{candidate.YearsExperience} years",
                     candidate.Speed >= 1.07 ? "Quick" : candidate.Speed <= 0.93 ? "Steady" : "Average pace",
                 })
        {
            var chip = HudWidgets.Chip();
            HudWidgets.SetChip(chip, text, UiStyle.Ground);
            facts.AddChild(chip.Chip);
        }

        column.AddChild(facts);

        var action = new HBoxContainer();
        action.AddThemeConstantOverride("separation", 8);
        var postings = _sim.PostingsFor(candidate.Role);
        // Don't let a long room name widen the card past the panel: the button trims instead.
        var where = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40), ThemeTypeVariation = UiStyle.Secondary, FitToLongestItem = false, ClipText = true };
        foreach (var posting in postings)
        {
            where.AddItem(posting.Name);
        }

        // Suggest wherever is shortest of this kind of staff.
        var best = postings.Select((p, i) => (Index: i, Short: needs.Where(n => n.RoomId == p.RoomId && n.Role == candidate.Role).Sum(n => n.Short)))
            .OrderByDescending(x => x.Short).ThenBy(x => x.Index).First().Index;
        where.Select(best);
        where.TooltipText = "Where they will work";
        action.AddChild(where);

        // Day or night: suggest whichever shift is shorter where they'd work.
        var shift = new OptionButton { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(92, 40), ThemeTypeVariation = UiStyle.Secondary };
        shift.AddItem("Days");
        shift.AddItem("Nights");
        shift.TooltipText = "Days 08:00 to 20:00, nights 20:00 to 08:00";
        void SuggestShift()
        {
            var posting = postings[Math.Clamp(where.Selected, 0, postings.Count - 1)];
            shift.Select(_sim.ShorterShift(candidate.Role, posting.RoomId) == StaffShift.Day ? 0 : 1);
        }

        SuggestShift();
        where.ItemSelected += _ => SuggestShift();
        action.AddChild(shift);
        var hire = UiStyle.Button("Hire", () =>
        {
            var posting = postings[Math.Clamp(where.Selected, 0, postings.Count - 1)];
            _sim.Hire(candidate.Id, posting.RoomId, shift.Selected == 1 ? StaffShift.Night : StaffShift.Day);
            _candidatesSignature = string.Empty;
            Refresh();
        }, 90);
        action.AddChild(hire);
        column.AddChild(action);
        return card;
    }

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    public static Color RoleColour(StaffRole role) => role switch
    {
        StaffRole.Doctor => new Color("#1F8A8A"),
        StaffRole.Nurse => UiStyle.Blue,
        StaffRole.Radiographer => new Color("#8E2448"),
        StaffRole.Surgeon => new Color("#00897B"),
        StaffRole.Pharmacist => new Color("#43A047"),
        StaffRole.CardiacPhysiologist => new Color("#7A2E4A"),
        StaffRole.Neurophysiologist => new Color("#5A3D7A"),
        StaffRole.Sonographer => new Color("#2F5D73"),
        StaffRole.Midwife => new Color("#8A2D6E"),
        StaffRole.Endoscopist => new Color("#2C4F7C"),
        StaffRole.PlasterTechnician => new Color("#5D6B78"),
        StaffRole.Physiotherapist => new Color("#1A5E8A"),
        StaffRole.Anaesthetist => new Color("#2E4A9E"),
        StaffRole.ScrubNurse => new Color("#7A3B69"),
        StaffRole.OutreachNurse => new Color("#B23A48"),
        StaffRole.Porter => new Color("#3C6E47"),
        StaffRole.Cleaner => new Color("#4F8A2E"),
        StaffRole.HealthcareAssistant => new Color("#6B7782"),
        StaffRole.ChildrensNurse => new Color("#C9573F"),
        StaffRole.Paediatrician => new Color("#4C5BC0"),
        StaffRole.PlaySpecialist => new Color("#43A047"),
        StaffRole.PalsOfficer => new Color("#0E7C86"),
        _ => new Color("#6E5A8C"),
    };
}
