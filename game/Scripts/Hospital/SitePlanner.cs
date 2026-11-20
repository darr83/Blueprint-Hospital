using System;
using System.Linq;
using BlueprintHospital.Core;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using BlueprintHospital.UI;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>Which entrance the site plan is placing.</summary>
public enum SitePlanStep
{
    MainEntrance,
    AmbulanceDoors,
    Done,
}

/// <summary>
/// The site plan at the start of a new game: the player clicks an outside wall for the main entrance, then
/// one for the ambulance doors, and the hospital is laid out again around each choice (roads, car park,
/// bays and, for a ready-made start, the starter rooms). Start opens the hospital.
/// </summary>
public partial class SitePlanner : Node2D
{
    private static readonly Color EntranceBlue = new("#1F5FBF");
    private static readonly Color AmbulanceRed = new("#D32F2F");
    private static readonly Color Bad = new("#9E9E9E");

    /// <summary>Screen pixels per tile in the plan view at zoom 1.</summary>
    private const float PixelsPerTileAtZoom1 = 32.1f;

    private HospitalScene _scene = null!;
    private SitePlan _plan = SitePlan.Standard;
    private bool _readyMade;
    private SitePlanStep _step;
    private SitePlan? _candidate;
    private string? _problem;
    private Label _instructions = null!;
    private Button _entranceButton = null!;
    private Button _ambulanceButton = null!;
    private Label _tip = null!;

    public void Initialise(HospitalScene scene)
    {
        _scene = scene;
        var session = scene.Session;
        _plan = session.Site.Layout?.Plan ?? SitePlan.Standard;
        _readyMade = GameManager.Instance.SitePlanning?.Start != StartingHospital.EmptyBuilding;
        _step = GameManager.Instance.SitePlanStep;
        session.Clock.SetSpeed(SimulationSpeed.Paused);
        scene.Hud.Visible = false;
        scene.SetPlanView(true);
        var size = GetViewportRect().Size;

        // The whole site from above, in the space below the banner.
        const float banner = 190f;
        var perTile = Math.Min(size.X * 0.94f / StarterSite.SiteWidth, (size.Y - banner - 16) / StarterSite.SiteLength);
        var zoom = Math.Clamp(perTile / PixelsPerTileAtZoom1, IsoCamera.MinZoom, 1f);
        scene.Camera.SnapTo(IsoDraw.ToScreen(StarterSite.SiteWidth / 2.0, (StarterSite.SiteLength / 2.0) - ((banner - 8) / 2 / (zoom * PixelsPerTileAtZoom1))), zoom);
        BuildBanner();
        ZIndex = 50;
    }

    private void BuildBanner()
    {
        var layer = new CanvasLayer { Layer = 20, Name = "SitePlanBanner" };
        AddChild(layer);
        var root = new Control { Theme = UiStyle.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UiStyle.Card(14, 12));
        panel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.Position = new Vector2(0, 12);
        root.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);
        box.AddChild(UiStyle.Heading("Site plan", 22));
        _instructions = UiStyle.Body(string.Empty, muted: true);
        _instructions.CustomMinimumSize = new Vector2(640, 0);
        box.AddChild(_instructions);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        box.AddChild(row);
        _entranceButton = UiStyle.SecondaryButton(string.Empty, () => SetStep(SitePlanStep.MainEntrance), 190);
        _ambulanceButton = UiStyle.SecondaryButton(string.Empty, () => SetStep(SitePlanStep.AmbulanceDoors), 190);
        row.AddChild(_entranceButton);
        row.AddChild(_ambulanceButton);
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        row.AddChild(UiStyle.SecondaryButton("Usual layout", UseUsualLayout));
        row.AddChild(UiStyle.Button("Start", Finish, 110));

        _tip = new Label { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _tip.AddThemeStyleboxOverride("normal", UiStyle.Box(UiStyle.TextDark, 6, null, 6));
        _tip.AddThemeColorOverride("font_color", Colors.White);
        _tip.AddThemeFontSizeOverride("font_size", 14);
        root.AddChild(_tip);
        Refresh();
    }

    private void Refresh()
    {
        _entranceButton.Text = $"Main entrance: {WallName(_plan.EntranceSide)}";
        _ambulanceButton.Text = $"Ambulance doors: {WallName(_plan.AmbulanceSide)}";
        Tint(_entranceButton, _step == SitePlanStep.MainEntrance, EntranceBlue);
        Tint(_ambulanceButton, _step == SitePlanStep.AmbulanceDoors, AmbulanceRed);
        _instructions.Text = (_step, _readyMade) switch
        {
            (SitePlanStep.MainEntrance, true) => "Click the front or back wall where the main entrance should go. The waiting room and A&E rooms move to match.",
            (SitePlanStep.MainEntrance, false) => "Click any outside wall where the main entrance should go. Walk-in patients, visitors and staff come in here.",
            (SitePlanStep.AmbulanceDoors, true) => "Click the east or west end of the A&E corridor for the ambulance doors. The four bays go outside them.",
            (SitePlanStep.AmbulanceDoors, false) => "Click any outside wall for the ambulance doors. The four ambulance bays go outside them.",
            _ => "Happy with it? Press Start to open the hospital, or pick an entrance above to move it.",
        };
    }

    private static void Tint(Button button, bool on, Color colour)
    {
        var box = UiStyle.Box(on ? colour.Lerp(Colors.White, 0.85f) : UiStyle.Surface, 10, on ? colour : UiStyle.Border, 10);
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" })
        {
            button.AddThemeStyleboxOverride(state, box);
        }
    }

    private static string WallName(WallSide side) => side switch
    {
        WallSide.North => "north wall",
        WallSide.South => "south wall",
        WallSide.East => "east wall",
        _ => "west wall",
    };

    private void SetStep(SitePlanStep step)
    {
        _step = step;
        GameManager.Instance.SitePlanStep = step;
        _candidate = null;
        Refresh();
        QueueRedraw();
    }

    private void UseUsualLayout()
    {
        if (_plan != SitePlan.Standard)
        {
            GameManager.Instance.Replan(SitePlan.Standard, SitePlanStep.Done);
        }
    }

    public void Finish()
    {
        GameManager.Instance.FinishSitePlan();
        _scene.SetPlanView(false);
        _scene.Hud.Visible = true;
        var b = _scene.Session.Site.Buildings[0].Entrances.First(e => !e.IsAmbulanceEntrance).Tile;
        _scene.Camera.SnapTo(IsoDraw.ToScreenGround(b.X + 4, b.Y), 0.56f);
        _scene.Session.Clock.SetSpeed(SimulationSpeed.Normal);
        QueueFree();
    }

    public override void _Process(double delta)
    {
        if (_step == SitePlanStep.Done)
        {
            _tip.Visible = false;
            return;
        }

        var mouse = GetGlobalMousePosition();
        var tile = IsoDraw.ScreenToTile(mouse);
        var candidate = CandidateAt(tile);
        if (candidate != _candidate)
        {
            _candidate = candidate;
            _problem = candidate is null ? null : ProblemWith(candidate);
            QueueRedraw();
        }

        _tip.Visible = _candidate is not null;
        if (_tip.Visible)
        {
            _tip.Text = _problem ?? (_step == SitePlanStep.MainEntrance ? "Click to put the main entrance here" : "Click to put the ambulance doors and bays here");
            _tip.Position = GetViewport().GetMousePosition() + new Vector2(18, 18);
        }
    }

    /// <summary>The plan with the entrance being placed moved to the wall nearest <paramref name="tile"/>.</summary>
    private SitePlan? CandidateAt(GridCoord tile)
    {
        var b = StarterSite.MainBuildingFootprint;
        if (tile.X < b.X - 24 || tile.X >= b.Right + 24 || tile.Y < b.Y - 24 || tile.Y >= b.Bottom + 24)
        {
            return null;
        }

        var ambulance = _step == SitePlanStep.AmbulanceDoors;
        var sides = _readyMade
            ? (ambulance ? new[] { WallSide.East, WallSide.West } : new[] { WallSide.North, WallSide.South })
            : new[] { WallSide.North, WallSide.East, WallSide.South, WallSide.West };

        // The nearest allowed wall, and how far along it the mouse is.
        var side = sides.OrderBy(s => s switch
        {
            WallSide.North => Math.Abs(tile.Y - b.Y) + Outside(tile.X, b.X, b.Right),
            WallSide.South => Math.Abs(tile.Y - (b.Bottom - 1)) + Outside(tile.X, b.X, b.Right),
            WallSide.West => Math.Abs(tile.X - b.X) + Outside(tile.Y, b.Y, b.Bottom),
            _ => Math.Abs(tile.X - (b.Right - 1)) + Outside(tile.Y, b.Y, b.Bottom),
        }).First();
        var along = side is WallSide.North or WallSide.South ? tile.X - b.X : tile.Y - b.Y;
        var at = Math.Clamp(along - (StarterSite.DoorWidth / 2), StarterSite.CornerGap, StarterSite.MaxDoorAt(side));

        if (ambulance)
        {
            return _readyMade
                ? _plan with { AmbulanceSide = side, AmbulanceAt = EmergencyDepartmentLayout.StarterAmbulanceAt(_plan.EntranceSide) }
                : _plan with { AmbulanceSide = side, AmbulanceAt = at };
        }

        // For a ready-made start the ambulance doors stay at the end of the A&E corridor, which moves with the front.
        return _readyMade
            ? _plan with { EntranceSide = side, EntranceAt = at, AmbulanceAt = EmergencyDepartmentLayout.StarterAmbulanceAt(side) }
            : _plan with { EntranceSide = side, EntranceAt = at };
    }

    private static int Outside(int v, int from, int to) => v < from ? from - v : v >= to ? v - to + 1 : 0;

    private static string? ProblemWith(SitePlan plan) => StarterSite.Problems(plan).FirstOrDefault();

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            GetViewport().SetInputAsHandled();
            if (_candidate is { } plan && _problem is null)
            {
                var next = _step == SitePlanStep.MainEntrance ? SitePlanStep.AmbulanceDoors : SitePlanStep.Done;
                if (plan == _plan)
                {
                    SetStep(next);
                }
                else
                {
                    GameManager.Instance.Replan(plan, next);
                }
            }

            return;
        }

        // Keys other than Escape (the pause menu) and the camera wait until the hospital starts.
        if (@event is InputEventKey { Pressed: true } key && key.Keycode is not (Key.Escape or Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Down or Key.Left or Key.Right))
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Draw()
    {
        if (_candidate is not { } plan || _step == SitePlanStep.Done)
        {
            return;
        }

        using var flat = IsoDraw.PushFlat();
        var ok = _problem is null;
        var b = StarterSite.MainBuildingFootprint;
        var ambulance = _step == SitePlanStep.AmbulanceDoors;
        var colour = ok ? (ambulance ? AmbulanceRed : EntranceBlue) : Bad;
        var side = ambulance ? plan.AmbulanceSide : plan.EntranceSide;
        var at = ambulance ? plan.AmbulanceAt : plan.EntranceAt;

        // The doorway, as a thick bar on the wall.
        var door = StarterSite.DoorTile(b, side, at);
        (double X, double Y, double W, double L) bar = side switch
        {
            WallSide.North => (door.X, door.Y - 0.6, StarterSite.DoorWidth, 1.2),
            WallSide.South => (door.X, door.Y + 0.4, StarterSite.DoorWidth, 1.2),
            WallSide.West => (door.X - 0.6, door.Y, 1.2, StarterSite.DoorWidth),
            _ => (door.X + 0.4, door.Y, 1.2, StarterSite.DoorWidth),
        };
        IsoDraw.FloorQuad(this, bar.X, bar.Y, bar.W, bar.L, colour);

        // Ghost of what goes outside it: the bays, or the front path.
        var ghost = ambulance ? StarterSite.BayBlockFor(plan) : StarterSite.FrontPathFor(plan);
        IsoDraw.FloorQuad(this, ghost.X, ghost.Y, ghost.Width, ghost.Length, new Color(colour, 0.28f));
        var corners = new[]
        {
            IsoDraw.ToScreen(ghost.X, ghost.Y), IsoDraw.ToScreen(ghost.Right, ghost.Y),
            IsoDraw.ToScreen(ghost.Right, ghost.Bottom), IsoDraw.ToScreen(ghost.X, ghost.Bottom), IsoDraw.ToScreen(ghost.X, ghost.Y),
        };
        DrawPolyline(corners, colour, 6f, true);
        if (ambulance)
        {
            var across = side is WallSide.East or WallSide.West;
            for (var i = 1; i < StarterSite.AmbulanceBays; i++)
            {
                var a = across ? IsoDraw.ToScreen(ghost.X, ghost.Y + (i * StarterSite.BayWidth)) : IsoDraw.ToScreen(ghost.X + (i * StarterSite.BayWidth), ghost.Y);
                var c = across ? IsoDraw.ToScreen(ghost.Right, ghost.Y + (i * StarterSite.BayWidth)) : IsoDraw.ToScreen(ghost.X + (i * StarterSite.BayWidth), ghost.Bottom);
                DrawLine(a, c, colour, 4f, true);
            }
        }
    }
}
