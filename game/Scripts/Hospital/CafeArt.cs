using System;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>What a vending machine sells: each kind has its own cabinet colour, front and side panels.</summary>
public enum Vending
{
    Snacks,
    Drinks,
    HotDrinks,
}

/// <summary>
/// Drawings for the cafeteria and vending point furniture: three kinds of vending machine (snacks
/// behind glass, a lit fridge of cold drinks, a hot drinks machine with a cup bay), a water machine
/// with its blue bottle, a serving counter with a glass display, tray rail and till, and cafe tables.
/// Machines face into the room, away from the wall they stand against.
/// </summary>
public static class CafeArt
{
    private static readonly Color[] Snacks =
    {
        new("#E53935"), new("#FDD835"), new("#43A047"), new("#1E88E5"), new("#FB8C00"), new("#8E24AA"), new("#F5F5F5"), new("#6D4C41"),
    };

    private static readonly Color Dark = new("#0E1419");
    private static readonly Color Steel = new("#C3CCD3");

    public static bool Draws(string itemId) => itemId is "vending_machine" or "water_cooler" or "food_counter" or "cafe_table";

    /// <summary>Adds the props for one item; <paramref name="addProp"/> places a drawing sorted by its anchor.</summary>
    public static void Add(string itemId, CareSpace room, PlacedFurniture item, float height, Color colour, Action<Vector2, Action<CanvasItem>> addProp)
    {
        var (x, y, w, l) = (item.X, item.Y, item.Width, item.Length);
        var seed = (int)Math.Abs((Math.Floor(x * 7) * 31) + (Math.Floor(y * 7) * 17));
        switch (itemId)
        {
            case "vending_machine":
            {
                // A row of machines takes turns: snacks, cold drinks, hot drinks.
                var front = FrontOf(room, x, y, w, l);
                var index = room.Furniture.Where(f => f.ItemId == itemId).OrderBy(f => f.X).ThenBy(f => f.Y).ToList().IndexOf(item);
                var kind = (Vending)(Math.Max(0, index) % 3);
                addProp(IsoDraw.ToScreen(x + (w / 2), y + (l / 2)), c => VendingMachine(c, x, y, w, l, height, front, kind, seed));
                break;
            }

            case "water_cooler":
                addProp(IsoDraw.ToScreen(x + (w / 2), y + (l / 2)), c => WaterCooler(c, x, y, w, l, height, FrontOf(room, x, y, w, l)));
                break;

            case "food_counter":
            {
                // One-tile slices so each sorts on its own, like other long furniture.
                var alongX = w >= l;
                var length = alongX ? w : l;
                var slices = Math.Max(1, (int)Math.Ceiling(length));
                var step = length / slices;
                var front = FrontOf(room, x, y, w, l);
                for (var i = 0; i < slices; i++)
                {
                    var (sx, sy, sw, sl) = alongX ? (x + (i * step), y, step, l) : (x, y + (i * step), w, step);
                    var index = i;
                    var joined = Sides.None;
                    if (i > 0)
                    {
                        joined |= alongX ? Sides.West : Sides.North;
                    }

                    if (i < slices - 1)
                    {
                        joined |= alongX ? Sides.East : Sides.South;
                    }

                    addProp(IsoDraw.ToScreen(sx + (sw / 2), sy + (sl / 2)), c => CounterSlice(c, sx, sy, sw, sl, height, colour, index, slices, seed, front, joined));
                }

                break;
            }

            case "cafe_table":
                addProp(IsoDraw.ToScreen(x + (w / 2), y + (l / 2)), c => Table(c, x, y, w, l, height, colour, seed));
                break;
        }
    }

    /// <summary>Which way an item against a wall faces: away from the wall along its long side.</summary>
    public static (int X, int Y) FrontOf(CareSpace room, double x, double y, double w, double l)
    {
        var r = room.Rect;
        if (w >= l)
        {
            return y - r.Y < r.Bottom - (y + l) ? (0, 1) : (0, -1);
        }

        return x - r.X < r.Right - (x + w) ? (1, 0) : (-1, 0);
    }

    private static void Shadow(CanvasItem c, double x, double y, double w, double l) =>
        c.DrawColoredPolygon(new[] { IsoDraw.ToScreen(x - 0.05, y - 0.05) + new Vector2(2, 2), IsoDraw.ToScreen(x + w + 0.1, y - 0.05) + new Vector2(2, 2), IsoDraw.ToScreen(x + w + 0.1, y + l + 0.1) + new Vector2(2, 2), IsoDraw.ToScreen(x - 0.05, y + l + 0.1) + new Vector2(2, 2) }, new Color(0, 0, 0, 0.13f));

    // ------------------------------------------------------------------ vending machines

    public static Color CabinetColour(Vending kind) => kind switch
    {
        Vending.Snacks => new Color("#C8402F"),
        Vending.Drinks => new Color("#1F6FC4"),
        _ => new Color("#4A342B"),
    };

    public static void VendingMachine(CanvasItem c, double x, double y, double w, double l, float height, (int X, int Y) front, Vending kind, int seed)
    {
        // Drawn slimmer than its floor space and a head taller than a person, like the real thing:
        // narrower across the front, and pulled in from the wall behind it.
        const double slim = 0.13, back = 0.3;
        (x, y, w, l) = front.X == 0
            ? (x + slim, front.Y > 0 ? y + back : y, w - (2 * slim), l - back)
            : (front.X > 0 ? x + back : x, y + slim, w - back, l - (2 * slim));
        height *= 1.25f;
        var body = CabinetColour(kind);
        Shadow(c, x, y, w, l);
        IsoDraw.Prism(c, x, y, w, l, height, body.Lightened(0.14f), body, skirting: new Color("#1B2228"), skirtingHeight: 3, rim: body.Lightened(0.35f));

        // Vent slots on the top.
        var up = new Vector2(0, -height);
        for (var i = 0; i < 3; i++)
        {
            var t = 0.3 + (i * 0.2);
            c.DrawLine(IsoDraw.ToScreen(x + (w * t), y + (l * 0.2)) + up, IsoDraw.ToScreen(x + (w * t), y + (l * 0.8)) + up, body.Darkened(0.25f), 1f, true);
        }

        var frontSide = IsoFace.SideOf(front);
        foreach (var face in IsoFace.Of(x, y, w, l, height))
        {
            var isFront = face.Side == frontSide;
            face.Draw(c, (length, h) =>
            {
                if (!isFront)
                {
                    SidePanel(c, kind, length, h);
                    return;
                }

                switch (kind)
                {
                    case Vending.Snacks:
                        SnackFront(c, length, h, seed);
                        break;
                    case Vending.Drinks:
                        DrinksFront(c, length, h, seed);
                        break;
                    default:
                        HotDrinksFront(c, length, h);
                        break;
                }
            });
        }
    }

    private static void Header(CanvasItem c, float length, string text, Color background, Color ink)
    {
        Flat.Round(c, 2.5f, 2.5f, length - 5, 8f, 1.5f, Dark);
        Flat.Round(c, 3.2f, 3.2f, length - 6.4f, 6.6f, 1.2f, background);
        Flat.Text(c, text, length / 2, 8.6f, 6, ink);
    }

    private static void Sheen(CanvasItem c, float x, float y, float w, float h) =>
        c.DrawColoredPolygon(new[] { new Vector2(x + (w * 0.32f), y), new Vector2(x + (w * 0.52f), y), new Vector2(x + (w * 0.24f), y + h), new Vector2(x + (w * 0.04f), y + h) }, new Color(1, 1, 1, 0.09f));

    /// <summary>The payment panel: display, keypad, contactless card reader, coin slot and coin return.</summary>
    private static void Payment(CanvasItem c, float x, float y, float w, float h, bool keypad)
    {
        Flat.Round(c, x, y, w, h, 1.5f, new Color("#D7DEE3"));
        Flat.Rect(c, x + 1.5f, y + 1.8f, w - 3, 4.6f, new Color("#0D1B14"));
        Flat.Rect(c, x + 2.5f, y + 3.3f, w - 7, 1f, new Color("#5CF08A"));
        var at = y + 8f;
        if (keypad)
        {
            var key = (w - 3.4f) / 3;
            for (var row = 0; row < 4; row++)
            {
                for (var col = 0; col < 3; col++)
                {
                    Flat.Rect(c, x + 1.9f + (col * key), at + (row * 2.3f), key - 0.7f, 1.6f, new Color("#4A5560"));
                }
            }

            at += 10.4f;
        }

        // Contactless reader with its little waves, then the coin slot and the return cup.
        Flat.Round(c, x + 1.5f, at, w - 3, 6.4f, 1.2f, new Color("#1B2228"));
        for (var i = 1; i <= 3; i++)
        {
            c.DrawArc(new Vector2(x + (w / 2) - 2.2f, at + 3.2f), i * 1.15f, -0.85f, 0.85f, 6, new Color("#6EC6FF"), 0.6f, true);
        }

        at += 7.6f;
        if (at + 4 < y + h)
        {
            Flat.Round(c, x + (w / 2) - 2.2f, at, 4.4f, 4f, 1f, new Color("#AEB8C1"));
            Flat.Rect(c, x + (w / 2) - 0.45f, at + 0.8f, 0.9f, 2.4f, Dark);
        }

        if (at + 10 < y + h)
        {
            Flat.Round(c, x + 2f, y + h - 5f, w - 4, 3.6f, 1.2f, new Color("#55616B"));
        }
    }

    private static void SnackFront(CanvasItem c, float length, float h, int seed)
    {
        Header(c, length, "SNACKS", new Color("#FFF3D6"), new Color("#8E2A1C"));
        var (wx, wy) = (4f, 13f);
        var (ww, wh) = ((length * 0.68f) - wx, h - wy - 13f);
        Flat.Rect(c, wx - 1, wy - 1, ww + 2, wh + 2, Dark);
        Flat.Rect(c, wx, wy, ww, wh, new Color("#27353F"));
        Flat.Rect(c, wx, wy, ww, 1.2f, new Color(0.95f, 0.98f, 1f, 0.75f));
        const int rows = 5;
        var rh = wh / rows;
        for (var row = 0; row < rows; row++)
        {
            var shelf = wy + ((row + 1) * rh) - 0.9f;
            var cols = row < 2 ? 5 : 6;
            var cw = ww / cols;
            for (var k = 0; k < cols; k++)
            {
                var colour = Snacks[(seed + (row * 5) + (k * 3)) % Snacks.Length];
                var cx = wx + ((k + 0.5f) * cw);
                switch (row)
                {
                    case 0:
                    case 1:
                        // Bags of crisps: a pillow pack with a crimped top and a round badge.
                        Flat.Round(c, cx - (cw * 0.38f), shelf - (rh * 0.84f), cw * 0.76f, rh * 0.84f, 1f, colour);
                        Flat.Rect(c, cx - (cw * 0.38f), shelf - (rh * 0.84f), cw * 0.76f, 0.9f, colour.Lightened(0.45f));
                        c.DrawCircle(new Vector2(cx, shelf - (rh * 0.38f)), cw * 0.17f, colour.Lightened(0.6f));
                        break;
                    case 2:
                        // Chocolate bars lying flat.
                        Flat.Rect(c, cx - (cw * 0.4f), shelf - (rh * 0.46f), cw * 0.8f, rh * 0.46f, colour.Darkened(0.1f));
                        Flat.Rect(c, cx - (cw * 0.4f), shelf - (rh * 0.34f), cw * 0.8f, rh * 0.14f, colour.Lightened(0.5f));
                        break;
                    case 3:
                        Bottle(c, cx, shelf, cw * 0.46f, rh * 0.9f, colour.Lightened(0.15f), Colors.White);
                        break;
                    default:
                        // Cans.
                        Flat.Round(c, cx - (cw * 0.3f), shelf - (rh * 0.66f), cw * 0.6f, rh * 0.66f, 0.8f, colour);
                        Flat.Rect(c, cx - (cw * 0.3f), shelf - (rh * 0.66f), cw * 0.6f, 0.8f, Steel);
                        break;
                }
            }

            // The shelf edge with its price tickets.
            Flat.Rect(c, wx, shelf, ww, 0.9f, new Color("#93A3AF"));
            for (var k = 0; k < cols; k++)
            {
                Flat.Rect(c, wx + ((k + 0.5f) * cw) - 0.9f, shelf, 1.8f, 0.9f, new Color("#FFF59D"));
            }
        }

        Sheen(c, wx, wy, ww, wh);

        // The collection flap.
        Flat.Round(c, wx - 1, h - 11f, ww + 2, 7f, 1f, Dark);
        Flat.Rect(c, wx + 1.5f, h - 9.8f, ww - 3, 4.4f, new Color("#27323B"));
        Flat.Rect(c, wx + 1.5f, h - 9.8f, ww - 3, 0.8f, new Color("#55616B"));
        Payment(c, (length * 0.68f) + 2.5f, wy - 1, length - (length * 0.68f) - 5.5f, h - wy - 3f, true);
    }

    private static void Bottle(CanvasItem c, float cx, float foot, float width, float height, Color body, Color cap)
    {
        Flat.Round(c, cx - (width / 2), foot - (height * 0.8f), width, height * 0.8f, width * 0.3f, body);
        Flat.Rect(c, cx - (width * 0.22f), foot - (height * 0.93f), width * 0.44f, height * 0.16f, body);
        Flat.Rect(c, cx - (width * 0.27f), foot - height, width * 0.54f, height * 0.1f, cap);
        Flat.Rect(c, cx - (width / 2), foot - (height * 0.52f), width, height * 0.22f, new Color(1, 1, 1, 0.82f));
    }

    private static void DrinksFront(CanvasItem c, float length, float h, int seed)
    {
        Header(c, length, "DRINKS", new Color("#E3F2FD"), new Color("#0D47A1"));
        var (dx, dy) = (4f, 13f);
        var (dw, dh) = ((length * 0.68f) - dx, h - dy - 5f);
        Flat.Rect(c, dx - 1, dy - 1, dw + 2, dh + 2, Dark);
        Flat.Rect(c, dx, dy, dw, dh, new Color("#D6ECF8"));
        Flat.Rect(c, dx, dy, dw, 1.6f, Colors.White);
        var drinks = new[]
        {
            (Body: new Color("#8FD3F4"), Cap: new Color("#1E88E5")), (Body: new Color("#3B2A20"), Cap: new Color("#E53935")),
            (Body: new Color("#FB8C00"), Cap: new Color("#F5F5F5")), (Body: new Color("#7CB342"), Cap: new Color("#2E7D32")),
            (Body: new Color("#F4E04D"), Cap: new Color("#F5F5F5")), (Body: new Color("#B71C1C"), Cap: new Color("#F5F5F5")),
        };
        const int rows = 5;
        var rh = (dh - 7f) / rows;
        for (var row = 0; row < rows; row++)
        {
            var shelf = dy + ((row + 1) * rh) - 0.8f;
            const int cols = 7;
            var cw = dw / cols;
            for (var k = 0; k < cols; k++)
            {
                var drink = drinks[(row + ((k + seed) / 3)) % drinks.Length];
                var cx = dx + ((k + 0.5f) * cw);
                if (row == rows - 1)
                {
                    Flat.Round(c, cx - (cw * 0.34f), shelf - (rh * 0.7f), cw * 0.68f, rh * 0.7f, 0.8f, drink.Body);
                    Flat.Rect(c, cx - (cw * 0.34f), shelf - (rh * 0.7f), cw * 0.68f, 0.8f, Steel);
                }
                else
                {
                    Bottle(c, cx, shelf, cw * 0.62f, rh * 0.94f, drink.Body, drink.Cap);
                }
            }

            Flat.Rect(c, dx, shelf, dw, 0.8f, new Color("#9FB4C2"));
        }

        // The delivery bin at the foot of the door, and the door handle.
        Flat.Rect(c, dx, dy + dh - 6.4f, dw, 6.4f, Dark);
        Flat.Rect(c, dx + 2, dy + dh - 5.2f, dw - 4, 3.8f, new Color("#27323B"));
        Flat.Rect(c, dx + dw - 2.4f, dy + (dh * 0.22f), 1.3f, dh * 0.4f, Steel);
        Sheen(c, dx, dy, dw, dh - 6.4f);
        Payment(c, (length * 0.68f) + 2.5f, dy - 1, length - (length * 0.68f) - 5.5f, h - dy - 3f, true);
    }

    private static void Cup(CanvasItem c, float cx, float cy, float s, bool steam)
    {
        var white = new Color("#FBFCFD");
        Flat.Rect(c, cx - (s * 0.75f), cy + (s * 0.52f), s * 1.5f, s * 0.13f, white);
        c.DrawArc(new Vector2(cx + (s * 0.55f), cy + (s * 0.05f)), s * 0.24f, -1.5f, 1.5f, 8, white, s * 0.12f, true);
        Flat.Round(c, cx - (s * 0.5f), cy - (s * 0.35f), s, s * 0.85f, s * 0.22f, white);
        Flat.Rect(c, cx - (s * 0.42f), cy - (s * 0.35f), s * 0.84f, s * 0.14f, new Color("#5D4037"));
        if (steam)
        {
            foreach (var off in new[] { -0.2f, 0.2f })
            {
                c.DrawPolyline(new[]
                {
                    new Vector2(cx + (s * off), cy - (s * 0.5f)), new Vector2(cx + (s * (off + 0.12f)), cy - (s * 0.7f)),
                    new Vector2(cx + (s * (off - 0.1f)), cy - (s * 0.9f)), new Vector2(cx + (s * (off + 0.05f)), cy - (s * 1.1f)),
                }, new Color(1, 1, 1, 0.75f), Math.Max(0.6f, s * 0.07f), true);
            }
        }
    }

    private static void HotDrinksFront(CanvasItem c, float length, float h)
    {
        Header(c, length, "HOT DRINKS", new Color("#FFE0B2"), new Color("#3E2723"));

        // A picture of a steaming cup.
        var (px, py, pw, ph) = (4f, 13f, (length * 0.47f) - 4f, 19f);
        Flat.Round(c, px - 0.8f, py - 0.8f, pw + 1.6f, ph + 1.6f, 1.8f, Dark);
        Flat.Round(c, px, py, pw, ph, 1.2f, new Color("#8A5A44"));
        c.DrawCircle(new Vector2(px + (pw / 2), py + (ph * 0.58f)), ph * 0.42f, new Color("#A9735A"));
        Cup(c, px + (pw / 2) - 0.6f, py + (ph * 0.6f), 9f, true);

        // The choices: a lit button for each drink.
        var (bx, bw) = ((length * 0.47f) + 2.5f, length - (length * 0.47f) - 6.5f);
        Flat.Round(c, bx - 0.8f, py - 0.8f, bw + 1.6f, ph + 1.6f, 1.8f, new Color("#2B201B"));
        var dots = new[] { "#3E2723", "#6D4C41", "#A1887F", "#D7CCC8", "#5D4037", "#7CB342", "#4E342E", "#FFF8E1" };
        for (var i = 0; i < 8; i++)
        {
            var (col, row) = (i % 2, i / 2);
            var (kx, ky, kw) = (bx + 0.8f + (col * (bw / 2)), py + 0.6f + (row * (ph / 4f)), (bw / 2) - 1.6f);
            Flat.Round(c, kx, ky, kw, (ph / 4f) - 1.2f, 1f, new Color("#FFF3E0"));
            c.DrawCircle(new Vector2(kx + 2.1f, ky + ((ph / 4f) - 1.2f) / 2), 1.2f, new Color(dots[i]));
            Flat.Rect(c, kx + 4.2f, ky + (((ph / 4f) - 1.2f) / 2) - 0.4f, kw - 5.6f, 0.8f, new Color("#8D6E63"));
        }

        // The cup bay: spout, a cup filling, and the drip tray.
        var (ax, ay, aw, ah) = (length * 0.2f, 34.5f, length * 0.42f, h - 34.5f - 4.5f);
        Flat.Round(c, ax, ay, aw, ah, 1.5f, Dark);
        Flat.Rect(c, ax + 1, ay + 1, aw - 2, ah - 2, new Color("#232D35"));
        Flat.Rect(c, ax + (aw / 2) - 1.2f, ay, 2.4f, 3.2f, Steel);
        Flat.Round(c, ax + (aw / 2) - 2.6f, ay + ah - 7.6f, 5.2f, 5.6f, 1f, new Color("#FBFCFD"));
        Flat.Rect(c, ax + (aw / 2) - 2.6f, ay + ah - 6f, 5.2f, 1.2f, new Color("#C8402F"));
        Flat.Rect(c, ax + 1, ay + ah - 2f, aw - 2, 1.4f, new Color("#7D8A95"));
        Payment(c, (length * 0.66f) + 1f, ay, length - (length * 0.66f) - 5f, ah, false);

        // Cup sizes on the left of the bay.
        Flat.Round(c, 4f, ay + 1.5f, (length * 0.2f) - 6f, ah - 3, 1.2f, new Color("#6B4A3D"));
        Cup(c, 4f + (((length * 0.2f) - 6f) / 2) - 0.4f, ay + (ah / 2) + 0.5f, 4.6f, false);
    }

    /// <summary>The cabinet's other sides: the name, a pale swoosh and a big picture of what's inside.</summary>
    private static void SidePanel(CanvasItem c, Vending kind, float length, float h)
    {
        c.DrawColoredPolygon(new[] { new Vector2(0, h * 0.62f), new Vector2(length, h * 0.3f), new Vector2(length, h * 0.52f), new Vector2(0, h * 0.84f) }, new Color(1, 1, 1, 0.16f));
        c.DrawColoredPolygon(new[] { new Vector2(0, h * 0.87f), new Vector2(length, h * 0.55f), new Vector2(length, h * 0.6f), new Vector2(0, h * 0.92f) }, new Color(1, 1, 1, 0.1f));
        Flat.Text(c, kind switch { Vending.Snacks => "SNACKS", Vending.Drinks => "DRINKS", _ => "HOT DRINKS" }, length / 2, 10f, 6, new Color(1, 1, 1, 0.95f));
        var (cx, cy) = (length / 2, h * 0.52f);
        switch (kind)
        {
            case Vending.Snacks:
                Flat.Round(c, cx - 6.5f, cy - 8.5f, 13, 17, 2f, new Color("#FFD54F"));
                Flat.Rect(c, cx - 6.5f, cy - 8.5f, 13, 2f, new Color("#FFF3C4"));
                Flat.Rect(c, cx - 6.5f, cy + 6.5f, 13, 2f, new Color("#F2B01E"));
                c.DrawCircle(new Vector2(cx, cy), 4f, new Color("#E53935"));
                c.DrawCircle(new Vector2(cx, cy), 2f, new Color("#FFF3C4"));
                break;

            case Vending.Drinks:
                Flat.Round(c, cx - 4.2f, cy - 5.5f, 8.4f, 15f, 3f, new Color("#E3F2FD"));
                Flat.Rect(c, cx - 1.8f, cy - 9.5f, 3.6f, 5f, new Color("#E3F2FD"));
                Flat.Rect(c, cx - 2.2f, cy - 11f, 4.4f, 2f, new Color("#0D47A1"));
                Flat.Rect(c, cx - 4.2f, cy - 0.5f, 8.4f, 4.6f, new Color("#64B5F6"));
                foreach (var (ox, oy) in new[] { (-9f, 4f), (9.5f, -3f), (8f, 7f) })
                {
                    c.DrawCircle(new Vector2(cx + ox, cy + oy), 1.5f, new Color(1, 1, 1, 0.55f));
                }

                break;

            default:
                c.DrawCircle(new Vector2(cx, cy + 1), 10.5f, new Color("#6B4A3D"));
                Cup(c, cx - 0.8f, cy + 2.5f, 10.5f, true);
                break;
        }

        // Louvres at the foot.
        for (var i = 0; i < 3; i++)
        {
            Flat.Rect(c, length * 0.2f, h - 9f + (i * 2f), length * 0.6f, 0.9f, new Color(0, 0, 0, 0.3f));
        }
    }

    // ------------------------------------------------------------------ water machine

    public static void WaterCooler(CanvasItem c, double x, double y, double w, double l, float height, (int X, int Y) front)
    {
        var body = height * 0.68f;
        var white = new Color("#F4F7F9");
        Shadow(c, x, y, w, l);
        IsoDraw.Prism(c, x, y, w, l, body, white, new Color("#DFE6EB"), skirting: new Color("#8C99A5"), skirtingHeight: 2, rim: Colors.White);
        var frontSide = IsoFace.SideOf(front);
        foreach (var face in IsoFace.Of(x, y, w, l, body))
        {
            if (face.Side != frontSide)
            {
                continue;
            }

            face.Draw(c, (length, h) =>
            {
                // The recess with a cold (blue) and a hot (red) tap over the drip tray, and a door below.
                Flat.Round(c, length * 0.14f, h * 0.14f, length * 0.72f, h * 0.36f, 1.2f, new Color("#9FB0BC"));
                Flat.Rect(c, length * 0.14f, h * 0.44f, length * 0.72f, 1.6f, new Color("#55616B"));
                foreach (var (u, tap) in new[] { (0.33f, "#1E88E5"), (0.67f, "#E53935") })
                {
                    Flat.Rect(c, (length * u) - 1.2f, h * 0.16f, 2.4f, 2.6f, white);
                    Flat.Rect(c, (length * u) - 1.6f, (h * 0.16f) + 2.6f, 3.2f, 1.5f, new Color(tap));
                }

                Flat.Round(c, length * 0.2f, h * 0.58f, length * 0.6f, h * 0.3f, 1f, new Color("#E9EEF2"));
                c.DrawCircle(new Vector2(length * 0.72f, h * 0.08f), 0.9f, new Color("#3EE07A"));
            });
        }

        // The upturned bottle: collar, neck and the big blue bottle with its ribs.
        var top = IsoDraw.ToScreen(x + (w / 2), y + (l / 2)) + new Vector2(0, -body);
        var r = (float)(Math.Min(w, l) * 14);
        var tall = height - body;
        c.DrawColoredPolygon(IsoDraw.FloorEllipse(top, r + 1.5f, 14), new Color("#B9C6CF"));
        var water = new Color(0.42f, 0.7f, 0.95f, 0.88f);
        Figures.RoundedRect(c, new Rect2(top + new Vector2(-r * 0.4f, -3.5f), new Vector2(r * 0.8f, 4f)), 1f, water.Darkened(0.1f));
        Figures.RoundedRect(c, new Rect2(top + new Vector2(-r, -tall - 1), new Vector2(2 * r, tall - 2)), r * 0.45f, water);
        c.DrawLine(top + new Vector2(-r, -tall * 0.5f), top + new Vector2(r, -tall * 0.5f), water.Darkened(0.2f), 0.8f);
        c.DrawLine(top + new Vector2(-r, -tall * 0.7f), top + new Vector2(r, -tall * 0.7f), water.Darkened(0.2f), 0.8f);
        c.DrawLine(top + new Vector2(-r * 0.6f, -tall * 0.92f), top + new Vector2(-r * 0.6f, -5), new Color(1, 1, 1, 0.55f), 1.2f, true);
        foreach (var (ox, oy) in new[] { (0.2f, 0.45f), (0.45f, 0.75f), (-0.1f, 0.82f) })
        {
            c.DrawCircle(top + new Vector2(r * ox, -tall * oy), 0.8f, new Color(1, 1, 1, 0.7f));
        }

        // A tube of paper cups on the side.
        var tube = top + new Vector2(-r - 3.5f, 2);
        c.DrawLine(tube, tube + new Vector2(0, 11), new Color("#FBFCFD"), 3.2f);
        for (var i = 0; i < 3; i++)
        {
            c.DrawLine(tube + new Vector2(-1.8f, 4 + (i * 2.4f)), tube + new Vector2(1.8f, 4 + (i * 2.4f)), new Color("#B9C6CF"), 0.6f);
        }
    }

    // ------------------------------------------------------------------ serving counter

    public static void CounterSlice(CanvasItem c, double x, double y, double w, double l, float height, Color colour, int index, int count, int seed, (int X, int Y) front, Sides joined)
    {
        var frontSide = IsoFace.SideOf(front);
        var teal = new Color("#2F7F86");
        var cream = new Color("#F3EEE4");
        if (index == 0)
        {
            Shadow(c, x, y, w, l);
        }

        // The tray rail on the customers' side, drawn first when it is behind the counter.
        var alongX = w >= l || (joined & (Sides.East | Sides.West)) != 0;
        var (rx, ry, rw, rl) = frontSide switch
        {
            Sides.South => (x, y + l, w, 0.34),
            Sides.North => (x, y - 0.34, w, 0.34),
            Sides.East => (x + w, y, 0.34, l),
            _ => (x - 0.34, y, 0.34, l),
        };
        var railInFront = IsoDraw.ToScreen(rx + (rw / 2), ry + (rl / 2)).Y > IsoDraw.ToScreen(x + (w / 2), y + (l / 2)).Y;
        void Rail()
        {
            var railJoin = joined & (alongX ? Sides.East | Sides.West : Sides.North | Sides.South);
            IsoDraw.Prism(c, rx, ry, rw, rl, 1.6f, Steel.Lightened(0.15f), Steel.Darkened(0.1f), railJoin | IsoFace.Opposite(frontSide), height - 8, rim: Colors.White);
            var (mx, my) = (rx + (rw / 2), ry + (rl / 2));
            c.DrawLine(alongX ? IsoDraw.ToScreen(rx, my) + new Vector2(0, -height + 6.2f) : IsoDraw.ToScreen(mx, ry) + new Vector2(0, -height + 6.2f),
                alongX ? IsoDraw.ToScreen(rx + rw, my) + new Vector2(0, -height + 6.2f) : IsoDraw.ToScreen(mx, ry + rl) + new Vector2(0, -height + 6.2f), Steel.Darkened(0.25f), 0.8f, true);
        }

        if (!railInFront)
        {
            Rail();
        }

        // The counter: a wood carcass, a teal front panel with a cream band, and a pale worktop.
        IsoDraw.Prism(c, x, y, w, l, height - 3, colour, colour.Darkened(0.16f), joined, skirting: new Color("#3A2F28"), skirtingHeight: 3);
        foreach (var face in IsoFace.Of(x, y, w, l, height - 3))
        {
            if (face.Side == frontSide)
            {
                face.Quad(c, 0, 3, 1, height - 3, teal);
                face.Quad(c, 0, height - 10, 1, height - 7.5f, cream);
                face.Line(c, 0, 3, 1, 3, teal.Darkened(0.35f));
            }
        }

        IsoDraw.Prism(c, x, y, w, l, 3, cream, cream.Darkened(0.14f), joined, height - 3, rim: Colors.White);
        if (railInFront)
        {
            Rail();
        }

        Vector2 Top(double u, double v, float lift = 0) => IsoDraw.ToScreen(x + (w * u), y + (l * v)) + new Vector2(0, -height - lift);
        if (index == count / 2)
        {
            // The till: a screen on a stalk, the cash drawer and a card reader.
            IsoDraw.Prism(c, x + (w * 0.25), y + (l * 0.3), w * 0.5, l * 0.4, 4, new Color("#3B4650"), new Color("#2A333B"), lift: height, rim: new Color("#66737E"));
            var stalk = Top(0.5, 0.5, 4);
            c.DrawLine(stalk, stalk + new Vector2(0, -6), new Color("#2A333B"), 2f);
            Figures.RoundedRect(c, new Rect2(stalk + new Vector2(-6.5f, -15), new Vector2(13, 9.5f)), 1.5f, new Color("#2A333B"));
            c.DrawRect(new Rect2(stalk + new Vector2(-5.3f, -13.8f), new Vector2(10.6f, 7.1f)), new Color("#BFE3F2"));
            c.DrawRect(new Rect2(stalk + new Vector2(-4.3f, -12.8f), new Vector2(5, 1.4f)), new Color("#2F7F86"));
            c.DrawRect(new Rect2(stalk + new Vector2(-4.3f, -10.4f), new Vector2(8, 0.9f)), new Color("#6B8CA0"));
            var reader = Top(0.8, 0.75);
            Figures.RoundedRect(c, new Rect2(reader + new Vector2(-2.2f, -5.5f), new Vector2(4.4f, 5.5f)), 1f, new Color("#1B2228"));
            c.DrawRect(new Rect2(reader + new Vector2(-1.4f, -4.7f), new Vector2(2.8f, 1.8f)), new Color("#7FD7C4"));
            return;
        }

        if (index == count - 1 && count > 3)
        {
            // A stack of trays and the cutlery pots at the start of the line.
            for (var i = 0; i < 5; i++)
            {
                IsoDraw.Prism(c, x + (w * 0.12), y + (l * 0.18), w * 0.76, l * 0.45, 1.3f, new Color(i % 2 == 0 ? "#3E7CB1" : "#356B9A"), new Color("#2B5A84"), lift: height + (i * 1.3f));
            }

            foreach (var (u, pot) in new[] { (0.3, "#E53935"), (0.52, "#FDD835"), (0.74, "#43A047") })
            {
                var p = Top(u, 0.82);
                c.DrawRect(new Rect2(p + new Vector2(-1.8f, -5), new Vector2(3.6f, 5)), Steel);
                c.DrawRect(new Rect2(p + new Vector2(-1.8f, -5), new Vector2(3.6f, 1.2f)), new Color(pot));
                c.DrawLine(p + new Vector2(-0.6f, -5), p + new Vector2(-1.2f, -8.5f), new Color("#E9EEF2"), 0.8f);
                c.DrawLine(p + new Vector2(0.7f, -5), p + new Vector2(1.1f, -8f), new Color("#E9EEF2"), 0.8f);
            }

            return;
        }

        // Food under a glass sneeze guard: sandwiches, cakes or the hot dishes.
        var (ix, iy, iw, il) = (x + (w * 0.06), y + (l * 0.14), w * 0.88, l * 0.72);
        var guardJoin = joined & (alongX ? Sides.East | Sides.West : Sides.North | Sides.South);
        switch ((index + seed) % 3)
        {
            case 0:
                foreach (var (u, v, bread) in new[] { (0.3, 0.32, "#F3E2B8"), (0.7, 0.32, "#C9A46A"), (0.3, 0.68, "#C9A46A"), (0.7, 0.68, "#F3E2B8") })
                {
                    var p = Top(u, v);
                    c.DrawColoredPolygon(new[] { p + new Vector2(-4.5f, 0), p + new Vector2(4.5f, 0), p + new Vector2(0, -5.5f) }, new Color(bread));
                    c.DrawLine(p + new Vector2(-3f, -1.2f), p + new Vector2(3f, -1.2f), new Color("#7CB342"), 1f);
                    c.DrawLine(p + new Vector2(-2.2f, -2.4f), p + new Vector2(2.2f, -2.4f), new Color("#E57373"), 0.9f);
                }

                break;

            case 1:
                foreach (var (u, v, icing) in new[] { (0.26, 0.35, "#F48FB1"), (0.55, 0.3, "#6D4C41"), (0.8, 0.4, "#FFD54F"), (0.4, 0.7, "#FFFFFF"), (0.7, 0.72, "#F48FB1") })
                {
                    var p = Top(u, v);
                    c.DrawColoredPolygon(IsoDraw.FloorEllipse(p, 4f, 12), new Color("#FBFCFD"));
                    c.DrawRect(new Rect2(p + new Vector2(-2.3f, -2.6f), new Vector2(4.6f, 2.6f)), new Color("#C9A46A"));
                    c.DrawCircle(p + new Vector2(0, -3f), 2.5f, new Color(icing));
                    c.DrawCircle(p + new Vector2(0.3f, -5.2f), 0.8f, new Color("#D32F2F"));
                }

                break;

            default:
                c.DrawColoredPolygon(new[] { Top(0.1, 0.2, 0.3f), Top(0.9, 0.2, 0.3f), Top(0.9, 0.8, 0.3f), Top(0.1, 0.8, 0.3f) }, new Color("#AEB8C1"));
                var across = alongX;
                foreach (var (t, food) in new[] { (0.14, "#F2A03A"), (0.42, "#F4E04D"), (0.7, "#8BC34A") })
                {
                    var (u0, v0, u1, v1) = across ? (t, 0.26, t + 0.2, 0.74) : (0.26, t, 0.74, t + 0.2);
                    c.DrawColoredPolygon(new[] { Top(u0, v0, 0.5f), Top(u1, v0, 0.5f), Top(u1, v1, 0.5f), Top(u0, v1, 0.5f) }, new Color(food));
                    var steam = Top((u0 + u1) / 2, (v0 + v1) / 2, 2);
                    c.DrawPolyline(new[] { steam, steam + new Vector2(1.2f, -2.5f), steam + new Vector2(-0.8f, -5f), steam + new Vector2(0.6f, -7.5f) }, new Color(1, 1, 1, 0.5f), 0.8f, true);
                }

                break;
        }

        IsoDraw.Prism(c, ix, iy, iw, il, 11, new Color(0.85f, 0.95f, 1f, 0.14f), new Color(0.72f, 0.88f, 0.96f, 0.26f), guardJoin, height, rim: new Color(1, 1, 1, 0.85f));
        foreach (var (px, py) in new[] { (ix, iy), (ix + iw, iy), (ix + iw, iy + il), (ix, iy + il) })
        {
            // Steel corner posts only at the ends of the whole guard.
            var end = alongX ? (px < x + 0.2 && (joined & Sides.West) == 0) || (px > x + w - 0.2 && (joined & Sides.East) == 0)
                : (py < y + 0.2 && (joined & Sides.North) == 0) || (py > y + l - 0.2 && (joined & Sides.South) == 0);
            if (end)
            {
                var p = IsoDraw.ToScreen(px, py) + new Vector2(0, -height);
                c.DrawLine(p, p + new Vector2(0, -11), Steel, 1.2f);
            }
        }
    }

    // ------------------------------------------------------------------ cafe table

    public static void Table(CanvasItem c, double x, double y, double w, double l, float height, Color colour, int seed)
    {
        var (cx, cy) = (x + (w / 2), y + (l / 2));
        var foot = IsoDraw.ToScreen(cx, cy);
        c.DrawColoredPolygon(IsoDraw.FloorEllipse(foot + new Vector2(1.5f, 1.5f), (float)(w * 17), 18), new Color(0, 0, 0, 0.12f));

        // A cross foot and a steel column.
        var legColour = new Color("#4A535B");
        c.DrawLine(IsoDraw.ToScreen(cx - (w * 0.32), cy) + new Vector2(0, -1), IsoDraw.ToScreen(cx + (w * 0.32), cy) + new Vector2(0, -1), legColour, 2.4f, true);
        c.DrawLine(IsoDraw.ToScreen(cx, cy - (l * 0.32)) + new Vector2(0, -1), IsoDraw.ToScreen(cx, cy + (l * 0.32)) + new Vector2(0, -1), legColour, 2.4f, true);
        c.DrawLine(foot + new Vector2(0, -1), foot + new Vector2(0, -height + 3), new Color("#8C979F"), 3f);
        c.DrawLine(foot + new Vector2(0.9f, -1), foot + new Vector2(0.9f, -height + 3), new Color("#B9C3CB"), 0.9f);

        // The top: a square with rounded corners and a darker edge band.
        const double cut = 0.16;
        Vector2[] Shape(float lift) => new[]
        {
            (x + cut, y), (x + w - cut, y), (x + w, y + cut), (x + w, y + l - cut), (x + w - cut, y + l), (x + cut, y + l), (x, y + l - cut), (x, y + cut),
        }.Select(p => IsoDraw.ToScreen(p.Item1, p.Item2) + new Vector2(0, -lift)).ToArray();
        for (var i = 0; i <= 3; i++)
        {
            c.DrawColoredPolygon(Shape(height - 3 + i), i == 3 ? colour.Lightened(0.12f) : colour.Darkened(0.3f - (i * 0.05f)));
        }

        var rimShape = Shape(height);
        c.DrawPolyline(rimShape.Append(rimShape[0]).ToArray(), colour.Lightened(0.35f), 0.8f, true);

        // A napkin holder and the sauces in the middle; now and then a tray somebody has left.
        var mid = foot + new Vector2(0, -height);
        c.DrawRect(new Rect2(mid + new Vector2(-2.6f, -4.2f), new Vector2(5.2f, 4.2f)), Steel);
        c.DrawRect(new Rect2(mid + new Vector2(-1.9f, -5.2f), new Vector2(3.8f, 3.4f)), Colors.White);
        c.DrawRect(new Rect2(mid + new Vector2(4f, -5f), new Vector2(1.8f, 5f)), new Color("#D32F2F"));
        c.DrawRect(new Rect2(mid + new Vector2(6.4f, -4.4f), new Vector2(1.8f, 4.4f)), new Color("#F9C22E"));
        if (seed % 3 == 0)
        {
            IsoDraw.Prism(c, cx - (w * 0.42), cy + (l * 0.08), w * 0.5, l * 0.34, 1, new Color("#3E7CB1"), new Color("#2B5A84"), lift: height);
            var plate = IsoDraw.ToScreen(cx - (w * 0.2), cy + (l * 0.25)) + new Vector2(0, -height - 1);
            c.DrawColoredPolygon(IsoDraw.FloorEllipse(plate, 4.2f, 12), Colors.White);
            c.DrawColoredPolygon(IsoDraw.FloorEllipse(plate, 2.4f, 10), new Color("#E9E2D0"));
            var cup = IsoDraw.ToScreen(cx + (w * 0.28), cy - (l * 0.25)) + new Vector2(0, -height);
            c.DrawRect(new Rect2(cup + new Vector2(-1.8f, -4.6f), new Vector2(3.6f, 4.6f)), Colors.White);
            c.DrawRect(new Rect2(cup + new Vector2(-1.8f, -4.6f), new Vector2(3.6f, 1.1f)), new Color("#6D4C41"));
        }
    }
}
