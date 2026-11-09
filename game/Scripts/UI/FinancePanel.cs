using System.Linq;
using System.Text;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The hospital's money: today's and recent days' income and spending by category, what the hospital
/// costs to run each day, and what it is paid for each patient.
/// </summary>
public partial class FinancePanel : PanelContainer
{
    private readonly RichTextLabel _text = new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private HospitalSimulation _sim = null!;
    private double _refresh;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", HudStyle.DarkPanel());

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        var header = new HBoxContainer();
        var title = new Label { Text = "Finances", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", UiStyle.TextDark);
        header.AddChild(title);
        var close = new Button { Text = "✕", ThemeTypeVariation = UiStyle.Secondary, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(40, 40) };
        close.Pressed += () => Visible = false;
        header.AddChild(close);
        column.AddChild(header);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _text.AddThemeColorOverride("default_color", new Color("#17232E"));
        _text.AddThemeFontSizeOverride("normal_font_size", 15);
        _text.AddThemeFontSizeOverride("bold_font_size", 15);
        scroll.AddChild(_text);
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
            _refresh = 0.5;
            Refresh();
        }
    }

    private void Refresh()
    {
        var sb = new StringBuilder();
        var balanceHex = _sim.Balance < 0 ? ClinicalText.RedHex : ClinicalText.GreenHex;
        sb.Append($"Balance [b][color={balanceHex}]£{_sim.Balance:N0}[/color][/b]\n\n");

        var days = _sim.Accounts.ToList();
        var today = days.FirstOrDefault();
        var yesterday = days.Skip(1).FirstOrDefault();
        var week = days.Take(7).ToList();
        sb.Append("[table=4]");
        Cell(sb, "[color=#5B6B79]£[/color]");
        Cell(sb, "[color=#5B6B79]Today[/color]", right: true);
        Cell(sb, "[color=#5B6B79]Yesterday[/color]", right: true);
        Cell(sb, $"[color=#5B6B79]{(week.Count == 1 ? "1 day" : $"{week.Count} days")}[/color]", right: true);
        foreach (var category in System.Enum.GetValues<MoneyCategory>())
        {
            var weekTotal = week.Sum(d => d[category]);
            if (weekTotal == 0 && (today?[category] ?? 0) == 0)
            {
                continue;
            }

            Cell(sb, CategoryName(category));
            Cell(sb, Money(today?[category] ?? 0), right: true);
            Cell(sb, Money(yesterday?[category] ?? 0), right: true);
            Cell(sb, Money(weekTotal), right: true);
        }

        Cell(sb, "[b]Net[/b]");
        Cell(sb, $"[b]{Money(today?.Net ?? 0)}[/b]", right: true);
        Cell(sb, $"[b]{Money(yesterday?.Net ?? 0)}[/b]", right: true);
        Cell(sb, $"[b]{Money(week.Sum(d => d.Net))}[/b]", right: true);
        sb.Append("[/table]\n\n");

        sb.Append("[color=#5B6B79]Running costs per day[/color]\n");
        sb.Append($"Staff wages  £{_sim.StaffCostPerDay:N0}  ({_sim.Staff.Count(s => s.Shift == Simulation.Staff.StaffShift.Day)} on days, {_sim.Staff.Count(s => s.Shift == Simulation.Staff.StaffShift.Night)} on nights; each hire covers one 12-hour shift)\n");
        sb.Append($"Building upkeep  £{_sim.UpkeepPerDay:N0}  ({_sim.MainBuilding.AreaSquareMetres:N0} m²)\n\n");

        sb.Append("[color=#5B6B79]Paid for each patient[/color]\n");
        sb.Append($"A&E attendance  Red £{HospitalSimulation.AttendanceTariff[TriageCategory.Red]}, Amber £{HospitalSimulation.AttendanceTariff[TriageCategory.Amber]}, Green £{HospitalSimulation.AttendanceTariff[TriageCategory.Green]}\n");
        sb.Append($"Ward admission  £{HospitalSimulation.AdmissionTariff:N0} + £{HospitalSimulation.BedDayTariff} a night\n");
        sb.Append("[color=#5B6B79]Patients who leave without being seen earn nothing. Tests and medicines are paid for as they are used.[/color]");
        _text.Text = sb.ToString();
    }

    private static void Cell(StringBuilder sb, string text, bool right = false) =>
        sb.Append(right ? $"[cell][right]{text}[/right][/cell]" : $"[cell]{text}  [/cell]");

    private static string Money(long amount) => amount switch
    {
        > 0 => $"[color={ClinicalText.GreenHex}]+{amount:N0}[/color]",
        < 0 => $"[color=#C4373D]{amount:N0}[/color]",
        _ => "[color=#5B6B79]–[/color]",
    };

    internal static string CategoryName(MoneyCategory category) => category switch
    {
        MoneyCategory.Attendances => "A&E patients",
        MoneyCategory.Admissions => "Ward patients",
        MoneyCategory.Staff => "Staff wages",
        MoneyCategory.Tests => "Tests",
        MoneyCategory.Medicines => "Medicines",
        MoneyCategory.Upkeep => "Upkeep",
        MoneyCategory.Construction => "Building work",
        MoneyCategory.Operations => "Operations",
        MoneyCategory.Clinics => "Clinics",
        MoneyCategory.Catering => "Cafe and vending",
        MoneyCategory.Maternity => "Maternity",
        MoneyCategory.Cheats => "Cheats",
        _ => category.ToString(),
    };
}
