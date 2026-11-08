using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Hospital;

/// <summary>
/// Furnishes a room from its template. Layouts are worked out in the room's own frame, with the
/// door in the front wall, then turned to face whichever way the door points, so the same template
/// works at any size and in any of the four directions. Furniture keeps its real size: a bigger
/// room gets more space and more seats, never a stretched bed.
/// </summary>
public static class RoomLayoutEngine
{
    /// <summary>Beds are drawn along the Y axis, 1.5 m by 2.5 m including clearance (3 x 5 tiles).</summary>
    private const int BedTilesX = 3;
    private const int BedTilesY = 5;

    /// <summary>Furniture a layout cannot work without.</summary>
    public static IReadOnlyList<string> RequiredFurniture(RoomLayoutKind layout) => layout switch
    {
        RoomLayoutKind.BedSpace => new[] { "hospital_bed", "obs_monitor" },
        RoomLayoutKind.Ward => new[] { "hospital_bed" },
        RoomLayoutKind.Triage => new[] { "staff_chair", "visitor_chair", "obs_monitor" },
        RoomLayoutKind.Waiting => new[] { "reception_desk", "staff_chair", "waiting_seat" },
        RoomLayoutKind.Cafeteria => new[] { "food_counter", "cafe_table", "cafe_chair" },
        RoomLayoutKind.Vending => new[] { "vending_machine" },
        _ => Array.Empty<string>(),
    };

    /// <summary>The footprint of a room <paramref name="widthTiles"/> along its door wall and <paramref name="lengthTiles"/> deep.</summary>
    public static GridRect RectFor(GridCoord corner, int widthTiles, int lengthTiles, WallSide doorSide) =>
        doorSide is WallSide.North or WallSide.South
            ? new GridRect(corner.X, corner.Y, widthTiles, lengthTiles)
            : new GridRect(corner.X, corner.Y, lengthTiles, widthTiles);

    /// <summary>Width along the door wall and depth away from it, in tiles.</summary>
    public static (int Width, int Depth) LocalSize(GridRect rect, WallSide doorSide) =>
        doorSide is WallSide.North or WallSide.South ? (rect.Width, rect.Length) : (rect.Length, rect.Width);

    /// <summary>Cost of building the room: fit-out, floor area and every item of furniture.</summary>
    public static int Cost(RoomTemplate template, GameDatabase database, GridRect rect, IEnumerable<PlacedFurniture> furniture) =>
        template.BaseCost
        + (int)Math.Round(template.CostPerSquareMetre * Units.AreaSquareMetres(rect.Width, rect.Length))
        + furniture.Sum(f => database.Furniture.TryGetValue(f.ItemId, out var item) ? item.Cost : 0);

    /// <summary>
    /// Lays out a room of the given template in <paramref name="rect"/> with its door on <paramref name="doorSide"/>.
    /// The door is centred on the wall unless <paramref name="doorStart"/> and <paramref name="doorRun"/> give the
    /// stretch of wall (in tiles from the room's corner) that opens onto a corridor; it is then centred on that stretch.
    /// <paramref name="extraDoors"/> are further doorways (a waiting room's street door); when null, a template with
    /// <see cref="RoomTemplate.DoorBothEnds"/> gets one opposite the main door.
    /// </summary>
    public static CareSpace Build(
        RoomTemplate template, GameDatabase database, string id, string name, GridRect rect, WallSide doorSide,
        int? doorStart = null, int? doorRun = null, IReadOnlyList<Doorway>? extraDoors = null)
    {
        var frame = new Frame(rect, doorSide);
        var run = Math.Clamp(doorRun ?? frame.W, 1, frame.W);
        var doorWidth = Math.Clamp(Units.MetresToTiles(template.DoorWidthM), 1, Math.Max(1, Math.Min(run, frame.W - 2)));
        var doorOffset = (frame.W - doorWidth) / 2;
        if (doorStart is { } start)
        {
            // Centre on the corridor stretch, then turn the wall offset into the room's own frame.
            var along = Math.Clamp(start + ((run - doorWidth) / 2), 1, Math.Max(1, frame.W - doorWidth - 1));
            doorOffset = doorSide == WallSide.North ? frame.W - along - doorWidth : along;
        }

        var backDoor = extraDoors is null ? template.DoorBothEnds : extraDoors.Count > 0;
        var plan = new Plan(frame, database, doorOffset, doorWidth, backDoor);

        switch (template.Layout)
        {
            case RoomLayoutKind.BedSpace:
            case RoomLayoutKind.Equipment:
                BedSpace(plan, template);
                break;
            case RoomLayoutKind.Ward:
                Ward(plan, template);
                break;
            case RoomLayoutKind.Triage:
                Triage(plan, template);
                break;
            case RoomLayoutKind.Waiting:
                Waiting(plan, template);
                break;
            case RoomLayoutKind.Office:
            case RoomLayoutKind.StaffRoom:
                Office(plan, template);
                break;
            case RoomLayoutKind.Cafeteria:
                Cafeteria(plan, template);
                break;
            case RoomLayoutKind.Vending:
                Vending(plan, template);
                break;
        }

        var doors = new List<Doorway> { frame.Door(doorOffset, doorWidth, back: false) };
        if (extraDoors is not null)
        {
            doors.AddRange(extraDoors);
        }
        else if (template.DoorBothEnds)
        {
            doors.Add(frame.Door(doorOffset, doorWidth, back: true));
        }

        return new CareSpace
        {
            Id = id,
            Name = name,
            Kind = template.Kind,
            DepartmentId = template.Department,
            TemplateId = template.Id,
            Rect = rect,
            DoorSide = doorSide,
            HasWalls = !template.IsCorridor,
            Doors = template.IsCorridor ? Array.Empty<Doorway>() : doors,
            PatientSpot = plan.PatientSpot,
            StaffSpot = plan.StaffSpot,
            Seats = plan.Seats,
            Beds = plan.Beds,
            VendingSpots = plan.Vendors,
            QueueTiles = plan.Queue,
            Furniture = plan.Items,
            BuildCost = Cost(template, database, rect, plan.Items),
        };
    }

    // ------------------------------------------------------------------ layouts

    private static void BedSpace(Plan plan, RoomTemplate template)
    {
        var f = plan.Frame;

        // The bed stands against the back wall, centred. Beds are always drawn head to the north,
        // so the footprint is fixed in site coordinates whichever way the room faces.
        var (su, sv) = f.SideOn ? (BedTilesY, BedTilesX) : (BedTilesX, BedTilesY);
        var bed = f.ToWorld((f.W - su) / 2, 1, su, sv);
        var spot = new GridCoord((int)Math.Floor(bed.X + (bed.W / 2)), (int)Math.Floor(bed.Y + (bed.L / 2)));
        plan.PatientSpot = spot;
        plan.Reserve(spot);

        // Imaging rooms and theatres use their own table (the first item drawn as a bed).
        var table = template.Furniture.FirstOrDefault(i => plan.Database.Furniture.TryGetValue(i, out var d) && d.Style == FurnitureStyle.Bed) ?? "hospital_bed";
        plan.AddWorld(table, bed.X, bed.Y, bed.W, bed.L);

        // A CT scanner's ring goes round the head end of the table.
        if (template.Furniture.Contains("ct_gantry"))
        {
            plan.AddWorld("ct_gantry", bed.X - 0.6, bed.Y + 0.4, bed.W + 1.2, 1.4);
        }

        // Staff stand beside the bed, on the side nearest the door.
        var door = plan.DoorTile;
        var candidates = new[] { new GridCoord(spot.X - 2, spot.Y + 1), new GridCoord(spot.X + 2, spot.Y + 1), new GridCoord(spot.X - 2, spot.Y), new GridCoord(spot.X + 2, spot.Y), new GridCoord(spot.X, spot.Y + 3) }
            .Where(c => f.Rect.Contains(c) && !f.Rect.IsOnEdge(c))
            .OrderBy(c => Math.Abs(c.X - door.X) + Math.Abs(c.Y - door.Y))
            .ToList();
        plan.StaffSpot = candidates.Count > 0 ? candidates[0] : new GridCoord(spot.X, Math.Min(spot.Y + 3, f.Rect.Bottom - 1));
        plan.Reserve(plan.StaffSpot);

        // Obs monitor (or anaesthetic machine, or X-ray tube) at the head of the bed, on the side away from where staff stand.
        var side = plan.StaffSpot.X < spot.X ? 1 : -1;
        var (cx, cy) = (spot.X + 0.5, spot.Y + 0.5);
        var headItem = template.Furniture.FirstOrDefault(i => i is "obs_monitor" or "anaesthetic_machine" or "xray_tube");
        if (headItem is not null)
        {
            plan.AddWorld(headItem, cx + (side * 1.4) - 0.3, cy - 1.6 - 0.3, 0.6, 0.6);
        }

        foreach (var item in template.Furniture.Where(i => i != table && i != headItem && i != "ct_gantry"))
        {
            plan.TryPlaceAgainstWalls(item);
        }
    }

    /// <summary>
    /// Beds stand in a row along the north wall (the south wall when the door is in the north wall),
    /// heads to the wall, 2.5 m apart (bed centres), with a bedside locker in each gap. From 7.5 m deep
    /// a second row stands along the opposite wall, facing the first across a clear aisle. Beds keep their real size, so a longer ward simply holds more of them.
    /// Staff work from the foot of each bed, in the aisle.
    /// </summary>
    private static void Ward(Plan plan, RoomTemplate template)
    {
        var r = plan.Frame.Rect;

        // Critical care beds stand further apart, with a ventilator and a monitor beside each one.
        var ventilated = template.Furniture.Contains("ventilator");
        var monitored = template.Furniture.Contains("obs_monitor");
        var pitch = ventilated ? BedTilesX + 4 : BedTilesX + 2;
        var north = (r.Y + 1, false);
        var south = (r.Bottom - 1 - BedTilesY, true);
        var rows = new List<(int Y, bool HeadSouth)> { plan.Frame.Side == WallSide.North ? south : north };
        if (r.Length >= 15)
        {
            rows.Add(plan.Frame.Side == WallSide.North ? north : south);
        }

        var door = plan.DoorApproach(4);
        var locker = template.Furniture.Contains("bedside_locker");
        foreach (var (rowY, headSouth) in rows)
        {
            for (var x = r.X + 1; x + BedTilesX <= r.Right - 1; x += pitch)
            {
                if (door.X < x + BedTilesX && x < door.X + door.W && door.Y < rowY + BedTilesY && rowY < door.Y + door.L)
                {
                    continue;
                }

                var spot = new GridCoord(x + 1, rowY + 2);
                var staff = new GridCoord(x + 1, headSouth ? rowY - 1 : rowY + BedTilesY);
                plan.AddWorld("hospital_bed", x, rowY, BedTilesX, BedTilesY);
                plan.Beds.Add(new WardBed { PatientSpot = spot, StaffSpot = staff, HeadSouth = headSouth });
                plan.Reserve(spot);
                plan.Reserve(staff);

                var headY = headSouth ? rowY + BedTilesY - 1.0 : rowY + 0.1;
                if (ventilated && x + BedTilesX + 3 <= r.Right - 1)
                {
                    plan.AddWorld("ventilator", x + BedTilesX + 0.2, headY, 0.8, 0.8);
                    plan.AddWorld("obs_monitor", x + BedTilesX + 1.4, headY, 0.6, 0.6);
                }
                else if (monitored && !locker && x + BedTilesX + 1 <= r.Right - 1)
                {
                    plan.AddWorld("obs_monitor", x + BedTilesX + 0.2, headY, 0.6, 0.6);
                }
                else if (locker && x + BedTilesX + 1 <= r.Right - 1)
                {
                    plan.AddWorld("bedside_locker", x + BedTilesX + 0.1, headY, 0.9, 0.9);
                }
            }
        }

        // Staff gather just inside the door, where they can see down the ward.
        plan.StaffSpot = plan.Frame.Tile(plan.DoorOffset + (plan.DoorWidth / 2), plan.Frame.D - 3);
        plan.PatientSpot = plan.Beds.Count > 0 ? plan.Beds[0].PatientSpot : plan.StaffSpot;

        foreach (var item in template.Furniture.Where(i => i is not "hospital_bed" and not "bedside_locker" and not "ventilator" and not "obs_monitor"))
        {
            plan.TryPlaceAgainstWalls(item);
        }
    }

    private static void Triage(Plan plan, RoomTemplate template)
    {
        var f = plan.Frame;

        // Nurse and patient sit facing each other in front of a desk on the back wall.
        var nurse = f.Tile(1, 2);
        var patient = f.Tile(Math.Min(3, f.W - 2), 2);
        plan.StaffSpot = nurse;
        plan.PatientSpot = patient;
        plan.AddChair("staff_chair", nurse);
        plan.AddChair("visitor_chair", patient);

        if (template.Furniture.Contains("triage_desk"))
        {
            plan.TryPlaceLocal("triage_desk", 0.4, 0.3);
        }

        var monitor = f.ToWorld(Math.Min(4.6, f.W - 1.1), 1.0, 0.6, 0.6);
        plan.AddWorld("obs_monitor", monitor.X, monitor.Y, monitor.W, monitor.L);

        foreach (var item in template.Furniture.Where(i => i is not "triage_desk" and not "obs_monitor" and not "staff_chair" and not "visitor_chair"))
        {
            plan.TryPlaceAgainstWalls(item);
        }
    }

    private static void Waiting(Plan plan, RoomTemplate template)
    {
        var f = plan.Frame;

        // Reception on the right, facing the street door at the back; the receptionist works
        // from the corridor side and patients queue back towards the entrance.
        var deskU = Math.Max(1, f.W - 6);
        var receptionist = f.Tile(f.W - 4, 4);
        plan.StaffSpot = receptionist;
        plan.PatientSpot = f.Tile(f.W - 4, 2);
        plan.AddChair("staff_chair", receptionist);
        var desk = f.ToWorld(deskU, 3.1, Math.Min(5, f.W - deskU - 0.5), 0.8);
        plan.AddWorld("reception_desk", desk.X, desk.Y, desk.W, desk.L);

        var queue = new List<GridCoord> { f.Tile(f.W - 4, 2), f.Tile(f.W - 4, 1) };
        for (var u = f.W - 5; u >= Math.Max(1, f.W - 8); u--)
        {
            queue.Add(f.Tile(u, 1));
        }

        plan.Queue = queue;
        foreach (var q in queue)
        {
            plan.Reserve(q);
        }

        // Rows of seats either side of a clear walkway between the doors.
        var walkFrom = plan.DoorOffset + (plan.DoorWidth / 2) - 2;
        var walkTo = walkFrom + 4;
        for (var v = 4; v <= f.D - 3; v += 3)
        {
            for (var u = 1; u <= f.W - 2; u++)
            {
                var inWalkway = u >= walkFrom && u < walkTo;
                var atReception = u >= deskU - 1 && v <= 5;
                if (!inWalkway && !atReception)
                {
                    plan.AddSeat("waiting_seat", f.Tile(u, v));
                }
            }
        }
    }

    /// <summary>Desks (or a table) down the middle with chairs either side. Used by offices, nurses' stations and staff rooms.</summary>
    private static void Office(Plan plan, RoomTemplate template)
    {
        var f = plan.Frame;
        var db = plan.Database;
        var desk = template.Furniture.Select(i => db.Furniture.GetValueOrDefault(i)).FirstOrDefault(i => i is { Style: FurnitureStyle.Box });
        var chair = template.Furniture.Select(i => db.Furniture.GetValueOrDefault(i)).FirstOrDefault(i => i is { Style: FurnitureStyle.Chair });

        var centre = f.D / 2.0;
        var deskDepth = desk is null ? 1.6 : desk.DepthM / Units.MetresPerTile;
        var rowA = (int)Math.Floor(centre - (deskDepth / 2)) - 1;
        var rowB = (int)Math.Ceiling(centre + (deskDepth / 2));

        if (desk is not null)
        {
            var deskWidth = desk.WidthM / Units.MetresPerTile;
            for (var u = 1.6; u + deskWidth <= f.W - 1.4; u += deskWidth)
            {
                var r = f.ToWorld(u, centre - (deskDepth / 2), deskWidth, deskDepth);
                plan.AddWorld(desk.Id, r.X, r.Y, r.W, r.L);
            }
        }

        if (chair is not null)
        {
            foreach (var row in new[] { rowA, rowB }.Where(r => r >= 1 && r <= f.D - 2).Distinct())
            {
                for (var u = 2; u <= f.W - 3; u += 2)
                {
                    plan.AddSeat(chair.Id, f.Tile(u, row));
                }
            }
        }

        plan.StaffSpot = plan.Seats.Count > 0 ? plan.Seats[0] : f.Tile(f.W / 2, 1);
        plan.PatientSpot = plan.StaffSpot;

        foreach (var item in template.Furniture.Where(i => i != desk?.Id && i != chair?.Id))
        {
            plan.TryPlaceAgainstWalls(item);
        }
    }

    /// <summary>The most vending machines any room gets.</summary>
    public const int MaxVendingMachines = 4;

    /// <summary>How many vending machines a cafeteria of this floor area gets: one in a small cafe, up to four in a big one.</summary>
    public static int CafeVendingMachines(double areaSquareMetres) => areaSquareMetres switch
    {
        < 40 => 1,
        < 65 => 2,
        < 100 => 3,
        _ => MaxVendingMachines,
    };

    /// <summary>
    /// A food counter centred on the back wall with its queue in front, a water machine beside it, vending
    /// machines down the side walls (more in a bigger cafe, up to four), and the rest of the floor filled
    /// with square tables of four. A bigger cafe simply holds more tables.
    /// </summary>
    private static void Cafeteria(Plan plan, RoomTemplate template)
    {
        var f = plan.Frame;
        var db = plan.Database;

        // Food counter along the back wall, between a third and a half of its length.
        var counterLength = Math.Clamp(Math.Round(f.W * 0.4), 3, 10);
        var counterDepth = db.Furniture.TryGetValue("food_counter", out var counterDef) ? counterDef.DepthM / Units.MetresPerTile : 1.6;
        var cu = Math.Floor((f.W - counterLength) / 2) + 0.2;
        var counter = f.ToWorld(cu, 0.3, counterLength - 0.4, counterDepth);
        plan.AddWorld("food_counter", counter.X, counter.Y, counter.W, counter.L);

        // Customers queue along the front of the counter; the till is in the middle.
        var queueV = (int)Math.Ceiling(0.3 + counterDepth);
        var queue = new List<GridCoord>();
        for (var u = (int)Math.Ceiling(cu); u < cu + counterLength - 0.4; u++)
        {
            queue.Add(f.Tile(u, queueV));
        }

        var till = f.Tile((int)Math.Floor(cu + ((counterLength - 0.4) / 2)), queueV);
        plan.Queue = queue.OrderBy(q => Math.Abs(q.X - till.X) + Math.Abs(q.Y - till.Y)).ToList();
        foreach (var q in plan.Queue)
        {
            plan.Reserve(q);
        }

        plan.PatientSpot = till;
        plan.StaffSpot = till;

        // Water machine on the back wall beside the counter.
        if (template.Furniture.Contains("water_cooler")
            && !plan.TryPlaceLocal("water_cooler", cu + counterLength - 0.2, 0.3)
            && !plan.TryPlaceLocal("water_cooler", cu - 1.2, 0.3))
        {
            plan.TryPlaceAgainstWalls("water_cooler");
        }

        // Vending machines down the side walls, starting at the back, each with a clear tile in front.
        var (leftUsed, rightUsed) = (false, false);
        if (template.Furniture.Contains("vending_machine"))
        {
            var wanted = CafeVendingMachines(Units.AreaSquareMetres(f.Rect.Width, f.Rect.Length));
            var placed = 0;
            foreach (var left in new[] { true, false })
            {
                for (var v = 0.3; placed < wanted && v < f.D - 1; v += 0.5)
                {
                    if (plan.TryPlaceTurnedAgainstSide("vending_machine", v, left, out var front))
                    {
                        plan.Reserve(front);
                        plan.Vendors.Add(front);
                        placed++;
                        (leftUsed, rightUsed) = left ? (true, rightUsed) : (leftUsed, true);
                    }
                }
            }
        }

        // Tables of four (a chair on each side, set in a pinwheel), filling the floor row by row with
        // a clear aisle round each one; the space inside the door is kept free.
        var minU = leftUsed ? 3 : 1;
        var maxU = f.W - (rightUsed ? 4 : 2);
        // First in neat rows and columns, then into any gaps left (round the doorway, say).
        foreach (var step in new[] { 5, 1 })
        {
            for (var bv = queueV + 1; bv + 4 <= f.D - 1; bv += step)
            {
                for (var bu = minU; bu + 4 <= maxU + 1; bu += step)
                {
                    plan.TryTableOfFour("cafe_table", "cafe_chair", bu, bv);
                }
            }
        }
    }

    /// <summary>
    /// A row of vending machines along the back wall (one for every 2 metres of wall, up to four) with a
    /// water machine at the end, and a seat or two by the side walls if the room is deep enough.
    /// </summary>
    private static void Vending(Plan plan, RoomTemplate template)
    {
        var f = plan.Frame;
        var db = plan.Database;
        var width = db.Furniture.TryGetValue("vending_machine", out var vm) ? vm.WidthM / Units.MetresPerTile : 1.8;
        var depth = vm is null ? 1.6 : vm.DepthM / Units.MetresPerTile;
        var frontV = (int)Math.Ceiling(0.3 + depth);

        var count = 0;
        for (var u = 0.3; count < MaxVendingMachines && u + width <= f.W - 0.2; u += width + 0.2)
        {
            if (plan.TryPlaceLocal("vending_machine", u, 0.3))
            {
                var front = f.Tile((int)Math.Floor(u + (width / 2)), frontV);
                plan.Reserve(front);
                plan.Vendors.Add(front);
                count++;
            }
        }

        // The water machine goes at the end of the row if there is room, otherwise against a wall.
        if (template.Furniture.Contains("water_cooler") && !plan.TryPlaceLocal("water_cooler", 0.3 + (count * (width + 0.2)), 0.3))
        {
            plan.TryPlaceAgainstWalls("water_cooler");
        }

        plan.Queue = plan.Vendors.ToList();
        plan.PatientSpot = plan.Vendors.Count > 0 ? plan.Vendors[0] : f.Tile(f.W / 2, frontV);
        plan.StaffSpot = plan.PatientSpot;

        // A seat by each side wall, out of the way of the door, if there is a row to spare.
        if (template.Furniture.Contains("waiting_seat") && f.D - 2 > frontV)
        {
            foreach (var u in new[] { 0, f.W - 1 })
            {
                var tile = f.Tile(u, f.D - 2);
                if ((u + 1 < plan.DoorOffset || u > plan.DoorOffset + plan.DoorWidth) && !plan.Items.Any(i => i.Overlaps(tile.X, tile.Y, 1, 1)))
                {
                    plan.AddSeat("waiting_seat", tile);
                }
            }
        }
    }

    // ------------------------------------------------------------------ frame and plan

    /// <summary>
    /// The room's own coordinates: u runs along the door wall (0..W), v from the back wall (0)
    /// to the door wall (D).
    /// </summary>
    private sealed class Frame
    {
        public Frame(GridRect rect, WallSide side)
        {
            Rect = rect;
            Side = side;
            (W, D) = LocalSize(rect, side);
        }

        public GridRect Rect { get; }

        public WallSide Side { get; }

        public int W { get; }

        public int D { get; }

        /// <summary>True when the door faces east or west, so the room's u axis runs along Y.</summary>
        public bool SideOn => Side is WallSide.East or WallSide.West;

        public (double X, double Y, double W, double L) ToWorld(double u, double v, double su, double sv) => Side switch
        {
            WallSide.South => (Rect.X + u, Rect.Y + v, su, sv),
            WallSide.North => (Rect.X + W - u - su, Rect.Y + D - v - sv, su, sv),
            WallSide.East => (Rect.X + v, Rect.Y + u, sv, su),
            _ => (Rect.X + D - v - sv, Rect.Y + u, sv, su),
        };

        public GridCoord Tile(int u, int v)
        {
            var (x, y, _, _) = ToWorld(Math.Clamp(u, 0, W - 1), Math.Clamp(v, 0, D - 1), 1, 1);
            return new GridCoord((int)Math.Round(x), (int)Math.Round(y));
        }

        /// <summary>A doorway in the front wall (or the back wall), as a side and offset of the room rectangle.</summary>
        public Doorway Door(int offset, int width, bool back)
        {
            var side = back ? Opposite(Side) : Side;
            var (x, y, _, _) = ToWorld(offset, back ? 0 : D - 1, width, 1);
            var along = side is WallSide.North or WallSide.South ? (int)Math.Round(x) - Rect.X : (int)Math.Round(y) - Rect.Y;
            return new Doorway(side, along, width);
        }

        public static WallSide Opposite(WallSide side) => side switch
        {
            WallSide.North => WallSide.South,
            WallSide.South => WallSide.North,
            WallSide.East => WallSide.West,
            _ => WallSide.East,
        };
    }

    private sealed class Plan(Frame frame, GameDatabase database, int doorOffset, int doorWidth, bool backDoor)
    {
        private readonly HashSet<GridCoord> _reserved = new();

        public Frame Frame { get; } = frame;

        public GameDatabase Database { get; } = database;

        public int DoorOffset { get; } = doorOffset;

        public int DoorWidth { get; } = doorWidth;

        public List<PlacedFurniture> Items { get; } = new();

        public List<GridCoord> Seats { get; } = new();

        public List<GridCoord> Queue { get; set; } = new();

        public List<WardBed> Beds { get; } = new();

        /// <summary>The tile in front of each vending machine, where a customer stands to buy.</summary>
        public List<GridCoord> Vendors { get; } = new();

        /// <summary>The tiles just inside each doorway, in site coordinates, which furniture must keep clear.</summary>
        public (double X, double Y, double W, double L) DoorApproach(int depth) =>
            Frame.ToWorld(DoorOffset - 1, Frame.D - depth, DoorWidth + 2, depth);

        public GridCoord PatientSpot { get; set; }

        public GridCoord StaffSpot { get; set; }

        public GridCoord DoorTile => Frame.Tile(DoorOffset + (DoorWidth / 2), Frame.D - 1);

        public void Reserve(GridCoord tile) => _reserved.Add(tile);

        public void AddWorld(string item, double x, double y, double w, double l) => Items.Add(new PlacedFurniture(item, x, y, w, l));

        public void AddChair(string item, GridCoord tile)
        {
            Reserve(tile);
            Items.Add(new PlacedFurniture(item, tile.X + 0.15, tile.Y + 0.15, 0.7, 0.7));
        }

        public void AddSeat(string item, GridCoord tile)
        {
            if (_reserved.Contains(tile))
            {
                return;
            }

            AddChair(item, tile);
            Seats.Add(tile);
        }

        /// <summary>Places an item at a spot in the room's own frame, if it fits there.</summary>
        public bool TryPlaceLocal(string itemId, double u, double v, bool turned = false)
        {
            if (!Database.Furniture.TryGetValue(itemId, out var item))
            {
                return false;
            }

            var su = item.WidthM / Units.MetresPerTile;
            var sv = item.DepthM / Units.MetresPerTile;
            if (turned)
            {
                (su, sv) = (sv, su);
            }

            if (u < 0.2 || v < 0.2 || u + su > Frame.W - 0.2 || v + sv > Frame.D - 0.2)
            {
                return false;
            }

            // Keep the doorways clear.
            if (u < DoorOffset + DoorWidth + 0.5 && u + su > DoorOffset - 0.5 && (v + sv > Frame.D - 2.5 || (backDoor && v < 2.5)))
            {
                return false;
            }

            var r = Frame.ToWorld(u, v, su, sv);
            if (Items.Any(i => i.Overlaps(r.X, r.Y, r.W, r.L, 0.1)))
            {
                return false;
            }

            for (var x = (int)Math.Floor(r.X); x < r.X + r.W; x++)
            {
                for (var y = (int)Math.Floor(r.Y); y < r.Y + r.L; y++)
                {
                    if (_reserved.Contains(new GridCoord(x, y)))
                    {
                        return false;
                    }
                }
            }

            AddWorld(itemId, r.X, r.Y, r.W, r.L);
            return true;
        }

        /// <summary>
        /// Places an item turned side-on against the left (or right) wall, <paramref name="v"/> from the back,
        /// and gives the tile just in front of it.
        /// </summary>
        public bool TryPlaceTurnedAgainstSide(string itemId, double v, bool left, out GridCoord front)
        {
            front = default;
            if (!Database.Furniture.TryGetValue(itemId, out var item))
            {
                return false;
            }

            var depth = item.DepthM / Units.MetresPerTile;
            var width = item.WidthM / Units.MetresPerTile;
            var u = left ? 0.3 : Frame.W - 0.3 - depth;
            var frontU = left ? (int)Math.Ceiling(u + depth) : (int)Math.Floor(u) - 1;
            var tile = Frame.Tile(frontU, (int)Math.Floor(v + (width / 2)));
            front = tile;
            if (_reserved.Contains(tile) || frontU < 0 || frontU >= Frame.W || Items.Any(i => i.Overlaps(tile.X, tile.Y, 1, 1)))
            {
                return false;
            }

            return TryPlaceLocal(itemId, u, v, turned: true);
        }

        /// <summary>
        /// A square table in the middle of a 4 x 4 tile block whose corner is (<paramref name="bu"/>, <paramref name="bv"/>),
        /// with a chair on each side. Skipped if it would touch other furniture, a kept-clear tile or the doorway.
        /// </summary>
        public bool TryTableOfFour(string tableId, string chairId, int bu, int bv)
        {
            if (!Database.Furniture.TryGetValue(tableId, out var table))
            {
                return false;
            }

            var size = table.WidthM / Units.MetresPerTile;
            var inset = (2 - size) / 2;
            var chairs = new[] { (bu + 1, bv), (bu + 3, bv + 1), (bu + 2, bv + 3), (bu, bv + 2) }
                .Select(c => Frame.Tile(c.Item1, c.Item2)).ToList();

            // Everything must be inside the room, off its edge tiles, and clear of the doorway.
            if (bu < 1 || bv < 1 || bu + 4 > Frame.W - 1 || bv + 4 > Frame.D - 1)
            {
                return false;
            }

            var door = DoorApproach(2);
            var whole = Frame.ToWorld(bu, bv, 4, 4);
            if (door.X < whole.X + whole.W && whole.X < door.X + door.W && door.Y < whole.Y + whole.L && whole.Y < door.Y + door.L)
            {
                return false;
            }

            if (backDoor && bv < 3)
            {
                return false;
            }

            // A one-tile aisle all round, so every chair can be reached.
            var top = Frame.ToWorld(bu + 1 + inset, bv + 1 + inset, size, size);
            if (Items.Any(i => i.Overlaps(whole.X, whole.Y, whole.W, whole.L, 0.95)))
            {
                return false;
            }

            for (var x = (int)Math.Floor(whole.X); x < whole.X + whole.W; x++)
            {
                for (var y = (int)Math.Floor(whole.Y); y < whole.Y + whole.L; y++)
                {
                    if (_reserved.Contains(new GridCoord(x, y)))
                    {
                        return false;
                    }
                }
            }

            AddWorld(tableId, top.X, top.Y, top.W, top.L);
            foreach (var chair in chairs)
            {
                AddSeat(chairId, chair);
            }

            return true;
        }

        /// <summary>Tries the back corners, then along the back wall, then down the side walls.</summary>
        public bool TryPlaceAgainstWalls(string itemId)
        {
            if (!Database.Furniture.TryGetValue(itemId, out var item))
            {
                return false;
            }

            var su = item.WidthM / Units.MetresPerTile;
            var sv = item.DepthM / Units.MetresPerTile;
            if (TryPlaceLocal(itemId, 0.3, 0.3) || TryPlaceLocal(itemId, Frame.W - 0.3 - su, 0.3))
            {
                return true;
            }

            for (var u = 1.0; u + su < Frame.W - 0.3; u += 0.5)
            {
                if (TryPlaceLocal(itemId, u, 0.3))
                {
                    return true;
                }
            }

            for (var v = 0.3; v + su < Frame.D - 0.3; v += 0.5)
            {
                if (TryPlaceLocal(itemId, 0.3, v, turned: true) || TryPlaceLocal(itemId, Frame.W - 0.3 - sv, v, turned: true))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
