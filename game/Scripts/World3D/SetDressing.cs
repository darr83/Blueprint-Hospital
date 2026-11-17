using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// Hospital set dressing that makes a ward read as a ward: a bed-head service panel on the wall over
/// every bed (oxygen, air and suction outlets, sockets, a reading light and the nurse-call strip) and,
/// on wards, a ceiling curtain track round each bed with the curtains drawn back to the wall.
/// Built with the wall decorations, so it goes when the walls are cut away.
/// </summary>
public static class SetDressing
{
    /// <summary>How far the head of a bed can be from its wall and still get a panel, in tiles.</summary>
    private const float Reach = 3.5f;

    /// <summary>Half the width of a curtained bed space, and how far the track runs past the middle of the bed, in tiles.</summary>
    private const float HalfBay = 1.45f;
    private const float PastMiddle = 2.6f;

    private static readonly Color Panel = new("#E8EDF1");
    private static readonly Color Stripe = new("#2E6FB0");
    private static readonly Color Lamp = new("#D7DEE3");
    private static readonly Color LampGlow = new("#FFF6D8");
    private static readonly Color Rail = new("#C9D1D8");
    private static readonly Color CurtainA = new("#8FB8D6");
    private static readonly Color CurtainB = new("#7FAACB");

    /// <summary>The wall each bed's head is against, as (tile column of the bed, the wall's line), or null when the bed stands clear of it.</summary>
    public static (float X, float Wall, float Into)? HeadWall(CareSpace space, WardBed bed)
    {
        var r = space.Rect;
        var wall = bed.HeadSouth ? r.Bottom : r.Y;
        if (Math.Abs(bed.PatientSpot.Y + 0.5f - wall) > Reach)
        {
            return null;
        }

        return (bed.PatientSpot.X + 0.5f, wall, bed.HeadSouth ? -1f : 1f);
    }

    /// <summary>Adds the panels (and curtain tracks) for every bed in <paramref name="space"/>; returns how many panels.</summary>
    public static int BedHeads(MeshBatch b, CareSpace space)
    {
        var was = b.Kind;
        var t = (float)WorldLayer.WallThickness / 2;
        var top = Walls3D.FullHeight;
        var curtained = space.IsWard && !space.Furniture.Any(f => f.ItemId == RoomLayoutEngine.BayCurtain);
        var count = 0;
        try
        {
            foreach (var bed in space.Beds)
            {
                if (HeadWall(space, bed) is not var (cx, wall, dir))
                {
                    continue;
                }

                var face = wall + (dir * (t + 0.03f));
                b.Kind = SurfaceKind.Laminate;
                b.Box(new Vector3(cx, Kit3D.M(1.45), face), new Vector3(2.2f, Kit3D.M(0.32), 0.07f), Panel);
                b.Box(new Vector3(cx, Kit3D.M(1.62), face + (dir * 0.005f)), new Vector3(2.2f, Kit3D.M(0.03), 0.075f), Stripe);
                b.Box(new Vector3(cx, Kit3D.M(1.85), face), new Vector3(0.5f, Kit3D.M(0.08), 0.12f), Lamp);
                b.Box(new Vector3(cx, Kit3D.M(1.81), face + (dir * 0.05f)), new Vector3(0.44f, Kit3D.M(0.015), 0.04f), LampGlow);

                // Medical gas outlets: oxygen (white), air (black and white), vacuum (yellow), with the green flowmeter's colour.
                var outlets = new[] { ("#F2F4F5", -0.55f), ("#1F7A3E", -0.35f), ("#2B2F33", -0.15f), ("#F2C230", 0.05f) };
                foreach (var (c, x) in outlets)
                {
                    b.Cylinder(new Vector3(cx + x, Kit3D.M(1.42), face + (dir * 0.05f)), 0.045f, 0.04f, 0.045f, new Color(c), new Basis(Vector3.Right, Mathf.Pi / 2));
                }

                // Two sockets with red switches.
                foreach (var x in new[] { 0.4f, 0.65f })
                {
                    b.Box(new Vector3(cx + x, Kit3D.M(1.42), face + (dir * 0.04f)), new Vector3(0.16f, Kit3D.M(0.08), 0.02f), new Color("#FAFAFA"));
                    b.Box(new Vector3(cx + x + 0.04f, Kit3D.M(1.44), face + (dir * 0.052f)), new Vector3(0.03f, Kit3D.M(0.02), 0.01f), new Color("#D32F2F"));
                }

                count++;
                if (!curtained)
                {
                    continue;
                }

                // Curtain track just under the top of the walls, the curtains gathered at the head wall.
                b.Kind = SurfaceKind.Metal;
                var rail = top - 0.08f;
                var foot = bed.PatientSpot.Y + 0.5f + (dir * PastMiddle);
                var start = wall + (dir * (t + 0.05f));
                foreach (var side in new[] { -HalfBay, HalfBay })
                {
                    b.Rod(new Vector3(cx + side, rail, start), new Vector3(cx + side, rail, foot), 0.025f, Rail, small: true);
                }

                b.Rod(new Vector3(cx - HalfBay, rail, foot), new Vector3(cx + HalfBay, rail, foot), 0.025f, Rail, small: true);
                b.Kind = SurfaceKind.Fabric;
                var hem = Kit3D.M(0.35);
                foreach (var side in new[] { -HalfBay, HalfBay })
                {
                    for (var k = 0; k < 4; k++)
                    {
                        var z = start + (dir * (0.12f + (k * 0.11f)));
                        b.Box(new Vector3(cx + side, (rail + hem) / 2, z), new Vector3(0.05f, rail - hem, 0.1f), k % 2 == 0 ? CurtainA : CurtainB);
                    }
                }
            }
        }
        finally
        {
            b.Kind = was;
        }

        return count;
    }
}
