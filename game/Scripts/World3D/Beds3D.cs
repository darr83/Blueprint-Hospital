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
    public void Rebuild()
    {
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
                    _ when space.IsWard => space.Beds.FirstOrDefault(b => b.PatientSpot == item.Tile) is { } wardBed
                        ? new BedView(this, wardBed.PatientSpot, wardBed.HeadSouth, null, () => wardBed.OccupantId, sim.IsMotherAndBabyWard(space) ? Looks.MaternityBlanket : Looks.WardBlanket, true)
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
                _mattress = table is { } t ? Carriers3D.Table(frame, t.Id, t.Colour) : Carriers3D.Bed(frame, made: false);
            }

            frame.AddTo(_node);
            if (!_table)
            {
                // An empty bed is made up, with its blanket on.
                var made = new MeshBatch();
                Carriers3D.BedMade(made);
                _made = made.AddTo(_node);
            }
        }

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
            Ring(patient is not null && ReferenceEquals(world.Selected, patient));
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
            var key = (look, band, babies?.Length ?? 0, babies is { Length: > 0 } ? babies[0] : null, asleep, doing, cast);
            Breathe(asleep, time);
            MealTable(doing == InBed.Meal);
            Busy(time);
            if (_lying is not null && Equals(_lyingKey, key))
            {
                return;
            }

            _lying?.QueueFree();
            _lyingKey = key;
            _lying = Lying3D.Build(look, _blanket, band, babies, awake: !asleep, doing, cast);
            Snore(asleep);
            _lying.Position = new Vector3(0, _mattress, 0);
            _top.AddChild(_lying);
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
            if (_lying?.GetNodeOrNull<Node3D>("Holding") is not { } hands)
            {
                return;
            }

            var f = (float)time + (_spot.X * 0.7f);
            var doing = _lyingKey is ValueTuple<PersonLook, Color?, int, BabyLook?, bool, InBed, float> k ? k.Item6 : InBed.None;
            hands.Rotation = doing switch
            {
                InBed.Meal => new Vector3(-0.9f * Mathf.Max(0, Mathf.Sin(f * 1.3f)), 0, 0),
                InBed.Book => new Vector3(0.05f * Mathf.Sin(f * 0.8f), 0, 0),
                _ => new Vector3(0.04f * Mathf.Sin(f * 2.2f), 0, 0),
            };
        }

        private void Ring(bool selected)
        {
            if (selected == (_ring is not null))
            {
                return;
            }

            if (!selected)
            {
                _ring!.QueueFree();
                _ring = null;
                return;
            }

            var b = new MeshBatch();
            b.Ring(new Vector3(0, 0.03f, 0), 1.35f, SelectedRing, Basis.FromScale(new Vector3(0.75f, 1, 1.75f)), 0.12f);
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
                    b.Cylinder(Kit3D.At(hx, hy, (top + bottom) / 2), 0.62f, top - bottom, 0.5f, new Color("#CFEFFF"), cone: true);
                    b.Ball(Kit3D.At(hx, hy, bottom + 0.05f), new Vector3(1.3f, 0.12f, 1.0f), new Color("#F4FCFF"));
                    b.Ball(Kit3D.At(hx, hy, top), new Vector3(0.22f, 0.12f, 0.22f), new Color("#FFFDE7"));
                    beam = Glow(b, parent);
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
                b.Ring(face, bore * 1.22f, new Color("#8FE3FF"), new Basis(Vector3.Right, Mathf.Pi / 2), 0.45f);
                b.Ring(face, bore * 1.22f, new Color("#8FE3FF", 0.25f), new Basis(Vector3.Right, Mathf.Pi / 2) * Basis.FromScale(new Vector3(1.12f, 1.12f, 1.12f)), 1.2f);
                ring = Glow(b, parent);
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
            }

            if (_ring is not null)
            {
                _ring.Visible = exposing;
                _ring.Transparency = exposing ? 0.35f * (0.5f + (0.5f * Mathf.Sin((float)time * 9))) : 0f;
            }
        }
    }
}
