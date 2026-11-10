using System;
using System.Linq;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>Cardiac arrests on screen: chest compressions, the flashing crash marker and the resus trolley on the move.</summary>
public static partial class Figures
{
    /// <summary>About 110 compressions a minute (in real seconds on screen: it reads as CPR at any game speed).</summary>
    private const double CompressionsPerSecond = 1.8;

    /// <summary>How far down the body goes on each compression, in pixels.</summary>
    public static float CompressionDip(double time) => (float)Math.Max(0, Math.Sin(time * CompressionsPerSecond * Math.PI * 2)) * 2.2f;

    /// <summary>
    /// Someone doing chest compressions: standing at the bedside, leaning in, arms straight down to the patient's
    /// chest, rocking with each push. Drawn in place of <see cref="Standing"/>.
    /// </summary>
    public static void DoingCpr(CanvasItem c, Vector2 foot, PersonLook look, Vector2 chest, double time, int seed)
    {
        var dip = CompressionDip(time);
        var r = new Rig(look);
        var lean = Mathf.Clamp(chest.X - foot.X, -1, 1) * 2.5f;

        // Body (legs, torso and head) dips with each push.
        Standing(c, foot + new Vector2(lean * 0.2f, 0), look, time, walking: false, seed);
        var pen = new Pen(c, 1f);
        var outer = look.Jacket ?? look.Top;
        var longSleeves = look.LongSleeves || look.Jacket is not null;

        // The hands meet on the chest: the point is pulled towards the body so the arms stay a sensible length.
        var shoulderMid = foot + new Vector2(lean, r.ShY + 2 + dip);
        var reach = chest - shoulderMid;
        var max = 26f * r.S;
        if (reach.Length() > max)
        {
            reach = reach.Normalized() * max;
        }

        var hands = shoulderMid + reach + new Vector2(0, dip);
        foreach (var side in new[] { -1f, 1f })
        {
            var shoulder = shoulderMid + new Vector2(side * r.ShW * 0.85f, 0);
            var elbow = shoulder.Lerp(hands, 0.5f);
            var sleeve = side > 0 ? outer.Darkened(0.1f) : outer;
            if (longSleeves)
            {
                pen.Limb(sleeve, r.ArmW, shoulder, elbow, hands + new Vector2(side * 1.2f, 0));
            }
            else
            {
                pen.Limb(sleeve, r.ArmW + 0.6f, shoulder, shoulder.Lerp(elbow, 0.7f));
                pen.Limb(look.Skin.Darkened(side > 0 ? 0.08f : 0f), r.ArmW * 0.85f, shoulder.Lerp(elbow, 0.7f), hands + new Vector2(side * 1.2f, 0));
            }
        }

        // Interlocked hands.
        pen.Oval(hands, 2.6f * r.S, 1.8f * r.S, look.Skin, look.Skin.Darkened(0.3f));
    }

    /// <summary>The crash marker: a pulsing red ring on the floor round the bed and a red heart above it.</summary>
    public static void CrashMarker(CanvasItem c, Vector2 centre, double time, float radius = 46)
    {
        var pulse = (float)(0.5 + (0.5 * Math.Sin(time * 6)));
        var red = new Color("#D32F2F");
        var ring = IsoDraw.FloorEllipse(centre, radius + (pulse * 4), 32);
        c.DrawPolyline(ring.Append(ring[0]).ToArray(), new Color(red, 0.55f + (0.4f * pulse)), 3.5f, true);

        // The heart floats above the bed, with a thin "flat line" under it.
        var at = centre + new Vector2(0, -78 - (pulse * 3));
        var size = 9f + (pulse * 1.5f);
        var heart = new Vector2[24];
        for (var i = 0; i < heart.Length; i++)
        {
            var t = i / (float)heart.Length * Mathf.Tau;
            var x = 16 * Mathf.Pow(Mathf.Sin(t), 3);
            var y = (13 * Mathf.Cos(t)) - (5 * Mathf.Cos(2 * t)) - (2 * Mathf.Cos(3 * t)) - Mathf.Cos(4 * t);
            heart[i] = at + new Vector2(x, -y) * (size / 16f);
        }

        c.DrawColoredPolygon(heart, red);
        c.DrawPolyline(heart.Append(heart[0]).ToArray(), red.Darkened(0.35f), 1.2f, true);
        c.DrawPolyline(new[] { at + new Vector2(-6, 0), at + new Vector2(-2, 0), at + new Vector2(-0.5f, -4), at + new Vector2(1.5f, 3), at + new Vector2(3, 0), at + new Vector2(6, 0) }, Colors.White, 1.4f, true);
    }

    /// <summary>
    /// A ward emergency (a fall, sepsis, a heart attack...): a pulsing amber ring on the floor and an amber warning
    /// sign above the patient, until the treatment is in.
    /// </summary>
    public static void EmergencyMarker(CanvasItem c, Vector2 centre, double time, float radius = 40, bool treated = false, float height = 84)
    {
        var pulse = treated ? 0.3f : (float)(0.5 + (0.5 * Math.Sin(time * 4)));
        var amber = new Color("#F59E0B");
        var ring = IsoDraw.FloorEllipse(centre, radius + (pulse * 3), 32);
        c.DrawPolyline(ring.Append(ring[0]).ToArray(), new Color(amber, treated ? 0.35f : 0.5f + (0.4f * pulse)), 3f, true);
        if (treated)
        {
            return;
        }

        // A warning triangle with an exclamation mark, bobbing gently.
        var at = centre + new Vector2(0, -height - (pulse * 3));
        var size = 11f;
        var triangle = new[] { at + new Vector2(0, -size), at + new Vector2(size * 1.05f, size * 0.8f), at + new Vector2(-size * 1.05f, size * 0.8f) };
        c.DrawColoredPolygon(triangle, amber);
        c.DrawPolyline(triangle.Append(triangle[0]).ToArray(), amber.Darkened(0.4f), 1.4f, true);
        c.DrawLine(at + new Vector2(0, -size * 0.45f), at + new Vector2(0, size * 0.25f), new Color("#3B2A05"), 2.2f, true);
        c.DrawCircle(at + new Vector2(0, size * 0.55f), 1.3f, new Color("#3B2A05"));
    }

    /// <summary>A resus trolley wherever it is (being pushed, or parked), drawn whole.</summary>
    public static void ResusTrolleyAt(CanvasItem c, double gx, double gy, double w, double l, float height, Color colour, (double X, double Y) facing)
    {
        // Drawers face the way it is going; the push handle is at the back, by whoever is pushing.
        var front = Math.Abs(facing.X) > Math.Abs(facing.Y)
            ? (facing.X > 0 ? Sides.East : Sides.West)
            : (facing.Y < 0 ? Sides.North : Sides.South);
        var (sw, sl) = front is Sides.East or Sides.West ? (l, w) : (w, l);
        var whole = (gx - (sw / 2), gy - (sl / 2), sw, sl);
        FurnitureArt.Slice(c, HospitalSimulation.ResusTrolleyItem, whole.Item1, whole.Item2, sw, sl, whole, height, colour, Sides.None, front);
    }
}
