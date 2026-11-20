using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Hospital;

/// <summary>
/// Joins each anaesthetic room to the operating theatre it is built against. The two rooms share a
/// wall; a pair of automatic doors is cut into the middle of the shared stretch (never at a corner),
/// so a patient on a trolley can be wheeled straight from one into the other.
/// </summary>
public static class TheatreSuite
{
    /// <summary>The automatic doors are 2 m wide (4 tiles) where the shared wall allows it.</summary>
    public const int LinkDoorTiles = 4;

    /// <summary>The shortest doorway a trolley fits through (1 m).</summary>
    public const int MinLinkDoorTiles = 2;

    /// <summary>
    /// The doorway through the wall that <paramref name="a"/> and <paramref name="b"/> share, as seen from
    /// each room, or null if they don't share enough wall. Corner tiles of either room are left out.
    /// </summary>
    public static (Doorway InA, Doorway InB)? SharedDoor(GridRect a, GridRect b)
    {
        // b east of a, west of a, south of a, north of a.
        if (a.Right == b.X)
        {
            return Along(a.Y, a.Bottom, b.Y, b.Bottom, start => (new Doorway(WallSide.East, start - a.Y, 0), new Doorway(WallSide.West, start - b.Y, 0)));
        }

        if (b.Right == a.X)
        {
            return Along(a.Y, a.Bottom, b.Y, b.Bottom, start => (new Doorway(WallSide.West, start - a.Y, 0), new Doorway(WallSide.East, start - b.Y, 0)));
        }

        if (a.Bottom == b.Y)
        {
            return Along(a.X, a.Right, b.X, b.Right, start => (new Doorway(WallSide.South, start - a.X, 0), new Doorway(WallSide.North, start - b.X, 0)));
        }

        if (b.Bottom == a.Y)
        {
            return Along(a.X, a.Right, b.X, b.Right, start => (new Doorway(WallSide.North, start - a.X, 0), new Doorway(WallSide.South, start - b.X, 0)));
        }

        return null;
    }

    private static (Doorway, Doorway)? Along(int a0, int a1, int b0, int b1, Func<int, (Doorway A, Doorway B)> make)
    {
        // Skip each room's corner tile at both ends of the shared stretch.
        var lo = Math.Max(a0 + 1, b0 + 1);
        var hi = Math.Min(a1 - 1, b1 - 1);
        var run = hi - lo;
        if (run < MinLinkDoorTiles)
        {
            return null;
        }

        var width = Math.Min(LinkDoorTiles, run);
        var start = lo + ((run - width) / 2);
        var (da, db) = make(start);
        return (da with { Width = width }, db with { Width = width });
    }

    /// <summary>
    /// Pairs every anaesthetic room with a theatre it shares a wall with (each theatre takes one), sets the
    /// doorways on both rooms and takes out any furniture standing in the way of the doors. Returns the pairs.
    /// </summary>
    public static IReadOnlyList<(CareSpace Theatre, CareSpace AnaestheticRoom)> Link(IEnumerable<CareSpace> spaces)
    {
        var all = spaces.ToList();
        foreach (var space in all.Where(s => s.Kind is CareSpaceKind.Theatre or CareSpaceKind.AnaestheticRoom))
        {
            space.LinkDoors.Clear();
            space.LinkedRoomId = null;
            space.Furniture = space.LaidOutFurniture;
        }

        var pairs = new List<(CareSpace, CareSpace)>();
        var theatres = all.Where(s => s.Kind == CareSpaceKind.Theatre).OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        foreach (var room in all.Where(s => s.Kind == CareSpaceKind.AnaestheticRoom).OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            foreach (var theatre in theatres.Where(t => t.LinkedRoomId is null))
            {
                if (SharedDoor(room.Rect, theatre.Rect) is not { } door)
                {
                    continue;
                }

                room.LinkDoors.Add(door.InA);
                theatre.LinkDoors.Add(door.InB);
                room.LinkedRoomId = theatre.Id;
                theatre.LinkedRoomId = room.Id;
                ClearDoorway(room, door.InA);
                ClearDoorway(theatre, door.InB);
                pairs.Add((theatre, room));
                break;
            }
        }

        return pairs;
    }

    /// <summary>The tiles just inside a doorway (two deep), which must be kept clear of furniture.</summary>
    public static IEnumerable<GridCoord> ApproachTiles(GridRect r, Doorway d, int depth = 2)
    {
        for (var i = 0; i < d.Width; i++)
        {
            for (var k = 0; k < depth; k++)
            {
                yield return d.Side switch
                {
                    WallSide.North => new GridCoord(r.X + d.Offset + i, r.Y + k),
                    WallSide.South => new GridCoord(r.X + d.Offset + i, r.Bottom - 1 - k),
                    WallSide.West => new GridCoord(r.X + k, r.Y + d.Offset + i),
                    _ => new GridCoord(r.Right - 1 - k, r.Y + d.Offset + i),
                };
            }
        }
    }

    private static void ClearDoorway(CareSpace room, Doorway door)
    {
        var tiles = ApproachTiles(room.Rect, door).ToList();
        // The table (or trolley) the patient lies on always stays.
        bool Table(PlacedFurniture f) => f.Overlaps(room.PatientSpot.X, room.PatientSpot.Y, 1, 1);
        room.Furniture = room.Furniture.Where(f => Table(f) || !tiles.Any(t => f.Overlaps(t.X, t.Y, 1, 1))).ToList();
    }
}
