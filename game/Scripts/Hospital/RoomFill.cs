using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Extra kit along the walls of clinical rooms, so they look stocked and busy: cupboards, instrument
/// trolleys, drug fridges, oxygen, basins, and ventilators and scanners where patients are sickest.
/// Looks only: it never blocks anyone (beds, the staff spot, the doors and the room's own furniture
/// are kept clear) and costs nothing. Shared by the 2D and 3D views, so both show the same pieces.
/// </summary>
public static class RoomFill
{
    /// <summary>One extra piece: its furniture id, footprint in tiles, and the way it faces (into the room).</summary>
    public readonly record struct Piece(string Id, int X, int Y, int W, int L, (int X, int Y) Front, int Seed);

    private static readonly (string Id, int Along)[] General =
    {
        ("clinical_cupboard", 2), ("instrument_trolley", 2), ("oxygen_cylinder", 1), ("drug_fridge", 1), ("hand_basin", 1),
    };

    private static readonly (string Id, int Along)[] Critical =
    {
        ("clinical_cupboard", 2), ("ventilator", 1), ("instrument_trolley", 2), ("oxygen_cylinder", 1), ("ultrasound_machine", 1), ("drug_fridge", 1),
    };

    private static readonly HashSet<CareSpaceKind> Clinical = new()
    {
        CareSpaceKind.Minors, CareSpaceKind.Majors, CareSpaceKind.Resus, CareSpaceKind.Triage, CareSpaceKind.Ward,
        CareSpaceKind.TheatreRecovery, CareSpaceKind.Clinic, CareSpaceKind.Echo, CareSpaceKind.Eeg, CareSpaceKind.Ultrasound,
        CareSpaceKind.Endoscopy, CareSpaceKind.EndoscopyRecovery, CareSpaceKind.PlasterRoom, CareSpaceKind.DeliveryRoom,
        CareSpaceKind.Theatre, CareSpaceKind.AnaestheticRoom, CareSpaceKind.XRay, CareSpaceKind.CT, CareSpaceKind.CathLab,
    };

    private static bool IsCritical(CareSpace s) =>
        s.Kind is CareSpaceKind.Resus or CareSpaceKind.Theatre or CareSpaceKind.AnaestheticRoom or CareSpaceKind.TheatreRecovery
        || (s.Kind == CareSpaceKind.Ward && (s.TemplateId.Contains("icu") || s.TemplateId.Contains("hdu")));

    /// <summary>A stable number from the room's id (string hashes change from run to run).</summary>
    private static int Stable(string text) => text.Aggregate(17, (h, c) => unchecked((h * 31) + c)) & int.MaxValue;

    public static List<Piece> For(CareSpace space, HospitalSimulation sim)
    {
        var pieces = new List<Piece>();
        if (!space.HasWalls || !Clinical.Contains(space.Kind))
        {
            return pieces;
        }

        var r = space.Rect;
        var busy = new bool[r.Width, r.Length];
        void Mark(double x0, double y0, double x1, double y1)
        {
            for (var x = Math.Max(r.X, (int)Math.Floor(x0)); x < Math.Min(r.Right, (int)Math.Ceiling(x1)); x++)
            {
                for (var y = Math.Max(r.Y, (int)Math.Floor(y0)); y < Math.Min(r.Bottom, (int)Math.Ceiling(y1)); y++)
                {
                    busy[x - r.X, y - r.Y] = true;
                }
            }
        }

        // Keep clear: the room's own furniture (with a tile round it), beds, where staff stand, and the doorways.
        foreach (var f in space.Furniture)
        {
            Mark(f.X - 1, f.Y - 1, f.X + f.Width + 1, f.Y + f.Length + 1);
        }

        foreach (var spot in space.Beds.Select(b => b.PatientSpot).Append(space.PatientSpot).Where(p => p.X != 0 || p.Y != 0))
        {
            Mark(spot.X - 2, spot.Y - 3, spot.X + 3, spot.Y + 4);
        }

        foreach (var spot in space.Beds.Select(b => b.StaffSpot).Append(space.StaffSpot).Where(p => p.X != 0 || p.Y != 0))
        {
            Mark(spot.X - 1, spot.Y - 1, spot.X + 2, spot.Y + 2);
        }

        foreach (var d in space.Doors.Concat(space.LinkDoors))
        {
            foreach (var (ex, ey, _) in RoomSigns.DoorwayEdges(r, d))
            {
                Mark(ex - 2, ey - 2, ex + 3, ey + 3);
            }
        }

        var kit = IsCritical(space) ? Critical : General;
        var seed = Stable(space.Id);
        var pick = seed % kit.Length;

        // Along each wall except the one with the door (that one faces the corridor, often glass).
        var sides = new List<((int X, int Y) Into, (int X, int Y) Along, (int X, int Y) Start, int Length, WallSide Side)>
        {
            ((0, 1), (1, 0), (r.X, r.Y), r.Width, WallSide.North),
            ((-1, 0), (0, 1), (r.Right - 1, r.Y), r.Length, WallSide.East),
            ((1, 0), (0, 1), (r.X, r.Y), r.Length, WallSide.West),
            ((0, -1), (1, 0), (r.X, r.Bottom - 1), r.Width, WallSide.South),
        };
        foreach (var side in sides.Where(s => space.Doors.Count == 0 || s.Side != space.DoorSide))
        {
            var i = 0;
            while (i < side.Length)
            {
                var placed = false;
                for (var k = 0; k < kit.Length && !placed; k++)
                {
                    var (id, along) = kit[(pick + k) % kit.Length];
                    if (i + along > side.Length || !sim.Database.Furniture.ContainsKey(id))
                    {
                        continue;
                    }

                    var cells = Enumerable.Range(0, along)
                        .Select(a => (X: side.Start.X + ((i + a) * side.Along.X), Y: side.Start.Y + ((i + a) * side.Along.Y)))
                        .ToList();
                    if (cells.Any(c => busy[c.X - r.X, c.Y - r.Y]))
                    {
                        continue;
                    }

                    // The piece, plus a tile of floor after it so the run doesn't look like one block.
                    foreach (var c in cells)
                    {
                        busy[c.X - r.X, c.Y - r.Y] = true;
                    }

                    var (x0, y0) = (cells.Min(c => c.X), cells.Min(c => c.Y));
                    pieces.Add(new Piece(id, x0, y0, cells.Max(c => c.X) + 1 - x0, cells.Max(c => c.Y) + 1 - y0, side.Into, seed + pieces.Count));
                    pick++;
                    i += along + 1;
                    placed = true;
                }

                if (!placed)
                {
                    i++;
                }
            }
        }

        return pieces;
    }
}
