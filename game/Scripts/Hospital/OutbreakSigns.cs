using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>One yellow floor sign outside a ward with norovirus.</summary>
/// <param name="X">Where it stands, in tiles.</param>
/// <param name="Y">Where it stands, in tiles.</param>
/// <param name="Facing">Which way its face looks (out from the ward door), as a unit step in tiles.</param>
/// <param name="Closed">The ward is closed to admissions (else it only warns of norovirus).</param>
public sealed record OutbreakSign(double X, double Y, (double X, double Y) Facing, string Ward, bool Closed);

/// <summary>
/// The signs that go up outside a ward with norovirus, shared by the 2D and 3D views: a yellow A-frame by
/// its door saying "Norovirus: please check with staff before visiting", or "Ward closed" once it shuts to
/// admissions. The ward's name label also says so.
/// </summary>
public static class OutbreakSigns
{
    public static readonly Color Yellow = new("#F6C915");
    public static readonly Color Ink = new("#1E2328");
    public static readonly Color Red = new("#C62828");

    /// <summary>The top line of a sign.</summary>
    public static string Heading(OutbreakSign sign) => sign.Closed ? "WARD CLOSED" : "NOROVIRUS";

    /// <summary>The smaller lines under it.</summary>
    public static string Body(OutbreakSign sign) => sign.Closed ? "Norovirus\nNo admissions" : "Check with staff\nbefore visiting";

    /// <summary>A short note for the ward's name label, or null if the ward has no outbreak.</summary>
    public static string? LabelNote(HospitalSimulation sim, CareSpace ward) =>
        sim.OutbreakOn(ward.Id) is not { } o ? null : o.IsClosedAt(sim.Now) ? "CLOSED · NOROVIRUS" : "NOROVIRUS";

    /// <summary>Something that changes when the signs should be redrawn.</summary>
    public static string Key(HospitalSimulation sim) =>
        string.Join(";", sim.Outbreaks.Select(o => $"{o.WardId}:{o.IsClosedAt(sim.Now)}"));

    public static List<OutbreakSign> Plan(HospitalSimulation sim)
    {
        var signs = new List<OutbreakSign>();
        foreach (var outbreak in sim.Outbreaks)
        {
            if (sim.FindSpace(outbreak.WardId) is not { Doors.Count: > 0 } ward)
            {
                continue;
            }

            var r = ward.Rect;
            var d = ward.Doors[0];
            var (mx, my) = RoomSigns.DoorMiddle(ward);
            (double X, double Y) outward = d.Side switch
            {
                WallSide.North => (0, -1),
                WallSide.South => (0, 1),
                WallSide.West => (-1, 0),
                _ => (1, 0),
            };

            // In the corridor just outside, to one side of the doorway so it doesn't stand in the way.
            var along = (X: Math.Abs(outward.Y), Y: Math.Abs(outward.X));
            var side = (d.Width / 2.0) + 1.0;
            var wallLength = d.Side is WallSide.North or WallSide.South ? r.Width : r.Length;
            var sign = d.Offset + d.Width + 2 <= wallLength ? 1 : -1;
            var x = mx + (outward.X * 1.1) + (along.X * side * sign);
            var y = my + (outward.Y * 1.1) + (along.Y * side * sign);
            signs.Add(new OutbreakSign(x, y, outward, ward.Name, outbreak.IsClosedAt(sim.Now)));
        }

        return signs;
    }
}
