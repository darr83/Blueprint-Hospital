using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// Walls and doorways of the floor in view. Wall runs are merged into one mesh; each named room's
/// doorway has a frame and door leaves that swing open as people come near.
/// </summary>
public partial class Walls3D : Node3D
{
    public static readonly Color WallTop = new("#F4F6F8");
    public static readonly Color WallFace = new("#DCE2E8");
    public static readonly Color Skirting = new("#9FB0BE");
    public static readonly Color FrameFace = new("#A9B7C3");
    public static readonly Color FrameTop = new("#C7D2DB");
    private static readonly Color LeafFace = new("#E7D9BF");
    private static readonly Color LeafEdge = new("#B9A27E");
    private static readonly Color Glass = new("#A8CBDC", 0.75f);
    private static readonly Color KickPlate = new("#B7C1C9");
    private static readonly Color Handle = new("#7A8792");

    private View3D _view = null!;
    private readonly List<(CareSpace Space, double Mx, double My, Node3D[] Hinges, float[] Closed)> _doors = new();
    private readonly List<Sliding> _sliding = new();

    /// <summary>
    /// A pair of sliding doors: a lift's steel landing doors (with the call button beside them) or the
    /// glass front doors at an entrance. They part as people come near and close again behind them.
    /// </summary>
    private sealed class Sliding
    {
        public required Node3D[] Panels { get; init; }
        public required Vector3[] Closed { get; init; }
        public required Vector3 Along { get; init; }
        public required float Travel { get; init; }
        public required double Mx { get; init; }
        public required double My { get; init; }
        public required float Reach { get; init; }

        /// <summary>How far either side of the middle along the doorway still counts as near (defaults to <see cref="Reach"/>).</summary>
        public float? Span { get; init; }

        /// <summary>Whether the doorway runs along x (so "across" it is y).</summary>
        public bool Horizontal { get; init; }
        public Node3D? Button { get; init; }
        public Simulation.World.GridRect? Inside { get; init; }
        public float Open { get; set; }
    }

    /// <summary>Full wall height.</summary>
    public static float FullHeight => Kit3D.H(WorldLayer.WallHeight);

    /// <summary>Walls cut down to a low skirting height, so every room can be seen into (X).</summary>
    public bool Cutaway { get; set; }

    private float Height => Cutaway ? Kit3D.H(12) : FullHeight;

    public void Initialise(View3D view) => _view = view;

    public void Rebuild()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }

        _doors.Clear();
        _sliding.Clear();
        var world = _view.World;
        var h = Height;
        const float t = (float)WorldLayer.WallThickness;
        var skirt = Kit3D.H(4);

        var walls = new MeshBatch();
        var points = new HashSet<(int X, int Y)>();
        foreach (var (x, y, horizontal) in world.WallEdges)
        {
            if (horizontal)
            {
                walls.Block(x, y - (t / 2), 1, t, h, WallFace);
                walls.Block(x, y - (t / 2) - 0.012, 1, t + 0.024, skirt, Skirting);
                points.Add((x, y));
                points.Add((x + 1, y));
            }
            else
            {
                walls.Block(x - (t / 2), y, t, 1, h, WallFace);
                walls.Block(x - (t / 2) - 0.012, y, t + 0.024, 1, skirt, Skirting);
                points.Add((x, y));
                points.Add((x, y + 1));
            }
        }

        // A post at every point a wall reaches closes corners and ends; posts beside doorways are frame trim.
        var doorPoints = new HashSet<(int X, int Y)>();
        foreach (var (x, y, horizontal) in world.DoorEdges)
        {
            doorPoints.Add((x, y));
            doorPoints.Add(horizontal ? (x + 1, y) : (x, y + 1));
        }

        foreach (var (x, y) in points)
        {
            var frame = doorPoints.Contains((x, y));
            walls.Block(x - (t / 2) - (frame ? 0.02 : 0), y - (t / 2) - (frame ? 0.02 : 0), t + (frame ? 0.04 : 0), t + (frame ? 0.04 : 0), h, frame ? FrameFace : WallFace);
        }

        // The tops of the walls, a shade lighter, so runs read clearly from above.
        foreach (var (x, y, horizontal) in world.WallEdges)
        {
            if (horizontal)
            {
                walls.Floor(x - (t / 2), y - (t / 2), 1 + t, t, h + 0.002f, WallTop);
            }
            else
            {
                walls.Floor(x - (t / 2), y - (t / 2), t, 1 + t, h + 0.002f, WallTop);
            }
        }

        walls.AddTo(this);

        if (Cutaway)
        {
            return;
        }

        foreach (var space in _view.Simulation.Spaces.Where(s => IsoDraw.OnViewFloor(s.Rect.Y) && RoomSigns.Named(s)))
        {
            AddDoorway(space);
        }

        if (IsoDraw.ViewFloor == 0)
        {
            foreach (var entrance in _view.Simulation.Site.Buildings.SelectMany(b => b.Entrances))
            {
                AddEntrance(entrance);
            }
        }
    }

    /// <summary>Swings each door open or shut, following whoever is near it, and slides the lift and entrance doors.</summary>
    public void Animate(double delta = 1.0 / 60)
    {
        foreach (var (space, mx, my, hinges, rest) in _doors)
        {
            var open = _view.World.DoorOpenness(space, mx, my) * (float)(Math.PI * 0.45);
            for (var i = 0; i < hinges.Length; i++)
            {
                hinges[i].Rotation = new Vector3(0, rest[i] + (open * (float)hinges[i].GetMeta("swing", 1f)), 0);
            }
        }

        if (_sliding.Count == 0)
        {
            return;
        }

        var sim = _view.Simulation;
        var people = sim.Patients.Cast<Simulation.Agents.Agent>().Concat(sim.Staff.Where(m => m.IsInBuilding)).Concat(sim.Outpatients)
            .Select(a => _view.World.Interpolated(a))
            .ToList();
        var step = sim.Clock.IsPaused ? 0f : (float)Math.Min(1, delta * 2.2);
        foreach (var door in _sliding)
        {
            var (alongR, acrossR) = (door.Span ?? door.Reach, door.Reach);
            var near = people.Any(p => (Math.Abs((door.Horizontal ? p.X : p.Y) - (door.Horizontal ? door.Mx : door.My)) < alongR
                                        && Math.Abs((door.Horizontal ? p.Y : p.X) - (door.Horizontal ? door.My : door.Mx)) < acrossR)
                || (door.Inside is { } r && p.X >= r.X && p.X < r.Right && p.Y >= r.Y && p.Y < r.Bottom));
            door.Open = Math.Clamp(door.Open + (near ? step : -step), 0, 1);
            var u = door.Open * door.Open * (3 - (2 * door.Open));
            for (var i = 0; i < door.Panels.Length; i++)
            {
                door.Panels[i].Position = door.Closed[i] + (door.Along * (i == 0 ? -1 : 1) * door.Travel * u);
            }

            if (door.Button is { } button)
            {
                // The call button lights while someone is waiting and the doors are still opening.
                button.Visible = near && door.Open < 0.98f;
            }
        }
    }

    /// <summary>
    /// The glass sliding doors at a building entrance, in a steel frame with a header over them, on the
    /// ground floor only (and only while the entrance is open, not built over).
    /// </summary>
    private void AddEntrance(Simulation.World.Entrance entrance)
    {
        var horizontal = entrance.Side is WallSide.North or WallSide.South;
        var line = entrance.Side switch
        {
            WallSide.North => entrance.Tile.Y,
            WallSide.South => entrance.Tile.Y + 1,
            WallSide.West => entrance.Tile.X,
            _ => entrance.Tile.X + 1,
        };
        var start = horizontal ? entrance.Tile.X : entrance.Tile.Y;
        var firstEdge = horizontal ? (start, line, true) : (line, start, false);
        if (!_view.World.DoorEdges.Contains(firstEdge))
        {
            return;
        }

        var width = entrance.WidthTiles;
        var doorH = Kit3D.H(RoomSigns.DoorHeight);
        var wallH = FullHeight;
        var steel = new Color("#7F8C96");
        var frame = new MeshBatch();
        void Along(double s0, double len, double thick, float from, float to, Color colour)
        {
            if (horizontal)
            {
                frame.Block(s0, line - (thick / 2), len, thick, to - from, colour, from);
            }
            else
            {
                frame.Block(line - (thick / 2), s0, thick, len, to - from, colour, from);
            }
        }

        const double post = 0.12;
        Along(start, post, 0.34, 0, doorH, steel);
        Along(start + width - post, post, 0.34, 0, doorH, steel);
        Along(start, width, 0.34, doorH, doorH + Kit3D.H(3), steel);
        Along(start, width, WorldLayer.WallThickness, doorH + Kit3D.H(3), wallH, WallFace);
        // The sensor box over the middle.
        Along(start + (width / 2) - 0.2, 0.4, 0.4, doorH - Kit3D.H(2), doorH, new Color("#37474F"));
        frame.AddTo(this);

        var unit = horizontal ? new Vector3(1, 0, 0) : new Vector3(0, 0, 1);
        var leaf = (float)((width - (post * 2)) / 2);
        var panels = new Node3D[2];
        var closed = new Vector3[2];
        for (var i = 0; i < 2; i++)
        {
            var mid = (float)(start + post + (leaf * (i + 0.5f)));
            closed[i] = horizontal ? new Vector3(mid, 0, (float)line) : new Vector3((float)line, 0, mid);
            var node = new Node3D { Name = $"Entrance{i}", Position = closed[i], Rotation = new Vector3(0, horizontal ? 0 : Mathf.Pi / 2, 0) };
            AddChild(node);
            var b = new MeshBatch();
            // A steel frame round the glass, with a push bar across it.
            var rail = new Color("#6E7B85");
            b.Box(new Vector3(0, 0.04f, 0), new Vector3(leaf, 0.08f, 0.07f), rail);
            b.Box(new Vector3(0, doorH - 0.04f, 0), new Vector3(leaf, 0.08f, 0.07f), rail);
            b.Box(new Vector3(-(leaf / 2) + 0.03f, doorH / 2, 0), new Vector3(0.06f, doorH, 0.07f), rail);
            b.Box(new Vector3((leaf / 2) - 0.03f, doorH / 2, 0), new Vector3(0.06f, doorH, 0.07f), rail);
            b.Box(new Vector3(0, doorH * 0.5f, 0), new Vector3(leaf - 0.1f, 0.04f, 0.08f), rail);
            b.AddTo(node);
            Kit3D.Box(node, new Vector3(0, doorH / 2, 0), new Vector3(leaf - 0.1f, doorH - 0.12f, 0.03f), new Color("#9FD3EA", 0.55f), Finish.Gloss, shadow: false);
            panels[i] = node;
        }

        var middle = start + (width / 2.0);
        var (mx, my) = horizontal ? (middle, (double)line) : ((double)line, middle);
        _sliding.Add(new Sliding { Panels = panels, Closed = closed, Along = unit, Travel = leaf * 0.92f, Mx = mx, My = my, Reach = 1.6f, Span = (float)(width / 2.0) + 0.3f, Horizontal = horizontal });
    }

    /// <summary>A lift's landing doors: two steel panels that part from the middle, with the call button beside them.</summary>
    private void AddLiftDoors(CareSpace lift, double openFrom, double openTo, double line, bool horizontal, (int X, int Y) outward, double mx, double my)
    {
        var doorH = Kit3D.H(RoomSigns.DoorHeight);
        var unit = horizontal ? new Vector3(1, 0, 0) : new Vector3(0, 0, 1);
        var out3 = new Vector3(outward.X, 0, outward.Y);
        var face = (float)(WorldLayer.WallThickness / 2) + 0.04f;
        const double post = 0.14;
        var leaf = (float)((openTo - openFrom - (post * 2)) / 2);
        var panels = new Node3D[2];
        var closed = new Vector3[2];
        for (var i = 0; i < 2; i++)
        {
            var mid = (float)(openFrom + post + (leaf * (i + 0.5f)));
            closed[i] = (horizontal ? new Vector3(mid, 0, (float)line) : new Vector3((float)line, 0, mid)) + (out3 * face);
            var node = new Node3D { Name = $"LiftDoor{i}", Position = closed[i], Rotation = new Vector3(0, horizontal ? 0 : Mathf.Pi / 2, 0) };
            AddChild(node);
            var b = new MeshBatch();
            b.Box(new Vector3(0, doorH / 2, 0), new Vector3(leaf, doorH - 0.02f, 0.05f), new Color("#C3CDD5"));
            b.Box(new Vector3(0, doorH / 2, 0.03f), new Vector3(leaf * 0.86f, doorH * 0.9f, 0.01f), new Color("#D5DEE5"));
            b.Box(new Vector3((i == 0 ? 1 : -1) * ((leaf / 2) - 0.012f), doorH / 2, 0.03f), new Vector3(0.02f, doorH - 0.04f, 0.012f), new Color("#8E9AA4"));
            b.AddTo(node);
            panels[i] = node;
        }

        // The call button plate on the wall beside the doors, and the button's light.
        var plateS = openTo + 0.28;
        var plate = (horizontal ? new Vector3((float)plateS, 0, (float)line) : new Vector3((float)line, 0, (float)plateS)) + (out3 * (face - 0.01f));
        var pb = new MeshBatch();
        pb.Box(plate + new Vector3(0, doorH * 0.55f, 0), horizontal ? new Vector3(0.16f, 0.32f, 0.03f) : new Vector3(0.03f, 0.32f, 0.16f), new Color("#9AA6B0"));
        pb.Ball(plate + (out3 * 0.02f) + new Vector3(0, doorH * 0.59f, 0), new Vector3(0.07f, 0.07f, 0.07f), new Color("#5A6670"), small: true);
        pb.Ball(plate + (out3 * 0.02f) + new Vector3(0, doorH * 0.51f, 0), new Vector3(0.07f, 0.07f, 0.07f), new Color("#5A6670"), small: true);
        pb.AddTo(this);
        var gb = new MeshBatch();
        gb.Ball(plate + (out3 * 0.03f) + new Vector3(0, doorH * 0.59f, 0), new Vector3(0.09f, 0.09f, 0.09f), new Color("#FFC94D"), small: true);
        gb.Ball(plate + (out3 * 0.03f) + new Vector3(0, doorH * 0.59f, 0), new Vector3(0.24f, 0.24f, 0.24f), new Color("#FFC94D", 0.35f), small: true);
        var glow = gb.AddTo(this, alpha: true, shadow: false)!;
        glow.MaterialOverride = Kit3D.VertexColoursUnshadedAlpha;
        glow.Name = "LiftButton";
        glow.Visible = false;

        _sliding.Add(new Sliding { Panels = panels, Closed = closed, Along = unit, Travel = leaf * 0.95f, Mx = mx, My = my, Reach = 0.9f, Button = glow, Inside = lift.Rect, Horizontal = horizontal });
    }

    /// <summary>A room's doorway: infill either side, frame and lintel, and one or two leaves swinging into the room.</summary>
    private void AddDoorway(CareSpace space)
    {
        var r = space.Rect;
        var d = space.Doors[0];
        var (a, b, o) = d.Side switch
        {
            WallSide.North => ((X: (double)r.X + d.Offset, Y: (double)r.Y), (X: (double)r.X + d.Offset + d.Width, Y: (double)r.Y), (X: 0, Y: -1)),
            WallSide.South => ((r.X + d.Offset, r.Bottom), (r.X + d.Offset + d.Width, r.Bottom), (0, 1)),
            WallSide.West => ((r.X, r.Y + d.Offset), (r.X, r.Y + d.Offset + d.Width), (-1, 0)),
            _ => ((r.Right, r.Y + d.Offset), (r.Right, r.Y + d.Offset + d.Width), (1, 0)),
        };
        var horizontal = o.X == 0;
        var start = horizontal ? Math.Min(a.X, b.X) : Math.Min(a.Y, b.Y);
        var doorTiles = horizontal ? Math.Abs(b.X - a.X) : Math.Abs(b.Y - a.Y);
        var line = horizontal ? a.Y : a.X;
        var wallT = WorldLayer.WallThickness;

        var batch = new MeshBatch();
        void Along(double s, double len, double thick, float from, float to, Color colour)
        {
            if (horizontal)
            {
                batch.Block(s, line - (thick / 2), len, thick, to - from, colour, from);
            }
            else
            {
                batch.Block(line - (thick / 2), s, thick, len, to - from, colour, from);
            }
        }

        var kind = RoomSigns.KindOf(space);
        var leaves = kind switch { RoomSigns.DoorKind.Double => 2, RoomSigns.DoorKind.Single => 1, _ => 0 };
        var leafTiles = leaves == 0 ? 0 : Math.Min(doorTiles / leaves, 2.0);
        var spare = doorTiles - (leafTiles * leaves);
        var gapStart = start + (spare / 2);
        var wallH = Height;
        var frameH = Kit3D.H(RoomSigns.FrameHeight);
        var doorH = Kit3D.H(RoomSigns.DoorHeight);

        if (spare > 0.01)
        {
            Along(start, spare / 2, wallT, 0, wallH, WallFace);
            Along(gapStart + (leafTiles * leaves), spare / 2, wallT, 0, wallH, WallFace);
        }

        const double post = 0.14;
        var openFrom = leaves == 0 ? start : gapStart;
        var openTo = leaves == 0 ? start + doorTiles : gapStart + (leafTiles * leaves);
        Along(openFrom, post, wallT + 0.04, 0, frameH, FrameFace);
        Along(openTo - post, post, wallT + 0.04, 0, frameH, FrameFace);
        Along(openFrom, openTo - openFrom, wallT + 0.04, doorH, frameH, FrameFace);
        if (wallH > frameH)
        {
            Along(start, doorTiles, wallT, frameH, wallH, WallFace);
        }

        batch.AddTo(this);
        if (space.Kind == CareSpaceKind.Lift)
        {
            var (lmx, lmy) = RoomSigns.DoorMiddle(space);
            AddLiftDoors(space, openFrom, openTo, line, horizontal, o, lmx, lmy);
            return;
        }

        if (leaves == 0)
        {
            return;
        }

        // Leaves hinge at the frame and swing into the room (away from the outward side).
        var unit = horizontal ? new Vector3(1, 0, 0) : new Vector3(0, 0, 1);
        var inward = new Vector3(-o.X, 0, -o.Y);
        var hinges = new Node3D[leaves];
        var rest = new float[leaves];
        for (var i = 0; i < leaves; i++)
        {
            var hingeS = i == 0 ? openFrom + post : openTo - post;
            var dir = i == 0 ? 1f : -1f;
            var width = (float)(leafTiles - post);
            var hingeAt = horizontal ? new Vector3((float)hingeS, 0, (float)line) : new Vector3((float)line, 0, (float)hingeS);
            var hinge = new Node3D { Name = $"Leaf{i}", Position = hingeAt };
            AddChild(hinge);

            // The leaf is built along the hinge's local +x; the hinge turns it to lie along the door line.
            var closed = unit * dir;
            var yaw = Mathf.Atan2(-closed.Z, closed.X);
            var openYaw = Mathf.Atan2(-inward.Z, inward.X);
            var sign = Mathf.Wrap(openYaw - yaw, -Mathf.Pi, Mathf.Pi) > 0 ? 1f : -1f;
            hinge.SetMeta("swing", sign);
            rest[i] = yaw;
            hinge.Rotation = new Vector3(0, yaw, 0);

            var leafH = doorH - Kit3D.H(2);
            var leaf = new MeshBatch();
            leaf.Box(new Vector3(width / 2, leafH / 2, 0), new Vector3(width, leafH, 0.07f), LeafFace);
            leaf.Box(new Vector3(width / 2, Kit3D.H(5), 0), new Vector3(width * 0.76f, Kit3D.H(8), 0.08f), KickPlate);
            leaf.Box(new Vector3(width - 0.03f, leafH / 2, 0), new Vector3(0.05f, leafH, 0.075f), LeafEdge);
            leaf.Box(new Vector3(width * 0.85f, leafH * 0.5f, 0), new Vector3(0.04f, Kit3D.H(7), 0.14f), Handle);
            leaf.AddTo(hinge);
            Kit3D.Box(hinge, new Vector3(width / 2, leafH * 0.71f, 0), new Vector3(width * 0.4f, leafH * 0.3f, 0.085f), Glass, Finish.Gloss, shadow: false);
            hinges[i] = hinge;
        }

        var (mx, my) = RoomSigns.DoorMiddle(space);
        _doors.Add((space, mx, my, hinges, rest));
    }
}
