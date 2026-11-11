using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// All the furniture and equipment in the rooms on the floor in view, and every chair. The fixed
/// pieces are merged into two meshes (solid and glass) so a whole floor of furniture is two draw
/// calls; cots and incubators (with their babies) and the resus trolleys (pushed about) are kept up to date.
/// </summary>
public partial class Furniture3D : Node3D
{
    private View3D _view = null!;
    private readonly List<CotView> _cots = new();
    private readonly Dictionary<int, Node3D> _trolleys = new();
    private int _count;

    public void Initialise(View3D view) => _view = view;

    /// <summary>How many pieces were built (for tests).</summary>
    public int Count => _count;

    public void Rebuild()
    {
        foreach (var child in GetChildren())
        {
            if (child.Name.ToString().StartsWith("Trolley", StringComparison.Ordinal))
            {
                continue;
            }

            child.QueueFree();
        }

        _cots.Clear();
        _count = 0;
        var world = _view.World;
        var sim = _view.Simulation;
        var solid = new MeshBatch();
        var glass = new MeshBatch();
        foreach (var space in world.ViewSpaces)
        {
            foreach (var item in space.Furniture)
            {
                if (item.ItemId == Simulation.Simulation.HospitalSimulation.ResusTrolleyItem || !sim.Database.Furniture.TryGetValue(item.ItemId, out var def))
                {
                    continue;
                }

                if (def.Style == FurnitureStyle.Chair || (def.Style == FurnitureStyle.Bed && def.Id != "incubator"))
                {
                    continue;
                }

                var colour = new Color(def.Colour);
                var height = Kit3D.M(def.HeightM);
                var (x, y, w, l) = (item.X, item.Y, item.Width, item.Length);
                var seed = (int)Math.Abs((Math.Floor(x * 7) * 31) + (Math.Floor(y * 7) * 17));
                if (def.Id is "cot" or "incubator")
                {
                    _cots.Add(new CotView(this, space, item, def.Id == "incubator", height, colour));
                    _count++;
                    continue;
                }

                if (def.Style == FurnitureStyle.Monitor)
                {
                    Place(solid, glass, item.CentreX, item.CentreY, (0, 1), 0.4, 0.4);
                    Furnish3D.Build(solid, glass, "obs_monitor", 0.4f, 0.4f, height, colour, seed);
                    _count++;
                    continue;
                }

                if (def.Id == "examination_couch")
                {
                    // Lies along its length, head at the north or west end.
                    var alongY = l >= w;
                    var yaw = alongY ? 0 : Mathf.Pi / 2;
                    var (across, length) = alongY ? (w, l) : (l, w);
                    var frame = new Transform3D(new Basis(Vector3.Up, yaw) * Basis.FromScale(new Vector3((float)(across / 1.44), 1, (float)(length / 4.1))), Kit3D.At(x + (w / 2), y + (l / 2)));
                    solid.Frame = frame;
                    Carriers3D.Table(solid, "treatment_couch", colour);
                    _count++;
                    continue;
                }

                var front = FrontOf(space, item, def.Id, sim);
                if (def.Id == "vending_machine")
                {
                    seed = Math.Max(0, space.Furniture.Where(f => f.ItemId == def.Id).OrderBy(f => f.X).ThenBy(f => f.Y).ToList().IndexOf(item)) % 3;
                }

                var (fw, fd) = Place(solid, glass, x + (w / 2), y + (l / 2), front, w, l);
                if (!Furnish3D.Build(solid, glass, def.Id, fw, fd, height, colour, seed))
                {
                    Furnish3D.Plain(solid, fw, fd, height, colour);
                }

                _count++;
            }
        }

        foreach (var chair in world.Chairs)
        {
            var (cx, cy) = ChairArt.Centre(chair.Tile, chair.Facing, chair.Shift);
            var (fx, fy) = ChairArt.Direction(chair.Facing);
            Place(solid, glass, cx, cy, ((int)fx, (int)fy), 1, 1);
            Furnish3D.Chair(solid, chair.ItemId, chair.Colour);
            _count++;
        }

        solid.Frame = glass.Frame = Transform3D.Identity;
        solid.AddTo(this);
        if (glass.AddTo(this, alpha: true, shadow: false) is { } g)
        {
            g.Name = "Glass";
        }
    }

    public void Sync()
    {
        var world = _view.World;
        var sim = _view.Simulation;
        foreach (var cot in _cots)
        {
            cot.Sync(world, sim);
        }

        SyncTrolleys(world, sim);
    }

    /// <summary>Points the batch's frame at a footprint's middle, facing <paramref name="front"/>; returns its size across and front to back.</summary>
    private static (float W, float D) Place(MeshBatch solid, MeshBatch glass, double cx, double cy, (int X, int Y) front, double w, double l)
    {
        var frame = new Transform3D(new Basis(Vector3.Up, Kit3D.Yaw(front.X, front.Y)), Kit3D.At(cx, cy));
        solid.Frame = glass.Frame = frame;
        return front.X != 0 ? ((float)l, (float)w) : ((float)w, (float)l);
    }

    /// <summary>The way an item faces: toilets away from their wall (cubicles towards the door), café pieces away from the wall, the rest by the 2D art's rules.</summary>
    private static (int X, int Y) FrontOf(CareSpace space, PlacedFurniture item, string id, Simulation.Simulation.HospitalSimulation sim)
    {
        var (x, y, w, l) = (item.X, item.Y, item.Width, item.Length);
        if (id == "toilet_cubicle")
        {
            return space.DoorSide switch { WallSide.North => (0, -1), WallSide.South => (0, 1), WallSide.East => (1, 0), _ => (-1, 0) };
        }

        if (ToiletArt.Draws(id))
        {
            var r = space.Rect;
            var gaps = new[] { (Gap: y - r.Y, Dir: (0, 1)), (Gap: r.Bottom - (y + l), Dir: (0, -1)), (Gap: x - r.X, Dir: (1, 0)), (Gap: r.Right - (x + w), Dir: (-1, 0)) };
            return gaps.OrderBy(g => g.Gap).First().Dir;
        }

        if (CafeArt.Draws(id))
        {
            return CafeArt.FrontOf(space, x, y, w, l);
        }

        return FurnitureArt.FrontOf(space, item) switch
        {
            Sides.North => (0, -1),
            Sides.East => (1, 0),
            Sides.West => (-1, 0),
            _ => (0, 1),
        };
    }

    // ------------------------------------------------------------------ resus trolleys

    private void SyncTrolleys(WorldLayer world, Simulation.Simulation.HospitalSimulation sim)
    {
        var present = sim.ResusTrolleys.ToDictionary(t => t.Id);
        foreach (var gone in _trolleys.Keys.Where(id => !present.ContainsKey(id)).ToList())
        {
            _trolleys[gone].QueueFree();
            _trolleys.Remove(gone);
        }

        foreach (var trolley in present.Values)
        {
            if (!_trolleys.TryGetValue(trolley.Id, out var node))
            {
                var def = sim.Database.Furniture.GetValueOrDefault(Simulation.Simulation.HospitalSimulation.ResusTrolleyItem);
                var b = new MeshBatch();
                Furnish3D.ResusTrolley(
                    b,
                    (float)((def?.WidthM ?? 0.6) / Units.MetresPerTile),
                    (float)((def?.DepthM ?? 0.7) / Units.MetresPerTile),
                    Kit3D.M(def?.HeightM ?? 1.0),
                    new Color(def?.Colour ?? "#D84B3E"));
                node = new Node3D { Name = $"Trolley{trolley.Id}" };
                b.AddTo(node);
                AddChild(node);
                _trolleys[trolley.Id] = node;
            }

            var (x, y) = world.Interpolated(trolley);
            node.Visible = IsoDraw.OnViewFloor(y);
            node.Position = Kit3D.At(x, y);
            var (fx, fy) = trolley.Facing;
            var (sx, sy) = Math.Abs(fx) > Math.Abs(fy) ? (Math.Sign(fx), 0) : (0, fy < 0 ? -1 : 1);
            node.Rotation = new Vector3(0, Kit3D.Yaw(sx, sy), 0);
        }
    }

    // ------------------------------------------------------------------ cots and incubators

    /// <summary>A cot or incubator, with the baby sleeping in it when there is one.</summary>
    private sealed class CotView
    {
        private readonly CareSpace _space;
        private readonly bool _incubator;
        private readonly Node3D _node;
        private readonly float _mattress;
        private readonly WardBed? _bed;
        private readonly int _index;
        private Node3D? _baby;
        private BabyLook? _look;

        public CotView(Node3D parent, CareSpace space, PlacedFurniture item, bool incubator, float height, Color colour)
        {
            _space = space;
            _incubator = incubator;
            var (gx, gy) = (item.X + (item.Width / 2), item.Y + (item.Length / 2));
            var headSouth = false;
            if (incubator)
            {
                _index = space.Beds.Count == 0 ? -1 : Enumerable.Range(0, space.Beds.Count).MinBy(i => Math.Pow(space.Beds[i].PatientSpot.X - gx, 2) + Math.Pow(space.Beds[i].PatientSpot.Y - gy, 2));
                headSouth = _index >= 0 && space.Beds[_index].HeadSouth;
            }
            else if (space.Beds.Count > 0)
            {
                _bed = space.Beds.MinBy(b => Math.Pow(b.PatientSpot.X + 0.5 - gx, 2) + Math.Pow(b.PatientSpot.Y + 0.5 - gy, 2));
            }

            // Lie them lengthways: the long side runs along the footprint's longer side.
            var alongY = item.Length >= item.Width;
            var (w, d) = alongY ? (item.Width, item.Length) : (item.Length, item.Width);
            _node = new Node3D { Position = Kit3D.At(gx, gy), Rotation = new Vector3(0, (alongY ? 0 : Mathf.Pi / 2) + (headSouth ? Mathf.Pi : 0), 0) };
            parent.AddChild(_node);
            var solid = new MeshBatch();
            var glass = new MeshBatch();
            _mattress = incubator ? Furnish3D.Incubator(solid, glass, (float)w, (float)d, height, colour) : Furnish3D.Cot(solid, glass, (float)w, (float)d, height);
            solid.AddTo(_node);
            glass.AddTo(_node, alpha: true, shadow: false);
        }

        public void Sync(WorldLayer world, Simulation.Simulation.HospitalSimulation sim)
        {
            BabyLook? look = null;
            if (_incubator)
            {
                var baby = sim.Babies.FirstOrDefault(b => b.Place == BabyPlace.NeonatalUnit && b.UnitId == _space.Id && b.CotIndex == _index);
                look = baby is null ? null : Looks.Baby(baby, sim.FindPatient(baby.MotherId));
            }
            else if (_bed?.OccupantId is { } id && sim.IsMotherAndBabyWard(_space) && sim.FindPatient(id) is { } mother
                     && !mother.IsMoving && mother.Tile == _bed.PatientSpot && world.InBed(mother) && world.OnChest(mother) is null)
            {
                look = world.BabiesWith(mother).FirstOrDefault();
            }

            if (look == _look)
            {
                return;
            }

            _look = look;
            _baby?.QueueFree();
            _baby = null;
            if (look is null)
            {
                return;
            }

            _baby = Baby3D.Bundle(look, 0.9f);
            _baby.Position = new Vector3(0, _mattress + 0.08f, -0.05f);
            _baby.Rotation = new Vector3(-Mathf.Pi / 2, 0, 0);
            _node.AddChild(_baby);
        }
    }
}
