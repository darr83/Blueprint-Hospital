using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Hospital;

/// <summary>
/// The pre-built A&amp;E every new game starts with, laid out inside the starter building.
/// (Rooms become player-built with the building system in a later milestone.)
/// Coordinates below are relative to the building's corner; one tile is 0.5 m.
/// </summary>
public static class EmergencyDepartmentLayout
{
    public const string Department = "emergency";

    public static List<CareSpace> Create(Building building)
    {
        var o = new GridCoord(building.Footprint.X, building.Footprint.Y);
        GridRect R(int x, int y, int w, int l) => new(o.X + x, o.Y + y, w, l);
        GridCoord P(int x, int y) => new(o.X + x, o.Y + y);

        var spaces = new List<CareSpace>
        {
            // Main corridor (4 m wide for trolleys) from the ambulance entrance westwards.
            new()
            {
                Id = "corridor_main", Name = "A&E corridor", Kind = CareSpaceKind.Corridor, DepartmentId = Department,
                Rect = R(4, 9, 56, 8), HasWalls = false,
            },
            new()
            {
                Id = "corridor_link", Name = "Waiting room corridor", Kind = CareSpaceKind.Corridor, DepartmentId = Department,
                Rect = R(22, 17, 8, 9), HasWalls = false,
            },

            // North side of the corridor: treatment rooms, 4 m x 4.5 m (18 m²); resus 6 m x 4.5 m (27 m²).
            Treatment("minors_1", "Minors 1", CareSpaceKind.Minors, R(4, 0, 8, 9), P(7, 3), P(5, 4)),
            Treatment("minors_2", "Minors 2", CareSpaceKind.Minors, R(12, 0, 8, 9), P(15, 3), P(13, 4)),
            new()
            {
                Id = "staff_room", Name = "Staff room", Kind = CareSpaceKind.StaffRoom, DepartmentId = Department,
                Rect = R(20, 0, 8, 9), Doors = new[] { new Doorway(WallSide.South, 3, 2) },
                StaffSpot = P(23, 3),
                Seats = new[] { P(21, 2), P(22, 2), P(24, 2), P(25, 2), P(21, 5), P(22, 5), P(24, 5), P(25, 5) },
            },
            Treatment("majors_1", "Majors 1", CareSpaceKind.Majors, R(28, 0, 8, 9), P(31, 3), P(33, 4)),
            Treatment("majors_2", "Majors 2", CareSpaceKind.Majors, R(36, 0, 8, 9), P(39, 3), P(41, 4)),
            new()
            {
                Id = "resus_1", Name = "Resus", Kind = CareSpaceKind.Resus, DepartmentId = Department,
                Rect = R(44, 0, 12, 9), Doors = new[] { new Doorway(WallSide.South, 4, 4) },
                PatientSpot = P(49, 3), StaffSpot = P(47, 4),
            },

            // South side: offices, triage, nurses' station and the waiting room.
            new()
            {
                Id = "doctors_office", Name = "Doctors' office", Kind = CareSpaceKind.DoctorsOffice, DepartmentId = Department,
                Rect = R(4, 17, 10, 9), Doors = new[] { new Doorway(WallSide.North, 6, 2) },
                StaffSpot = P(8, 21),
                Seats = new[] { P(6, 20), P(8, 20), P(10, 20), P(6, 23), P(8, 23), P(10, 23) },
            },
            new()
            {
                Id = "triage_1", Name = "Triage", Kind = CareSpaceKind.Triage, DepartmentId = Department,
                Rect = R(30, 17, 8, 9), Doors = new[] { new Doorway(WallSide.West, 3, 2) },
                PatientSpot = P(34, 21), StaffSpot = P(33, 22),
            },
            new()
            {
                Id = "nurses_station", Name = "Nurses' station", Kind = CareSpaceKind.NursesStation, DepartmentId = Department,
                Rect = R(38, 17, 10, 9), Doors = new[] { new Doorway(WallSide.North, 2, 6) },
                StaffSpot = P(42, 20),
                Seats = new[] { P(40, 20), P(42, 20), P(44, 20), P(46, 20), P(40, 22), P(42, 22), P(44, 22), P(46, 22) },
            },
            new()
            {
                Id = "waiting_room", Name = "Waiting room", Kind = CareSpaceKind.Waiting, DepartmentId = Department,
                Rect = R(14, 26, 22, 14),
                Doors = new[] { new Doorway(WallSide.North, 8, 8), new Doorway(WallSide.South, 8, 8) },
                // Reception desk: receptionist behind it, patients in front.
                StaffSpot = P(33, 32), PatientSpot = P(33, 34),
                Seats = WaitingSeats(P),
            },
        };

        return spaces;
    }

    /// <summary>Tiles where patients queue for reception, nearest the desk first.</summary>
    public static IReadOnlyList<GridCoord> ReceptionQueue(Building building)
    {
        var o = new GridCoord(building.Footprint.X, building.Footprint.Y);
        return new[] { new GridCoord(o.X + 33, o.Y + 34), new GridCoord(o.X + 33, o.Y + 35), new GridCoord(o.X + 33, o.Y + 36), new GridCoord(o.X + 32, o.Y + 37), new GridCoord(o.X + 31, o.Y + 37), new GridCoord(o.X + 30, o.Y + 37) };
    }

    private static CareSpace Treatment(string id, string name, CareSpaceKind kind, GridRect rect, GridCoord bed, GridCoord staff) => new()
    {
        Id = id,
        Name = name,
        Kind = kind,
        DepartmentId = Department,
        Rect = rect,
        Doors = new[] { new Doorway(WallSide.South, 3, 3) },
        PatientSpot = bed,
        StaffSpot = staff,
    };

    private static List<GridCoord> WaitingSeats(Func<int, int, GridCoord> p)
    {
        var seats = new List<GridCoord>();
        foreach (var y in new[] { 29, 31, 33, 35 })
        {
            for (var x = 15; x <= 20; x++)
            {
                seats.Add(p(x, y));
            }
        }

        foreach (var y in new[] { 29, 31 })
        {
            for (var x = 25; x <= 30; x++)
            {
                seats.Add(p(x, y));
            }
        }

        return seats;
    }

    /// <summary>Builds the walkability grid: paths and floors walkable, walls blocking except at doors and entrances.</summary>
    public static NavGrid BuildNavGrid(SiteMap site, IEnumerable<CareSpace> spaces)
    {
        var grid = new NavGrid(site.Width, site.Length);
        foreach (var tile in site.Bounds.Tiles())
        {
            grid.SetCost(tile, site[tile] switch
            {
                TileKind.BuildingFloor => 1,
                TileKind.Paving => 1,
                TileKind.AmbulanceBay => 1,
                TileKind.Parking => 2,
                TileKind.Road => 3,
                TileKind.Grass => 4,
                _ => 1,
            });
        }

        foreach (var building in site.Buildings)
        {
            BlockPerimeter(grid, building.Footprint, (tile, side) => building.IsOpening(tile, side));
        }

        foreach (var space in spaces.Where(s => s.HasWalls))
        {
            BlockPerimeter(grid, space.Rect, space.IsDoor);
        }

        return grid;
    }

    private static void BlockPerimeter(NavGrid grid, GridRect rect, Func<GridCoord, WallSide, bool> isOpening)
    {
        for (var x = rect.X; x < rect.Right; x++)
        {
            var north = new GridCoord(x, rect.Y);
            if (!isOpening(north, WallSide.North))
            {
                grid.BlockEdge(north, new GridCoord(x, rect.Y - 1));
            }

            var south = new GridCoord(x, rect.Bottom - 1);
            if (!isOpening(south, WallSide.South))
            {
                grid.BlockEdge(south, new GridCoord(x, rect.Bottom));
            }
        }

        for (var y = rect.Y; y < rect.Bottom; y++)
        {
            var west = new GridCoord(rect.X, y);
            if (!isOpening(west, WallSide.West))
            {
                grid.BlockEdge(west, new GridCoord(rect.X - 1, y));
            }

            var east = new GridCoord(rect.Right - 1, y);
            if (!isOpening(east, WallSide.East))
            {
                grid.BlockEdge(east, new GridCoord(rect.Right, y));
            }
        }
    }
}
