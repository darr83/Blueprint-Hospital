using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Room decorations that match what each room is for: a skeleton in fracture clinic, a model
/// heart in cardiology, posters, boards and clocks on the walls, plants, a water cooler and so on.
/// They are only for looks: the simulation doesn't know about them. Floor props stand in free
/// spots along the walls, away from doors, beds, seats and furniture. Wall pieces are painted on
/// the inside of the walls and only show on walls that face the viewer, as with a real cutaway.
/// </summary>
public static class Decorations
{
    private enum Wall
    {
        Tv, HandWashPoster, HeightChart, PainPoster, EyeChart, GelDispenser, GloveDispenser, SharpsBin,
        OxygenPanel, CrashClock, BedBoard, Clock, Lightbox, Noticeboard, EcgPoster, BrainPoster, GutPoster,
        PastelPrint, BpCuff, DiabetesPoster, ClinicSign, PharmacySign, WarningLight, ApronRack, RadiationSign,
        ControlWindow, HeartPoster, Mirror, WardBoard, BonePoster, FireExtinguisher,
        Welcome, EmergencyDirections, ToiletSign, AccessibleSign, FireExit, StaffOnly, NoSmoking, MenuBoard, VisitingTimes, Defibrillator, OutpatientsWelcome,
    }

    private enum Floor
    {
        Plant, WaterCooler, LeafletRack, Scales, SlingBox, AirwayBag, Bookshelf, Fridge, Skeleton, HeartModel,
        BrainModel, GutModel, PelvisModel, PrivacyScreen, Kiosk, FloorLamp, MannequinHead, ScopeCabinet,
        BlanketStack, ScrubSink, PlasterShelf, Bucket, GymBall, Dumbbells, OxygenSpare, Bins,
    }

    private enum OnTop
    {
        Computer, Phone, Folders, KettleMugs, ReflexHammer, PillTray, GelWarmer,
    }

    /// <summary>A picture or sign on the inside of a wall: <see cref="Rect"/>'s <see cref="Side"/> wall from T0 to T1 tiles along it.</summary>
    public sealed record WallPiece(GridRect Rect, WallSide Side, double T0, double T1, string Kind, Action<CanvasItem, float, float> DrawFlat);

    /// <summary>A prop standing on the floor at (X, Y), its front facing (Dx, Dy) into the room.</summary>
    public sealed record FloorPiece(string Kind, double X, double Y, float Scale, int Dx, int Dy);

    /// <summary>A small thing on a desk or counter top at (X, Y), <see cref="TopPx"/> 2D pixels up.</summary>
    public sealed record TopPiece(string Kind, double X, double Y, float TopPx, bool AlongX);

    /// <summary>Theatre lights (or the cath lab's screens) hanging over (X, Y).</summary>
    public sealed record OverheadPiece(bool Theatre, double X, double Y);

    /// <summary>The wall pieces placed by the last <see cref="Plan"/> (the 3D world builds from these).</summary>
    public static List<WallPiece> WallPieces { get; } = new();

    public static List<FloorPiece> FloorPieces { get; } = new();

    public static List<TopPiece> TopPieces { get; } = new();

    public static List<OverheadPiece> OverheadPieces { get; } = new();

    /// <summary>A wall piece drawn flat into a w × h pixel box (y down), for painting into a texture.</summary>
    private static void DrawFlatWall(CanvasItem c, Wall kind, float w, float h) => DrawWall(c, kind, (u, v) => new Vector2(u * w, (1 - v) * h));

    private static void AddWallPiece(GridRect r, WallSide side, double t0, double t1, Wall kind) =>
        WallPieces.Add(new WallPiece(r, side, t0, t1, kind.ToString(), (c, w, h) => DrawFlatWall(c, kind, w, h)));

    private sealed record Recipe(Wall[] Walls, Floor[] Floors, (string Item, OnTop[] Things)[]? Tops = null);

    private static readonly Wall[] NoWalls = Array.Empty<Wall>();
    private static readonly Floor[] NoFloors = Array.Empty<Floor>();

    private static Recipe? RecipeFor(CareSpace space) => space.Kind switch
    {
        CareSpaceKind.Waiting => new(new[] { Wall.Welcome, Wall.Tv, Wall.EmergencyDirections, Wall.Defibrillator, Wall.HandWashPoster, Wall.NoSmoking }, new[] { Floor.WaterCooler, Floor.LeafletRack, Floor.Plant, Floor.Bins, Floor.Plant }),
        CareSpaceKind.Cafeteria => new(new[] { Wall.MenuBoard, Wall.Clock, Wall.NoSmoking, Wall.Defibrillator }, new[] { Floor.Bins, Floor.Plant, Floor.Plant }),
        CareSpaceKind.VendingPoint => new(new[] { Wall.HandWashPoster, Wall.NoSmoking }, new[] { Floor.Bins }),
        CareSpaceKind.Toilets => new(new[] { Wall.ToiletSign, Wall.HandWashPoster }, NoFloors),
        CareSpaceKind.AccessibleToilet => new(new[] { Wall.AccessibleSign }, NoFloors),
        CareSpaceKind.StaffToilet => new(new[] { Wall.StaffOnly }, NoFloors),
        CareSpaceKind.CleaningRoom => new(new[] { Wall.StaffOnly, Wall.GloveDispenser }, NoFloors),
        CareSpaceKind.OutreachOffice => new(new[] { Wall.Noticeboard, Wall.Clock }, new[] { Floor.Bookshelf },
            new[] { ("office_desk", new[] { OnTop.Computer, OnTop.Phone }) }),
        CareSpaceKind.Triage => new(new[] { Wall.HeightChart, Wall.PainPoster }, new[] { Floor.Scales }),
        CareSpaceKind.Minors => new(new[] { Wall.EyeChart, Wall.GelDispenser }, new[] { Floor.SlingBox }),
        CareSpaceKind.Majors => new(new[] { Wall.OxygenPanel, Wall.GloveDispenser, Wall.SharpsBin }, NoFloors),
        CareSpaceKind.Resus => new(new[] { Wall.CrashClock, Wall.OxygenPanel, Wall.SharpsBin }, new[] { Floor.AirwayBag }),
        CareSpaceKind.NursesStation => new(new[] { Wall.BedBoard, Wall.Clock }, NoFloors,
            new[] { ("station_counter", new[] { OnTop.Computer, OnTop.Phone, OnTop.Folders }), ("office_desk", new[] { OnTop.Computer, OnTop.Folders }) }),
        CareSpaceKind.DoctorsOffice => new(new[] { Wall.Lightbox, Wall.Clock }, new[] { Floor.Bookshelf },
            new[] { ("office_desk", new[] { OnTop.Computer, OnTop.Computer, OnTop.Folders }) }),
        CareSpaceKind.StaffRoom => new(new[] { Wall.Noticeboard, Wall.Clock, Wall.StaffOnly }, new[] { Floor.Fridge, Floor.Plant, Floor.Bins },
            new[] { ("kitchenette", new[] { OnTop.KettleMugs }) }),
        CareSpaceKind.Clinic => space.TemplateId switch
        {
            "fracture_clinic" => new(new[] { Wall.Lightbox, Wall.BonePoster }, new[] { Floor.Skeleton }, ClinicDesk()),
            "cardiology_clinic" => new(new[] { Wall.EcgPoster, Wall.HeartPoster }, new[] { Floor.HeartModel }, ClinicDesk()),
            "neurology_clinic" => new(new[] { Wall.BrainPoster }, new[] { Floor.BrainModel }, ClinicDesk(OnTop.ReflexHammer)),
            "gastro_clinic" => new(new[] { Wall.GutPoster }, new[] { Floor.GutModel }, ClinicDesk()),
            "gynaecology_clinic" or "gynae_exam_room" => new(new[] { Wall.PastelPrint }, new[] { Floor.PelvisModel, Floor.PrivacyScreen }, ClinicDesk()),
            "medical_clinic" => new(new[] { Wall.BpCuff, Wall.DiabetesPoster }, NoFloors, ClinicDesk()),
            "antenatal_clinic" => new(new[] { Wall.PastelPrint, Wall.BpCuff }, new[] { Floor.Scales, Floor.Plant }, ClinicDesk()),
            "antenatal_scan_room" => new(new[] { Wall.PastelPrint }, new[] { Floor.FloorLamp }, new[] { ("ultrasound_machine", new[] { OnTop.GelWarmer }) }),
            _ => new(new[] { Wall.HandWashPoster }, NoFloors, ClinicDesk()),
        },
        CareSpaceKind.OutpatientReception => new(new[] { Wall.OutpatientsWelcome, Wall.ClinicSign, Wall.HandWashPoster, Wall.Defibrillator }, new[] { Floor.Kiosk, Floor.Plant, Floor.Bins },
            new[] { ("reception_desk", new[] { OnTop.Computer, OnTop.Phone }) }),
        CareSpaceKind.XRay => new(new[] { Wall.WarningLight, Wall.ApronRack, Wall.RadiationSign, Wall.Lightbox }, NoFloors),
        CareSpaceKind.CT => new(new[] { Wall.RadiationSign, Wall.ControlWindow }, NoFloors),
        CareSpaceKind.Ultrasound => new(new[] { Wall.PastelPrint }, new[] { Floor.FloorLamp }, new[] { ("ultrasound_machine", new[] { OnTop.GelWarmer }) }),
        CareSpaceKind.Echo => new(new[] { Wall.HeartPoster }, new[] { Floor.FloorLamp }, new[] { ("ultrasound_machine", new[] { OnTop.GelWarmer }) }),
        CareSpaceKind.Eeg => new(new[] { Wall.BrainPoster }, new[] { Floor.MannequinHead }),
        CareSpaceKind.CathLab => new(new[] { Wall.ApronRack, Wall.WarningLight }, NoFloors),
        CareSpaceKind.Endoscopy => new(new[] { Wall.GutPoster }, new[] { Floor.ScopeCabinet }),
        CareSpaceKind.EndoscopyRecovery => new(new[] { Wall.Clock }, new[] { Floor.BlanketStack }),
        CareSpaceKind.Theatre => new(new[] { Wall.Clock, Wall.StaffOnly, Wall.Lightbox, Wall.GloveDispenser }, new[] { Floor.ScrubSink }),
        CareSpaceKind.DeliveryRoom => new(new[] { Wall.Clock, Wall.PastelPrint, Wall.GelDispenser, Wall.OxygenPanel }, new[] { Floor.BlanketStack, Floor.Plant }),
        CareSpaceKind.PlasterRoom => new(new[] { Wall.BonePoster }, new[] { Floor.PlasterShelf, Floor.Bucket }),
        CareSpaceKind.PhysioGym => new(new[] { Wall.Mirror, Wall.Mirror }, new[] { Floor.GymBall, Floor.Dumbbells, Floor.GymBall }),
        CareSpaceKind.Pharmacy => new(new[] { Wall.PharmacySign, Wall.StaffOnly, Wall.Clock }, NoFloors, new[] { ("pharmacy_counter", new[] { OnTop.PillTray, OnTop.Computer }) }),
        CareSpaceKind.Ward => new(new[] { Wall.WardBoard, Wall.GelDispenser, Wall.VisitingTimes, WardPoster(space), Wall.Clock }, new[] { Floor.Plant, WardExtra(space) }),
        _ => null,
    };

    private static (string, OnTop[])[] ClinicDesk(params OnTop[] extra) =>
        new[] { ("office_desk", new[] { OnTop.Computer }.Concat(extra).ToArray()), ("triage_desk", new[] { OnTop.Computer }.Concat(extra).ToArray()) };

    private static Wall WardPoster(CareSpace ward) => ward.DepartmentId switch
    {
        "cardiology" => Wall.HeartPoster,
        "neurology" => Wall.BrainPoster,
        "gastroenterology" => Wall.GutPoster,
        "gynaecology" or "maternity" => Wall.PastelPrint,
        "trauma_orthopaedics" => Wall.BonePoster,
        _ => Wall.HandWashPoster,
    };

    private static Floor WardExtra(CareSpace ward) => ward.DepartmentId switch
    {
        "cardiology" => Floor.HeartModel,
        "neurology" => Floor.BrainModel,
        "trauma_orthopaedics" => Floor.Skeleton,
        "general_medicine" or "emergency" => Floor.OxygenSpare,
        "maternity" => Floor.BlanketStack,
        _ => Floor.Plant,
    };

    // ------------------------------------------------------------------ placement

    /// <summary>Every decoration in the hospital: where it sorts, and how to draw it.</summary>
    public static List<(Vector2 Anchor, Action<CanvasItem> Draw)> Plan(HospitalSimulation sim)
    {
        var props = new List<(Vector2, Action<CanvasItem>)>();
        WallPieces.Clear();
        FloorPieces.Clear();
        TopPieces.Clear();
        OverheadPieces.Clear();
        foreach (var space in sim.Spaces.Where(s => s.HasWalls && IsoDraw.OnViewFloor(s.Rect.Y)))
        {
            if (RecipeFor(space) is not { } recipe)
            {
                continue;
            }

            var blocked = Blocked(sim, space);
            PlaceWalls(sim, space, recipe.Walls, props);
            PlaceFloors(space, recipe.Floors, blocked, props);
            if (recipe.Tops is { } tops)
            {
                PlaceTops(sim, space, tops, props);
            }

            // Theatre lights and the cath lab's screens hang over the table.
            if (space.Kind is CareSpaceKind.Theatre or CareSpaceKind.CathLab)
            {
                var (cx, cy) = (space.PatientSpot.X + 0.5, space.PatientSpot.Y + 0.5);
                var theatre = space.Kind == CareSpaceKind.Theatre;
                OverheadPieces.Add(new OverheadPiece(theatre, theatre ? cx : cx + 1.4, theatre ? cy : cy - 0.6));
                props.Add((IsoDraw.ToScreen(cx, cy) + new Vector2(0, 3), c =>
                {
                    if (theatre)
                    {
                        SurgicalLights(c, cx, cy);
                    }
                    else
                    {
                        MonitorBank(c, cx + 1.4, cy - 0.6);
                    }
                }));
            }
        }

        // The extinguishers and plants follow the ground floor's outer walls, so only show them there.
        if (IsoDraw.ViewFloor == 0)
        {
            PlaceCorridorBits(sim, props);
        }
        return props;
    }

    /// <summary>Tiles in a room that a floor decoration must not stand on.</summary>
    private static HashSet<GridCoord> Blocked(HospitalSimulation sim, CareSpace space)
    {
        var blocked = new HashSet<GridCoord>();
        void Area(double x, double y, double w, double l)
        {
            for (var tx = (int)Math.Floor(x); tx < (int)Math.Ceiling(x + w); tx++)
            {
                for (var ty = (int)Math.Floor(y); ty < (int)Math.Ceiling(y + l); ty++)
                {
                    blocked.Add(new GridCoord(tx, ty));
                }
            }
        }

        foreach (var item in space.Furniture)
        {
            Area(item.X - 0.3, item.Y - 0.3, item.Width + 0.6, item.Length + 0.6);
        }

        Area(space.PatientSpot.X - 1.4, space.PatientSpot.Y - 2.2, 3.8, 5.4);
        foreach (var bed in space.Beds)
        {
            Area(bed.PatientSpot.X - 1.4, bed.PatientSpot.Y - 2.2, 3.8, 5.4);
            Area(bed.StaffSpot.X - 1, bed.StaffSpot.Y - 1, 3, 3);
        }

        foreach (var tile in space.Seats.Concat(space.QueueTiles).Append(space.StaffSpot))
        {
            Area(tile.X - 1, tile.Y - 1, 3, 3);
        }

        // Keep the way in clear: the doorway and two tiles either side, three deep.
        var r = space.Rect;
        foreach (var door in space.Doors)
        {
            var (a, b) = (door.Offset - 2, door.Offset + door.Width + 2);
            switch (door.Side)
            {
                case WallSide.North: Area(r.X + a, r.Y, b - a, 3); break;
                case WallSide.South: Area(r.X + a, r.Bottom - 3, b - a, 3); break;
                case WallSide.West: Area(r.X, r.Y + a, 3, b - a); break;
                default: Area(r.Right - 3, r.Y + a, 3, b - a); break;
            }
        }

        // People walk to the other rooms' corridor chairs too.
        foreach (var chair in sim.Chairs)
        {
            blocked.Add(chair.Tile);
        }

        return blocked;
    }

    private static readonly WallSide[] Sides4 = { WallSide.North, WallSide.East, WallSide.South, WallSide.West };

    private static void PlaceFloors(CareSpace space, Floor[] floors, HashSet<GridCoord> blocked, List<(Vector2, Action<CanvasItem>)> props)
    {
        if (floors.Length == 0)
        {
            return;
        }

        // Candidate spots: free tiles touching a wall, corners first, then spread along the walls.
        var r = space.Rect;
        var candidates = new List<(GridCoord Tile, double Score)>();
        for (var x = r.X; x < r.Right; x++)
        {
            for (var y = r.Y; y < r.Bottom; y++)
            {
                var edge = x == r.X || x == r.Right - 1 || y == r.Y || y == r.Bottom - 1;
                var tile = new GridCoord(x, y);
                if (!edge || blocked.Contains(tile))
                {
                    continue;
                }

                // Corners first, and the back walls of the usual view before the front ones, where a
                // low front wall would hide them.
                var corner = (x == r.X || x == r.Right - 1) && (y == r.Y || y == r.Bottom - 1);
                var back = x == r.X || y == r.Y;
                candidates.Add((tile, (corner ? 0 : 10) + (back ? 0 : 25) + ((x * 7 + y * 13) % 5)));
            }
        }

        var used = new List<GridCoord>();
        foreach (var floor in floors)
        {
            var pick = candidates.Where(c => used.All(u => Math.Abs(u.X - c.Tile.X) + Math.Abs(u.Y - c.Tile.Y) >= 3))
                .OrderBy(c => c.Score).Select(c => (GridCoord?)c.Tile).FirstOrDefault();
            if (pick is not { } tile)
            {
                break;
            }

            used.Add(tile);

            // Stand a little in from the wall the tile touches.
            var (gx, gy) = (tile.X + 0.5, tile.Y + 0.5);
            gx += tile.X == r.X ? 0.15 : tile.X == r.Right - 1 ? -0.15 : 0;
            gy += tile.Y == r.Y ? 0.15 : tile.Y == r.Bottom - 1 ? -0.15 : 0;
            var kind = floor;
            var scale = kind is Floor.Bins or Floor.Plant or Floor.Skeleton or Floor.PrivacyScreen or Floor.FloorLamp or Floor.GymBall or Floor.Bucket or Floor.OxygenSpare ? FloorScale : 1.15f;
            var (dx, dy) = tile.Y == r.Y ? (0, 1) : tile.Y == r.Bottom - 1 ? (0, -1) : tile.X == r.X ? (1, 0) : (-1, 0);
            FloorPieces.Add(new FloorPiece(kind.ToString(), gx, gy, scale, dx, dy));
            props.Add((IsoDraw.ToScreen(gx, gy), c => Scaled(c, IsoDraw.ToScreen(gx, gy), scale, () => DrawFloor(c, kind, gx, gy))));
        }
    }

    /// <summary>Floor decorations are drawn a little larger than life so they read at normal zoom.</summary>
    private const float FloorScale = 1.45f;

    /// <summary>Draws with everything scaled about <paramref name="about"/>, then puts the prop's own transform back.</summary>
    private static void Scaled(CanvasItem c, Vector2 about, float k, Action draw)
    {
        if (c is not Node2D node)
        {
            draw();
            return;
        }

        c.DrawSetTransformMatrix(new Transform2D(0, new Vector2(k, k), 0, (about * (1 - k)) - node.Position));
        draw();
        c.DrawSetTransform(-node.Position);
    }

    /// <summary>Spreads the wall pieces over all four walls, so each view shows some of them.</summary>
    private static void PlaceWalls(HospitalSimulation sim, CareSpace space, Wall[] walls, List<(Vector2, Action<CanvasItem>)> props)
    {
        if (walls.Length == 0)
        {
            return;
        }

        var r = space.Rect;
        var next = 0;
        foreach (var side in Sides4)
        {
            var length = side is WallSide.North or WallSide.South ? r.Width : r.Length;

            // Free stretches of this wall: not a doorway, not behind tall furniture.
            var free = new bool[length];
            for (var t = 0; t < length; t++)
            {
                free[t] = t > 0 && t < length - 1;
            }

            foreach (var door in space.Doors.Where(d => d.Side == side))
            {
                for (var t = door.Offset - 1; t < door.Offset + door.Width + 1; t++)
                {
                    if (t >= 0 && t < length)
                    {
                        free[t] = false;
                    }
                }
            }

            foreach (var item in space.Furniture)
            {
                var tall = sim.Database.Furniture.TryGetValue(item.ItemId, out var def) && def.HeightM * IsoDraw.PixelsPerMetre > WorldLayer.WallHeight;
                if (!tall)
                {
                    continue;
                }

                var (near, from, to) = side switch
                {
                    WallSide.North => (item.Y - r.Y < 1.2, item.X - r.X, item.X + item.Width - r.X),
                    WallSide.South => (r.Bottom - (item.Y + item.Length) < 1.2, item.X - r.X, item.X + item.Width - r.X),
                    WallSide.West => (item.X - r.X < 1.2, item.Y - r.Y, item.Y + item.Length - r.Y),
                    _ => (r.Right - (item.X + item.Width) < 1.2, item.Y - r.Y, item.Y + item.Length - r.Y),
                };
                if (near)
                {
                    for (var t = (int)Math.Floor(from); t < (int)Math.Ceiling(to); t++)
                    {
                        if (t >= 0 && t < length)
                        {
                            free[t] = false;
                        }
                    }
                }
            }

            // Not over waiting chairs standing against this wall: the picture would sort in front of their backs.
            foreach (var chair in sim.Chairs.Where(c => r.Contains(c.Tile)))
            {
                var (against, at) = side switch
                {
                    WallSide.North => (chair.Tile.Y == r.Y, chair.Tile.X - r.X),
                    WallSide.South => (chair.Tile.Y == r.Bottom - 1, chair.Tile.X - r.X),
                    WallSide.West => (chair.Tile.X == r.X, chair.Tile.Y - r.Y),
                    _ => (chair.Tile.X == r.Right - 1, chair.Tile.Y - r.Y),
                };
                for (var t = at - 1; against && t <= at + 1; t++)
                {
                    if (t >= 0 && t < length)
                    {
                        free[t] = false;
                    }
                }
            }

            // Up to two pieces per wall, each about 1.5 tiles wide, in the longest free stretches.
            var runs = new List<(int Start, int Length)>();
            for (var t = 0; t < length;)
            {
                if (!free[t])
                {
                    t++;
                    continue;
                }

                var s = t;
                while (t < length && free[t])
                {
                    t++;
                }

                runs.Add((s, t - s));
            }

            foreach (var run in runs.Where(run => run.Length >= 3).OrderByDescending(run => run.Length).Take(length >= 8 ? 2 : 1))
            {
                var kind = walls[next++ % walls.Length];
                var width = Math.Min(WallWidth(kind), run.Length - 0.4);
                var centre = run.Start + (run.Length / 2.0);
                var (t0, t1) = (centre - (width / 2), centre + (width / 2));
                var wallSide = side;
                AddWallPiece(r, wallSide, t0, t1, kind);
                props.Add((AfterWall(r, wallSide, t0, t1), c =>
                {
                    if (Facing(r, wallSide))
                    {
                        DrawWall(c, kind, (u, v) => WallPoint(r, wallSide, t0, t1, u, v));
                    }
                }));
            }
        }
    }

    private static double WallWidth(Wall kind) => kind switch
    {
        Wall.Clock or Wall.CrashClock or Wall.GelDispenser or Wall.SharpsBin or Wall.FireExtinguisher or Wall.BpCuff => 1.0,
        Wall.Tv or Wall.BedBoard or Wall.WardBoard or Wall.Noticeboard or Wall.ControlWindow or Wall.Mirror => 3.2,
        Wall.Defibrillator => 0.9,
        Wall.ToiletSign or Wall.AccessibleSign => 1.6,
        Wall.Welcome or Wall.OutpatientsWelcome => 3.4,
        Wall.PharmacySign or Wall.EmergencyDirections or Wall.ClinicSign or Wall.VisitingTimes or Wall.HandWashPoster => 2.4,
        Wall.OxygenPanel or Wall.ApronRack or Wall.GloveDispenser or Wall.FireExit or Wall.StaffOnly or Wall.NoSmoking or Wall.MenuBoard or Wall.RadiationSign or Wall.WarningLight => 2.0,
        _ => 1.7,
    };

    private static void PlaceTops(HospitalSimulation sim, CareSpace space, (string Item, OnTop[] Things)[] tops, List<(Vector2, Action<CanvasItem>)> props)
    {
        foreach (var (itemId, things) in tops)
        {
            foreach (var item in space.Furniture.Where(f => f.ItemId == itemId).Take(2))
            {
                if (!sim.Database.Furniture.TryGetValue(item.ItemId, out var def))
                {
                    continue;
                }

                var height = FurnitureArt.TopHeight(item.ItemId, (float)(def.HeightM * IsoDraw.PixelsPerMetre));
                var alongX = item.Width >= item.Length;
                var length = alongX ? item.Width : item.Length;
                var anchor = new[]
                {
                    IsoDraw.ToScreen(item.X, item.Y), IsoDraw.ToScreen(item.X + item.Width, item.Y),
                    IsoDraw.ToScreen(item.X, item.Y + item.Length), IsoDraw.ToScreen(item.X + item.Width, item.Y + item.Length),
                }.MaxBy(p => p.Y);
                var spots = things.Select((thing, i) =>
                {
                    var f = (i + 0.5) / things.Length;
                    var along = length * f;
                    return (thing, alongX ? item.X + along : item.CentreX, alongX ? item.CentreY : item.Y + along);
                }).ToArray();
                foreach (var (thing, gx, gy) in spots)
                {
                    TopPieces.Add(new TopPiece(thing.ToString(), gx, gy, height, alongX));
                }

                props.Add((anchor + new Vector2(0, 0.8f), c =>
                {
                    foreach (var (thing, gx, gy) in spots)
                    {
                        DrawTop(c, thing, IsoDraw.ToScreen(gx, gy) + new Vector2(0, -height));
                    }
                }));
            }
        }
    }

    /// <summary>Fire extinguishers on the building's walls and plants in its open spaces, outside the rooms.</summary>
    private static void PlaceCorridorBits(HospitalSimulation sim, List<(Vector2, Action<CanvasItem>)> props)
    {
        var rooms = sim.Spaces.Where(s => s.HasWalls && IsoDraw.OnViewFloor(s.Rect.Y)).Select(s => s.Rect).ToList();
        var chairs = sim.Chairs.Select(c => c.Tile).ToHashSet();
        foreach (var building in sim.Site.Buildings)
        {
            var count = 0;
            foreach (var (inside, _, side) in building.OuterEdges())
            {
                if (building.IsOpening(inside, side) || rooms.Any(r => r.X - 2 <= inside.X && inside.X < r.Right + 2 && r.Y - 2 <= inside.Y && inside.Y < r.Bottom + 2)
                    || chairs.Contains(inside) || ((inside.X * 3) + (inside.Y * 5)) % 2 != 0)
                {
                    continue;
                }

                count++;
                if (count % 9 == 3)
                {
                    var rect = new GridRect(inside.X, inside.Y, 1, 1);
                    var wallSide = side;
                    AddWallPiece(rect, wallSide, 0.15, 0.85, Wall.FireExtinguisher);
                    props.Add((AfterWall(rect, wallSide, 0.15, 0.85), c =>
                    {
                        if (Facing(rect, wallSide))
                        {
                            DrawWall(c, Wall.FireExtinguisher, (u, v) => WallPoint(rect, wallSide, 0.15, 0.85, u, v));
                        }
                    }));
                }
                else if (count % 9 == 5)
                {
                    var rect = new GridRect(inside.X, inside.Y, 1, 1);
                    var wallSide = side;
                    AddWallPiece(rect, wallSide, -0.4, 1.4, Wall.FireExit);
                    props.Add((AfterWall(rect, wallSide, -0.4, 1.4), c =>
                    {
                        if (Facing(rect, wallSide))
                        {
                            DrawWall(c, Wall.FireExit, (u, v) => WallPoint(rect, wallSide, -0.4, 1.4, u, v));
                        }
                    }));
                }
                else if (count % 9 == 7)
                {
                    var (gx, gy) = (inside.X + 0.5, inside.Y + 0.5);
                    FloorPieces.Add(new FloorPiece(nameof(Floor.Plant), gx, gy, FloorScale, 0, 1));
                    props.Add((IsoDraw.ToScreen(gx, gy), c => Scaled(c, IsoDraw.ToScreen(gx, gy), FloorScale, () => DrawFloor(c, Floor.Plant, gx, gy))));
                }
            }
        }
    }

    // ------------------------------------------------------------------ walls: geometry

    private const double Half = WorldLayer.WallThickness / 2;

    /// <summary>A point on the inside face of a room's wall: t tiles along it, v pixels up.</summary>
    private static Vector2 InnerFace(GridRect r, WallSide side, double t, float v)
    {
        var (x, y) = side switch
        {
            WallSide.North => (r.X + t, r.Y + Half),
            WallSide.South => (r.X + t, r.Bottom - Half),
            WallSide.West => (r.X + Half, r.Y + t),
            _ => (r.Right - Half, r.Y + t),
        };
        return IsoDraw.ToScreen(x, y) + new Vector2(0, -v);
    }

    /// <summary>
    /// Where a picture lying on a wall between t0 and t1 tiles sorts: just after every wall piece and
    /// corner post it covers, worked out the same way the walls themselves sort. Sorting by the
    /// picture's own ends is not enough: the wall piece under one end can sort later than that end
    /// and paint over it, cutting the sign off with a straight edge.
    /// </summary>
    private static Vector2 AfterWall(GridRect r, WallSide side, double t0, double t1)
    {
        // A footprint along the wall line, from a to a + length tiles along it.
        (double X, double Y, double W, double L) Along(double a, double length) => side switch
        {
            WallSide.North => (r.X + a, r.Y - Half, length, Half * 2),
            WallSide.South => (r.X + a, r.Bottom - Half, length, Half * 2),
            WallSide.West => (r.X - Half, r.Y + a, Half * 2, length),
            _ => (r.Right - Half, r.Y + a, Half * 2, length),
        };

        var best = new[] { InnerFace(r, side, t0, 0), InnerFace(r, side, t1, 0) }.MaxBy(p => p.Y);
        void Consider((double X, double Y, double W, double L) f)
        {
            var key = WorldLayer.FrontFace(f.X, f.Y, f.W, f.L);
            if (key.Y > best.Y)
            {
                best = key;
            }
        }

        for (var k = (int)Math.Floor(t0) - 1; k <= (int)Math.Ceiling(t1) + 1; k++)
        {
            // The post at each tile corner, then the piece of wall between it and the next.
            if (k + Half > t0 && k - Half < t1)
            {
                Consider(Along(k - Half, Half * 2));
            }

            if (k + 1 - Half > t0 && k + Half < t1)
            {
                Consider(Along(k + Half, 1 - (Half * 2)));
            }
        }

        return best + new Vector2(0, 0.6f);
    }

    /// <summary>True when the inside of this wall faces the viewer in the current view angle.</summary>
    private static bool Facing(GridRect r, WallSide side)
    {
        var (nx, ny) = side switch
        {
            WallSide.North => (0.0, 1.0),
            WallSide.South => (0.0, -1.0),
            WallSide.West => (1.0, 0.0),
            _ => (-1.0, 0.0),
        };
        return (IsoDraw.ToScreen(nx, ny) - IsoDraw.ToScreen(0, 0)).Y > 0;
    }

    /// <summary>
    /// A point on a wall piece: u runs 0 to 1 left to right as seen on screen (so pictures never
    /// appear mirrored), v runs 0 to 1 up the face from the floor to the top of the wall.
    /// </summary>
    private static Vector2 WallPoint(GridRect r, WallSide side, double t0, double t1, float u, float v)
    {
        var (a, b) = (InnerFace(r, side, t0, 0), InnerFace(r, side, t1, 0));
        var (left, right) = a.X <= b.X ? (a, b) : (b, a);
        return left.Lerp(right, u) + new Vector2(0, -v * WorldLayer.WallHeight);
    }

    // ------------------------------------------------------------------ walls: art

    private delegate Vector2 Frame(float u, float v);

    private static void Quad(CanvasItem c, Frame f, float u0, float v0, float u1, float v1, Color colour) =>
        c.DrawColoredPolygon(new[] { f(u0, v0), f(u1, v0), f(u1, v1), f(u0, v1) }, colour);

    private static void Line(CanvasItem c, Frame f, float u0, float v0, float u1, float v1, Color colour, float width = 1f) =>
        c.DrawLine(f(u0, v0), f(u1, v1), colour, width, true);

    /// <summary>A poster: white card with a coloured header, framed, with a picture drawn by <paramref name="picture"/>.</summary>
    private static void Poster(CanvasItem c, Frame f, Color header, Action<Frame> picture, float v0 = 0.2f, float v1 = 0.94f)
    {
        Quad(c, f, 0.04f, v0 - 0.03f, 0.96f, v1 + 0.03f, new Color("#8C99A5"));
        Quad(c, f, 0.08f, v0, 0.92f, v1, new Color("#FBFCFD"));
        Quad(c, f, 0.08f, v1 - ((v1 - v0) * 0.18f), 0.92f, v1, header);
        picture((u, v) => f(0.1f + (u * 0.8f), v0 + 0.04f + (v * (v1 - v0) * 0.72f)));
    }

    /// <summary>Lays a flat sign on the wall between two heights (0 to 1 up the wall): <paramref name="draw"/> gets its width and height in pixels.</summary>
    private static void Sign(CanvasItem c, Frame f, float v0, float v1, Action<float, float> draw)
    {
        var (a, b) = (f(0, v1), f(1, v1));
        Flat.On(c, a, b - a, () => draw((b - a).Length(), (v1 - v0) * WorldLayer.WallHeight));
    }

    private static void PicturePoster(CanvasItem c, Frame f, string colour, string title, Pictogram icon, string ink) =>
        Sign(c, (u, v) => f(0.08f + (u * 0.84f), v), 0.3f, 0.95f, (w, h) => Signs.Poster(c, w, h, new Color(colour), title,
            r => Signs.Draw(c, icon, r.GetCenter().X, r.GetCenter().Y, Math.Min(r.Size.X, r.Size.Y) * 0.92f, new Color(ink), Colors.White)));

    private static void DrawWall(CanvasItem c, Wall kind, Frame f)
    {
        var red = new Color("#E53935");
        var blue = new Color("#1F5FBF");
        var dark = new Color("#2B3640");
        switch (kind)
        {
            case Wall.Tv:
                Quad(c, f, 0.05f, 0.42f, 0.95f, 0.98f, new Color("#1B2228"));
                Quad(c, f, 0.09f, 0.48f, 0.91f, 0.93f, new Color("#123A63"));
                Quad(c, f, 0.09f, 0.8f, 0.91f, 0.93f, blue);
                Line(c, f, 0.15f, 0.7f, 0.6f, 0.7f, Colors.White, 1.4f);
                Line(c, f, 0.15f, 0.6f, 0.45f, 0.6f, new Color("#9EC3E8"), 1.2f);
                Line(c, f, 0.68f, 0.56f, 0.86f, 0.56f, new Color("#FFD54F"), 2.2f);
                Line(c, f, 0.68f, 0.67f, 0.86f, 0.67f, new Color("#FFD54F"), 2.2f);
                break;

            case Wall.HandWashPoster:
                Sign(c, f, 0.52f, 0.84f, (w, h) => Signs.Notice(c, w, h, new Color("#00897B"), Pictogram.Drop, "Clean your hands", "Use the gel as you come in"));
                break;

            case Wall.Welcome:
                Sign(c, f, 0.56f, 0.88f, (w, h) => Signs.Banner(c, w, h, new Color("#005EB8"), Pictogram.Info, "Emergency Department", "Please book in at reception"));
                break;

            case Wall.OutpatientsWelcome:
                Sign(c, f, 0.56f, 0.88f, (w, h) => Signs.Banner(c, w, h, new Color("#005EB8"), Pictogram.Info, "Outpatients", "Please book in at reception"));
                break;

            case Wall.EmergencyDirections:
                Sign(c, f, 0.3f, 0.92f, (w, h) => Signs.Directions(c, w, h, "Emergency", ("Triage", 3, Pictogram.Clipboard), ("Toilets", 0, Pictogram.Man), ("Cafe", 2, Pictogram.Cutlery)));
                break;

            case Wall.ToiletSign:
                Sign(c, f, 0.45f, 0.9f, (w, h) => Signs.Toilets(c, w, h, Pictogram.Man, Pictogram.Woman));
                break;

            case Wall.AccessibleSign:
                Sign(c, f, 0.45f, 0.9f, (w, h) => Signs.Toilets(c, w, h, Pictogram.Wheelchair, Pictogram.Baby));
                break;

            case Wall.FireExit:
                Sign(c, f, 0.7f, 0.92f, (w, h) => Signs.FireExit(c, w, h, 0));
                break;

            case Wall.StaffOnly:
                Sign(c, f, 0.6f, 0.83f, (w, h) => Signs.Prohibition(c, w, h, "STAFF ONLY", false));
                break;

            case Wall.NoSmoking:
                Sign(c, f, 0.6f, 0.83f, (w, h) => Signs.Prohibition(c, w, h, "No smoking", true));
                break;

            case Wall.MenuBoard:
                Sign(c, f, 0.28f, 0.94f, (w, h) => Signs.Menu(c, w, h));
                break;

            case Wall.VisitingTimes:
                Sign(c, f, 0.52f, 0.84f, (w, h) => Signs.Notice(c, w, h, new Color("#1E88E5"), Pictogram.Chair, "Visiting times", "2 pm to 8 pm every day"));
                break;

            case Wall.Defibrillator:
                Sign(c, f, 0.38f, 0.8f, (w, h) => Signs.Defibrillator(c, w, h));
                break;

            case Wall.HeightChart:
                Quad(c, f, 0.35f, 0.05f, 0.65f, 0.98f, Colors.White);
                for (var i = 1; i < 10; i++)
                {
                    var v = 0.05f + (i * 0.093f);
                    Line(c, f, 0.35f, v, i % 2 == 0 ? 0.6f : 0.48f, v, new Color("#5B6B78"), 0.8f);
                }

                Quad(c, f, 0.35f, 0.05f, 0.4f, 0.98f, new Color("#7E57C2"));
                break;

            case Wall.PainPoster:
                Poster(c, f, new Color("#7E57C2"), p =>
                {
                    var faces = new[] { "#43A047", "#9CCC65", "#F9A825", "#FB8C00", "#E53935" };
                    for (var i = 0; i < faces.Length; i++)
                    {
                        c.DrawCircle(p(0.1f + (i * 0.2f), 0.55f), 3f, new Color(faces[i]));
                    }
                });
                break;

            case Wall.EyeChart:
                Quad(c, f, 0.2f, 0.25f, 0.8f, 0.97f, Colors.White);
                Quad(c, f, 0.2f, 0.25f, 0.8f, 0.97f, new Color(0, 0, 0, 0));
                for (var i = 0; i < 6; i++)
                {
                    var v = 0.88f - (i * 0.11f);
                    var w = 0.18f - (i * 0.025f);
                    for (var k = -i / 2; k <= i / 2; k++)
                    {
                        var u = 0.5f + (k * (w + 0.04f));
                        Quad(c, f, u - (w / 2), v - 0.035f - (0.01f * (5 - i)), u + (w / 2), v, dark);
                    }
                }

                break;

            case Wall.GelDispenser:
                Quad(c, f, 0.25f, 0.45f, 0.75f, 0.85f, Colors.White);
                Quad(c, f, 0.25f, 0.7f, 0.75f, 0.85f, new Color("#2BB3A3"));
                Quad(c, f, 0.4f, 0.4f, 0.6f, 0.46f, new Color("#9AA6B0"));
                break;

            case Wall.GloveDispenser:
                foreach (var (u, colour) in new[] { (0.05f, "#7E57C2"), (0.37f, "#1F5FBF"), (0.69f, "#43A047") })
                {
                    Quad(c, f, u, 0.45f, u + 0.27f, 0.8f, new Color(colour));
                    Quad(c, f, u + 0.08f, 0.55f, u + 0.19f, 0.62f, Colors.White);
                }

                Quad(c, f, 0.03f, 0.42f, 0.97f, 0.46f, new Color("#B9C3CB"));
                break;

            case Wall.SharpsBin:
                Quad(c, f, 0.2f, 0.35f, 0.8f, 0.72f, new Color("#F7D53E"));
                Quad(c, f, 0.18f, 0.72f, 0.82f, 0.82f, new Color("#F57C00"));
                Line(c, f, 0.3f, 0.55f, 0.7f, 0.55f, new Color("#C9A200"), 1f);
                break;

            case Wall.OxygenPanel:
                Quad(c, f, 0.03f, 0.55f, 0.97f, 0.85f, new Color("#E9EDF0"));
                Line(c, f, 0.03f, 0.55f, 0.97f, 0.55f, new Color("#B9C3CB"), 1f);
                foreach (var (u, colour) in new[] { (0.2f, "#FFFFFF"), (0.4f, "#F2F2F2"), (0.6f, "#FBC02D"), (0.8f, "#2B3640") })
                {
                    c.DrawCircle(f(u, 0.7f), 2.4f, new Color("#7D8A95"));
                    c.DrawCircle(f(u, 0.7f), 1.6f, new Color(colour));
                }

                // Oxygen flowmeter.
                Line(c, f, 0.2f, 0.62f, 0.2f, 0.42f, new Color("#4CAF50"), 2.4f);
                break;

            case Wall.CrashClock:
            case Wall.Clock:
            {
                var centre = f(0.5f, 0.7f);
                var face = kind == Wall.CrashClock ? new Color("#1B2228") : Colors.White;
                c.DrawCircle(centre, 6.5f, kind == Wall.CrashClock ? red : dark);
                c.DrawCircle(centre, 5.3f, face);
                if (kind == Wall.CrashClock)
                {
                    c.DrawString(ThemeDB.FallbackFont, centre + new Vector2(-4.5f, 2.2f), "00:00", HorizontalAlignment.Left, -1, 4, new Color("#FF5A4F"));
                }
                else
                {
                    c.DrawLine(centre, centre + new Vector2(0, -4), dark, 1f);
                    c.DrawLine(centre, centre + new Vector2(3, 0.5f), dark, 1f);
                    c.DrawCircle(centre, 0.8f, red);
                }

                break;
            }

            case Wall.BedBoard:
            case Wall.WardBoard:
                Quad(c, f, 0.03f, 0.3f, 0.97f, 0.95f, new Color("#9AA6B0"));
                Quad(c, f, 0.05f, 0.33f, 0.95f, 0.92f, Colors.White);
                for (var i = 0; i < 5; i++)
                {
                    var v = 0.4f + (i * 0.1f);
                    Line(c, f, 0.08f, v, 0.92f, v, new Color("#C9D3DC"), 0.7f);
                    Quad(c, f, 0.08f, v + 0.015f, 0.14f, v + 0.075f, new Color(new[] { "#E53935", "#F9A825", "#43A047", "#43A047", "#F9A825" }[i]));
                    Line(c, f, 0.18f, v + 0.045f, 0.18f + (0.12f * (1 + ((i * 7) % 4))), v + 0.045f, new Color("#1F5FBF"), 0.9f);
                }

                Quad(c, f, 0.05f, 0.86f, 0.95f, 0.92f, kind == Wall.WardBoard ? new Color("#1E88E5") : new Color("#2BB3A3"));
                break;

            case Wall.Lightbox:
                Quad(c, f, 0.05f, 0.35f, 0.95f, 0.95f, new Color("#B9C3CB"));
                Quad(c, f, 0.1f, 0.4f, 0.9f, 0.9f, new Color("#EAF4FB"));
                Quad(c, f, 0.2f, 0.45f, 0.8f, 0.86f, new Color("#1C2630"));
                // A forearm film: two long bones and the wrist.
                Line(c, f, 0.38f, 0.48f, 0.42f, 0.74f, new Color("#E8EEF2"), 1.6f);
                Line(c, f, 0.55f, 0.48f, 0.52f, 0.74f, new Color("#E8EEF2"), 1.6f);
                foreach (var u in new[] { 0.4f, 0.47f, 0.54f })
                {
                    c.DrawCircle(f(u, 0.79f), 1.3f, new Color("#DDE5EA"));
                }

                break;

            case Wall.Noticeboard:
                Quad(c, f, 0.03f, 0.35f, 0.97f, 0.95f, new Color("#7A5A3A"));
                Quad(c, f, 0.06f, 0.39f, 0.94f, 0.91f, new Color("#C69C6D"));
                foreach (var (u, v, colour) in new[] { (0.12f, 0.55f, "#FFF59D"), (0.3f, 0.7f, "#FFFFFF"), (0.5f, 0.5f, "#90CAF9"), (0.68f, 0.68f, "#F48FB1"), (0.8f, 0.48f, "#FFFFFF") })
                {
                    Quad(c, f, u, v, u + 0.13f, v + 0.17f, new Color(colour));
                    c.DrawCircle(f(u + 0.065f, v + 0.15f), 0.8f, red);
                }

                break;

            case Wall.EcgPoster:
                PicturePoster(c, f, "#AD1457", "HEART RHYTHM", Pictogram.Pulse, "#E53935");
                break;

            case Wall.HeartPoster:
                PicturePoster(c, f, "#AD1457", "YOUR HEART", Pictogram.Heart, "#D32F2F");
                break;

            case Wall.BrainPoster:
                PicturePoster(c, f, "#6A1B9A", "THE BRAIN", Pictogram.Brain, "#F4A6B7");
                break;

            case Wall.GutPoster:
                PicturePoster(c, f, "#455A64", "DIGESTION", Pictogram.Scope, "#E57373");
                break;

            case Wall.BonePoster:
                PicturePoster(c, f, "#8D6E63", "BONES", Pictogram.Bone, "#B9A98C");
                break;

            case Wall.PastelPrint:
                Quad(c, f, 0.08f, 0.32f, 0.92f, 0.94f, new Color("#E7DCCB"));
                Quad(c, f, 0.13f, 0.38f, 0.87f, 0.88f, new Color("#F3E5F5"));
                Quad(c, f, 0.13f, 0.38f, 0.87f, 0.55f, new Color("#C8E6C9"));
                foreach (var (u, colour) in new[] { (0.3f, "#F48FB1"), (0.5f, "#CE93D8"), (0.7f, "#FFCC80") })
                {
                    Line(c, f, u, 0.45f, u, 0.62f, new Color("#81C784"), 0.8f);
                    c.DrawCircle(f(u, 0.65f), 2f, new Color(colour));
                }

                break;

            case Wall.BpCuff:
                Quad(c, f, 0.2f, 0.45f, 0.8f, 0.85f, new Color("#ECEFF1"));
                c.DrawCircle(f(0.5f, 0.68f), 3.2f, Colors.White);
                c.DrawArc(f(0.5f, 0.68f), 3.2f, 0, Mathf.Tau, 16, dark, 0.8f, true);
                Line(c, f, 0.5f, 0.68f, 0.6f, 0.75f, red, 0.8f);
                c.DrawPolyline(new[] { f(0.3f, 0.45f), f(0.25f, 0.25f), f(0.45f, 0.2f) }, new Color("#1F5FBF"), 1.4f, true);
                break;

            case Wall.DiabetesPoster:
                PicturePoster(c, f, "#1E88E5", "DIABETES", Pictogram.Drop, "#E53935");
                break;

            case Wall.ClinicSign:
                Sign(c, f, 0.3f, 0.92f, (w, h) => Signs.Directions(c, w, h, "Outpatients", ("Clinics", 0, Pictogram.Clipboard), ("X-ray", 3, Pictogram.Bone), ("Pharmacy", 2, Pictogram.Pill)));
                break;

            case Wall.PharmacySign:
                Sign(c, f, 0.56f, 0.88f, (w, h) => Signs.Banner(c, w, h, new Color("#1E8E3E"), Pictogram.Cross, "PHARMACY", "Collect prescriptions here"));
                break;

            case Wall.WarningLight:
                Sign(c, f, 0.66f, 0.92f, (w, h) =>
                {
                    Flat.Round(c, w * 0.12f, 0, w * 0.76f, h, 2f, new Color("#2B3036"));
                    Flat.Round(c, (w * 0.12f) + 1.5f, 1.5f, (w * 0.76f) - 3, h - 3, 1.5f, new Color("#FF5A4F"));
                    Flat.Text(c, "X-RAY ON", w / 2, (h / 2) + 0.4f, 6, Colors.White);
                    Flat.Text(c, "Do not enter", w / 2, (h / 2) + 6f, 4, new Color(1, 1, 1, 0.9f), false);
                });
                break;

            case Wall.RadiationSign:
                Sign(c, f, 0.56f, 0.86f, (w, h) => Signs.Warning(c, w, h, Pictogram.Radiation, "X-RAYS", "Controlled area"));
                break;

            case Wall.ControlWindow:
                Quad(c, f, 0.03f, 0.35f, 0.97f, 0.97f, new Color("#9AA6B0"));
                Quad(c, f, 0.07f, 0.42f, 0.93f, 0.9f, new Color("#7FB3D5"));
                Line(c, f, 0.2f, 0.88f, 0.45f, 0.45f, new Color("#D6EAF8", 0.8f), 1.5f);
                Line(c, f, 0.35f, 0.88f, 0.55f, 0.5f, new Color("#D6EAF8", 0.6f), 1f);
                break;

            case Wall.ApronRack:
                Line(c, f, 0.05f, 0.92f, 0.95f, 0.92f, new Color("#7D8A95"), 1.4f);
                foreach (var (u, colour) in new[] { (0.12f, "#1F5FBF"), (0.42f, "#7E57C2"), (0.72f, "#2E7D32") })
                {
                    c.DrawColoredPolygon(new[] { f(u, 0.9f), f(u + 0.18f, 0.9f), f(u + 0.2f, 0.3f), f(u - 0.02f, 0.3f) }, new Color(colour));
                }

                break;

            case Wall.Mirror:
                Quad(c, f, 0.03f, 0.1f, 0.97f, 0.97f, new Color("#9AA6B0"));
                Quad(c, f, 0.06f, 0.14f, 0.94f, 0.93f, new Color("#D6E9F2"));
                Line(c, f, 0.15f, 0.2f, 0.4f, 0.85f, new Color("#FFFFFF", 0.7f), 2f);
                Line(c, f, 0.3f, 0.2f, 0.5f, 0.7f, new Color("#FFFFFF", 0.5f), 1f);
                break;

            case Wall.FireExtinguisher:
                Quad(c, f, 0.1f, 0.4f, 0.9f, 0.95f, new Color("#ECEFF1"));
                Quad(c, f, 0.32f, 0.12f, 0.68f, 0.68f, red);
                Quad(c, f, 0.32f, 0.5f, 0.68f, 0.57f, new Color("#FBFCFD"));
                Line(c, f, 0.5f, 0.68f, 0.5f, 0.78f, Colors.Black, 1.4f);
                Line(c, f, 0.5f, 0.76f, 0.75f, 0.6f, Colors.Black, 0.9f);
                break;
        }
    }

    private static void Heart(CanvasItem c, Vector2 centre, float size)
    {
        var colour = new Color("#D32F2F");
        c.DrawCircle(centre + new Vector2(-size * 0.45f, -size * 0.2f), size * 0.55f, colour);
        c.DrawCircle(centre + new Vector2(size * 0.45f, -size * 0.2f), size * 0.55f, colour);
        c.DrawColoredPolygon(new[] { centre + new Vector2(-size, -0.05f * size), centre + new Vector2(size, -0.05f * size), centre + new Vector2(0, size) }, colour);
    }

    private static void Brain(CanvasItem c, Vector2 centre, float size)
    {
        var colour = new Color("#F4A6B7");
        foreach (var (x, y) in new[] { (-0.45f, -0.15f), (0f, -0.35f), (0.45f, -0.15f), (-0.25f, 0.25f), (0.3f, 0.25f) })
        {
            c.DrawCircle(centre + new Vector2(x * size, y * size), size * 0.5f, colour);
        }

        c.DrawLine(centre + new Vector2(0, -size * 0.75f), centre + new Vector2(0, size * 0.5f), colour.Darkened(0.3f), 0.8f);
        c.DrawArc(centre + new Vector2(-size * 0.35f, 0), size * 0.3f, 0.5f, 2.6f, 6, colour.Darkened(0.3f), 0.7f, true);
        c.DrawArc(centre + new Vector2(size * 0.35f, 0), size * 0.3f, 0.5f, 2.6f, 6, colour.Darkened(0.3f), 0.7f, true);
    }

    // ------------------------------------------------------------------ floor props

    private static void Shadow(CanvasItem c, Vector2 at, float radius) =>
        c.DrawColoredPolygon(IsoDraw.FloorEllipse(at + new Vector2(1, 1), radius), new Color(0, 0, 0, 0.16f));

    /// <summary>A small box drawn about a floor point, (w, l) tiles in size.</summary>
    private static void Cube(CanvasItem c, double gx, double gy, double w, double l, float height, Color top, Color? face = null, float lift = 0, Color? rim = null) =>
        IsoDraw.Prism(c, gx - (w / 2), gy - (l / 2), w, l, height, top, face ?? top.Darkened(0.2f), lift: lift, rim: rim ?? top.Lightened(0.25f));

    private static void Pedestal(CanvasItem c, double gx, double gy) =>
        Cube(c, gx, gy, 0.6, 0.6, 22, new Color("#E7EBEE"), new Color("#C9D2DA"));

    private static void DrawFloor(CanvasItem c, Floor kind, double gx, double gy)
    {
        var p = IsoDraw.ToScreen(gx, gy);
        switch (kind)
        {
            case Floor.Plant:
            {
                Shadow(c, p, 9);
                c.DrawColoredPolygon(new[] { p + new Vector2(-6, -12), p + new Vector2(6, -12), p + new Vector2(4.5f, 0), p + new Vector2(-4.5f, 0) }, new Color("#B5653A"));
                c.DrawRect(new Rect2(p + new Vector2(-6.5f, -14), new Vector2(13, 3)), new Color("#C9774A"));
                var leaf = new Color("#3F8F4A");
                foreach (var (x, y, r) in new[] { (-6f, -22f, 6f), (6f, -23f, 6f), (0f, -30f, 7f), (-3f, -18f, 5f), (4f, -17f, 5f), (0f, -24f, 6f) })
                {
                    c.DrawCircle(p + new Vector2(x, y), r, leaf);
                    c.DrawCircle(p + new Vector2(x - 1.5f, y - 1.5f), r * 0.5f, leaf.Lightened(0.2f));
                }

                break;
            }

            case Floor.WaterCooler:
                Cube(c, gx, gy, 0.6, 0.6, 22, new Color("#E9EDF0"));
                c.DrawColoredPolygon(new[] { p + new Vector2(-5, -24), p + new Vector2(5, -24), p + new Vector2(6, -36), p + new Vector2(0, -40), p + new Vector2(-6, -36) }, new Color("#64B5F6", 0.85f));
                c.DrawLine(p + new Vector2(-3, -30), p + new Vector2(-3, -36), new Color("#E3F2FD", 0.9f), 1.5f);
                c.DrawRect(new Rect2(p + new Vector2(-2, -14), new Vector2(4, 2)), new Color("#1E88E5"));
                break;

            case Floor.LeafletRack:
                Cube(c, gx, gy, 0.7, 0.3, 30, new Color("#B9C3CB"));
                foreach (var (y, colour) in new[] { (-28f, "#43A047"), (-21f, "#1F5FBF"), (-14f, "#F9A825"), (-7f, "#E53935") })
                {
                    c.DrawRect(new Rect2(p + new Vector2(-8, y), new Vector2(7, 6)), new Color(colour));
                    c.DrawRect(new Rect2(p + new Vector2(1, y), new Vector2(7, 6)), Colors.White);
                }

                break;

            case Floor.Scales:
                Cube(c, gx, gy, 0.6, 0.6, 3, new Color("#ECEFF1"));
                c.DrawLine(p + new Vector2(0, -3), p + new Vector2(0, -30), new Color("#9AA6B0"), 2f);
                c.DrawRect(new Rect2(p + new Vector2(-5, -36), new Vector2(10, 7)), new Color("#37474F"));
                c.DrawRect(new Rect2(p + new Vector2(-3.5f, -35), new Vector2(7, 4)), new Color("#A5D6A7"));
                break;

            case Floor.SlingBox:
                Cube(c, gx, gy, 0.7, 0.5, 9, new Color("#D7B98E"));
                c.DrawCircle(p + new Vector2(-2, -12), 3, new Color("#5C8FC4"));
                c.DrawCircle(p + new Vector2(3, -11), 2.5f, Colors.White);
                break;

            case Floor.AirwayBag:
                Cube(c, gx, gy, 0.8, 0.5, 9, new Color("#D84B3E"), rim: new Color("#F28B82"));
                c.DrawLine(p + new Vector2(-6, -13), p + new Vector2(6, -13), new Color("#2B3036"), 1.5f);
                c.DrawRect(new Rect2(p + new Vector2(-2, -9), new Vector2(4, 4)), Colors.White);
                break;

            case Floor.Bookshelf:
                Cube(c, gx, gy, 0.9, 0.35, 44, new Color("#8C6D4F"), new Color("#6E543B"));
                for (var i = 0; i < 4; i++)
                {
                    for (var k = 0; k < 4; k++)
                    {
                        var colour = new[] { "#1F5FBF", "#E53935", "#43A047", "#F9A825", "#ECEFF1", "#7E57C2" }[(i + (k * 2)) % 6];
                        c.DrawRect(new Rect2(p + new Vector2(-10 + (k * 5), -42 + (i * 10)), new Vector2(3.5f, 8)), new Color(colour));
                    }
                }

                break;

            case Floor.Fridge:
                Cube(c, gx, gy, 0.7, 0.7, 42, new Color("#F2F4F6"), new Color("#D9DFE5"));
                c.DrawLine(p + new Vector2(-9, -26), p + new Vector2(9, -26), new Color("#AEB8C1"), 1f);
                c.DrawLine(p + new Vector2(6, -36), p + new Vector2(6, -30), new Color("#8D99A3"), 1.5f);
                c.DrawCircle(p + new Vector2(-4, -34), 1.5f, new Color("#F48FB1"));
                break;

            case Floor.Skeleton:
                Skeleton(c, p);
                break;

            case Floor.HeartModel:
                Pedestal(c, gx, gy);
                Heart(c, p + new Vector2(0, -31), 9);
                c.DrawLine(p + new Vector2(-2, -37), p + new Vector2(-4, -44), new Color("#1E88E5"), 2.6f);
                c.DrawLine(p + new Vector2(2, -37), p + new Vector2(5, -43), new Color("#D32F2F"), 2.6f);
                break;

            case Floor.BrainModel:
                Pedestal(c, gx, gy);
                Brain(c, p + new Vector2(0, -31), 9);
                break;

            case Floor.GutModel:
                Pedestal(c, gx, gy);
                c.DrawRect(new Rect2(p + new Vector2(-6, -40), new Vector2(12, 18)), new Color("#F5D3BC"));
                c.DrawPolyline(new[] { p + new Vector2(-3, -24), p + new Vector2(-4, -30), p + new Vector2(4, -30), p + new Vector2(4, -37), p + new Vector2(-4, -37), p + new Vector2(-4, -33), p + new Vector2(1, -33) }, new Color("#E57373"), 2f, true);
                c.DrawCircle(p + new Vector2(-2, -38), 2.2f, new Color("#A1887F"));
                break;

            case Floor.PelvisModel:
                Pedestal(c, gx, gy);
                c.DrawColoredPolygon(new[] { p + new Vector2(-8, -34), p + new Vector2(-3, -28), p + new Vector2(0, -24), p + new Vector2(3, -28), p + new Vector2(8, -34), p + new Vector2(6, -29), p + new Vector2(0, -22), p + new Vector2(-6, -29) }, new Color("#ECE7DA"));
                c.DrawArc(p + new Vector2(0, -28), 3, 0, Mathf.Tau, 10, new Color("#C9BFA8"), 1f, true);
                break;

            case Floor.PrivacyScreen:
            {
                Shadow(c, p, 12);
                for (var i = 0; i < 3; i++)
                {
                    var x = -13 + (i * 9);
                    var lean = i == 1 ? -2 : 0;
                    c.DrawRect(new Rect2(p + new Vector2(x, -36 + lean), new Vector2(8, 32)), new Color("#F3E5F5"));
                    c.DrawRect(new Rect2(p + new Vector2(x, -36 + lean), new Vector2(8, 3)), new Color("#9C8AD0"));
                    c.DrawLine(p + new Vector2(x, -36 + lean), p + new Vector2(x, -1), new Color("#9AA6B0"), 1f);
                }

                break;
            }

            case Floor.Kiosk:
                Cube(c, gx, gy, 0.5, 0.4, 26, new Color("#E7EBEE"), new Color("#C9D2DA"));
                c.DrawColoredPolygon(new[] { p + new Vector2(-7, -40), p + new Vector2(7, -40), p + new Vector2(7, -27), p + new Vector2(-7, -27) }, new Color("#1B2228"));
                c.DrawRect(new Rect2(p + new Vector2(-5.5f, -38.5f), new Vector2(11, 10)), new Color("#1F5FBF"));
                c.DrawRect(new Rect2(p + new Vector2(-4, -33), new Vector2(8, 2.5f)), Colors.White);
                break;

            case Floor.FloorLamp:
                Shadow(c, p, 6);
                c.DrawLine(p, p + new Vector2(0, -44), new Color("#56616B"), 1.6f);
                c.DrawColoredPolygon(IsoDraw.FloorEllipse(p, 7), new Color("#FFE9B0", 0.25f));
                c.DrawColoredPolygon(new[] { p + new Vector2(-5, -42), p + new Vector2(5, -42), p + new Vector2(3, -50), p + new Vector2(-3, -50) }, new Color("#FFE0A0"));
                break;

            case Floor.MannequinHead:
                Pedestal(c, gx, gy);
                c.DrawCircle(p + new Vector2(0, -30), 7, new Color("#E9DFD4"));
                c.DrawArc(p + new Vector2(0, -31), 7, Mathf.Pi, Mathf.Tau, 12, new Color("#F5F5F5"), 3f, true);
                foreach (var (x, y) in new[] { (-5f, -33f), (-2f, -37f), (2f, -37f), (5f, -33f), (0f, -34f) })
                {
                    c.DrawCircle(p + new Vector2(x, y), 1.1f, new Color("#E53935"));
                }

                c.DrawLine(p + new Vector2(5, -30), p + new Vector2(11, -18), new Color("#455A64"), 1f);
                break;

            case Floor.ScopeCabinet:
                Cube(c, gx, gy, 0.8, 0.4, 46, new Color("#E9EDF0"), new Color("#CDD5DC"));
                c.DrawRect(new Rect2(p + new Vector2(-9, -44), new Vector2(18, 38)), new Color("#BFE0F0", 0.6f));
                foreach (var x in new[] { -5f, 0f, 5f })
                {
                    c.DrawLine(p + new Vector2(x, -42), p + new Vector2(x, -10), new Color("#263238"), 1.6f);
                    c.DrawCircle(p + new Vector2(x, -42), 1.6f, new Color("#455A64"));
                }

                break;

            case Floor.BlanketStack:
                Cube(c, gx, gy, 0.7, 0.5, 14, new Color("#D9CBB5"));
                Cube(c, gx, gy, 0.6, 0.45, 4, new Color("#BFD3E6"), lift: 14);
                Cube(c, gx, gy, 0.6, 0.45, 4, new Color("#F2B8C6"), lift: 18);
                Cube(c, gx, gy, 0.6, 0.45, 4, new Color("#BFD3E6"), lift: 22);
                break;

            case Floor.ScrubSink:
                Cube(c, gx, gy, 0.9, 0.5, 24, new Color("#DDE5EA"), new Color("#B9C6D0"));
                c.DrawColoredPolygon(new[] { IsoDraw.ToScreen(gx - 0.3, gy - 0.15) + new Vector2(0, -24), IsoDraw.ToScreen(gx + 0.3, gy - 0.15) + new Vector2(0, -24), IsoDraw.ToScreen(gx + 0.3, gy + 0.15) + new Vector2(0, -24), IsoDraw.ToScreen(gx - 0.3, gy + 0.15) + new Vector2(0, -24) }, new Color("#A9BBC8"));
                foreach (var x in new[] { -5f, 5f })
                {
                    c.DrawLine(p + new Vector2(x, -26), p + new Vector2(x, -34), new Color("#9AA6B0"), 1.6f);
                    c.DrawLine(p + new Vector2(x, -34), p + new Vector2(x + 3, -32), new Color("#9AA6B0"), 1.4f);
                }

                break;

            case Floor.PlasterShelf:
                Cube(c, gx, gy, 0.9, 0.4, 32, new Color("#C9CFD4"), new Color("#AAB3BA"));
                for (var i = 0; i < 3; i++)
                {
                    for (var k = 0; k < 3; k++)
                    {
                        c.DrawCircle(p + new Vector2(-7 + (k * 7), -27 + (i * 9)), 2.8f, i == 1 ? new Color("#90CAF9") : Colors.White);
                    }
                }

                break;

            case Floor.Bucket:
                Shadow(c, p, 6);
                c.DrawColoredPolygon(new[] { p + new Vector2(-5, -11), p + new Vector2(5, -11), p + new Vector2(4, 0), p + new Vector2(-4, 0) }, new Color("#90A4AE"));
                c.DrawColoredPolygon(IsoDraw.FloorEllipse(p + new Vector2(0, -11), 5, 12), new Color("#D6E4EC"));
                c.DrawArc(p + new Vector2(0, -12), 5, Mathf.Pi, Mathf.Tau, 8, new Color("#607D8B"), 1f, true);
                break;

            case Floor.GymBall:
                Shadow(c, p, 9);
                c.DrawCircle(p + new Vector2(0, -9), 9, new Color("#1E88E5"));
                c.DrawCircle(p + new Vector2(-3, -12), 3, new Color("#64B5F6"));
                break;

            case Floor.Dumbbells:
                Cube(c, gx, gy, 0.9, 0.4, 10, new Color("#56616B"));
                foreach (var (x, colour) in new[] { (-6f, "#E53935"), (0f, "#1F5FBF"), (6f, "#F9A825") })
                {
                    c.DrawLine(p + new Vector2(x - 2.5f, -12), p + new Vector2(x + 2.5f, -12), new Color("#B9C3CB"), 1.2f);
                    c.DrawCircle(p + new Vector2(x - 2.5f, -12), 1.8f, new Color(colour));
                    c.DrawCircle(p + new Vector2(x + 2.5f, -12), 1.8f, new Color(colour));
                }

                break;

            case Floor.Bins:
                // A pair of pedal bins: general waste (black lid) and recycling (green lid).
                foreach (var (dx, lid, badge) in new[] { (-0.2, "#2B3036", "#9AA6B0"), (0.2, "#2E7D32", "#FFFFFF") })
                {
                    var q = IsoDraw.ToScreen(gx + dx, gy - dx);
                    Shadow(c, q, 6);
                    c.DrawColoredPolygon(new[] { q + new Vector2(-4.5f, 0), q + new Vector2(4.5f, 0), q + new Vector2(5.2f, -13), q + new Vector2(-5.2f, -13) }, new Color("#E4E9ED"));
                    c.DrawColoredPolygon(new[] { q + new Vector2(1.5f, 0), q + new Vector2(4.5f, 0), q + new Vector2(5.2f, -13), q + new Vector2(2f, -13) }, new Color("#CBD4DB"));
                    c.DrawColoredPolygon(IsoDraw.FloorEllipse(q + new Vector2(0, -13.5f), 5.6f, 12), new Color(lid));
                    c.DrawRect(new Rect2(q + new Vector2(-2.2f, -9.5f), new Vector2(4.4f, 4.4f)), new Color(lid));
                    c.DrawCircle(q + new Vector2(0, -7.3f), 1.3f, new Color(badge));
                    c.DrawRect(new Rect2(q + new Vector2(-2, -1.2f), new Vector2(4, 1.2f)), new Color("#55616B"));
                }

                break;

            case Floor.OxygenSpare:
                foreach (var dx in new[] { -0.15, 0.15 })
                {
                    var q = IsoDraw.ToScreen(gx + dx, gy - dx);
                    Shadow(c, q, 5);
                    c.DrawRect(new Rect2(q + new Vector2(-4, -30), new Vector2(8, 30)), new Color("#F2F4F5"));
                    c.DrawRect(new Rect2(q + new Vector2(-4, -30), new Vector2(8, 5)), Colors.White);
                    c.DrawRect(new Rect2(q + new Vector2(-1.5f, -33), new Vector2(3, 3)), new Color("#2C2F33"));
                }

                break;
        }
    }

    private static void Skeleton(CanvasItem c, Vector2 p)
    {
        var bone = new Color("#EFEADF");
        var joint = new Color("#D8D0BE");
        var stand = new Color("#6E7A84");
        Shadow(c, p, 9);
        c.DrawLine(p + new Vector2(-7, 0), p + new Vector2(7, 0), stand, 2f);
        c.DrawLine(p + new Vector2(0, 0), p + new Vector2(0, -60), stand, 1.4f);

        // Legs, pelvis, spine and ribs, arms, skull.
        foreach (var s in new[] { -1f, 1f })
        {
            c.DrawLine(p + new Vector2(s * 3, -26), p + new Vector2(s * 3.5f, -14), bone, 1.8f);
            c.DrawLine(p + new Vector2(s * 3.5f, -14), p + new Vector2(s * 3.5f, -3), bone, 1.6f);
            c.DrawCircle(p + new Vector2(s * 3.5f, -14), 1.3f, joint);
            c.DrawLine(p + new Vector2(s * 3.5f, -3), p + new Vector2(s * 5.5f, -2), bone, 1.5f);
            c.DrawLine(p + new Vector2(s * 7, -44), p + new Vector2(s * 8.5f, -34), bone, 1.5f);
            c.DrawLine(p + new Vector2(s * 8.5f, -34), p + new Vector2(s * 9, -25), bone, 1.4f);
            c.DrawCircle(p + new Vector2(s * 8.5f, -34), 1.2f, joint);
        }

        c.DrawColoredPolygon(new[] { p + new Vector2(-6, -30), p + new Vector2(6, -30), p + new Vector2(4, -25), p + new Vector2(0, -27), p + new Vector2(-4, -25) }, bone);
        c.DrawLine(p + new Vector2(0, -30), p + new Vector2(0, -46), bone, 1.4f);
        for (var i = 0; i < 4; i++)
        {
            var y = -43 + (i * 3);
            var w = 5.5f - (i * 0.6f);
            c.DrawArc(p + new Vector2(0, y + 2), w, Mathf.Pi * 1.05f, Mathf.Tau * 0.97f, 8, bone, 1.1f, true);
        }

        c.DrawLine(p + new Vector2(-7, -45), p + new Vector2(7, -45), bone, 1.6f);
        c.DrawCircle(p + new Vector2(0, -51), 5, bone);
        c.DrawRect(new Rect2(p + new Vector2(-3, -49), new Vector2(6, 4)), bone);
        c.DrawCircle(p + new Vector2(-1.8f, -51), 1.2f, new Color("#3A3A3A"));
        c.DrawCircle(p + new Vector2(1.8f, -51), 1.2f, new Color("#3A3A3A"));
        c.DrawLine(p + new Vector2(-2, -46.5f), p + new Vector2(2, -46.5f), new Color("#8A8170"), 0.8f);
    }

    // ------------------------------------------------------------------ on desks and counters

    private static void DrawTop(CanvasItem c, OnTop kind, Vector2 at)
    {
        switch (kind)
        {
            case OnTop.Computer:
                c.DrawRect(new Rect2(at + new Vector2(-1.5f, -6), new Vector2(3, 6)), new Color("#3A4148"));
                c.DrawRect(new Rect2(at + new Vector2(-9, -18), new Vector2(18, 12)), new Color("#2B3036"));
                c.DrawRect(new Rect2(at + new Vector2(-8, -17), new Vector2(16, 10)), new Color("#2F6FB5"));
                c.DrawRect(new Rect2(at + new Vector2(-8, -17), new Vector2(16, 2.5f)), new Color("#1F5FBF"));
                c.DrawLine(at + new Vector2(-6, -11), at + new Vector2(3, -11), new Color("#D6E9F8"), 1f);
                c.DrawRect(new Rect2(at + new Vector2(-6, 1), new Vector2(12, 3)), new Color("#E3E7EA"));
                break;

            case OnTop.Phone:
                c.DrawRect(new Rect2(at + new Vector2(-4, -3), new Vector2(8, 4)), new Color("#2B3036"));
                c.DrawLine(at + new Vector2(-4, -4), at + new Vector2(4, -4), new Color("#3A4148"), 2.2f);
                break;

            case OnTop.Folders:
                foreach (var (i, colour) in new[] { (0, "#E53935"), (1, "#1F5FBF"), (2, "#F9A825") })
                {
                    c.DrawRect(new Rect2(at + new Vector2(-6 + (i * 0.6f), -2 - (i * 2)), new Vector2(11, 2)), new Color(colour));
                }

                break;

            case OnTop.KettleMugs:
                c.DrawRect(new Rect2(at + new Vector2(-9, -9), new Vector2(7, 9)), new Color("#ECEFF1"));
                c.DrawRect(new Rect2(at + new Vector2(-9, -10), new Vector2(7, 2)), new Color("#2B3036"));
                c.DrawLine(at + new Vector2(-2, -7), at + new Vector2(0, -6), new Color("#ECEFF1"), 1.5f);
                foreach (var (x, colour) in new[] { (3f, "#E53935"), (8f, "#1F5FBF") })
                {
                    c.DrawRect(new Rect2(at + new Vector2(x, -5), new Vector2(4, 5)), new Color(colour));
                }

                break;

            case OnTop.ReflexHammer:
                c.DrawLine(at + new Vector2(-6, -1), at + new Vector2(3, -3), new Color("#9AA6B0"), 1.2f);
                c.DrawColoredPolygon(new[] { at + new Vector2(2, -6), at + new Vector2(6, -4), at + new Vector2(4, 0), at + new Vector2(0, -2) }, new Color("#E53935"));
                break;

            case OnTop.PillTray:
                c.DrawRect(new Rect2(at + new Vector2(-6, -2), new Vector2(12, 3)), new Color("#ECEFF1"));
                foreach (var x in new[] { -3f, 0f, 2f })
                {
                    c.DrawCircle(at + new Vector2(x, -2), 1f, new Color("#FFCC80"));
                }

                c.DrawRect(new Rect2(at + new Vector2(5, -8), new Vector2(4, 7)), new Color("#FFFFFF"));
                c.DrawRect(new Rect2(at + new Vector2(5, -8), new Vector2(4, 2)), new Color("#E53935"));
                break;

            case OnTop.GelWarmer:
                c.DrawRect(new Rect2(at + new Vector2(-3, -9), new Vector2(6, 9)), new Color("#81D4FA"));
                c.DrawRect(new Rect2(at + new Vector2(-1, -11), new Vector2(2, 2)), Colors.White);
                break;
        }
    }

    // ------------------------------------------------------------------ hanging over the table

    private static void SurgicalLights(CanvasItem c, double cx, double cy)
    {
        var centre = IsoDraw.ToScreen(cx, cy) + new Vector2(0, -72);
        c.DrawLine(centre + new Vector2(0, -60), centre + new Vector2(0, -16), new Color("#9AA6B0"), 2.5f);
        foreach (var (dx, dy) in new[] { (-16f, 4f), (14f, -2f) })
        {
            var lamp = centre + new Vector2(dx, dy);
            c.DrawLine(centre + new Vector2(0, -16), lamp + new Vector2(0, -6), new Color("#9AA6B0"), 2f);
            c.DrawColoredPolygon(IsoDraw.FloorEllipse(lamp, 11, 18), new Color("#D7DEE3"));
            c.DrawColoredPolygon(IsoDraw.FloorEllipse(lamp + new Vector2(0, 2), 8, 16), new Color("#FFF8D6"));
            c.DrawColoredPolygon(IsoDraw.FloorEllipse(IsoDraw.ToScreen(cx, cy) + new Vector2(dx * 0.4f, -18), 16, 18), new Color(1, 0.98f, 0.85f, 0.12f));
        }
    }

    private static void MonitorBank(CanvasItem c, double gx, double gy)
    {
        var centre = IsoDraw.ToScreen(gx, gy) + new Vector2(0, -70);
        c.DrawLine(centre + new Vector2(0, -50), centre + new Vector2(0, -12), new Color("#9AA6B0"), 2.5f);
        for (var i = 0; i < 4; i++)
        {
            var r = new Rect2(centre + new Vector2(-17 + ((i % 2) * 17), -12 + ((i / 2) * 11)), new Vector2(16, 10));
            c.DrawRect(r, new Color("#1B2228"));
            c.DrawRect(r.Grow(-1.2f), new Color(i == 0 ? "#26323C" : "#0E1A22"));
            if (i == 0)
            {
                c.DrawCircle(r.GetCenter(), 3, new Color("#9FB0BE"));
            }
            else
            {
                c.DrawLine(r.Position + new Vector2(2, 6), r.Position + new Vector2(14, 5), new Color(i == 1 ? "#3EE07A" : "#4FC3F7"), 0.8f);
            }
        }
    }
}
