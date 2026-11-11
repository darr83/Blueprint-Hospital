using System.Collections.Generic;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// Collects many coloured shapes into one mesh, so a whole floor of walls (or a room of furniture)
/// is a single draw call. Each shape is a primitive mesh moved into place, with its colour per vertex.
/// </summary>
public sealed class MeshBatch
{
    private readonly List<Vector3> _vertices = new();
    private readonly List<Vector3> _normals = new();
    private readonly List<Color> _colours = new();
    private readonly List<int> _indices = new();

    private static readonly Dictionary<Mesh, (Vector3[] V, Vector3[] N, int[] I)> Cache = new();

    public bool IsEmpty => _vertices.Count == 0;

    /// <summary>Applied to everything added from now on: lets a part built upright be laid down or turned.</summary>
    public Transform3D Frame { get; set; } = Transform3D.Identity;

    /// <summary>Adds <paramref name="mesh"/> moved by <paramref name="xf"/>, painted <paramref name="colour"/>.</summary>
    public void Add(Mesh mesh, Transform3D xf, Color colour)
    {
        if (!Cache.TryGetValue(mesh, out var data))
        {
            var arrays = mesh.SurfaceGetArrays(0);
            var v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var n = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var i = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            data = (v, n, i);
            Cache[mesh] = data;
        }

        xf = Frame * xf;
        var normalBasis = xf.Basis.Inverse().Transposed();
        var start = _vertices.Count;
        for (var k = 0; k < data.V.Length; k++)
        {
            _vertices.Add(xf * data.V[k]);
            _normals.Add((normalBasis * data.N[k]).Normalized());
            _colours.Add(colour);
        }

        foreach (var index in data.I)
        {
            _indices.Add(start + index);
        }
    }

    /// <summary>An axis-aligned box from its centre and size.</summary>
    public void Box(Vector3 centre, Vector3 size, Color colour) =>
        Add(Kit3D.UnitBox, new Transform3D(Basis.FromScale(size), centre), colour);

    /// <summary>A box standing on the floor: footprint corner (x, y), size w × l tiles, from height <paramref name="from"/> up by <paramref name="height"/>.</summary>
    public void Block(double x, double y, double w, double l, float height, Color colour, float from = 0) =>
        Box(new Vector3((float)(x + (w / 2)), from + (height / 2), (float)(y + (l / 2))), new Vector3((float)w, height, (float)l), colour);

    /// <summary>A box turned by <paramref name="basis"/> (applied before its size).</summary>
    public void Box(Vector3 centre, Vector3 size, Basis basis, Color colour) =>
        Add(Kit3D.UnitBox, new Transform3D(basis * Basis.FromScale(size), centre), colour);

    /// <summary>A box turned by <paramref name="basis"/>.</summary>
    public void Box(Vector3 centre, Vector3 size, Color colour, Basis basis) => Box(centre, size, basis, colour);

    /// <summary>An ellipsoid: a sphere stretched to <paramref name="size"/> (full widths).</summary>
    public void Ball(Vector3 centre, Vector3 size, Color colour, bool small = false) =>
        Add(small ? Kit3D.SmallSphere : Kit3D.UnitSphere, new Transform3D(Basis.FromScale(size), centre), colour);

    /// <summary>An ellipsoid turned by <paramref name="basis"/>.</summary>
    public void Ball(Vector3 centre, Vector3 size, Basis basis, Color colour, bool small = false) =>
        Add(small ? Kit3D.SmallSphere : Kit3D.UnitSphere, new Transform3D(basis * Basis.FromScale(size), centre), colour);

    /// <summary>A rounded rod from <paramref name="from"/> to <paramref name="to"/>, for limbs.</summary>
    public void Capsule(Vector3 from, Vector3 to, float radius, Color colour, float depthScale = 1f)
    {
        var along = to - from;
        var basis = Kit3D.AlongBasis(along.Normalized());
        Add(Kit3D.UnitCapsule, new Transform3D(basis * Basis.FromScale(new Vector3(radius * 2, along.Length() + (radius * 2), radius * 2 * depthScale)), (from + to) / 2), colour);
    }

    /// <summary>A rounded body part: a capsule standing upright, <paramref name="size"/> across, tall and deep.</summary>
    public void Pill(Vector3 centre, Vector3 size, Color colour, Basis? basis = null) =>
        Add(Kit3D.UnitCapsule, new Transform3D((basis ?? Basis.Identity) * Basis.FromScale(size), centre), colour);

    /// <summary>A cylinder from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public void Rod(Vector3 from, Vector3 to, float radius, Color colour, bool small = false)
    {
        var along = to - from;
        var basis = Kit3D.AlongBasis(along.Normalized());
        Add(small ? Kit3D.SmallCylinder : Kit3D.UnitCylinder, new Transform3D(basis * Basis.FromScale(new Vector3(radius * 2, along.Length(), radius * 2)), (from + to) / 2), colour);
    }

    /// <summary>An upright cylinder (or a cone with <paramref name="cone"/>) centred at <paramref name="centre"/>.</summary>
    public void Cylinder(Vector3 centre, float radiusX, float height, float radiusZ, Color colour, Basis? basis = null, bool cone = false) =>
        Add(cone ? Kit3D.UnitCone : Kit3D.UnitCylinder, new Transform3D((basis ?? Basis.Identity) * Basis.FromScale(new Vector3(radiusX * 2, height, radiusZ * 2)), centre), colour);

    /// <summary>A ring (torus) lying flat, <paramref name="radius"/> to the middle of its band (the band is about a fifth as thick).</summary>
    public void Ring(Vector3 centre, float radius, Color colour, Basis? basis = null, float flatten = 1f)
    {
        var scale = radius / 0.45f;
        Add(Kit3D.UnitRing, new Transform3D((basis ?? Basis.Identity) * Basis.FromScale(new Vector3(scale, scale * flatten, scale)), centre), colour);
    }

    /// <summary>A flat quad lying on the floor at height <paramref name="lift"/>.</summary>
    public void Floor(double x, double y, double w, double l, float lift, Color colour)
    {
        var start = _vertices.Count;
        var f = Frame;
        var (x0, y0, x1, y1) = ((float)x, (float)y, (float)(x + w), (float)(y + l));
        _vertices.AddRange(new[] { f * new Vector3(x0, lift, y0), f * new Vector3(x1, lift, y0), f * new Vector3(x1, lift, y1), f * new Vector3(x0, lift, y1) });
        for (var k = 0; k < 4; k++)
        {
            _normals.Add((f.Basis * Vector3.Up).Normalized());
            _colours.Add(colour);
        }

        _indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
    }

    /// <summary>A flat triangle, seen from both sides.</summary>
    public void Tri(Vector3 a, Vector3 b, Vector3 c, Color colour)
    {
        var f = Frame;
        var n = (f.Basis * (b - a).Cross(c - a)).Normalized();
        var start = _vertices.Count;
        _vertices.AddRange(new[] { f * a, f * b, f * c, f * a, f * c, f * b });
        for (var k = 0; k < 6; k++)
        {
            _normals.Add(k < 3 ? n : -n);
            _colours.Add(colour);
        }

        _indices.AddRange(new[] { start, start + 2, start + 1, start + 3, start + 5, start + 4 });
    }

    /// <summary>The finished mesh (null when nothing was added).</summary>
    public ArrayMesh? Build()
    {
        if (IsEmpty)
        {
            return null;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _colours.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = _indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>Adds the finished mesh to <paramref name="parent"/> as one instance (nothing if empty).</summary>
    public MeshInstance3D? AddTo(Node3D parent, bool alpha = false, bool shadow = true)
    {
        if (Build() is not { } mesh)
        {
            return null;
        }

        var instance = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = alpha ? Kit3D.VertexColoursAlpha : Kit3D.VertexColours,
            CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(instance);
        return instance;
    }
}
