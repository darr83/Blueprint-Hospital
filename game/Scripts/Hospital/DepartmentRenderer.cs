using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// The department's floors: a tint per kind of room, the ambulance bay markings and coloured
/// coloured wayfinding bands outside clinical rooms.
/// Everything that stands up from the floor is drawn by <see cref="WorldLayer"/>.
/// </summary>
public partial class DepartmentRenderer : Node2D
{
    public static readonly Dictionary<CareSpaceKind, Color> FloorColours = new()
    {
        [CareSpaceKind.Resus] = new Color("#F3D6D3"),
        [CareSpaceKind.Majors] = new Color("#F4E7C6"),
        [CareSpaceKind.Minors] = new Color("#D8EBD3"),
        [CareSpaceKind.Triage] = new Color("#E2DCF0"),
        [CareSpaceKind.Waiting] = new Color("#D6E4F2"),
        [CareSpaceKind.NursesStation] = new Color("#DDE3E8"),
        [CareSpaceKind.DoctorsOffice] = new Color("#DDE3E8"),
        [CareSpaceKind.StaffRoom] = new Color("#EADFCF"),
        [CareSpaceKind.Corridor] = new Color("#D9DEDB"),
        [CareSpaceKind.Ward] = new Color("#DCE8F0"),
        [CareSpaceKind.XRay] = new Color("#DFE3E8"),
        [CareSpaceKind.CT] = new Color("#DFE3E8"),
        [CareSpaceKind.Theatre] = new Color("#D2E9E5"),
        [CareSpaceKind.AnaestheticRoom] = new Color("#D9EBE8"),
        [CareSpaceKind.TheatreRecovery] = new Color("#E0EEEA"),
        [CareSpaceKind.Pharmacy] = new Color("#E0EDDC"),
        [CareSpaceKind.Clinic] = new Color("#E3E9F3"),
        [CareSpaceKind.OutpatientReception] = new Color("#D6E4F2"),
        [CareSpaceKind.Echo] = new Color("#EBDDE3"),
        [CareSpaceKind.Eeg] = new Color("#E4DDEE"),
        [CareSpaceKind.Ultrasound] = new Color("#DCE7EC"),
        [CareSpaceKind.Endoscopy] = new Color("#DDE6EA"),
        [CareSpaceKind.CathLab] = new Color("#DFE3E8"),
        [CareSpaceKind.PlasterRoom] = new Color("#E6E4DE"),
        [CareSpaceKind.MentalHealthAssessment] = new Color("#DDEDEE"),
        [CareSpaceKind.MentalHealthLounge] = new Color("#EDE3D3"),
        [CareSpaceKind.MentalHealthGarden] = new Color("#A9CC8C"),
        [CareSpaceKind.PhysioGym] = new Color("#DDE9E2"),
        [CareSpaceKind.EndoscopyRecovery] = new Color("#E2EAEE"),
        [CareSpaceKind.OutreachOffice] = new Color("#E6DFEE"),
        [CareSpaceKind.Cafeteria] = new Color("#EFE3D2"),
        [CareSpaceKind.VendingPoint] = new Color("#ECE2D6"),
        [CareSpaceKind.Toilets] = new Color("#DCE8EC"),
        [CareSpaceKind.AccessibleToilet] = new Color("#D9E6EE"),
        [CareSpaceKind.StaffToilet] = new Color("#E0E6EA"),
        [CareSpaceKind.CleaningRoom] = new Color("#DDE5DA"),
        [CareSpaceKind.Lift] = new Color("#C9CFD5"),
        [CareSpaceKind.Stairs] = new Color("#D8D4CC"),
        [CareSpaceKind.DeliveryRoom] = new Color("#F2DDE8"),
        [CareSpaceKind.PlayRoom] = new Color("#E6F2D5"),
        [CareSpaceKind.ParentsRoom] = new Color("#EFE4D6"),
    };

    /// <summary>
    /// A room's usual floor colour: by its kind, except the children's rooms, which have their own
    /// brighter colours (sunny yellow wards, mint cubicles, sky blue waiting, lavender intensive care).
    /// </summary>
    public static Color FloorColourOf(CareSpace space) =>
        space.Kind == CareSpaceKind.MentalHealthGarden ? KindColour(space) : KindColour(space).Lerp(CleanWhite, CleanMix);

    /// <summary>The clean white the floors are laid in: each room's own colour shows only as a faint tint.</summary>
    private static readonly Color CleanWhite = new("#F6F8F9");

    /// <summary>How far each room's floor colour is taken towards <see cref="CleanWhite"/>.</summary>
    private const float CleanMix = 0.68f;

    private static Color KindColour(CareSpace space)
    {
        if (space.DepartmentId == HospitalSimulation.PaediatricsDepartmentId)
        {
            switch (space.Kind)
            {
                case CareSpaceKind.Ward when space.TemplateId is "picu" or "childrens_hdu":
                    return new Color("#E6E0F3");
                case CareSpaceKind.Ward:
                    return new Color("#F5ECC8");
                case CareSpaceKind.Majors:
                    return new Color("#DDF0E4");
                case CareSpaceKind.Waiting:
                    return new Color("#D3EAF7");
                case CareSpaceKind.Clinic:
                    return new Color("#F6E6D6");
                case CareSpaceKind.PlayRoom when space.TemplateId == "teen_room":
                    return new Color("#DCE1F2");
            }
        }

        if (space.Kind == CareSpaceKind.Ward && space.DepartmentId == HospitalSimulation.MentalHealthDepartmentId)
        {
            // Calm pale teal, like the assessment room.
            return new Color("#DDEDEE");
        }

        if (space.Kind == CareSpaceKind.MentalHealthGarden)
        {
            // Grass: kept strong, not washed out like the floors indoors.
            return FloorColours[space.Kind];
        }

        return FloorColours.GetValueOrDefault(space.Kind, new Color("#E6E9EC"));
    }

    private HospitalSimulation _sim = null!;
    private static HospitalSimulation? _current;

    private static bool IsCriticalCare(CareSpace space) => _current?.IsCriticalCareUnit(space) == true;

    public void Initialise(HospitalSimulation simulation)
    {
        _sim = simulation;
        _current = simulation;
        Repaint();
    }

    /// <summary>True for the copy that paints the floors flat, into the texture on the 3D floor.</summary>
    public bool Flat { get; set; }

    /// <summary>Each room's floor, by room id: drawn on its own so that one room can change without the rest being drawn again.</summary>
    private readonly Dictionary<string, RoomFloor> _roomFloors = new();
    private (int Rotation, int Floor, bool Plan)? _view;

    /// <summary>How many room floors were drawn again at the last repaint (for tests).</summary>
    public int RoomsRepainted { get; private set; }

    /// <summary>
    /// Brings the floors up to date after building, decorating or turning the view: only the rooms
    /// that are new, moved or decorated are drawn again, then the markings over them.
    /// </summary>
    public void Repaint()
    {
        QueueRedraw();
        if (_sim is null)
        {
            return;
        }

        var view = (IsoDraw.Rotation, IsoDraw.ViewFloor, IsoDraw.Plan);
        var all = view != _view;
        _view = view;
        RoomsRepainted = 0;
        var seen = new HashSet<string>();
        var index = 0;
        foreach (var space in _sim.Spaces.Where(s => IsoDraw.OnViewFloor(s.Rect.Y)).OrderBy(s => s.Kind == CareSpaceKind.Corridor ? 0 : 1))
        {
            seen.Add(space.Id);
            if (!_roomFloors.TryGetValue(space.Id, out var node))
            {
                node = new RoomFloor
                {
                    Name = $"Floor {space.Id}",
                    Renderer = this,
                    ShowBehindParent = true,
                    TextureRepeat = TextureRepeatEnum.Enabled,
                    TextureFilter = TextureFilterEnum.Nearest,
                };
                _roomFloors[space.Id] = node;
                AddChild(node);
            }

            node.Space = space;
            var key = (space.Rect, space.Kind, DecorArt.LookOf(_sim, space));
            if (all || node.Key != key)
            {
                node.Key = key;
                node.QueueRedraw();
                RoomsRepainted++;
            }

            MoveChild(node, index++);
        }

        foreach (var gone in _roomFloors.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            var node = _roomFloors[gone];
            _roomFloors.Remove(gone);
            RemoveChild(node);
            node.QueueFree();
        }
    }

    /// <summary>One room's floor, drawn under the markings of the <see cref="DepartmentRenderer"/> it belongs to.</summary>
    private sealed partial class RoomFloor : Node2D
    {
        public DepartmentRenderer Renderer { get; init; } = null!;

        public CareSpace? Space { get; set; }

        public (GridRect Rect, CareSpaceKind Kind, RoomDecor Look)? Key { get; set; }

        public override void _Draw()
        {
            if (Space is not null)
            {
                using var flat = Renderer.Flat ? IsoDraw.PushFlat() : default(IsoDraw.FlatScope?);
                Renderer.DrawRoom(this, Space);
            }
        }
    }

    /// <summary>A room's floor: its colour with a faint tile pattern, or the finish it was decorated with.</summary>
    private void DrawRoom(CanvasItem canvas, CareSpace space)
    {
        var r = space.Rect;
        var colour = FloorColourOf(space);
        if (DecorArt.LookOf(_sim, space) is { IsStandard: false } look && look.Floor != FloorFinish.Standard)
        {
            DecorArt.DrawRoomFloor(canvas, space, look);
            return;
        }

        // The floor colour with a subtle half-metre tile pattern: one shape with a repeating chequer
        // texture (a tile in every other square 2.5% darker), not a square drawn for every tile.
        var points = new[] { IsoDraw.ToScreen(r.X, r.Y), IsoDraw.ToScreen(r.Right, r.Y), IsoDraw.ToScreen(r.Right, r.Bottom), IsoDraw.ToScreen(r.X, r.Bottom) };
        var uvs = new[] { new Vector2(r.X, r.Y) / 2, new Vector2(r.Right, r.Y) / 2, new Vector2(r.Right, r.Bottom) / 2, new Vector2(r.X, r.Bottom) / 2 };
        canvas.DrawPolygon(points, new[] { colour, colour, colour, colour }, uvs, Chequer);
    }

    private static ImageTexture? _chequer;

    /// <summary>
    /// Two by two tiles, 16 texels each: white tiles, every other one 1.5% darker, with a thin grey grout
    /// line round each. It repeats across a room's floor and takes the room's colour.
    /// </summary>
    private static ImageTexture Chequer
    {
        get
        {
            if (_chequer is null)
            {
                const int tile = 16;
                var image = Image.CreateEmpty(tile * 2, tile * 2, false, Image.Format.Rgba8);
                var dark = Colors.White.Darkened(0.015f);
                var grout = Colors.White.Darkened(0.08f);
                for (var x = 0; x < tile * 2; x++)
                {
                    for (var y = 0; y < tile * 2; y++)
                    {
                        var edge = x % tile == 0 || y % tile == 0;
                        image.SetPixel(x, y, edge ? grout : ((x / tile) + (y / tile)) % 2 == 0 ? dark : Colors.White);
                    }
                }

                image.GenerateMipmaps();
                _chequer = ImageTexture.CreateFromImage(image);
            }

            return _chequer;
        }
    }

    /// <summary>The markings over the floors: wayfinding bands, lifts and stairs, and the ambulance bays. The floors themselves are the children, drawn behind.</summary>
    public override void _Draw()
    {
        if (_sim is null)
        {
            return;
        }

        using var flat = Flat ? IsoDraw.PushFlat() : default(IsoDraw.FlatScope?);

        DrawWayfinding();
        foreach (var space in _sim.Spaces.Where(s => s.IsShaft && IsoDraw.OnViewFloor(s.Rect.Y)))
        {
            if (space.Kind == CareSpaceKind.Lift)
            {
                DrawLift(space);
            }
            else
            {
                DrawStairs(space);
            }
        }

        if (IsoDraw.ViewFloor > 0)
        {
            return;
        }

        DrawAmbulanceBays();
    }

    /// <summary>
    /// The ambulance bays painted on the tarmac, UK style: a yellow line down each side and across the
    /// back, and AMBULANCE written up the open end where the vehicles reverse in.
    /// </summary>
    private void DrawAmbulanceBays()
    {
        var yellow = new Color("#F2C230");
        const double line = 0.18;
        var font = ThemeDB.FallbackFont;
        var side = _sim.AmbulanceBaySide;
        var across = side is WallSide.East or WallSide.West;
        foreach (var bay in _sim.AmbulanceBays)
        {
            if (across)
            {
                // Bays run east-west: lines along the north and south edges, the back line by the building.
                IsoDraw.FloorQuad(this, bay.X, bay.Y - (line / 2), bay.Width, line, yellow);
                IsoDraw.FloorQuad(this, bay.X, bay.Bottom - (line / 2), bay.Width, line, yellow);
                IsoDraw.FloorQuad(this, side == WallSide.East ? bay.X : bay.Right - (line * 1.5), bay.Y, line * 1.5, bay.Length, yellow);
            }
            else
            {
                IsoDraw.FloorQuad(this, bay.X - (line / 2), bay.Y, line, bay.Length, yellow);
                IsoDraw.FloorQuad(this, bay.Right - (line / 2), bay.Y, line, bay.Length, yellow);
                IsoDraw.FloorQuad(this, bay.X, side == WallSide.South ? bay.Y : bay.Bottom - (line * 1.5), bay.Width, line * 1.5, yellow);
            }

            if (!Flat)
            {
                continue;
            }

            // Lettering across the mouth of the bay, reading from the road.
            const int size = 22;
            const string text = "AMBULANCE";
            var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
            var (at, angle) = side switch
            {
                WallSide.East => (IsoDraw.ToScreen(bay.Right - 0.55, bay.Y + (bay.Length / 2.0)), -Mathf.Pi / 2),
                WallSide.West => (IsoDraw.ToScreen(bay.X + 0.55, bay.Y + (bay.Length / 2.0)), Mathf.Pi / 2),
                WallSide.North => (IsoDraw.ToScreen(bay.X + (bay.Width / 2.0), bay.Y + 0.55), Mathf.Pi),
                _ => (IsoDraw.ToScreen(bay.X + (bay.Width / 2.0), bay.Bottom - 0.55), 0f),
            };
            DrawSetTransform(at, angle, Vector2.One);
            DrawString(font, new Vector2(-width / 2, size * 0.35f), text, HorizontalAlignment.Left, -1, size, new Color(yellow, 0.9f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    /// <summary>The colour of the band outside a clinical room's door, or null for rooms without one.</summary>
    /// <summary>The colour scheme the player picked for a room's front, if any (set by the world layer).</summary>
    public static Func<CareSpace, string?>? ChosenColour { get; set; }

    /// <summary>A room's wayfinding colour: the scheme the player picked, else its department's colour (null for plain rooms).</summary>
    public static Color? WayfindingColour(CareSpace space) =>
        ChosenColour?.Invoke(space) is { } chosen ? new Color(chosen) : DepartmentColour(space);

    /// <summary>The wayfinding colour a room has from its kind and department, before any colour scheme is picked.</summary>
    public static Color? DepartmentColour(CareSpace space) =>
        space.Kind switch
        {
            CareSpaceKind.Resus => new Color("#E53935"),
            CareSpaceKind.Majors => new Color("#F9A825"),
            CareSpaceKind.Minors => new Color("#43A047"),
            CareSpaceKind.Triage => new Color("#7E57C2"),
            CareSpaceKind.Ward when IsCriticalCare(space) => new Color("#5E35B1"),
            CareSpaceKind.Ward when space.DepartmentId == HospitalSimulation.MaternityDepartmentId => new Color("#D46A9F"),
            CareSpaceKind.Ward when space.DepartmentId == HospitalSimulation.MentalHealthDepartmentId => new Color("#0097A7"),
            CareSpaceKind.MentalHealthLounge or CareSpaceKind.MentalHealthGarden => new Color("#0097A7"),
            CareSpaceKind.DeliveryRoom => new Color("#D46A9F"),
            CareSpaceKind.Ward => new Color("#1E88E5"),
            CareSpaceKind.OutreachOffice => new Color("#5E35B1"),
            CareSpaceKind.XRay or CareSpaceKind.CT => new Color("#546E7A"),
            CareSpaceKind.Theatre or CareSpaceKind.AnaestheticRoom or CareSpaceKind.TheatreRecovery => new Color("#00897B"),
            CareSpaceKind.Pharmacy => new Color("#7CB342"),
            CareSpaceKind.Clinic => new Color("#5C6BC0"),
            CareSpaceKind.Echo or CareSpaceKind.CathLab => new Color("#AD1457"),
            CareSpaceKind.Eeg => new Color("#6A1B9A"),
            CareSpaceKind.Ultrasound => new Color("#00838F"),
            CareSpaceKind.Endoscopy or CareSpaceKind.EndoscopyRecovery => new Color("#455A64"),
            CareSpaceKind.PlasterRoom => new Color("#8D6E63"),
            CareSpaceKind.MentalHealthAssessment => new Color("#0097A7"),
            CareSpaceKind.PhysioGym => new Color("#2E7D32"),
            CareSpaceKind.Lift or CareSpaceKind.Stairs => new Color("#37474F"),
            CareSpaceKind.PlayRoom => new Color("#E39B00"),
            CareSpaceKind.ParentsRoom => new Color("#8D6E63"),
            _ => (Color?)null,
        };

    /// <summary>
    /// A coloured band on the corridor floor outside each clinical room's door, so the way to
    /// Resus (red), Majors (amber), Minors (green) and Triage (purple) can be seen at a glance.
    /// </summary>
    private void DrawWayfinding()
    {
        foreach (var space in _sim.Spaces.Where(s => s.HasWalls && s.Doors.Count > 0 && IsoDraw.OnViewFloor(s.Rect.Y)))
        {
            var colour = WayfindingColour(space);
            if (colour is not { } c)
            {
                continue;
            }

            var door = space.Doors[0];
            var r = space.Rect;
            const double gap = 0.35;
            ((double X, double Y) A, (double X, double Y) B) ends = door.Side switch
            {
                WallSide.North => ((r.X + door.Offset, r.Y - gap), (r.X + door.Offset + door.Width, r.Y - gap)),
                WallSide.South => ((r.X + door.Offset, r.Bottom + gap), (r.X + door.Offset + door.Width, r.Bottom + gap)),
                WallSide.West => ((r.X - gap, r.Y + door.Offset), (r.X - gap, r.Y + door.Offset + door.Width)),
                _ => ((r.Right + gap, r.Y + door.Offset), (r.Right + gap, r.Y + door.Offset + door.Width)),
            };
            Line(new[] { ends.A, ends.B }, c, 6f);
        }
    }

    /// <summary>A lift car: a brushed steel floor plate with a chequer tread, and the landing doors' sill at the doorway.</summary>
    private void DrawLift(CareSpace space)
    {
        var r = space.Rect;
        const double inset = 0.45;
        var (x, y, w, l) = (r.X + inset, r.Y + inset, r.Width - (2 * inset), r.Length - (2 * inset));
        IsoDraw.FloorQuad(this, x, y, w, l, new Color("#9EA7B0"));
        for (var i = 1; i < 6; i++)
        {
            var t = i / 6.0;
            Line(new[] { (x + (w * t), y), (x + (w * t), y + l) }, new Color("#B9C1C9"), 1.5f);
            Line(new[] { (x, y + (l * t)), (x + w, y + (l * t)) }, new Color("#B9C1C9"), 1.5f);
        }

        IsoDraw.FloorOutline(this, x, y, w, l, new Color("#5E6B77"), 2f);

        // The sill and the two sliding door leaves, drawn open, at the doorway.
        if (space.Doors.Count > 0)
        {
            var door = space.Doors[0];
            var (a, b) = DoorEnds(r, door, -0.15);
            Line(new[] { a, b }, new Color("#6D7883"), 7f);
        }
    }

    /// <summary>A flight of stairs: treads rising away from the door, with a handrail down each side and an arrow up.</summary>
    private void DrawStairs(CareSpace space)
    {
        var r = space.Rect;
        const double inset = 0.45;
        var (x, y, w, l) = (r.X + inset, r.Y + inset, r.Width - (2 * inset), r.Length - (2 * inset));
        var side = space.DoorSide;
        var alongY = side is WallSide.North or WallSide.South;
        var depth = alongY ? l : w;
        var steps = Math.Max(4, (int)Math.Round(depth * 2));
        for (var i = 0; i < steps; i++)
        {
            // Step 0 is at the door; each one further in is a little darker, as if higher and catching less light.
            var from = i * depth / steps;
            var to = (i + 1) * depth / steps;
            var shade = new Color("#E2DDD3").Darkened(0.04f + (0.3f * i / steps));
            var (sx, sy, sw, sl) = side switch
            {
                WallSide.South => (x, y + l - to, w, to - from),
                WallSide.North => (x, y + from, w, to - from),
                WallSide.East => (x + w - to, y, to - from, l),
                _ => (x + from, y, to - from, l),
            };
            IsoDraw.FloorQuad(this, sx, sy, sw, sl, shade);
            IsoDraw.FloorOutline(this, sx, sy, sw, sl, new Color("#8C857A"), 1f);
        }

        // Handrails along both long sides.
        var rail = new Color("#5D4E3C");
        if (alongY)
        {
            Line(new[] { (x + 0.15, y), (x + 0.15, y + l) }, rail, 3f);
            Line(new[] { (x + w - 0.15, y), (x + w - 0.15, y + l) }, rail, 3f);
        }
        else
        {
            Line(new[] { (x, y + 0.15), (x + w, y + 0.15) }, rail, 3f);
            Line(new[] { (x, y + l - 0.15), (x + w, y + l - 0.15) }, rail, 3f);
        }

        // An arrow pointing up the flight, away from the door.
        var (cx, cy) = (x + (w / 2), y + (l / 2));
        var (dx, dy) = side switch
        {
            WallSide.South => (0.0, -1.0),
            WallSide.North => (0.0, 1.0),
            WallSide.East => (-1.0, 0.0),
            _ => (1.0, 0.0),
        };
        var half = depth * 0.3;
        var tip = (cx + (dx * half), cy + (dy * half));
        Line(new[] { (cx - (dx * half), cy - (dy * half)), tip }, new Color("#37474F"), 4f);
        Line(new[] { (tip.Item1 - (dx * 0.6) - (dy * 0.5), tip.Item2 - (dy * 0.6) + (dx * 0.5)), tip, (tip.Item1 - (dx * 0.6) + (dy * 0.5), tip.Item2 - (dy * 0.6) - (dx * 0.5)) }, new Color("#37474F"), 4f);
    }

    /// <summary>The two ends of a doorway, <paramref name="gap"/> tiles outside the wall (negative: inside).</summary>
    private static ((double X, double Y) A, (double X, double Y) B) DoorEnds(GridRect r, Doorway door, double gap) => door.Side switch
    {
        WallSide.North => ((r.X + door.Offset, r.Y - gap), (r.X + door.Offset + door.Width, r.Y - gap)),
        WallSide.South => ((r.X + door.Offset, r.Bottom + gap), (r.X + door.Offset + door.Width, r.Bottom + gap)),
        WallSide.West => ((r.X - gap, r.Y + door.Offset), (r.X - gap, r.Y + door.Offset + door.Width)),
        _ => ((r.Right + gap, r.Y + door.Offset), (r.Right + gap, r.Y + door.Offset + door.Width)),
    };

    private void Line((double X, double Y)[] points, Color colour, float width = 3f)
    {
        var screen = points.Select(p => IsoDraw.ToScreen(p.X, p.Y)).ToArray();
        DrawPolyline(screen, new Color(colour, 0.75f), width, true);
    }
}
