using System;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>Which alert hangs over a patient.</summary>
public enum AlertKind
{
    /// <summary>A cardiac arrest: a pulsing red ring and a red heart with a flat line.</summary>
    Crash,

    /// <summary>A ward emergency: a pulsing amber ring and an amber warning sign.</summary>
    Emergency,

    /// <summary>An emergency whose treatment is in: a faint amber ring only.</summary>
    Treated,
}

/// <summary>
/// The crash and emergency markers: a ring on the floor round the patient and a sign floating
/// above them that always turns to face the camera, both pulsing.
/// </summary>
public partial class Alert3D : Node3D
{
    private static readonly Color Red = new("#D32F2F");
    private static readonly Color Amber = new("#F59E0B");

    private AlertKind _kind;
    private Node3D _ring = null!;
    private Node3D? _sign;
    private float _height;

    public AlertKind Kind => _kind;

    /// <summary>Makes a marker; <paramref name="radius"/> is the ring's size in tiles, <paramref name="height"/> the sign's height in 2D pixels.</summary>
    public static Alert3D Make(AlertKind kind, float radius, float height)
    {
        var alert = new Alert3D { Name = "Alert", _kind = kind, _height = Kit3D.H(height) };
        var colour = kind == AlertKind.Crash ? Red : Amber;
        var ring = new MeshBatch();
        ring.Ring(new Vector3(0, 0.03f, 0), radius, new Color(colour, kind == AlertKind.Treated ? 0.4f : 0.85f), null, 0.15f);
        alert._ring = new Node3D();
        alert.AddChild(alert._ring);
        if (ring.AddTo(alert._ring, alpha: true, shadow: false) is { } mesh)
        {
            mesh.MaterialOverride = Kit3D.VertexColoursUnshadedAlpha;
        }

        if (kind == AlertKind.Treated)
        {
            return alert;
        }

        var sign = new MeshBatch();
        if (kind == AlertKind.Crash)
        {
            // A heart: two lobes and a point, with a white flat line across it.
            var s = 0.26f;
            sign.Ball(new Vector3(-s * 0.55f, s * 0.35f, 0), new Vector3(s * 1.25f, s * 1.25f, s * 0.5f), Red, small: true);
            sign.Ball(new Vector3(s * 0.55f, s * 0.35f, 0), new Vector3(s * 1.25f, s * 1.25f, s * 0.5f), Red, small: true);
            sign.Tri(new Vector3(-s * 1.15f, s * 0.2f, 0), new Vector3(s * 1.15f, s * 0.2f, 0), new Vector3(0, -s * 1.15f, 0), Red);
            sign.Box(new Vector3(0, -s * 0.2f, 0), new Vector3(s * 1.2f, s * 0.95f, s * 0.35f), Red, new Basis(Vector3.Back, Mathf.Pi / 4));
            sign.Box(new Vector3(-s * 0.55f, s * 0.2f, s * 0.32f), new Vector3(s * 0.6f, s * 0.12f, 0.01f), Colors.White);
            sign.Box(new Vector3(s * 0.55f, s * 0.2f, s * 0.32f), new Vector3(s * 0.6f, s * 0.12f, 0.01f), Colors.White);
            sign.Box(new Vector3(-s * 0.08f, s * 0.35f, s * 0.32f), new Vector3(s * 0.12f, s * 0.5f, 0.01f), Colors.White, new Basis(Vector3.Back, -0.4f));
            sign.Box(new Vector3(s * 0.12f, s * 0.1f, s * 0.32f), new Vector3(s * 0.12f, s * 0.55f, 0.01f), Colors.White, new Basis(Vector3.Back, 0.4f));
        }
        else
        {
            // A warning triangle with an exclamation mark.
            var s = 0.28f;
            var ink = new Color("#3B2A05");
            sign.Tri(new Vector3(0, s, 0), new Vector3(-s * 1.1f, -s * 0.85f, 0), new Vector3(s * 1.1f, -s * 0.85f, 0), Amber);
            sign.Box(new Vector3(0, -s * 0.05f, 0.01f), new Vector3(s * 0.16f, s * 0.6f, 0.01f), ink);
            sign.Box(new Vector3(0, -s * 0.6f, 0.01f), new Vector3(s * 0.16f, s * 0.16f, 0.01f), ink);
        }

        alert._sign = new Node3D { Position = new Vector3(0, alert._height, 0) };
        alert.AddChild(alert._sign);
        if (sign.AddTo(alert._sign, shadow: false) is { } signMesh)
        {
            signMesh.MaterialOverride = Kit3D.VertexColoursUnshaded;
        }

        return alert;
    }

    public override void _Process(double delta)
    {
        var t = Time.GetTicksMsec() / 1000.0;
        var pulse = _kind == AlertKind.Treated ? 0.3f : (float)(0.5 + (0.5 * Math.Sin(t * (_kind == AlertKind.Crash ? 6 : 4))));
        _ring.Scale = Vector3.One * (1f + (pulse * 0.07f));
        if (_sign is null)
        {
            return;
        }

        _sign.Position = new Vector3(0, _height + (pulse * Kit3D.H(3)), 0);
        if (GetViewport()?.GetCamera3D() is { } camera)
        {
            _sign.GlobalBasis = camera.GlobalBasis.Orthonormalized() * Basis.FromScale(Vector3.One * (1f + (pulse * 0.1f)));
        }
    }
}
