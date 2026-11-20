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
/// Close-up review shots of people sitting, run with: godot --path game -- --seat-shots=DIR (needs a display).
/// Puts someone on every kind of chair (waiting, visitor, desk, staff room, café) and photographs them from
/// all four sides.
/// </summary>
public partial class SeatShots : Node
{
    private string _directory = "";
    private int _frame;
    private int _shot;
    private HospitalScene? _hospital;

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--seat-shots=", StringComparison.Ordinal))
            {
                var shots = new SeatShots { Name = "SeatShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--seat-shots=".Length..] };
                GameManager.Instance.AddChild(shots);
                Callable.From(() => GameManager.Instance.StartNewGame("Seat Review General")).CallDeferred();
                return true;
            }
        }

        return false;
    }

    private static readonly (double X, double Y) Centre = (16, 9);

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
                Build(h);
                _frame = 0;
            }

            return;
        }

        if (_frame % 6 != 0)
        {
            return;
        }

        var view = _shot / 2;
        if (view >= 4)
        {
            GD.Print("[seats] Done.");
            GetTree().Quit(0);
            return;
        }

        if (_shot % 2 == 0)
        {
            _hospital.RotateView(view - IsoDraw.Rotation);
            _hospital.Camera.SnapTo(IsoDraw.ToScreen(Centre.X, Centre.Y), 3.2f);
        }
        else
        {
            var image = GetViewport().GetTexture().GetImage();
            DirAccess.MakeDirRecursiveAbsolute(_directory);
            var path = $"{_directory}/seats_view{view}.png";
            image.SavePng(path);
            GD.Print($"[seats] Saved {path}");
        }

        _shot++;
    }

    private static void Build(HospitalScene h)
    {
        var root = new Node3D { Name = "SeatReview" };
        h.View3D.Root.AddChild(root);
        var chairs = new (string Id, Color Colour)[]
        {
            ("waiting_seat", new Color("#7A2430")), ("waiting_seat", new Color("#2E7D9A")), ("visitor_chair", new Color("#5B7FA6")),
            ("staff_chair", new Color("#3E4A57")), ("lounge_chair", new Color("#6E8B5E")), ("cafe_chair", new Color("#B5651D")),
        };
        var people = new[]
        {
            Looks.Preview(301, Sex.Male, 34), Looks.Preview(302, Sex.Female, 71), Looks.Preview(303, Sex.Male, 8),
            Looks.Staff(new StaffMember { Id = 7, Name = "Pharmacist", Role = StaffRole.Pharmacist, Grade = "Pharmacist", DepartmentId = "pharmacy", Age = 40 }),
            Looks.Staff(new StaffMember { Id = 8, Name = "Nurse", Role = StaffRole.Nurse, Grade = "Band 5 nurse", DepartmentId = "ed", Age = 28 }),
            Looks.Preview(304, Sex.Female, 45),
        };

        for (var i = 0; i < chairs.Length; i++)
        {
            // A row along the screen, the empty twin of each chair beside it to compare.
            foreach (var occupied in new[] { true, false })
            {
                var x = Centre.X - 3.6 + (i * 1.3) + (occupied ? 0 : 0.0);
                var y = Centre.Y + 3.6 - (i * 1.3) + (occupied ? 0 : 1.3);
                var holder = new Node3D { Position = Kit3D.At(x, y), Rotation = new Vector3(0, 0, 0) };
                var b = new MeshBatch();
                Furnish3D.Chair(b, chairs[i].Id, chairs[i].Colour);
                b.AddTo(holder);
                if (occupied)
                {
                    var person = Person3D.Build(people[i]);
                    person.SetPose(Pose.Seated, 0);
                    person.SetAct(i == 0 ? Act.Phone : Act.None, Pose.Seated, 0.5);
                    holder.AddChild(person);
                }

                root.AddChild(holder);
            }
        }
    }
}
