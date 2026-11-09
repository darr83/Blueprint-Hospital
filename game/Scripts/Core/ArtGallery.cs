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

    private static readonly string[] Pages = { "people", "beds_and_transport", "equipment", "maternity" };

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

        Action<CanvasItem> Furniture(string id) => c =>
        {
            if (!sim.Database.Furniture.TryGetValue(id, out var def))
            {
                return;
            }

            var (w, l) = (def.WidthM / 0.5, def.DepthM / 0.5);
            FurnitureArt.Slice(c, id, -w / 2, -l / 2, w, l, (-w / 2, -l / 2, w, l), (float)(def.HeightM * IsoDraw.PixelsPerMetre), new Color(def.Colour), Sides.None);
        };

        switch (page)
        {
            case "maternity":
                MaternityPage((y, spacing, items, scale) => Row(y, spacing, items, scale), Furniture, foot, time);
                break;

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
                        Figures.Bed(c, 0, 0, false, false, lift => Figures.Lying(c, 0, 0, P(2), Looks.EdBlanket, lift, false, Looks.Amber));
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
                        Figures.LyingAlong(c, 0, 0, dir, P(6), Looks.EdBlanket, lift, Looks.Red);
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

    /// <summary>The maternity sheet: midwives, mums, babies, cots, incubators and the delivery room kit.</summary>
    private void MaternityPage(Action<float, float, IEnumerable<(string, Action<CanvasItem>)>, float> row, Func<string, Action<CanvasItem>> item, Vector2 foot, double time)
    {
        var mum = _patients.FirstOrDefault(p => p.Sex == Sex.Female) ?? _patients[0];
        var other = _patients.Where(p => p.Sex == Sex.Female).Skip(1).FirstOrDefault() ?? _patients[^1];
        var young = new Color("#4A3020");
        var pregnant = Looks.Patient(mum) with { Bump = true, Jacket = null, Hair = young, Glasses = false, Stick = false, Beard = false, Style = HairStyle.Ponytail, LongHair = true };
        var pregnant2 = Looks.Patient(other) with { Bump = true, Jacket = null, Hair = new Color("#2B1E16"), Stick = false, Beard = false, Style = HairStyle.Curly };
        var delivered = pregnant with { Bump = false };
        BabyLook Baby(int id) => Looks.Baby(new Baby { Id = id, FirstName = "Baby", Surname = "Gallery", Sex = id % 2 == 0 ? Sex.Female : Sex.Male, WeightKg = 3.4, GestationWeeks = 40, BornAt = DateTime.Today, MotherId = mum.Id, Delivery = "Normal vaginal birth" }, mum);
        PersonLook Staff(int id, StaffRole role, string grade) => Looks.Staff(new StaffMember { Id = id, Name = grade, Role = role, Grade = grade, DepartmentId = "maternity" });
        var midwife = Staff(3, StaffRole.Midwife, "Band 6 midwife");
        var senior = Staff(8, StaffRole.Midwife, "Band 7 midwife");
        var nurse = Staff(7, StaffRole.Nurse, "Band 5 nurse");

        row(175, 150, new (string, Action<CanvasItem>)[]
        {
            ("Midwife", c => Figures.Standing(c, foot, midwife, time, false, 21)),
            ("Senior midwife", c => Figures.Standing(c, foot, senior, time, false, 56)),
            ("Midwife + baby", c => Figures.Standing(c, foot, Staff(10, StaffRole.Midwife, "Band 5 midwife"), time, true, 70, holding: Baby(1))),
            ("Nurse + baby, away", c => Figures.Standing(c, foot, nurse, time, true, 49, away: true, holding: Baby(2))),
            ("In labour", c => Figures.Standing(c, foot, pregnant, time, true, 13)),
            ("Antenatal visit", c => Figures.Standing(c, foot, pregnant2 with { Jacket = new Color("#5B4636") }, time, false, 26)),
            ("Mum + baby", c => Figures.Standing(c, foot, delivered, time, true, 39, holding: Baby(3))),
            ("Mum seated", c => Figures.Seated(c, foot, delivered, holding: Baby(4))),
        }, 1.7f);

        void BedAndCot(CanvasItem c, BabyLook[] inCot)
        {
            var cot = (X: 1.1, Y: -2.0, W: 1.2, L: 1.6);
            var cotFirst = IsoDraw.ToScreen(cot.X + 0.6, cot.Y + 0.8).Y < IsoDraw.ToScreen(0, 0).Y;
            if (cotFirst) MaternityArt.Cot(c, cot, 25.2f, inCot);
            Figures.Bed(c, 0, 0, false, false, lift => Figures.Lying(c, 0, 0, delivered, Looks.MaternityBlanket, lift, false));
            if (!cotFirst) MaternityArt.Cot(c, cot, 25.2f, inCot);
        }

        row(470, 380, new (string, Action<CanvasItem>)[]
        {
            ("Labour, CTG on", c =>
            {
                Figures.Bed(c, 0, 0, false, false, lift => Figures.Lying(c, 0, 0, pregnant, Looks.MaternityBlanket, lift, false));
            }),
            ("Skin to skin", c => Figures.Bed(c, 0, 0, false, false, lift => Figures.Lying(c, 0, 0, delivered with { Style = HairStyle.Long }, Looks.MaternityBlanket, lift, false, onChest: new[] { Baby(5) }))),
            ("Bed + cot", c => BedAndCot(c, new[] { Baby(6) })),
            ("Twins", c => BedAndCot(c, new[] { Baby(7), Baby(8) })),
        }, 1.9f);

        row(790, 190, new (string, Action<CanvasItem>)[]
        {
            ("Incubator", c => MaternityArt.Incubator(c, (-0.7, -1.2, 1.4, 2.4), 36.4f, new Color("#5C8FB8"), Baby(9), false, time)),
            ("Empty incubator", c => MaternityArt.Incubator(c, (-0.7, -1.2, 1.4, 2.4), 36.4f, new Color("#5C8FB8"), null, true, time)),
            ("Empty cot", c => MaternityArt.Cot(c, (-0.6, -0.8, 1.2, 1.6), 25.2f, Array.Empty<BabyLook>())),
            ("CTG monitor", item("ctg_monitor")),
            ("Resuscitaire", item("resuscitaire")),
            ("Birthing ball", item("birthing_ball")),
            ("Changing unit", item("baby_changing_unit")),
            ("Breast pump", item("breast_pump")),
        }, 1.5f);
    }

    /// <summary>The ambulance crew's stretcher, as drawn while the patient waits in the ambulance queue.</summary>
    private static void AmbulanceStretcher(CanvasItem c, PersonLook look) => Figures.AmbulanceStretcher(c, 0, 0, (0, 1), look, new Color("#B5413B"), Looks.Red);
}
