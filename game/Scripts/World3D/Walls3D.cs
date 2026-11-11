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
    }

    /// <summary>Swings each door open or shut, following whoever is near it.</summary>
    public void Animate()
    {
        foreach (var (space, mx, my, hinges, rest) in _doors)
        {
            var open = _view.World.DoorOpenness(space, mx, my) * (float)(Math.PI * 0.45);
            for (var i = 0; i < hinges.Length; i++)
            {
                hinges[i].Rotation = new Vector3(0, rest[i] + (open * (float)hinges[i].GetMeta("swing", 1f)), 0);
            }
        }
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
