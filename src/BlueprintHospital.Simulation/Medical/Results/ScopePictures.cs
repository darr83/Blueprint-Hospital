using static BlueprintHospital.Simulation.Medical.Results.Shapes;

namespace BlueprintHospital.Simulation.Medical.Results;

/// <summary>Colour pictures: endoscopy camera views and the bedside pregnancy test cassette.</summary>
internal static class ScopePictures
{
    public static void Register(PictureRegistrar add)
    {
        add("ogd_normal", PictureKind.Image, "Endoscopy (OGD), normal", r => Upper(r, 0));
        add("ogd_gastritis", PictureKind.Image, "Endoscopy (OGD), red stomach lining", r => Upper(r, 1));
        add("ogd_bleeding_ulcer", PictureKind.Image, "Endoscopy (OGD), duodenal ulcer treated with clips", r => Upper(r, 2));
        add("ogd_oesophagitis", PictureKind.Image, "Endoscopy (OGD), inflamed lower gullet", r => Upper(r, 3));
        add("sig_normal", PictureKind.Image, "Flexible sigmoidoscopy, normal", r => Lower(r, 0));
        add("sig_piles", PictureKind.Image, "Flexible sigmoidoscopy, small piles", r => Lower(r, 1));
        add("sig_colitis", PictureKind.Image, "Flexible sigmoidoscopy, severe ulcerative colitis", r => Lower(r, 2));
        add("sig_diverticula", PictureKind.Image, "Flexible sigmoidoscopy, diverticula", r => Lower(r, 3));
        add("preg_negative", PictureKind.Image, "Pregnancy test cassette, negative (one line)", _ => Cassette(false));
        add("preg_positive", PictureKind.Image, "Pregnancy test cassette, positive (two lines)", _ => Cassette(true));
    }

    private const float Cx = 200;
    private const float Cy = 200;

    private static void Scope(PictureCanvas c, Random r, Rgb mucosa, Rgb deep, float lumenX, float lumenY, float lumenR)
    {
        c.R.Paint(Circle(Cx, Cy, 190), mucosa, 1, 2);
        c.R.Paint(Circle(lumenX, lumenY, lumenR * 2.4f), deep, 0.7f, lumenR * 1.6f);
        c.R.Paint(Circle(lumenX, lumenY, lumenR), Rgb.Hex(0x1A0606), 1, lumenR * 0.6f);
        c.R.Paint(Circle(Cx, Cy, 190), Rgb.Grey(0.35f), 0.6f, 60, Blend.Multiply);
        c.R.Paint(Ellipse(Cx, Cy, 90, 90), Rgb.Grey(1), 0.08f, 80, Blend.Add);
    }

    private static void Vessels(PictureCanvas c, Random r, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var x = (float)(40 + (r.NextDouble() * 320));
            var y = (float)(40 + (r.NextDouble() * 320));
            var a = r.NextDouble() * Math.PI * 2;
            var len = 30 + (r.NextDouble() * 40);
            var ex = x + (float)(Math.Cos(a) * len);
            var ey = y + (float)(Math.Sin(a) * len);
            var bend = (float)((r.NextDouble() - 0.5) * 30);
            var pts = Bezier((x, y), (x + ((ex - x) / 3) + bend, y + ((ey - y) / 3) - bend), (x + (2 * (ex - x) / 3) - bend, y + (2 * (ey - y) / 3) + bend), (ex, ey), 8);
            c.R.Paint(Polyline(pts, 1.0f, 0.4f), Rgb.Hex(0xA8313A), 0.3f, 1.2f, Blend.Over, Box.Of(pts).Grow(3));
        }
    }

    private static void Shine(PictureCanvas c, Random r, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var a = r.NextDouble() * Math.PI * 2;
            var d = 50 + (r.NextDouble() * 90);
            var sx = (float)(Cx + (Math.Cos(a) * d));
            var sy = (float)(Cy + (Math.Sin(a) * d));
            c.R.Paint(Ellipse(sx, sy, (float)(5 + (r.NextDouble() * 8)), (float)(1.5 + (r.NextDouble() * 2)), (float)(a + (Math.PI / 2))), Rgb.Grey(1), 0.7f, 2.5f, Blend.Over, Box.Around(sx, sy, 14, 14));
        }
    }

    private static RenderedPicture Finish(PictureCanvas c, Random r, string title, string place)
    {
        c.R.Grain(r, 0.03f);
        c.R.Mask(Circle(Cx, Cy, 190), Rgb.Grey(0.02f), 1.5f);
        c.Label(10, 10, place, 12);
        c.Label(c.R.Width - 10, 10, "HD SCOPE", 11, 1);
        return c.Done(title);
    }

    private static RenderedPicture Upper(Random r, int kind)
    {
        var c = new PictureCanvas(400, 400, Rgb.Grey(0.02f));
        var place = kind switch { 2 => "D1 DUODENAL BULB", 3 => "LOWER OESOPHAGUS", _ => "STOMACH BODY" };
        var mucosa = kind == 3 ? Rgb.Hex(0xE8B2A8) : Rgb.Hex(0xE79A8E);
        Scope(c, r, mucosa, Rgb.Hex(0x8E3A35), kind == 3 ? 200 : 236, kind == 3 ? 196 : 176, kind == 3 ? 26 : 32);
        if (kind is 0 or 1)
        {
            for (var i = 0; i < 6; i++)
            {
                var y = 70 + (i * 50);
                c.R.Stroke(Polyline(Bezier((40, y + (float)(r.NextDouble() * 20)), (140, y - 50), (260, y + 40), (370, y - (float)(r.NextDouble() * 20))), 0.1f), 12, Rgb.Hex(0xF4B8AE), 0.35f, 10);
            }
        }

        Vessels(c, r, kind == 1 ? 6 : 14);
        switch (kind)
        {
            case 1:
                for (var i = 0; i < 9; i++)
                {
                    c.R.Paint(Ellipse((float)(70 + (r.NextDouble() * 260)), (float)(70 + (r.NextDouble() * 260)), 26, 16, (float)r.NextDouble()), Rgb.Hex(0xC8303A), 0.55f, 14);
                }

                c.Mark(200, 200, 150, "Red, inflamed lining");
                break;
            case 2:
                c.R.Paint(Ellipse(150, 236, 46, 34, 0.3f), Rgb.Hex(0xB8303A), 0.8f, 10);
                c.R.Paint(Ellipse(150, 236, 30, 22, 0.3f), Rgb.Hex(0xF0E7C8), 1, 3);
                c.R.Paint(Circle(160, 240, 8), Rgb.Hex(0x3A0A0E), 1, 2);
                c.R.Paint(Polyline(new List<(float X, float Y)> { (160, 250), (176, 290), (170, 340) }, 5, 2), Rgb.Hex(0x9E0E1A), 0.9f, 2);
                foreach (var (x, y, a) in new[] { (126f, 214f, 0.6f), (176f, 226f, -0.4f) })
                {
                    c.R.Paint(Rect(x, y, 20, 4, a, 2), Rgb.Hex(0x8C949C), 1, 1.2f);
                    c.R.Paint(Rect(x - 2, y - 1, 16, 1.2f, a), Rgb.Grey(1), 0.8f, 1);
                    c.R.Paint(Rect(x + 18, y + 10, 4, 12, a, 2), Rgb.Hex(0x6E767E), 1, 1.2f);
                }

                c.Mark(150, 236, 62, "Ulcer with clips");
                break;
            case 3:
                for (var i = 0; i < 6; i++)
                {
                    var a = (float)(i / 6.0 * Math.PI * 2) + 0.3f;
                    c.R.Paint(Capsule(200 + (MathF.Cos(a) * 40), 196 + (MathF.Sin(a) * 40), 200 + (MathF.Cos(a) * (110 + (float)(r.NextDouble() * 50))), 196 + (MathF.Sin(a) * (110 + (float)(r.NextDouble() * 50))), 8, 1.5f), Rgb.Hex(0xC42A34), 0.6f, 6);
                }

                c.Mark(200, 196, 130, "Inflamed gullet");
                break;
        }

        Shine(c, r, 4);
        return Finish(c, r, "OGD (upper GI endoscopy)", place);
    }

    private static RenderedPicture Lower(Random r, int kind)
    {
        var c = new PictureCanvas(400, 400, Rgb.Grey(0.02f));
        var mucosa = kind == 2 ? Rgb.Hex(0xC9454A) : Rgb.Hex(0xEAA48A);
        Scope(c, r, mucosa, Rgb.Hex(0x7A3426), 214, 186, 30);
        for (var i = 1; i <= 4; i++)
        {
            c.R.Stroke(Ellipse(214 - (i * 4), 186 + (i * 4), 34 + (i * 36), 30 + (i * 34)), 7, kind == 2 ? Rgb.Hex(0xD8606A) : Rgb.Hex(0xF5BFA6), 0.65f, 5);
        }

        if (kind != 2)
        {
            Vessels(c, r, 18);
        }

        switch (kind)
        {
            case 1:
                c.R.Paint(Ellipse(80, 300, 44, 30, 0.6f), Rgb.Hex(0x7A3A6A), 0.75f, 8);
                c.R.Paint(Ellipse(310, 320, 36, 24, -0.5f), Rgb.Hex(0x7A3A6A), 0.7f, 8);
                c.Mark(90, 300, 52, "Small piles");
                break;
            case 2:
                for (var i = 0; i < 16; i++)
                {
                    var ux = (float)(50 + (r.NextDouble() * 300));
                    var uy = (float)(50 + (r.NextDouble() * 300));
                    c.R.Paint(Circle(ux, uy, 9), Rgb.Hex(0x9E1820), 0.5f, 5, Blend.Over, Box.Around(ux, uy, 14, 14));
                    c.R.Paint(Ellipse(ux, uy, (float)(3 + (r.NextDouble() * 4)), (float)(2 + (r.NextDouble() * 3))), Rgb.Hex(0xF2E2BE), 0.9f, 1.5f, Blend.Over, Box.Around(ux, uy, 10, 10));
                }

                c.R.Paint(Polyline(new List<(float X, float Y)> { (120, 250), (130, 300), (126, 360) }, 6, 2), Rgb.Hex(0x8A0A12), 0.85f, 2);
                c.Mark(200, 200, 150, "Ulcerated, bleeding lining");
                break;
            case 3:
                foreach (var (x, y) in new[] { (110f, 120f), (300f, 250f), (140f, 290f), (290f, 110f) })
                {
                    c.R.Paint(Ellipse(x, y, 14, 11), Rgb.Hex(0x2A0A08), 1, 4);
                }

                c.R.Paint(Ellipse(300, 250, 34, 30), Rgb.Hex(0xD0404A), 0.35f, 12);
                c.Mark(300, 250, 34, "Diverticulum");
                break;
        }

        Shine(c, r, 4);
        return Finish(c, r, "Flexible sigmoidoscopy", "SIGMOID 25 cm");
    }

    private static RenderedPicture Cassette(bool positive)
    {
        var c = new PictureCanvas(420, 170, Rgb.Hex(0xEEF2F5));
        c.R.Paint(Rect(212, 92, 196, 48, 0, 30), Rgb.Hex(0xC6CFD7), 0.6f, 8);
        c.R.Paint(Rect(210, 86, 196, 48, 0, 30), Rgb.Hex(0xFAFBFC), 1, 1.2f);
        c.R.Stroke(Rect(210, 86, 196, 48, 0, 30), 2, Rgb.Hex(0xC2CBD3), 1, 1);
        c.R.Paint(Circle(70, 86, 18), Rgb.Hex(0xE3E8ED), 1, 1.2f);
        c.R.Stroke(Circle(70, 86, 18), 2, Rgb.Hex(0xC2CBD3), 1, 1);
        c.R.Paint(Rect(250, 86, 70, 18, 0, 6), Rgb.Hex(0xFFFFFF), 1, 1);
        c.R.Stroke(Rect(250, 86, 70, 18, 0, 6), 2, Rgb.Hex(0xC2CBD3), 1, 1);
        c.R.Paint(Rect(276, 86, 3, 15), Rgb.Hex(0xD23A62), 1, 1);
        if (positive)
        {
            c.R.Paint(Rect(228, 86, 3, 15), Rgb.Hex(0xD23A62), 0.9f, 1);
            c.Mark(229, 86, 22, "Test line");
        }

        c.Label(70, 116, "S", 13, 0);
        c.Label(229, 112, "T", 13, 0);
        c.Label(277, 112, "C", 13, 0);
        return c.Done("Urine hCG test cassette");
    }
}
