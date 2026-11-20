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
                GameManager.Instance.Settings.View3D = true;
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
        ("Surgeon", StaffRole.Surgeon, "Consultant"), ("Anaesthetist", StaffRole.Anaesthetist, "Consultant"), ("Scrub nurse", StaffRole.ScrubNurse, "Band 5 scrub nurse"), ("Theatre sister", StaffRole.ScrubNurse, "Band 7 theatre sister"), ("Endoscopist", StaffRole.Endoscopist, "Endoscopist"),
        ("Radiographer", StaffRole.Radiographer, "Radiographer"), ("Sonographer", StaffRole.Sonographer, "Sonographer"), ("Cardiac phys.", StaffRole.CardiacPhysiologist, "Cardiac physiologist"),
        ("Neurophys.", StaffRole.Neurophysiologist, "Neurophysiologist"), ("Pharmacist", StaffRole.Pharmacist, "Pharmacist"), ("Physio", StaffRole.Physiotherapist, "Physiotherapist"),
        ("Plaster tech", StaffRole.PlasterTechnician, "Plaster technician"), ("Porter", StaffRole.Porter, "Porter"), ("Reception", StaffRole.Receptionist, "Receptionist"), ("Cleaner", StaffRole.Cleaner, "Cleaner"),
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
                Build(h);
                _frame = 0;
            }

            return;
        }

        if (_frame % 6 != 0)
        {
            return;
        }

        // The whole line-up from five sides, then (with --people3d-close) each row close up, five people a shot.
        var views = new System.Collections.Generic.List<(string Name, int Rotation, Pose Pose, double X, double Y, float Zoom)>
        {
            ("standing", 0, Pose.Standing, Centre.X + 3.5, Centre.Y + 8.5, 1.2f), ("walking", 0, Pose.Walking, Centre.X + 3.5, Centre.Y + 8.5, 1.2f),
            ("seated", 0, Pose.Seated, Centre.X + 3.5, Centre.Y + 8.5, 1.2f), ("behind", 2, Pose.Standing, Centre.X + 3.5, Centre.Y + 8.5, 1.2f),
            ("side", 1, Pose.Walking, Centre.X + 3.5, Centre.Y + 8.5, 1.2f),
        };
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--people3d-close") >= 0)
        {
            views.Clear();
            for (var row = 0; row < 4; row++)
            {
                for (var half = 0; half < 2; half++)
                {
                    var col = 2 + (half * 5);
                    views.Add(($"close_{row}{half}", 0, Pose.Standing, Centre.X - 9 + (col * 1.6) + (row * 2.2) + 0.6, Centre.Y + 9 - (col * 1.6) + (row * 2.2) + 0.6, 3f));
                }
            }

            views.Add(("close_walk", 0, Pose.Walking, Centre.X - 9 + (2 * 1.6) + 0.6, Centre.Y + 9 - (2 * 1.6) + 0.6, 3f));
            views.Add(("close_back", 2, Pose.Standing, Centre.X - 9 + (2 * 1.6) - 0.6, Centre.Y + 9 - (2 * 1.6) - 0.6, 3f));
            views.Add(("close_seated", 0, Pose.Seated, Centre.X - 9 + (2 * 1.6) + 0.6, Centre.Y + 9 - (2 * 1.6) + 0.6, 3f));
            views.Add(("close_beds", 0, Pose.Standing, Centre.X + 6.5, Centre.Y + 12.5, 2.2f));
            views.Add(("close_beds2", 0, Pose.Standing, Centre.X + 14.2, Centre.Y + 3.8, 2.2f));
            views.Add(("close_beds3", 0, Pose.Standing, Centre.X + 26.5, Centre.Y - 8.5, 2.2f));
            views.Add(("close_beds4", 1, Pose.Standing, Centre.X + 26.5, Centre.Y - 8.5, 2.2f));
        }

        if (System.Environment.GetEnvironmentVariable("BH_SHOTS") == "beds")
        {
            views.Clear();
            views.Add(("beds_a", 0, Pose.Standing, Centre.X + 5.2, Centre.Y + 13.8, 3.4f));
            views.Add(("beds_b", 0, Pose.Standing, Centre.X + 10.4, Centre.Y + 8.6, 3.4f));
            views.Add(("beds_c", 0, Pose.Standing, Centre.X + 26.5, Centre.Y - 8.5, 2.2f));
            views.Add(("beds_d", 1, Pose.Standing, Centre.X + 7.8, Centre.Y + 12.5, 2.8f));
            views.Add(("bay", 0, Pose.Standing, 37.6 + DX, 33.2 + DY, 2.4f));
            views.Add(("bay_close", 0, Pose.Standing, 37.0 + DX, 31.4 + DY, 3.6f));
        }

        var index = _shot / 2;
        if (index >= views.Count)
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

            _hospital.Camera.SnapTo(IsoDraw.ToScreen(view.X, view.Y), view.Zoom);
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

        if (System.Environment.GetEnvironmentVariable("BH_SHOTS") == "beds")
        {
            Bay();
        }

        // A row of beds, tables and trolleys with patients on them.
        var patient = Looks.Preview(77, Sex.Female, 58);
        var child = Looks.Preview(78, Sex.Male, 7);
        var kinds = new[] { "bed", "bed_empty", "trolley", "stretcher", "floor", "xray_table", "ct_table", "operating_table", "cath_lab_table", "treatment_couch", "wheelchair", "cot" };
        for (var i = 0; i < kinds.Length; i++)
        {
            var holder = new Node3D { Position = Kit3D.At(Centre.X - 6 + (i * 2.6) + 9, Centre.Y + 6 - (i * 2.6) + 9) };
            var b = new MeshBatch();
            var who = i == 2 ? child : patient;
            var top = kinds[i] switch
            {
                "bed" => Carriers3D.Bed(b, false, Carriers3D.BedBack.Raised),
                "bed_empty" => Carriers3D.Bed(b, true),
                "trolley" => Carriers3D.Trolley(b, Carriers3D.TrolleyBack.Raised),
                "stretcher" => Carriers3D.Stretcher(b, Carriers3D.StretcherBack.Raised),
                "floor" => 0f,
                "wheelchair" or "cot" => -1f,
                var id => Carriers3D.Table(b, id, new Color("#7FA7C4")),
            };
            if (kinds[i] == "stretcher")
            {
                Carriers3D.Straps(b, top);
            }

            if (kinds[i] == "cot")
            {
                var glass = new MeshBatch();
                Furnish3D.Cot(b, glass, 1.0f, 1.6f, Kit3D.M(0.9));
                glass.AddTo(holder, alpha: true, shadow: false);
            }

            if (kinds[i] == "wheelchair")
            {
                Carriers3D.Wheelchair(b);
                var sitter = Person3D.Build(patient);
                sitter.Seat = Person3D.SeatShape.Wheelchair(sitter.Look.Scale);
                sitter.SetPose(Pose.Seated, 0);
                holder.AddChild(sitter);
            }

            b.AddTo(holder);
            if (top >= 0 && kinds[i] != "bed_empty")
            {
                var rest = kinds[i] switch { "bed" => Carriers3D.BedBack, "trolley" => Carriers3D.TrolleyBack, "stretcher" => Carriers3D.StretcherBack, _ => (Carriers3D.Backrest?)null };
                var lying = Lying3D.Build(who, kinds[i] == "floor" ? who.Top : Looks.EdBlanket, kinds[i] == "bed" ? new Color("#F2A33A") : null, null, rest: rest, angle: rest?.Raised ?? 0);
                lying.Position = new Vector3(0, top, 0);
                holder.AddChild(lying);
            }

            if (i == 0)
            {
                holder.AddChild(Alert3D.Make(AlertKind.Crash, 1.45f, 78));
            }

            if (kinds[i] == "floor")
            {
                holder.AddChild(Alert3D.Make(AlertKind.Emergency, 1.1f, 100));
            }

            holder.Rotation = new Vector3(0, Mathf.Pi / 2, 0);
            _line.AddChild(holder);
        }
    }

    /// <summary>A staged ward bay for the people review: four beds on a hospital floor, a nurse, a doctor and a porter with a trolley.</summary>
    private const float DX = -23, DY = 5;

    private void Bay()
    {
        if (GetTree().Root.FindChild("Outside", true, false) is Node3D outside)
        {
            outside.Visible = false;
        }

        var floor = new MeshBatch();
        floor.Box(new Vector3(37 + DX, 0.01f, 33 + DY), new Vector3(18, 0.02f, 18), new Color("#E3E8EA"));
        floor.AddTo(_line!);
        var who = new[] { Looks.Preview(301, Sex.Male, 79), Looks.Preview(302, Sex.Female, 58), Looks.Preview(303, Sex.Male, 34), Looks.Preview(304, Sex.Female, 86) };
        var bands = new Color?[] { new Color("#F2A33A"), new Color("#43A047"), new Color("#43A047"), new Color("#E53935") };
        for (var i = 0; i < who.Length; i++)
        {
            var holder = new Node3D { Position = Kit3D.At(31 + DX + (i * 2.7), 37 + DY - (i * 2.7)), Rotation = new Vector3(0, Mathf.Pi / 2, 0) };
            var b = new MeshBatch();
            // Sat up by day; the second bed asleep with the back lower, the last one reading.
            var angle = i == 1 ? Mathf.DegToRad(20) : Carriers3D.BedBack.Raised;
            var top = Carriers3D.Bed(b, false, angle);
            b.AddTo(holder);
            var lying = Lying3D.Build(who[i], Looks.WardBlanket, bands[i], null, awake: i != 1, doing: i == 3 ? InBed.Book : i == 2 ? InBed.Phone : InBed.None, rest: Carriers3D.BedBack, angle: angle);
            lying.Position = new Vector3(0, top, 0);
            holder.AddChild(lying);
            _line!.AddChild(holder);
        }

        Person3D Staff(StaffRole role, string grade, int id, double x, double y, float turn)
        {
            var p = Person3D.Build(Looks.Staff(new StaffMember { Id = id, Name = grade, Role = role, Grade = grade, DepartmentId = "ward", Age = 30 }));
            p.Position = Kit3D.At(x, y);
            p.Rotation = new Vector3(0, turn, 0);
            _line!.AddChild(p);
            return p;
        }

        Staff(StaffRole.Nurse, "Band 5 nurse", 91, 33.6 + DX, 36.0 + DY, Mathf.Pi / 4);
        Staff(StaffRole.Doctor, "Registrar", 92, 39.0 + DX, 30.6 + DY, Mathf.Pi / 4);
        var porter = Staff(StaffRole.Porter, "Porter", 93, 37.4 + DX, 39.6 + DY, Mathf.Pi / 2);
        porter.SetPose(Pose.Walking, 0.6);

        var t = new Node3D { Position = Kit3D.At(39.6 + DX, 39.6 + DY), Rotation = new Vector3(0, Mathf.Pi / 2, 0) };
        var tb = new MeshBatch();
        var tt = Carriers3D.Trolley(tb, Carriers3D.TrolleyBack.Raised);
        tb.AddTo(t);
        var rider = Lying3D.Build(Looks.Preview(305, Sex.Female, 27), Looks.EdBlanket, new Color("#F2A33A"), null, awake: true, rest: Carriers3D.TrolleyBack, angle: Carriers3D.TrolleyBack.Raised);
        rider.Position = new Vector3(0, tt, 0);
        t.AddChild(rider);
        _line!.AddChild(t);
    }
}
