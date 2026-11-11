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
                    _ when space.IsServiceRoom => new BedView(this, space.PatientSpot, false, (def.Id, colour), null, space.Kind == CareSpaceKind.Theatre ? TheatreDrape : ScanSheet, false, SlideTowards(space, item, def.Id)),
                    _ when space.IsWard => space.Beds.FirstOrDefault(b => b.PatientSpot == item.Tile) is { } wardBed
                        ? new BedView(this, wardBed.PatientSpot, wardBed.HeadSouth, null, () => wardBed.OccupantId, sim.IsMotherAndBabyWard(space) ? Looks.MaternityBlanket : Looks.WardBlanket, true)
                        : null,
                    _ => new BedView(this, space.PatientSpot, false, null, () => space.OccupantId, Looks.EdBlanket, false),
                };
                if (view is not null)
                {
                    _beds.Add(view);
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

        foreach (var bed in _beds)
        {
            bed.Sync(_view.World, _view.Simulation, _time);
        }
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
                // The folded blanket at the foot of an empty bed.
                var made = new MeshBatch();
                made.Box(new Vector3(0, _mattress + Kit3D.H(1.5f), 1.45f), new Vector3(1.3f, Kit3D.H(3), 0.7f), new Color("#BFD3E6"));
                _made = made.AddTo(_node);
            }
        }

        public void Sync(WorldLayer world, HospitalSimulation sim, double time)
        {
            var patient = _table
                ? sim.Patients.FirstOrDefault(p => !p.IsMoving && p.Tile == _spot)
                : world.BedOccupant(_spot, _occupantId?.Invoke());
            if (_made is not null)
            {
                _made.Visible = patient is null;
            }

            Lying(world, patient);
            Slide(patient is not null && sim.Staff.Any(s => s.Role == Simulation.Staff.StaffRole.Radiographer && s.CurrentTaskId is { } id && sim.Tasks.Any(t => t.Id == id && t.Started && t.PatientId == patient.Id)), time);
            Ring(patient is not null && ReferenceEquals(world.Selected, patient));
            Alert(patient);
        }

        /// <summary>During a scan the table top glides in, holds while the scanner runs, and glides back out.</summary>
        private void Slide(bool scanning, double time)
        {
            if (_slide == 0)
            {
                return;
            }

            var into = 0f;
            if (scanning)
            {
                var cycle = (float)(time % 10.0);
                into = cycle switch
                {
                    < 2.5f => Smooth(cycle / 2.5f),
                    < 6.5f => 1f,
                    < 9f => 1f - Smooth((cycle - 6.5f) / 2.5f),
                    _ => 0f,
                };
            }

            _top.Position = new Vector3(0, 0, _slide * into);
        }

        private static float Smooth(float x) => x * x * (3 - (2 * x));

        private void Lying(WorldLayer world, Patient? patient)
        {
            if (patient is null)
            {
                _lying?.QueueFree();
                (_lying, _lyingKey) = (null, null);
                return;
            }

            var look = Looks.Patient(patient);
            var band = _table || _onWard || patient.Maternity ? null : Looks.Wristband(patient.Triage);
            var babies = world.OnChest(patient);
            var key = (look, band, babies?.Length ?? 0, babies is { Length: > 0 } ? babies[0] : null);
            if (_lying is not null && Equals(_lyingKey, key))
            {
                return;
            }

            _lying?.QueueFree();
            _lyingKey = key;
            _lying = Lying3D.Build(look, _blanket, band, babies);
            _lying.Position = new Vector3(0, _mattress, 0);
            _top.AddChild(_lying);
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
}
