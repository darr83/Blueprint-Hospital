using System;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Transport;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// An ambulance in the 3D world: the Kenney model in UK yellow and green (made by
/// tools/ArtBaker/export_ambulance_mesh.py), its blue lights flashing left and right on an
/// emergency run, and its call sign floating over the roof.
/// </summary>
public partial class Ambulance3D : Node3D
{
    private const string MeshPath = "res://Art/Vehicles/ambulance.obj";

    // How fast the body swings round to a new heading (radians per second of real time).
    private const float TurnRate = 5f;

    private static Mesh? _mesh;
    private static readonly StandardMaterial3D LightOn = new()
    {
        AlbedoColor = new Color("#8CD1FF"),
        EmissionEnabled = true,
        Emission = new Color("#59B8FF"),
        EmissionEnergyMultiplier = 3f,
    };

    private MeshInstance3D _body = null!;
    private Label3D _callSign = null!;
    private int _left = -1;
    private int _right = -1;
    private int _lit = -1;
    private float _yaw = float.NaN;
    private double _lastTime = double.NaN;

    public static Ambulance3D Make(Node3D parent)
    {
        _mesh ??= GD.Load<Mesh>(MeshPath);
        var a = new Ambulance3D { Name = "Ambulance" };
        a._body = new MeshInstance3D { Mesh = _mesh };
        a.AddChild(a._body);
        for (var i = 0; i < _mesh.GetSurfaceCount(); i++)
        {
            var name = _mesh.SurfaceGetMaterial(i)?.ResourceName ?? "";
            if (name == "" && _mesh is ArrayMesh am)
            {
                name = am.SurfaceGetName(i);
            }

            if (name.Contains("lightLeft", StringComparison.Ordinal))
            {
                a._left = i;
            }
            else if (name.Contains("lightRight", StringComparison.Ordinal))
            {
                a._right = i;
            }
        }

        a._callSign = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 40,
            PixelSize = 0.008f,
            Modulate = new Color("#1E5631"),
            OutlineModulate = new Color(1, 1, 1, 0.85f),
            OutlineSize = 10,
            Position = new Vector3(0, Kit3D.H(96), 0),
        };
        a.AddChild(a._callSign);
        parent.AddChild(a);
        return a;
    }

    public void Sync(WorldLayer world, HospitalSimulation sim, AmbulanceVehicle ambulance)
    {
        // The simulation places an ambulance by its centre, which is where the model's origin is.
        // Its nose points along its heading: backwards while it reverses into a bay and once parked.
        var (x, y) = world.Interpolated(ambulance);
        var (dx, dy) = ambulance.Heading;
        var len = Math.Sqrt((dx * dx) + (dy * dy));
        var (fx, fy) = len < 1e-6 ? (1.0, 0.0) : (dx / len, dy / len);
        Position = Kit3D.At(x, y);

        // Swing round to a new heading over a moment rather than snapping, so it turns into the bay.
        var target = Kit3D.Yaw(fx, fy);
        var step = double.IsNaN(_lastTime) ? 0 : Math.Clamp(world.Time - _lastTime, 0, 0.25);
        _lastTime = world.Time;
        _yaw = float.IsNaN(_yaw) ? target : _yaw + Math.Clamp(Mathf.Wrap(target - _yaw, -Mathf.Pi, Mathf.Pi), -(float)(TurnRate * step), (float)(TurnRate * step));
        Rotation = new Vector3(0, _yaw, 0);
        _callSign.Text = ambulance.CallSign;

        // Blue lights flash on an emergency run, alternating left and right.
        var flashing = ambulance.State is AmbulanceState.Driving or AmbulanceState.WaitingForBay
                       && sim.FindPatient(ambulance.PatientId) is { Triage: TriageCategory.Red or null };
        var lit = flashing ? (int)(world.Time * 6) % 2 : -1;
        if (lit == _lit)
        {
            return;
        }

        _lit = lit;
        if (_left >= 0)
        {
            _body.SetSurfaceOverrideMaterial(_left, lit == 0 ? LightOn : null);
        }

        if (_right >= 0)
        {
            _body.SetSurfaceOverrideMaterial(_right, lit == 1 ? LightOn : null);
        }
    }
}
