using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// The fronts of rooms, along the wall their door is in, where it faces a corridor or open floor:
/// a panel in the department's colour either side of the door, and glass windows along the rest.
/// Rooms that need privacy or shielding (toilets, stores, X-ray, CT, theatres...) keep solid walls
/// away from the door. Shared by the 2D and 3D views, so they show the same fronts.
/// </summary>
public static class RoomFronts
{
    /// <summary>How many wall tiles either side of a door take the coloured panel.</summary>
    public const int PanelReach = 2;

    /// <summary>A wall edge (as in <see cref="WorldLayer.WallEdges"/>) with its colour and the way out of the room (unit x, y).</summary>
    public readonly record struct Panel(Color Colour, int OutX, int OutY);

    public sealed class Fronts
    {
        public Dictionary<(int X, int Y, bool Horizontal), Panel> Panels { get; } = new();

        public Dictionary<(int X, int Y, bool Horizontal), Panel> Glass { get; } = new();
    }

    private static readonly HashSet<CareSpaceKind> Solid = new()
    {
        CareSpaceKind.XRay, CareSpaceKind.CT, CareSpaceKind.CathLab, CareSpaceKind.Theatre, CareSpaceKind.AnaestheticRoom,
        CareSpaceKind.Toilets, CareSpaceKind.AccessibleToilet, CareSpaceKind.StaffToilet, CareSpaceKind.CleaningRoom,
        CareSpaceKind.Lift, CareSpaceKind.Stairs, CareSpaceKind.DeliveryRoom,
    };

    /// <summary>Whether a room keeps solid walls along its front (apart from the coloured panels by the door).</summary>
    public static bool KeepsSolid(CareSpace space) =>
        Solid.Contains(space.Kind) || space.TemplateId.Contains("store") || space.TemplateId.Contains("toilet");

    public static Fronts Of(HospitalSimulation sim, IEnumerable<CareSpace> spaces)
    {
        var fronts = new Fronts();
        var walled = sim.Spaces.Where(s => s.HasWalls).Select(s => s.Rect).ToList();
        var open = sim.Spaces.Where(s => !s.HasWalls).Select(s => s.Rect).ToList();
        var buildings = sim.Site.Buildings.Select(b => b.Footprint).ToList();
        static bool In(GridRect r, double x, double y) => x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom;
        foreach (var space in spaces.Where(s => RoomSigns.Named(s) && !s.IsShaft && !RoomSigns.OpenGap(s)))
        {
            var r = space.Rect;
            var door = space.Doors[0];
            var (ox, oy) = door.Side switch { WallSide.North => (0, -1), WallSide.South => (0, 1), WallSide.West => (-1, 0), _ => (1, 0) };
            var horizontal = door.Side is WallSide.North or WallSide.South;
            var length = horizontal ? r.Width : r.Length;
            var panel = new Panel(RoomSigns.Colour(space), ox, oy);
            var solid = KeepsSolid(space);
            for (var i = 0; i < length; i++)
            {
                var edge = door.Side switch
                {
                    WallSide.North => (r.X + i, r.Y, true),
                    WallSide.South => (r.X + i, r.Bottom, true),
                    WallSide.West => (r.X, r.Y + i, false),
                    _ => (r.Right, r.Y + i, false),
                };

                // The tile just outside: it must be corridor or open floor inside the building, not another room or outdoors.
                var (mx, my) = horizontal ? (edge.Item1 + 0.5, edge.Item2 + (oy * 0.5)) : (edge.Item1 + (ox * 0.5), edge.Item2 + 0.5);
                if (walled.Any(w => In(w, mx, my)) || !(open.Any(o => In(o, mx, my)) || buildings.Any(b => In(b, mx, my)))
                    || space.Doors.Any(d => i >= d.Offset && i < d.Offset + d.Width))
                {
                    continue;
                }

                if (space.Doors.Any(d => i >= d.Offset - PanelReach && i < d.Offset + d.Width + PanelReach))
                {
                    fronts.Panels[edge] = panel;
                }
                else if (!solid)
                {
                    fronts.Glass[edge] = panel;
                }
            }
        }

        return fronts;
    }
}
