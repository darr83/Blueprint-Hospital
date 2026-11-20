using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// Every bed, couch and imaging or operating table on the floor in view, each with whoever is lying
/// on it: a patient in bed under their blanket, wristband on, or on the table during a scan or an operation.
/// Rebuilt when the building changes; the patients are kept up to date every frame.
/// </summary>
public partial class Beds3D : Node3D
{
    private static readonly Color SelectedRing = new("#FFEB3B");
    private static readonly Color TheatreDrape = new("#4E9C8F");
    private static readonly Color ScanSheet = new("#CFE0EE");

    private View3D _view = null!;
    private readonly List<BedView> _beds = new();
    private readonly List<ScanFx> _fx = new();
    private readonly List<(CareSpace Room, SpotLight3D Light)> _theatreLights = new();

    public void Initialise(View3D view) => _view = view;

    public int Count => _beds.Count;

    /// <summary>Builds a bed or table for every one in the rooms on the floor in view.</summary>
    private string? _builtKey;

    /// <summary>Builds the beds again, unless <paramref name="key"/> (the rooms and their furniture) is what they were last built from.</summary>
    public void Rebuild(string? key = null)
    {
        if (key is not null && key == _builtKey)
        {
            return;
        }

        _builtKey = key;
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }

        _beds.Clear();
        _fx.Clear();
        _theatreLights.Clear();
        var world = _view.World;
        var sim = _view.Simulation;
        foreach (var space in world.ViewSpaces)
        {
            foreach (var item in space.Furniture)
            {
                if (!sim.Database.Furniture.TryGetValue(item.ItemId, out var def) || def.Style != FurnitureStyle.Bed || def.Id == "incubator")
                {
                    continue;
                }

                var colour = new Color(def.Colour);
                BedView? view = def.Id switch
                {
                    "birthing_bed" => new BedView(this, space.PatientSpot, false, null, () => space.OccupantId, Looks.MaternityBlanket, false),
                    _ when space.IsServiceRoom => new BedView(this, space.PatientSpot, false, (def.Id, colour), null, space.Kind == CareSpaceKind.Theatre ? TheatreDrape : ScanSheet, false, SlideTowards(space, item, def.Id)) { Plaster = space.Kind == CareSpaceKind.PlasterRoom },
                    _ when space.IsMultiBed => space.Beds.FirstOrDefault(b => b.PatientSpot == item.Tile) is { } bay
                        ? new BedView(this, bay.PatientSpot, bay.HeadSouth, null, () => bay.OccupantId, Looks.EdBlanket, false)
                        : null,
                    _ when space.Kind == CareSpaceKind.TheatreRecovery => space.Beds.FirstOrDefault(b => b.PatientSpot == item.Tile) is { } recoveryBay
                        ? new BedView(this, recoveryBay.PatientSpot, recoveryBay.HeadSouth, null, () => recoveryBay.OccupantId, Looks.WardBlanket, false)
                        : null,
                    _ when space.IsWard => space.Beds.FirstOrDefault(b => b.PatientSpot == item.Tile) is { } wardBed
                        ? new BedView(this, wardBed.PatientSpot, wardBed.HeadSouth, null, () => wardBed.OccupantId, Looks.WardBlanketFor(sim, space, wardBed), true)
                        : null,
                    _ => new BedView(this, space.PatientSpot, false, null, () => space.OccupantId, Looks.EdBlanket, false),
                };
                if (view is not null)
                {
                    _beds.Add(view);
                }

                if (def.Id is "operating_table" or "cath_lab_table")
                {
                    // The theatre lights come on over the table while an operation is under way.
                    var light = new SpotLight3D
                    {
                        Name = "TheatreLight",
                        Position = Kit3D.At(space.PatientSpot.X + 0.5, space.PatientSpot.Y + 0.5, Kit3D.H(84)),
                        RotationDegrees = new Vector3(-90, 0, 0),
                        LightColor = new Color("#FFF6DD"),
                        LightEnergy = 3.5f,
                        SpotRange = Kit3D.H(84) * 1.6f,
                        SpotAngle = 28,
                        ShadowEnabled = false,
                        Visible = false,
                    };
                    AddChild(light);
                    _theatreLights.Add((space, light));
                }

                if (def.Id is "xray_table" or "ct_table" && ScanFx.Make(this, space, item, def.Id == "ct_table", sim) is { } fx)
                {
                    _fx.Add(fx);
                }
            }
        }
    }

    /// <summary>The animation clock, in real seconds; it stands still while the game is paused.</summary>
    private double _time;

    public void Sync(double delta)
    {
        if (!_view.Simulation.Clock.IsPaused)
        {
            _time += delta;
        }

        // Patients waiting for a nurse to come: their call light is on.
        var calling = _view.Simulation.Tasks
            .Where(t => !t.Started && t.Role is Simulation.Staff.StaffRole.Nurse or Simulation.Staff.StaffRole.HealthcareAssistant
                && t.Kind is Simulation.Staff.TaskKind.Observations or Simulation.Staff.TaskKind.GiveMedication)
            .Select(t => t.PatientId)
            .ToHashSet();
        // Patients having a drip put up right now: the bed's pole carries the new bag.
        var dripping = _view.Simulation.Tasks
            .Where(t => t.Started && Activity3D.OnDrip(t, _view.Simulation))
            .Select(t => t.PatientId)
            .ToHashSet();
        foreach (var bed in _beds)
        {
            bed.Sync(_view.World, _view.Simulation, _time, calling, dripping);
        }

        foreach (var fx in _fx)
        {
            fx.Sync(_view.Simulation, _time);
        }

        foreach (var (room, light) in _theatreLights)
        {
            light.Visible = _view.Simulation.Tasks.Any(t => t.Started && t.SpaceId == room.Id && t.Kind is Simulation.Staff.TaskKind.Surgery or Simulation.Staff.TaskKind.Procedure);
        }
    }

    /// <summary>Where the scan of <paramref name="patient"/> is in its round (see <see cref="Activity3D.ScanPhase"/>), or null when nobody is being scanned.</summary>
    private static double? ScanPhaseFor(HospitalSimulation sim, Patient? patient, double time)
    {
        if (patient is null)
        {
            return null;
        }

        var task = sim.Tasks.FirstOrDefault(t => t.Started && t.Kind == Simulation.Staff.TaskKind.Imaging && t.PatientId == patient.Id);
        return task is null ? null : Activity3D.ScanPhase(time, task.Id);
    }

    /// <summary>
    /// For a CT table: how far (in tiles, + towards south) its top slides to carry the patient into the
    /// scanner's ring, or zero for tables that stay put.
    /// </summary>
    private static float SlideTowards(CareSpace space, PlacedFurniture table, string id)
    {
        if (id != "ct_table" || space.Furniture.FirstOrDefault(f => f.ItemId == "ct_gantry") is not { } gantry)
        {
            return 0;
        }

        var dy = gantry.CentreY - table.CentreY;
        var reach = (float)Math.Min(Math.Abs(dy) - 0.4, 2.0);
        return reach <= 0 ? 0 : Math.Sign(dy) * reach;
    }

    /// <summary>One bed or table and its patient.</summary>
    private sealed class BedView
    {
        private readonly GridCoord _spot;
        private readonly bool _table;
        private readonly Func<int?>? _occupantId;
        private readonly Color _blanket;
        private readonly bool _onWard;
        private readonly Node3D _node;
        private readonly float _mattress;
        private Node3D? _made;
        private Node3D? _lying;
        private object? _lyingKey;
        private Node3D? _ring;
        private Color? _ringColour;
        private Alert3D? _alert;
        private readonly Node3D _top;
        private readonly float _slide;

        public BedView(Node3D parent, GridCoord spot, bool headSouth, (string Id, Color Colour)? table, Func<int?>? occupantId, Color blanket, bool onWard, float slide = 0)
        {
            _slide = slide;
            _spot = spot;
            _table = table is not null;
            _occupantId = occupantId;
            _blanket = blanket;
            _onWard = onWard;
            _node = new Node3D
            {
                Name = $"Bed{spot.X}_{spot.Y}",
                Position = Kit3D.At(spot.X + 0.5, spot.Y + 0.5),
                Rotation = new Vector3(0, headSouth ? Mathf.Pi : 0, 0),
            };
            parent.AddChild(_node);

            var frame = new MeshBatch();
            _top = _node;
            if (slide != 0 && table is { } ct)
            {
                // The CT table's top (and whoever lies on it) is its own node, so it can glide into the scanner.
                Carriers3D.Table(frame, "ct_base", ct.Colour);
                _top = new Node3D { Name = "TableTop" };
                _node.AddChild(_top);
                var top = new MeshBatch();
                _mattress = Carriers3D.CtTop(top, ct.Colour);
                top.AddTo(_top);
            }
            else
            {
                _mattress = table is { } t ? Carriers3D.Table(frame, t.Id, t.Colour) : Carriers3D.Bed(frame, made: false, back: null);
            }

            frame.AddTo(_node);
            if (!_table)
            {
                Recline(0);

                // An empty bed is made up, with its blanket on.
                var made = new MeshBatch();
                Carriers3D.BedMade(made, System.Array.IndexOf(Looks.ChildrensBlankets, _blanket) >= 0 ? _blanket : null);
                _made = made.AddTo(_node);
            }
        }

        private readonly System.Collections.Generic.Dictionary<float, Node3D> _backs = new();
        private InBed _doing;

        /// <summary>
        /// Sets the bed's backrest at <paramref name="angle"/>: each angle it has been at is built once and kept, and
        /// only the one in use is shown.
        /// </summary>
        private void Recline(float angle)
        {
            if (!_backs.ContainsKey(angle))
            {
                var b = new MeshBatch();
                Carriers3D.Back(b, "bed", Carriers3D.BedBack, _mattress, angle);
                var back = b.AddTo(_node)!;
                back.Name = $"Back{Mathf.RoundToInt(Mathf.RadToDeg(angle))}";
                _backs[angle] = back;
            }

            foreach (var (at, back) in _backs)
            {
                back.Visible = at == angle;
            }
        }

        /// <summary>How far the bed's back is up for <paramref name="patient"/>: sat up by day, lower asleep, flat for CPR.</summary>
        private static float BackAngle(Patient? patient, bool asleep) => patient switch
        {
            null or { InArrest: true } or { IsAlive: false } => 0f,
            _ when asleep => Mathf.DegToRad(20),
            _ => Carriers3D.BedBack.Raised,
        };

        /// <summary>A plaster room couch: the cast goes on whoever lies on it.</summary>
        public bool Plaster { get; init; }

        public void Sync(WorldLayer world, HospitalSimulation sim, double time, HashSet<int> calling, HashSet<int> dripping)
        {
            var patient = _table
                ? sim.Patients.FirstOrDefault(p => !p.IsMoving && p.Tile == _spot)
                : world.BedOccupant(_spot, _occupantId?.Invoke());

            // Out of bed with the physio: the bed is left empty while they walk.
            if (patient is not null && !_table && Activity3D.PhysioWalk(patient, world, sim, time) is not null)
            {
                patient = null;
            }

            if (_made is not null)
            {
                _made.Visible = patient is null;
            }

            Lying(world, patient, time);
            Slide(ScanPhaseFor(sim, patient, time));
            Ring(patient is null ? null
                : ReferenceEquals(world.Selected, patient) ? SelectedRing
                : ReferenceEquals(world.Hovered, patient) ? Agents3D.HoverRing
                : null);
            Alert(patient);
            CallLight(_onWard && patient is not null && calling.Contains(patient.Id) && !Asleep(sim, patient), time);
            DripPole(_onWard && patient is not null && dripping.Contains(patient.Id));
        }

        private Node3D? _pole;

        /// <summary>The bed's own drip pole at the head corner, with a bag of fluid, while a drip goes up.</summary>
        private void DripPole(bool on)
        {
            if (_pole is null && !on)
            {
                return;
            }

            if (_pole is null)
            {
                var b = new MeshBatch();
                var steel = new Color("#B0BEC5");
                var foot = new Vector3(-0.66f, Kit3D.H(20f), -1.9f);
                var top = new Vector3(-0.66f, Kit3D.H(66f), -1.9f);
                b.Rod(foot, top, 0.022f, steel);
                b.Rod(top + new Vector3(-0.1f, 0, 0), top + new Vector3(0.1f, 0, 0), 0.016f, steel, small: true);
                var bag = top + new Vector3(0.08f, -Kit3D.H(7f), 0);
                b.Box(bag, new Vector3(0.12f, Kit3D.H(9f), 0.05f), Basis.Identity, new Color("#DCEFFA"));
                b.Box(bag + new Vector3(0, -Kit3D.H(1.5f), 0.02f), new Vector3(0.08f, Kit3D.H(3.5f), 0.012f), Basis.Identity, new Color("#4FA3E0"));
                b.Rod(bag - new Vector3(0, Kit3D.H(4.5f), 0), new Vector3(-0.3f, Kit3D.H(26f), -1.3f), 0.008f, new Color("#E8F1F5"), small: true);
                _pole = b.AddTo(_node)!;
                _pole.Name = "DripPole";
            }

            _pole.Visible = on;
        }

        private Node3D? _bell;

        /// <summary>The call light on the bed head, flashing while the patient waits for a nurse.</summary>
        private void CallLight(bool on, double time)
        {
            if (_bell is null && !on)
            {
                return;
            }

            if (_bell is null)
            {
                var b = new MeshBatch();
                b.Ball(new Vector3(0.6f, Kit3D.H(28.2f), -1.99f), new Vector3(0.17f, 0.12f, 0.17f), new Color("#FFB300"), small: true);
                b.Ball(new Vector3(0.6f, Kit3D.H(28.2f), -1.99f), new Vector3(0.42f, 0.3f, 0.42f), new Color("#FFB300", 0.3f), small: true);
                _bell = b.AddTo(_node, alpha: true, shadow: false)!;
                ((MeshInstance3D)_bell).MaterialOverride = Kit3D.VertexColoursUnshadedAlpha;
                _bell.Name = "CallLight";
            }

            _bell.Visible = on && Mathf.PosMod((float)time, 1.6f) < 1.1f;
        }

        /// <summary>What a patient awake in a ward bed is doing: lunch on a tray at mealtimes, otherwise a book, their phone or nothing much.</summary>
        private InBed Doing(HospitalSimulation sim, Patient patient, bool asleep, double time)
        {
            if (!_onWard || asleep || patient.BusyWithTaskId is not null || patient.InArrest || patient.Emergency is not null || !patient.IsAlive)
            {
                return InBed.None;
            }

            var t = sim.Now.TimeOfDay.TotalHours;
            if (t is >= 8 and < 8.75 or >= 12.25 and < 13.25 or >= 17.25 and < 18.25)
            {
                return InBed.Meal;
            }

            var roll = Activity3D.Chance(patient.Id, (int)Math.Floor((time + (patient.Id * 3.1)) / 20));
            return roll switch
            {
                < 0.3 => InBed.Book,
                < 0.5 => InBed.Phone,
                _ => InBed.None,
            };
        }

        private Node3D? _table3;

        /// <summary>The table across the bed, with a tray of food, at mealtimes.</summary>
        private void MealTable(bool on)
        {
            if (_table3 is null && !on)
            {
                return;
            }

            if (_table3 is null)
            {
                var b = new MeshBatch();
                var top = _mattress + 0.32f;
                var z = -0.3f;
                var grey = new Color("#B0BEC5");
                b.Box(new Vector3(0.95f, 0.02f, z), new Vector3(0.12f, 0.04f, 0.6f), grey);
                b.Box(new Vector3(0.95f, top / 2, z), new Vector3(0.06f, top, 0.06f), grey);
                b.Box(new Vector3(0.25f, top, z), new Vector3(1.45f, 0.04f, 0.5f), new Color("#D7C4A3"));
                b.Box(new Vector3(0.15f, top + 0.03f, z), new Vector3(0.75f, 0.02f, 0.42f), new Color("#5C7A99"));
                b.Cylinder(new Vector3(0.05f, top + 0.05f, z), 0.17f, 0.02f, 0.17f, Colors.White);
                b.Ball(new Vector3(0.0f, top + 0.08f, z - 0.03f), new Vector3(0.12f, 0.06f, 0.1f), new Color("#C8913F"), small: true);
                b.Ball(new Vector3(0.1f, top + 0.07f, z + 0.05f), new Vector3(0.1f, 0.05f, 0.08f), new Color("#6AA84F"), small: true);
                b.Cylinder(new Vector3(0.38f, top + 0.09f, z - 0.08f), 0.05f, 0.12f, 0.05f, new Color("#F4F1EA"));
                b.Cylinder(new Vector3(0.38f, top + 0.155f, z - 0.08f), 0.042f, 0.01f, 0.042f, new Color("#6B4226"));
                _table3 = new Node3D { Name = "MealTable" };
                b.AddTo(_table3);
                _node.AddChild(_table3);
            }

            _table3.Visible = on;
        }

        /// <summary>During a scan the table top glides in, holds while the scanner runs, and glides back out.</summary>
        private void Slide(double? phase)
        {
            if (_slide == 0)
            {
                return;
            }

            var into = phase switch
            {
                null => 0f,
                < 5.4 => 0f,
                < 6.6 => Smooth((float)((phase.Value - 5.4) / 1.2)),
                < 8.6 => 1f,
                < 9.8 => 1f - Smooth((float)((phase.Value - 8.6) / 1.2)),
                _ => 0f,
            };
            _top.Position = new Vector3(0, 0, _slide * into);
        }

        private static float Smooth(float x) => x * x * (3 - (2 * x));

        private static bool Asleep(HospitalSimulation sim, Patient patient)
        {
            var hour = sim.Now.Hour;
            return (hour >= 22 || hour < 7) && patient.BusyWithTaskId is null && !patient.InArrest && patient.Emergency is null && patient.IsAlive;
        }

        private Label3D? _zs;

        /// <summary>Shows (or clears) the little Zs drifting up from a sleeping patient.</summary>
        private void Snore(bool asleep)
        {
            if (asleep == (_zs is not null))
            {
                return;
            }

            if (!asleep)
            {
                _zs!.QueueFree();
                _zs = null;
                return;
            }

            _zs = new Label3D
            {
                Text = "z",
                FontSize = 64,
                PixelSize = 0.006f,
                Modulate = new Color("#5C7FA3"),
                OutlineSize = 10,
                OutlineModulate = new Color(1, 1, 1, 0.8f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = false,
                Position = new Vector3(0.25f, _mattress + 0.7f, -1.55f),
            };
            _node.AddChild(_zs);
        }

        /// <summary>A slow rise and fall under the covers while asleep, and the Zs float up and fade.</summary>
        private void Breathe(bool asleep, double time)
        {
            var phase = (float)(time + (_spot.X * 0.37) + (_spot.Y * 0.61));
            if (_lying is not null)
            {
                _lying.Scale = asleep ? new Vector3(1, 1 + (0.06f * Mathf.Sin(phase * 1.4f)), 1) : Vector3.One;
            }

            if (_zs is not null)
            {
                var rise = Mathf.PosMod(phase * 0.45f, 1f);
                _zs.Position = new Vector3(0.25f + (0.15f * rise), _mattress + 0.55f + (0.9f * rise), -1.55f);
                _zs.Text = rise < 0.33f ? "z" : rise < 0.66f ? "zz" : "zzz";
                _zs.Modulate = new Color("#5C7FA3", 1f - rise);
            }
        }

        private void Lying(WorldLayer world, Patient? patient, double time)
        {
            if (patient is null)
            {
                if (!_table)
                {
                    Recline(0);
                }

                Snore(false);
                MealTable(false);
                _lying?.QueueFree();
                (_lying, _lyingKey) = (null, null);
                return;
            }

            var look = Looks.Patient(patient);
            var band = _table || _onWard || patient.Maternity ? null : Looks.Wristband(patient.Triage);
            var babies = world.OnChest(patient);
            // In bed by day they're awake; at night, unless someone is with them, they sleep (eyes shut, a few Zs).
            var asleep = !_table && Asleep(world.Simulation, patient);
            var doing = babies is { Length: > 0 } ? InBed.None : Doing(world.Simulation, patient, asleep, time);
            var cast = Plaster ? CastOn(world.Simulation, patient) : 0f;
            var angle = _table ? 0f : BackAngle(patient, asleep);
            var sim = world.Simulation;
            var (care, careTask) = Procedure3D.For(patient, sim);
            var worker = careTask is null ? null : Procedure3D.Doing(careTask, sim);
            if (babies is { Length: > 0 } || (care is Care.Ecg or Care.Birth && worker is null) || (_table && care is Care.Ecg or Care.Birth))
            {
                (care, careTask) = (Care.None, null);
            }

            var side = care switch
            {
                Care.Ecg or Care.Birth or Care.Endoscopy => _careFor == careTask?.Id ? _careSide : Procedure3D.Side(patient, worker, sim),
                Care.Echo or Care.Ultrasound or Care.Eeg => Procedure3D.MachineSide(patient, sim),
                _ => 1f,
            };
            (_careSide, _careFor) = (side, careTask?.Id);
            var born = care == Care.Birth ? Procedure3D.Born(careTask!, sim) : 0f;
            if (born >= 2f)
            {
                // The baby is on her chest: she lies back and cuddles it until the midwife has finished.
                babies = new[] { Newborn(look) };
                care = Care.None;
            }

            if (care == Care.Birth)
            {
                (angle, asleep, doing) = (Mathf.DegToRad(58), false, InBed.None);
            }
            else if (care == Care.Ecg)
            {
                (asleep, doing) = (false, InBed.None);
            }
            else if (babies is { Length: > 0 })
            {
                (asleep, doing) = (false, InBed.None);
            }

            var key = (look, band, babies?.Length ?? 0, babies is { Length: > 0 } ? babies[0] : null, asleep, doing, cast, angle, care, side);
            Breathe(asleep, time);
            MealTable(doing == InBed.Meal);
            Busy(time);
            EcgCart(care == Care.Ecg, side, careTask, sim, time);
            Labour(care == Care.Birth ? careTask : null, look, side, born, time, angle);
            Laser(care, careTask, time);
            EegWires(care == Care.Eeg ? Procedure3D.Machine(patient, sim) : null, sim);
            var rebuilt = _lying is null || !Equals(_lyingKey, key);
            if (!rebuilt && care == Care.Endoscopy && _lying is not null)
            {
                OnSide(_lying, side);
            }
            if (_lying is not null && Equals(_lyingKey, key))
            {
                return;
            }

            _lying?.QueueFree();
            _lyingKey = key;
            _lying = Lying3D.Build(look, _blanket, band, babies, awake: !asleep, doing, cast, _table ? null : Carriers3D.BedBack, angle, care, side);
            _doing = doing;
            if (!_table)
            {
                Recline(angle);
            }

            Snore(asleep);
            _lying.Position = new Vector3(0, _mattress, 0);
            if (care == Care.Endoscopy)
            {
                OnSide(_lying, side);
            }

            _top.AddChild(_lying);
        }

        private float _careSide = 1;
        private Node3D? _cart;
        private Node3D? _cartBlip;
        private Node3D? _cartPaper;
        private float _cartSide;

        /// <summary>
        /// The ECG machine on its cart beside the bed while the recording is taken: a screen with the trace
        /// running across it, and the pink paper strip printing out of the side as the test goes on.
        /// </summary>
        private void EcgCart(bool on, float side, Simulation.Staff.ClinicalTask? task, HospitalSimulation sim, double time)
        {
            if (_cart is not null && (!on || side != _cartSide))
            {
                _cart.QueueFree();
                (_cart, _cartBlip, _cartPaper) = (null, null, null);
            }

            if (!on)
            {
                return;
            }

            if (_cart is null)
            {
                _cartSide = side;
                var socket = Lying3D.EcgSocket(side) + new Vector3(0, _mattress, 0);
                _cart = new Node3D { Name = "EcgCart", Position = new Vector3(socket.X + (side * 0.32f), 0, socket.Z) };
                _node.AddChild(_cart);
                var b = new MeshBatch();
                var steel = new Color("#B0BEC5");
                var body = new Color("#ECEFF1");
                var top = socket.Y - 0.1f;
                foreach (var (x, z) in new[] { (-0.3f, -0.3f), (0.3f, -0.3f), (0.3f, 0.3f), (-0.3f, 0.3f) })
                {
                    b.Ball(new Vector3(x, 0.07f, z), new Vector3(0.13f, 0.14f, 0.13f), new Color("#263238"), small: true);
                    b.Rod(new Vector3(x, 0.1f, z), new Vector3(x, top, z), 0.022f, steel, small: true);
                }

                b.Box(new Vector3(0, 0.2f, 0), new Vector3(0.7f, 0.04f, 0.7f), steel);
                b.Box(new Vector3(0, top * 0.55f, 0), new Vector3(0.66f, 0.2f, 0.62f), new Color("#D6DEE3"));
                b.Box(new Vector3(0, top, 0), new Vector3(0.78f, 0.05f, 0.78f), body);
                // The machine: a cream box with a sloping screen facing the foot of the bed.
                b.Box(new Vector3(0, top + 0.13f, 0), new Vector3(0.66f, 0.22f, 0.6f), new Color("#F5F1E6"));
                b.Box(new Vector3(0, top + 0.1f, 0.31f), new Vector3(0.5f, 0.06f, 0.03f), new Color("#1E88E5"));
                var tilt = new Basis(Vector3.Right, -0.5f);
                b.Box(new Vector3(0, top + 0.44f, -0.1f), new Vector3(0.62f, 0.42f, 0.05f), tilt, new Color("#263238"));
                b.Box(new Vector3(0, top + 0.44f, -0.1f) + (tilt * new Vector3(0, 0, 0.03f)), new Vector3(0.54f, 0.34f, 0.012f), tilt, new Color("#0B1F17"));
                b.Ball(new Vector3(-side * 0.33f, top + 0.13f, 0.1f), new Vector3(0.1f, 0.1f, 0.1f), new Color("#546E7A"), small: true);
                b.AddTo(_cart);

                // The trace: a row of green marks on the screen, with a bright blip that runs along it.
                var trace = new MeshBatch();
                var green = new Color("#5CFF8A");
                var screen = new Transform3D(tilt, new Vector3(0, top + 0.44f, -0.1f) + (tilt * new Vector3(0, 0, 0.04f)));
                for (var row = 0; row < 3; row++)
                {
                    var y = 0.1f - (row * 0.1f);
                    for (var i = 0; i < 12; i++)
                    {
                        var x = -0.24f + (i * 0.044f);
                        var spike = i % 4 == 2 ? 0.07f : i % 4 == 3 ? 0.025f : 0.008f;
                        trace.Box(screen * new Vector3(x, y + (spike / 2) - 0.02f, 0), new Vector3(0.012f, spike, 0.004f), tilt, green);
                    }
                }

                var lines = trace.AddTo(_cart, alpha: true, shadow: false)!;
                lines.MaterialOverride = Kit3D.VertexColoursUnshadedAlpha;
                var blip = new MeshBatch();
                blip.Ball(Vector3.Zero, new Vector3(0.05f, 0.05f, 0.02f), new Color("#D8FFE4"), small: true);
                _cartBlip = new Node3D { Name = "Blip" };
                _cart.AddChild(_cartBlip);
                var dot = blip.AddTo(_cartBlip, alpha: true, shadow: false)!;
                dot.MaterialOverride = Kit3D.VertexColoursUnshadedAlpha;
                _cartBlip.SetMeta("screen", screen);

                // The paper strip: pink graph paper feeding out of the side of the machine.
                _cartPaper = new Node3D { Name = "Paper", Position = new Vector3(side * 0.33f, top + 0.16f, 0.12f) };
                _cart.AddChild(_cartPaper);
                var paper = new MeshBatch();
                paper.Box(new Vector3(side * 0.5f, 0, 0), new Vector3(1f, 0.008f, 0.2f), new Color("#FFD9E0"));
                paper.Box(new Vector3(side * 0.5f, 0.005f, 0), new Vector3(1f, 0.004f, 0.012f), new Color("#C2185B"));
                paper.AddTo(_cartPaper, shadow: false);
            }

            if (_cartBlip is not null)
            {
                var screen = (Transform3D)_cartBlip.GetMeta("screen");
                var u = Mathf.PosMod((float)time * 0.55f, 1f);
                _cartBlip.Position = screen * new Vector3(-0.25f + (0.5f * u), 0.1f - (0.1f * (int)(Mathf.PosMod((float)time * 0.55f, 3f))), 0.004f);
            }

            if (_cartPaper is not null)
            {
                // Leads go on for the first part of the test; the strip prints during the rest.
                var done = task is null ? 0 : Activity3D.Progress(task, sim);
                var printed = (float)Math.Clamp((done - 0.45) / 0.5, 0, 1);
                _cartPaper.Visible = printed > 0.02f;
                _cartPaper.Scale = new Vector3(Mathf.Max(0.02f, printed * 0.5f), 1, 1);
                _cartPaper.Rotation = new Vector3(0, 0, -_cartSide * 0.5f * printed);
            }
        }

        private int? _careFor;
        private Node3D? _newborn;

        /// <summary>A newborn wrapped in a white towel with a little hat, with its mother's colouring.</summary>
        private static BabyLook Newborn(PersonLook mother) => new(new Color("#F7F9FA"), new Color("#DCE8EE"), new Color("#FFF3C4"), mother.Skin.Lightened(0.08f));

        /// <summary>Turned on their side for an endoscopy, facing whoever is doing it.</summary>
        private void OnSide(Node3D lying, float side)
        {
            lying.Rotation = new Vector3(0, 0, -side * 1.3f);
            lying.Position = new Vector3(-side * 0.12f, _mattress + 0.3f, 0);
        }

        /// <summary>
        /// Labour: she curls forward and pushes with each contraction, then rests. When the baby is born the
        /// midwife lifts it clear (born 0 to 1) and lays it on its mother's chest (1 to 2).
        /// </summary>
        private void Labour(Simulation.Staff.ClinicalTask? task, PersonLook look, float side, float born, double time, float angle)
        {
            if (task is null)
            {
                _newborn?.QueueFree();
                _newborn = null;
                return;
            }

            var push = born > 0 ? 0f : Procedure3D.Push(time, task.Id);
            if (_lying?.FindChild("Upper", false, false) is Node3D upper)
            {
                upper.Transform = Carriers3D.BedBack.Turn(0, angle + (0.2f * push));
            }

            if (born <= 0)
            {
                _newborn?.QueueFree();
                _newborn = null;
                return;
            }

            if (_newborn is null)
            {
                _newborn = Baby3D.Bundle(Newborn(look), 1f);
                _newborn.Name = "Newborn";
                _node.AddChild(_newborn);
            }

            var hb = Carriers3D.BedBack.HingeZ + 0.15f;
            var up = new Vector3(0, _mattress, 0);
            var from = up + new Vector3(0, 0.3f, hb + 0.8f);
            var hands = up + new Vector3(side * 0.8f, 0.8f, hb + 0.55f);
            var chest = up + Lying3D.Chest(look, Carriers3D.BedBack, angle, 0.5f);
            _newborn.Position = born < 1f
                ? from.Lerp(hands, Smooth(born)) + new Vector3(0, 0.2f * Mathf.Sin(born * Mathf.Pi), 0)
                : hands.Lerp(chest, Smooth(born - 1f)) + new Vector3(0, 0.25f * Mathf.Sin((born - 1f) * Mathf.Pi), 0);
            _newborn.Rotation = new Vector3(0, Mathf.Pi / 2, 0.25f * Mathf.Sin((float)time * 1.3f));
        }

        private Node3D? _wires;

        /// <summary>An EEG: the bundle of coloured wires from the cap on the patient's head to the recorder's head box.</summary>
        private void EegWires(PlacedFurniture? machine, HospitalSimulation sim)
        {
            if (machine is null)
            {
                _wires?.QueueFree();
                _wires = null;
                return;
            }

            if (_wires is not null)
            {
                return;
            }

            var h = sim.Database.Furniture.TryGetValue(machine.ItemId, out var def) ? Kit3D.M(def.HeightM) : 1.2f;
            var box = _node.ToLocal(Kit3D.At(machine.CentreX, machine.CentreY, (h * 0.62f) + 0.12f));
            var crown = new Vector3(0, _mattress + 0.2f, -1.66f);
            var sag = crown.Lerp(box, 0.5f) + new Vector3(0, -0.25f, -0.15f);
            var b = new MeshBatch();
            var colours = new[] { "#E53935", "#FDD835", "#43A047", "#1E88E5", "#8E24AA" };
            for (var i = 0; i < colours.Length; i++)
            {
                var off = new Vector3((i - 2) * 0.018f, (i % 2) * 0.014f, 0);
                var c = new Color(colours[i]);
                b.Rod(crown + off, crown.Lerp(sag, 0.6f) + (off * 1.5f), 0.008f, c, small: true);
                b.Rod(crown.Lerp(sag, 0.6f) + (off * 1.5f), sag + (off * 1.5f), 0.008f, c, small: true);
                b.Rod(sag + (off * 1.5f), sag.Lerp(box, 0.6f) + off + new Vector3(0, 0.05f, 0), 0.008f, c, small: true);
                b.Rod(sag.Lerp(box, 0.6f) + off + new Vector3(0, 0.05f, 0), box + off, 0.008f, c, small: true);
            }

            _wires = b.AddTo(_node, shadow: false)!;
            _wires.Name = "EegWires";
        }

        private Node3D? _laser;
        private Care _laserFor;

        /// <summary>
        /// Lining up for a picture: the X-ray tube's light field (a soft yellow rectangle with a cross) on the part
        /// being X-rayed, or the CT scanner's red laser line across the patient, while the radiographer positions them.
        /// </summary>
        private void Laser(Care care, Simulation.Staff.ClinicalTask? task, double time)
        {
            var xray = care is Care.XrayChest or Care.XrayAbdomen or Care.XrayArm or Care.XrayLeg;
            var ct = care is Care.CtHead or Care.CtBody;
            var phase = task is { Started: true } ? Activity3D.ScanPhase(time, task.Id) : -1;
            var on = (xray || ct) && phase is >= 0.6 and < 5.2;
            if (_laser is not null && (!on || care != _laserFor))
            {
                _laser.QueueFree();
                _laser = null;
            }

            if (!on || _laser is not null)
            {
                return;
            }

            _laserFor = care;
            var at = Procedure3D.Target(care);
            var b = new MeshBatch();
            var y = _mattress + 0.36f;
            if (xray)
            {
                var (w, l) = care switch { Care.XrayArm => (0.4f, 0.6f), Care.XrayLeg => (0.5f, 0.85f), _ => (0.95f, 0.85f) };
                b.Box(new Vector3(at.X, y, at.Y), new Vector3(w, 0.004f, l), new Color("#FFF59D", 0.42f));
                b.Box(new Vector3(at.X, y + 0.004f, at.Y), new Vector3(w, 0.004f, 0.014f), new Color("#5D4037", 0.8f));
                b.Box(new Vector3(at.X, y + 0.004f, at.Y), new Vector3(0.014f, 0.004f, l), new Color("#5D4037", 0.8f));
            }
            else
            {
                b.Box(new Vector3(0, y, at.Y), new Vector3(1.3f, 0.006f, 0.022f), new Color("#FF1744", 0.9f));
                b.Box(new Vector3(0, y, at.Y + 0.6f), new Vector3(0.022f, 0.006f, 2.6f), new Color("#FF1744", 0.9f));
            }

            var mesh = b.AddTo(_top, alpha: true, shadow: false)!;
            mesh.MaterialOverride = Kit3D.VertexColoursUnshadedAlpha;
            mesh.Name = "LineUp";
            _laser = mesh;
        }

        /// <summary>How much of the cast is on (in fifths), while a plaster technician is putting it on.</summary>
        /// <remarks>Negative for a cast on the leg.</remarks>
        private static float CastOn(HospitalSimulation sim, Patient patient)
        {
            var task = sim.Tasks.FirstOrDefault(t => t.Started && t.Kind == Simulation.Staff.TaskKind.Procedure && t.PatientId == patient.Id);
            if (task is null)
            {
                return 0f;
            }

            var done = Mathf.Max(0.2f, Mathf.Ceil((float)Activity3D.Progress(task, sim) * 5f) / 5f);
            return Activity3D.LegCast(task) ? -done : done;
        }

        /// <summary>The raised hands move a little: turning a page, scrolling, a forkful now and then.</summary>
        private void Busy(double time)
        {
            if (_lying?.FindChild("Holding", true, false) is not Node3D hands)
            {
                return;
            }

            var f = (float)time + (_spot.X * 0.7f);
            var doing = _doing;
            hands.Rotation = doing switch
            {
                InBed.Meal => new Vector3(-0.9f * Mathf.Max(0, Mathf.Sin(f * 1.3f)), 0, 0),
                InBed.Book => new Vector3(0.05f * Mathf.Sin(f * 0.8f), 0, 0),
                _ => new Vector3(0.04f * Mathf.Sin(f * 2.2f), 0, 0),
            };
        }

        /// <summary>The ring round the bed: yellow when its patient is selected, blue while the mouse is over them.</summary>
        private void Ring(Color? colour)
        {
            if (colour == _ringColour)
            {
                return;
            }

            _ringColour = colour;
            _ring?.QueueFree();
            _ring = null;
            if (colour is not { } c)
            {
                return;
            }

            var b = new MeshBatch();
            b.Ring(new Vector3(0, 0.03f, 0), 1.35f, c, Basis.FromScale(new Vector3(0.75f, 1, 1.75f)), 0.12f);
            _ring = new Node3D();
            _node.AddChild(_ring);
            b.AddTo(_ring, shadow: false);
        }

        private void Alert(Patient? patient)
        {
            AlertKind? kind = patient switch
            {
                { InArrest: true } => AlertKind.Crash,
                { Emergency: not null, EmergencyTreated: true } => AlertKind.Treated,
                { Emergency: not null } => AlertKind.Emergency,
                _ => null,
            };
            if (kind == _alert?.Kind)
            {
                return;
            }

            _alert?.QueueFree();
            _alert = kind is { } k ? Alert3D.Make(k, 1.45f, k == AlertKind.Crash ? 78 : 84) : null;
            if (_alert is not null)
            {
                _node.AddChild(_alert);
            }
        }
    }

    /// <summary>
    /// The lights of a scan: the amber "X-ray in use" lamp on the lead screen while the radiographer is behind it,
    /// a soft beam from the X-ray tube at the moment the picture is taken, and the CT ring glowing and pulsing
    /// while the patient is inside it.
    /// </summary>
    private sealed class ScanFx
    {
        private static readonly Color AmberOff = new("#7A5418");
        private static readonly Color AmberOn = new("#FFB300");

        private readonly CareSpace _room;
        private readonly bool _ct;
        private readonly Node3D? _lamp;
        private readonly MeshInstance3D? _beam;
        private readonly MeshInstance3D? _ring;

        private ScanFx(CareSpace room, bool ct, Node3D? lamp, MeshInstance3D? beam, MeshInstance3D? ring)
        {
            (_room, _ct, _lamp, _beam, _ring) = (room, ct, lamp, beam, ring);
        }

        public static ScanFx? Make(Node3D parent, CareSpace room, PlacedFurniture table, bool ct, HospitalSimulation sim)
        {
            Node3D? lamp = null;
            if (room.Furniture.FirstOrDefault(f => f.ItemId == "lead_screen") is { } screen && sim.Database.Furniture.TryGetValue("lead_screen", out var screenDef))
            {
                var top = Kit3D.M(screenDef.HeightM);
                var off = new MeshBatch();
                off.Ball(Kit3D.At(screen.CentreX, screen.CentreY, top + 0.07f), new Vector3(0.16f, 0.13f, 0.16f), AmberOff, small: true);
                off.Box(Kit3D.At(screen.CentreX, screen.CentreY, top + 0.01f), new Vector3(0.12f, 0.04f, 0.12f), new Color("#37474F"));
                off.AddTo(parent);
                var on = new MeshBatch();
                on.Ball(Kit3D.At(screen.CentreX, screen.CentreY, top + 0.07f), new Vector3(0.19f, 0.16f, 0.19f), AmberOn, small: true);
                on.Ball(Kit3D.At(screen.CentreX, screen.CentreY, top + 0.07f), new Vector3(0.42f, 0.36f, 0.42f), new Color(AmberOn, 0.28f), small: true);
                lamp = Glow(on, parent);
                lamp.Visible = false;
            }

            MeshInstance3D? beam = null;
            MeshInstance3D? ring = null;
            if (!ct)
            {
                // A soft cone of light from the tube's collimator straight down onto the patient.
                if (room.Furniture.FirstOrDefault(f => f.ItemId == "xray_tube") is { } tube && sim.Database.Furniture.TryGetValue("xray_tube", out var tubeDef))
                {
                    var h = Kit3D.M(tubeDef.HeightM);
                    // Over the patient's chest, where the picture is taken.
                    var (hx, hy) = (table.CentreX, table.CentreY - 0.7);
                    var top = h * 0.75f;
                    var bottom = Kit3D.H(23);
                    var b = new MeshBatch();
                    b.Cylinder(Kit3D.At(0, 0, (top + bottom) / 2), 0.62f, top - bottom, 0.5f, new Color("#CFEFFF"), cone: true);
                    b.Ball(Kit3D.At(0, 0, bottom + 0.05f), new Vector3(1.3f, 0.12f, 1.0f), new Color("#F4FCFF"));
                    b.Ball(Kit3D.At(0, 0, top), new Vector3(0.22f, 0.12f, 0.22f), new Color("#FFFDE7"));
                    beam = Glow(b, parent);
                    beam.Position = Kit3D.At(hx, hy);
                    beam.SetMeta("table", new Vector2((float)table.CentreX, (float)table.CentreY));
                    beam.Transparency = 0.3f;
                    beam.Visible = false;
                }
            }
            else if (room.Furniture.FirstOrDefault(f => f.ItemId == "ct_gantry") is { } gantry && sim.Database.Furniture.TryGetValue("ct_gantry", out var gantryDef))
            {
                var h = Kit3D.M(gantryDef.HeightM);
                var across = (float)gantry.Width;
                var bore = Math.Min(across, h) * 0.24f;
                var side = Math.Sign(table.CentreY - gantry.CentreY);
                var face = Kit3D.At(gantry.CentreX, gantry.CentreY + (side * ((gantry.Length / 2) + 0.03)), h * 0.55f);
                var b = new MeshBatch();
                b.Ring(Vector3.Zero, bore * 1.22f, new Color("#8FE3FF"), new Basis(Vector3.Right, Mathf.Pi / 2), 0.45f);
                b.Ring(Vector3.Zero, bore * 1.22f, new Color("#8FE3FF", 0.25f), new Basis(Vector3.Right, Mathf.Pi / 2) * Basis.FromScale(new Vector3(1.12f, 1.12f, 1.12f)), 1.2f);
                // The tube going round inside the ring: a bright spot and the one opposite it.
                b.Ball(new Vector3(bore * 1.22f, 0, 0), new Vector3(0.3f, 0.3f, 0.12f), new Color("#FFFFFF"), small: true);
                b.Ball(new Vector3(-bore * 1.22f, 0, 0), new Vector3(0.2f, 0.2f, 0.1f), new Color("#E1F8FF"), small: true);
                ring = Glow(b, parent);
                ring.Position = face;
                ring.Visible = false;
            }

            return lamp is null && beam is null && ring is null ? null : new ScanFx(room, ct, lamp, beam, ring);
        }

        private static MeshInstance3D Glow(MeshBatch b, Node3D parent)
        {
            var mesh = b.AddTo(parent, alpha: true, shadow: false)!;
            mesh.MaterialOverride = Kit3D.VertexColoursUnshadedAlpha;
            return mesh;
        }

        public void Sync(HospitalSimulation sim, double time)
        {
            var task = sim.Tasks.FirstOrDefault(t => t.Started && t.Kind == Simulation.Staff.TaskKind.Imaging && t.SpaceId == _room.Id
                && sim.FindPatient(t.PatientId) is { IsMoving: false } p && p.Tile == _room.PatientSpot);
            double? phase = task is null ? null : Activity3D.ScanPhase(time, task.Id);
            if (_lamp is not null)
            {
                _lamp.Visible = phase is >= 5.4 and < 9.2;
            }

            var exposing = phase is { } ph && Activity3D.Exposing(ph, _ct);
            if (_beam is not null)
            {
                _beam.Visible = exposing;
                if (exposing && task is not null && sim.FindPatient(task.PatientId) is { } on && _beam.GetMeta("table").AsVector2() is var t)
                {
                    var at = Procedure3D.Target(Procedure3D.For(on, sim).Care);
                    _beam.Position = Kit3D.At(t.X + at.X, t.Y + at.Y);
                    var narrow = at.X != 0 ? 0.55f : 1f;
                    _beam.Scale = new Vector3(narrow, 1, narrow);
                }
            }

            if (_ring is not null)
            {
                _ring.Visible = exposing;
                _ring.Transparency = exposing ? 0.2f * (0.5f + (0.5f * Mathf.Sin((float)time * 9))) : 0f;
                _ring.Rotation = new Vector3(0, 0, (float)time * 7f);
            }
        }
    }
}
