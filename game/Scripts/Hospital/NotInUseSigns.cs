using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>Where a red "Not in use" sign stands: the middle of the room, turned to face out through its door.</summary>
public readonly record struct NotInUseSign(double X, double Y, (double X, double Y) Facing, string RoomName);

/// <summary>
/// The red "NOT IN USE" signs in rooms that are built but not joined up to a corridor yet (see
/// <see cref="CareSpace.InUse"/>): a floor sign and a red label in the middle of the room, in both the 2D and 3D views.
/// </summary>
public static class NotInUseSigns
{
    public static readonly Color Red = new("#C62828");

    public const string Text = "NOT IN USE";

    /// <summary>The smaller line under it on the 3D floor sign.</summary>
    public const string Body = "No corridor\nto the door";

    /// <summary>Something that changes when the signs should be redrawn.</summary>
    public static string Key(HospitalSimulation sim) =>
        string.Join(";", sim.Spaces.Where(s => !s.InUse).Select(s => s.Id).OrderBy(id => id, System.StringComparer.Ordinal));

    public static List<NotInUseSign> Plan(HospitalSimulation sim)
    {
        var signs = new List<NotInUseSign>();
        foreach (var room in sim.Spaces.Where(s => !s.InUse && s.HasWalls).OrderBy(s => s.Id, System.StringComparer.Ordinal))
        {
            var r = room.Rect;
            var facing = (room.Doors.Count > 0 ? room.Doors[0].Side : room.DoorSide) switch
            {
                WallSide.North => (0.0, -1.0),
                WallSide.South => (0.0, 1.0),
                WallSide.West => (-1.0, 0.0),
                _ => (1.0, 0.0),
            };
            signs.Add(new NotInUseSign(r.X + (r.Width / 2.0), r.Y + (r.Length / 2.0), facing, room.Name));
        }

        return signs;
    }
}
