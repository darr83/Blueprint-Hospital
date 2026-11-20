using static BlueprintHospital.Simulation.Medical.Results.Shapes;

namespace BlueprintHospital.Simulation.Medical.Results;

/// <summary>
/// Plain X-ray pictures: limbs, chest and abdomen. Bone and soft tissue add light the way they block X-rays,
/// so overlapping bones look brighter, and a fracture is the bone outline cut and shifted.
/// </summary>
internal static class XRayPictures
{
    private static readonly Rgb Film = Rgb.Grey(0.02f);

    public static void Register(PictureRegistrar add)
    {
        add("ankle_normal", PictureKind.Image, "Ankle X-ray, normal", r => Ankle(r, AnkleBreak.None));
        add("ankle_weber_b", PictureKind.Image, "Ankle X-ray, displaced Weber B fibula fracture", r => Ankle(r, AnkleBreak.WeberB));
        add("ankle_fibula_undisplaced", PictureKind.Image, "Ankle X-ray, undisplaced distal fibula fracture", r => Ankle(r, AnkleBreak.Undisplaced));
        add("ankle_hairline", PictureKind.Image, "Ankle X-ray, possible hairline lucency", r => Ankle(r, AnkleBreak.Hairline));
        add("hip_normal", PictureKind.Image, "Hip X-ray, normal", r => Hip(r, 0));
        add("hip_intracapsular", PictureKind.Image, "Hip X-ray, displaced intracapsular neck of femur fracture", r => Hip(r, 1));
        add("hip_intertrochanteric", PictureKind.Image, "Hip X-ray, intertrochanteric fracture", r => Hip(r, 2));
        add("wrist_normal", PictureKind.Image, "Wrist X-ray, normal", r => Wrist(r, 0));
        add("wrist_colles", PictureKind.Image, "Wrist X-ray, Colles' fracture of the distal radius", r => Wrist(r, 1));
        add("wrist_buckle", PictureKind.Image, "Wrist X-ray, buckle fracture of the distal radius", r => Wrist(r, 2));
        add("shoulder_normal", PictureKind.Image, "Shoulder X-ray, normal", r => Shoulder(r, false));
        add("shoulder_dislocation", PictureKind.Image, "Shoulder X-ray, anterior dislocation", r => Shoulder(r, true));
        add("tibia_normal", PictureKind.Image, "Lower leg X-ray, normal", r => Tibia(r, false));
        add("tibia_spiral", PictureKind.Image, "Lower leg X-ray, displaced spiral tibial fracture", r => Tibia(r, true));
        add("knee_normal", PictureKind.Image, "Knee X-ray, normal", r => Knee(r, false));
        add("knee_effusion", PictureKind.Image, "Knee X-ray, joint effusion", r => Knee(r, true));

        add("cxr_normal", PictureKind.Image, "Chest X-ray, normal", r => Chest(r, ChestFinding.None));
        add("cxr_subtle_left_base", PictureKind.Image, "Chest X-ray, subtle shadowing at the left base", r => Chest(r, ChestFinding.SubtleLeftBase));
        add("cxr_rll_consolidation", PictureKind.Image, "Chest X-ray, right lower lobe pneumonia", r => Chest(r, ChestFinding.RightLowerConsolidation));
        add("cxr_left_base_consolidation", PictureKind.Image, "Chest X-ray, left basal pneumonia", r => Chest(r, ChestFinding.LeftBaseConsolidation));
        add("cxr_pulmonary_oedema", PictureKind.Image, "Chest X-ray, big heart, fluid on the lungs and effusions", r => Chest(r, ChestFinding.Oedema));
        add("cxr_hyperinflation", PictureKind.Image, "Chest X-ray, hyperinflated lungs (COPD)", r => Chest(r, ChestFinding.Hyperinflation));
        add("cxr_pe_wedge", PictureKind.Image, "Chest X-ray, small wedge opacity and effusion at the right base", r => Chest(r, ChestFinding.Wedge));
        add("cxr_free_air", PictureKind.Image, "Chest X-ray, free air under the right diaphragm", r => Chest(r, ChestFinding.FreeAir));
        add("cxr_viral_patchy", PictureKind.Image, "Chest X-ray, patchy shadowing in both lungs", r => Chest(r, ChestFinding.Patchy));

        add("axr_normal", PictureKind.Image, "Abdominal X-ray, normal gas pattern", r => Abdomen(r, 0));
        add("axr_mild_loops", PictureKind.Image, "Abdominal X-ray, a few mildly dilated loops", r => Abdomen(r, 1));
        add("axr_lbo", PictureKind.Image, "Abdominal X-ray, dilated large bowel", r => Abdomen(r, 2));
        add("axr_sbo", PictureKind.Image, "Abdominal X-ray, dilated small bowel", r => Abdomen(r, 3));
    }

    // ---------- shared ----------
    private static void SoftTissue(PictureCanvas c, Sdf s, float v = 0.17f, float soft = 14) =>
        c.R.Paint(s, Rgb.Grey(v), 1, soft, Blend.Add);

    private static void Bone(PictureCanvas c, Sdf s, Box b, float density = 0.30f, float cortex = 0.24f, float cortexWidth = 5)
    {
        c.R.Paint(s, Rgb.Grey(density), 1, 1.4f, Blend.Add, b);
        c.R.PaintBand(s, cortexWidth, Rgb.Grey(cortex), 1, 1.3f, Blend.Add, b);
    }

    private static RenderedPicture Finish(PictureCanvas c, Random r, string title, bool mirror, string view)
    {
        c.R.Blur(1);
        c.R.Grain(r, 0.06f);
        if (mirror)
        {
            c.Mirror();
        }

        var side = title.Contains("Left", StringComparison.Ordinal) ? "L" : title.Contains("Right", StringComparison.Ordinal) ? "R" : null;
        if (side is not null)
        {
            c.Label(16, 16, side, 20, -1, true);
        }

        c.Label(c.R.Width - 12, c.R.Height - 20, view, 12, 1);
        return c.Done(title);
    }

    // ---------- ankle ----------
    private enum AnkleBreak
    {
        None,
        WeberB,
        Undisplaced,
        Hairline,
    }

    private static RenderedPicture Ankle(Random r, AnkleBreak kind)
    {
        const float k = 0.9f;
        var c = new PictureCanvas(360, 440, Film);
        SoftTissue(c, Polygon(Path("M92,-10 L348,-10 C352,150 362,280 354,360 C346,420 332,470 302,490 L118,490 C88,452 78,400 84,330 C88,250 90,120 92,-10 Z", k)), 0.2f);
        var calc = Ellipse(214 * k, 456 * k, 84 * k, 30 * k);
        Bone(c, calc, Box.Around(214 * k, 456 * k, 84 * k, 30 * k), 0.2f, 0.12f);
        var talusPts = Path("M154,314 C182,300 250,300 272,314 C286,342 281,382 261,398 L170,402 C150,388 145,342 154,314 Z", k);
        Bone(c, Polygon(talusPts), Box.Of(talusPts), 0.28f);
        var tibiaPts = Path("M150,-10 L246,-10 C249,150 256,230 276,290 L271,305 C232,299 182,299 152,306 L141,356 C129,360 118,351 118,336 C116,300 128,250 145,200 C148,120 150,60 150,-10 Z", k);
        Bone(c, Polygon(tibiaPts), Box.Of(tibiaPts), 0.3f, 0.26f, 6);
        c.R.Stroke(Polyline(Path("M152,305 C182,299 232,299 271,304", k), 0.1f), 3, Rgb.Grey(0.18f), 1, 1, Blend.Add);
        var fibPts = Path("M284,-10 L312,-10 C310,120 307,220 313,296 C319,338 324,372 313,388 C301,396 290,384 288,362 C284,320 282,240 283,160 Z", k);
        Sdf fibula = Polygon(fibPts);
        var (ax, ay, bx, by) = (240 * k, 306 * k, 360 * k, 258 * k);
        var jitter = (float)r.NextDouble();
        switch (kind)
        {
            case AnkleBreak.WeberB:
                fibula = Fracture(fibula, ax, ay, bx, by, 2.4f, 4 + (2 * jitter), 3, 0.03f);
                c.Mark(299 * k, 281 * k, 34, "Fracture");
                break;
            case AnkleBreak.Undisplaced:
                fibula = Fracture(fibula, ax, ay, bx, by, 1.6f);
                c.Mark(299 * k, 281 * k, 32, "Fracture");
                break;
            case AnkleBreak.Hairline:
                fibula = Subtract(fibula, Capsule(286 * k, 283 * k, 300 * k, 277 * k, 0.6f));
                c.Mark(293 * k, 280 * k, 26, "Possible hairline line");
                break;
        }

        Bone(c, fibula, Box.Of(fibPts).Grow(8), 0.3f, 0.24f, 4);
        var left = r.Next(2) == 0;
        return Finish(c, r, left ? "XR Left ankle (AP mortise)" : "XR Right ankle (AP mortise)", !left, "AP MORTISE");
    }

    // ---------- hip ----------
    private static RenderedPicture Hip(Random r, int kind)
    {
        var c = new PictureCanvas(360, 440, Film);
        SoftTissue(c, Polygon(Path("M-10,30 C80,10 300,20 380,60 L380,450 L130,450 C120,380 60,330 -10,320 Z")), 0.19f, 20);
        var ilium = Path("M20,40 C80,10 200,18 250,60 L262,130 C250,150 236,162 226,176 L180,186 C140,172 96,150 64,128 C38,108 18,80 20,40 Z");
        Bone(c, Polygon(ilium), Box.Of(ilium), 0.17f, 0.12f, 6);
        var ischium = Subtract(Ellipse(150, 300, 44, 36), Ellipse(152, 290, 27, 21));
        Bone(c, Union(ischium, Capsule(190, 252, 100, 268, 13)), new Box(80, 240, 200, 340), 0.22f, 0.16f, 4);
        Sdf femur = SmoothUnion(
            10,
            Circle(205, 215, 38),
            Capsule(205, 215, 250, 275, 25),
            Ellipse(276, 268, 26, 36, 0.2f),
            Ellipse(229, 318, 11, 16),
            Capsule(262, 290, 275, 460, 31, 27));
        var femurBox = new Box(160, 170, 320, 450);
        switch (kind)
        {
            case 1:
                femur = Fracture(femur, 240, 221, 202, 250, 2, 7 + (3 * (float)r.NextDouble()), -11, 0.12f);
                c.Mark(222, 236, 40, "Neck of femur fracture");
                break;
            case 2:
                femur = Fracture(femur, 290, 248, 220, 324, 2, 5, -9, 0.07f);
                c.Mark(255, 285, 48, "Fracture");
                break;
        }

        Bone(c, femur, femurBox, 0.3f, 0.24f, 6);
        c.R.PaintBand(Circle(205, 215, 46), 6, Rgb.Grey(0.16f), 1, 1.5f, Blend.Add, new Box(156, 160, 254, 206));
        var left = r.Next(2) == 0;
        return Finish(c, r, left ? "XR Left hip (AP)" : "XR Right hip (AP)", left, "AP");
    }

    // ---------- wrist ----------
    private static RenderedPicture Wrist(Random r, int kind)
    {
        var c = new PictureCanvas(360, 440, Film);
        SoftTissue(c, Polygon(Path("M70,450 C66,340 54,260 50,200 C44,120 30,40 20,-10 L300,-10 C292,60 286,140 280,210 C276,280 270,360 268,450 Z")), 0.2f, 16);
        var mcs = new[] { (95f, 132f, 58f, 22f, 13f), (125, 124, 112, -10, 12), (160, 118, 164, -10, 12), (193, 124, 206, -10, 10.5f), (222, 134, 246, -5, 10) };
        foreach (var (x1, y1, x2, y2, rr) in mcs)
        {
            var s = Capsule(x1, y1, x2, y2, rr + 2, rr - 1);
            Bone(c, s, new Box(Math.Min(x1, x2) - 20, Math.Min(y1, y2) - 20, Math.Max(x1, x2) + 20, Math.Max(y1, y2) + 20), 0.26f, 0.22f, 4);
        }

        var carpals = new[]
        {
            Ellipse(124, 182, 23, 14, -0.5f), Ellipse(165, 186, 18, 15), Ellipse(200, 181, 15, 13), Ellipse(212, 170, 10, 9),
            Ellipse(98, 152, 17, 14), Ellipse(128, 146, 14, 12), Ellipse(160, 146, 16, 22), Ellipse(196, 147, 17, 19),
        };
        foreach (var s in carpals)
        {
            Bone(c, s, new Box(60, 110, 240, 215), 0.2f, 0.18f, 3);
        }

        var radiusPts = Path("M108,450 L152,450 C150,360 152,300 160,262 C178,240 192,225 196,206 C170,200 130,196 92,204 C98,228 106,250 110,268 C112,320 110,380 108,450 Z");
        Sdf radius = Polygon(radiusPts);
        Sdf ulna = SmoothUnion(6, Capsule(236, 450, 222, 240, 14, 13), Ellipse(222, 230, 19, 15), Capsule(230, 220, 236, 200, 5, 4));
        switch (kind)
        {
            case 1:
                radius = Fracture(radius, 80, 238, 210, 246, 1.5f, -6, 4, -0.06f);
                c.R.Paint(Capsule(104, 241, 158, 245, 3), Rgb.Grey(0.16f), 1, 2, Blend.Add);
                c.Mark(130, 240, 42, "Fracture");
                break;
            case 2:
                radius = Union(radius, Circle(106, 252, 4.5f), Circle(158, 252, 4.5f));
                c.R.Paint(Capsule(110, 252, 154, 254, 1.6f), Rgb.Grey(0.12f), 1, 2, Blend.Add);
                c.Mark(132, 252, 38, "Buckle fracture");
                break;
        }

        Bone(c, radius, Box.Of(radiusPts).Grow(10), 0.3f, 0.24f, 5);
        Bone(c, ulna, new Box(195, 190, 260, 450), 0.28f, 0.22f, 4);
        var left = r.Next(2) == 0;
        return Finish(c, r, left ? "XR Left wrist (PA)" : "XR Right wrist (PA)", !left, "PA");
    }

    // ---------- shoulder ----------
    private static RenderedPicture Shoulder(Random r, bool dislocated)
    {
        var c = new PictureCanvas(360, 440, Film);
        SoftTissue(c, Polygon(Path("M-10,40 C120,30 260,40 330,90 C370,130 380,220 372,450 L-10,450 Z")), 0.17f, 22);
        for (var i = 0; i < 6; i++)
        {
            var y = 150 + (i * 48);
            c.R.Stroke(Polyline(Bezier((-10, y), (60, y - 40), (140, y - 30), (190, y + 10)), 0.1f), 7, Rgb.Grey(0.09f), 1, 2, Blend.Add);
        }

        Bone(c, Polyline(Bezier((-10, 64), (80, 50), (170, 78), (232, 76)), 11), new Box(-10, 40, 245, 95), 0.27f, 0.2f, 4);
        Bone(c, Capsule(226, 78, 296, 92, 14, 12), new Box(210, 60, 310, 110), 0.25f, 0.2f, 4);
        var scap = Path("M150,120 C190,110 215,118 228,128 L234,200 C220,260 180,330 140,400 C120,330 115,220 150,120 Z");
        Bone(c, Polygon(scap), Box.Of(scap), 0.13f, 0.1f, 5);
        Bone(c, Ellipse(232, 176, 11, 40), new Box(215, 130, 250, 220), 0.2f, 0.2f, 4);
        Bone(c, Capsule(206, 114, 246, 136, 9, 7), new Box(195, 100, 260, 150), 0.24f, 0.18f, 3);
        Sdf humerus = SmoothUnion(12, Circle(274, 182, 48), Ellipse(316, 172, 18, 24), Capsule(290, 218, 304, 460, 33, 30));
        if (dislocated)
        {
            humerus = Rotate(Move(humerus, -56, 48), 218, 230, 0.12f);
            c.Mark(222, 232, 60, "Dislocated humeral head");
        }

        Bone(c, humerus, new Box(150, 120, 360, 450), 0.3f, 0.24f, 6);
        var left = r.Next(2) == 0;
        return Finish(c, r, left ? "XR Left shoulder (AP)" : "XR Right shoulder (AP)", left, "AP");
    }

    // ---------- tibia ----------
    private static RenderedPicture Tibia(Random r, bool broken)
    {
        var c = new PictureCanvas(320, 460, Film);
        SoftTissue(c, Polygon(Path("M70,-10 L280,-10 C292,120 290,260 270,470 L96,470 C80,300 64,140 70,-10 Z")), 0.2f, 16);
        Sdf tibia = Capsule(150, -20, 160, 480, 33, 28);
        Sdf fibula = Capsule(246, -20, 236, 480, 12, 10);
        if (broken)
        {
            tibia = Fracture(tibia, 108, 268, 206, 168, 2.4f, 10, -12, 0.07f);
            c.Mark(156, 220, 58, "Spiral fracture");
        }

        Bone(c, tibia, new Box(100, -20, 220, 480), 0.32f, 0.3f, 10);
        Bone(c, fibula, new Box(215, -20, 270, 480), 0.28f, 0.22f, 4);
        var left = r.Next(2) == 0;
        return Finish(c, r, left ? "XR Left tibia and fibula (AP)" : "XR Right tibia and fibula (AP)", !left, "AP");
    }

    // ---------- knee ----------
    private static RenderedPicture Knee(Random r, bool effusion)
    {
        var c = new PictureCanvas(360, 440, Film);
        SoftTissue(c, Polygon(Path("M70,-10 L290,-10 C300,90 312,170 300,240 C292,320 280,380 276,450 L100,450 C96,380 76,320 66,240 C56,170 64,90 70,-10 Z")), 0.2f, 16);
        if (effusion)
        {
            c.R.Paint(Ellipse(180, 150, 126, 92), Rgb.Grey(0.1f), 1, 18, Blend.Add);
            c.R.Paint(Ellipse(66, 210, 30, 70), Rgb.Grey(0.08f), 1, 14, Blend.Add);
            c.R.Paint(Ellipse(298, 210, 30, 70), Rgb.Grey(0.08f), 1, 14, Blend.Add);
            c.Mark(180, 160, 108, "Joint effusion");
        }

        Sdf femur = Subtract(SmoothUnion(14, Capsule(180, -20, 180, 150, 38, 40), Ellipse(140, 192, 42, 34), Ellipse(222, 192, 42, 34)), Ellipse(181, 222, 13, 20));
        Bone(c, femur, new Box(80, -20, 280, 240), 0.3f, 0.24f, 6);
        var tib = Path("M118,236 L246,236 C252,262 242,292 222,306 L216,450 L150,450 L144,306 C124,292 112,262 118,236 Z");
        Bone(c, Union(Polygon(tib), Ellipse(172, 236, 6, 9), Ellipse(190, 236, 6, 9)), Box.Of(tib).Grow(10), 0.3f, 0.24f, 6);
        Bone(c, SmoothUnion(5, Ellipse(262, 302, 16, 20), Capsule(262, 312, 258, 460, 10)), new Box(230, 270, 290, 450), 0.26f, 0.2f, 4);
        Bone(c, Ellipse(182, 176, 34, 40), new Box(140, 130, 225, 225), 0.11f, 0.08f, 4);
        var left = r.Next(2) == 0;
        return Finish(c, r, left ? "XR Left knee (AP)" : "XR Right knee (AP)", !left, "AP");
    }

    // ---------- chest ----------
    private enum ChestFinding
    {
        None,
        SubtleLeftBase,
        RightLowerConsolidation,
        LeftBaseConsolidation,
        Oedema,
        Hyperinflation,
        Wedge,
        FreeAir,
        Patchy,
    }

    private static RenderedPicture Chest(Random r, ChestFinding f)
    {
        var c = new PictureCanvas(420, 460, Rgb.Grey(0.02f));
        var hyper = f == ChestFinding.Hyperinflation;
        var bigHeart = f == ChestFinding.Oedema;
        c.R.Paint(Polygon(Path("M6,470 L6,128 C8,86 58,66 120,60 L172,42 L248,42 L300,60 C362,66 412,86 414,128 L414,470 Z")), Rgb.Grey(0.3f), 1, 16, Blend.Over);

        // Lungs: dark air spaces.
        var diaphragmY = hyper ? 400 : 372;
        var rightLung = Polygon(Path($"M192,82 C150,80 104,102 88,152 C70,212 66,300 70,{diaphragmY} C110,{diaphragmY - 20} 160,{diaphragmY - 26} 196,{diaphragmY - 10} Z"));
        var leftLung = Polygon(Path($"M230,82 C274,80 318,102 334,152 C352,212 356,300 352,{diaphragmY + 6} C322,{diaphragmY - 4} 288,{diaphragmY - 10} 262,{diaphragmY - 4} C256,330 242,302 232,292 Z"));
        c.R.Paint(Union(rightLung, leftLung), Rgb.Grey(hyper ? 0.03f : 0.06f), 1, 9, Blend.Over);

        // Spine, trachea and heart.
        c.R.Paint(Rect(210, 250, 11, 220), Rgb.Grey(0.14f), 1, 4, Blend.Add);
        c.R.Paint(Capsule(210, 36, 210, 150, 7), Rgb.Grey(0.04f), 1, 3, Blend.Over);
        var heart = bigHeart
            ? Polygon(Path("M182,180 C236,172 320,214 336,288 C346,342 312,376 254,378 L160,378 C140,330 150,232 182,180 Z"))
            : hyper
                ? Polygon(Path("M196,196 C224,192 262,226 268,290 C272,350 252,388 226,392 L188,392 C180,340 182,250 196,196 Z"))
                : Polygon(Path("M194,196 C236,190 292,226 306,288 C316,340 292,372 252,374 L184,374 C174,330 178,248 194,196 Z"));
        c.R.Paint(heart, Rgb.Grey(0.42f), 1, 7, Blend.Over);
        c.R.Paint(Rect(206, 140, 30, 60, 0, 20), Rgb.Grey(0.42f), 1, 8, Blend.Over);

        // Below the diaphragm.
        var abd = Polygon(Path($"M30,{diaphragmY + 14} C92,{diaphragmY - 30} 160,{diaphragmY - 34} 202,{diaphragmY - 6} C250,{diaphragmY - 22} 318,{diaphragmY - 22} 390,{diaphragmY + 16} L414,470 L6,470 Z"));
        c.R.Paint(abd, Rgb.Grey(0.52f), 1, 6, Blend.Over);

        // Ribs and clavicles.
        var ribCount = hyper ? 10 : 9;
        for (var i = 0; i < ribCount; i++)
        {
            var y = 104 + (i * (hyper ? 28 : 29));
            c.R.Stroke(Polyline(Bezier((198, y), (150, y - 26), (92, y - 6), (68, y + 32)), 0.1f), 7, Rgb.Grey(0.075f), 1, 3, Blend.Add, new Box(60, y - 30, 205, y + 40));
            c.R.Stroke(Polyline(Bezier((222, y), (270, y - 26), (328, y - 6), (352, y + 32)), 0.1f), 7, Rgb.Grey(0.075f), 1, 3, Blend.Add, new Box(215, y - 30, 360, y + 40));
            c.R.Stroke(Polyline(Bezier((80, y + 50), (120, y + 32), (160, y + 42), (196, y + 60)), 0.1f), 5, Rgb.Grey(0.03f), 1, 3, Blend.Add, new Box(75, y + 25, 200, y + 65));
            c.R.Stroke(Polyline(Bezier((340, y + 50), (300, y + 32), (260, y + 42), (224, y + 60)), 0.1f), 5, Rgb.Grey(0.03f), 1, 3, Blend.Add, new Box(220, y + 25, 345, y + 65));
        }

        c.R.Paint(Polyline(Bezier((198, 96), (160, 80), (116, 74), (64, 86)), 6), Rgb.Grey(0.3f), 1, 2, Blend.Add);
        c.R.Paint(Polyline(Bezier((222, 96), (260, 80), (304, 74), (356, 86)), 6), Rgb.Grey(0.3f), 1, 2, Blend.Add);

        // Lung markings.
        for (var i = 0; i < 18; i++)
        {
            var side = i % 2 == 0 ? -1 : 1;
            var a = 0.4 + (r.NextDouble() * 1.4);
            var len = 50 + (r.NextDouble() * 70);
            var sx = 210 + (side * 30);
            var sy = 200 + (r.NextDouble() * 40);
            c.R.Stroke(Capsule((float)sx, (float)sy, (float)(sx + (side * Math.Sin(a) * len)), (float)(sy + (Math.Cos(a) * len * (r.Next(2) == 0 ? -1 : 1))), 0.1f), 2, Rgb.Grey(0.06f), 1, 2, Blend.Add);
        }

        switch (f)
        {
            case ChestFinding.RightLowerConsolidation:
                Haze(c, r, 128, diaphragmY - 50, 60, 44, 0.38f);
                c.R.Stroke(Polyline(new List<(float X, float Y)> { (112, 312), (126, 322), (136, 318) }, 0.1f), 2.5f, Rgb.Grey(0.04f), 1, 1.5f);
                c.Mark(128, diaphragmY - 52, 66, "Consolidation");
                break;
            case ChestFinding.LeftBaseConsolidation:
                Haze(c, r, 300, diaphragmY - 44, 52, 40, 0.38f);
                c.Mark(300, diaphragmY - 46, 60, "Consolidation");
                break;
            case ChestFinding.SubtleLeftBase:
                Haze(c, r, 306, diaphragmY - 40, 26, 20, 0.14f);
                c.Mark(306, diaphragmY - 40, 34, "Subtle shadowing");
                break;
            case ChestFinding.Oedema:
                Haze(c, r, 150, 230, 60, 80, 0.2f);
                Haze(c, r, 272, 230, 60, 80, 0.18f);
                c.R.Paint(Polygon(Path($"M64,{diaphragmY + 10} C70,{diaphragmY - 40} 84,{diaphragmY - 60} 100,{diaphragmY - 54} L120,{diaphragmY + 10} Z")), Rgb.Grey(0.5f), 1, 6);
                c.R.Paint(Polygon(Path($"M356,{diaphragmY + 14} C352,{diaphragmY - 36} 340,{diaphragmY - 56} 322,{diaphragmY - 50} L300,{diaphragmY + 14} Z")), Rgb.Grey(0.5f), 1, 6);
                for (var i = 0; i < 4; i++)
                {
                    c.R.Stroke(Capsule(176 - (i * 12), 170 - (i * 6), 130 - (i * 14), 110 + (i * 4), 0.1f), 3, Rgb.Grey(0.14f), 1, 2, Blend.Add);
                    c.R.Stroke(Capsule(244 + (i * 12), 170 - (i * 6), 290 + (i * 14), 110 + (i * 4), 0.1f), 3, Rgb.Grey(0.14f), 1, 2, Blend.Add);
                }

                c.Mark(250, 290, 92, "Enlarged heart");
                c.Mark(90, diaphragmY - 30, 40, "Effusion");
                break;
            case ChestFinding.Hyperinflation:
                c.Mark(130, diaphragmY - 6, 50, "Flat diaphragm");
                break;
            case ChestFinding.Wedge:
                c.R.Paint(Polygon(new List<(float X, float Y)> { (84, diaphragmY - 12), (130, diaphragmY - 70), (150, diaphragmY - 18) }), Rgb.Grey(0.3f), 1, 7);
                c.R.Paint(Polygon(Path($"M66,{diaphragmY + 8} C70,{diaphragmY - 20} 78,{diaphragmY - 30} 90,{diaphragmY - 26} L104,{diaphragmY + 8} Z")), Rgb.Grey(0.45f), 1, 5);
                c.Mark(112, diaphragmY - 40, 48, "Wedge opacity");
                break;
            case ChestFinding.FreeAir:
                c.R.Paint(Polygon(Path($"M70,{diaphragmY + 6} C100,{diaphragmY - 30} 160,{diaphragmY - 34} 196,{diaphragmY - 8} C160,{diaphragmY - 16} 110,{diaphragmY - 12} 70,{diaphragmY + 16} Z")), Rgb.Grey(0.02f), 1, 2);
                c.R.Stroke(Polyline(Bezier((72, diaphragmY + 4), (104, diaphragmY - 32), (160, diaphragmY - 36), (196, diaphragmY - 10)), 0.1f), 3, Rgb.Grey(0.55f), 1, 1.5f);
                c.Mark(134, diaphragmY - 16, 56, "Free air");
                break;
            case ChestFinding.Patchy:
                for (var i = 0; i < 7; i++)
                {
                    var x = i % 2 == 0 ? 110 + (r.NextDouble() * 60) : 260 + (r.NextDouble() * 60);
                    Haze(c, r, (float)x, (float)(150 + (r.NextDouble() * 190)), 20, 16, 0.2f);
                }

                c.Mark(210, 250, 150, "Patchy shadowing");
                break;
        }

        c.R.Blur(1);
        c.R.Grain(r, 0.05f);
        c.Label(16, 16, "R", 20, -1, true);
        c.Label(c.R.Width - 12, 14, hyper || f == ChestFinding.FreeAir ? "PA ERECT" : "PA", 12, 1);
        return c.Done("XR Chest (PA)");
    }

    private static void Haze(PictureCanvas c, Random r, float x, float y, float rx, float ry, float v)
    {
        c.R.Paint(Ellipse(x, y, rx, ry), Rgb.Grey(v * 0.7f), 1, rx * 0.7f, Blend.Add, Box.Around(x, y, rx, ry));
        for (var i = 0; i < 5; i++)
        {
            var dx = (float)((r.NextDouble() - 0.5) * rx * 1.2);
            var dy = (float)((r.NextDouble() - 0.5) * ry * 1.2);
            c.R.Paint(Ellipse(x + dx, y + dy, rx * 0.35f, ry * 0.35f), Rgb.Grey(v * 0.25f), 1, rx * 0.3f, Blend.Add, Box.Around(x + dx, y + dy, rx * 0.35f, ry * 0.35f));
        }
    }

    // ---------- abdomen ----------
    private static RenderedPicture Abdomen(Random r, int kind)
    {
        var c = new PictureCanvas(380, 460, Rgb.Grey(0.02f));
        c.R.Paint(Polygon(Path("M20,-10 L360,-10 C372,140 376,300 360,470 L20,470 C4,300 8,140 20,-10 Z")), Rgb.Grey(0.36f), 1, 18);
        c.R.Paint(Polygon(Path("M30,-10 L190,-10 C200,40 196,90 160,120 C110,140 60,130 30,110 Z")), Rgb.Grey(0.06f), 1, 18, Blend.Add);
        for (var i = 0; i < 6; i++)
        {
            var y = 20 + (i * 56);
            var v = Rect(190, y, 22, 22, 0, 4);
            c.R.Paint(v, Rgb.Grey(0.18f), 1, 2, Blend.Add);
            c.R.PaintBand(v, 3, Rgb.Grey(0.1f), 1, 1, Blend.Add);
            c.R.Paint(Capsule(160, y, 220, y, 4), Rgb.Grey(0.08f), 1, 2, Blend.Add);
        }

        var wings = Path("M40,470 C40,400 60,350 120,330 C150,340 170,380 178,420 L178,470 Z");
        Bone(c, Polygon(wings), Box.Of(wings), 0.16f, 0.14f, 5);
        var wings2 = Path("M340,470 C340,400 320,350 260,330 C230,340 210,380 202,420 L202,470 Z");
        Bone(c, Polygon(wings2), Box.Of(wings2), 0.16f, 0.14f, 5);

        void Gas(Sdf s, float v = 0.22f, float soft = 3) => c.R.Paint(s, Rgb.Grey(-v), 1, soft, Blend.Add);

        switch (kind)
        {
            case 2:
                Gas(Polyline(new List<(float X, float Y)> { (70, 330), (66, 200), (90, 110), (190, 92), (290, 108), (316, 200), (310, 330), (250, 380), (196, 372) }, 34), 0.3f, 4);
                for (var i = 0; i < 9; i++)
                {
                    var t = i / 8f;
                    var x = i < 3 ? 40 + (t * 20) : i < 6 ? 120 + ((i - 3) * 70) : 280 + (t * 20);
                    var y = i < 3 ? 300 - (i * 70) : i < 6 ? 98 : 140 + ((i - 6) * 70);
                    if (i is >= 3 and < 6)
                    {
                        c.R.Paint(Capsule(x, y - 26, x + 6, y + 6, 1.6f), Rgb.Grey(0.16f), 1, 1.5f, Blend.Add);
                    }
                    else
                    {
                        c.R.Paint(Capsule(x + 12, y, x + 44, y + 4, 1.6f), Rgb.Grey(0.16f), 1, 1.5f, Blend.Add);
                    }
                }

                c.Mark(190, 230, 170, "Dilated large bowel");
                break;
            case 3:
                for (var row = 0; row < 4; row++)
                {
                    var y = 150 + (row * 46);
                    var loop = Capsule(110 + (row % 2 * 20), y, 270 - (row % 2 * 20), y + 6, 18);
                    Gas(loop, 0.28f);
                    for (var k = 0; k < 14; k++)
                    {
                        var x = 116 + (row % 2 * 20) + (k * 11);
                        c.R.Paint(Capsule(x, y - 16, x + 1, y + 20, 1), Rgb.Grey(0.12f), 1, 1, Blend.Add);
                    }
                }

                c.Mark(190, 220, 120, "Dilated small bowel");
                break;
            default:
                foreach (var (x, y, rx, ry) in new[] { (70f, 300f, 16f, 10f), (82, 180, 14, 9), (140, 104, 18, 8), (240, 108, 20, 9), (306, 170, 13, 9), (300, 290, 12, 8), (220, 372, 22, 10) })
                {
                    Gas(Ellipse(x + (float)(r.NextDouble() * 8), y, rx, ry), 0.16f, 3);
                }

                if (kind == 1)
                {
                    Gas(Capsule(150, 220, 220, 226, 14), 0.2f);
                    Gas(Capsule(160, 260, 230, 262, 13), 0.2f);
                    c.Mark(190, 242, 62, "Mildly dilated loops");
                }

                break;
        }

        c.R.Blur(1);
        c.R.Grain(r, 0.05f);
        c.Label(16, 16, "R", 20, -1, true);
        c.Label(c.R.Width - 12, c.R.Height - 20, "SUPINE", 12, 1);
        return c.Done("XR Abdomen (supine)");
    }
}
