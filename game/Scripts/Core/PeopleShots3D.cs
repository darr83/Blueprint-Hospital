using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.World3D;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Review shots of the 3D people, run with: godot --path game -- --people3d-shots=DIR (needs a display).
/// Lines up every staff uniform and a spread of patients on the lawn, standing, walking and sitting,
/// and photographs them from two sides.
/// </summary>
public partial class PeopleShots3D : Node
{
    private string _directory = "";
    private int _frame;
    private int _shot;
    private HospitalScene? _hospital;
    private Node3D? _line;

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--people3d-shots=", StringComparison.Ordinal))
            {
                var shots = new PeopleShots3D { Name = "PeopleShots3D", ProcessMode = ProcessModeEnum.Always, _directory = arg["--people3d-shots=".Length..] };
                GameManager.Instance.AddChild(shots);
                Callable.From(() => GameManager.Instance.StartNewGame("People Review General")).CallDeferred();
                return true;
            }
        }

        return false;
    }

    private static readonly (string Label, StaffRole Role, string Grade)[] Staff =
    {
        ("Nurse B5", StaffRole.Nurse, "Band 5 nurse"), ("Nurse B6", StaffRole.Nurse, "Band 6 nurse"), ("Sister B7", StaffRole.Nurse, "Band 7 nurse"),
        ("Matron", StaffRole.Nurse, "Band 8 nurse"), ("HCA", StaffRole.HealthcareAssistant, "Healthcare assistant"), ("Outreach", StaffRole.OutreachNurse, "Band 7 nurse"),
        ("Midwife", StaffRole.Midwife, "Band 6 midwife"), ("Senior midwife", StaffRole.Midwife, "Band 7 midwife"), ("Junior doctor", StaffRole.Doctor, "Junior Doctor"),
        ("Doctor", StaffRole.Doctor, "Doctor"), ("Registrar", StaffRole.Doctor, "Registrar"), ("Consultant", StaffRole.Doctor, "Consultant"),
        ("Surgeon", StaffRole.Surgeon, "Consultant"), ("Anaesthetist", StaffRole.Anaesthetist, "Consultant"), ("Endoscopist", StaffRole.Endoscopist, "Endoscopist"),
        ("Radiographer", StaffRole.Radiographer, "Radiographer"), ("Sonographer", StaffRole.Sonographer, "Sonographer"), ("Cardiac phys.", StaffRole.CardiacPhysiologist, "Cardiac physiologist"),
        ("Neurophys.", StaffRole.Neurophysiologist, "Neurophysiologist"), ("Pharmacist", StaffRole.Pharmacist, "Pharmacist"), ("Physio", StaffRole.Physiotherapist, "Physiotherapist"),
        ("Plaster tech", StaffRole.PlasterTechnician, "Plaster technician"), ("Porter", StaffRole.Porter, "Porter"), ("Reception", StaffRole.Receptionist, "Receptionist"),
    };

    public override void _Process(double delta)
    {
        _frame++;
        if (_hospital is null)
        {
            if (GetTree().CurrentScene is HospitalScene h && h.IsNodeReady())
            {
                _hospital = h;
                h.Hud.Visible = false;
                if (h.GetNodeOrNull<CanvasItem>("TileCursor") is { } cursor)
                {
                    cursor.Visible = false;
                }

                h.Session.Clock.SetSpeed(SimulationSpeed.Paused);
                h.Set3D(true);
                Build(h);
                _frame = 0;
            }

            return;
        }

        if (_frame % 6 != 0)
        {
            return;
        }

        var views = new (string Name, int Rotation, Pose Pose)[]
        {
            ("standing", 0, Pose.Standing), ("walking", 0, Pose.Walking), ("seated", 0, Pose.Seated), ("behind", 2, Pose.Standing), ("side", 1, Pose.Walking),
        };
        var index = _shot / 2;
        if (index >= views.Length)
        {
            GD.Print("[people3d] Done.");
            GetTree().Quit(0);
            return;
        }

        var view = views[index];
        if (_shot % 2 == 0)
        {
            _hospital.RotateView(view.Rotation - IsoDraw.Rotation);
            foreach (var person in _line!.GetChildren().OfType<Person3D>())
            {
                person.SetPose(view.Pose, 1.2);
            }

            _hospital.Camera.SnapTo(IsoDraw.ToScreen(Centre.X + 1.1, Centre.Y + 4.7), 1.45f);
        }
        else
        {
            var image = GetViewport().GetTexture().GetImage();
            DirAccess.MakeDirRecursiveAbsolute(_directory);
            var path = $"{_directory}/people_{view.Name}.png";
            image.SavePng(path);
            GD.Print($"[people3d] Saved {path}");
        }

        _shot++;
    }

    private static readonly (double X, double Y) Centre = (16, 9);

    private void Build(HospitalScene h)
    {
        _line = new Node3D { Name = "Lineup" };
        h.View3D.Root.AddChild(_line);
        var looks = Staff.Select((s, i) => Looks.Staff(new StaffMember { Id = 3 + i, Name = s.Label, Role = s.Role, Grade = s.Grade, DepartmentId = "ed", Age = 24 + ((i * 7) % 38) })).ToList();
        var ages = new[] { (3, Sex.Male), (6, Sex.Female), (9, Sex.Male), (14, Sex.Female), (16, Sex.Male), (27, Sex.Female), (34, Sex.Male), (45, Sex.Female), (52, Sex.Male), (66, Sex.Female), (74, Sex.Male), (81, Sex.Female), (88, Sex.Male) };
        looks.AddRange(ages.Select((a, i) => Looks.Preview(40 + (i * 13), a.Item2, a.Item1)));
        looks.Add(Looks.Preview(500, Sex.Female, 31) with { Bump = true, Style = HairStyle.Ponytail });
        looks.Add(Looks.Preview(501, Sex.Male, 79) with { Stick = true });
        looks.Add(Looks.Preview(502, Sex.Female, 84) with { Frame = true });

        for (var i = 0; i < looks.Count; i++)
        {
            var person = Person3D.Build(looks[i]);
            var row = i / 10;
            var col = i % 10;

            // Rows run along the screen: each step is a tile right and a tile up the grid.
            person.Position = Kit3D.At(Centre.X - 9 + (col * 1.6) + (row * 2.2), Centre.Y + 9 - (col * 1.6) + (row * 2.2));
            person.Rotation = new Vector3(0, Mathf.Pi / 4, 0);
            if (i == 2)
            {
                person.Hold(Looks.Baby(new Simulation.Patients.Baby { Id = 1, MotherId = 1, FirstName = "Baby", Surname = "Review", Sex = Sex.Female, WeightKg = 3.4, GestationWeeks = 40, BornAt = DateTime.Now, Delivery = "Normal" }, null));
            }

            if (i == 1)
            {
                person.CarryBag(true);
            }

            _line.AddChild(person);
        }
    }
}
