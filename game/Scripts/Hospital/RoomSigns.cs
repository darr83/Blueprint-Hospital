using System;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Doorways of walled rooms: a frame, single or double doors that swing open as people come
/// and go, and the room's name on a board standing on top of the frame.
/// </summary>
public static class RoomSigns
{
    private static Font? _sign;

    private static Font Sign => _sign ??= new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = 0.5f };

    /// <summary>Rooms that get a name: walled rooms with a door, other than lifts and stairs.</summary>
    public static bool Named(CareSpace space) => space.HasWalls && space.Doors.Count > 0 && space.Kind != CareSpaceKind.Corridor;

    /// <summary>The colour used for a room's name: its wayfinding colour, or a slate grey.</summary>
    public static Color Colour(CareSpace space) => DepartmentRenderer.WayfindingColour(space) ?? new Color("#5D7080");

    /// <summary>The wall line a door sits in, and its two ends along it, plus the outward direction.</summary>
    private static ((double X, double Y) A, (double X, double Y) B, (double X, double Y) Out) DoorLine(CareSpace space)
    {
        var r = space.Rect;
        var d = space.Doors[0];
        return d.Side switch
        {
            WallSide.North => ((r.X + d.Offset, r.Y), (r.X + d.Offset + d.Width, r.Y), (0, -1)),
            WallSide.South => ((r.X + d.Offset, r.Bottom), (r.X + d.Offset + d.Width, r.Bottom), (0, 1)),
            WallSide.West => ((r.X, r.Y + d.Offset), (r.X, r.Y + d.Offset + d.Width), (-1, 0)),
            _ => ((r.Right, r.Y + d.Offset), (r.Right, r.Y + d.Offset + d.Width), (1, 0)),
        };
    }

    // ------------------------------------------------------------------ door, frame and sign

    /// <summary>Clear height of a doorway on screen (about 2.3 m), so people walk under the frame.</summary>
    public const float DoorHeight = 64f;

    /// <summary>Height of the top of a door frame; tall walls are built to the same height.</summary>
    public const float FrameHeight = 70f;

    /// <summary>What hangs in a doorway.</summary>
    public enum DoorKind
    {
        /// <summary>No door: open-plan public areas, lifts and stairs.</summary>
        None,

        /// <summary>One leaf, for staff-only rooms and small rooms people walk into.</summary>
        Single,

        /// <summary>Two leaves, for rooms patients are wheeled into on a bed or trolley.</summary>
        Double,
    }

    public static DoorKind KindOf(CareSpace space) => space.Kind switch
    {
        _ when space.IsTreatmentSpace || space.IsWard => DoorKind.Double,
        CareSpaceKind.Theatre or CareSpaceKind.CT or CareSpaceKind.XRay or CareSpaceKind.CathLab
            or CareSpaceKind.Endoscopy or CareSpaceKind.EndoscopyRecovery or CareSpaceKind.DeliveryRoom => DoorKind.Double,
        CareSpaceKind.Waiting or CareSpaceKind.Cafeteria or CareSpaceKind.VendingPoint or CareSpaceKind.OutpatientReception
            or CareSpaceKind.Lift or CareSpaceKind.Stairs => DoorKind.None,
        _ => DoorKind.Single,
    };

    /// <summary>The middle of a room's doorway, in tiles.</summary>
    public static (double X, double Y) DoorMiddle(CareSpace space)
    {
        var (a, b, _) = DoorLine(space);
        return ((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    }

    /// <summary>The footprint of the door frame, for sorting it among the walls.</summary>
    public static (double X, double Y, double W, double L) SignFootprint(CareSpace space)
    {
        var (a, b, o) = DoorLine(space);
        const double t = 0.12;
        return o.X == 0
            ? (Math.Min(a.X, b.X), a.Y - (t / 2), Math.Abs(b.X - a.X), t)
            : (a.X - (t / 2), Math.Min(a.Y, b.Y), t, Math.Abs(b.Y - a.Y));
    }

    private static readonly Color LeafFace = new("#E7D9BF");
    private static readonly Color LeafEdge = new("#B9A27E");
    private static readonly Color Glass = new("#A8CBDC");
    private static readonly Color KickPlate = new("#B7C1C9");

    /// <summary>
    /// A doorway: frame posts and a lintel at door height, single or double doors that swing
    /// into the room by <paramref name="open"/> (0 shut, 1 wide open), and the room's name on a
    /// board standing on the frame, lettered on whichever face is towards the viewer.
    /// </summary>
    public static void Doorway(CanvasItem c, CareSpace space, float open, double wallThickness, float wallHeight, Color wallTop, Color wallFace, Color frameTop, Color frameFace)
    {
        var node = (Node2D)c;
        var (a, b, o) = DoorLine(space);
        var horizontal = o.X == 0;
        var unit = horizontal ? (X: 1.0, Y: 0.0) : (X: 0.0, Y: 1.0);
        var start = horizontal ? Math.Min(a.X, b.X) : Math.Min(a.Y, b.Y);
        var doorTiles = horizontal ? Math.Abs(b.X - a.X) : Math.Abs(b.Y - a.Y);
        var line = horizontal ? a.Y : a.X;
        var h = wallThickness / 2;

        // A box along the door line from s to s + len (tiles), between two heights.
        void Along(double s, double len, double thick, float from, float to, Color top, Color face)
        {
            if (horizontal)
            {
                IsoDraw.Prism(c, s, line - (thick / 2), len, thick, to - from, top, face, lift: from);
            }
            else
            {
                IsoDraw.Prism(c, line - (thick / 2), s, thick, len, to - from, top, face, lift: from);
            }
        }

        (double X, double Y) At(double s, double inward) => horizontal
            ? (s, line - (o.Y * inward))
            : (line - (o.X * inward), s);

        // Leaves no wider than a metre (2 tiles) each; any spare width is a fixed panel beside them.
        var kind = KindOf(space);
        var leaves = kind switch { DoorKind.Double => 2, DoorKind.Single => 1, _ => 0 };
        var leafTiles = leaves == 0 ? 0 : Math.Min(doorTiles / leaves, 2.0);
        var spare = doorTiles - (leafTiles * leaves);
        var gapStart = start + (spare / 2);

        // Infill panels either side of the doors, built up like wall.
        if (spare > 0.01)
        {
            Along(start, spare / 2, wallThickness, 0, wallHeight, wallTop, wallFace);
            Along(gapStart + (leafTiles * leaves), spare / 2, wallThickness, 0, wallHeight, wallTop, wallFace);
        }

        // Frame posts each side of the opening, a lintel across, and wall above it up to the wall top.
        const double post = 0.14;
        var openFrom = leaves == 0 ? start : gapStart;
        var openTo = leaves == 0 ? start + doorTiles : gapStart + (leafTiles * leaves);
        Along(openFrom, post, wallThickness + 0.04, 0, FrameHeight, frameTop, frameFace);
        Along(openTo - post, post, wallThickness + 0.04, 0, FrameHeight, frameTop, frameFace);
        Along(openFrom, openTo - openFrom, wallThickness + 0.04, DoorHeight, FrameHeight, frameTop, frameFace);
        if (wallHeight > FrameHeight)
        {
            Along(start, doorTiles, wallThickness, FrameHeight, wallHeight, wallTop, wallFace);
        }

        // The door leaves, hinged at the frame and swinging into the room.
        var angle = open * (float)(Math.PI * 0.45);
        var up = new Vector2(0, -(DoorHeight - 2));
        for (var i = 0; i < leaves; i++)
        {
            var hingeS = i == 0 ? openFrom + post : openTo - post;
            var dir = i == 0 ? 1 : -1;
            var w = leafTiles - post;
            var hinge = At(hingeS, 0);
            var edge = At(hingeS + (dir * w * Math.Cos(angle)), w * Math.Sin(angle));
            var p0 = IsoDraw.ToScreen(hinge.X, hinge.Y);
            var p1 = IsoDraw.ToScreen(edge.X, edge.Y);
            Vector2 P(float u, float v) => p0.Lerp(p1, u) + (up * v);
            c.DrawColoredPolygon(new[] { P(0, 0), P(1, 0), P(1, 1), P(0, 1) }, LeafFace.Darkened(0.06f * open));
            c.DrawColoredPolygon(new[] { P(0.12f, 0.03f), P(0.88f, 0.03f), P(0.88f, 0.16f), P(0.12f, 0.16f) }, KickPlate);
            c.DrawColoredPolygon(new[] { P(0.3f, 0.55f), P(0.7f, 0.55f), P(0.7f, 0.88f), P(0.3f, 0.88f) }, Glass);
            c.DrawPolyline(new[] { P(0, 0), P(1, 0), P(1, 1), P(0, 1), P(0, 0) }, LeafEdge, 1f, true);
            var handle = P(0.85f, 0.48f);
            c.DrawLine(handle, handle + new Vector2(0, -6), new Color("#7A8792"), 2f, true);
        }

        // The name board standing on the frame.
        var name = space.Name;
        const int size = 15;
        const float boardHeight = 21f;
        const float stripe = 3f;
        const double t = 0.1;
        var font = Sign;
        var textWidth = font.GetStringSize(name, HorizontalAlignment.Left, -1, size).X;
        var mid = DoorMiddle(space);
        var tilePx = (IsoDraw.ToScreen(mid.X + unit.X, mid.Y + unit.Y) - IsoDraw.ToScreen(mid.X, mid.Y)).Length();
        var boardTiles = Math.Max(Math.Min(openTo - openFrom, 4), (textWidth + 18) / tilePx);
        var boardBase = Math.Max(FrameHeight, wallHeight) + 2;
        Along((horizontal ? mid.X : mid.Y) - (boardTiles / 2), boardTiles, t, boardBase, boardBase + boardHeight, new Color("#E9EEF2"), new Color("#FBFCFD"));

        var faceOffset = (X: o.X * ((t / 2) + 0.005), Y: o.Y * ((t / 2) + 0.005));
        var front = IsoDraw.ToScreen(mid.X + faceOffset.X, mid.Y + faceOffset.Y).Y >= IsoDraw.ToScreen(mid.X - faceOffset.X, mid.Y - faceOffset.Y).Y ? 1 : -1;
        var centre = (X: mid.X + (faceOffset.X * front), Y: mid.Y + (faceOffset.Y * front));
        var c0 = IsoDraw.ToScreen(centre.X, centre.Y);
        var along = IsoDraw.ToScreen(centre.X + unit.X, centre.Y + unit.Y) - c0;
        along /= along.Length();
        if (along.X < 0)
        {
            along = -along;
        }

        var faceTop = c0 + new Vector2(0, -(boardBase + boardHeight));
        var halfBoard = along * (float)(boardTiles * tilePx / 2);
        var stripeDown = new Vector2(0, stripe);
        c.DrawColoredPolygon(new[] { faceTop - halfBoard, faceTop + halfBoard, faceTop + halfBoard + stripeDown, faceTop - halfBoard + stripeDown }, Colour(space));
        var baseline = faceTop + new Vector2(0, stripe + 13.5f);
        var origin = baseline - (along * (textWidth / 2));
        c.DrawSetTransformMatrix(new Transform2D(along, new Vector2(0, 1), origin - node.Position));
        c.DrawString(font, Vector2.Zero, name, HorizontalAlignment.Left, -1, size, new Color("#1D2B36"));
        c.DrawSetTransform(-node.Position);
    }
}
