using static BlueprintHospital.Simulation.Medical.Results.Shapes;

namespace BlueprintHospital.Simulation.Medical.Results;

/// <summary>Ultrasound pictures: the pelvic scan and the echocardiogram, as a grainy fan-shaped view.</summary>
internal static class UltrasoundPictures
{
    public static void Register(PictureRegistrar add)
    {
        add("pus_normal", PictureKind.Image, "Pelvic ultrasound, normal", r => Pelvis(r, PelvicFinding.None));
        add("pus_simple_cyst", PictureKind.Image, "Pelvic ultrasound, small simple ovarian cyst", r => Pelvis(r, PelvicFinding.SimpleCyst));
        add("pus_ectopic", PictureKind.Image, "Pelvic ultrasound, ectopic pregnancy with free fluid", r => Pelvis(r, PelvicFinding.Ectopic));
        add("pus_iup_bleed", PictureKind.Image, "Pelvic ultrasound, live pregnancy with a small bleed", r => Pelvis(r, PelvicFinding.PregnancyBleed));
        add("pus_torsion", PictureKind.Image, "Pelvic ultrasound, swollen ovary with a large cyst (torsion)", r => Pelvis(r, PelvicFinding.Torsion));
        add("pus_hydrosalpinx", PictureKind.Image, "Pelvic ultrasound, fluid-filled tubes", r => Pelvis(r, PelvicFinding.Tubes));
        add("pus_iup", PictureKind.Image, "Pelvic ultrasound, live pregnancy", r => Pelvis(r, PelvicFinding.Pregnancy));
        add("pus_twins", PictureKind.Image, "Pelvic ultrasound, twin pregnancy", r => Pelvis(r, PelvicFinding.Twins));

        add("echo_normal", PictureKind.Image, "Echocardiogram, normal", r => Echo(r, EchoFinding.None));
        add("echo_thick_aortic", PictureKind.Image, "Echocardiogram, mildly thickened valve", r => Echo(r, EchoFinding.ThickValve));
        add("echo_poor_lv", PictureKind.Image, "Echocardiogram, big weak left ventricle", r => Echo(r, EchoFinding.PoorLv));
        add("echo_anterior_rwma", PictureKind.Image, "Echocardiogram, wall not moving near the apex", r => Echo(r, EchoFinding.AnteriorWall));
        add("echo_lateral_rwma", PictureKind.Image, "Echocardiogram, lateral wall moving poorly", r => Echo(r, EchoFinding.LateralWall));
        add("echo_big_la", PictureKind.Image, "Echocardiogram, dilated left atrium", r => Echo(r, EchoFinding.BigAtrium));
        add("echo_rv_dilated", PictureKind.Image, "Echocardiogram, dilated right ventricle", r => Echo(r, EchoFinding.BigRv));
        add("echo_effusion", PictureKind.Image, "Echocardiogram, pericardial effusion", r => Echo(r, EchoFinding.Effusion));
    }

    private const float ApexX = 220;
    private const float ApexY = 6;

    private static Sdf Fan(float r0, float r1, float half) => (x, y) =>
    {
        var dx = x - ApexX;
        var dy = y - ApexY;
        var rad = MathF.Sqrt((dx * dx) + (dy * dy));
        var theta = MathF.Atan2(dx, dy);
        return Math.Max(Math.Max(r0 - rad, rad - r1), (MathF.Abs(theta) - half) * rad);
    };

    private static RenderedPicture Finish(PictureCanvas c, Random r, string title, params string[] corner)
    {
        c.R.Speckle(r, 0.85f);
        c.R.Blur(1);
        c.R.Mask(Fan(24, 336, 0.78f), Rgb.Grey(0.02f), 1.5f);
        for (var i = 0; i < corner.Length; i++)
        {
            c.Label(12, 12 + (i * 16), corner[i], 12);
        }

        return c.Done(title);
    }

    private enum PelvicFinding
    {
        None,
        SimpleCyst,
        Ectopic,
        PregnancyBleed,
        Torsion,
        Tubes,
        Pregnancy,
        Twins,
    }

    private static RenderedPicture Pelvis(Random r, PelvicFinding f)
    {
        var c = new PictureCanvas(440, 350, Rgb.Grey(0.2f));
        var uterus = Ellipse(170, 170, 94, 58, -0.32f);
        c.R.Paint(uterus, Rgb.Grey(0.42f), 1, 6);
        var pregnant = f is PelvicFinding.Pregnancy or PelvicFinding.PregnancyBleed or PelvicFinding.Twins;
        if (!pregnant)
        {
            c.R.Paint(Ellipse(172, 166, 64, 7, -0.32f), Rgb.Grey(0.85f), 1, 3);
        }

        void Sac(float x, float y, float rx, float ry, bool heartbeat)
        {
            c.R.Stroke(Ellipse(x, y, rx, ry, -0.3f), 6, Rgb.Grey(0.9f), 1, 2);
            c.R.Paint(Ellipse(x, y, rx - 3, ry - 3, -0.3f), Rgb.Grey(0.02f), 1, 2);
            c.R.Stroke(Circle(x - (rx * 0.45f), y - (ry * 0.3f), 5), 2, Rgb.Grey(0.9f), 1, 1);
            c.R.Paint(Ellipse(x + 4, y + 4, rx * 0.42f, ry * 0.22f, 0.4f), Rgb.Grey(0.55f), 1, 3);
            if (heartbeat)
            {
                c.R.Paint(Circle(x + 2, y + 2, 2.5f), Rgb.Grey(1f), 1, 1);
            }
        }

        var ovaryX = 352f;
        var ovaryY = 150f;
        if (f != PelvicFinding.Torsion)
        {
            c.R.Paint(Ellipse(ovaryX, ovaryY, 32, 21), Rgb.Grey(0.3f), 1, 5);
            for (var i = 0; i < 4; i++)
            {
                c.R.Paint(Circle(ovaryX - 14 + (i * 9), ovaryY + (i % 2 == 0 ? -4 : 5), 4), Rgb.Grey(0.03f), 1, 1.5f);
            }
        }

        switch (f)
        {
            case PelvicFinding.SimpleCyst:
                c.R.Paint(Circle(ovaryX + 6, ovaryY, 14), Rgb.Grey(0.02f), 1, 1.5f);
                c.Mark(ovaryX + 6, ovaryY, 26, "Simple cyst");
                break;
            case PelvicFinding.Ectopic:
                c.R.Stroke(Circle(322, 206, 26), 9, Rgb.Grey(0.95f), 1, 2);
                c.R.Paint(Circle(322, 206, 20), Rgb.Grey(0.06f), 1, 2);
                c.R.Paint(Ellipse(236, 296, 118, 26, 0.05f), Rgb.Grey(0.01f), 1, 3);
                c.Mark(322, 206, 42, "Ectopic");
                c.Mark(236, 296, 64, "Free fluid (blood)");
                break;
            case PelvicFinding.PregnancyBleed:
                Sac(170, 166, 36, 26, true);
                c.R.Paint(Ellipse(120, 196, 26, 9, -0.35f), Rgb.Grey(0.08f), 1, 3);
                c.Mark(170, 166, 44, "Live pregnancy");
                c.Mark(118, 198, 30, "Small bleed");
                break;
            case PelvicFinding.Pregnancy:
                Sac(170, 166, 38, 28, true);
                c.Mark(170, 166, 46, "Pregnancy sac");
                break;
            case PelvicFinding.Twins:
                Sac(140, 176, 30, 24, true);
                Sac(206, 152, 30, 24, true);
                c.Mark(174, 164, 70, "Two sacs (twins)");
                break;
            case PelvicFinding.Torsion:
                c.R.Paint(Ellipse(330, 196, 66, 54), Rgb.Grey(0.34f), 1, 5);
                c.R.Paint(Circle(330, 196, 42), Rgb.Grey(0.03f), 1, 2);
                c.Mark(330, 196, 72, "Swollen ovary, no flow");
                break;
            case PelvicFinding.Tubes:
                c.R.Paint(Polyline(Bezier((268, 190), (300, 230), (340, 200), (370, 240)), 11), Rgb.Grey(0.04f), 1, 2);
                c.R.Paint(Polyline(Bezier((90, 230), (70, 260), (120, 280), (100, 300)), 10), Rgb.Grey(0.04f), 1, 2);
                c.Mark(320, 216, 54, "Fluid-filled tube");
                break;
        }

        var label = f is PelvicFinding.Ectopic or PelvicFinding.Torsion or PelvicFinding.SimpleCyst ? "LT ADNEXA" : "UTERUS SAG";
        c.Label(12, c.R.Height - 24, label, 12);
        var pic = Finish(c, r, "US Pelvis (transvaginal)", "TV  7.5 MHz", "Depth 9 cm");
        return pic;
    }

    private enum EchoFinding
    {
        None,
        ThickValve,
        PoorLv,
        AnteriorWall,
        LateralWall,
        BigAtrium,
        BigRv,
        Effusion,
    }

    private static RenderedPicture Echo(Random r, EchoFinding f)
    {
        var c = new PictureCanvas(440, 350, Rgb.Grey(0.1f));
        var heart = Ellipse(220, 186, 150, 160);
        if (f == EchoFinding.Effusion)
        {
            c.R.Paint(Ellipse(220, 186, 168, 178), Rgb.Grey(0.75f), 1, 4);
            c.R.Paint(Ellipse(220, 186, 160, 170), Rgb.Grey(0.02f), 1, 3);
            c.Mark(340, 160, 34, "Fluid around the heart");
        }
        else
        {
            c.R.Stroke(heart, 6, Rgb.Grey(0.75f), 1, 3);
        }

        c.R.Paint(heart, Rgb.Grey(0.4f), 1, 4);
        var lvRx = f == EchoFinding.PoorLv ? 62f : 46f;
        var lvRy = f == EchoFinding.PoorLv ? 100f : 84f;
        var rvRx = f == EchoFinding.BigRv ? 62f : 36f;
        var laRx = f == EchoFinding.BigAtrium ? 66f : 44f;
        var laRy = f == EchoFinding.BigAtrium ? 54f : 38f;
        var blood = Rgb.Grey(0.02f);
        c.R.Paint(Ellipse(268, 142, lvRx, lvRy, -0.15f), blood, 1, 3);
        c.R.Paint(Ellipse(f == EchoFinding.BigRv ? 160 : 168, 148, rvRx, 74, 0.2f), blood, 1, 3);
        c.R.Paint(Ellipse(268, 272, laRx, laRy), blood, 1, 3);
        c.R.Paint(Ellipse(168, 268, 40, 36), blood, 1, 3);
        c.R.Paint(Capsule(244, 232, 266, 214, 2.4f), Rgb.Grey(0.8f), 1, 1.5f);
        c.R.Paint(Capsule(296, 232, 278, 212, 2.4f), Rgb.Grey(0.8f), 1, 1.5f);
        c.R.Paint(Capsule(150, 232, 168, 216, 2.2f), Rgb.Grey(0.75f), 1, 1.5f);
        c.R.Paint(Capsule(194, 232, 180, 214, 2.2f), Rgb.Grey(0.75f), 1, 1.5f);
        c.Label(296, 120, "LV", 12);
        c.Label(150, 120, "RV", 12);
        c.Label(296, 276, "LA", 12);
        c.Label(150, 272, "RA", 12);
        switch (f)
        {
            case EchoFinding.ThickValve:
                c.R.Paint(Circle(222, 232, 6), Rgb.Grey(0.95f), 1, 2);
                c.Mark(222, 232, 18, "Thickened valve");
                break;
            case EchoFinding.PoorLv:
                c.Mark(270, 142, 96, "Big, weak ventricle (EF ~30%)");
                break;
            case EchoFinding.AnteriorWall:
                c.R.Paint(Ellipse(246, 70, 18, 8, -0.5f), Rgb.Grey(-0.2f), 1, 4, Blend.Add);
                c.Mark(244, 72, 32, "Wall not moving");
                break;
            case EchoFinding.LateralWall:
                c.R.Paint(Ellipse(318, 150, 8, 26), Rgb.Grey(-0.15f), 1, 4, Blend.Add);
                c.Mark(318, 150, 34, "Wall moving poorly");
                break;
            case EchoFinding.BigAtrium:
                c.Mark(268, 272, 64, "Dilated left atrium");
                break;
            case EchoFinding.BigRv:
                c.Mark(160, 148, 72, "Dilated right ventricle");
                break;
        }

        var bpm = 60 + r.Next(40);
        return Finish(c, r, "Echocardiogram (apical 4-chamber)", "S5-1  2.0 MHz", $"HR {bpm}", "A4C");
    }
}
