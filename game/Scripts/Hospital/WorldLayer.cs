using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.Transport;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>Where an obs monitor stands, and the room whose patient it is connected to.</summary>
/// <summary>An obs monitor; on a ward (ICU, HDU) it shows the patient in the bed beside it.</summary>
public sealed record ObsMonitorInfo(CareSpace Space, double X, double Y, WardBed? Bed = null);

/// <summary>
/// Everything that stands up from the floor: walls, furniture, equipment, people and vehicles.
/// Each object is its own node positioned at its depth anchor, and Godot's Y-sort draws them
/// back to front, so people pass correctly behind and in front of walls, desks and beds.
/// </summary>
public partial class WorldLayer : Node2D
{
    /// <summary>Height of every wall on screen (2.5 m), level with the top of the door frames.</summary>
    public const float WallHeight = RoomSigns.FrameHeight;

    /// <summary>Wall thickness in tiles.</summary>
    public const double WallThickness = 0.3;

    private static readonly Color WallTop = new("#F4F6F8");
    private static readonly Color WallFace = new("#D9DFE5");

    private readonly Dictionary<Agent, AgentNode> _agentNodes = new();
    /// <summary>Every seat, and the way someone sitting on it looks.</summary>
    private readonly Dictionary<GridCoord, WallSide> _seatTiles = new();

    /// <summary>How far each placed chair stands off the middle of its tile, away from the walls beside it.</summary>
    private readonly Dictionary<GridCoord, (double X, double Y)> _chairShifts = new();
    private readonly List<ObsMonitorInfo> _monitors = new();

    /// <summary>Where a patient lies on each cubicle and ward bed.</summary>
    private readonly HashSet<GridCoord> _bedSpots = new();

    /// <summary>Mother and baby ward beds that have a cot beside them, by the bed's patient spot.</summary>
    private readonly HashSet<GridCoord> _cotBeds = new();
    private readonly List<Node> _staticNodes = new();
    private IDisposable? _layoutSubscription;
    private HospitalSimulation _sim = null!;
    private SimulationClock _clock = null!;

    public HospitalSimulation Simulation => _sim;

    public SimulationClock Clock => _clock;

    /// <summary>Seconds since the scene started, for animation.</summary>
    public double Time { get; private set; }

    /// <summary>The patient or staff member the player has selected, if any.</summary>
    public Agent? Selected { get; set; }

    public void Initialise(HospitalSimulation simulation, SimulationClock clock)
    {
        _sim = simulation;
        _clock = clock;
        YSortEnabled = true;
        _layoutSubscription = _sim.Events.Subscribe<LayoutChanged>(_ => _layoutDirty = true);
        RebuildStatic();
    }

    private bool _layoutDirty;

    public override void _ExitTree() => _layoutSubscription?.Dispose();

    /// <summary>Rebuilds walls and furniture, after rooms are built, moved or demolished.</summary>
    public void RebuildStatic()
    {
        foreach (var node in _staticNodes)
        {
            node.QueueFree();
        }

        _staticNodes.Clear();
        _monitors.Clear();
        _bedSpots.Clear();
        _cotBeds.Clear();
        _seatTiles.Clear();
        _chairShifts.Clear();

        foreach (var space in _sim.Spaces)
        {
            foreach (var seat in space.Seats)
            {
                _seatTiles[seat] = WallSide.South;
            }

            foreach (var item in space.Furniture)
            {
                if (_sim.Database.Furniture.TryGetValue(item.ItemId, out var def) && def.Style == FurnitureStyle.Chair)
                {
                    _seatTiles[item.Tile] = RoomChairFacing(space, item);
                }
            }
        }

        foreach (var chair in _sim.Chairs)
        {
            _seatTiles[chair.Tile] = chair.Facing;
            _chairShifts[chair.Tile] = _sim.ChairShift(chair.Tile);
        }

        BuildWalls();
        BuildFurniture();
        foreach (var (anchor, draw) in Decorations.Plan(_sim))
        {
            AddProp(anchor, draw);
        }

        _layoutDirty = false;
    }

    public override void _Process(double delta)
    {
        Time += delta;
        if (_layoutDirty)
        {
            RebuildStatic();
        }

        SyncAgents();
    }

    // ------------------------------------------------------------------ people and vehicles

    /// <summary>World position of an agent's feet, interpolated between simulation ticks.</summary>
    public Vector2 FootPosition(Agent agent)
    {
        var (x, y) = Interpolated(agent);
        if (IsSeated(agent))
        {
            // Sitting on a chair, which stands a little forward of the middle of its tile.
            var (cx, cy) = ChairArt.Centre(agent.Tile, SeatFacing(agent), _chairShifts.TryGetValue(agent.Tile, out var shift) ? shift : null);
            (x, y) = (x + (cx - (agent.Tile.X + 0.5)), y + (cy - (agent.Tile.Y + 0.5)));
        }

        return IsoDraw.ToScreen(x, y);
    }

    public (double X, double Y) Interpolated(Agent agent)
    {
        // Just stepped out of the lift onto another floor: no sliding across from the floor below.
        if (IsoDraw.FloorOf(agent.PreviousY) != IsoDraw.FloorOf(agent.Y))
        {
            return (agent.X, agent.Y);
        }

        var t = _clock.TickProgress;
        return (agent.PreviousX + ((agent.X - agent.PreviousX) * t), agent.PreviousY + ((agent.Y - agent.PreviousY) * t));
    }

    /// <summary>True if the agent is on the floor in view (ambulances are always on the ground).</summary>
    public bool OnViewFloor(Agent agent) => IsoDraw.OnViewFloor(agent.Y);

    /// <summary>The rooms on the floor in view.</summary>
    private IEnumerable<CareSpace> ViewSpaces => _sim.Spaces.Where(s => IsoDraw.OnViewFloor(s.Rect.Y));

    /// <summary>The patient or staff member under a world-space point, nearest the viewer first.</summary>
    public Agent? HitTest(Vector2 world)
    {
        Agent? best = null;
        var bestDepth = float.MinValue;
        foreach (var (agent, node) in _agentNodes)
        {
            if (agent is AmbulanceVehicle || !node.Visible)
            {
                continue;
            }

            var foot = FootPosition(agent);
            var box = agent is Patient p && IsLying(p)
                ? new Rect2(foot.X - 36, foot.Y - 60, 72, 74)
                : new Rect2(foot.X - 14, foot.Y - 62, 28, 66);
            if (box.HasPoint(world) && foot.Y > bestDepth)
            {
                best = agent;
                bestDepth = foot.Y;
            }
        }

        return best;
    }

    /// <summary>On a trolley away from a bed: being pushed, waiting for a porter, or parked in corridor care.</summary>
    public bool OnTrolley(Patient patient) =>
        patient.Mobility == Mobility.Trolley && patient.IsAlive && !InBed(patient) && !IsSeated(patient)
        && (patient.IsMoving || patient.PendingMove is not null || patient.PushedByStaffId is not null || InCorridorCare(patient));

    /// <summary>Sitting in a wheelchair: being pushed, waiting for a porter, or parked in corridor care.</summary>
    public bool InWheelchair(Patient patient) =>
        patient.Mobility == Mobility.Wheelchair && !IsSeated(patient)
        && (patient.IsMoving || patient.PendingMove is not null || patient.PushedByStaffId is not null || InCorridorCare(patient));

    /// <summary>Still on the ambulance crew's stretcher at the bay, waiting for handover (or to be wheeled off it).</summary>
    public bool OnAmbulanceStretcher(Patient patient) =>
        patient.Stage == PatientStage.InAmbulanceQueue && !patient.IsMoving
        && (patient.CorridorSpot is null || _sim.AmbulanceBayPatientTiles.Contains(patient.Tile));

    /// <summary>Parked on a trolley (or in a wheelchair) in a corridor space, waiting for a cubicle.</summary>
    public static bool InCorridorCare(Patient patient) => patient.CorridorSpot is { } spot && patient.Tile == spot && !patient.IsMoving;

    /// <summary>Lying on a bed in a treatment space, or on an ambulance trolley.</summary>
    public bool IsLying(Patient patient)
    {
        if (patient.IsMoving)
        {
            return OnTrolley(patient);
        }

        return OnAmbulanceStretcher(patient) || InBed(patient) || OnTrolley(patient);
    }

    /// <summary>Lying on the bed of a treatment space or a ward.</summary>
    public bool InBed(Patient patient)
    {
        if (patient.IsMoving)
        {
            return false;
        }

        // On an X-ray, CT or operating table.
        if (_sim.Spaces.Any(s => s.IsServiceRoom && s.Kind != CareSpaceKind.Pharmacy && s.PatientSpot == patient.Tile))
        {
            return true;
        }

        // Still lying on a bed they have been moved out of, waiting for a porter to wheel them away:
        // they stay in that bed rather than appearing on a trolley on top of it.
        if (_bedSpots.Contains(patient.Tile) && !IsSeated(patient))
        {
            return true;
        }

        if (patient.CareSpaceId is not { } id || _sim.FindSpace(id) is not { } space)
        {
            return false;
        }

        return space.IsTreatmentSpace ? patient.Tile == space.PatientSpot : _sim.BedOf(patient) is { } bed && patient.Tile == bed.PatientSpot;
    }

    public bool IsSeated(Agent agent) => !agent.IsMoving && _seatTiles.ContainsKey(agent.Tile);

    /// <summary>The way someone sitting where <paramref name="agent"/> is looks.</summary>
    public WallSide SeatFacing(Agent agent) => _seatTiles.GetValueOrDefault(agent.Tile, WallSide.South);

    /// <summary>Patients still on an incoming ambulance are not on the map yet.</summary>
    public bool IsOnMap(Patient patient) =>
        patient.Stage != PatientStage.Gone
        && !_sim.Ambulances.Any(a => a.PatientId == patient.Id && a.Purpose == AmbulancePurpose.Arrival && a.State is AmbulanceState.Driving or AmbulanceState.WaitingForBay);

    private void SyncAgents()
    {
        var present = new HashSet<Agent>();
        foreach (var a in _sim.Ambulances)
        {
            present.Add(a);
        }

        foreach (var p in _sim.Patients)
        {
            present.Add(p);
        }

        // Off-duty staff are at home, so they aren't drawn.
        foreach (var s in _sim.Staff.Where(s => s.IsInBuilding))
        {
            present.Add(s);
        }

        foreach (var o in _sim.Outpatients)
        {
            present.Add(o);
        }

        foreach (var gone in _agentNodes.Keys.Where(a => !present.Contains(a)).ToList())
        {
            _agentNodes[gone].QueueFree();
            _agentNodes.Remove(gone);
        }

        foreach (var agent in present)
        {
            if (!_agentNodes.TryGetValue(agent, out var node))
            {
                node = new AgentNode(this, agent);
                _agentNodes[agent] = node;
                AddChild(node);
            }

            node.Sync();
        }
    }

    // ------------------------------------------------------------------ walls

    private void BuildWalls()
    {
        // Collect wall edges from the building shell and every walled room. An edge is a wall
        // if anything wants a wall there and nothing wants a doorway there.
        var walls = new HashSet<(int X, int Y, bool Horizontal)>();
        var doors = new HashSet<(int X, int Y, bool Horizontal)>();

        void Perimeter(GridRect r, Func<GridCoord, WallSide, bool> isOpening)
        {
            for (var x = r.X; x < r.Right; x++)
            {
                (isOpening(new GridCoord(x, r.Y), WallSide.North) ? doors : walls).Add((x, r.Y, true));
                (isOpening(new GridCoord(x, r.Bottom - 1), WallSide.South) ? doors : walls).Add((x, r.Bottom, true));
            }

            for (var y = r.Y; y < r.Bottom; y++)
            {
                (isOpening(new GridCoord(r.X, y), WallSide.West) ? doors : walls).Add((r.X, y, false));
                (isOpening(new GridCoord(r.Right - 1, y), WallSide.East) ? doors : walls).Add((r.Right, y, false));
            }
        }

        // The outer walls of the floor in view; only the ground floor has entrances.
        var up = IsoDraw.ViewFloor * _sim.Site.FloorStride;
        foreach (var building in _sim.Site.Buildings)
        {
            foreach (var (inside, _, side) in building.OuterEdges())
            {
                var edge = side switch
                {
                    WallSide.North => (inside.X, inside.Y + up, true),
                    WallSide.South => (inside.X, inside.Y + 1 + up, true),
                    WallSide.West => (inside.X, inside.Y + up, false),
                    _ => (inside.X + 1, inside.Y + up, false),
                };
                // A room built over an entrance (a waiting room by the front path) closes it unless it has a door there.
                var open = up == 0 && building.IsOpening(inside, side)
                    && _sim.Spaces.Where(s => s.HasWalls && s.Rect.Contains(inside)).All(s => s.IsDoor(inside, side));
                (open ? doors : walls).Add(edge);
            }
        }

        foreach (var space in ViewSpaces.Where(s => s.HasWalls))
        {
            Perimeter(space.Rect, space.IsDoor);
        }

        // Every wall is cut into one-tile pieces that sort on their own, joined by a square post at
        // every grid point a wall touches. Faces that meet a neighbour are left out, so straight
        // runs, corners, T-junctions and crossings all join seamlessly from all four view angles.
        var solid = walls.Where(w => !doors.Contains(w)).ToHashSet();
        var joints = new Dictionary<(int X, int Y), Sides>();
        void Join((int X, int Y) at, Sides side) => joints[at] = joints.GetValueOrDefault(at) | side;
        foreach (var (x, y, horizontal) in solid)
        {
            if (horizontal)
            {
                Join((x, y), Sides.East);
                Join((x + 1, y), Sides.West);
            }
            else
            {
                Join((x, y), Sides.South);
                Join((x, y + 1), Sides.North);
            }
        }

        var doorPoints = new HashSet<(int X, int Y)>();
        foreach (var (x, y, horizontal) in doors.Where(d => !solid.Contains(d)))
        {
            doorPoints.Add((x, y));
            doorPoints.Add(horizontal ? (x + 1, y) : (x, y + 1));
        }

        const double h = WallThickness / 2;
        const double span = 1 - WallThickness;

        // Which walled room each tile is in, so each face of a wall takes the finish of the room it looks into.
        var roomAt = new Dictionary<GridCoord, CareSpace>();
        foreach (var space in ViewSpaces.Where(s => s.HasWalls).OrderByDescending(s => s.Rect.Area))
        {
            var r = space.Rect;
            for (var tx = r.X; tx < r.Right; tx++)
            {
                for (var ty = r.Y; ty < r.Bottom; ty++)
                {
                    roomAt[new GridCoord(tx, ty)] = space;
                }
            }
        }

        WallPaint?[] Paint(double px, double py, double pw, double pl)
        {
            var faces = new WallPaint?[4];
            var (cx, cy) = (px + (pw / 2), py + (pl / 2));
            var outward = new[] { (cx, py - 0.4), (px + pw + 0.4, cy), (cx, py + pl + 0.4), (px - 0.4, cy) };
            for (var i = 0; i < 4; i++)
            {
                var tile = new GridCoord((int)Math.Floor(outward[i].Item1), (int)Math.Floor(outward[i].Item2));
                if (roomAt.TryGetValue(tile, out var room) && DecorArt.LookOf(_sim, room) is { Walls: not WallFinish.Standard } look)
                {
                    faces[i] = new WallPaint(look.Walls, DecorArt.WallColour(look), DecorArt.WallAccent(room, look));
                }
            }

            return faces;
        }

        foreach (var (x, y, horizontal) in solid)
        {
            if (horizontal)
            {
                var paint = Paint(x + h, y - h, span, WallThickness);
                AddProp(FrontFace(x + h, y - h, span, WallThickness), c => Wall(c, x + h, y - h, span, WallThickness, Sides.East | Sides.West, paint: paint));
            }
            else
            {
                var paint = Paint(x - h, y + h, WallThickness, span);
                AddProp(FrontFace(x - h, y + h, WallThickness, span), c => Wall(c, x - h, y + h, WallThickness, span, Sides.North | Sides.South, paint: paint));
            }
        }

        foreach (var ((x, y), joined) in joints)
        {
            // Posts beside a doorway are its frame, in a darker trim.
            var frame = doorPoints.Contains((x, y));
            var paint = frame ? null : Paint(x - h, y - h, WallThickness, WallThickness);
            AddProp(FrontFace(x - h, y - h, WallThickness, WallThickness), c => Wall(c, x - h, y - h, WallThickness, WallThickness, joined, frame, paint));
        }

        // Each room's doorway: frame, doors and name board.
        foreach (var space in ViewSpaces.Where(RoomSigns.Named))
        {
            var (fx, fy, fw, fl) = RoomSigns.SignFootprint(space);
            var (mx, my) = RoomSigns.DoorMiddle(space);
            AddProp(FrontFace(fx, fy, fw, fl), c => RoomSigns.Doorway(c, space, DoorOpenness(space, mx, my), WallThickness, WallHeight, WallTop, WallFace, FrameTop, FrameFace), animated: true);
        }
    }

    private readonly Dictionary<CareSpace, (float Open, double At)> _doors = new();

    /// <summary>How far a room's door stands open: it swings open as someone comes near and closes behind them.</summary>
    private float DoorOpenness(CareSpace space, double mx, double my)
    {
        static bool Near(Agent a, double x, double y) => Math.Abs(a.X - x) < 1.6 && Math.Abs(a.Y - y) < 1.6;
        var near = _sim.Patients.Any(p => Near(p, mx, my)) || _sim.Staff.Any(s => Near(s, mx, my));
        var (open, at) = _doors.GetValueOrDefault(space, (0f, Time));
        var step = (float)Math.Clamp((Time - at) * 4, 0, 1);
        open = Math.Clamp(open + (near ? step : -step), 0, 1);
        _doors[space] = (open, Time);
        return open;
    }

    private static readonly Color WallSkirting = new("#9FB0BE");
    private static readonly Color WallRim = new("#C3CDD6");
    private static readonly Color FrameFace = new("#A9B7C3");
    private static readonly Color FrameTop = new("#C7D2DB");

    /// <summary>A decorated wall face: its finish, colour and second colour.</summary>
    private sealed record WallPaint(WallFinish Finish, Color Colour, Color? Accent);

    private static void Wall(CanvasItem c, double x, double y, double w, double l, Sides joined, bool frame = false, WallPaint?[]? paint = null)
    {
        IsoDraw.Prism(c, x, y, w, l, WallHeight, frame ? FrameTop : WallTop, frame ? FrameFace : WallFace, joined, skirting: frame ? FrameFace.Darkened(0.15f) : WallSkirting, rim: WallRim);
        if (paint is null)
        {
            return;
        }

        foreach (var (a, b, side, left) in IsoDraw.VisibleFaces(x, y, w, l))
        {
            var index = side switch { Sides.North => 0, Sides.East => 1, Sides.South => 2, _ => 3 };
            if ((joined & side) == 0 && paint[index] is { } face)
            {
                DecorArt.DrawWallFace(c, a, b, WallHeight, left ? 0f : 0.1f, face.Finish, face.Colour, face.Accent, WallSkirting);
            }
        }
    }

    /// <summary>The middle of whichever side of a footprint is nearest the viewer, so thin pieces sort right from every angle.</summary>
    private static Vector2 FrontFace(double x, double y, double w, double l)
    {
        var sides = new[]
        {
            IsoDraw.ToScreen(x + (w / 2), y), IsoDraw.ToScreen(x + (w / 2), y + l),
            IsoDraw.ToScreen(x, y + (l / 2)), IsoDraw.ToScreen(x + w, y + (l / 2)),
        };
        return sides.MaxBy(p => p.Y);
    }

    // ------------------------------------------------------------------ furniture

    private void BuildFurniture()
    {
        foreach (var space in ViewSpaces)
        {
            foreach (var item in space.Furniture)
            {
                if (!_sim.Database.Furniture.TryGetValue(item.ItemId, out var def))
                {
                    continue;
                }

                var colour = new Color(def.Colour);
                var height = (float)(def.HeightM * IsoDraw.PixelsPerMetre);
                switch (def.Style)
                {
                    case FurnitureStyle.Box when def.Id == "cot":
                        AddCot(space, item, height);
                        break;
                    case FurnitureStyle.Bed when def.Id == "incubator":
                        AddIncubator(space, item, height, colour);
                        break;
                    case FurnitureStyle.Bed when def.Id == "birthing_bed":
                        AddBed(space.PatientSpot, false, () => space.OccupantId, blanket: Looks.MaternityBlanket);
                        break;
                    case FurnitureStyle.Bed when space.IsServiceRoom:
                        AddTable(space, def.Id, colour);
                        break;
                    case FurnitureStyle.Bed when space.IsWard:
                        if (space.Beds.FirstOrDefault(b => b.PatientSpot == item.Tile) is { } wardBed)
                        {
                            AddBed(wardBed.PatientSpot, wardBed.HeadSouth, () => wardBed.OccupantId, onWard: true, blanket: _sim.IsMotherAndBabyWard(space) ? Looks.MaternityBlanket : null);
                        }

                        break;
                    case FurnitureStyle.Bed:
                        AddBed(space.PatientSpot, false, () => space.OccupantId);
                        break;
                    case FurnitureStyle.Monitor:
                        AddObsMonitor(space, item.CentreX, item.CentreY);
                        break;
                    case FurnitureStyle.Chair:
                        Chair(item.Tile, RoomChairFacing(space, item), colour);
                        break;
                    case FurnitureStyle.Box when ToiletArt.Draws(def.Id):
                        ToiletArt.Add(def.Id, space, item, colour, _sim, (anchor, draw, animated) => AddProp(anchor, draw, animated));
                        break;
                    case FurnitureStyle.Box when CafeArt.Draws(def.Id):
                        CafeArt.Add(def.Id, space, item, (float)(def.HeightM * IsoDraw.PixelsPerMetre), colour, (anchor, draw) => AddProp(anchor, draw));
                        break;
                    default:
                        Block(item.ItemId, item.X, item.Y, item.Width, item.Length, (float)(def.HeightM * IsoDraw.PixelsPerMetre), colour, FurnitureArt.FrontOf(space, item));
                        break;
                }
            }
        }

        foreach (var chair in _sim.Chairs.Where(c => IsoDraw.OnViewFloor(c.Tile.Y)))
        {
            Chair(chair.Tile, chair.Facing, new Color(chair.Colour), _chairShifts.GetValueOrDefault(chair.Tile));
        }
    }

    private void AddBed(GridCoord spot, bool headSouth, Func<int?> occupantId, bool onWard = false, Color? blanket = null)
    {
        var (cx, cy) = (spot.X + 0.5, spot.Y + 0.5);
        _bedSpots.Add(spot);

        // Bed and whoever is lying on it are one object, anchored at the middle of the bed.
        AddProp(IsoDraw.ToScreen(cx, cy), c =>
        {
            // Usually the bed's own patient; otherwise whoever is still lying here waiting to be moved.
            var occupant = occupantId() is { } id ? _sim.FindPatient(id) : null;
            if (occupant is null || occupant.Tile != spot || !InBed(occupant))
            {
                occupant = _sim.Patients.FirstOrDefault(p => p.Tile == spot && InBed(p));
            }

            var lying = occupant is not null;
            if (lying && ReferenceEquals(Selected, occupant))
            {
                var ring = IsoDraw.FloorEllipse(IsoDraw.ToScreen(cx, cy), 46, 32);
                c.DrawPolyline(ring.Append(ring[0]).ToArray(), new Color("#FFEB3B"), 3f, true);
            }

            Figures.Bed(c, cx, cy, made: !lying, headSouth, lift =>
            {
                if (lying)
                {
                    var cover = blanket ?? (onWard ? Looks.WardBlanket : Looks.EdBlanket);
                    var band = onWard || occupant!.Maternity ? null : Looks.Wristband(occupant!.Triage);
                    Figures.Lying(c, cx, cy, Looks.Patient(occupant!), cover, lift, headSouth, band, OnChest(occupant!));
                }
            });
        }, animated: true);
    }

    /// <summary>An imaging or operating table, with the patient lying on it during a scan or an operation.</summary>
    private void AddTable(CareSpace space, string itemId, Color colour)
    {
        var spot = space.PatientSpot;
        var (cx, cy) = (spot.X + 0.5, spot.Y + 0.5);
        var cover = space.Kind == CareSpaceKind.Theatre ? new Color("#4E9C8F") : new Color("#CFE0EE");
        AddProp(IsoDraw.ToScreen(cx, cy), c =>
        {
            var patient = _sim.Patients.FirstOrDefault(p => !p.IsMoving && p.Tile == spot);
            if (patient is not null && ReferenceEquals(Selected, patient))
            {
                var ring = IsoDraw.FloorEllipse(IsoDraw.ToScreen(cx, cy), 46, 32);
                c.DrawPolyline(ring.Append(ring[0]).ToArray(), new Color("#FFEB3B"), 3f, true);
            }

            var lift = TableArt.Draw(c, itemId, cx, cy, colour);
            if (patient is not null)
            {
                Figures.Lying(c, cx, cy, Looks.Patient(patient), cover, lift, false, onChest: OnChest(patient));
            }
        }, animated: true);
    }

    // ------------------------------------------------------------------ babies

    /// <summary>A mother's babies who are with her (not on the neonatal unit or being carried).</summary>
    public BabyLook[] BabiesWith(Patient mother)
    {
        if (!mother.Maternity || !mother.Delivered)
        {
            return Array.Empty<BabyLook>();
        }

        return _sim.Babies.Where(b => b.MotherId == mother.Id && b.Place == BabyPlace.WithMother).Select(b => Looks.Baby(b, mother)).ToArray();
    }

    /// <summary>True when mum is in a mother and baby ward bed with a cot beside it, so her baby sleeps in the cot.</summary>
    private bool BabyInCot(Patient mother) =>
        !mother.IsMoving && _sim.BedOf(mother) is { } bed && mother.Tile == bed.PatientSpot && _cotBeds.Contains(bed.PatientSpot) && InBed(mother);

    /// <summary>Babies lying on mum's chest: skin to skin after the birth, or anywhere she is lying without a cot beside her.</summary>
    private BabyLook[]? OnChest(Patient mother) => BabyInCot(mother) ? null : BabiesWith(mother);

    /// <summary>The baby mum carries in her arms when she is up and about.</summary>
    private BabyLook? InArms(Patient mother) => BabiesWith(mother) is { Length: > 0 } babies ? babies[0] : null;

    /// <summary>The baby a nurse is carrying to or from the neonatal unit.</summary>
    private BabyLook? CarriedBy(StaffMember member)
    {
        var baby = _sim.Babies.FirstOrDefault(b => b.Place == BabyPlace.Carried && b.CarriedByStaffId == member.Id);
        return baby is null ? null : Looks.Baby(baby, _sim.FindPatient(baby.MotherId));
    }

    /// <summary>A cot: on a mother and baby ward it belongs to the nearest bed, and that mum's baby sleeps in it while she is in bed.</summary>
    private void AddCot(CareSpace space, PlacedFurniture item, float height)
    {
        var whole = (item.X, item.Y, item.Width, item.Length);
        var (gx, gy) = (item.X + (item.Width / 2), item.Y + (item.Length / 2));
        var bed = _sim.IsMotherAndBabyWard(space) && space.Beds.Count > 0
            ? space.Beds.MinBy(b => Math.Pow(b.PatientSpot.X + 0.5 - gx, 2) + Math.Pow(b.PatientSpot.Y + 0.5 - gy, 2))
            : null;
        if (bed is not null)
        {
            _cotBeds.Add(bed.PatientSpot);
        }

        AddProp(IsoDraw.ToScreen(gx, gy), c =>
        {
            var babies = Array.Empty<BabyLook>();
            if (bed?.OccupantId is { } id && _sim.FindPatient(id) is { } mother && BabyInCot(mother))
            {
                babies = BabiesWith(mother);
            }

            MaternityArt.Cot(c, whole, height, babies);
        }, animated: bed is not null);
    }

    /// <summary>An incubator on the neonatal unit, with its baby.</summary>
    private void AddIncubator(CareSpace space, PlacedFurniture item, float height, Color colour)
    {
        var whole = (item.X, item.Y, item.Width, item.Length);
        var (gx, gy) = (item.X + (item.Width / 2), item.Y + (item.Length / 2));
        var index = space.Beds.Count == 0 ? -1 : Enumerable.Range(0, space.Beds.Count).MinBy(i => Math.Pow(space.Beds[i].PatientSpot.X - gx, 2) + Math.Pow(space.Beds[i].PatientSpot.Y - gy, 2));
        var headSouth = index >= 0 && space.Beds[index].HeadSouth;
        AddProp(IsoDraw.ToScreen(gx, gy), c =>
        {
            var baby = _sim.Babies.FirstOrDefault(b => b.Place == BabyPlace.NeonatalUnit && b.UnitId == space.Id && b.CotIndex == index);
            var look = baby is null ? null : Looks.Baby(baby, _sim.FindPatient(baby.MotherId));
            MaternityArt.Incubator(c, whole, height, colour, look, headSouth, Time);
        }, animated: true);
    }

    private void AddObsMonitor(CareSpace space, double gx, double gy)
    {
        var bed = space.Beds.Count == 0 ? null : space.Beds.MinBy(b => Math.Pow(b.PatientSpot.X + 0.5 - gx, 2) + Math.Pow(b.PatientSpot.Y + 0.5 - gy, 2));
        _monitors.Add(new ObsMonitorInfo(space, gx, gy, bed));
        AddProp(IsoDraw.ToScreen(gx, gy), c => Figures.MonitorStand(c, gx, gy));
    }

    /// <summary>Every obs monitor's position and room; their screens are drawn above the world so they are never hidden.</summary>
    public IReadOnlyList<ObsMonitorInfo> Monitors => _monitors;

    /// <summary>The patient connected to a monitor: whoever is on the bed (or in the triage chair).</summary>
    /// <summary>The patient a monitor is showing: the one in its bed on a ward, otherwise the room's patient.</summary>
    public Patient? ConnectedPatient(ObsMonitorInfo monitor)
    {
        if (monitor.Bed is not { } bed)
        {
            return ConnectedPatient(monitor.Space);
        }

        var patient = bed.OccupantId is { } id ? _sim.FindPatient(id) : null;
        return patient is not null && !patient.IsMoving && patient.Tile == bed.PatientSpot ? patient : null;
    }

    public Patient? ConnectedPatient(CareSpace space)
    {
        var patient = space.Kind == CareSpaceKind.Triage
            ? _sim.Patients.FirstOrDefault(p => !p.IsMoving && p.Tile == space.PatientSpot)
            : space.OccupantId is { } id ? _sim.FindPatient(id) : null;
        return patient is not null && !patient.IsMoving && patient.Tile == space.PatientSpot ? patient : null;
    }

    /// <summary>A chair in two parts, so whoever sits on it sorts between the seat and the back.</summary>
    private void Chair(GridCoord tile, WallSide facing, Color colour, (double X, double Y)? shift = null)
    {
        AddProp(ChairArt.SeatAnchor(tile, facing, shift), c => ChairArt.Seat(c, tile, facing, colour, shift));
        AddProp(ChairArt.BackAnchor(tile, facing, shift), c => ChairArt.Back(c, tile, facing, colour, shift));
    }

    /// <summary>A chair in a room faces the desk or table beside it, otherwise south.</summary>
    private WallSide RoomChairFacing(CareSpace space, PlacedFurniture chair)
    {
        var (cx, cy) = (chair.Tile.X + 0.5, chair.Tile.Y + 0.5);
        PlacedFurniture? best = null;
        var bestGap = 0.75;
        foreach (var item in space.Furniture)
        {
            if (ReferenceEquals(item, chair) || !_sim.Database.Furniture.TryGetValue(item.ItemId, out var def) || def.Style == FurnitureStyle.Chair)
            {
                continue;
            }

            var gx = Math.Max(0, Math.Max(item.X - cx, cx - (item.X + item.Width)));
            var gy = Math.Max(0, Math.Max(item.Y - cy, cy - (item.Y + item.Length)));
            var gap = Math.Max(gx, gy);
            if (gap < bestGap)
            {
                (best, bestGap) = (item, gap);
            }
        }

        if (best is null)
        {
            return WallSide.South;
        }

        var (dx, dy) = (best.CentreX - cx, best.CentreY - cy);
        return Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? WallSide.East : WallSide.West) : (dy > 0 ? WallSide.South : WallSide.North);
    }

    /// <summary>A piece of furniture, split into one-tile slices along its length so each slice sorts correctly.</summary>
    private void Block(string itemId, double x, double y, double w, double l, float height, Color colour, Sides front = Sides.None)
    {
        var alongX = w >= l;
        var length = alongX ? w : l;
        var slices = Math.Max(1, (int)Math.Ceiling(length));
        var step = length / slices;
        for (var i = 0; i < slices; i++)
        {
            var (sx, sy, sw, sl) = alongX ? (x + (i * step), y, step, l) : (x, y + (i * step), w, step);
            var joined = Sides.None;
            if (i > 0)
            {
                joined |= alongX ? Sides.West : Sides.North;
            }

            if (i < slices - 1)
            {
                joined |= alongX ? Sides.East : Sides.South;
            }

            AddProp(IsoDraw.ToScreen(sx + (sw / 2), sy + (sl / 2)), c => FurnitureArt.Slice(c, itemId, sx, sy, sw, sl, (x, y, w, l), height, colour, joined, front));
        }
    }

    private void AddProp(Vector2 anchor, Action<CanvasItem> draw, bool animated = false)
    {
        var node = new PropNode(draw, animated) { Position = anchor };
        _staticNodes.Add(node);
        AddChild(node);
    }

    /// <summary>A static or animated object drawn in world coordinates and sorted by its anchor.</summary>
    private sealed partial class PropNode(Action<CanvasItem> draw, bool animated) : Node2D
    {
        public override void _Ready() => SetProcess(animated);

        public override void _Process(double delta) => QueueRedraw();

        public override void _Draw()
        {
            DrawSetTransform(-Position);
            draw(this);
        }
    }

    /// <summary>A patient, member of staff or ambulance, kept at its interpolated position.</summary>
    private sealed partial class AgentNode(WorldLayer world, Agent agent) : Node2D
    {
        public override void _Ready()
        {
            // The ambulance is a baked sprite; mipmaps keep it smooth when the camera zooms out.
            if (agent is AmbulanceVehicle)
            {
                TextureFilter = TextureFilterEnum.LinearWithMipmaps;
            }
        }

        public void Sync()
        {
            var foot = world.FootPosition(agent);
            Visible = world.OnViewFloor(agent) && agent switch
            {
                Patient p => world.IsOnMap(p) && !world.InBed(p),
                _ => true,
            } && world.Simulation.ToiletTripOf(agent)?.Stage != ToiletStage.Using;

            // Seated people sit just in front of their chair. A trolley is long, so it sorts by its
            // nearest corner: walls behind it don't cover it as it passes.
            Position = foot + new Vector2(0, world.IsSeated(agent) ? 1 : agent is Patient p2 && world.OnTrolley(p2) ? TrolleyDepth(p2, foot) : 0);
            QueueRedraw();
        }

        public override void _Draw()
        {
            DrawSetTransform(-Position);
            var foot = world.FootPosition(agent);
            switch (agent)
            {
                case AmbulanceVehicle ambulance:
                    Vehicles.Ambulance(this, world, ambulance);
                    break;

                case Patient patient:
                    DrawPatient(patient, foot);
                    break;

                case Outpatient outpatient:
                {
                    DrawRing(foot, ReferenceEquals(world.Selected, outpatient) ? new Color("#FFEB3B") : Looks.OutpatientRing);
                    var clinicLook = Looks.Outpatient(outpatient, world.Simulation.FindAppointment(outpatient.AppointmentId));
                    if (world.IsSeated(outpatient))
                    {
                        var facing = world.SeatFacing(outpatient);
                        Figures.Seated(this, foot, clinicLook, away: ChairArt.FacesAway(facing), lean: ChairArt.Lean(facing));
                    }
                    else
                    {
                        Figures.Standing(this, foot, clinicLook, world.Time, outpatient.IsMoving, outpatient.Id * 17, away: WalkingAway(outpatient, outpatient.IsMoving));
                    }

                    break;
                }

                case StaffMember member:
                    DrawRing(foot, ReferenceEquals(world.Selected, member) ? new Color("#FFEB3B") : (Color?)null);
                    var look = Looks.Staff(member);
                    if (world.IsSeated(member))
                    {
                        var facing = world.SeatFacing(member);
                        Figures.Seated(this, foot, look, away: ChairArt.FacesAway(facing), lean: ChairArt.Lean(facing));
                    }
                    else
                    {
                        var pushing = member.PushingPatientId is { } pid && world.Simulation.FindPatient(pid) is { IsMoving: true };
                        Figures.Standing(this, foot, look, world.Time, member.IsMoving || pushing, member.Id * 7, away: WalkingAway(member, member.IsMoving || pushing), holding: world.CarriedBy(member), medicineBag: world.Simulation.IsCarryingMedicines(member));
                    }

                    break;
            }
        }

        private float TrolleyDepth(Patient patient, Vector2 foot)
        {
            var (x, y) = world.Interpolated(patient);
            var (fx, fy) = patient.Facing;
            var (nx, ny) = (-fy, fx);
            var nearest = float.MinValue;
            foreach (var (a, b) in new[] { (2.0, 0.7), (2.0, -0.7), (-2.0, 0.7), (-2.0, -0.7) })
            {
                nearest = Math.Max(nearest, IsoDraw.ToScreen(x + (fx * a) + (nx * b), y + (fy * a) + (ny * b)).Y);
            }

            return Math.Max(0, nearest - foot.Y - 2);
        }

        private void DrawPatient(Patient patient, Vector2 foot)
        {
            var look = Looks.Patient(patient);
            var alpha = patient.IsAlive ? 1f : 0.5f;
            var selected = ReferenceEquals(world.Selected, patient);

            if (world.OnAmbulanceStretcher(patient))
            {
                // On the ambulance crew's trolley.
                var (gx, gy) = world.Interpolated(patient);
                if (selected)
                {
                    var ring = IsoDraw.FloorEllipse(foot, 42, 32);
                    DrawPolyline(ring.Append(ring[0]).ToArray(), new Color("#FFEB3B"), 3f, true);
                }

                Figures.AmbulanceStretcher(this, gx, gy, (0, 1), look, new Color("#B5413B"), Looks.Wristband(patient.Triage));
                return;
            }

            if (world.OnTrolley(patient))
            {
                // Being pushed on a trolley, or waiting on one for a porter.
                var (gx, gy) = world.Interpolated(patient);
                if (selected)
                {
                    var ring = IsoDraw.FloorEllipse(foot, 42, 32);
                    DrawPolyline(ring.Append(ring[0]).ToArray(), new Color("#FFEB3B"), 3f, true);
                }

                var lift = Figures.Trolley(this, gx, gy, patient.Facing);
                var blanket = patient.Maternity ? Looks.MaternityBlanket : patient.Stage == PatientStage.OnWard ? Looks.WardBlanket : Looks.EdBlanket;
                Figures.LyingAlong(this, gx, gy, patient.Facing, look, blanket, lift, patient.Maternity ? null : Looks.Wristband(patient.Triage), world.BabiesWith(patient));
                return;
            }

            DrawRing(foot, selected ? new Color("#FFEB3B") : Looks.TriageRing(patient.Triage));
            var baby = world.InArms(patient);
            if (world.InWheelchair(patient))
            {
                Figures.WheelchairBack(this, foot);
                Figures.Seated(this, foot, look, alpha, baby);
                Figures.WheelchairFront(this, foot);
            }
            else if (world.IsSeated(patient))
            {
                var facing = world.SeatFacing(patient);
                Figures.Seated(this, foot, look, alpha, baby, ChairArt.FacesAway(facing), ChairArt.Lean(facing));
            }
            else
            {
                Figures.Standing(this, foot, look, world.Time, patient.IsMoving, patient.Id * 13, alpha, WalkingAway(patient, patient.IsMoving), baby);
            }
        }

        /// <summary>True when someone walking is heading up the screen, so they are seen from behind.</summary>
        private static bool WalkingAway(Agent who, bool moving)
        {
            if (!moving)
            {
                return false;
            }

            var (fx, fy) = who.Facing;
            return (IsoDraw.ToScreen(fx, fy) - IsoDraw.ToScreen(0, 0)).Y < -0.01f;
        }

        private void DrawRing(Vector2 foot, Color? colour)
        {
            if (colour is not { } c)
            {
                return;
            }

            var ring = IsoDraw.FloorEllipse(foot, 13, 24);
            DrawPolyline(ring.Append(ring[0]).ToArray(), c, 2.5f, true);
        }
    }
}
