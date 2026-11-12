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
/// <summary>A chair as built: its tile, the way it faces, its colour, how far it is moved off the middle of its tile, and its furniture id.</summary>
public sealed record ChairInfo(GridCoord Tile, WallSide Facing, Color Colour, (double X, double Y)? Shift, string ItemId);

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
    private IDisposable? _layoutSubscription;
    private HospitalSimulation _sim = null!;
    private SimulationClock _clock = null!;

    public HospitalSimulation Simulation => _sim;

    public SimulationClock Clock => _clock;

    /// <summary>Seconds since the scene started, for animation.</summary>
    public double Time { get; private set; }

    /// <summary>The patient or staff member the player has selected, if any.</summary>
    public Agent? Selected { get; set; }

    /// <summary>Leg swing (radians) per tile walked, so steps keep time with how fast someone moves on screen.</summary>
    private const double StridePerTile = 1.6;

    /// <summary>The fastest legs swing (radians per real second), so fast-forward doesn't turn walking into a blur.</summary>
    private const double MaxStrideRate = 22;

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Agent, StrideState> _strides = new();

    private sealed class StrideState
    {
        public double Phase;
        public double LastTime = double.NaN;
    }

    /// <summary>
    /// The animation time to draw <paramref name="agent"/>'s legs at. It runs at the person's real
    /// on-screen pace (slower walkers and trolley pushers step slower, fast-forward steps faster),
    /// and holds still while the game is paused.
    /// </summary>
    public double StrideTime(Agent agent, bool walking)
    {
        var state = _strides.GetOrCreateValue(agent);
        var dt = double.IsNaN(state.LastTime) ? 0 : Math.Max(0, Time - state.LastTime);
        state.LastTime = Time;
        if (walking)
        {
            var tilesPerRealSecond = agent.WalkSpeed * SimulationClock.GameTimePerRealSecond.TotalSeconds * (int)_clock.Speed;
            state.Phase += Math.Min(tilesPerRealSecond * StridePerTile, MaxStrideRate) * dt;
        }

        // People.Standing swings legs at 9 radians per unit of time.
        return state.Phase / 9;
    }

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

    /// <summary>Counts rebuilds, so the 3D view knows when to rebuild its walls and floors too.</summary>
    public int StaticVersion { get; private set; }

    /// <summary>Rebuilds walls and furniture, after rooms are built, moved or demolished.</summary>
    public void RebuildStatic()
    {
        StaticVersion++;
        _monitors.Clear();
        _bedSpots.Clear();
        _cotBeds.Clear();
        _chairs.Clear();
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
        Decorations.Plan(_sim);

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

    // ------------------------------------------------------------------ resus trolleys

    /// <summary>Where a trolley is now, between simulation ticks.</summary>
    public (double X, double Y) Interpolated(ResusTrolley trolley)
    {
        var t = _clock.TickProgress;
        if (IsoDraw.FloorOf(trolley.PreviousY) != IsoDraw.FloorOf(trolley.Y))
        {
            return (trolley.X, trolley.Y);
        }

        return (trolley.PreviousX + ((trolley.X - trolley.PreviousX) * t), trolley.PreviousY + ((trolley.Y - trolley.PreviousY) * t));
    }

    // ------------------------------------------------------------------ people and vehicles

    /// <summary>World position of an agent's feet, interpolated between simulation ticks.</summary>
    public Vector2 FootPosition(Agent agent)
    {
        var (x, y) = FootGrid(agent);
        return IsoDraw.ToScreen(x, y);
    }

    /// <summary>Where an agent's feet are on the grid (in tiles), interpolated between simulation ticks.</summary>
    public (double X, double Y) FootGrid(Agent agent)
    {
        // On the floor beside their bed after a fall.
        if (agent is Patient { OnFloorAfterFall: true } fallen && _sim.BedOf(fallen) is { } bed)
        {
            return (bed.StaffSpot.X + 0.5, bed.StaffSpot.Y + 0.5);
        }

        var (x, y) = Interpolated(agent);
        if (IsSeated(agent))
        {
            // Sitting on a chair, which stands a little forward of the middle of its tile.
            var (cx, cy) = ChairArt.Centre(agent.Tile, SeatFacing(agent), _chairShifts.TryGetValue(agent.Tile, out var shift) ? shift : null);
            (x, y) = (x + (cx - (agent.Tile.X + 0.5)), y + (cy - (agent.Tile.Y + 0.5)));
        }

        return (x, y);
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
    public IEnumerable<CareSpace> ViewSpaces => _sim.Spaces.Where(s => IsoDraw.OnViewFloor(s.Rect.Y));

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

    /// <summary>For the member of a crash team doing compressions right now: where the patient's chest is on screen.</summary>
    public (Vector2 Chest, Vector2 Foot)? CprChest(StaffMember member)
    {
        if (CprSpot(member) is not { } spot)
        {
            return null;
        }

        return (IsoDraw.ToScreen(spot.ChestX, spot.ChestY) + new Vector2(0, -spot.Lift), IsoDraw.ToScreen(spot.FootX, spot.FootY));
    }

    /// <summary>
    /// For the member of a crash team doing compressions right now: where they stand and where the
    /// patient's chest is (grid tiles), and how high the chest is (2D pixels).
    /// </summary>
    public (double ChestX, double ChestY, double FootX, double FootY, float Lift)? CprSpot(StaffMember member)
    {
        if (member.CrashPatientId is not { } id || member.IsMoving || _sim.ArrestOf(id) is not { } arrest || !ReferenceEquals(_sim.CompressingNow(arrest), member)
            || _sim.FindPatient(id) is not { } patient)
        {
            return null;
        }

        var (x, y) = Interpolated(patient);
        var raised = OnTrolley(patient) || OnAmbulanceStretcher(patient);
        var lift = InBed(patient) ? 22f : raised ? 24f : 6f;

        // Stand right up against the bed (or beside the patient on the floor), on whichever side they were sent to,
        // with the hands on the near side of the chest.
        var (mx, my) = Interpolated(member);
        var (dx, dy) = (mx - x, my - y);
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 0.01)
        {
            return (x, y, mx, my, lift);
        }

        var (ux, uy) = (dx / length, dy / length);
        var gap = InBed(patient) || raised ? 1.25 : 0.8;
        return (x + (ux * 0.25), y + (uy * 0.25), x + (ux * gap), y + (uy * gap), lift);
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
        if (patient.IsMoving || patient.OnFloorAfterFall)
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

        return _sim.BedOf(patient) is { } bed ? patient.Tile == bed.PatientSpot : space.IsTreatmentSpace && patient.Tile == space.PatientSpot;
    }

    public bool IsSeated(Agent agent) =>
        !agent.IsMoving && _seatTiles.ContainsKey(agent.Tile) && (!_sitters.TryGetValue(agent.Tile, out var sitter) || sitter == agent);

    /// <summary>Who sits on each occupied chair this frame: one person to a chair, however briefly two meet there.</summary>
    private readonly Dictionary<GridCoord, Agent> _sitters = new();

    private void SeatSitters(IEnumerable<Agent> present)
    {
        _sitters.Clear();
        var claims = present
            .Where(a => a is not AmbulanceVehicle && !a.IsMoving && _seatTiles.ContainsKey(a.Tile))
            .OrderBy(a => a switch
            {
                // Whoever booked the chair, then whoever is working from it, then whoever got there first.
                Patient p when p.Seat == p.Tile => 0,
                Outpatient o when o.Seat == o.Tile => 0,
                StaffMember { Activity: StaffActivity.Working } => 1,
                _ => 2,
            })
            .ThenBy(a => a.Id);
        foreach (var agent in claims)
        {
            _sitters.TryAdd(agent.Tile, agent);
        }
    }

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

        SeatSitters(present);
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
        WallEdges = solid;
        DoorEdges = doors.Where(d => !solid.Contains(d)).ToHashSet();
    }

    /// <summary>Wall edges on the floor in view (a horizontal edge runs from (X, Y) to (X + 1, Y)); the 2D plan draws these.</summary>
    public IReadOnlyCollection<(int X, int Y, bool Horizontal)> WallEdges { get; private set; } = Array.Empty<(int, int, bool)>();

    /// <summary>Doorway edges on the floor in view.</summary>
    public IReadOnlyCollection<(int X, int Y, bool Horizontal)> DoorEdges { get; private set; } = Array.Empty<(int, int, bool)>();

    /// <summary>Everyone on the floor in view who is on the map, including patients lying in bed (the 2D plan draws them all).</summary>
    public IEnumerable<Agent> AgentsInView() =>
        _agentNodes.Keys.Where(a => OnViewFloor(a)
                                    && (a is not Patient p || IsOnMap(p))
                                    && _sim.ToiletTripOf(a)?.Stage != ToiletStage.Using);

    private readonly Dictionary<CareSpace, (float Open, double At)> _doors = new();

    /// <summary>How far a room's door stands open: it swings open as someone comes near and closes behind them.</summary>
    public float DoorOpenness(CareSpace space, double mx, double my)
    {
        static bool Near(Agent a, double x, double y) => Math.Abs(a.X - x) < 1.6 && Math.Abs(a.Y - y) < 1.6;
        var near = _sim.Patients.Any(p => Near(p, mx, my)) || _sim.Staff.Any(s => Near(s, mx, my));
        var (open, at) = _doors.GetValueOrDefault(space, (0f, Time));
        var step = (float)Math.Clamp((Time - at) * 4, 0, 1);
        open = Math.Clamp(open + (near ? step : -step), 0, 1);
        _doors[space] = (open, Time);
        return open;
    }

    /// <summary>The middle of whichever side of a footprint is nearest the viewer, so thin pieces sort right from every angle.</summary>
    internal static Vector2 FrontFace(double x, double y, double w, double l)
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
                // Resus trolleys can be moved and pushed about, so they are drawn on their own (see SyncTrolleys).
                if (item.ItemId == HospitalSimulation.ResusTrolleyItem || !_sim.Database.Furniture.TryGetValue(item.ItemId, out var def))
                {
                    continue;
                }

                var colour = new Color(def.Colour);
                switch (def.Style)
                {
                    case FurnitureStyle.Box when def.Id == "cot":
                        AddCot(space, item);
                        break;
                    case FurnitureStyle.Bed when def.Id is "incubator":
                        break;
                    case FurnitureStyle.Bed when def.Id == "birthing_bed":
                        _bedSpots.Add(space.PatientSpot);
                        break;
                    case FurnitureStyle.Bed when space.IsServiceRoom:
                        break;
                    case FurnitureStyle.Bed when space.HasBeds:
                        if (space.Beds.FirstOrDefault(b => b.PatientSpot == item.Tile) is { } wardBed)
                        {
                            _bedSpots.Add(wardBed.PatientSpot);
                        }

                        break;
                    case FurnitureStyle.Bed:
                        _bedSpots.Add(space.PatientSpot);
                        break;
                    case FurnitureStyle.Monitor:
                        AddObsMonitor(space, item.CentreX, item.CentreY);
                        break;
                    case FurnitureStyle.Chair:
                        _chairs.Add(new ChairInfo(item.Tile, RoomChairFacing(space, item), colour, null, def.Id));
                        break;
                }
            }
        }

        foreach (var chair in _sim.Chairs.Where(c => IsoDraw.OnViewFloor(c.Tile.Y)))
        {
            _chairs.Add(new ChairInfo(chair.Tile, chair.Facing, new Color(chair.Colour), _chairShifts.GetValueOrDefault(chair.Tile), "waiting_seat"));
        }
    }

    /// <summary>Who is lying in the bed at <paramref name="spot"/>: usually its own patient, otherwise whoever is still lying there waiting to be moved.</summary>
    public Patient? BedOccupant(GridCoord spot, int? occupantId)
    {
        var occupant = occupantId is { } id ? _sim.FindPatient(id) : null;
        if (occupant is null || occupant.Tile != spot || !InBed(occupant))
        {
            occupant = _sim.Patients.FirstOrDefault(p => p.Tile == spot && InBed(p));
        }

        return occupant;
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
    public BabyLook[]? OnChest(Patient mother) => BabyInCot(mother) ? null : BabiesWith(mother);

    /// <summary>The baby mum carries in her arms when she is up and about.</summary>
    public BabyLook? InArms(Patient mother) => BabiesWith(mother) is { Length: > 0 } babies ? babies[0] : null;

    /// <summary>The baby a nurse is carrying to or from the neonatal unit.</summary>
    public BabyLook? CarriedBy(StaffMember member)
    {
        var baby = _sim.Babies.FirstOrDefault(b => b.Place == BabyPlace.Carried && b.CarriedByStaffId == member.Id);
        return baby is null ? null : Looks.Baby(baby, _sim.FindPatient(baby.MotherId));
    }

    /// <summary>A cot: on a mother and baby ward it belongs to the nearest bed, and that mum's baby sleeps in it while she is in bed.</summary>
    private void AddCot(CareSpace space, PlacedFurniture item)
    {
        var (gx, gy) = (item.X + (item.Width / 2), item.Y + (item.Length / 2));
        if (_sim.IsMotherAndBabyWard(space) && space.Beds.Count > 0)
        {
            _cotBeds.Add(space.Beds.MinBy(b => Math.Pow(b.PatientSpot.X + 0.5 - gx, 2) + Math.Pow(b.PatientSpot.Y + 0.5 - gy, 2))!.PatientSpot);
        }
    }

    private void AddObsMonitor(CareSpace space, double gx, double gy)
    {
        var bed = space.Beds.Count == 0 ? null : space.Beds.MinBy(b => Math.Pow(b.PatientSpot.X + 0.5 - gx, 2) + Math.Pow(b.PatientSpot.Y + 0.5 - gy, 2));
        _monitors.Add(new ObsMonitorInfo(space, gx, gy, bed));
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

    private readonly List<ChairInfo> _chairs = new();

    /// <summary>Every chair on the floor in view, as last built: where its seat is, which way it faces, its colour and kind.</summary>
    public IReadOnlyList<ChairInfo> Chairs => _chairs;

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
            Position = foot;
        }
    }
}
