using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.TopDown;

/// <summary>A bed, couch or table someone can lie on: where it is, which way its head points and what covers whoever is on it.</summary>
public sealed record BedSpot(CareSpace Space, GridCoord Spot, bool HeadSouth, string? TableId, Func<int?>? OccupantId, Color Blanket, bool OnWard)
{
    public double X => Spot.X + 0.5;

    public double Y => Spot.Y + 0.5;

    /// <summary>The angle of the bed's frame: the head is towards -y in it.</summary>
    public double Angle => HeadSouth ? Math.PI : 0;

    /// <summary>True for theatre and cath lab tables: the lights come on over them during a case.</summary>
    public bool Theatre => TableId is "operating_table" or "cath_lab_table";
}

/// <summary>A baby's cot or an incubator: its frame, and how to find the baby that belongs in it.</summary>
public sealed record CotSpot(CareSpace Space, double X, double Y, double Angle, bool Incubator, int Index, WardBed? Bed);

/// <summary>
/// Everything in the rooms that stays put: the door thresholds, the grid,
/// the walls' shadows, curtain tracks, furniture, equipment, chairs, made-up beds and the room
/// decorations. Drawn once and kept until the layout changes; whoever lies in the beds is drawn by
/// the <see cref="ActorsLayer"/>.
/// </summary>
public partial class RoomsLayer : Node2D
{
    private static readonly Color DrapeGreen = new("#4E9C8F");
    private static readonly Color ScanSheet = new("#CFE0EE");

    private HospitalSimulation _sim = null!;
    private WorldLayer _world = null!;

    public bool Shadows { get; set; } = true;

    public bool Detailed { get; set; } = true;

    public bool ShowGrid { get; set; }

    public WallPlan Plan { get; set; } = new();

    /// <summary>Every bed, couch and table on the floor in view, as last built.</summary>
    public List<BedSpot> Beds { get; } = new();

    public List<CotSpot> Cots { get; } = new();

    /// <summary>How many pieces of furniture (chairs included) were drawn.</summary>
    public int FurnitureCount { get; private set; }

    /// <summary>How many plants and other decorations were drawn.</summary>
    public int PropCount { get; private set; }

    public void Initialise(HospitalSimulation simulation, WorldLayer world)
    {
        _sim = simulation;
        _world = world;
    }

    /// <summary>Works out where the beds and cots are. Called before the redraw, so the people layer can use them straight away.</summary>
    public void Rebuild()
    {
        Beds.Clear();
        Cots.Clear();
        foreach (var space in _world.ViewSpaces)
        {
            foreach (var item in space.Furniture)
            {
                if (!_sim.Database.Furniture.TryGetValue(item.ItemId, out var def))
                {
                    continue;
                }

                if (def.Id is "cot" or "incubator")
                {
                    AddCot(space, item, def.Id == "incubator");
                    continue;
                }

                if (def.Style != FurnitureStyle.Bed)
                {
                    continue;
                }

                BedSpot? bed = def.Id switch
                {
                    "birthing_bed" => new BedSpot(space, space.PatientSpot, false, "birthing_bed", () => space.OccupantId, Looks.MaternityBlanket, false),
                    _ when space.IsServiceRoom => new BedSpot(space, space.PatientSpot, false, def.Id, null, space.Kind == CareSpaceKind.Theatre ? DrapeGreen : ScanSheet, false),
                    _ when space.IsMultiBed => space.Beds.FirstOrDefault(b => b.PatientSpot == item.Tile) is { } bay
                        ? new BedSpot(space, bay.PatientSpot, bay.HeadSouth, null, () => bay.OccupantId, Looks.EdBlanket, false)
                        : null,
                    _ when space.Kind == CareSpaceKind.TheatreRecovery => space.Beds.FirstOrDefault(b => b.PatientSpot == item.Tile) is { } recoveryBay
                        ? new BedSpot(space, recoveryBay.PatientSpot, recoveryBay.HeadSouth, null, () => recoveryBay.OccupantId, Looks.WardBlanket, false)
                        : null,
                    _ when space.IsWard => space.Beds.FirstOrDefault(b => b.PatientSpot == item.Tile) is { } wardBed
                        ? new BedSpot(space, wardBed.PatientSpot, wardBed.HeadSouth, null, () => wardBed.OccupantId, Looks.WardBlanketFor(_sim, space, wardBed), true)
                        : null,
                    _ => new BedSpot(space, space.PatientSpot, false, null, () => space.OccupantId, Looks.CubicleBlanket(_sim, space), false),
                };
                if (bed is not null)
                {
                    Beds.Add(bed);
                }
            }
        }

        QueueRedraw();
    }

    private void AddCot(CareSpace space, PlacedFurniture item, bool incubator)
    {
        var (gx, gy) = (item.CentreX, item.CentreY);
        var headSouth = false;
        var index = -1;
        WardBed? bed = null;
        if (incubator)
        {
            index = HospitalSimulation.IncubatorIndex(space, item);
            headSouth = index >= 0 && space.Beds[index].HeadSouth;
        }
        else if (space.Beds.Count > 0)
        {
            bed = space.Beds.MinBy(b => Math.Pow(b.PatientSpot.X + 0.5 - gx, 2) + Math.Pow(b.PatientSpot.Y + 0.5 - gy, 2));
        }

        // Lie them lengthways: the long side runs along the footprint's longer side.
        var alongY = item.Length >= item.Width;
        Cots.Add(new CotSpot(space, gx, gy, (alongY ? 0 : Math.PI / 2) + (headSouth ? Math.PI : 0), incubator, index, bed));
    }

    /// <summary>How near a wall a bed's head has to be for the service panel to go on that wall, in tiles.</summary>
    private const double HeadWallReach = 3.5;

    /// <summary>The wall a bed's head is against, as (the bed's middle along the wall, the wall's line, which way is into the room), or null when the bed stands clear of it.</summary>
    public static (double X, double Wall, int Into)? HeadWall(CareSpace space, WardBed bed)
    {
        var r = space.Rect;
        var wall = bed.HeadSouth ? r.Bottom : r.Y;
        return Math.Abs(bed.PatientSpot.Y + 0.5 - wall) > HeadWallReach ? null : (bed.PatientSpot.X + 0.5, wall, bed.HeadSouth ? -1 : 1);
    }

    /// <summary>How many bed-head service panels were drawn.</summary>
    public int BedHeadCount { get; private set; }

    /// <summary>
    /// Which way the head end (the chair back) of a parent's fold-out bed points, on the grid. The bed is drawn
    /// facing its front (<see cref="FrontOf"/>) with the chair back at -y of its frame when it runs front to back,
    /// otherwise at -x; a parent asleep on it lies with their head there.
    /// </summary>
    public static (double X, double Y) FoldOutHead(CareSpace ward, PlacedFurniture bed)
    {
        var front = FrontOf(ward, bed, "parent_bed");
        var (across, deep) = front.X != 0 ? (bed.Length, bed.Width) : (bed.Width, bed.Length);
        return deep >= across ? (-front.X, -front.Y) : (-front.Y, front.X);
    }

    /// <summary>Which way a piece of furniture faces: the side people use it from.</summary>
    public static (int X, int Y) FrontOf(CareSpace space, PlacedFurniture item, string id)
    {
        var (x, y, w, l) = (item.X, item.Y, item.Width, item.Length);
        if (id == "toilet_cubicle")
        {
            return space.DoorSide switch { WallSide.North => (0, -1), WallSide.South => (0, 1), WallSide.East => (1, 0), _ => (-1, 0) };
        }

        if (ToiletArt.Draws(id))
        {
            var r = space.Rect;
            var gaps = new[] { (Gap: y - r.Y, Dir: (0, 1)), (Gap: r.Bottom - (y + l), Dir: (0, -1)), (Gap: x - r.X, Dir: (1, 0)), (Gap: r.Right - (x + w), Dir: (-1, 0)) };
            return gaps.OrderBy(g => g.Gap).First().Dir;
        }

        if (CafeArt.Draws(id))
        {
            return CafeArt.FrontOf(space, x, y, w, l);
        }

        // A parent's fold-out bed faces the aisle like the ward bed beside it, so its head is at the same end.
        if (id == "parent_bed" && space.Beds.Count > 0)
        {
            var beside = space.Beds.MinBy(b => Math.Abs(b.PatientSpot.Y + 0.5 - (y + (l / 2))) + (Math.Abs(b.PatientSpot.X + 0.5 - (x + (w / 2))) * 0.1))!;
            return beside.HeadSouth ? (0, -1) : (0, 1);
        }

        return FurnitureArt.FrontOf(space, item) switch
        {
            Sides.North => (0, -1),
            Sides.East => (1, 0),
            Sides.West => (-1, 0),
            _ => (0, 1),
        };
    }

    public override void _Draw()
    {
        if (_sim is null)
        {
            return;
        }

        var p = new Pen(this, Shadows);
        FurnitureCount = 0;
        PropCount = 0;
        BedHeadCount = 0;
        foreach (var door in Plan.Doors)
        {
            WallsLayer.Threshold(p, door);
        }

        if (ShowGrid)
        {
            Grid(p);
        }

        // The walls' shadows fall on the floor, under the furniture.
        p.Shadow(
            c =>
            {
                foreach (var (x, y, w, h) in Plan.InnerRects.Concat(Plan.ShellRects))
                {
                    p.Rect(x, y, w, h, c);
                }
            },
            0.16,
            0.22,
            1.1f);

        foreach (var space in _world.ViewSpaces)
        {
            Furnish(p, space);
        }

        foreach (var chair in _world.Chairs)
        {
            var (cx, cy) = ChairArt.Centre(chair.Tile, chair.Facing, chair.Shift);
            FurnitureKit.Chair(p, chair.ItemId, cx, cy, ChairArt.Direction(chair.Facing), chair.Colour);
            FurnitureCount++;
        }

        foreach (var bed in Beds)
        {
            p.Push(bed.X, bed.Y, bed.Angle);
            if (bed.TableId is null)
            {
                FurnitureKit.Bed(p, bed.Blanket);
            }
            else if (bed.TableId == "birthing_bed")
            {
                FurnitureKit.Bed(p, bed.Blanket, rails: true, birthing: true);
            }
            else
            {
                var colour = _sim.Database.Furniture.TryGetValue(bed.TableId, out var def) ? new Color(def.Colour) : new Color("#8FB3CC");
                FurnitureKit.Table(p, bed.TableId, colour);
            }

            p.Pop();
            FurnitureCount++;
        }

        foreach (var cot in Cots)
        {
            var item = cot.Space.Furniture.FirstOrDefault(f => Math.Abs(f.CentreX - cot.X) < 0.01 && Math.Abs(f.CentreY - cot.Y) < 0.01);
            var (w, d) = item is null ? (1.0, 1.6) : (Math.Min(item.Width, item.Length), Math.Max(item.Width, item.Length));
            p.Push(cot.X, cot.Y, cot.Angle);
            FurnitureKit.Cot(p, w, d, cot.Incubator);
            p.Pop();
            FurnitureCount++;
        }

        // The room decorations standing on the floor, and the small things on the desks.
        foreach (var prop in Decorations.FloorPieces)
        {
            if (FurnitureKit.Prop(p, prop.Kind, prop.X, prop.Y, prop.Scale, prop.Dx, prop.Dy))
            {
                PropCount++;
            }
        }

        foreach (var top in Decorations.TopPieces)
        {
            FurnitureKit.OnTop(p, top.Kind, top.X, top.Y);
        }
    }

    private void Grid(Pen p)
    {
        var fine = new Color("#5C7A99", 0.16f);
        var metre = new Color("#5C7A99", 0.3f);
        var up = IsoDraw.ViewFloor * _sim.Site.FloorStride;
        foreach (var area in _sim.Site.Buildings.SelectMany(b => b.Areas))
        {
            for (var y = 0; y <= area.Length; y++)
            {
                p.Line(area.X, area.Y + up + y, area.Right, area.Y + up + y, y % 2 == 0 ? metre : fine, 1.0 / Pen.T);
            }

            for (var x = 0; x <= area.Width; x++)
            {
                p.Line(area.X + x, area.Y + up, area.X + x, area.Bottom + up, x % 2 == 0 ? metre : fine, 1.0 / Pen.T);
            }
        }
    }

    private void Furnish(Pen p, CareSpace space)
    {
        // A ward without bay curtains has a curtain track round each bed.
        if (space.IsWard && !space.Furniture.Any(f => f.ItemId == RoomLayoutEngine.BayCurtain))
        {
            foreach (var bed in space.Beds)
            {
                var (cx, cy) = (bed.PatientSpot.X + 0.5, bed.PatientSpot.Y + 0.5);
                var (x0, y0, w, l) = (cx - 2.1, cy - 2.75 - (bed.HeadSouth ? 0.5 : 0), 4.2, 6.0);
                var track = new Color("#3E8E9E", 0.5f);
                p.Dashes(x0, y0, x0 + w, y0, track, 0.06, 0.25, 0.2);
                p.Dashes(x0, y0 + l, x0 + w, y0 + l, track, 0.06, 0.25, 0.2);
                p.Dashes(x0, y0, x0, y0 + l, track, 0.06, 0.25, 0.2);
                p.Dashes(x0 + w, y0, x0 + w, y0 + l, track, 0.06, 0.25, 0.2);
            }
        }

        // The service panel on the wall at the head of each bed: oxygen, suction and sockets.
        foreach (var bed in space.Beds)
        {
            if (HeadWall(space, bed) is not var (x, wall, _))
            {
                continue;
            }

            BedHeadCount++;
            var y = bed.HeadSouth ? wall - 0.42 : wall + 0.2;
            p.Round(x - 1.1, y, 2.2, 0.22, 0.04, new Color("#E9EEF2"), new Color("#9AA6B2"), 0.02);
            foreach (var (hex, dx) in new[] { ("#F2F4F5", -0.7), ("#1F7A3E", -0.45), ("#2B2F33", -0.2), ("#F2C230", 0.05) })
            {
                p.Circle(x + dx, y + 0.11, 0.06, new Color(hex), new Color("#5D6B79"), 0.015);
            }

            p.Rect(x + 0.4, y + 0.05, 0.16, 0.12, new Color("#D8483B"));
            p.Rect(x + 0.66, y + 0.05, 0.16, 0.12, new Color("#D8483B"));
        }

        foreach (var item in space.Furniture)
        {
            if (item.ItemId == HospitalSimulation.ResusTrolleyItem || !_sim.Database.Furniture.TryGetValue(item.ItemId, out var def))
            {
                continue;
            }

            // Chairs, beds and cots are drawn in their own passes.
            if (def.Style is FurnitureStyle.Chair or FurnitureStyle.Bed || def.Id == "cot")
            {
                continue;
            }

            var colour = new Color(def.Colour);
            var (x, y, w, l) = (item.X, item.Y, item.Width, item.Length);
            var seed = (int)Math.Abs((Math.Floor(x * 7) * 31) + (Math.Floor(y * 7) * 17));
            FurnitureCount++;
            if (def.Style == FurnitureStyle.Monitor)
            {
                p.Push(item.CentreX, item.CentreY);
                FurnitureKit.MonitorStand(p, false, null);
                p.Pop();
                continue;
            }

            if (def.Id == "xray_tube")
            {
                FurnitureKit.XrayTube(p, item.CentreX, item.CentreY, space.PatientSpot.X + 0.5, space.PatientSpot.Y + 0.5);
                continue;
            }

            if (def.Id == "examination_couch")
            {
                // Lies along its length, head at the north or west end.
                var alongY = l >= w;
                p.Push(item.CentreX, item.CentreY, alongY ? 0 : -Math.PI / 2);
                FurnitureKit.Couch(p, alongY ? w : l, alongY ? l : w, colour);
                p.Pop();
                continue;
            }

            var front = FrontOf(space, item, def.Id);
            if (def.Id == RoomLayoutEngine.BayCurtain && space.Beds.Count > 0)
            {
                // Faces the foot of the beds either side, where the aisle is.
                var beside = space.Beds.MinBy(b => Math.Abs(b.PatientSpot.Y + 0.5 - item.CentreY) + (Math.Abs(b.PatientSpot.X + 0.5 - item.CentreX) * 0.1))!;
                front = beside.HeadSouth ? (0, -1) : (0, 1);
            }

            if (def.Id == "vending_machine")
            {
                seed = Math.Max(0, space.Furniture.Where(f => f.ItemId == def.Id).OrderBy(f => f.X).ThenBy(f => f.Y).ToList().IndexOf(item)) % 3;
            }

            FurnitureKit.Draw(p, def.Id, x, y, w, l, front, colour, seed);
        }

        // Extra kit lining the walls of clinical rooms (looks only; see RoomFill).
        foreach (var piece in RoomFill.For(space, _sim))
        {
            var def = _sim.Database.Furniture[piece.Id];
            FurnitureKit.Draw(p, piece.Id, piece.X + 0.04, piece.Y + 0.04, piece.W - 0.08, piece.L - 0.08, piece.Front, new Color(def.Colour), piece.Seed);
            FurnitureCount++;
        }
    }
}

/// <summary>
/// The band round the foot of each room's walls, under the furniture. Kept apart from the
/// <see cref="RoomsLayer"/> so that trying out or changing a wall finish in Decorate repaints only
/// these bands and the floors, not every piece of furniture in the building.
/// </summary>
public partial class BandsLayer : Node2D
{
    private HospitalSimulation _sim = null!;
    private WorldLayer _world = null!;

    public bool Shadows { get; set; } = true;

    public void Initialise(HospitalSimulation simulation, WorldLayer world)
    {
        _sim = simulation;
        _world = world;
    }

    public override void _Draw()
    {
        if (_sim is null)
        {
            return;
        }

        var p = new Pen(this, Shadows);
        foreach (var space in _world.ViewSpaces)
        {
            WallBand(p, space);
        }
    }

    /// <summary>
    /// The band round the foot of a room's walls, just inside them: the department's colour, or the
    /// wall finish picked in Decorate with its accent colour as the line.
    /// </summary>
    private void WallBand(Pen p, CareSpace space)
    {
        if (!space.HasWalls || space.IsShaft)
        {
            return;
        }

        var r = space.Rect;
        const double m = (WallPlan.InnerThickness / 2) + 0.07;
        var look = DecorArt.LookOf(_sim, space);
        if (look.Walls != WallFinish.Standard || look.WallColour is not null)
        {
            var wall = DecorArt.WallColour(look);
            p.Round(r.X + m + 0.03, r.Y + m + 0.03, r.Width - (2 * m) - 0.06, r.Length - (2 * m) - 0.06, 0.05, null, wall, 0.2);
            if (DecorArt.WallAccent(space, look) is { } accent)
            {
                p.Round(r.X + m + 0.14, r.Y + m + 0.14, r.Width - (2 * m) - 0.28, r.Length - (2 * m) - 0.28, 0.05, null, new Color(accent, 0.8f), 0.06);
            }

            return;
        }

        if (DepartmentRenderer.WayfindingColour(space) is { } colour)
        {
            p.Round(r.X + m, r.Y + m, r.Width - (2 * m), r.Length - (2 * m), 0.05, null, new Color(colour, 0.55f), 0.14);
        }
    }
}
