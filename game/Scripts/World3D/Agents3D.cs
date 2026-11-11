using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.Transport;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// Everyone in the 3D world: patients, staff and clinic visitors, each a <see cref="Person3D"/>
/// kept at their interpolated position, turning smoothly to face where they are going.
/// </summary>
public partial class Agents3D : Node3D
{
    private static readonly Color SelectedRing = new("#FFEB3B");

    private View3D _view = null!;
    private readonly Dictionary<Agent, AgentView> _views = new();

    public void Initialise(View3D view) => _view = view;

    /// <summary>The 3D figure standing in for <paramref name="agent"/>, if any (for tests and close-ups).</summary>
    public Node3D? ViewOf(Agent agent) => _views.TryGetValue(agent, out var v) ? v.Node : null;

    public int Count => _views.Count;

    public void Sync(double delta)
    {
        var world = _view.World;
        var sim = _view.Simulation;
        var present = new HashSet<Agent>();
        foreach (var p in sim.Patients)
        {
            present.Add(p);
        }

        foreach (var s in sim.Staff.Where(s => s.IsInBuilding))
        {
            present.Add(s);
        }

        foreach (var o in sim.Outpatients)
        {
            present.Add(o);
        }

        foreach (var a in sim.Ambulances)
        {
            present.Add(a);
        }

        foreach (var gone in _views.Keys.Where(a => !present.Contains(a)).ToList())
        {
            _views[gone].Node.QueueFree();
            _views.Remove(gone);
        }

        foreach (var agent in present)
        {
            if (!_views.TryGetValue(agent, out var view))
            {
                view = new AgentView(this, agent);
                _views[agent] = view;
            }

            view.Sync(world, sim, delta);
        }
    }

    /// <summary>One agent's figure, with the state it was last drawn in.</summary>
    private sealed class AgentView
    {
        private readonly Agents3D _owner;
        private readonly Agent _agent;
        private Person3D? _person;
        private MeshInstance3D? _ring;
        private (Color Colour, float Radius)? _ringKey;
        private float _yaw = float.NaN;
        private Node3D? _lying;
        private object? _lyingKey;
        private Node3D? _chair;
        private Alert3D? _alert;
        private Ambulance3D? _ambulance;

        public AgentView(Agents3D owner, Agent agent)
        {
            _owner = owner;
            _agent = agent;
            Node = new Node3D { Name = $"{agent.GetType().Name}{agent.Id}" };
            owner.AddChild(Node);
        }

        public Node3D Node { get; }

        public void Sync(WorldLayer world, HospitalSimulation sim, double delta)
        {
            if (_agent is AmbulanceVehicle vehicle)
            {
                Node.Visible = world.OnViewFloor(vehicle);
                _ambulance ??= Ambulance3D.Make(Node);
                _ambulance.Sync(world, sim, vehicle);
                return;
            }

            var visible = world.OnViewFloor(_agent) && _agent switch
            {
                Patient p => world.IsOnMap(p) && !world.InBed(p),
                _ => true,
            } && sim.ToiletTripOf(_agent)?.Stage != Simulation.Simulation.ToiletStage.Using;
            Node.Visible = visible;
            if (!visible)
            {
                return;
            }

            var look = _agent switch
            {
                Patient p => Looks.Patient(p),
                StaffMember m => Looks.Staff(m),
                Outpatient o => Looks.Outpatient(o, sim.FindAppointment(o.AppointmentId)),
                _ => null,
            };
            if (look is null)
            {
                Node.Visible = false;
                return;
            }

            if (_agent is Patient lyingPatient && LyingDown(world, sim, lyingPatient, look))
            {
                if (_person is not null)
                {
                    _person.Visible = false;
                }

                return;
            }

            ClearLying();
            if (_person is null || _person.Look != look)
            {
                _person?.QueueFree();
                _person = Person3D.Build(look);
                Node.AddChild(_person);
            }

            _person.Visible = true;

            var (x, y) = world.FootGrid(_agent);
            var pose = _agent.IsMoving ? Pose.Walking : Pose.Standing;
            var facing = _agent.Facing;
            var seated = world.IsSeated(_agent);
            BabyLook? baby = null;
            var bag = false;

            switch (_agent)
            {
                case StaffMember member when world.CprSpot(member) is { } cpr:
                    (x, y) = (cpr.FootX, cpr.FootY);
                    facing = (cpr.ChestX - cpr.FootX, cpr.ChestY - cpr.FootY);
                    pose = Pose.Cpr;
                    break;
                case StaffMember member:
                {
                    var pushing = member.PushingPatientId is { } pid && sim.FindPatient(pid) is { IsMoving: true };
                    pushing |= member.CrashPatientId is not null && member.IsMoving && sim.ResusTrolleys.Any(t => t.PushedByStaffId == member.Id);
                    if (pushing)
                    {
                        pose = Pose.Walking;
                    }

                    baby = world.CarriedBy(member);
                    bag = sim.IsCarryingMedicines(member);
                    break;
                }

                case Patient patient:
                    baby = world.InArms(patient);
                    if (world.InWheelchair(patient))
                    {
                        seated = true;
                    }

                    break;
            }

            Wheelchair(_agent is Patient wp && world.InWheelchair(wp), look.Scale);
            if (seated)
            {
                pose = Pose.Seated;
                facing = world.IsSeated(_agent) ? Facing(world.SeatFacing(_agent)) : facing;
            }

            Node.Position = Kit3D.At(x, y);

            // Turn smoothly towards where they're heading, rather than snapping round.
            var want = Kit3D.Yaw(facing.X, facing.Y);
            if (float.IsNaN(_yaw) || pose != Pose.Walking)
            {
                _yaw = pose == Pose.Walking || pose == Pose.Cpr || seated ? want : float.IsNaN(_yaw) ? want : _yaw;
            }
            else
            {
                var diff = Mathf.Wrap(want - _yaw, -Mathf.Pi, Mathf.Pi);
                _yaw += diff * (1f - Mathf.Exp(-12f * (float)delta));
            }

            _person.Rotation = new Vector3(0, _yaw, 0);
            if (_chair is not null)
            {
                _chair.Rotation = _person.Rotation;
            }

            _person.Hold(baby);
            _person.CarryBag(bag);
            var walking = pose == Pose.Walking;
            var stride = (world.StrideTime(_agent, walking) * 9) + (_agent.Id * 7);
            _person.SetPose(pose, stride);
            _person.Modulate(_agent is Patient { IsAlive: false });

            Ring(world, upright: true);
            SetAlert(null, 0, 0);
        }

        /// <summary>
        /// A patient lying down rather than on their feet: on a trolley or the ambulance crew's stretcher
        /// (head at the back, by the porter), on the floor beside their bed after a fall, or collapsed in an arrest.
        /// </summary>
        private bool LyingDown(WorldLayer world, HospitalSimulation sim, Patient patient, PersonLook look)
        {
            (double X, double Y) at;
            (double X, double Y) dir;
            string carrier;
            Color blanket;
            Color? band = patient.Maternity ? null : Looks.Wristband(patient.Triage);
            BabyLook[]? babies = null;
            AlertKind? alert = patient.InArrest ? AlertKind.Crash : null;
            if (world.OnAmbulanceStretcher(patient))
            {
                (at, dir, carrier, blanket) = (world.Interpolated(patient), (0, 1), "stretcher", new Color("#B5413B"));
            }
            else if (world.OnTrolley(patient))
            {
                blanket = patient.Maternity ? Looks.MaternityBlanket : patient.Stage == PatientStage.OnWard ? Looks.WardBlanket : Looks.EdBlanket;
                (at, dir, carrier) = (world.Interpolated(patient), patient.Facing, "trolley");
                babies = world.BabiesWith(patient);
            }
            else if (patient.OnFloorAfterFall && sim.BedOf(patient) is { } fallenFrom)
            {
                (at, dir, carrier, blanket, band) = ((fallenFrom.StaffSpot.X + 0.5, fallenFrom.StaffSpot.Y + 0.5), (1, 0), "floor", look.Trousers, null);
                alert = AlertKind.Emergency;
            }
            else if (patient.InArrest)
            {
                (at, dir, carrier, blanket, band) = (world.Interpolated(patient), patient.Facing, "floor", look.Top, null);
            }
            else
            {
                return false;
            }

            Node.Position = Kit3D.At(at.X, at.Y);
            var key = (look, carrier, blanket, band, babies?.Length ?? 0);
            if (_lying is null || !Equals(_lyingKey, key))
            {
                ClearLying();
                _lyingKey = key;
                _lying = new Node3D { Name = "LyingDown" };
                var b = new MeshBatch();
                var mattress = carrier switch
                {
                    "stretcher" => Carriers3D.Stretcher(b),
                    "trolley" => Carriers3D.Trolley(b),
                    _ => 0f,
                };
                if (carrier == "stretcher")
                {
                    Carriers3D.Straps(b, mattress);
                }

                b.AddTo(_lying);
                var figure = Lying3D.Build(look, blanket, band, babies);
                figure.Position = new Vector3(0, mattress, 0);
                _lying.AddChild(figure);
                Node.AddChild(_lying);
            }

            var len = Math.Sqrt((dir.X * dir.X) + (dir.Y * dir.Y));
            _lying.Rotation = new Vector3(0, len < 1e-6 ? 0 : Kit3D.Yaw(dir.X / len, dir.Y / len), 0);
            Wheelchair(false, 1);
            Ring(world, upright: false);
            SetAlert(alert, carrier == "floor" ? 1.1f : 1.35f, alert == AlertKind.Crash ? 74 : 100);
            return true;
        }

        private void ClearLying()
        {
            _lying?.QueueFree();
            (_lying, _lyingKey) = (null, null);
        }

        /// <summary>Shows (or takes away) the wheelchair round a seated patient.</summary>
        private void Wheelchair(bool on, float scale)
        {
            if (on == (_chair is not null))
            {
                return;
            }

            if (!on)
            {
                _chair!.QueueFree();
                _chair = null;
                return;
            }

            _chair = new Node3D { Name = "Wheelchair" };
            var b = new MeshBatch();
            Carriers3D.Wheelchair(b, Mathf.Clamp(scale, 0.6f, 1f));
            b.AddTo(_chair);
            Node.AddChild(_chair);
        }

        private void SetAlert(AlertKind? kind, float radius, float height)
        {
            if (kind == _alert?.Kind)
            {
                return;
            }

            _alert?.QueueFree();
            _alert = kind is { } k ? Alert3D.Make(k, radius, height) : null;
            if (_alert is not null)
            {
                Node.AddChild(_alert);
            }
        }

        /// <summary>The ring on the floor: yellow when selected, a patient's triage colour, or lilac for a clinic visitor (people on their feet only).</summary>
        private void Ring(WorldLayer world, bool upright)
        {
            var selected = ReferenceEquals(world.Selected, _agent);
            Color? colour = selected ? SelectedRing : !upright ? null : _agent switch
            {
                Patient p => Looks.TriageRing(p.Triage),
                Outpatient => Looks.OutpatientRing,
                _ => null,
            };
            var radius = upright ? Kit3D.H(13) * 0.85f : 1.25f;
            (Color, float)? key = colour is { } c0 ? (c0, radius) : null;
            if (key == _ringKey)
            {
                return;
            }

            _ringKey = key;
            _ring?.QueueFree();
            _ring = null;
            if (colour is not { } c)
            {
                return;
            }

            var b = new MeshBatch();
            b.Ring(new Vector3(0, 0.02f, 0), radius, c, null, 0.12f);
            _ring = b.AddTo(Node, shadow: false);
        }

        private static (double X, double Y) Facing(Simulation.World.WallSide side) => side switch
        {
            Simulation.World.WallSide.North => (0, -1),
            Simulation.World.WallSide.East => (1, 0),
            Simulation.World.WallSide.West => (-1, 0),
            _ => (0, 1),
        };
    }
}
