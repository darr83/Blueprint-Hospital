using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// The small moving parts that make the building feel lived in, none of which changes the simulation:
/// the check-in screens and printers on the reception desks, the traces on the anaesthetic machine's
/// monitor during an operation, the mobiles turning over the cots, and the night: corridors and wards
/// dimmed, with night lights over the ward beds and a lamp at the nurses' station.
/// </summary>
public partial class Life3D : Node3D
{
    private static readonly Color Dusk = new("#0B1630");
    private static readonly Color Lamp = new("#FFD27A");

    private View3D _view = null!;
    private readonly List<Desk> _desks = new();
    private readonly List<Monitor> _monitors = new();
    private readonly List<Node3D> _mobiles = new();
    private readonly List<GeometryInstance3D> _dim = new();
    private readonly List<Node3D> _nightLights = new();

    public void Initialise(View3D view) => _view = view;

    /// <summary>How many reception desks, theatre monitors and cot mobiles are animated (for tests).</summary>
    /// <summary>The spot in front of the nearest check-in screen within <paramref name="within"/> tiles, if any.</summary>
    public (double X, double Y, double FaceX, double FaceY)? KioskNear(double x, double y, double within)
    {
        var best = _desks.OrderBy(k => Math.Abs(k.Kiosk.X - x) + Math.Abs(k.Kiosk.Y - y)).FirstOrDefault();
        return best is null || Math.Abs(best.Kiosk.X - x) + Math.Abs(best.Kiosk.Y - y) > within
            ? null
            : (best.Kiosk.X, best.Kiosk.Y, best.Faces.X, best.Faces.Y);
    }

    public int Count => _desks.Count + _monitors.Count + _mobiles.Count;

    public void Rebuild()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }

        _desks.Clear();
        _monitors.Clear();
        _mobiles.Clear();
        _dim.Clear();
        _nightLights.Clear();
        var world = _view.World;
        var sim = _view.Simulation;
        var corridors = new MeshBatch();
        var wards = new MeshBatch();
        var lamps = new MeshBatch();
        foreach (var space in world.ViewSpaces)
        {
            if (space.Kind == CareSpaceKind.Corridor)
            {
                corridors.Floor(space.Rect.X, space.Rect.Y, space.Rect.Width, space.Rect.Length, 0.045f, Dusk);
            }
            else if (space.IsWard)
            {
                wards.Floor(space.Rect.X, space.Rect.Y, space.Rect.Width, space.Rect.Length, 0.045f, Dusk);
                foreach (var bed in space.Beds)
                {
                    // A little reading light on the wall over the head of each bed.
                    var head = Kit3D.At(bed.PatientSpot.X + 0.5, bed.PatientSpot.Y + 0.5 + (bed.HeadSouth ? 2.15 : -2.15), Kit3D.H(46));
                    lamps.Box(head, new Vector3(0.36f, 0.08f, 0.08f), Lamp);
                    lamps.Ball(head + new Vector3(0, -0.15f, 0), new Vector3(1.2f, 0.9f, 1.2f), new Color(Lamp, 0.22f));
                }
            }

            foreach (var item in space.Furniture)
            {
                if (!sim.Database.Furniture.TryGetValue(item.ItemId, out var def))
                {
                    continue;
                }

                var frame = Furniture3D.Frame(space, item, def.Id, sim, out var w, out var d);
                var h = Kit3D.M(def.HeightM);
                switch (def.Id)
                {
                    case "reception_desk":
                        _desks.Add(Desk.Make(this, space, item, frame, w, d, h));
                        break;
                    case "anaesthetic_machine":
                        _monitors.Add(Monitor.Make(this, space, frame, w, h));
                        break;
                    case "cot":
                        _mobiles.Add(Mobile(frame, item, h));
                        break;
                    case "station_counter" when space.IsWard:
                        // The nurses' station keeps a lamp on all night.
                        var top = frame * new Vector3(0, h + 0.25f, 0);
                        lamps.Ball(top, new Vector3(0.22f, 0.16f, 0.22f), Lamp);
                        lamps.Ball(top, new Vector3(2.4f, 1.4f, 2.4f), new Color(Lamp, 0.18f));
                        break;
                }
            }
        }

        Dim(corridors, 0.32f);
        Dim(wards, 0.26f);
        if (lamps.AddTo(this, alpha: true, shadow: false) is { } glow)
        {
            glow.Name = "NightLights";
            glow.MaterialOverride = Kit3D.VertexColoursUnshadedAlpha;
            glow.Visible = false;
            _nightLights.Add(glow);
        }
    }

    private readonly Dictionary<GeometryInstance3D, float> _depth = new();

    private void Dim(MeshBatch b, float depth)
    {
        if (b.AddTo(this, alpha: true, shadow: false) is not { } mesh)
        {
            return;
        }

        mesh.Name = "NightDim";
        mesh.MaterialOverride = Kit3D.VertexColoursUnshadedAlpha;
        mesh.Visible = false;
        _dim.Add(mesh);
        _depth[mesh] = depth;
    }

    public void Sync()
    {
        var sim = _view.Simulation;
        var time = _view.People.AnimTime;
        var night = _view.Night;
        foreach (var mesh in _dim)
        {
            mesh.Visible = night > 0.02f;
            mesh.Transparency = 1f - (night * _depth[mesh]);
        }

        foreach (var lights in _nightLights)
        {
            lights.Visible = night > 0.4f;
        }

        foreach (var desk in _desks)
        {
            desk.Sync(_view.World, sim, time);
        }

        foreach (var monitor in _monitors)
        {
            monitor.Sync(sim, time);
        }

        foreach (var mobile in _mobiles)
        {
            mobile.Rotation = new Vector3(0, (float)(time * 0.6), 0);
        }
    }

    /// <summary>A mobile on an arm over the head of a cot: four little shapes on threads, turning slowly.</summary>
    private Node3D Mobile(Transform3D frame, PlacedFurniture item, float h)
    {
        var (w, d) = ((float)Math.Min(item.Width, item.Length), (float)Math.Max(item.Width, item.Length));
        var alongY = item.Length >= item.Width;
        var cot = new Transform3D(new Basis(Vector3.Up, alongY ? 0 : Mathf.Pi / 2), frame.Origin);
        var arm = new MeshBatch { Frame = cot };
        var top = h + 0.55f;
        var post = new Vector3((w / 2) - 0.06f, 0, -(d / 2) + 0.1f);
        arm.Rod(post + new Vector3(0, h * 0.62f, 0), post + new Vector3(0, top, 0), 0.015f, new Color("#E0E6EA"), small: true);
        var hubAt = new Vector3(0, top, -(d / 2) + 0.3f);
        arm.Rod(post + new Vector3(0, top, 0), hubAt, 0.012f, new Color("#E0E6EA"), small: true);
        arm.AddTo(this);

        var hub = new Node3D { Name = "Mobile", Position = cot * hubAt };
        AddChild(hub);
        var b = new MeshBatch();
        var colours = new[] { new Color("#FFD54F"), new Color("#81D4FA"), new Color("#F48FB1"), new Color("#A5D6A7") };
        b.Rod(new Vector3(-0.13f, -0.02f, 0), new Vector3(0.13f, -0.02f, 0), 0.006f, new Color("#B0BEC5"), small: true);
        b.Rod(new Vector3(0, -0.02f, -0.13f), new Vector3(0, -0.02f, 0.13f), 0.006f, new Color("#B0BEC5"), small: true);
        for (var i = 0; i < 4; i++)
        {
            var a = i * Mathf.Pi / 2;
            var end = new Vector3(Mathf.Cos(a) * 0.13f, -0.02f, Mathf.Sin(a) * 0.13f);
            b.Rod(end, end + new Vector3(0, -0.12f, 0), 0.003f, new Color("#ECEFF1"), small: true);
            if (i % 2 == 0)
            {
                b.Ball(end + new Vector3(0, -0.15f, 0), new Vector3(0.07f, 0.07f, 0.07f), colours[i], small: true);
            }
            else
            {
                b.Box(end + new Vector3(0, -0.15f, 0), new Vector3(0.06f, 0.06f, 0.02f), new Basis(Vector3.Forward, Mathf.Pi / 4), colours[i]);
            }
        }

        b.AddTo(hub);
        return hub;
    }

    /// <summary>A reception desk's check-in screen (visitors' side) and printer (staff side).</summary>
    private sealed class Desk
    {
        private PlacedFurniture _item = null!;
        private MeshInstance3D _screen = null!;
        private Node3D _paper = null!;
        private Vector3 _paperFrom;
        private Vector3 _paperTo;

        /// <summary>Where a visitor stands to use the check-in screen, in grid tiles.</summary>
        public (double X, double Y) Kiosk { get; private set; }

        /// <summary>Which way they face to use it.</summary>
        public (double X, double Y) Faces { get; private set; }

        public static Desk Make(Node3D parent, CareSpace space, PlacedFurniture item, Transform3D frame, float w, float d, float h)
        {
            var desk = new Desk { _item = item };
            var worktop = h - Kit3D.H(9);
            var b = new MeshBatch { Frame = frame };

            // The check-in screen on a little stand on the visitors' ledge, at the far end.
            var sx = (w / 2) - 0.32f;
            var at = new Vector3(sx, h + 0.16f, (d / 2) - 0.1f);
            var front = frame * new Vector3(sx, 0, (d / 2) + 0.42f);
            var back = frame * new Vector3(sx, 0, 0);
            desk.Kiosk = (front.X, front.Z);
            desk.Faces = (back.X - front.X, back.Z - front.Z);
            var tilt = new Basis(Vector3.Right, -0.55f);
            b.Rod(new Vector3(sx, h, (d / 2) - 0.12f), at, 0.02f, new Color("#3A4148"), small: true);
            b.Box(at, new Vector3(0.36f, 0.26f, 0.04f), tilt, new Color("#2B2F33"));

            // The printer on the staff worktop, at the other end.
            var px = -(w / 2) + 0.32f;
            b.Box(new Vector3(px, worktop + 0.07f, -0.2f), new Vector3(0.34f, 0.14f, 0.28f), new Color("#E3E8EC"));
            b.Box(new Vector3(px, worktop + 0.145f, -0.2f), new Vector3(0.3f, 0.01f, 0.22f), new Color("#9AA6B0"));
            b.Box(new Vector3(px, worktop + 0.06f, -0.345f), new Vector3(0.22f, 0.012f, 0.02f), new Color("#3A4148"));
            b.AddTo(parent);

            var glow = new MeshBatch { Frame = frame };
            glow.Box(at + (tilt * new Vector3(0, 0, 0.022f)), new Vector3(0.3f, 0.2f, 0.005f), tilt, new Color("#7FD8F0"));
            glow.Box(at + (tilt * new Vector3(0, 0.045f, 0.026f)), new Vector3(0.22f, 0.03f, 0.004f), tilt, Colors.White);
            glow.Box(at + (tilt * new Vector3(0, -0.04f, 0.026f)), new Vector3(0.16f, 0.05f, 0.004f), tilt, new Color("#3EE07A"));
            desk._screen = glow.AddTo(parent, shadow: false)!;
            desk._screen.MaterialOverride = Kit3D.VertexColoursUnshaded;
            desk._screen.Name = "CheckInScreen";

            desk._paperFrom = frame * new Vector3(px, worktop + 0.06f, -0.22f);
            desk._paperTo = frame * new Vector3(px, worktop + 0.06f, -0.48f);
            desk._paper = new Node3D { Name = "PrinterPaper", Position = desk._paperFrom, Basis = frame.Basis };
            var paper = new MeshBatch();
            paper.Box(Vector3.Zero, new Vector3(0.2f, 0.006f, 0.26f), Colors.White);
            paper.Box(new Vector3(0, 0.004f, 0.05f), new Vector3(0.14f, 0.002f, 0.015f), new Color("#90A4AE"));
            paper.Box(new Vector3(0, 0.004f, 0f), new Vector3(0.14f, 0.002f, 0.015f), new Color("#90A4AE"));
            paper.AddTo(desk._paper, shadow: false);
            desk._paper.Visible = false;
            parent.AddChild(desk._paper);
            return desk;
        }

        public void Sync(WorldLayer world, HospitalSimulation sim, double time)
        {
            // Someone is checking in at this desk: the screen flashes as they tap, and their form prints out.
            var busy = sim.Outpatients.Any(o => o.Stage == Simulation.Patients.OutpatientStage.CheckingIn && Near(world.Interpolated(o)))
                       || sim.Tasks.Any(t => t.Started && t.Kind == TaskKind.Register && sim.FindPatient(t.PatientId) is { } p && Near(world.Interpolated(p)));
            _screen.Transparency = busy && Mathf.Sin((float)time * 7) > 0.1f ? 0f : 0.35f;
            var cycle = (float)Mathf.PosMod(time, 6.0);
            _paper.Visible = busy && cycle > 3;
            if (_paper.Visible)
            {
                _paper.Position = _paperFrom.Lerp(_paperTo, Mathf.Clamp((cycle - 3) / 1.6f, 0, 1));
            }
        }

        private bool Near((double X, double Y) at) =>
            at.X > _item.X - 2 && at.X < _item.X + _item.Width + 2 && at.Y > _item.Y - 2 && at.Y < _item.Y + _item.Length + 2;
    }

    /// <summary>The anaesthetic machine's monitor during an operation: an ECG and a pleth trace, swept across the screen.</summary>
    private sealed class Monitor
    {
        private CareSpace _room = null!;
        private Node3D _traces = null!;
        private Node3D _sweep = null!;
        private float _width;

        public static Monitor Make(Node3D parent, CareSpace room, Transform3D frame, float w, float h)
        {
            var m = new Monitor { _room = room, _width = w * 0.8f * 0.82f };
            var tilt = new Basis(Vector3.Right, -0.12f);
            var centre = new Vector3(0, h * 0.93f, -0.05f) + (tilt * new Vector3(0, 0, 0.036f));
            m._traces = new Node3D { Name = "TheatreMonitor", Transform = frame * new Transform3D(tilt, centre) };
            parent.AddChild(m._traces);
            var screenH = h * 0.18f * 0.8f;
            var b = new MeshBatch();
            Wave(b, m._width, screenH * 0.18f, screenH * 0.22f, new Color("#3EE07A"), ecg: true);
            Wave(b, m._width, -screenH * 0.18f, screenH * 0.16f, new Color("#4FC3F7"), ecg: false);
            var mesh = b.AddTo(m._traces, shadow: false)!;
            mesh.MaterialOverride = Kit3D.VertexColoursUnshaded;

            // The sweep: a gap of blank screen moving across, wiping the old trace.
            m._sweep = new Node3D { Name = "Sweep" };
            var s = new MeshBatch();
            s.Box(new Vector3(0, 0, 0.004f), new Vector3(m._width * 0.12f, screenH * 0.95f, 0.004f), new Color("#14202B"));
            s.AddTo(m._sweep, shadow: false);
            m._traces.AddChild(m._sweep);
            m._traces.Visible = false;
            return m;
        }

        /// <summary>A trace across the screen: a heartbeat spike pattern, or the smooth pleth wave.</summary>
        private static void Wave(MeshBatch b, float width, float y, float amp, Color colour, bool ecg)
        {
            const int steps = 36;
            Vector3 At(int i)
            {
                var u = (float)i / steps;
                var beat = Mathf.PosMod(u * 3f, 1f);
                var v = ecg
                    ? beat switch { < 0.40f => 0f, < 0.45f => 0.15f, < 0.5f => -0.25f, < 0.55f => 1f, < 0.6f => -0.4f, < 0.75f => 0.12f, _ => 0f }
                    : 0.5f * (1 + Mathf.Sin(beat * Mathf.Tau)) - 0.3f;
                return new Vector3((u - 0.5f) * width, y + (v * amp), 0);
            }

            for (var i = 1; i <= steps; i++)
            {
                b.Rod(At(i - 1), At(i), 0.006f, colour, small: true);
            }
        }

        public void Sync(HospitalSimulation sim, double time)
        {
            var on = sim.Tasks.Any(t => t.Started && t.SpaceId == _room.Id && t.Kind is TaskKind.Surgery or TaskKind.Procedure);
            _traces.Visible = on;
            if (on)
            {
                var u = (float)Mathf.PosMod(time * 0.4, 1.0);
                _sweep.Position = new Vector3((u - 0.5f) * _width, 0, 0);
            }
        }
    }
}
