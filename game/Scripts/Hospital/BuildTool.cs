using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Hospital;

public enum BuildMode
{
    None,
    Room,
    Corridor,
    Extend,

    /// <summary>Placing waiting chairs: click for one, drag for a row. Z turns them.</summary>
    Chair,

    /// <summary>Placing (or moving) a waiting room's reception desk. Z turns it.</summary>
    Desk,

    /// <summary>Putting doors in a waiting room's walls, or clicking one to take it out.</summary>
    Door,

    /// <summary>Resus trolleys: click one to pick it up and click again to put it down; click empty floor to buy one. X sells a bought one.</summary>
    Trolley,
}

/// <summary>
/// Places rooms, corridors and building extensions, all by dragging from corner to corner.
/// While dragging, a see-through preview is green with the price when it can be built and red
/// with the reason when it can't. A room shows the furniture that will fill it and where its door
/// will go: on the wall that touches a corridor. Releasing the mouse builds it.
/// </summary>
public partial class BuildTool : Node2D
{
    private static readonly Color Good = new("#2EBD6B");
    private static readonly Color Bad = new("#E53935");

    private HospitalSimulation _sim = null!;
    private IsoProjection _projection = null!;
    private GridCoord _hover;

    /// <summary>The tile under the cursor.</summary>
    public GridCoord Hover => _hover;
    private GridCoord? _dragStart;
    private GridRect _rect;
    private string _planKey = string.Empty;

    public BuildMode Mode { get; private set; }

    public RoomTemplate? Template { get; private set; }

    /// <summary>The room being moved, if this is a move rather than a new room.</summary>
    public string? MovingId { get; private set; }


    /// <summary>The latest check of what is under the cursor.</summary>
    public BuildCheck? Check { get; private set; }

    /// <summary>Colour for new chairs.</summary>
    public string ChairColour { get; set; } = ChairColours.Default;

    /// <summary>The way the player has turned chairs with Z; null lets each chair face away from the nearest wall.</summary>
    public WallSide? ChairFacing { get; private set; }

    /// <summary>The wall the player has put a room's door on with Z; null lets the game pick the wall with the most corridor beside it.</summary>
    public WallSide? DoorChoice { get; private set; }

    /// <summary>The wall the door of the room being dragged out goes on, as things stand.</summary>
    public WallSide? PlannedDoorSide => Mode == BuildMode.Room && Template is { } t && _dragStart is not null ? _sim.AutoPlan(t.Id, _rect, MovingId, DoorChoice).DoorSide : null;

    /// <summary>The side of the reception desk patients stand on.</summary>
    public WallSide DeskFacing { get; private set; } = WallSide.South;

    /// <summary>The waiting room under the cursor (for the desk and door tools).</summary>
    public CareSpace? TargetRoom { get; private set; }

    /// <summary>The resus trolley picked up to move, if any.</summary>
    public ResusTrolley? CarriedTrolley { get; private set; }

    /// <summary>The resus trolley under the cursor, if any.</summary>
    public ResusTrolley? HoveredTrolley => _sim.ResusTrolleys.FirstOrDefault(t => t.Home == _hover || t.Tile == _hover);

    /// <summary>True when a click on the door tool would take an existing door out.</summary>
    public bool RemovingDoor { get; private set; }

    public bool Active => Mode != BuildMode.None;

    /// <summary>False while a test or tutorial drives the preview with <see cref="PreviewAt"/>.</summary>
    public bool FollowMouse { get; set; } = true;

    /// <summary>Drags the preview from one tile to another without the mouse (for the smoke test).</summary>
    public void PreviewDrag(GridCoord from, GridCoord to)
    {
        FollowMouse = false;
        _dragStart = from;
        _hover = to;
        Refresh(force: true);
    }

    /// <summary>Builds whatever is being previewed, if it can be built.</summary>
    public void Confirm() => Place();

    /// <summary>Raised when the check changes, so the build panel can show the cost and any problems.</summary>
    public event Action? CheckChanged;

    /// <summary>Raised after something is built, with a short description.</summary>
    public event Action<string>? Built;

    /// <summary>Raised when the tool stops (cancelled or a move finished).</summary>
    public event Action? Stopped;

    public void Initialise(HospitalSimulation simulation, IsoProjection projection)
    {
        _sim = simulation;
        _projection = projection;
        ZIndex = 5;
    }

    /// <summary>Starts dragging out a room; with <paramref name="movingId"/>, the drag rebuilds that room at a new place or size.</summary>
    public void StartRoom(RoomTemplate template, string? movingId = null)
    {
        Mode = BuildMode.Room;
        Template = template;
        MovingId = movingId;

        // Moving or resizing keeps the door on the same wall unless the player turns it.
        DoorChoice = movingId is not null ? _sim.FindSpace(movingId)?.DoorSide : null;
        Restart();
    }

    public void StartCorridor()
    {
        Mode = BuildMode.Corridor;
        Template = _sim.Database.RoomTemplates.Values.First(t => t.IsCorridor);
        MovingId = null;
        Restart();
    }

    /// <summary>Starts placing waiting chairs: each click places one, a drag places a row.</summary>
    public void StartChairs(string? colour = null)
    {
        Mode = BuildMode.Chair;
        Template = null;
        MovingId = null;
        ChairColour = colour ?? ChairColour;
        ChairFacing = null;
        Restart();
    }

    /// <summary>Starts moving and buying resus trolleys.</summary>
    public void StartTrolleys()
    {
        Mode = BuildMode.Trolley;
        Template = null;
        MovingId = null;
        CarriedTrolley = null;
        Restart();
    }

    /// <summary>Starts placing a waiting room's reception desk.</summary>
    public void StartDesk()
    {
        Mode = BuildMode.Desk;
        Template = null;
        MovingId = null;
        Restart();
    }

    /// <summary>Starts putting doors in waiting room walls.</summary>
    public void StartDoors()
    {
        Mode = BuildMode.Door;
        Template = null;
        MovingId = null;
        Restart();
    }

    /// <summary>Turns the chairs or desk being placed a quarter turn, or moves a room's door to its next wall (the Z key).</summary>
    public void Turn()
    {
        switch (Mode)
        {
            case BuildMode.Chair:
                ChairFacing = WaitingRoomLayout.Clockwise(ChairFacing ?? _sim.SuggestChairFacing(_hover));
                break;
            case BuildMode.Desk:
                DeskFacing = WaitingRoomLayout.Clockwise(DeskFacing);
                break;
            case BuildMode.Room when Template is { } room:
                // Step the door round to the next wall with a corridor beside it; the furniture turns with it.
                var current = PlannedDoorSide ?? DoorChoice ?? WallSide.South;
                DoorChoice = _dragStart is null ? WaitingRoomLayout.Clockwise(current) : _sim.NextDoorSide(room.Id, _rect, current, MovingId) ?? WaitingRoomLayout.Clockwise(current);
                break;
            default:
                return;
        }

        Refresh(force: true);
    }

    /// <summary>Makes new chairs face one way (for the smoke test and the art shots).</summary>
    public void FaceChairs(WallSide facing)
    {
        ChairFacing = facing;
        Refresh(force: true);
    }

    /// <summary>Lets chairs face away from the nearest wall again.</summary>
    public void AutoFacing()
    {
        ChairFacing = null;
        Refresh(force: true);
    }

    /// <summary>Points the preview at a tile without the mouse (for the smoke test).</summary>
    public void PreviewAt(GridCoord tile)
    {
        FollowMouse = false;
        _dragStart = null;
        _hover = tile;
        Refresh(force: true);
    }

    /// <summary>The way a chair placed at <paramref name="tile"/> now would face.</summary>
    public WallSide FacingFor(GridCoord tile) => ChairFacing ?? _sim.SuggestChairFacing(tile);

    /// <summary>The tiles a chair drag covers: a straight row along whichever way the drag went furthest.</summary>
    private List<GridCoord> ChairRow()
    {
        var start = _dragStart ?? _hover;
        var (dx, dy) = (_hover.X - start.X, _hover.Y - start.Y);
        var row = new List<GridCoord>();
        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            for (var i = 0; i <= Math.Abs(dx); i++)
            {
                row.Add(new GridCoord(start.X + (i * Math.Sign(dx)), start.Y));
            }
        }
        else
        {
            for (var i = 0; i <= Math.Abs(dy); i++)
            {
                row.Add(new GridCoord(start.X, start.Y + (i * Math.Sign(dy))));
            }
        }

        return row;
    }

    /// <summary>A row of chairs all face the same way: the way the first one would.</summary>
    private WallSide RowFacing(List<GridCoord> row)
    {
        if (ChairFacing is { } chosen)
        {
            return chosen;
        }

        // Along a row the chairs face across it, away from the nearest wall.
        var facing = _sim.SuggestChairFacing(row[0]);
        if (row.Count > 1)
        {
            var alongX = row[0].Y == row[^1].Y;
            if (alongX && facing is WallSide.East or WallSide.West || !alongX && facing is WallSide.North or WallSide.South)
            {
                facing = alongX ? WallSide.South : WallSide.East;
            }
        }

        return facing;
    }

    /// <summary>The waiting room whose floor (or the wall around it) is at <paramref name="tile"/>.</summary>
    private CareSpace? WaitingRoomAt(GridCoord tile, bool includeOutside)
    {
        var rooms = _sim.SpacesOfKind(CareSpaceKind.Waiting).ToList();
        var room = rooms.FirstOrDefault(r => r.Rect.Contains(tile));
        if (room is not null || !includeOutside)
        {
            return room;
        }

        return rooms.FirstOrDefault(r => new GridRect(r.Rect.X - 1, r.Rect.Y - 1, r.Rect.Width + 2, r.Rect.Length + 2).Contains(tile));
    }

    /// <summary>The wall tile inside the room nearest to <paramref name="tile"/> (which may be just outside it).</summary>
    private static GridCoord Inside(GridRect r, GridCoord tile) =>
        new(Math.Clamp(tile.X, r.X, r.Right - 1), Math.Clamp(tile.Y, r.Y, r.Bottom - 1));

    public void StartExtension()
    {
        Mode = BuildMode.Extend;
        Template = null;
        MovingId = null;
        Restart();
    }

    public void Cancel()
    {
        if (!Active)
        {
            return;
        }

        Mode = BuildMode.None;
        MovingId = null;
        CarriedTrolley = null;
        _dragStart = null;
        Check = null;
        QueueRedraw();
        CheckChanged?.Invoke();
        Stopped?.Invoke();
    }

    private void Restart()
    {
        _dragStart = null;
        _planKey = string.Empty;
        Refresh(force: true);
    }

    /// <summary>Handles mouse and keys while the tool is active. Returns true if the event was used.</summary>
    public bool HandleInput(InputEvent @event)
    {
        if (!Active)
        {
            return false;
        }

        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } when Mode == BuildMode.Trolley && CarriedTrolley is not null:
                CarriedTrolley = null;
                Refresh(force: true);
                return true;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } when Mode == BuildMode.Trolley:
                ClickTrolley();
                return true;

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.X } when Mode == BuildMode.Trolley && HoveredTrolley is { HomeRoomId: null } sold:
                if (_sim.SellResusTrolley(sold.Id))
                {
                    Built?.Invoke($"Sold a resus trolley: £{_sim.ResusTrolleyCost / 2:N0} back.");
                }

                Refresh(force: true);
                return true;

            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                if (_dragStart is not null)
                {
                    _dragStart = null;
                    Refresh(force: true);
                }
                else
                {
                    Cancel();
                }

                return true;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } when Mode is BuildMode.Desk or BuildMode.Door:
                Refresh(force: true);
                Place();
                return true;

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Z } when Mode is BuildMode.Chair or BuildMode.Desk or BuildMode.Room:
                Turn();
                return true;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                _dragStart = _hover;
                Refresh(force: true);
                return true;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } when _dragStart is not null:
                Refresh(force: true);
                Place();
                _dragStart = null;
                Refresh(force: true);
                return true;

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }:
                Cancel();
                return true;
        }

        return false;
    }

    /// <summary>Picks up the trolley under the cursor, or puts the carried one down here, or buys one here.</summary>
    private void ClickTrolley()
    {
        Refresh(force: true);
        if (CarriedTrolley is null && HoveredTrolley is { } picked)
        {
            CarriedTrolley = picked;
            Refresh(force: true);
            return;
        }

        if (Check is not { CanBuild: true })
        {
            return;
        }

        if (CarriedTrolley is { } carried)
        {
            if (_sim.MoveResusTrolley(carried.Id, _hover).CanBuild)
            {
                Built?.Invoke("Moved the resus trolley.");
            }

            CarriedTrolley = null;
        }
        else if (_sim.BuyResusTrolley(_hover) is { CanBuild: true } bought)
        {
            Built?.Invoke($"Placed a resus trolley for £{bought.Cost:N0}.");
        }

        Refresh(force: true);
    }

    private void Place()
    {
        if (Check is not { CanBuild: true })
        {
            return;
        }

        BuildCheck result;
        string what;
        switch (Mode)
        {
            case BuildMode.Room when MovingId is { } id:
                result = _sim.MoveRoom(id, _sim.AutoPlan(Template!.Id, _rect, id, DoorChoice));
                what = $"Moved {_sim.FindSpace(id)?.Name}";
                break;
            case BuildMode.Chair:
            {
                var row = ChairRow();
                var facing = RowFacing(row);
                var placed = 0;
                var cost = 0;
                foreach (var tile in row)
                {
                    var one = _sim.PlaceChair(tile, facing, ChairColour);
                    if (one.CanBuild)
                    {
                        placed++;
                        cost += one.Cost;
                    }
                }

                result = new BuildCheck(cost, placed > 0 ? Array.Empty<string>() : new[] { "Nothing placed." });
                what = cost == 0 ? $"Turned and recoloured {placed} chair{(placed == 1 ? string.Empty : "s")}" : $"Placed {placed} chair{(placed == 1 ? string.Empty : "s")}";
                break;
            }

            case BuildMode.Desk when TargetRoom is { } deskRoom:
                result = _sim.PlaceReceptionDesk(deskRoom.Id, _hover, DeskFacing);
                what = $"Placed the reception desk in {deskRoom.Name}";
                break;

            case BuildMode.Door when TargetRoom is { } doorRoom && RemovingDoor:
                result = _sim.RemoveWaitingDoor(doorRoom.Id, Inside(doorRoom.Rect, _hover));
                what = $"Took a door out of {doorRoom.Name}";
                break;

            case BuildMode.Door when TargetRoom is { } doorRoom:
            {
                var tile = Inside(doorRoom.Rect, _hover);
                var entrance = _sim.DoorAt(doorRoom, tile) is { } door && _sim.IsEntranceDoor(doorRoom, door);
                result = _sim.PlaceWaitingDoor(doorRoom.Id, tile);
                what = entrance ? $"Put the entrance doors into {doorRoom.Name}" : $"Put doors through to the corridor in {doorRoom.Name}";
                break;
            }

            case BuildMode.Desk or BuildMode.Door:
                return;
            case BuildMode.Room:
            case BuildMode.Corridor:
                result = _sim.BuildRoom(_sim.AutoPlan(Template!.Id, _rect, doorSide: Mode == BuildMode.Room ? DoorChoice : null));
                what = $"Built {Template.Name.ToLowerInvariant()}";
                break;
            default:
                result = _sim.ExtendBuilding(_sim.Site.Local(_rect));
                what = "Extended the building";
                break;
        }

        if (!result.CanBuild)
        {
            return;
        }

        Built?.Invoke(result.Cost < 0 ? $"{what}: £{-result.Cost:N0} refunded." : result.Cost == 0 ? $"{what}." : $"{what} for £{result.Cost:N0}.");
        if (MovingId is not null)
        {
            Cancel();
            return;
        }

        Refresh(force: true);
    }

    public override void _Process(double delta)
    {
        if (!Active || !FollowMouse)
        {
            return;
        }

        var mouse = GetGlobalMousePosition();
        var tile = IsoDraw.ScreenToTile(mouse);
        if (tile != _hover)
        {
            _hover = tile;
            Refresh(force: false);
        }
    }

    /// <summary>Works out the footprint under the cursor and checks it, if anything changed.</summary>
    private void Refresh(bool force)
    {
        if (!Active)
        {
            return;
        }

        _rect = DragRect();

        var key = $"{Mode}|{_rect}|{_sim.Balance}|{_dragStart}|{ChairFacing}|{ChairColour}|{DeskFacing}|{DoorChoice}|{CarriedTrolley?.Id}|{_sim.ResusTrolleys.Count}";
        if (!force && key == _planKey)
        {
            return;
        }

        _planKey = key;
        TargetRoom = Mode switch
        {
            BuildMode.Desk => WaitingRoomAt(_hover, includeOutside: false),
            BuildMode.Door => WaitingRoomAt(_hover, includeOutside: true),
            _ => null,
        };
        RemovingDoor = Mode == BuildMode.Door && TargetRoom is { } wall && _sim.ExistingDoorAt(wall, Inside(wall.Rect, _hover)) is not null;
        Check = Mode switch
        {
            BuildMode.Chair => CheckChairs(),
            BuildMode.Trolley when CarriedTrolley is null && HoveredTrolley is not null => new BuildCheck(0, Array.Empty<string>()),
            BuildMode.Trolley => _sim.CheckResusTrolley(_hover, CarriedTrolley),
            BuildMode.Desk when TargetRoom is null => new BuildCheck(0, new[] { "Click inside a waiting room to place its reception desk." }),
            BuildMode.Desk => _sim.CheckReceptionDesk(TargetRoom.Id, _hover, DeskFacing),
            BuildMode.Door when TargetRoom is null => new BuildCheck(0, new[] { "Click along a waiting room's wall to put a door in." }),
            BuildMode.Door when RemovingDoor => _sim.CheckRemoveWaitingDoor(TargetRoom.Id, Inside(TargetRoom.Rect, _hover)),
            BuildMode.Door => _sim.CheckWaitingDoor(TargetRoom.Id, Inside(TargetRoom.Rect, _hover)),
            BuildMode.Room when _dragStart is null => new BuildCheck(0, new[] { $"Drag out the {Template!.Name.ToLowerInvariant()} from corner to corner." }),
            BuildMode.Room => _sim.CheckRoomAt(Template!.Id, _rect, MovingId, DoorChoice),
            BuildMode.Corridor when _dragStart is null => new BuildCheck(0, new[] { "Drag out the corridor from corner to corner. It can cross other corridors." }),
            BuildMode.Corridor => _sim.CheckRoom(new RoomPlan(Template!.Id, _rect, WallSide.South)),
            _ when _dragStart is null => new BuildCheck(0, new[] { "Drag out the area to add to the building." }),
            _ => _sim.CheckExtension(_sim.Site.Local(_rect)),
        };

        QueueRedraw();
        CheckChanged?.Invoke();
    }

    /// <summary>Checks every chair in the row: what the ones that can go will cost, or why none can.</summary>
    private BuildCheck CheckChairs()
    {
        var row = ChairRow();
        var facing = RowFacing(row);
        var checks = row.Select(t => _sim.CheckChair(t, facing, ChairColour)).ToList();
        var good = checks.Where(c => c.CanBuild).ToList();
        if (good.Count == 0)
        {
            return checks[0];
        }

        // Each chair must be affordable on top of the ones before it.
        var cost = good.Sum(c => c.Cost);
        return _sim.CanAfford(cost) ? new BuildCheck(cost, Array.Empty<string>()) : new BuildCheck(cost, new[] { $"Not enough money: costs £{cost:N0}, you have £{_sim.Balance:N0}." });
    }

    private GridRect DragRect()
    {
        var start = _dragStart ?? _hover;
        var x = Math.Min(start.X, _hover.X);
        var y = Math.Min(start.Y, _hover.Y);
        return new GridRect(x, y, Math.Abs(_hover.X - start.X) + 1, Math.Abs(_hover.Y - start.Y) + 1);
    }

    // ------------------------------------------------------------------ preview

    public override void _Draw()
    {
        if (!Active || Check is null)
        {
            return;
        }

        if (Mode == BuildMode.Chair)
        {
            DrawChairPreview();
            return;
        }

        if (Mode == BuildMode.Trolley)
        {
            DrawTrolleyPreview();
            return;
        }

        if (Mode is BuildMode.Desk or BuildMode.Door)
        {
            DrawFittingPreview();
            return;
        }

        if (_dragStart is null)
        {
            // Before the drag starts, just mark the tile under the cursor.
            var t = new[] { P(_hover.X, _hover.Y), P(_hover.X + 1, _hover.Y), P(_hover.X + 1, _hover.Y + 1), P(_hover.X, _hover.Y + 1) };
            DrawColoredPolygon(t, new Color(Good, 0.35f));
            DrawPolyline(t.Append(t[0]).ToArray(), Good, 2f, true);
            return;
        }

        var ok = Check.CanBuild;
        var colour = ok ? Good : Bad;
        var r = _rect;
        var quad = new[] { P(r.X, r.Y), P(r.Right, r.Y), P(r.Right, r.Bottom), P(r.X, r.Bottom) };
        DrawColoredPolygon(quad, new Color(colour, 0.28f));
        DrawPolyline(quad.Append(quad[0]).ToArray(), colour, 3f, true);

        if (Mode == BuildMode.Room && Check.Preview is { } room)
        {
            // Furniture outlines, and the doorways in a brighter colour.
            foreach (var item in room.Furniture)
            {
                var f = new[] { P(item.X, item.Y), P(item.X + item.Width, item.Y), P(item.X + item.Width, item.Y + item.Length), P(item.X, item.Y + item.Length) };
                DrawColoredPolygon(f, new Color(1, 1, 1, 0.45f));
                DrawPolyline(f.Append(f[0]).ToArray(), new Color(colour, 0.9f), 1.5f, true);
            }

            foreach (var door in room.Doors)
            {
                var (a, b) = DoorEnds(room.Rect, door);
                DrawLine(a, b, new Color("#1E88E5"), 7f, true);
            }
        }

        CostTag(P(r.Right, r.Y));
    }

    /// <summary>Ghost chairs along the row being placed, outlined green or red, with the cost.</summary>
    private void DrawChairPreview()
    {
        var row = ChairRow();
        var facing = RowFacing(row);
        var colour = new Color(ChairColour);
        foreach (var tile in row.OrderBy(t => IsoDraw.ToScreen(t.X + 0.5, t.Y + 0.5).Y))
        {
            var ok = _sim.CheckChair(tile, facing, ChairColour).CanBuild;
            var quad = new[] { P(tile.X, tile.Y), P(tile.X + 1, tile.Y), P(tile.X + 1, tile.Y + 1), P(tile.X, tile.Y + 1) };
            DrawColoredPolygon(quad, new Color(ok ? Good : Bad, 0.3f));
            DrawPolyline(quad.Append(quad[0]).ToArray(), ok ? Good : Bad, 2f, true);
            if (ok)
            {
                ChairArt.Whole(this, tile, facing, new Color(colour, 0.85f), _sim.ChairShift(tile));
            }

            // An arrow showing which way people will sit.
            var centre = IsoDraw.ToScreen(tile.X + 0.5, tile.Y + 0.5);
            var tip = centre + (ChairArt.ScreenDirection(facing) * 0.55f);
            DrawLine(centre, tip, new Color("#1D2B36"), 2f, true);
        }

        var last = row.MaxBy(t => IsoDraw.ToScreen(t.X + 1, t.Y).X);
        CostTag(P(last.X + 1, last.Y));
    }

    /// <summary>Every trolley's spot ringed; the tile under the cursor green or red, with a ghost trolley when placing.</summary>
    private void DrawTrolleyPreview()
    {
        foreach (var trolley in _sim.ResusTrolleys.Where(t => IsoDraw.OnViewFloor(t.Home.Y)))
        {
            var ring = IsoDraw.FloorEllipse(IsoDraw.ToScreen(trolley.Home.X + 0.5, trolley.Home.Y + 0.5), 16, 20);
            var colour = ReferenceEquals(trolley, CarriedTrolley) ? new Color("#FFEB3B") : new Color("#1E88E5");
            DrawPolyline(ring.Append(ring[0]).ToArray(), colour, 2f, true);
        }

        var ok = Check!.CanBuild;
        var t = _hover;
        var quad = new[] { P(t.X, t.Y), P(t.X + 1, t.Y), P(t.X + 1, t.Y + 1), P(t.X, t.Y + 1) };
        DrawColoredPolygon(quad, new Color(ok ? Good : Bad, 0.3f));
        DrawPolyline(quad.Append(quad[0]).ToArray(), ok ? Good : Bad, 2f, true);
        if (ok && (CarriedTrolley is not null || HoveredTrolley is null))
        {
            var def = _sim.Database.Furniture.GetValueOrDefault(HospitalSimulation.ResusTrolleyItem);
            var w = (def?.WidthM ?? 0.6) / Units.MetresPerTile;
            var l = (def?.DepthM ?? 0.7) / Units.MetresPerTile;
            Figures.ResusTrolleyAt(this, t.X + 0.5, t.Y + 0.5, w, l, (float)((def?.HeightM ?? 1.0) * IsoDraw.PixelsPerMetre), new Color(def?.Colour ?? "#D84B3E", 0.8f), (0, 1));
        }

        CostTag(P(t.X + 1, t.Y));
    }

    /// <summary>The reception desk or door being placed: its tiles, the receptionist and queue spots, and the cost.</summary>
    private void DrawFittingPreview()
    {
        var ok = Check!.CanBuild;
        var colour = ok ? Good : Bad;
        void Tile(GridCoord t, Color fill)
        {
            var quad = new[] { P(t.X, t.Y), P(t.X + 1, t.Y), P(t.X + 1, t.Y + 1), P(t.X, t.Y + 1) };
            DrawColoredPolygon(quad, fill);
            DrawPolyline(quad.Append(quad[0]).ToArray(), new Color(colour, 0.9f), 1.5f, true);
        }

        if (TargetRoom is { } room)
        {
            var r = room.Rect;
            var outline = new[] { P(r.X, r.Y), P(r.Right, r.Y), P(r.Right, r.Bottom), P(r.X, r.Bottom) };
            DrawPolyline(outline.Append(outline[0]).ToArray(), new Color("#1E88E5", 0.8f), 2f, true);
        }

        if (Mode == BuildMode.Desk && TargetRoom is { } deskRoom)
        {
            var layout = WaitingRoomLayout.Desk(deskRoom.Rect, new ReceptionDesk(_hover, DeskFacing), WaitingRoomLayout.DoorTiles(deskRoom.Rect, deskRoom.Doors));
            foreach (var t in layout.DeskTiles)
            {
                Tile(t, new Color(colour, 0.45f));
            }

            Tile(layout.StaffSpot, new Color("#1E88E5", 0.35f));
            foreach (var t in layout.Queue)
            {
                Tile(t, new Color(1, 1, 1, 0.3f));
            }

            var f = layout.Furniture;
            var top = new[] { P(f.X, f.Y), P(f.X + f.Width, f.Y), P(f.X + f.Width, f.Y + f.Length), P(f.X, f.Y + f.Length) };
            DrawColoredPolygon(Array.ConvertAll(top, v => v + new Vector2(0, -30)), new Color("#8C6D4F", 0.75f));
            CostTag(P(layout.DeskTiles.Max(t => t.X) + 1, layout.DeskTiles.Min(t => t.Y)));
            return;
        }

        if (Mode == BuildMode.Door && TargetRoom is { } doorRoom)
        {
            var tile = Inside(doorRoom.Rect, _hover);
            var door = RemovingDoor ? _sim.ExistingDoorAt(doorRoom, tile) : _sim.DoorAt(doorRoom, tile);
            if (door is not null)
            {
                var (a, b) = DoorEnds(doorRoom.Rect, door);
                DrawLine(a, b, RemovingDoor ? Bad : ok ? (_sim.IsEntranceDoor(doorRoom, door) ? new Color("#1E88E5") : Good) : Bad, 8f, true);
                CostTag(b);
                return;
            }
        }

        Tile(_hover, new Color(colour, 0.3f));
        CostTag(P(_hover.X + 1, _hover.Y));
    }

    /// <summary>A cost (or the first problem) beside the preview, kept the same size whatever the zoom.</summary>
    private void CostTag(Vector2 at)
    {
        var ok = Check!.CanBuild;
        var zoom = Math.Max(0.2f, GetViewport().GetCanvasTransform().X.X);
        var font = ThemeDB.FallbackFont;
        var size = (int)Math.Round(17 / zoom);
        var text = ok
            ? Mode == BuildMode.Trolley && CarriedTrolley is null && HoveredTrolley is not null ? "Click to pick up"
            : Check.Cost < 0 ? $"Refund £{-Check.Cost:N0}" : Check.Cost == 0 ? (Mode == BuildMode.Trolley ? "Put down here" : "Free") : $"£{Check.Cost:N0}"
            : Check.Problems[0];
        var anchor = at + new Vector2(8 / zoom, -6 / zoom);
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        DrawRect(new Rect2(anchor + new Vector2(-6 / zoom, -size), new Vector2(width + (12 / zoom), size * 1.45f)), new Color(UiTop, 0.92f));
        DrawString(font, anchor, text, HorizontalAlignment.Left, -1, size, ok ? new Color("#9BE7B5") : new Color("#FFB4AE"));
    }

    private static readonly Color UiTop = new("#1D2B36");

    private Vector2 P(double x, double y)
    {
        return IsoDraw.ToScreen(x, y);
    }

    private (Vector2, Vector2) DoorEnds(GridRect r, Doorway door) => door.Side switch
    {
        WallSide.North => (P(r.X + door.Offset, r.Y), P(r.X + door.Offset + door.Width, r.Y)),
        WallSide.South => (P(r.X + door.Offset, r.Bottom), P(r.X + door.Offset + door.Width, r.Bottom)),
        WallSide.West => (P(r.X, r.Y + door.Offset), P(r.X, r.Y + door.Offset + door.Width)),
        _ => (P(r.Right, r.Y + door.Offset), P(r.Right, r.Y + door.Offset + door.Width)),
    };
}
