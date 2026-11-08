using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Art review sheets of people, beds, patient transport and equipment, run with:
/// godot --path game -- --art-gallery=DIR (needs a display, not --headless). Each sheet is
/// saved from two view angles, then the game quits. Drawn with the same code as the game world.
/// </summary>
public partial class ArtGallery : Node2D
{
    private const float Scale = 1.9f;

    private readonly List<(string Label, Vector2 At, float Scale, Action<CanvasItem> Draw)> _cells = new();
    private string _directory = "";
    private HospitalScene? _hospital;
    private int _frame;
    private int _shot;
    private Patient[] _patients = Array.Empty<Patient>();

    private static readonly string[] Pages = { "people", "beds_and_transport", "equipment" };

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--art-gallery=", StringComparison.Ordinal))
            {
                var layer = new CanvasLayer { Name = "ArtGalleryLayer", Layer = 90, ProcessMode = ProcessModeEnum.Always };
                layer.AddChild(new ArtGallery { Name = "ArtGallery", _directory = arg["--art-gallery=".Length..] });
                GameManager.Instance.AddChild(layer);
                Callable.From(() => GameManager.Instance.StartNewGame("Art Review General")).CallDeferred();
                return true;
            }
        }

        return false;
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_hospital is null)
        {
            if (GetTree().CurrentScene is HospitalScene h && h.IsNodeReady())
            {
                _hospital = h;
                h.Hud.Visible = false;
                for (var i = 0; i < 10; i++)
                {
                    h.Session.Simulation.SpawnPatient(i % 2 == 0 ? ArrivalMethod.WalkIn : ArrivalMethod.Ambulance);
                }

                h.Session.Clock.SetSpeed(SimulationSpeed.Paused);
                for (var i = 0; i < 40; i++)
                {
                    h.Session.Clock.Step();
                }

                _patients = h.Session.Simulation.Patients.OrderBy(p => p.Id).ToArray();
                _frame = 0;
            }

            return;
        }

        const int settle = 5;
        if (_frame % settle != 0)
        {
            return;
        }

        var page = _shot / 4;
        var rotation = (_shot / 2) % 2;
        if (page >= Pages.Length)
        {
            GD.Print("[gallery] Done.");
            GetTree().Quit(0);
            return;
        }

        if (_shot % 2 == 0)
        {
            if (IsoDraw.Rotation != rotation)
            {
                _hospital.RotateView(rotation - IsoDraw.Rotation);
            }

            Layout(Pages[page]);
            QueueRedraw();
        }
        else
        {
            var image = GetViewport().GetTexture().GetImage();
            DirAccess.MakeDirRecursiveAbsolute(_directory);
            var path = $"{_directory}/{Pages[page]}_{rotation}.png";
            image.SavePng(path);
            GD.Print($"[gallery] Saved {path}");
        }

        _shot++;
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color("#D9DEE2"));
        var font = ThemeDB.FallbackFont;
        foreach (var (label, at, scale, draw) in _cells)
        {
            // Each cell is drawn around grid point (0, 0), scaled up, with the label below.
            var origin = at - (IsoDraw.ToScreen(0, 0) * scale);
            DrawSetTransform(origin, 0, new Vector2(scale, scale));
            draw(this);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            var width = font.GetStringSize(label, HorizontalAlignment.Left, -1, 13).X;
            DrawString(font, at + new Vector2(-width / 2, 22 * scale), label, HorizontalAlignment.Left, -1, 13, new Color("#1D2B36"));
        }
    }

    private void Layout(string page)
    {
        _cells.Clear();
        var sim = _hospital!.Session.Simulation;
        const double time = 1.3;
        var foot = IsoDraw.ToScreen(0, 0);
        PersonLook P(int i) => Looks.Patient(_patients[i % _patients.Length]);

        var middle = GetViewportRect().Size.X / 2;
        void Row(float y, float spacing, IEnumerable<(string, Action<CanvasItem>)> items, float scale = Scale)
        {
            var list = items.ToList();
            var x = middle - (spacing * (list.Count - 1) / 2);
            foreach (var (label, draw) in list)
            {
                _cells.Add((label, new Vector2(x, y), scale, draw));
                x += spacing;
            }
        }

        switch (page)
        {
            case "people":
            {
                var staff = new (string Label, StaffRole Role, string Grade)[]
                {
                    ("Band 5 nurse", StaffRole.Nurse, "Band 5 nurse"),
                    ("Band 7 sister", StaffRole.Nurse, "Band 7 nurse"),
                    ("Outreach", StaffRole.OutreachNurse, "Band 7 nurse"),
                    ("HCA", StaffRole.HealthcareAssistant, "Healthcare assistant"),
                    ("Doctor", StaffRole.Doctor, "Doctor"),
                    ("Consultant", StaffRole.Doctor, "Consultant"),
                    ("Surgeon", StaffRole.Surgeon, "Consultant"),
                    ("Anaesthetist", StaffRole.Anaesthetist, "Consultant"),
                    ("Porter", StaffRole.Porter, "Porter"),
                    ("Reception", StaffRole.Receptionist, "Receptionist"),
                };
                var staff2 = new (string Label, StaffRole Role, string Grade)[]
                {
                    ("Radiographer", StaffRole.Radiographer, "Radiographer"),
                    ("Pharmacist", StaffRole.Pharmacist, "Pharmacist"),
                    ("Physio", StaffRole.Physiotherapist, "Physiotherapist"),
                    ("Cardiac phys.", StaffRole.CardiacPhysiologist, "Cardiac physiologist"),
                    ("Neurophys.", StaffRole.Neurophysiologist, "Neurophysiologist"),
                    ("Sonographer", StaffRole.Sonographer, "Sonographer"),
                    ("Endoscopist", StaffRole.Endoscopist, "Endoscopist"),
                    ("Plaster tech", StaffRole.PlasterTechnician, "Plaster technician"),
                    ("Nurse (walk)", StaffRole.Nurse, "Band 6 nurse"),
                    ("Doctor (sits)", StaffRole.Doctor, "Registrar"),
                };

                Action<CanvasItem> Person((string Label, StaffRole Role, string Grade) s, int id)
                {
                    var member = new StaffMember { Id = id, Name = s.Label, Role = s.Role, Grade = s.Grade, DepartmentId = "ed" };
                    var look = Looks.Staff(member);
                    return s.Label.Contains("sits")
                        ? c => Figures.Seated(c, foot, look)
                        : c => Figures.Standing(c, foot, look, time, s.Label.Contains("walk"), id * 7);
                }

                Row(165, 150, staff.Select((s, i) => (s.Label, Person(s, i + 1))));
                Row(420, 150, staff2.Select((s, i) => (s.Label, Person(s, i + 20))));
                var nurse = Looks.Staff(new StaffMember { Id = 7, Name = "Nurse", Role = StaffRole.Nurse, Grade = "Band 5 nurse", DepartmentId = "ed" });
                string Who(int i) => $"Patient {_patients[i % _patients.Length].Age}{(_patients[i % _patients.Length].Sex == Sex.Female ? "F" : "M")}";
                Row(680, 150, new (string, Action<CanvasItem>)[]
                {
                    ("Patient walking", c => Figures.Standing(c, foot, P(0), time, true, 0)),
                    ("Walking away", c => Figures.Standing(c, foot, P(1), time, true, 13, away: true)),
                    (Who(2), c => Figures.Standing(c, foot, P(2), time, false, 26)),
                    (Who(3), c => Figures.Standing(c, foot, P(3), time, false, 39)),
                    (Who(4), c => Figures.Standing(c, foot, P(4), time, false, 52)),
                    (Who(6), c => Figures.Standing(c, foot, P(6), time, false, 78)),
                    ("Patient seated", c => Figures.Seated(c, foot, P(5))),
                    ("Nurse, away", c => Figures.Standing(c, foot, nurse, time, true, 49, away: true)),
                    ("Porter, away", c => Figures.Standing(c, foot, Looks.Staff(new StaffMember { Id = 9, Name = "P", Role = StaffRole.Porter, Grade = "Porter", DepartmentId = "ed" }), time, true, 63, away: true)),
                    ("Surgeon, away", c => Figures.Standing(c, foot, Looks.Staff(new StaffMember { Id = 11, Name = "S", Role = StaffRole.Surgeon, Grade = "Consultant", DepartmentId = "ed" }), time, true, 77, away: true)),
                });
                break;
            }

            case "beds_and_transport":
            {
                var porter = Looks.Staff(new StaffMember { Id = 4, Name = "Porter", Role = StaffRole.Porter, Grade = "Porter", DepartmentId = "ed" });
                var obs = _patients[0].CurrentObservations() ?? _patients[0].BaselineObservations;
                Row(340, 500, new (string, Action<CanvasItem>)[]
                {
                    ("Empty bed", c => Figures.Bed(c, 0, 0, made: true)),
                    ("Amber + monitor", c =>
                    {
                        Figures.MonitorStand(c, -1.6, -2.4);
                        Figures.Bed(c, 0, 0, false, false, lift => Figures.Lying(c, 0, 0, P(2), Looks.Amber, lift));
                        Figures.ObsScreen(c, -1.6, -2.4, time, obs, Looks.Amber, 3);
                    }),
                    ("Ward bed", c => Figures.Bed(c, 0, 0, false, true, lift => Figures.Lying(c, 0, 0, P(4), Looks.WardBlanket, lift, true))),
                }, 2f);
                Row(700, 500, new (string, Action<CanvasItem>)[]
                {
                    ("Trolley + porter", c =>
                    {
                        var dir = (1.0, 0.0);
                        var head = IsoDraw.ToScreen(-2.6, 0);
                        var headBehind = head.Y <= foot.Y;
                        if (headBehind) Figures.Standing(c, head, porter, time, true, 5);
                        var lift = Figures.Trolley(c, 0, 0, dir);
                        Figures.LyingAlong(c, 0, 0, dir, P(6), Looks.Red, lift);
                        if (!headBehind) Figures.Standing(c, head, porter, time, true, 5);
                    }),
                    ("Ambulance stretcher", c => AmbulanceStretcher(c, P(7))),
                    ("Wheelchair + porter", c =>
                    {
                        var behind = IsoDraw.ToScreen(-0.9, -0.9);
                        var front = IsoDraw.ToScreen(0, 0);
                        var back = behind.Y < front.Y;
                        if (back) Figures.Standing(c, behind, porter, time, true, 3);
                        Figures.WheelchairBack(c, front);
                        Figures.Seated(c, front, P(5));
                        Figures.WheelchairFront(c, front);
                        if (!back) Figures.Standing(c, behind, porter, time, true, 3);
                    }),
                }, 2f);
                break;
            }

            case "equipment":
            {
                var obs = _patients[0].CurrentObservations() ?? _patients[0].BaselineObservations;
                Action<CanvasItem> Item(string id) => c =>
                {
                    if (!sim.Database.Furniture.TryGetValue(id, out var def))
                    {
                        return;
                    }

                    var (w, l) = (def.WidthM / 0.5, def.DepthM / 0.5);
                    var (x, y) = (-w / 2, -l / 2);
                    var height = (float)(def.HeightM * IsoDraw.PixelsPerMetre);

                    // Sliced like the game world does it, drawn back to front.
                    var alongX = w >= l;
                    var length = alongX ? w : l;
                    var count = Math.Max(1, (int)Math.Ceiling(length));
                    var step = length / count;
                    var slices = Enumerable.Range(0, count).Select(i =>
                    {
                        var (sx, sy, sw, sl) = alongX ? (x + (i * step), y, step, l) : (x, y + (i * step), w, step);
                        var joined = Sides.None;
                        if (i > 0) joined |= alongX ? Sides.West : Sides.North;
                        if (i < count - 1) joined |= alongX ? Sides.East : Sides.South;
                        return (sx, sy, sw, sl, joined);
                    }).OrderBy(s => IsoDraw.ToScreen(s.sx + (s.sw / 2), s.sy + (s.sl / 2)).Y);
                    foreach (var (sx, sy, sw, sl, joined) in slices)
                    {
                        FurnitureArt.Slice(c, id, sx, sy, sw, sl, (x, y, w, l), height, new Color(def.Colour), joined);
                    }
                };

                Row(185, 150, new (string, Action<CanvasItem>)[]
                {
                    ("Obs monitor", c => { Figures.MonitorStand(c, 0, 0); Figures.ObsScreen(c, 0, 0, time, obs, Looks.Red, 7); }),
                    ("Ventilator", Item("ventilator")),
                    ("Drip stand + pump", Item("drip_stand")),
                    ("Oxygen", Item("oxygen_cylinder")),
                    ("Resus trolley", Item("resus_trolley")),
                    ("Anaesthetic", Item("anaesthetic_machine")),
                    ("Instruments", Item("instrument_trolley")),
                    ("Ultrasound", Item("ultrasound_machine")),
                    ("EEG", Item("eeg_machine")),
                    ("Endoscopy stack", Item("endoscopy_stack")),
                }, 1.6f);
                Row(480, 205, new (string, Action<CanvasItem>)[]
                {
                    ("C-arm", Item("c_arm")),
                    ("X-ray tube", Item("xray_tube")),
                    ("CT gantry", Item("ct_gantry")),
                    ("Lead screen", Item("lead_screen")),
                    ("Plaster trolley", Item("plaster_trolley")),
                    ("Hand basin", Item("hand_basin")),
                    ("Cupboard", Item("clinical_cupboard")),
                }, 1.25f);
                Row(760, 240, new (string, Action<CanvasItem>)[]
                {
                    ("Exam couch", Item("examination_couch")),
                    ("Parallel bars", Item("parallel_bars")),
                    ("Exercise mat", Item("exercise_mat")),
                    ("Practice stairs", Item("practice_stairs")),
                    ("Drug fridge", Item("drug_fridge")),
                    ("Locker", Item("bedside_locker")),
                }, 0.95f);
                break;
            }
        }
    }

    /// <summary>The ambulance crew's stretcher, as drawn while the patient waits in the ambulance queue.</summary>
    private static void AmbulanceStretcher(CanvasItem c, PersonLook look) => Figures.AmbulanceStretcher(c, 0, 0, (0, 1), look, Looks.Red);
}
