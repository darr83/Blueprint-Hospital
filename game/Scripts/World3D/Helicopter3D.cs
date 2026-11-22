using System;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Transport;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// The air ambulance in the 3D world, built from rounded shapes in the same friendly style as the cars: a red
/// pod with a yellow band and dark glazing, a long tail boom with a fin and its shrouded tail rotor, skids, and
/// a four-blade main rotor that spins (a see-through blur when it is going fast). Its shadow stays on the
/// ground below it. Modelled about its centre with its nose along +Z, sizes in tiles (half metres).
/// </summary>
public partial class Helicopter3D : Node3D
{
    private const float TurnRate = 1.6f;

    /// <summary>The helicopter is drawn at this share of its real height above the ground, so it stays in view.</summary>
    private const double HeightShown = 0.45;

    private static readonly Color Red = new("#C8202B");
    private static readonly Color Yellow = new("#F2D21B");
    private static readonly Color Glass = new("#22303C");
    private static readonly Color Metal = new("#3A3F45");

    private static ArrayMesh? _body;
    private static ArrayMesh? _blades;

    private Node3D _craft = null!;
    private Node3D _rotor = null!;
    private Node3D _tailRotor = null!;
    private MeshInstance3D _blur = null!;
    private MeshInstance3D _shadow = null!;
    private Label3D _callSign = null!;
    private float _yaw = float.NaN;
    private double _lastTime = double.NaN;
    private double _spin;

    public static Helicopter3D Make(Node3D parent)
    {
        _body ??= BuildBody();
        _blades ??= BuildBlades();
        var h = new Helicopter3D { Name = "Helicopter" };
        h._craft = new Node3D { Name = "Craft" };
        h.AddChild(h._craft);
        h._craft.AddChild(new MeshInstance3D { Mesh = _body, MaterialOverride = Kit3D.VertexColours });
        h._rotor = new Node3D { Name = "Rotor", Position = new Vector3(0, Kit3D.M(2.75), 0.6f) };
        h._craft.AddChild(h._rotor);
        h._rotor.AddChild(new MeshInstance3D { Mesh = _blades, MaterialOverride = Kit3D.VertexColours });
        h._blur = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 10, BottomRadius = 10, Height = 0.02f, RadialSegments = 40 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.16f, 0.18f, 0.21f, 0.22f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        h._rotor.AddChild(h._blur);
        h._tailRotor = new Node3D { Name = "TailRotor", Position = new Vector3(0, Kit3D.M(1.55), -11.3f) };
        h._craft.AddChild(h._tailRotor);
        var tail = new MeshBatch();
        for (var i = 0; i < 4; i++)
        {
            tail.Box(Vector3.Zero, new Vector3(0.08f, 1.5f, 0.18f), Basis.FromEuler(new Vector3(i * Mathf.Pi / 4, 0, 0)), Metal);
        }

        tail.AddTo(h._tailRotor, shadow: false);
        h._shadow = Kit3D.Disc(h, Vector3.Zero, 5.5f, 9f, new Color(0, 0, 0, 0.22f));
        h._callSign = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 40,
            PixelSize = 0.008f,
            Modulate = new Color("#8E1A22"),
            OutlineModulate = new Color(1, 1, 1, 0.85f),
            OutlineSize = 10,
            Position = new Vector3(0, Kit3D.M(4.2), 0),
        };
        h._craft.AddChild(h._callSign);
        parent.AddChild(h);
        return h;
    }

    public void Sync(WorldLayer world, HelicopterVehicle heli)
    {
        var (x, y) = world.Interpolated(heli);
        Position = Kit3D.At(x, y);
        _craft.Position = new Vector3(0, Kit3D.M(heli.Altitude * HeightShown), 0);
        _callSign.Text = heli.CallSign;

        var step = double.IsNaN(_lastTime) ? 0 : Math.Clamp(world.Time - _lastTime, 0, 0.25);
        _lastTime = world.Time;
        var (dx, dy) = heli.Facing;
        var target = Math.Abs(dx) + Math.Abs(dy) < 1e-6 ? 0f : Kit3D.Yaw(dx, dy);
        if (heli.State is HelicopterState.Inbound or HelicopterState.Outbound || float.IsNaN(_yaw))
        {
            _yaw = float.IsNaN(_yaw) ? target : _yaw + Math.Clamp(Mathf.Wrap(target - _yaw, -Mathf.Pi, Mathf.Pi), -(float)(TurnRate * step), (float)(TurnRate * step));
        }

        _craft.Rotation = new Vector3(heli.State == HelicopterState.Inbound ? 0.08f : heli.State == HelicopterState.Outbound ? 0.1f : 0f, _yaw, 0);

        // Rotors: they spin with the simulation's rotor speed, the main one blurring into a disc when fast.
        _spin += step * heli.Rotor * 24;
        _rotor.Rotation = new Vector3(0, (float)(_spin % Math.Tau), 0);
        _tailRotor.Rotation = new Vector3((float)(_spin * 3 % Math.Tau), 0, 0);
        _blur.Visible = heli.Rotor > 0.6;

        // The shadow stays on the ground, shrinking and fading as it climbs.
        var lift = (float)Math.Clamp(heli.Altitude / HelicopterVehicle.CruiseMetres, 0, 1);
        _shadow.Rotation = new Vector3(0, _yaw, 0);
        _shadow.Scale = Vector3.One * (1 - (lift * 0.35f));
        _shadow.Transparency = lift * 0.55f;
    }

    /// <summary>Everything but the rotors.</summary>
    private static ArrayMesh BuildBody()
    {
        var m = new MeshBatch();
        float H(double metres) => Kit3D.M(metres);

        // Skids on their struts.
        foreach (var sx in new[] { -1.9f, 1.9f })
        {
            m.Pill(new Vector3(sx, H(0.08), 0.3f), new Vector3(0.3f, 0.3f, 7f), Metal);
            m.Box(new Vector3(sx * 0.8f, H(0.42), 2f), new Vector3(0.16f, H(0.7), 0.18f), Basis.FromEuler(new Vector3(0, 0, sx * 0.5f)), Metal);
            m.Box(new Vector3(sx * 0.8f, H(0.42), -1.4f), new Vector3(0.16f, H(0.7), 0.18f), Basis.FromEuler(new Vector3(0, 0, sx * 0.5f)), Metal);
        }

        // The pod: red, with a yellow band, dark glazing wrapped round the nose and an engine cowl on top.
        m.Ball(new Vector3(0, H(1.35), 0.6f), new Vector3(3.3f, H(1.9), 8.8f), Red);
        m.Box(new Vector3(0, H(1.05), 0.2f), new Vector3(3.28f, H(0.32), 4.6f), Yellow);
        m.Ball(new Vector3(0, H(1.5), 3.1f), new Vector3(2.9f, H(1.35), 3.6f), Glass);
        m.Box(new Vector3(1.62f, H(1.55), 0.2f), new Vector3(0.06f, H(0.6), 1.6f), Glass);
        m.Box(new Vector3(-1.62f, H(1.55), 0.2f), new Vector3(0.06f, H(0.6), 1.6f), Glass);
        m.Pill(new Vector3(0, H(2.3), -0.8f), new Vector3(1.7f, H(0.6), 4.2f), Red.Darkened(0.08f));
        m.Cylinder(new Vector3(0, H(2.6), 0.6f), 0.28f, H(0.4), 0.28f, Metal);

        // The white cross on the doors.
        foreach (var side in new[] { -1f, 1f })
        {
            m.Box(new Vector3(side * 1.66f, H(1.5), -1.2f), new Vector3(0.04f, H(0.5), 0.3f), Colors.White);
            m.Box(new Vector3(side * 1.66f, H(1.5), -1.2f), new Vector3(0.04f, H(0.17), 0.9f), Colors.White);
        }

        // Tail boom tapering back, the fin and stabiliser, and the shroud round the tail rotor.
        var along = Basis.FromEuler(new Vector3(-Mathf.Pi / 2, 0, 0));
        m.Cylinder(new Vector3(0, H(1.7), -6.6f), 0.62f, 9.2f, 0.62f, Red, along, cone: true);
        m.Box(new Vector3(0, H(1.6), -5.5f), new Vector3(0.2f, H(0.12), 4.5f), Yellow);
        m.Box(new Vector3(0, H(1.7), -8.4f), new Vector3(3.6f, H(0.1), 0.8f), Red);
        m.Box(new Vector3(0, H(2.3), -11.1f), new Vector3(0.22f, H(1.6), 1.6f), Basis.FromEuler(new Vector3(-0.3f, 0, 0)), Red);
        var sideways = Basis.FromEuler(new Vector3(0, 0, Mathf.Pi / 2));
        m.Cylinder(new Vector3(0, H(1.55), -11.3f), 0.9f, 0.5f, 0.9f, Red.Darkened(0.12f), sideways);
        return m.Build()!;
    }

    /// <summary>The four main rotor blades on their hub.</summary>
    private static ArrayMesh BuildBlades()
    {
        var m = new MeshBatch();
        m.Cylinder(Vector3.Zero, 0.6f, 0.25f, 0.6f, Metal);
        for (var i = 0; i < 4; i++)
        {
            var a = i * Mathf.Pi / 2;
            var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            m.Box(dir * 5.2f, new Vector3(0.55f, 0.06f, 9.6f), Basis.FromEuler(new Vector3(0, a, 0)), new Color("#2A2E33"));
        }

        return m.Build()!;
    }
}
