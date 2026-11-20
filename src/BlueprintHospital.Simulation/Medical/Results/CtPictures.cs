using static BlueprintHospital.Simulation.Medical.Results.Shapes;

namespace BlueprintHospital.Simulation.Medical.Results;

/// <summary>CT slices: head (brain window), CT pulmonary angiogram and abdomen (soft tissue window).</summary>
internal static class CtPictures
{
    public static void Register(PictureRegistrar add)
    {
        add("cth_normal", PictureKind.Image, "CT head, normal", r => Head(r, 0));
        add("cth_sah", PictureKind.Image, "CT head, subarachnoid haemorrhage", r => Head(r, 1));
        add("cth_mca_ischaemia", PictureKind.Image, "CT head, early left MCA infarct", r => Head(r, 2));
        add("cth_scalp_swelling", PictureKind.Image, "CT head, scalp swelling only", r => Head(r, 3));
        add("cth_old_infarct", PictureKind.Image, "CT head, small old infarct", r => Head(r, 4));
        add("cth_old_scar", PictureKind.Image, "CT head, old right temporal scar", r => Head(r, 5));

        add("ctpa_normal", PictureKind.Image, "CTPA, no clot", r => Ctpa(r, 0));
        add("ctpa_bilateral", PictureKind.Image, "CTPA, clots in both main pulmonary arteries", r => Ctpa(r, 1));
        add("ctpa_segmental", PictureKind.Image, "CTPA, right lower lobe segmental clot", r => Ctpa(r, 2));
        add("ctpa_subsegmental", PictureKind.Image, "CTPA, possible small subsegmental clot", r => Ctpa(r, 3));

        add("cta_normal", PictureKind.Image, "CT abdomen, normal", r => Abdomen(r, AbdoFinding.None));
        add("cta_free_fluid", PictureKind.Image, "CT abdomen, small pelvic free fluid", r => Abdomen(r, AbdoFinding.FreeFluid));
        add("cta_sbo", PictureKind.Image, "CT abdomen, small bowel obstruction", r => Abdomen(r, AbdoFinding.Sbo));
        add("cta_perforation", PictureKind.Image, "CT abdomen, free air and fluid (perforation)", r => Abdomen(r, AbdoFinding.Perforation));
        add("cta_appendicitis", PictureKind.Image, "CT abdomen, inflamed appendix", r => Abdomen(r, AbdoFinding.Appendicitis));
        add("cta_pancreatitis", PictureKind.Image, "CT abdomen, swollen pancreas with fluid", r => Abdomen(r, AbdoFinding.Pancreatitis));
        add("cta_hernia", PictureKind.Image, "CT abdomen, bowel trapped in a right inguinal hernia", r => Abdomen(r, AbdoFinding.Hernia));
        add("cta_diverticulitis", PictureKind.Image, "CT abdomen, sigmoid diverticulitis", r => Abdomen(r, AbdoFinding.Diverticulitis));
        add("cta_lbo_tumour", PictureKind.Image, "CT abdomen, sigmoid tumour with dilated colon", r => Abdomen(r, AbdoFinding.Tumour));
    }

    private static RenderedPicture Finish(PictureCanvas c, Random r, string title, string window, string slice)
    {
        c.R.Blur(1);
        c.R.Grain(r, 0.05f);
        c.Label(12, 12, slice, 12);
        c.Label(c.R.Width - 12, 12, window, 12, 1);
        c.Label(14, (c.R.Height / 2) - 10, "R", 18);
        c.Label(c.R.Width - 14, (c.R.Height / 2) - 10, "L", 18, 1);
        return c.Done(title);
    }

    private static RenderedPicture Head(Random r, int kind)
    {
        var c = new PictureCanvas(400, 420, Rgb.Grey(0));
        const float cx = 200, cy = 212;
        c.R.Paint(Ellipse(cx, cy, 154, 184), Rgb.Grey(0.2f), 1, 4);
        if (kind == 3)
        {
            c.R.Paint(Ellipse(108, 70, 46, 26, -0.6f), Rgb.Grey(0.24f), 1, 6);
            c.Mark(104, 72, 40, "Scalp swelling");
        }

        c.R.Paint(Ellipse(cx, cy, 130, 160), Rgb.Grey(0.44f), 1, 2);
        c.R.Stroke(Ellipse(cx, cy, 138, 168), 15, Rgb.Grey(0.96f), 1, 1.5f);

        // Grey and white matter: a slightly darker core with soft sulci at the edges.
        c.R.Paint(Ellipse(cx - 52, cy - 20, 52, 92), Rgb.Grey(-0.05f), 1, 26, Blend.Add);
        c.R.Paint(Ellipse(cx + 52, cy - 20, 52, 92), Rgb.Grey(-0.05f), 1, 26, Blend.Add);
        for (var i = 0; i < 26; i++)
        {
            var a = (float)(i / 26.0 * Math.PI * 2);
            var x0 = cx + (MathF.Cos(a) * 124);
            var y0 = cy + (MathF.Sin(a) * 152);
            var x1 = cx + (MathF.Cos(a) * 104);
            var y1 = cy + (MathF.Sin(a) * 128);
            c.R.Stroke(Capsule(x0, y0, x1, y1, 0.1f), 2.5f, Rgb.Grey(-0.1f), 1, 1.5f, Blend.Add);
        }

        var blood = kind == 1;
        var csf = Rgb.Grey(blood ? 0.86f : 0.16f);
        c.R.Paint(Polyline(Bezier((cx, 58), (cx - 1, 100), (cx + 1, 140), (cx, 178)), blood ? 2.6f : 1.6f), blood ? csf : Rgb.Grey(0.6f), 1, 2);
        c.R.Paint(Polyline(Bezier((cx - 28, 206), (cx - 50, 206), (cx - 62, 186), (cx - 84, 184)).Concat(Bezier((cx - 84, 184), (cx - 98, 182), (cx - 108, 170), (cx - 114, 160))).ToList(), 3.2f), csf, 1, 2);
        c.R.Paint(Polyline(Bezier((cx + 28, 206), (cx + 50, 206), (cx + 62, 188), (cx + 84, 186)).Concat(Bezier((cx + 84, 186), (cx + 98, 184), (cx + 108, 172), (cx + 114, 162))).ToList(), 3.2f), csf, 1, 2);
        c.R.Paint(Polygon(Path("M200,184 C206,192 212,196 230,195 C224,203 220,210 226,232 C214,226 206,224 200,226 C192,224 184,227 172,233 C179,214 175,204 168,196 C184,197 194,193 200,184 Z")), csf, 1, 2.5f);
        c.R.Stroke(Circle(cx, 262, 28), blood ? 6 : 4, csf, 1, 2);
        c.R.Paint(Circle(cx, 262, 25), Rgb.Grey(0.48f), 1, 2);
        c.R.Paint(Ellipse(136, 246, 7, 11), Rgb.Grey(0.15f), 1, 2);
        c.R.Paint(Ellipse(264, 246, 7, 11), Rgb.Grey(0.15f), 1, 2);
        c.R.Paint(Rect(cx, 306, 5, 8), Rgb.Grey(0.15f), 1, 2);
        c.R.Paint(Ellipse(cx, 340, 72, 30), Rgb.Grey(0.46f), 1, 6);

        switch (kind)
        {
            case 1:
                c.Mark(cx, 220, 80, "Blood in the cisterns");
                break;
            case 2:
                c.R.Paint(Polygon(Path("M246,130 C290,120 318,170 322,220 C324,262 300,290 270,284 C254,250 248,190 246,130 Z")), Rgb.Grey(-0.07f), 1, 10, Blend.Add);
                c.R.Paint(Capsule(226, 204, 262, 200, 2.5f), Rgb.Grey(0.3f), 1, 1.5f, Blend.Add);
                c.Mark(286, 206, 60, "Early infarct");
                break;
            case 4:
                c.R.Paint(Ellipse(286, 132, 12, 9), Rgb.Grey(0.16f), 1, 2);
                c.Mark(286, 132, 26, "Old infarct");
                break;
            case 5:
                c.R.Paint(Ellipse(112, 250, 14, 10, 0.4f), Rgb.Grey(0.17f), 1, 2);
                c.Mark(112, 250, 28, "Old scar");
                break;
        }

        return Finish(c, r, "CT Head (non-contrast)", "W 80  L 40", "AXIAL 5 mm");
    }

    private static RenderedPicture Ctpa(Random r, int kind)
    {
        var c = new PictureCanvas(440, 340, Rgb.Grey(0));
        c.R.Paint(Ellipse(220, 175, 204, 148), Rgb.Grey(0.24f), 1, 3);
        c.R.Paint(Ellipse(220, 175, 188, 134), Rgb.Grey(0.44f), 1, 3);
        c.R.Paint(Ellipse(220, 178, 178, 124), Rgb.Grey(0.3f), 1, 3);
        c.R.Paint(Ellipse(130, 170, 82, 104, 0.08f), Rgb.Grey(0.02f), 1, 3);
        c.R.Paint(Ellipse(312, 170, 80, 104, -0.08f), Rgb.Grey(0.02f), 1, 3);
        c.R.Paint(Ellipse(220, 150, 52, 92), Rgb.Grey(0.42f), 1, 6);
        for (var i = 0; i < 40; i++)
        {
            var left = i % 2 == 0;
            var x = (float)((left ? 130 : 312) + ((r.NextDouble() - 0.5) * 120));
            var y = (float)(170 + ((r.NextDouble() - 0.5) * 160));
            c.R.Paint(Circle(x, y, (float)(1.5 + (r.NextDouble() * 2.5))), Rgb.Grey(0.6f), 1, 1);
        }

        var vessel = Rgb.Grey(0.92f);
        c.R.Paint(Circle(196, 140, 24), vessel, 1, 1.5f);
        c.R.Paint(Ellipse(246, 128, 30, 25), vessel, 1, 1.5f);
        c.R.Paint(Capsule(240, 136, 150, 152, 14, 11), vessel, 1, 1.5f);
        c.R.Paint(Capsule(254, 124, 314, 114, 13, 10), vessel, 1, 1.5f);
        c.R.Paint(Capsule(150, 152, 116, 212, 8, 6), vessel, 1, 1.5f);
        c.R.Paint(Circle(256, 226, 19), vessel, 1, 1.5f);
        c.R.Paint(Circle(220, 278, 30), Rgb.Grey(0.85f), 1, 2);
        c.R.Paint(Circle(220, 278, 18), Rgb.Grey(0.5f), 1, 2);
        c.R.Paint(Rect(220, 44, 22, 8, 0, 4), Rgb.Grey(0.85f), 1, 2);
        var clot = Rgb.Grey(0.36f);
        switch (kind)
        {
            case 1:
                c.R.Paint(Ellipse(240, 138, 18, 8, 0.2f), clot, 1, 2);
                c.R.Paint(Ellipse(176, 148, 16, 6, 0.15f), clot, 1, 2);
                c.R.Paint(Ellipse(290, 118, 14, 6, -0.15f), clot, 1, 2);
                c.Mark(236, 134, 82, "Clots in both arteries");
                break;
            case 2:
                c.R.Paint(Ellipse(126, 196, 5, 4), clot, 1, 1.5f);
                c.Mark(126, 196, 26, "Clot");
                break;
            case 3:
                c.R.Paint(Circle(118, 208, 2.4f), clot, 1, 1);
                c.Mark(118, 208, 20, "Possible small clot");
                break;
        }

        return Finish(c, r, "CT Pulmonary angiogram", "W 400  L 40", "AXIAL 1 mm");
    }

    private enum AbdoFinding
    {
        None,
        FreeFluid,
        Sbo,
        Perforation,
        Appendicitis,
        Pancreatitis,
        Hernia,
        Diverticulitis,
        Tumour,
    }

    private static RenderedPicture Abdomen(Random r, AbdoFinding f)
    {
        var c = new PictureCanvas(440, 340, Rgb.Grey(0));
        var fat = Rgb.Grey(0.22f);
        var body = Ellipse(220, 172, 200, 146);
        if (f == AbdoFinding.Hernia)
        {
            body = SmoothUnion(16, body, Circle(102, 304, 34));
        }

        c.R.Paint(body, fat, 1, 2);
        c.R.Stroke(Ellipse(220, 172, 178, 126), 10, Rgb.Grey(0.46f), 1, 2);
        c.R.Paint(Ellipse(220, 176, 172, 120), fat, 1, 2);
        c.R.Paint(Polygon(Path("M60,120 C80,70 150,46 200,60 C214,90 200,140 180,168 C140,200 90,200 64,176 Z")), Rgb.Grey(0.56f), 1, 2);
        c.R.Paint(Ellipse(346, 150, 38, 56, 0.3f), Rgb.Grey(0.54f), 1, 2);
        c.R.Paint(Ellipse(292, 86, 50, 30), Rgb.Grey(0.42f), 1, 2);
        c.R.Paint(Ellipse(292, 72, 34, 11), Rgb.Grey(0), 1, 1.5f);
        foreach (var x in new[] { 142f, 300f })
        {
            c.R.Paint(Ellipse(x, 222, 26, 36), Rgb.Grey(0.72f), 1, 2);
            c.R.Paint(Ellipse(x + (x < 200 ? 8 : -8), 222, 10, 18), Rgb.Grey(0.5f), 1, 2);
        }

        c.R.Paint(Circle(234, 238, 12), Rgb.Grey(0.86f), 1, 1.5f);
        c.R.Paint(Ellipse(204, 234, 14, 10), Rgb.Grey(0.6f), 1, 1.5f);
        c.R.Paint(Circle(220, 278, 30), Rgb.Grey(0.88f), 1, 2);
        c.R.Paint(Circle(220, 278, 18), Rgb.Grey(0.5f), 1, 2);
        c.R.Paint(Ellipse(180, 270, 18, 24), Rgb.Grey(0.46f), 1, 2);
        c.R.Paint(Ellipse(262, 270, 18, 24), Rgb.Grey(0.46f), 1, 2);
        var pancreasR = f == AbdoFinding.Pancreatitis ? 22f : 12f;
        c.R.Paint(Capsule(186, 176, 300, 160, pancreasR), Rgb.Grey(0.5f), 1, 3);

        void Loop(float x, float y, float rad, bool gas, bool fluid)
        {
            c.R.Paint(Circle(x, y, rad), Rgb.Grey(0.62f), 1, 1.5f);
            c.R.Paint(Circle(x, y, rad - 2.5f), Rgb.Grey(fluid ? 0.36f : 0.44f), 1, 1.5f);
            if (gas)
            {
                c.R.Paint(Intersect(Circle(x, y, rad - 2.5f), (px, py) => py - (y - (rad * 0.25f))), Rgb.Grey(0.0f), 1, 1);
            }
        }

        if (f == AbdoFinding.Sbo)
        {
            foreach (var (x, y) in new[] { (170f, 110f), (230f, 104f), (150f, 160f), (214f, 156f), (270f, 120f), (250f, 186f) })
            {
                Loop(x, y, 26, r.Next(3) > 0, true);
            }

            c.Mark(210, 146, 88, "Dilated small bowel");
        }
        else
        {
            foreach (var (x, y) in new[] { (190f, 100f), (226f, 96f), (170f, 132f), (246f, 128f), (210f, 120f) })
            {
                Loop(x + (float)(r.NextDouble() * 6), y, 11, r.Next(2) == 0, false);
            }
        }

        switch (f)
        {
            case AbdoFinding.FreeFluid:
                c.R.Paint(Ellipse(260, 300, 30, 10, 0.1f), Rgb.Grey(0.32f), 1, 3);
                c.Mark(260, 300, 34, "Free fluid");
                break;
            case AbdoFinding.Perforation:
                c.R.Paint(Ellipse(176, 66, 54, 7, -0.12f), Rgb.Grey(0), 1, 2);
                c.R.Paint(Circle(214, 140, 5), Rgb.Grey(0), 1, 1);
                c.R.Paint(Circle(226, 150, 3), Rgb.Grey(0), 1, 1);
                c.R.Paint(Ellipse(70, 150, 12, 40, 0.3f), Rgb.Grey(0.32f), 1, 3);
                c.Mark(176, 68, 60, "Free air");
                break;
            case AbdoFinding.Appendicitis:
                c.R.Paint(Ellipse(116, 262, 34, 26), Rgb.Grey(0.32f), 1, 14);
                c.R.Paint(Capsule(104, 248, 132, 278, 9), Rgb.Grey(0.66f), 1, 1.5f);
                c.R.Paint(Capsule(104, 248, 132, 278, 5), Rgb.Grey(0.4f), 1, 1.5f);
                c.Mark(118, 262, 38, "Inflamed appendix");
                break;
            case AbdoFinding.Pancreatitis:
                c.R.Paint(Ellipse(244, 182, 76, 34, -0.12f), Rgb.Grey(0.34f), 0.7f, 16);
                c.Mark(242, 168, 72, "Swollen pancreas");
                break;
            case AbdoFinding.Hernia:
                Loop(100, 306, 18, true, true);
                c.Mark(100, 306, 36, "Trapped bowel");
                break;
            case AbdoFinding.Diverticulitis:
                c.R.Paint(Ellipse(318, 270, 44, 30), Rgb.Grey(0.32f), 1, 14);
                c.R.Paint(Capsule(296, 256, 340, 284, 12), Rgb.Grey(0.6f), 1, 1.5f);
                c.R.Paint(Capsule(296, 256, 340, 284, 6), Rgb.Grey(0.42f), 1, 1.5f);
                foreach (var (x, y) in new[] { (300f, 244f), (322f, 254f), (342f, 270f), (312f, 280f) })
                {
                    c.R.Paint(Circle(x, y, 3.5f), Rgb.Grey(0), 1, 1);
                }

                c.Mark(318, 268, 44, "Diverticulitis");
                break;
            case AbdoFinding.Tumour:
                foreach (var (x, y) in new[] { (96f, 230f), (344f, 236f), (300f, 70f) })
                {
                    c.R.Paint(Circle(x, y, 30), Rgb.Grey(0.62f), 1, 1.5f);
                    c.R.Paint(Circle(x, y, 27), Rgb.Grey(0.02f), 1, 1.5f);
                }

                c.R.Paint(Ellipse(316, 290, 22, 16), Rgb.Grey(0.5f), 1, 3);
                c.Mark(316, 290, 30, "Tumour");
                break;
        }

        return Finish(c, r, "CT Abdomen and pelvis (portal venous)", "W 400  L 50", "AXIAL 5 mm");
    }
}
