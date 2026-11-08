using System;
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
}

/// <summary>
/// Places rooms, corridors and building extensions. Shows a see-through preview that is green
/// when it can be built and red when it can't, with the furniture the room will get. Rooms follow
/// the cursor (R turns them, left click builds); corridors and extensions are dragged out.
/// </summary>
public partial class BuildTool : Node2D
{
    private static readonly Color Good = new("#2EBD6B");
    private static readonly Color Bad = new("#E53935");

    private HospitalSimulation _sim = null!;
    private IsoProjection _projection = null!;
    private GridCoord _hover;
    private GridCoord? _dragStart;
    private GridRect _rect;
    private string _planKey = string.Empty;

    public BuildMode Mode { get; private set; }

    public RoomTemplate? Template { get; private set; }

    /// <summary>Room size in tiles: along the door wall, and deep.</summary>
    public (int Width, int Depth) Size { get; private set; }

    public WallSide DoorSide { get; private set; } = WallSide.South;

    /// <summary>The room being moved, if this is a move rather than a new room.</summary>
    public string? MovingId { get; private set; }

    public int CorridorWidth { get; private set; } = 5;

    /// <summary>The latest check of what is under the cursor.</summary>
    public BuildCheck? Check { get; private set; }

    public bool Active => Mode != BuildMode.None;

    /// <summary>False while a test or tutorial drives the preview with <see cref="PreviewAt"/>.</summary>
    public bool FollowMouse { get; set; } = true;

    /// <summary>Moves the preview to a tile without the mouse (for the smoke test).</summary>
    public void PreviewAt(GridCoord tile)
    {
        FollowMouse = false;
        _hover = tile;
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

    public void StartRoom(RoomTemplate template, int widthTiles, int depthTiles, WallSide door = WallSide.South, string? movingId = null)
    {
        Mode = BuildMode.Room;
        Template = template;
        Size = (widthTiles, depthTiles);
        DoorSide = door;
        MovingId = movingId;
        Restart();
    }

    public void StartCorridor(int widthTiles)
    {
        Mode = BuildMode.Corridor;
        Template = _sim.Database.RoomTemplates.Values.First(t => t.IsCorridor);
        CorridorWidth = widthTiles;
        MovingId = null;
        Restart();
    }

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
        _dragStart = null;
        Check = null;
        QueueRedraw();
        CheckChanged?.Invoke();
        Stopped?.Invoke();
    }

    public void Rotate()
    {
        DoorSide = DoorSide switch
        {
            WallSide.South => WallSide.West,
            WallSide.West => WallSide.North,
            WallSide.North => WallSide.East,
            _ => WallSide.South,
        };
        Refresh(force: true);
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

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                if (Mode == BuildMode.Room)
                {
                    Place();
                }
                else
                {
                    _dragStart = _hover;
                    Refresh(force: true);
                }

                return true;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } when _dragStart is not null:
                Refresh(force: true);
                Place();
                _dragStart = null;
                Refresh(force: true);
                return true;

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.R } when Mode == BuildMode.Room:
                Rotate();
                return true;

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }:
                Cancel();
                return true;
        }

        return false;
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
                result = _sim.MoveRoom(id, new RoomPlan(Template!.Id, _rect, DoorSide));
                what = $"Moved {_sim.FindSpace(id)?.Name}";
                break;
            case BuildMode.Room:
            case BuildMode.Corridor:
                result = _sim.BuildRoom(new RoomPlan(Template!.Id, _rect, DoorSide));
                what = $"Built {Template.Name.ToLowerInvariant()}";
                break;
            default:
                result = _sim.ExtendBuilding(_rect);
                what = "Extended the building";
                break;
        }

        if (!result.CanBuild)
        {
            return;
        }

        Built?.Invoke($"{what} for £{result.Cost:N0}.");
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
        var tile = _projection.ScreenToTile(mouse.X, mouse.Y);
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

        _rect = Mode switch
        {
            BuildMode.Room => RoomRect(),
            BuildMode.Corridor => CorridorRect(),
            _ => DragRect(),
        };

        var key = $"{Mode}|{_rect}|{DoorSide}|{_sim.Balance}|{_dragStart}";
        if (!force && key == _planKey)
        {
            return;
        }

        _planKey = key;
        Check = Mode switch
        {
            BuildMode.Room => _sim.CheckRoom(new RoomPlan(Template!.Id, _rect, DoorSide), MovingId),
            BuildMode.Corridor when _dragStart is null => new BuildCheck(0, new[] { "Drag along the floor to lay the corridor." }),
            BuildMode.Corridor => _sim.CheckRoom(new RoomPlan(Template!.Id, _rect, WallSide.South)),
            _ when _dragStart is null => new BuildCheck(0, new[] { "Drag out the area to add to the building." }),
            _ => _sim.CheckExtension(_rect),
        };

        QueueRedraw();
        CheckChanged?.Invoke();
    }

    private GridRect RoomRect()
    {
        var probe = RoomLayoutEngine.RectFor(default, Size.Width, Size.Depth, DoorSide);
        return new GridRect(_hover.X - (probe.Width / 2), _hover.Y - (probe.Length / 2), probe.Width, probe.Length);
    }

    private GridRect CorridorRect()
    {
        var w = CorridorWidth;
        var start = _dragStart ?? _hover;
        var dx = _hover.X - start.X;
        var dy = _hover.Y - start.Y;
        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            var x = Math.Min(start.X, _hover.X);
            return new GridRect(x, start.Y - (w / 2), Math.Max(w, Math.Abs(dx) + 1), w);
        }

        var y = Math.Min(start.Y, _hover.Y);
        return new GridRect(start.X - (w / 2), y, w, Math.Max(w, Math.Abs(dy) + 1));
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

        // A cost tag beside the preview, kept the same size whatever the zoom.
        var zoom = Math.Max(0.2f, GetViewport().GetCanvasTransform().X.X);
        var font = ThemeDB.FallbackFont;
        var size = (int)Math.Round(17 / zoom);
        var text = ok
            ? Check.Cost < 0 ? $"Refund £{-Check.Cost:N0}" : $"£{Check.Cost:N0}"
            : Check.Problems[0];
        var anchor = P(r.Right, r.Y) + new Vector2(8 / zoom, -6 / zoom);
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        DrawRect(new Rect2(anchor + new Vector2(-6 / zoom, -size), new Vector2(width + (12 / zoom), size * 1.45f)), new Color(UiTop, 0.92f));
        DrawString(font, anchor, text, HorizontalAlignment.Left, -1, size, ok ? new Color("#9BE7B5") : new Color("#FFB4AE"));
    }

    private static readonly Color UiTop = new("#1D2B36");

    private Vector2 P(double x, double y)
    {
        var (sx, sy) = _projection.GridToScreen(x, y);
        return new Vector2((float)sx, (float)sy);
    }

    private (Vector2, Vector2) DoorEnds(GridRect r, Doorway door) => door.Side switch
    {
        WallSide.North => (P(r.X + door.Offset, r.Y), P(r.X + door.Offset + door.Width, r.Y)),
        WallSide.South => (P(r.X + door.Offset, r.Bottom), P(r.X + door.Offset + door.Width, r.Bottom)),
        WallSide.West => (P(r.X, r.Y + door.Offset), P(r.X, r.Y + door.Offset + door.Width)),
        _ => (P(r.Right, r.Y + door.Offset), P(r.Right, r.Y + door.Offset + door.Width)),
    };
}
