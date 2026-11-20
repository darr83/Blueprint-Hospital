namespace BlueprintHospital.Simulation.Medical.Results;

/// <summary>What sort of picture a result has.</summary>
public enum PictureKind
{
    /// <summary>An image drawn pixel by pixel (X-ray, CT, ultrasound, endoscopy, test cassette).</summary>
    Image,

    /// <summary>A 12-lead ECG trace, drawn as lines on ECG paper.</summary>
    Ecg,

    /// <summary>An EEG recording, drawn as lines.</summary>
    Eeg,
}

/// <summary>A circle the viewer can draw around the finding, with its label.</summary>
public sealed record PictureMarker(float X, float Y, float Radius, string Label);

/// <summary>Text burned into the picture's corner, such as the L/R side marker or the CT window.</summary>
/// <param name="Align">-1 left, 0 centre, 1 right.</param>
public sealed record PictureLabel(float X, float Y, string Text, float Size = 13, int Align = -1, bool Boxed = false);

/// <summary>A drawn picture with its title, corner labels and finding markers.</summary>
public sealed class RenderedPicture
{
    public required string Title { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>RGBA, 8 bits per channel, row by row.</summary>
    public required byte[] Rgba { get; init; }

    public List<PictureLabel> Labels { get; } = new();

    public List<PictureMarker> Markers { get; } = new();
}

/// <summary>
/// Every picture a result can show, by id. Investigations name these ids in their JSON
/// (<c>pictures</c> and <c>normal_picture</c>), and the Medical Editor offers them in a list.
/// </summary>
public static class ResultPictures
{
    private static readonly Dictionary<string, PictureSpec> All = new(StringComparer.Ordinal);

    static ResultPictures()
    {
        XRayPictures.Register(Add);
        CtPictures.Register(Add);
        UltrasoundPictures.Register(Add);
        ScopePictures.Register(Add);
        Waveforms.Register(Add);
    }

    /// <summary>All picture ids, in a stable order (for the editor and tests).</summary>
    public static IReadOnlyList<string> Ids => All.Keys.Order(StringComparer.Ordinal).ToList();

    public static bool Has(string id) => All.ContainsKey(id);

    public static PictureKind KindOf(string id) => All[id].Kind;

    /// <summary>A short plain description of the picture, for the editor's list.</summary>
    public static string Describe(string id) => All[id].Description;

    /// <summary>Draws image picture <paramref name="id"/>. <paramref name="seed"/> varies side and small details.</summary>
    public static RenderedPicture Render(string id, int seed)
    {
        var spec = All[id];
        if (spec.Draw is null)
        {
            throw new InvalidOperationException($"Picture '{id}' is a trace, not an image.");
        }

        return spec.Draw(new Random(seed));
    }

    private static void Add(string id, PictureKind kind, string description, Func<Random, RenderedPicture>? draw) =>
        All.Add(id, new PictureSpec(kind, description, draw));

    private sealed record PictureSpec(PictureKind Kind, string Description, Func<Random, RenderedPicture>? Draw);
}

/// <summary>Registers a picture: id, kind, description, and how to draw it (null for traces).</summary>
public delegate void PictureRegistrar(string id, PictureKind kind, string description, Func<Random, RenderedPicture>? draw);

/// <summary>A picture being drawn: the raster plus the labels and markers that go on top.</summary>
public sealed class PictureCanvas
{
    public PictureCanvas(int width, int height, Rgb background)
    {
        R = new Raster(width, height);
        R.Fill(background);
    }

    public Raster R { get; }

    public List<PictureLabel> Labels { get; } = new();

    public List<PictureMarker> Markers { get; } = new();

    public void Mark(float x, float y, float radius, string label) => Markers.Add(new PictureMarker(x, y, radius, label));

    public void Label(float x, float y, string text, float size = 13, int align = -1, bool boxed = false) =>
        Labels.Add(new PictureLabel(x, y, text, size, align, boxed));

    /// <summary>Mirrors the picture and its markers (labels are added after mirroring).</summary>
    public void Mirror()
    {
        R.FlipHorizontal();
        for (var i = 0; i < Markers.Count; i++)
        {
            var m = Markers[i];
            Markers[i] = m with { X = R.Width - m.X };
        }
    }

    public RenderedPicture Done(string title)
    {
        var p = new RenderedPicture { Title = title, Width = R.Width, Height = R.Height, Rgba = R.ToRgba() };
        p.Labels.AddRange(Labels);
        p.Markers.AddRange(Markers);
        return p;
    }
}
