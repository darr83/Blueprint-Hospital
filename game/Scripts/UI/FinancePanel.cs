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
    private readonly Label[] _tiles = new Label[3];
    private HospitalSimulation _sim = null!;
    private double _refresh;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", HudStyle.DarkPanel());

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", UiStyle.Gap);
        AddChild(column);

        var header = UiStyle.PanelHeader("Finances", "Income and spending, today and recent days", () => Visible = false);
        column.AddChild(header.Row);
        column.AddChild(HudWidgets.Divider());

        var tiles = new HBoxContainer();
        tiles.AddThemeConstantOverride("separation", UiStyle.GapSmall);
        foreach (var (caption, index) in new[] { ("Balance", 0), ("Today", 1), ("Yesterday", 2) })
        {
            var tile = HudWidgets.StatTile(caption);
            _tiles[index] = tile.Value;
            tiles.AddChild(tile.Tile);
        }

        column.AddChild(tiles);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _text.AddThemeColorOverride("default_color", UiStyle.TextDark);
        _text.AddThemeFontSizeOverride("normal_font_size", UiStyle.SizeBody);
        _text.AddThemeFontSizeOverride("bold_font_size", UiStyle.SizeBody);
        _text.AddThemeColorOverride("table_odd_row_bg", UiStyle.Subtle);
        _text.AddThemeColorOverride("table_even_row_bg", UiStyle.Surface);
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
        var days = _sim.Accounts.ToList();
        var today = days.FirstOrDefault();
        var yesterday = days.Skip(1).FirstOrDefault();
        var week = days.Take(7).ToList();
        SetTile(0, _sim.Balance, signed: false);
        SetTile(1, today?.Net ?? 0, signed: true);
        SetTile(2, yesterday?.Net ?? 0, signed: true);

        sb.Append($"[font_size={UiStyle.SizeHeading}][b]By category[/b][/font_size]\n");
        sb.Append("[table=4]");
        Head(sb, "£");
        Head(sb, "Today", right: true);
        Head(sb, "Yesterday", right: true);
        Head(sb, week.Count == 1 ? "1 day" : $"{week.Count} days", right: true);
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

        Total(sb, "[b]Net[/b]");
        Total(sb, $"[b]{Money(today?.Net ?? 0)}[/b]", right: true);
        Total(sb, $"[b]{Money(yesterday?.Net ?? 0)}[/b]", right: true);
        Total(sb, $"[b]{Money(week.Sum(d => d.Net))}[/b]", right: true);
        sb.Append("[/table]\n\n");

        sb.Append($"[font_size={UiStyle.SizeHeading}][b]Running costs per day[/b][/font_size]\n");
        sb.Append(UiStyle.Facts(
            $"[color=#5B6B79]Staff wages[/color]  £{_sim.StaffCostPerDay:N0}: {_sim.Staff.Count(s => s.Shift == Simulation.Staff.StaffShift.Day)} on days, {_sim.Staff.Count(s => s.Shift == Simulation.Staff.StaffShift.Night)} on nights; each hire covers one 12-hour shift\n" +
            $"[color=#5B6B79]Building upkeep[/color]  £{_sim.UpkeepPerDay:N0} for {_sim.MainBuilding.AreaSquareMetres:N0} m²"));
        sb.Append("\n\n");

        sb.Append($"[font_size={UiStyle.SizeHeading}][b]Paid for each patient[/b][/font_size]\n");
        sb.Append(UiStyle.Facts(
            $"[color=#5B6B79]A&E attendance[/color]  Red £{HospitalSimulation.AttendanceTariff[TriageCategory.Red]}, Amber £{HospitalSimulation.AttendanceTariff[TriageCategory.Amber]}, Green £{HospitalSimulation.AttendanceTariff[TriageCategory.Green]}\n" +
            $"[color=#5B6B79]Ward admission[/color]  £{HospitalSimulation.AdmissionTariff:N0} + £{HospitalSimulation.BedDayTariff} a night"));
        sb.Append("\n[color=#5B6B79][font_size=13]Patients who leave without being seen earn nothing. Tests and medicines are paid for as they are used.[/font_size][/color]");
        _text.Text = sb.ToString();
    }

    private void SetTile(int index, long amount, bool signed)
    {
        var label = _tiles[index];
        var size = System.Math.Abs(amount);
        var figure = size >= 1_000_000 ? $"£{size / 1_000_000.0:0.00}m" : $"£{size:N0}";
        label.Text = signed && amount > 0 ? $"+{figure}" : amount < 0 ? $"−{figure}" : figure;
        label.TooltipText = $"£{amount:N0}";
        label.AddThemeColorOverride("font_color", amount < 0 ? UiStyle.Red : signed && amount > 0 ? UiStyle.Green : UiStyle.TextDark);
    }

    private static void Head(StringBuilder sb, string text, bool right = false) =>
        sb.Append(right
            ? $"[cell padding=8,6,8,6][right][color=#5B6B79][font_size=12]{text}[/font_size][/color][/right][/cell]"
            : $"[cell padding=8,6,8,6][color=#5B6B79][font_size=12]{text}[/font_size][/color][/cell]");

    private static void Total(StringBuilder sb, string text, bool right = false) =>
        sb.Append(right ? $"[cell bg=#E6EEFA padding=8,6,8,6][right]{text}[/right][/cell]" : $"[cell bg=#E6EEFA padding=8,6,8,6]{text}[/cell]");

    private static void Cell(StringBuilder sb, string text, bool right = false) =>
        sb.Append(right ? $"[cell padding=8,5,8,5][right]{text}[/right][/cell]" : $"[cell expand=1 padding=8,5,8,5]{text}[/cell]");

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
        MoneyCategory.Prescriptions => "Prescriptions",
        MoneyCategory.Cheats => "Cheats",
        MoneyCategory.Grants => "Level grants",
        _ => category.ToString(),
    };
}
