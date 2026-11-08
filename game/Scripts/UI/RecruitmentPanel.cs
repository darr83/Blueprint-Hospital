using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// Hiring. The recommended team for each place (a guide the player can ignore), then today's
/// candidates: each with their grade, skill, experience and pay, where they would work, and a Hire button.
/// A new list of candidates arrives every morning at 08:00.
/// </summary>
public partial class RecruitmentPanel : PanelContainer
{
    private readonly VBoxContainer _needs = new();
    private readonly VBoxContainer _candidates = new();
    private readonly Label _needsSummary = UiStyle.Caption(string.Empty, 13);
    private readonly Label _candidatesNote = UiStyle.Caption(string.Empty, 13);
    private HospitalSimulation _sim = null!;
    private string _needsSignature = string.Empty;
    private string _candidatesSignature = string.Empty;
    private double _refresh;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", UiStyle.Card(20));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        AddChild(column);

        var header = new HBoxContainer();
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 0);
        var title = new Label { Text = "Recruitment" };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", UiStyle.TextDark);
        titles.AddChild(title);
        titles.AddChild(UiStyle.Caption("New candidates arrive every morning at 08:00", 13));
        header.AddChild(titles);
        var close = UiStyle.SecondaryButton("✕", () => Visible = false);
        close.TooltipText = "Close (Esc)";
        close.CustomMinimumSize = new Vector2(40, 40);
        header.AddChild(close);
        column.AddChild(header);
        column.AddChild(HudWidgets.Divider());

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(body);
        column.AddChild(scroll);

        body.AddChild(HudWidgets.Section("Recommended team"));
        _needsSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_needsSummary);
        _needs.AddThemeConstantOverride("separation", 4);
        body.AddChild(_needs);

        body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        body.AddChild(HudWidgets.Section("Today's candidates"));
        _candidatesNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_candidatesNote);
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
        RefreshNeeds();
        RefreshCandidates();
    }

    private void RefreshNeeds()
    {
        var needs = _sim.RecommendedStaffing();
        var signature = string.Join("|", needs.Select(n => $"{n.Place}{n.Role}{n.Have}/{n.Recommended}"));
        if (signature == _needsSignature)
        {
            return;
        }

        _needsSignature = signature;
        foreach (var child in _needs.GetChildren())
        {
            child.QueueFree();
        }

        var shortBy = needs.Sum(n => n.Short);
        _needsSummary.Text = shortBy == 0
            ? "You have the recommended team everywhere. It's a guide: hire more or fewer as you like."
            : $"{shortBy} short of the recommended team. It's a guide: hire more or fewer as you like.";

        foreach (var place in needs.GroupBy(n => n.Place))
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color("#F7F9FB"), 10, null, 10));
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 10);
            row.AddChild(line);
            var name = new Label { Text = place.Key, CustomMinimumSize = new Vector2(120, 0), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
            name.AddThemeFontSizeOverride("font_size", 14);
            line.AddChild(name);
            var counts = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            counts.AddThemeConstantOverride("h_separation", 6);
            counts.AddThemeConstantOverride("v_separation", 4);
            foreach (var need in place)
            {
                var chip = HudWidgets.Chip();
                var text = $"{need.Have}/{need.Recommended} {StaffGrades.RoleName(need.Role, plural: need.Recommended != 1)}";
                HudWidgets.SetChip(chip, text, need.Short > 0 ? UiStyle.AmberTint : UiStyle.BlueTint);
                if (need.Short > 0)
                {
                    chip.Text.AddThemeColorOverride("font_color", UiStyle.AmberText);
                }

                counts.AddChild(chip.Chip);
            }

            line.AddChild(counts);
            _needs.AddChild(row);
        }
    }

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
            ? "Everyone who applied today has been hired. More arrive tomorrow at 08:00."
            : "Salaries are per post: shifts are covered around the clock.";

        var needs = _sim.RecommendedStaffing();
        foreach (var candidate in candidates.OrderBy(c => c.Role).ThenByDescending(c => c.Skill))
        {
            _candidates.AddChild(CandidateCard(candidate, needs));
        }
    }

    private Control CandidateCard(JobCandidate candidate, IReadOnlyList<StaffingNeed> needs)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Surface, 12, new Color("#E6ECF1"), 12));
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
        header.AddChild(who);
        var pay = new Label { Text = $"£{candidate.Salary:N0}", VerticalAlignment = VerticalAlignment.Center };
        pay.AddThemeFontSizeOverride("font_size", 15);
        header.AddChild(pay);
        column.AddChild(header);

        var facts = new HBoxContainer();
        facts.AddThemeConstantOverride("separation", 6);
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
        var where = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40), ThemeTypeVariation = UiStyle.Secondary };
        foreach (var posting in postings)
        {
            where.AddItem(posting.Name);
        }

        // Suggest wherever is shortest of this kind of staff.
        var best = postings.Select((p, i) => (Index: i, Short: needs.FirstOrDefault(n => n.RoomId == p.RoomId && n.Role == candidate.Role)?.Short ?? 0))
            .OrderByDescending(x => x.Short).ThenBy(x => x.Index).First().Index;
        where.Select(best);
        where.TooltipText = "Where they will work";
        action.AddChild(where);
        var hire = UiStyle.Button("Hire", () =>
        {
            var posting = postings[Math.Clamp(where.Selected, 0, postings.Count - 1)];
            _sim.Hire(candidate.Id, posting.RoomId);
            _needsSignature = _candidatesSignature = string.Empty;
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
        StaffRole.Nurse => new Color("#1F5FBF"),
        StaffRole.Radiographer => new Color("#8E2448"),
        StaffRole.Surgeon => new Color("#00897B"),
        StaffRole.Pharmacist => new Color("#43A047"),
        StaffRole.CardiacPhysiologist => new Color("#7A2E4A"),
        StaffRole.Neurophysiologist => new Color("#5A3D7A"),
        StaffRole.Sonographer => new Color("#2F5D73"),
        StaffRole.Endoscopist => new Color("#2C4F7C"),
        StaffRole.Porter => new Color("#3C6E47"),
        _ => new Color("#6E5A8C"),
    };
}
