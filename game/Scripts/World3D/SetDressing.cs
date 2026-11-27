using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// Hospital set dressing that makes a ward read as a ward: a bed-head service panel on the wall over
/// every bed (oxygen, air and suction outlets, sockets, a reading light and the nurse-call strip) and,
/// on wards, a ceiling curtain track round each bed with the curtains drawn back to the wall.
/// Corridors get handrails and corner guards, and room doors hand gel outside and glove boxes and a
/// sharps bin inside. Built with the wall decorations, so it goes when the walls are cut away.
/// </summary>
public static class SetDressing
{
    /// <summary>How far the head of a bed can be from its wall and still get a panel, in tiles.</summary>
    private const float Reach = 3.5f;

    /// <summary>Half the width of a curtained bed space, and how far the track runs past the middle of the bed, in tiles.</summary>
    private const float HalfBay = 1.45f;
    private const float PastMiddle = 2.6f;

    private static readonly Color Panel = new("#E8EDF1");
    private static readonly Color Stripe = new("#2E6FB0");
    private static readonly Color Lamp = new("#D7DEE3");
    private static readonly Color LampGlow = new("#FFF6D8");
    private static readonly Color Rail = new("#C9D1D8");
    private static readonly Color CurtainA = new("#8FB8D6");
    private static readonly Color CurtainB = new("#7FAACB");

    /// <summary>The wall each bed's head is against, as (tile column of the bed, the wall's line), or null when the bed stands clear of it.</summary>
    public static (float X, float Wall, float Into)? HeadWall(CareSpace space, WardBed bed)
    {
        var r = space.Rect;
        var wall = bed.HeadSouth ? r.Bottom : r.Y;
        if (Math.Abs(bed.PatientSpot.Y + 0.5f - wall) > Reach)
        {
            return null;
        }

        return (bed.PatientSpot.X + 0.5f, wall, bed.HeadSouth ? -1f : 1f);
    }

    /// <summary>Adds the panels (and curtain tracks) for every bed in <paramref name="space"/>; returns how many panels.</summary>
    public static int BedHeads(MeshBatch b, CareSpace space)
    {
        var was = b.Kind;
        var t = (float)WorldLayer.WallThickness / 2;
        var top = Walls3D.FullHeight;
        var curtained = space.IsWard && !space.Furniture.Any(f => f.ItemId is RoomLayoutEngine.BayCurtain or RoomLayoutEngine.BedroomWall);
        var count = 0;
        try
        {
            foreach (var bed in space.Beds)
            {
                if (HeadWall(space, bed) is not var (cx, wall, dir))
                {
                    continue;
                }

                var face = wall + (dir * (t + 0.03f));
                b.Kind = SurfaceKind.Laminate;
                b.Box(new Vector3(cx, Kit3D.M(1.45), face), new Vector3(2.2f, Kit3D.M(0.32), 0.07f), Panel);
                b.Box(new Vector3(cx, Kit3D.M(1.62), face + (dir * 0.005f)), new Vector3(2.2f, Kit3D.M(0.03), 0.075f), Stripe);
                b.Box(new Vector3(cx, Kit3D.M(1.85), face), new Vector3(0.5f, Kit3D.M(0.08), 0.12f), Lamp);
                b.Box(new Vector3(cx, Kit3D.M(1.81), face + (dir * 0.05f)), new Vector3(0.44f, Kit3D.M(0.015), 0.04f), LampGlow);

                // Medical gas outlets: oxygen (white), air (black and white), vacuum (yellow), with the green flowmeter's colour.
                var outlets = new[] { ("#F2F4F5", -0.55f), ("#1F7A3E", -0.35f), ("#2B2F33", -0.15f), ("#F2C230", 0.05f) };
                foreach (var (c, x) in outlets)
                {
                    b.Cylinder(new Vector3(cx + x, Kit3D.M(1.42), face + (dir * 0.05f)), 0.045f, 0.04f, 0.045f, new Color(c), new Basis(Vector3.Right, Mathf.Pi / 2));
                }

                // Two sockets with red switches.
                foreach (var x in new[] { 0.4f, 0.65f })
                {
                    b.Box(new Vector3(cx + x, Kit3D.M(1.42), face + (dir * 0.04f)), new Vector3(0.16f, Kit3D.M(0.08), 0.02f), new Color("#FAFAFA"));
                    b.Box(new Vector3(cx + x + 0.04f, Kit3D.M(1.44), face + (dir * 0.052f)), new Vector3(0.03f, Kit3D.M(0.02), 0.01f), new Color("#D32F2F"));
                }

                count++;
                if (!curtained)
                {
                    continue;
                }

                // Curtain track just under the top of the walls, the curtains gathered at the head wall.
                b.Kind = SurfaceKind.Metal;
                var rail = top - 0.08f;
                var foot = bed.PatientSpot.Y + 0.5f + (dir * PastMiddle);
                var start = wall + (dir * (t + 0.05f));
                foreach (var side in new[] { -HalfBay, HalfBay })
                {
                    b.Rod(new Vector3(cx + side, rail, start), new Vector3(cx + side, rail, foot), 0.025f, Rail, small: true);
                }

                b.Rod(new Vector3(cx - HalfBay, rail, foot), new Vector3(cx + HalfBay, rail, foot), 0.025f, Rail, small: true);
                b.Kind = SurfaceKind.Fabric;
                var hem = Kit3D.M(0.35);
                foreach (var side in new[] { -HalfBay, HalfBay })
                {
                    for (var k = 0; k < 4; k++)
                    {
                        var z = start + (dir * (0.12f + (k * 0.11f)));
                        b.Box(new Vector3(cx + side, (rail + hem) / 2, z), new Vector3(0.05f, rail - hem, 0.1f), k % 2 == 0 ? CurtainA : CurtainB);
                    }
                }
            }
        }
        finally
        {
            b.Kind = was;
        }

        return count;
    }

    private static readonly Color RailWood = new("#B98A5C");
    private static readonly Color Bracket = new("#B8C0C7");
    private static readonly Color Guard = new("#D9DEE2");
    private static readonly Color Dispenser = new("#F4F6F7");
    private static readonly Color DispenserFront = new("#2F80C9");
    private static readonly Color SharpsYellow = new("#F2C230");

    /// <summary>The tiles that are corridor (not inside a room) on the floor in view.</summary>
    private static HashSet<(int X, int Y)> CorridorTiles(WorldLayer world)
    {
        var tiles = new HashSet<(int X, int Y)>();
        var spaces = world.ViewSpaces.ToList();
        foreach (var space in spaces.Where(s => s.Kind == CareSpaceKind.Corridor))
        {
            for (var y = space.Rect.Y; y < space.Rect.Bottom; y++)
            {
                for (var x = space.Rect.X; x < space.Rect.Right; x++)
                {
                    tiles.Add((x, y));
                }
            }
        }

        foreach (var room in spaces.Where(s => s.HasWalls && s.Kind != CareSpaceKind.Corridor))
        {
            for (var y = room.Rect.Y; y < room.Rect.Bottom; y++)
            {
                for (var x = room.Rect.X; x < room.Rect.Right; x++)
                {
                    tiles.Remove((x, y));
                }
            }
        }

        return tiles;
    }

    /// <summary>
    /// Corridors: a timber handrail on brackets along every wall (broken at doorways) and a steel
    /// corner guard on every corner that sticks out into a corridor. Returns (rail lengths, guards).
    /// </summary>
    public static (int Rails, int Guards) Corridors(MeshBatch b, WorldLayer world)
    {
        var corridor = CorridorTiles(world);
        var walls = world.WallEdges;
        var doors = world.DoorEdges;
        var t = (float)WorldLayer.WallThickness / 2;
        var height = Kit3D.M(0.9);
        var was = b.Kind;
        var rails = 0;
        var guards = 0;
        try
        {
            foreach (var (x, y, horizontal) in walls)
            {
                // The two tiles either side of this piece of wall, and which way is out of the wall towards each.
                var sides = horizontal
                    ? new[] { ((x, y - 1), -1f), ((x, y), 1f) }
                    : new[] { ((x - 1, y), -1f), ((x, y), 1f) };
                foreach (var (tile, dir) in sides)
                {
                    if (!corridor.Contains(tile))
                    {
                        continue;
                    }

                    // Stop short of a doorway or a corner at either end.
                    var before = horizontal ? (x - 1, y, true) : (x, y - 1, false);
                    var after = horizontal ? (x + 1, y, true) : (x, y + 1, false);
                    var from = walls.Contains(before) ? 0f : 0.15f;
                    var to = walls.Contains(after) ? 1f : 0.85f;
                    var off = (float)(horizontal ? y : x) + (dir * (t + 0.07f));
                    var along0 = (horizontal ? x : y) + from;
                    var along1 = (horizontal ? x : y) + to;
                    Vector3 P(float along, float h, float o) => horizontal ? new Vector3(along, h, o) : new Vector3(o, h, along);

                    b.Kind = SurfaceKind.Wood;
                    b.Rod(P(along0, height, off), P(along1, height, off), 0.045f, RailWood, small: true);
                    b.Kind = SurfaceKind.Metal;
                    var mid = (along0 + along1) / 2;
                    b.Box(P(mid, height - 0.06f, (float)(horizontal ? y : x) + (dir * (t + 0.035f))), horizontal ? new Vector3(0.04f, 0.1f, 0.07f) : new Vector3(0.07f, 0.1f, 0.04f), Bracket);
                    rails++;
                }
            }

            // Corner guards: a grid point where a room's corner juts into the corridor (three of the four tiles round it are corridor).
            var points = walls.SelectMany(w => w.Horizontal ? new[] { (w.X, w.Y), (w.X + 1, w.Y) } : new[] { (w.X, w.Y), (w.X, w.Y + 1) }).Distinct();
            b.Kind = SurfaceKind.Metal;
            var tall = Kit3D.M(1.5);
            foreach (var (px, py) in points)
            {
                var around = new[] { (px - 1, py - 1), (px, py - 1), (px - 1, py), (px, py) };
                var open = around.Count(corridor.Contains);
                if (open != 3 || doors.Contains((px, py, true)) || doors.Contains((px - 1, py, true)) || doors.Contains((px, py, false)) || doors.Contains((px, py - 1, false)))
                {
                    continue;
                }

                var room = around.First(a => !corridor.Contains(a));
                var sx = room.Item1 < px ? 1f : -1f; // out of the room's corner, towards the corridor
                var sz = room.Item2 < py ? 1f : -1f;
                var c = new Vector3(px + (sx * t), tall / 2, py + (sz * t));
                b.Box(c + new Vector3(sx * 0.01f, 0, -sz * 0.09f), new Vector3(0.025f, tall, 0.2f), Guard);
                b.Box(c + new Vector3(-sx * 0.09f, 0, sz * 0.01f), new Vector3(0.2f, tall, 0.025f), Guard);
                guards++;
            }
        }
        finally
        {
            b.Kind = was;
        }

        return (rails, guards);
    }

    /// <summary>
    /// By the door of every clinical room and ward: a hand gel dispenser on the corridor side and,
    /// inside, a rack of glove boxes (small, medium, large) and a yellow sharps bin. Returns how many doors.
    /// </summary>
    public static int Doors(MeshBatch b, WorldLayer world)
    {
        var corridor = CorridorTiles(world);
        var t = (float)WorldLayer.WallThickness / 2;
        var was = b.Kind;
        var count = 0;
        try
        {
            foreach (var space in world.ViewSpaces.Where(s => s.HasWalls && s.Kind != CareSpaceKind.Corridor && s.Doors.Count > 0))
            {
                var clinical = space.IsTreatmentSpace || space.HasBeds || space.Kind is CareSpaceKind.Triage or CareSpaceKind.Clinic;
                var d = space.Doors[0];
                var r = space.Rect;
                var horizontal = d.Side is WallSide.North or WallSide.South;
                var line = d.Side switch { WallSide.North => r.Y, WallSide.South => r.Bottom, WallSide.West => r.X, _ => r.Right };
                var outward = d.Side is WallSide.North or WallSide.West ? -1f : 1f;
                var start = (horizontal ? r.X : r.Y) + d.Offset;
                var end = start + d.Width;
                var length = horizontal ? r.Width : r.Length;
                var origin = horizontal ? r.X : r.Y;
                Vector3 P(float along, float h, float o) => horizontal ? new Vector3(along, h, o) : new Vector3(o, h, along);

                // Which side of the door has room on the wall (prefer the side away from the room's corner).
                var right = end + 0.5f <= origin + length - 0.3f;
                var at = right ? end + 0.35f : start - 0.35f;

                // Outside: hand gel, where the door opens onto a corridor.
                var outside = d.Side switch
                {
                    WallSide.North => (start, r.Y - 1),
                    WallSide.South => (start, r.Bottom),
                    WallSide.West => (r.X - 1, start),
                    _ => (r.Right, start),
                };
                if (corridor.Contains(outside))
                {
                    var face = line + (outward * (t + 0.04f));
                    b.Kind = SurfaceKind.Laminate;
                    b.Box(P(at, Kit3D.M(1.25), face), horizontal ? new Vector3(0.2f, Kit3D.M(0.25), 0.08f) : new Vector3(0.08f, Kit3D.M(0.25), 0.2f), Dispenser);
                    b.Box(P(at, Kit3D.M(1.28), face + (outward * 0.042f)), horizontal ? new Vector3(0.14f, Kit3D.M(0.1), 0.01f) : new Vector3(0.01f, Kit3D.M(0.1), 0.14f), DispenserFront);
                    b.Box(P(at, Kit3D.M(1.1), face + (outward * 0.03f)), horizontal ? new Vector3(0.08f, Kit3D.M(0.03), 0.06f) : new Vector3(0.06f, Kit3D.M(0.03), 0.08f), Dispenser);
                }

                if (!clinical)
                {
                    count++;
                    continue;
                }

                // Inside: three glove boxes in a wire rack, and a sharps bin below.
                var inner = line - (outward * (t + 0.06f));
                b.Kind = SurfaceKind.Laminate;
                var gloves = new[] { new Color("#7E57C2"), new Color("#2F80C9"), new Color("#26A69A") };
                for (var k = 0; k < 3; k++)
                {
                    var along = at + ((right ? 1 : -1) * (0.05f + (k * 0.26f)));
                    b.Box(P(along, Kit3D.M(1.3), inner), horizontal ? new Vector3(0.24f, Kit3D.M(0.13), 0.11f) : new Vector3(0.11f, Kit3D.M(0.13), 0.24f), Dispenser);
                    b.Box(P(along, Kit3D.M(1.3), inner - (outward * 0.056f)), horizontal ? new Vector3(0.2f, Kit3D.M(0.06), 0.01f) : new Vector3(0.01f, Kit3D.M(0.06), 0.2f), gloves[k]);
                }

                var bin = at + ((right ? 1 : -1) * 0.3f);
                b.Box(P(bin, Kit3D.M(0.95), inner), horizontal ? new Vector3(0.26f, Kit3D.M(0.3), 0.18f) : new Vector3(0.18f, Kit3D.M(0.3), 0.26f), SharpsYellow);
                b.Box(P(bin, Kit3D.M(1.115), inner), horizontal ? new Vector3(0.27f, Kit3D.M(0.04), 0.19f) : new Vector3(0.19f, Kit3D.M(0.04), 0.27f), new Color("#D32F2F"));
                count++;
            }
        }
        finally
        {
            b.Kind = was;
        }

        return count;
    }
}
