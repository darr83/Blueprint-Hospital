using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>A decoration standing on the floor: a Christmas tree, a pumpkin or a basket of eggs, centred on a tile.</summary>
public sealed record DecorPiece(string Kind, double X, double Y, int Seed);

/// <summary>
/// A string of bunting (or tinsel) along the inside of a wall. It runs so that the room is on its left
/// (the direction (-dy, dx) from the line points into the room), which is the way the flags hang.
/// </summary>
public sealed record DecorRun(double X1, double Y1, double X2, double Y2, IReadOnlyList<Color> Colours, bool Tinsel);

/// <summary>
/// Where the seasonal decorations go, shared by the 2D and 3D views: in December a Christmas tree in
/// each waiting room and ward and tinsel round their walls; at Halloween pumpkins in the waiting rooms
/// and orange and purple bunting; at Easter pastel bunting and a basket of eggs. They go up by
/// themselves (see the calendar events) unless the player has turned them off in Settings.
/// </summary>
public static class SeasonalDecor
{
    public static readonly Color[] TinselColours = { new("#C62828"), new("#2E7D32"), new("#F2C230"), new("#C0C7CE") };
    public static readonly Color[] HalloweenColours = { new("#F57C00"), new("#6A1B9A"), new("#212121") };
    public static readonly Color[] EasterColours = { new("#F8BBD0"), new("#FFF59D"), new("#B3E5FC"), new("#C5E1A5"), new("#D1C4E9") };

    /// <summary>The decorations up now: "christmas", "halloween", "easter" or "" (none, or turned off).</summary>
    public static string Current(HospitalSimulation sim) =>
        Override ?? (BlueprintHospital.Core.GameManager.Instance?.Settings.SeasonalDecorations == false ? string.Empty : sim.DecorationsNow);

    /// <summary>For review shots: shows these decorations whatever the date.</summary>
    public static string? Override { get; set; }

    /// <summary>Where everything goes for a kind of decorations.</summary>
    public static (IReadOnlyList<DecorPiece> Pieces, IReadOnlyList<DecorRun> Runs) Plan(HospitalSimulation sim, string kind)
    {
        var pieces = new List<DecorPiece>();
        var runs = new List<DecorRun>();
        if (kind.Length == 0)
        {
            return (pieces, runs);
        }

        var seats = sim.Chairs.Select(c => c.Tile).ToHashSet();
        foreach (var room in sim.Spaces.Where(r => r.HasWalls && r.Kind is CareSpaceKind.Waiting or CareSpaceKind.Ward or CareSpaceKind.Cafeteria))
        {
            var colours = kind switch
            {
                "christmas" => TinselColours,
                "halloween" => HalloweenColours,
                _ => EasterColours,
            };
            if (kind != "halloween" || room.Kind != CareSpaceKind.Ward)
            {
                Bunting(room, colours, kind == "christmas", runs);
            }

            var spots = FreeCorners(sim, room, seats).ToList();
            switch (kind)
            {
                case "christmas" when room.Kind is CareSpaceKind.Waiting or CareSpaceKind.Ward && spots.Count > 0:
                    pieces.Add(new DecorPiece("tree", Inward(room, spots[0]).X, Inward(room, spots[0]).Y, Seed(room)));
                    break;
                case "halloween" when room.Kind == CareSpaceKind.Waiting:
                    foreach (var (spot, i) in spots.Take(2).Select((s, i) => (s, i)))
                    {
                        pieces.Add(new DecorPiece("pumpkin", Inward(room, spot).X, Inward(room, spot).Y, Seed(room) + i));
                    }

                    break;
                case "easter" when room.Kind == CareSpaceKind.Waiting && spots.Count > 0:
                    pieces.Add(new DecorPiece("eggs", Inward(room, spots[0]).X, Inward(room, spots[0]).Y, Seed(room)));
                    break;
            }
        }

        return (pieces, runs);
    }

    /// <summary>The middle of a tile by the wall, moved a little into the room so a tree does not hang over the wall.</summary>
    private static (double X, double Y) Inward(CareSpace room, GridCoord c)
    {
        var r = room.Rect;
        var x = c.X + 0.5 + (c.X == r.X ? 0.6 : c.X == r.Right - 1 ? -0.6 : 0);
        var y = c.Y + 0.5 + (c.Y == r.Y ? 0.6 : c.Y == r.Bottom - 1 ? -0.6 : 0);
        return (x, y);
    }

    private static int Seed(CareSpace room) => room.Rect.X * 131 + room.Rect.Y * 17;

    /// <summary>Bunting along each wall of the room, broken where the doors are.</summary>
    private static void Bunting(CareSpace room, IReadOnlyList<Color> colours, bool tinsel, List<DecorRun> runs)
    {
        const double inset = 0.3;
        var r = room.Rect;
        var doors = room.Doors.Concat(room.LinkDoors).ToList();
        foreach (var side in new[] { WallSide.North, WallSide.East, WallSide.South, WallSide.West })
        {
            var length = side is WallSide.North or WallSide.South ? r.Width : r.Length;
            var gaps = doors.Where(d => d.Side == side).Select(d => (From: d.Offset - 0.2, To: d.Offset + d.Width + 0.2)).OrderBy(g => g.From).ToList();
            var at = 0.3;
            foreach (var (from, to) in gaps.Append((length - 0.3, length + 1.0)))
            {
                if (from - at >= 1)
                {
                    runs.Add(Along(side, r, at, from, inset, colours, tinsel));
                }

                at = Math.Max(at, to);
            }
        }
    }

    private static DecorRun Along(WallSide side, GridRect r, double from, double to, double inset, IReadOnlyList<Color> colours, bool tinsel) => side switch
    {
        WallSide.North => new DecorRun(r.X + from, r.Y + inset, r.X + to, r.Y + inset, colours, tinsel),
        WallSide.South => new DecorRun(r.X + to, r.Bottom - inset, r.X + from, r.Bottom - inset, colours, tinsel),
        WallSide.West => new DecorRun(r.X + inset, r.Y + to, r.X + inset, r.Y + from, colours, tinsel),
        _ => new DecorRun(r.Right - inset, r.Y + from, r.Right - inset, r.Y + to, colours, tinsel),
    };

    /// <summary>Something stands right next to it (a chair or furniture), so a tree there would be in the way.</summary>
    private static bool Crowded(HospitalSimulation sim, CareSpace room, GridCoord c, HashSet<GridCoord> chairs) =>
        room.Furniture.Any(f => f.Overlaps(c.X - 0.4, c.Y - 0.4, 1.8, 1.8))
        || Decorations.FloorPieces.Any(d => Math.Abs(d.X - (c.X + 0.5)) < 1.6 && Math.Abs(d.Y - (c.Y + 0.5)) < 1.6)
        || chairs.Any(t => Math.Max(Math.Abs(t.X - c.X), Math.Abs(t.Y - c.Y)) <= 1);

    /// <summary>Free tiles along the room's walls, the corners first, well apart and away from the doors.</summary>
    private static IEnumerable<GridCoord> FreeCorners(HospitalSimulation sim, CareSpace room, HashSet<GridCoord> chairs)
    {
        var r = room.Rect;
        if (r.Width < 3 || r.Length < 3)
        {
            yield break;
        }

        var doorTiles = room.Doors.Concat(room.LinkDoors).SelectMany(d => Enumerable.Range(d.Offset, d.Width).Select(i => d.Side switch
        {
            WallSide.North => new GridCoord(r.X + i, r.Y),
            WallSide.South => new GridCoord(r.X + i, r.Bottom - 1),
            WallSide.West => new GridCoord(r.X, r.Y + i),
            _ => new GridCoord(r.Right - 1, r.Y + i),
        })).ToList();
        var corners = new[]
        {
            new GridCoord(r.X, r.Y), new GridCoord(r.Right - 1, r.Y), new GridCoord(r.X, r.Bottom - 1), new GridCoord(r.Right - 1, r.Bottom - 1),
        };

        // Tiles along the walls, the corners first (the back corner first, which the near walls don't hide in the usual view), away from the doors.
        int FromCorner(GridCoord c) => corners.Min(k => Math.Abs(k.X - c.X) + Math.Abs(k.Y - c.Y));
        int FromDoor(GridCoord c) => doorTiles.Count == 0 ? 99 : doorTiles.Min(d => Math.Abs(d.X - c.X) + Math.Abs(d.Y - c.Y));
        var edge = r.Tiles().Where(t => r.IsOnEdge(t)).OrderBy(FromCorner).ThenBy(c => c.X - r.X + c.Y - r.Y).ThenByDescending(FromDoor).ToList();
        var taken = new List<GridCoord>();
        foreach (var c in edge)
        {
            if (taken.Any(t => Math.Max(Math.Abs(t.X - c.X), Math.Abs(t.Y - c.Y)) < 3))
            {
                continue;
            }

            var near = doorTiles.Any(d => Math.Max(Math.Abs(d.X - c.X), Math.Abs(d.Y - c.Y)) <= 1);
            var blocked = room.Furniture.Any(f => f.Overlaps(c.X + 0.1, c.Y + 0.1, 0.8, 0.8))
                          || chairs.Contains(c) || sim.IsSeat(c) || room.Seats.Contains(c) || room.QueueTiles.Contains(c)
                          || room.Beds.Any(b => b.PatientSpot == c || b.StaffSpot == c);
            if (!near && !blocked && !Crowded(sim, room, c, chairs))
            {
                taken.Add(c);
                yield return c;
            }
        }
    }
}
