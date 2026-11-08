using System;
using System.Linq;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// Lists of everyone in the department: patients (sickest first) and staff (with what they
/// are doing and how tired they are). Clicking a row selects that person.
/// </summary>
public partial class RosterPanel : PanelContainer
{
    private readonly VBoxContainer _list = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Button _patientsTab = new() { Text = "Patients", ToggleMode = true, FocusMode = FocusModeEnum.None };
    private readonly Button _staffTab = new() { Text = "Staff", ToggleMode = true, FocusMode = FocusModeEnum.None };
    private HospitalSimulation _sim = null!;
    private bool _showStaff;
    private double _refresh;
    private string _signature = string.Empty;

    public event Action<Patient>? PatientClicked;

    public event Action<StaffMember>? StaffClicked;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        Theme = UiStyle.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", HudStyle.DarkPanel());
        CustomMinimumSize = new Vector2(340, 0);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        AddChild(column);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 4);
        foreach (var tab in new[] { _patientsTab, _staffTab })
        {
            tab.CustomMinimumSize = new Vector2(100, 32);
            tabs.AddChild(tab);
        }

        _patientsTab.Pressed += () => ShowTab(false);
        _staffTab.Pressed += () => ShowTab(true);
        column.AddChild(tabs);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_list);
        column.AddChild(scroll);
        ShowTab(false);
    }

    public void ShowTab(bool staff)
    {
        _showStaff = staff;
        _patientsTab.SetPressedNoSignal(!staff);
        _staffTab.SetPressedNoSignal(staff);
        _signature = string.Empty;
        _refresh = 0;
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        _refresh -= delta;
        if (_refresh > 0)
        {
            return;
        }

        _refresh = 1.0;
        if (_showStaff)
        {
            BuildStaff();
        }
        else
        {
            BuildPatients();
        }
    }

    private void BuildPatients()
    {
        var now = _sim.Now;
        var patients = _sim.Patients
            .Where(p => p.IsInDepartment)
            .OrderBy(p => p.Triage switch { TriageCategory.Red => 0, TriageCategory.Amber => 1, TriageCategory.Green => 2, _ => 3 })
            .ThenBy(p => p.ArrivedAt)
            .ToList();

        var rows = patients.Select(p => (Patient: p, Text: $"{p.FullName}, {p.Age}  ·  {ClinicalText.Duration(p.TimeInDepartment(now))}\n{ClinicalText.Whereabouts(_sim, p)}")).ToList();
        Rebuild(string.Join("|", rows.Select(r => r.Text)), () =>
        {
            if (rows.Count == 0)
            {
                AddNote("No patients in the department.");
            }

            foreach (var (patient, text) in rows)
            {
                AddRow(ClinicalText.TriageColour(patient.Triage), text, () => PatientClicked?.Invoke(patient));
            }
        });
    }

    private void BuildStaff()
    {
        var rows = _sim.Staff.Select(m =>
        {
            var tired = m.Fatigue >= 0.75 ? "exhausted" : m.Fatigue >= 0.5 ? "tired" : "fresh";
            return (Member: m, Text: $"{m.Name}  ·  {m.Grade}\n{m.StatusText}  ·  {tired} ({m.Fatigue:P0})  ·  {m.TasksCompleted} tasks");
        }).ToList();

        Rebuild(string.Join("|", rows.Select(r => r.Text)), () =>
        {
            foreach (var (member, text) in rows)
            {
                var colour = member.Role switch
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
                    StaffRole.PlasterTechnician => new Color("#5D6B78"),
                    StaffRole.Physiotherapist => new Color("#1A5E8A"),
                    StaffRole.Porter => new Color("#3C6E47"),
                    _ => new Color("#6E5A8C"),
                };
                AddRow(colour, text, () => StaffClicked?.Invoke(member));
            }
        });
    }

    private void Rebuild(string signature, Action build)
    {
        if (signature == _signature)
        {
            return;
        }

        _signature = signature;
        foreach (var child in _list.GetChildren())
        {
            child.QueueFree();
        }

        build();
    }

    private void AddRow(Color stripeColour, string text, Action onPressed)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(new ColorRect { CustomMinimumSize = new Vector2(6, 0), Color = stripeColour });
        row.AddChild(HudStyle.RowButton(text, onPressed));
        _list.AddChild(row);
    }

    private void AddNote(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", new Color("#5B6B79"));
        _list.AddChild(label);
    }
}
