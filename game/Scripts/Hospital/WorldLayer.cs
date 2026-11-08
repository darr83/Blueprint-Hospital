using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Core;
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
public sealed record ObsMonitorInfo(CareSpace Space, double X, double Y);

/// <summary>
/// Everything that stands up from the floor: walls, furniture, equipment, people and vehicles.
/// Each object is its own node positioned at its depth anchor, and Godot's Y-sort draws them
/// back to front, so people pass correctly behind and in front of walls, desks and beds.
/// </summary>
public partial class WorldLayer : Node2D
{
    /// <summary>Height of every wall on screen (about 1.1 m, cut away so the rooms stay visible).</summary>
    public const float WallHeight = 30f;

    /// <summary>Wall thickness in tiles.</summary>
    public const double WallThickness = 0.3;

    private static readonly Color WallTop = new("#F4F6F8");
    private static readonly Color WallFace = new("#D9DFE5");

    private readonly Dictionary<Agent, AgentNode> _agentNodes = new();
    private readonly HashSet<GridCoord> _seatTiles = new();
    private readonly List<ObsMonitorInfo> _monitors = new();
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

        foreach (var space in _sim.Spaces)
        {
            foreach (var seat in space.Seats)
            {
                _seatTiles.Add(seat);
            }
        }

        BuildWalls();
        BuildFurniture();
    }

    public override void _Process(double delta)
    {
        Time += delta;
        SyncAgents();
    }

    // ------------------------------------------------------------------ people and vehicles

    /// <summary>World position of an agent's feet, interpolated between simulation ticks.</summary>
    public Vector2 FootPosition(Agent agent)
    {
        var (x, y) = Interpolated(agent);
        return IsoDraw.ToScreen(x, y);
    }

    public (double X, double Y) Interpolated(Agent agent)
    {
        var t = _clock.TickProgress;
        return (agent.PreviousX + ((agent.X - agent.PreviousX) * t), agent.PreviousY + ((agent.Y - agent.PreviousY) * t));
    }

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

    /// <summary>Lying on a bed in a treatment space, or on an ambulance trolley.</summary>
    public bool IsLying(Patient patient)
    {
        if (patient.IsMoving)
        {
            return false;
        }

        return patient.Stage == PatientStage.InAmbulanceQueue || BedOf(patient) is not null;
    }

    /// <summary>The treatment space whose bed the patient is lying on, if any.</summary>
    public CareSpace? BedOf(Patient patient) =>
        !patient.IsMoving && patient.CareSpaceId is { } id && _sim.Space(id) is { IsTreatmentSpace: true } space && patient.Tile == space.PatientSpot
            ? space
            : null;

    public bool IsSeated(Agent agent) => !agent.IsMoving && _seatTiles.Contains(agent.Tile);

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

        foreach (var s in _sim.Staff)
        {
            present.Add(s);
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

        foreach (var building in _sim.Site.Buildings)
        {
            Perimeter(building.Footprint, building.IsOpening);
        }

        foreach (var space in _sim.Spaces.Where(s => s.HasWalls))
        {
            Perimeter(space.Rect, space.IsDoor);
        }

        const double h = WallThickness / 2;
        foreach (var (x, y, horizontal) in walls.Where(w => !doors.Contains(w)))
        {
            // Each one-tile piece sorts on its own, anchored at its front face.
            if (horizontal)
            {
                AddProp(IsoDraw.ToScreen(x + 0.5, y + h), c => IsoDraw.Box(c, x, y - h, 1, WallThickness, WallHeight, WallTop, sideColour: WallFace));
            }
            else
            {
                AddProp(IsoDraw.ToScreen(x + h, y + 0.5), c => IsoDraw.Box(c, x - h, y, WallThickness, 1, WallHeight, WallTop, sideColour: WallFace));
            }
        }

        // Door posts where a doorway meets a wall, so openings read clearly.
        bool IsWall((int, int, bool) e) => walls.Contains(e) && !doors.Contains(e);
        foreach (var (x, y, horizontal) in doors)
        {
            if (IsWall(horizontal ? (x - 1, y, true) : (x, y - 1, false)))
            {
                AddDoorPost(x, y);
            }

            if (IsWall(horizontal ? (x + 1, y, true) : (x, y + 1, false)))
            {
                AddDoorPost(horizontal ? x + 1 : x, horizontal ? y : y + 1);
            }
        }
    }

    private void AddDoorPost(int x, int y)
    {
        const double s = 0.22;
        AddProp(IsoDraw.ToScreen(x, y + s), c => IsoDraw.Box(c, x - s, y - s, s * 2, s * 2, WallHeight + 5, new Color("#B8C4CE"), sideColour: new Color("#9AA8B4")));
    }

    // ------------------------------------------------------------------ furniture

    private void BuildFurniture()
    {
        foreach (var space in _sim.Spaces)
        {
            switch (space.Kind)
            {
                case CareSpaceKind.Resus:
                case CareSpaceKind.Majors:
                case CareSpaceKind.Minors:
                    AddBedSpace(space);
                    break;

                case CareSpaceKind.Triage:
                    Chair(space.PatientSpot, new Color("#7E6BB5"));
                    Chair(space.StaffSpot, new Color("#56606B"));
                    Block(space.StaffSpot.X - 1.4, space.StaffSpot.Y - 1.2, 1.1, 2.2, 22, new Color("#E3E7EA"));
                    AddObsMonitor(space, space.PatientSpot.X + 1.9, space.PatientSpot.Y - 0.4);
                    break;

                case CareSpaceKind.Waiting:
                    foreach (var seat in space.Seats)
                    {
                        Chair(seat, new Color("#4F7FB0"));
                    }

                    // Reception desk between the receptionist and the queue, with a raised counter.
                    Block(space.StaffSpot.X - 2, space.StaffSpot.Y + 1, 5, 0.8, 26, new Color("#8C6D4F"));
                    Block(space.StaffSpot.X - 2.1, space.StaffSpot.Y + 1.5, 5.2, 0.4, 32, new Color("#E3D7C6"));
                    Chair(space.StaffSpot, new Color("#56606B"));
                    break;

                case CareSpaceKind.NursesStation:
                    Block(space.Rect.X + 1.5, space.Rect.Y + 4.1, space.Rect.Width - 3, 0.9, 24, new Color("#8C6D4F"));
                    foreach (var seat in space.Seats)
                    {
                        Chair(seat, new Color("#56606B"));
                    }

                    break;

                case CareSpaceKind.DoctorsOffice:
                    Block(space.Rect.X + 1.6, space.Rect.Y + 4.1, space.Rect.Width - 3.2, 1.6, 20, new Color("#A9865F"));
                    foreach (var seat in space.Seats)
                    {
                        Chair(seat, new Color("#56606B"));
                    }

                    break;

                case CareSpaceKind.StaffRoom:
                    Block(space.Rect.X + 1.8, space.Rect.Y + 3.1, 4.4, 1.6, 18, new Color("#A9865F"));
                    foreach (var seat in space.Seats)
                    {
                        Chair(seat, new Color("#C0703A"));
                    }

                    Block(space.Rect.Right - 1.3, space.Rect.Y + 0.4, 1, 3, 24, new Color("#E3E7EA"));
                    break;
            }
        }
    }

    private void AddBedSpace(CareSpace space)
    {
        var spot = space.PatientSpot;
        var (cx, cy) = (spot.X + 0.5, spot.Y + 0.5);

        // Bed and whoever is lying on it are one object, anchored at the middle of the bed.
        AddProp(IsoDraw.ToScreen(cx, cy), c =>
        {
            var occupant = space.OccupantId is { } id ? _sim.FindPatient(id) : null;
            var lying = occupant is not null && BedOf(occupant) == space;
            if (lying && ReferenceEquals(Selected, occupant))
            {
                var ring = IsoDraw.FloorEllipse(IsoDraw.ToScreen(cx, cy), 46, 32);
                c.DrawPolyline(ring.Append(ring[0]).ToArray(), new Color("#FFEB3B"), 3f, true);
            }

            var lift = Figures.Bed(c, cx, cy, made: !lying);
            if (lying)
            {
                Figures.Lying(c, cx, cy, Looks.Patient(occupant!), Looks.Blanket(occupant!.Triage), lift);
            }
        }, animated: true);

        // Obs monitor at the head of the bed, on the side away from where staff stand.
        var side = space.StaffSpot.X < spot.X ? 1 : -1;
        AddObsMonitor(space, cx + (side * 1.4), cy - 1.6);

        if (space.Kind == CareSpaceKind.Resus)
        {
            // Resuscitation trolley and an oxygen cylinder.
            Block(cx - (side * 2.6) - 0.6, cy - 2.4, 1.2, 1.4, 24, new Color("#D84B3E"));
            Block(cx + (side * 2.4), cy + 1.2, 0.5, 0.5, 30, new Color("#E8EDEF"));
        }
    }

    private void AddObsMonitor(CareSpace space, double gx, double gy)
    {
        _monitors.Add(new ObsMonitorInfo(space, gx, gy));
        AddProp(IsoDraw.ToScreen(gx, gy), c => Figures.MonitorStand(c, gx, gy));
    }

    /// <summary>Every obs monitor's position and room; their screens are drawn above the world so they are never hidden.</summary>
    public IReadOnlyList<ObsMonitorInfo> Monitors => _monitors;

    /// <summary>The patient connected to a monitor: whoever is on the bed (or in the triage chair).</summary>
    public Patient? ConnectedPatient(CareSpace space)
    {
        var patient = space.Kind == CareSpaceKind.Triage
            ? _sim.Patients.FirstOrDefault(p => !p.IsMoving && p.Tile == space.PatientSpot)
            : space.OccupantId is { } id ? _sim.FindPatient(id) : null;
        return patient is not null && !patient.IsMoving && patient.Tile == space.PatientSpot ? patient : null;
    }

    private void Chair(GridCoord tile, Color colour)
    {
        var (x, y) = (tile.X + 0.15, tile.Y + 0.15);
        AddProp(IsoDraw.ToScreen(tile.X + 0.5, tile.Y + 0.4), c =>
        {
            IsoDraw.Box(c, x + 0.05, y, 0.6, 0.12, 24, colour.Darkened(0.1f));
            IsoDraw.Box(c, x, y, 0.7, 0.7, 11, colour);
        });
    }

    /// <summary>A box of furniture, split into one-tile slices along its length so each slice sorts correctly.</summary>
    private void Block(double x, double y, double w, double l, float height, Color colour)
    {
        var alongX = w >= l;
        var length = alongX ? w : l;
        var slices = Math.Max(1, (int)Math.Ceiling(length));
        var step = length / slices;
        for (var i = 0; i < slices; i++)
        {
            var (sx, sy, sw, sl) = alongX ? (x + (i * step), y, step, l) : (x, y + (i * step), w, step);
            AddProp(IsoDraw.ToScreen(sx + (sw / 2), sy + (sl / 2)), c => IsoDraw.Box(c, sx, sy, sw, sl, height, colour, shadow: false));
        }
    }

    private void AddProp(Vector2 anchor, Action<CanvasItem> draw, bool animated = false)
    {
        AddChild(new PropNode(draw, animated) { Position = anchor });
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
        public void Sync()
        {
            var foot = world.FootPosition(agent);
            Visible = agent switch
            {
                Patient p => world.IsOnMap(p) && world.BedOf(p) is null,
                _ => true,
            };

            // Seated people sit just in front of their chair.
            Position = foot + new Vector2(0, world.IsSeated(agent) ? 1 : 0);
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

                case StaffMember member:
                    DrawRing(foot, ReferenceEquals(world.Selected, member) ? new Color("#FFEB3B") : (Color?)null);
                    var look = Looks.Staff(member);
                    if (world.IsSeated(member))
                    {
                        Figures.Seated(this, foot, look);
                    }
                    else
                    {
                        Figures.Standing(this, foot, look, world.Time, member.IsMoving, member.Id * 7);
                    }

                    break;
            }
        }

        private void DrawPatient(Patient patient, Vector2 foot)
        {
            var look = Looks.Patient(patient);
            var alpha = patient.IsAlive ? 1f : 0.5f;
            var selected = ReferenceEquals(world.Selected, patient);

            if (patient.Stage == PatientStage.InAmbulanceQueue && !patient.IsMoving)
            {
                // On the ambulance crew's trolley.
                var (gx, gy) = world.Interpolated(patient);
                if (selected)
                {
                    var ring = IsoDraw.FloorEllipse(foot, 42, 32);
                    DrawPolyline(ring.Append(ring[0]).ToArray(), new Color("#FFEB3B"), 3f, true);
                }

                IsoDraw.Box(this, gx - 0.7, gy - 2.0, 1.4, 4.0, 14, new Color("#C9D1D8"));
                Figures.Lying(this, gx, gy, look, Looks.Blanket(patient.Triage), 14);
                return;
            }

            DrawRing(foot, selected ? new Color("#FFEB3B") : Looks.TriageRing(patient.Triage));
            if (world.IsSeated(patient))
            {
                Figures.Seated(this, foot, look, alpha);
            }
            else
            {
                Figures.Standing(this, foot, look, world.Time, patient.IsMoving, patient.Id * 13, alpha);
            }
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
