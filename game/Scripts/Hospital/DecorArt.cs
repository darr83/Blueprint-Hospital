using System;
using System.Collections.Generic;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Draws the floor and wall finishes the player chose in the Decorate panel: seven floors and
/// five wall styles, each in a colour. The standard finish is drawn by <see cref="DepartmentRenderer"/>
/// and <see cref="WorldLayer"/> as before. The same drawing makes the swatches in the panel.
/// </summary>
public static class DecorArt
{
    /// <summary>The look being tried out in the Decorate panel before it is paid for, shown in the world.</summary>
    public static (string RoomId, RoomDecor Look)? Preview { get; set; }

    /// <summary>The finish a room shows: the one being tried out, or the one it has.</summary>
    public static RoomDecor LookOf(HospitalSimulation sim, CareSpace space) =>
        Preview is { } p && p.RoomId == space.Id ? p.Look : sim.DecorOf(space);

    private static readonly Color Cream = new("#F2EEE6");
    private static readonly Color Navy = new("#3B4A5A");
    private static readonly Color Oak = new("#C99A6B");
    private static readonly Color PanelOak = new("#B88655");

    // ------------------------------------------------------------------ colours

    /// <summary>The floor colour: the one picked, or the finish's usual colour in this room.</summary>
    public static Color FloorColour(CareSpace space, RoomDecor look)
    {
        if (look.FloorColour is { } hex)
        {
            return new Color(hex);
        }

        var tint = DepartmentRenderer.FloorColours.GetValueOrDefault(space.Kind, new Color("#E6E9EC"));
        return look.Floor switch
        {
            FloorFinish.WoodPlanks => Oak,
            FloorFinish.Chequer => Navy,
            FloorFinish.CarpetTiles => tint.Darkened(0.3f),
            FloorFinish.SafetyVinyl => tint.Darkened(0.06f),
            _ => tint,
        };
    }

    /// <summary>The wall colour: the one picked, or the finish's usual colour.</summary>
    public static Color WallColour(RoomDecor look) =>
        look.WallColour is { } hex ? new Color(hex) : look.Walls switch
        {
            WallFinish.WoodPanel => new Color("#F3E9D6"),
            WallFinish.Tiles => new Color("#F4F7F8"),
            WallFinish.Paint => new Color("#EEF3F6"),
            _ => new Color("#F1F4F6"),
        };

    /// <summary>The second colour on a wall: the dado, stripe or tile band, in the room's wayfinding colour where it has one.</summary>
    public static Color? WallAccent(CareSpace? space, RoomDecor look)
    {
        var way = space is null ? null : DepartmentRenderer.WayfindingColour(space);
        return look.Walls switch
        {
            WallFinish.TwoTone => way is { } w ? w.Lightened(0.35f) : WallColour(look).Darkened(0.22f),
            WallFinish.Stripe => way ?? new Color("#5C6BC0"),
            WallFinish.Tiles => way,
            WallFinish.WoodPanel => PanelOak,
            _ => null,
        };
    }

    // ------------------------------------------------------------------ floors

    /// <summary>Lays a floor finish over a rectangle of tiles, mapped to the screen by <paramref name="map"/>.</summary>
    public static void DrawFloor(CanvasItem c, double x, double y, double w, double l, FloorFinish finish, Color colour, Func<double, double, Vector2> map, int seed)
    {
        void Quad(double qx, double qy, double qw, double ql, Color col) =>
            c.DrawColoredPolygon(new[] { map(qx, qy), map(qx + qw, qy), map(qx + qw, qy + ql), map(qx, qy + ql) }, col);
        void Line(double x0, double y0, double x1, double y1, Color col, float width = 1f) =>
            c.DrawLine(map(x0, y0), map(x1, y1), col, width, true);

        var rng = new Random(seed);
        Quad(x, y, w, l, colour);
        var (right, bottom) = (x + w, y + l);
        switch (finish)
        {
            case FloorFinish.Standard:
            case FloorFinish.Vinyl:
                for (var tx = Math.Floor(x); tx < right; tx++)
                {
                    for (var ty = Math.Floor(y); ty < bottom; ty++)
                    {
                        if (((int)tx + (int)ty) % 2 == 0)
                        {
                            Quad(Math.Max(tx, x), Math.Max(ty, y), Math.Min(tx + 1, right) - Math.Max(tx, x), Math.Min(ty + 1, bottom) - Math.Max(ty, y), colour.Darkened(finish == FloorFinish.Vinyl ? 0.035f : 0.025f));
                        }
                    }
                }

                if (finish == FloorFinish.Vinyl)
                {
                    // Welded seams every two metres.
                    for (var sx = Math.Ceiling(x / 4) * 4; sx < right; sx += 4)
                    {
                        Line(sx, y, sx, bottom, new Color(colour.Darkened(0.15f), 0.5f));
                    }
                }

                break;

            case FloorFinish.CeramicTiles:
            {
                var grout = colour.Darkened(0.13f);
                for (var tx = x; tx < right; tx++)
                {
                    for (var ty = y; ty < bottom; ty++)
                    {
                        Quad(tx, ty, Math.Min(1, right - tx), Math.Min(1, bottom - ty), colour.Darkened((float)(rng.NextDouble() * 0.03)));
                    }
                }

                for (var tx = x + 1; tx < right; tx++)
                {
                    Line(tx, y, tx, bottom, grout, 1.5f);
                }

                for (var ty = y + 1; ty < bottom; ty++)
                {
                    Line(x, ty, right, ty, grout, 1.5f);
                }

                break;
            }

            case FloorFinish.WoodPlanks:
            {
                var seam = colour.Darkened(0.25f);
                for (var py = 0.0; py < l - 0.001; py += 0.25)
                {
                    var row = (int)Math.Round(py * 4);
                    var px = -(row * 0.37 % 1.6);
                    var depth = Math.Min(0.25, l - py);
                    while (px < w)
                    {
                        var len = 1.2 + (rng.NextDouble() * 0.8);
                        var x0 = Math.Max(0, px);
                        var x1 = Math.Min(w, px + len);
                        Quad(x + x0, y + py, x1 - x0, depth, colour.Darkened((float)(rng.NextDouble() * 0.09) - 0.02f));
                        if (x1 < w)
                        {
                            Line(x + x1, y + py, x + x1, y + py + depth, seam);
                        }

                        px += len;
                    }

                    if (py > 0)
                    {
                        Line(x, y + py, right, y + py, seam);
                    }
                }

                break;
            }

            case FloorFinish.Terrazzo:
            case FloorFinish.SafetyVinyl:
            {
                var terrazzo = finish == FloorFinish.Terrazzo;
                var size = (map(1, 0) - map(0, 0)).Length() / 45f;
                var count = (int)(w * l * (terrazzo ? 14 : 22));
                var flecks = terrazzo
                    ? new[] { colour.Darkened(0.45f), colour.Lightened(0.5f), colour.Darkened(0.25f), new Color("#C9A27E") }
                    : new[] { colour.Darkened(0.35f), colour.Lightened(0.45f), colour.Darkened(0.2f) };
                for (var i = 0; i < count; i++)
                {
                    var p = map(x + (rng.NextDouble() * w), y + (rng.NextDouble() * l));
                    var r = (terrazzo ? 0.9f + (float)(rng.NextDouble() * 1.8) : 0.8f + (float)(rng.NextDouble() * 0.7)) * size;
                    c.DrawCircle(p, r, flecks[rng.Next(flecks.Length)]);
                }

                if (terrazzo)
                {
                    // Brass strips in a two-metre grid.
                    var strip = new Color(colour.Darkened(0.22f), 0.6f);
                    for (var sx = Math.Ceiling((x + 0.01) / 4) * 4; sx < right; sx += 4)
                    {
                        Line(sx, y, sx, bottom, strip);
                    }

                    for (var sy = Math.Ceiling((y + 0.01) / 4) * 4; sy < bottom; sy += 4)
                    {
                        Line(x, sy, right, sy, strip);
                    }
                }

                break;
            }

            case FloorFinish.CarpetTiles:
            {
                var pile = new Color(colour.Darkened(0.3f), 0.35f);
                for (var tx = x; tx < right; tx++)
                {
                    for (var ty = y; ty < bottom; ty++)
                    {
                        var (tw, tl) = (Math.Min(1, right - tx), Math.Min(1, bottom - ty));
                        var turned = ((int)Math.Floor(tx) + (int)Math.Floor(ty)) % 2 == 0;
                        Quad(tx, ty, tw, tl, turned ? colour : colour.Darkened(0.06f));
                        for (var t = 0.2; t < 1; t += 0.2)
                        {
                            if (turned && t < tw)
                            {
                                Line(tx + t, ty + 0.05, tx + t, ty + tl - 0.05, pile);
                            }
                            else if (!turned && t < tl)
                            {
                                Line(tx + 0.05, ty + t, tx + tw - 0.05, ty + t, pile);
                            }
                        }
                    }
                }

                break;
            }

            case FloorFinish.Chequer:
            {
                var other = colour.Luminance > 0.55f ? Navy : Cream;
                Quad(x, y, w, l, other);
                for (var tx = x; tx < right; tx++)
                {
                    for (var ty = y; ty < bottom; ty++)
                    {
                        if (((int)Math.Floor(tx) + (int)Math.Floor(ty)) % 2 == 0)
                        {
                            Quad(tx, ty, Math.Min(1, right - tx), Math.Min(1, bottom - ty), colour);
                        }
                    }
                }

                break;
            }
        }
    }

    /// <summary>A room's floor in the world, in its finish.</summary>
    public static void DrawRoomFloor(CanvasItem c, CareSpace space, RoomDecor look)
    {
        var r = space.Rect;
        DrawFloor(c, r.X, r.Y, r.Width, r.Length, look.Floor, FloorColour(space, look), IsoDraw.ToScreen, (r.X * 73856093) ^ (r.Y * 19349663));
    }

    // ------------------------------------------------------------------ walls

    /// <summary>
    /// Paints one face of a wall: <paramref name="a"/> and <paramref name="b"/> are its two ends on
    /// the floor (left to right on screen) and it rises <paramref name="height"/> pixels.
    /// </summary>
    public static void DrawWallFace(CanvasItem c, Vector2 a, Vector2 b, float height, float shade, WallFinish finish, Color colour, Color? accent, Color skirting, float scale = 1f)
    {
        if (finish == WallFinish.Standard)
        {
            return;
        }

        Vector2 At(float t, float h) => a.Lerp(b, t) + new Vector2(0, -h);
        void Band(float from, float to, Color col) =>
            c.DrawColoredPolygon(new[] { a + new Vector2(0, -from), b + new Vector2(0, -from), b + new Vector2(0, -to), a + new Vector2(0, -to) }, col.Darkened(shade));

        Band(0, height, colour);
        var length = a.DistanceTo(b);
        switch (finish)
        {
            case WallFinish.TwoTone:
            {
                var lower = accent ?? colour.Darkened(0.22f);
                Band(0, height * 0.42f, lower);
                Band(height * 0.42f, height * 0.46f, lower.Lightened(0.4f));
                break;
            }

            case WallFinish.Stripe:
            {
                var stripe = accent ?? colour.Darkened(0.4f);
                Band(height * 0.5f, height * 0.6f, stripe);
                Band(height * 0.63f, height * 0.66f, stripe);
                break;
            }

            case WallFinish.Tiles:
            {
                var grout = colour.Darkened(0.12f).Darkened(shade);
                var row = 9f * scale;
                for (var h = row; h < height - 2; h += row)
                {
                    c.DrawLine(At(0, h), At(1, h), grout, 1f, true);
                }

                var n = Math.Max(1, (int)Math.Round(length / (13f * scale)));
                for (var i = 1; i < n; i++)
                {
                    c.DrawLine(At(i / (float)n, 0), At(i / (float)n, height - 2), grout, 1f, true);
                }

                if (accent is { } band)
                {
                    Band(height * 0.5f, (height * 0.5f) + row, band);
                }

                break;
            }

            case WallFinish.WoodPanel:
            {
                var wood = accent ?? PanelOak;
                var top = height * 0.45f;
                Band(0, top, wood);
                var n = Math.Max(1, (int)Math.Round(length / (9f * scale)));
                for (var i = 1; i < n; i++)
                {
                    c.DrawLine(At(i / (float)n, 0), At(i / (float)n, top), wood.Darkened(0.18f).Darkened(shade), 1f, true);
                }

                Band(top, top + (3 * scale), wood.Darkened(0.25f));
                break;
            }
        }

        var s = new Vector2(0, -4f * scale);
        c.DrawColoredPolygon(new[] { a, b, b + s, a + s }, skirting.Darkened(shade));
    }
}
