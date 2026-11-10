using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.World;
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

    private static string[] Pages = { "people", "people2", "beds_and_transport", "equipment", "maternity", "chairs", "cafe", "signs", "wallsigns", "furniture", "tables" };

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--art-gallery=", StringComparison.Ordinal))
            {
                var layer = new CanvasLayer { Name = "ArtGalleryLayer", Layer = 90, ProcessMode = ProcessModeEnum.Always };
                layer.AddChild(new ArtGallery { Name = "ArtGallery", _directory = arg["--art-gallery=".Length..] });
                if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--art-pages=", StringComparison.Ordinal)) is { } only)
                {
                    Pages = only["--art-pages=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries);
                }

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
            Flat.Override = new Transform2D(0, new Vector2(scale, scale), 0, origin);
            draw(this);
            Flat.Override = null;
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            var width = font.GetStringSize(label, HorizontalAlignment.Left, -1, 13).X;
            DrawString(font, at + new Vector2(-width / 2, 32 * scale), label, HorizontalAlignment.Left, -1, 13, new Color("#1D2B36"));
        }
    }

    /// <summary>
    /// A piece of furniture cut into one-tile slices and drawn back to front, as the game world does.
    /// <paramref name="repeat"/> stands several side by side; <paramref name="alongX"/> turns it.
    /// </summary>
    private Action<CanvasItem> Sliced(string id, Sides front, int repeat = 1, bool alongX = true) => c =>
    {
        var sim = _hospital!.Session.Simulation;
        if (!sim.Database.Furniture.TryGetValue(id, out var def))
        {
            return;
        }

        var (longSide, shortSide) = (Math.Max(def.WidthM, def.DepthM) / 0.5 * repeat, Math.Min(def.WidthM, def.DepthM) / 0.5);
        var (w, l) = alongX ? (longSide, shortSide) : (shortSide, longSide);
        var (x, y) = (-w / 2, -l / 2);
        var height = (float)(def.HeightM * IsoDraw.PixelsPerMetre);
        var count = Math.Max(1, (int)Math.Ceiling(longSide));
        var step = longSide / count;
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
            FurnitureArt.Slice(c, id, sx, sy, sw, sl, (x, y, w, l), height, new Color(def.Colour), joined, front);
        }
    };

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
            var fit = Math.Min(1f, ((middle * 2) - 30) / (spacing * list.Count));
            (spacing, scale) = (spacing * fit, scale * fit);
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
            case "tables":
            {
                (string, Action<CanvasItem>) Table(string label, string id, string colour, int patient = -1) => (label, c =>
                {
                    var lift = TableArt.Draw(c, id, 0, 0, new Color(colour));
                    if (patient >= 0)
                    {
                        Figures.Lying(c, 0, 0, P(patient), new Color("#CFE0EE"), lift, false);
                    }
                });
                Row(290, 460, new[]
                {
                    Table("Treatment couch", "treatment_couch", "#7FA7C4"), Table("Scan couch with a patient", "scan_couch", "#8FB3CC", 2),
                    Table("X-ray table", "xray_table", "#DDE3E8"), Table("CT scanner table", "ct_table", "#E4E9ED"),
                }, 2.0f);
                Row(760, 460, new[]
                {
                    Table("Operating table", "operating_table", "#5E7A8C"), Table("Operating table with a patient", "operating_table", "#5E7A8C", 5),
                    Table("Cath lab table", "cath_lab_table", "#E4E9ED"), Table("X-ray table with a patient", "xray_table", "#DDE3E8", 4),
                }, 2.0f);
                break;
            }

            case "furniture":
            {
                Row(180, 300, new[]
                {
                    ("Office desk", Sliced("office_desk", Sides.South)), ("Office desk (other way)", Sliced("office_desk", Sides.West, 1, false)),
                    ("Triage desk", Sliced("triage_desk", Sides.South)), ("Staff room table", Sliced("staff_table", Sides.South)),
                    ("Clinical cupboard", Sliced("clinical_cupboard", Sides.South)), ("Bedside locker", Sliced("bedside_locker", Sides.South)),
                }, 1.7f);
                Row(450, 440, new[]
                {
                    ("Reception desk", Sliced("reception_desk", Sides.South)), ("Nurses' station", Sliced("station_counter", Sides.East, 3, false)),
                    ("Pharmacy counter", Sliced("pharmacy_counter", Sides.South)), ("Staff kitchenette", Sliced("kitchenette", Sides.East, 1, false)),
                }, 1.7f);
                Row(820, 300, new[]
                {
                    ("Medicine shelves", Sliced("medicine_shelves", Sides.South)), ("Drugs fridge", Sliced("drug_fridge", Sides.South)),
                    ("Wash basin", Sliced("hand_basin", Sides.South)), ("Wash basin (other way)", Sliced("hand_basin", Sides.East, 1, false)),
                    ("Plaster trolley", Sliced("plaster_trolley", Sides.South)), ("Plaster sink", Sliced("plaster_sink", Sides.East, 1, false)),
                }, 1.7f);
                break;
            }

            case "signs":
            {
                // The three door sign styles, each on a doorway seen from both sides of the view.
                var rooms = new (string Name, string Colour, Pictogram Icon)[]
                {
                    ("Resus 1", "#E53935", Pictogram.Pulse), ("Majors 3", "#F9A825", Pictogram.Bed), ("Ward 2", "#1E88E5", Pictogram.Bed),
                    ("X-ray room 1", "#546E7A", Pictogram.Bone), ("Pharmacy 1", "#7CB342", Pictogram.Pill), ("Cafeteria 1", "#5D7080", Pictogram.Cutlery),
                };
                foreach (var (style, row) in new[] { (DoorSignStyle.Stripe, 0), (DoorSignStyle.Fascia, 1), (DoorSignStyle.Slate, 2) })
                {
                    Row(190 + (row * 285), 300, rooms.Select((r, i) => ($"{"ABC"[row]}", (Action<CanvasItem>)(c => Door(c, r.Name, new Color(r.Colour), r.Icon, style, i % 2 == 0)))), 1.6f);
                }

                break;

                static void Door(CanvasItem c, string name, Color colour, Pictogram icon, DoorSignStyle style, bool horizontal)
                {
                    var (wallTop, wallFace) = (new Color("#F4F6F8"), new Color("#D9DFE5"));
                    var (frameTop, frameFace) = (new Color("#C7D2DB"), new Color("#A9B7C3"));
                    void Along(double s, double len, double thick, float from, float to, Color top, Color face)
                    {
                        if (horizontal) IsoDraw.Prism(c, s, -thick / 2, len, thick, to - from, top, face, lift: from);
                        else IsoDraw.Prism(c, -thick / 2, s, thick, len, to - from, top, face, lift: from);
                    }

                    Along(-3, 1.5, 0.3, 0, 70, wallTop, wallFace);
                    Along(1.5, 1.5, 0.3, 0, 70, wallTop, wallFace);
                    Along(-1.5, 0.14, 0.34, 0, 70, frameTop, frameFace);
                    Along(1.36, 0.14, 0.34, 0, 70, frameTop, frameFace);
                    Along(-1.5, 3, 0.34, 64, 70, frameTop, frameFace);
                    var a = horizontal ? IsoDraw.ToScreen(-1.36, 0) : IsoDraw.ToScreen(0, -1.36);
                    var b = horizontal ? IsoDraw.ToScreen(1.36, 0) : IsoDraw.ToScreen(0, 1.36);
                    Vector2 P(float u, float v) => a.Lerp(b, u) + new Vector2(0, -62 * v);
                    c.DrawColoredPolygon(new[] { P(0, 0), P(1, 0), P(1, 1), P(0, 1) }, new Color("#E7D9BF"));
                    c.DrawLine(P(0.5f, 0), P(0.5f, 1), new Color("#B9A27E"), 1f);
                    foreach (var u in new[] { 0.14f, 0.64f })
                    {
                        c.DrawColoredPolygon(new[] { P(u, 0.55f), P(u + 0.22f, 0.55f), P(u + 0.22f, 0.88f), P(u, 0.88f) }, new Color("#A8CBDC"));
                        c.DrawColoredPolygon(new[] { P(u - 0.06f, 0.03f), P(u + 0.28f, 0.03f), P(u + 0.28f, 0.16f), P(u - 0.06f, 0.16f) }, new Color("#B7C1C9"));
                    }

                    RoomSigns.NameBoard(c, name, colour, icon, style, (0, 0), horizontal, horizontal ? (0, 1) : (1, 0), 72, 3);
                }
            }

            case "wallsigns":
            {
                (string, Action<CanvasItem>) Wall(string label, double tiles, float top, float signHeight, bool leftFacing, Action<CanvasItem, float, float> sign) => (label, c =>
                {
                    // A piece of wall with the sign on it, seen on a wall facing left or facing right.
                    var (wallTop, wallFace) = (new Color("#F4F6F8"), new Color("#D9DFE5"));
                    var half = (tiles / 2) + 0.4;
                    if (leftFacing) IsoDraw.Prism(c, -0.15, -half, 0.3, 2 * half, 70, wallTop, wallFace, skirting: new Color("#B4BFC9"));
                    else IsoDraw.Prism(c, -half, -0.15, 2 * half, 0.3, 70, wallTop, wallFace, skirting: new Color("#B4BFC9"));
                    var a = leftFacing ? IsoDraw.ToScreen(-0.15, tiles / 2) : IsoDraw.ToScreen(-tiles / 2, 0.15);
                    var b = leftFacing ? IsoDraw.ToScreen(-0.15, -tiles / 2) : IsoDraw.ToScreen(tiles / 2, 0.15);
                    Flat.On(c, a + new Vector2(0, -top), b - a, () => sign(c, (b - a).Length(), signHeight));
                });

                Row(200, 310, new[]
                {
                    Wall("Directions (reception)", 2.4, 62, 44, false, (c, w, h) => Signs.Directions(c, w, h, "Outpatients", ("Clinics", 0, Pictogram.Clipboard), ("X-ray", 3, Pictogram.Bone), ("Pharmacy", 2, Pictogram.Pill))),
                    Wall("Directions (A&E)", 2.4, 62, 44, true, (c, w, h) => Signs.Directions(c, w, h, "Emergency", ("Triage", 3, Pictogram.Clipboard), ("Toilets", 0, Pictogram.Man), ("Cafe", 2, Pictogram.Cutlery))),
                    Wall("Pharmacy", 2.6, 60, 22, false, (c, w, h) => Signs.Banner(c, w, h, new Color("#1E8E3E"), Pictogram.Cross, "PHARMACY", "Collect prescriptions here")),
                    Wall("Welcome", 3.2, 60, 22, true, (c, w, h) => Signs.Banner(c, w, h, new Color("#005EB8"), Pictogram.Info, "Emergency Department", "Please book in at reception")),
                    Wall("Toilets", 1.6, 62, 30, false, (c, w, h) => Signs.Toilets(c, w, h, Pictogram.Man, Pictogram.Woman)),
                    Wall("Accessible toilet", 1.6, 62, 30, true, (c, w, h) => Signs.Toilets(c, w, h, Pictogram.Wheelchair, Pictogram.Baby)),
                }, 1.75f);
                Row(520, 310, new[]
                {
                    Wall("Fire exit", 1.8, 62, 15, false, (c, w, h) => Signs.FireExit(c, w, h, 0)),
                    Wall("X-ray warning", 2.2, 58, 20, true, (c, w, h) => Signs.Warning(c, w, h, Pictogram.Radiation, "X-RAYS", "Controlled area")),
                    Wall("Staff only", 2.0, 56, 16, false, (c, w, h) => Signs.Prohibition(c, w, h, "STAFF ONLY", false)),
                    Wall("No smoking", 2.0, 56, 16, true, (c, w, h) => Signs.Prohibition(c, w, h, "No smoking", true)),
                    Wall("Cafe menu", 2.0, 64, 46, false, (c, w, h) => Signs.Menu(c, w, h)),
                    Wall("Defibrillator", 0.9, 54, 30, true, (c, w, h) => Signs.Defibrillator(c, w, h)),
                }, 1.75f);
                Row(840, 310, new[]
                {
                    Wall("Visiting times", 2.4, 58, 22, false, (c, w, h) => Signs.Notice(c, w, h, new Color("#1E88E5"), Pictogram.Chair, "Visiting times", "2 pm to 8 pm every day")),
                    Wall("Clean your hands", 2.4, 58, 22, true, (c, w, h) => Signs.Notice(c, w, h, new Color("#00897B"), Pictogram.Drop, "Clean your hands", "Use the gel as you come in")),
                    Wall("Poster: heart", 1.5, 64, 44, false, (c, w, h) => Signs.Poster(c, w, h, new Color("#AD1457"), "HEART", r => Signs.Draw(c, Pictogram.Heart, r.GetCenter().X, r.GetCenter().Y, Math.Min(r.Size.X, r.Size.Y) * 0.9f, new Color("#D32F2F"), Colors.White))),
                    Wall("Poster: bones", 1.5, 64, 44, true, (c, w, h) => Signs.Poster(c, w, h, new Color("#8D6E63"), "BONES", r => Signs.Draw(c, Pictogram.Bone, r.GetCenter().X, r.GetCenter().Y, Math.Min(r.Size.X, r.Size.Y) * 0.9f, new Color("#B9A98C"), Colors.White))),
                    Wall("Poster: brain", 1.5, 64, 44, false, (c, w, h) => Signs.Poster(c, w, h, new Color("#6A1B9A"), "BRAIN", r => Signs.Draw(c, Pictogram.Brain, r.GetCenter().X, r.GetCenter().Y, Math.Min(r.Size.X, r.Size.Y) * 0.9f, new Color("#F4A6B7"), Colors.White))),
                    Wall("Radiation (CT)", 2.2, 58, 20, true, (c, w, h) => Signs.Warning(c, w, h, Pictogram.Radiation, "CT SCANNER", "Do not enter on red")),
                }, 1.75f);
                break;
            }

            case "cafe":
            {
                (string, Action<CanvasItem>) Machine(string label, Vending kind, (int, int) front) =>
                    (label, c => CafeArt.VendingMachine(c, -0.9, -0.8, 1.8, 1.6, 51.8f, front, kind, 3));
                Row(215, 250, new[]
                {
                    Machine("Snacks", Vending.Snacks, (0, 1)), Machine("Cold drinks", Vending.Drinks, (0, 1)), Machine("Hot drinks", Vending.HotDrinks, (0, 1)),
                    Machine("Snacks (other way)", Vending.Snacks, (1, 0)), Machine("Cold drinks", Vending.Drinks, (1, 0)), Machine("Hot drinks", Vending.HotDrinks, (1, 0)),
                    ("Water machine", c => CafeArt.WaterCooler(c, -0.4, -0.4, 0.8, 0.8, 36.4f, (0, 1))),
                }, 2.0f);
                Row(500, 270, new[]
                {
                    Machine("From behind", Vending.Snacks, (0, -1)), Machine("From behind", Vending.Drinks, (-1, 0)), Machine("From behind", Vending.HotDrinks, (0, -1)),
                    ("Cafe table", (Action<CanvasItem>)(c => CafeArt.Table(c, -0.8, -0.8, 1.6, 1.6, 21f, new Color("#B07A4A"), 3))),
                    ("Cafe table", c => CafeArt.Table(c, -0.8, -0.8, 1.6, 1.6, 21f, new Color("#B07A4A"), 1)),
                }, 2.0f);
                Row(830, 800, new (string, Action<CanvasItem>)[]
                {
                    ("Serving counter", c => Counter(c, (0, 1))),
                    ("Serving counter (other way)", c => Counter(c, (1, 0))),
                }, 2.0f);
                break;

                static void Counter(CanvasItem c, (int X, int Y) front)
                {
                    var alongX = front.X == 0;
                    var slices = Enumerable.Range(0, 5).Select(i =>
                    {
                        var (sx, sy, sw, sl) = alongX ? (-2.5 + i, -0.8, 1.0, 1.6) : (-0.8, -2.5 + i, 1.6, 1.0);
                        var joined = Sides.None;
                        if (i > 0) joined |= alongX ? Sides.West : Sides.North;
                        if (i < 4) joined |= alongX ? Sides.East : Sides.South;
                        return (i, sx, sy, sw, sl, joined);
                    }).OrderBy(s => IsoDraw.ToScreen(s.sx + (s.sw / 2), s.sy + (s.sl / 2)).Y);
                    foreach (var (i, sx, sy, sw, sl, joined) in slices)
                    {
                        CafeArt.CounterSlice(c, sx, sy, sw, sl, 28f, new Color("#C98B4E"), i, 5, 2, front, joined);
                    }
                }
            }

            case "chairs":
                ChairsPage((y, spacing, items, scale) => Row(y, spacing, items, scale), P);
                break;

            case "maternity":
                MaternityPage((y, spacing, items, scale) => Row(y, spacing, items, scale), Furniture, foot, time);
                break;

            case "people":
            case "people2":
            {
                var staff = new (string Label, StaffRole Role, string Grade)[]
                {
                    ("Nurse B5", StaffRole.Nurse, "Band 5 nurse"),
                    ("Nurse B6", StaffRole.Nurse, "Band 6 nurse"),
                    ("Sister B7", StaffRole.Nurse, "Band 7 nurse"),
                    ("Matron B8-9", StaffRole.Nurse, "Band 8 nurse"),
                    ("HCA", StaffRole.HealthcareAssistant, "Healthcare assistant"),
                    ("Outreach", StaffRole.OutreachNurse, "Band 7 nurse"),
                    ("Midwife", StaffRole.Midwife, "Band 6 midwife"),
                    ("Senior midwife", StaffRole.Midwife, "Band 7 midwife"),
                    ("Junior doctor", StaffRole.Doctor, "Junior Doctor"),
                    ("Doctor", StaffRole.Doctor, "Doctor"),
                    ("Registrar", StaffRole.Doctor, "Registrar"),
                    ("Consultant", StaffRole.Doctor, "Consultant"),
                    ("Surgeon", StaffRole.Surgeon, "Consultant"),
                    ("Anaesthetist", StaffRole.Anaesthetist, "Consultant"),
                    ("Endoscopist", StaffRole.Endoscopist, "Endoscopist"),
                    ("Radiographer", StaffRole.Radiographer, "Radiographer"),
                    ("Sonographer", StaffRole.Sonographer, "Sonographer"),
                    ("Cardiac phys.", StaffRole.CardiacPhysiologist, "Cardiac physiologist"),
                    ("Neurophys.", StaffRole.Neurophysiologist, "Neurophysiologist"),
                    ("Pharmacist", StaffRole.Pharmacist, "Pharmacist"),
                    ("Physio", StaffRole.Physiotherapist, "Physiotherapist"),
                    ("Plaster tech", StaffRole.PlasterTechnician, "Plaster technician"),
                    ("Porter", StaffRole.Porter, "Porter"),
                    ("Reception", StaffRole.Receptionist, "Receptionist"),
                };

                Action<CanvasItem> Person((string Label, StaffRole Role, string Grade) s, int id, int pose)
                {
                    var member = new StaffMember { Id = id, Name = s.Label, Role = s.Role, Grade = s.Grade, DepartmentId = "ed", Age = 24 + ((id * 7) % 38) };
                    var look = Looks.Staff(member);
                    return pose switch
                    {
                        1 => c => Figures.Standing(c, foot, look, time, true, id * 7, away: true),
                        2 => c => Figures.Seated(c, foot, look),
                        _ => c => Figures.Standing(c, foot, look, time, false, id * 7),
                    };
                }

                if (page == "people")
                {
                    for (var r = 0; r < 3; r++)
                    {
                        Row(150 + (r * 190), 170, staff.Skip(r * 8).Take(8).Select((s, i) => (s.Label, Person(s, 3 + (r * 8) + i, 0))), 2.2f);
                    }

                    var ages = new[] { (3, Sex.Male), (6, Sex.Female), (9, Sex.Male), (14, Sex.Female), (16, Sex.Male), (27, Sex.Female), (34, Sex.Male), (45, Sex.Female), (52, Sex.Male), (66, Sex.Female), (74, Sex.Male), (81, Sex.Female), (88, Sex.Male) };
                    Row(735, 115, ages.Select((a, i) => ($"{(a.Item2 == Sex.Female ? "F" : "M")} {a.Item1}", (Action<CanvasItem>)(c => Figures.Standing(c, foot, Looks.Preview(40 + (i * 13), a.Item2, a.Item1), time, false, i)))), 2.2f);
                }
                else
                {
                    // A crowd: many patients of every age, then people from behind and sitting down.
                    for (var r = 0; r < 3; r++)
                    {
                        Row(140 + (r * 175), 96, Enumerable.Range(0, 16).Select(i =>
                        {
                            var n = (r * 16) + i;
                            var age = new[] { 4, 7, 10, 13, 17, 22, 28, 33, 39, 44, 51, 58, 64, 71, 79, 87 }[(i + (r * 5)) % 16];
                            var sex = (n + r) % 2 == 0 ? Sex.Female : Sex.Male;
                            return ($"{(sex == Sex.Female ? "F" : "M")} {age}", (Action<CanvasItem>)(c => Figures.Standing(c, foot, Looks.Preview(200 + (n * 31), sex, age, n % 3 == 0), time, false, n)));
                        }), 2f);
                    }

                    Row(700, 96, Enumerable.Range(0, 16).Select(i =>
                    {
                        var age = new[] { 4, 9, 15, 26, 38, 55, 70, 86 }[i % 8];
                        var sex = i % 2 == 0 ? Sex.Female : Sex.Male;
                        var look = Looks.Preview(900 + (i * 17), sex, age);
                        return i < 6 ? ("away", (Action<CanvasItem>)(c => Figures.Standing(c, foot, look, time, true, i, away: true)))
                            : i < 11 ? ("seated", c => Figures.Seated(c, foot, look))
                            : i < 14 ? ("staff away", Person(staff[(i * 5) % staff.Length], 60 + i, 1))
                            : ("staff seated", Person(staff[(i * 3) % staff.Length], 70 + i, 2));
                    }), 2f);
                }

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
        var pregnant = Looks.Patient(mum) with { Bump = true, Jacket = null, Hair = young, Glasses = false, Stick = false, Beard = false, Style = HairStyle.Ponytail };
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

    /// <summary>The chairs sheet: the old chair, the new one at each facing, with and without a sitter, and every colour.</summary>
    private static void ChairsPage(Action<float, float, IEnumerable<(string, Action<CanvasItem>)>, float> row, Func<int, PersonLook> person)
    {
        var tile = new GridCoord(0, 0);
        var blue = new Color(ChairColours.Default);
        var sides = new[] { WallSide.South, WallSide.East, WallSide.North, WallSide.West };
        string Name(WallSide s) => s switch { WallSide.South => "south", WallSide.East => "east", WallSide.North => "north", _ => "west" };

        void Sitting(CanvasItem c, WallSide facing, Color colour, PersonLook look)
        {
            var foot = IsoDraw.ToScreen(0.5, 0.5);
            ChairArt.Back(c, tile, facing, colour);
            if (!ChairArt.FacesAway(facing))
            {
                ChairArt.Seat(c, tile, facing, colour);
                Figures.Seated(c, foot, look, lean: ChairArt.Lean(facing));
            }
            else
            {
                ChairArt.Seat(c, tile, facing, colour);
                Figures.Seated(c, foot, look, away: true);
                ChairArt.Back(c, tile, facing, colour);
            }
        }

        var list = new List<(string, Action<CanvasItem>)>
        {
            ("Old chair", c =>
            {
                IsoDraw.Box(c, 0.2, 0.15, 0.6, 0.12, 24, blue.Darkened(0.1f));
                IsoDraw.Box(c, 0.15, 0.15, 0.7, 0.7, 11, blue);
            }),
        };
        list.AddRange(sides.Select(s => ($"Facing {Name(s)}", (Action<CanvasItem>)(c => ChairArt.Whole(c, tile, s, blue)))));
        row(190, 230, list, 3.2f);

        row(470, 230, sides.Select((s, i) => ($"Sitting, {Name(s)}", (Action<CanvasItem>)(c => Sitting(c, s, blue, person(i + 2))))), 3.2f);

        row(760, 175, ChairColours.All.Select((col, i) => (col.Name, (Action<CanvasItem>)(c => ChairArt.Whole(c, tile, WallSide.South, new Color(col.Hex))))), 2.6f);
    }

    /// <summary>The ambulance crew's stretcher, as drawn while the patient waits in the ambulance queue.</summary>
    private static void AmbulanceStretcher(CanvasItem c, PersonLook look) => Figures.AmbulanceStretcher(c, 0, 0, (0, 1), look, new Color("#B5413B"), Looks.Red);
}
