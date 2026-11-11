using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.Hospital;
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

    /// <summary>The animation clock, in real seconds; it stands still while the game is paused.</summary>
    public double AnimTime { get; internal set; }

    private Dictionary<int, ClinicalTask> _tasks = new();

    public void Sync(double delta)
    {
        var world = _view.World;
        var sim = _view.Simulation;
        if (!sim.Clock.IsPaused)
        {
            AnimTime += delta;
        }

        _tasks = sim.Tasks.Where(t => t.Started).ToDictionary(t => t.Id);
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

        /// <summary>Just short of a drip stand, on the side they come from, facing it.</summary>
        private static (double X, double Y, double FaceX, double FaceY) StandSpot((double X, double Y) stand, double x, double y)
        {
            var (dx, dy) = (x - stand.X, y - stand.Y);
            var d = Math.Max(0.01, Math.Sqrt((dx * dx) + (dy * dy)));
            return (stand.X + (dx / d * 0.75), stand.Y + (dy / d * 0.75), -dx, -dy);
        }

        private (double X, double Y) _shift;
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

            // A ward patient out of bed for physio is drawn on their feet beside it.
            var physio = _agent is Patient pp ? Activity3D.PhysioWalk(pp, world, sim, _owner.AnimTime) : null;
            var visible = world.OnViewFloor(_agent) && _agent switch
            {
                Patient p => world.IsOnMap(p) && (!world.InBed(p) || physio is not null),
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

            if (physio is { Frame: true } && !look.Frame)
            {
                look = look with { Frame = true };
            }

            if (_agent is Patient lyingPatient && physio is null && LyingDown(world, sim, lyingPatient, look))
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

            // What they are doing with their hands, and who or what they turn to while they do it.
            var inChair = _agent is Patient wp && world.InWheelchair(wp);
            var t = _owner.AnimTime + (_agent.Id * 1.618);
            var (act, faceTo) = pose == Pose.Cpr || inChair || baby is not null ? (Act.None, null) : Activity3D.For(_agent, world, sim, _owner._tasks, _owner.AnimTime);
            if (faceTo is { } target && !seated)
            {
                var (dx, dy) = (target.X - x, target.Y - y);
                var d = Math.Sqrt((dx * dx) + (dy * dy));
                if (d > 0.2)
                {
                    facing = (dx / d, dy / d);
                    pose = Pose.Standing;
                }
            }

            // A radiographer goes behind the lead screen to take the picture, then comes back to the patient.
            if (_agent is StaffMember radiographer && Activity3D.ScanVisit(radiographer, world, sim, _owner._tasks, _owner.AnimTime) is { } visit)
            {
                (x, y) = visit.At;
                (act, faceTo) = (visit.Act, null);
                facing = visit.Facing;
                pose = visit.Walking ? Pose.Walking : Pose.Standing;
            }

            // A pharmacist goes to the shelves for the box and back to count the tablets out.
            if (_agent is StaffMember pharmacist && Activity3D.PharmacyVisit(pharmacist, world, sim, _owner._tasks, _owner.AnimTime) is { } round)
            {
                (x, y) = round.At;
                (act, faceTo) = (round.Act, null);
                facing = round.Facing;
                pose = round.Walking ? Pose.Walking : Pose.Standing;
            }

            // Checking in: step along the counter to the check-in screen, and back again after. Putting up a
            // drip: step round to the drip stand.
            {
                var kiosk = act == Act.CheckIn ? _owner._view.Life.KioskNear(x, y, 3)
                    : act == Act.HangDrip && _agent is StaffMember dripper && Activity3D.DripStandFor(dripper, sim, _owner._tasks) is { } stand
                        ? StandSpot(stand, x - _shift.X, y - _shift.Y)
                        : null;
                var goal = kiosk is { } k ? (X: k.X - x, Y: k.Y - y) : (X: 0.0, Y: 0.0);
                var (gx, gy) = (goal.X - _shift.X, goal.Y - _shift.Y);
                var gap = Math.Sqrt((gx * gx) + (gy * gy));
                if (gap > 0.04 || _shift != (0, 0))
                {
                    var stepLen = Math.Min(gap, 1.6 * delta);
                    _shift = gap < 1e-6 ? goal : (_shift.X + (gx / gap * stepLen), _shift.Y + (gy / gap * stepLen));
                    if (gap > 0.04 && !_agent.IsMoving)
                    {
                        (act, faceTo) = (Act.None, null);
                        facing = (gx, gy);
                        pose = Pose.Walking;
                    }

                    if (!_agent.IsMoving)
                    {
                        (x, y) = (x + _shift.X, y + _shift.Y);
                        if (gap <= 0.04 && kiosk is { } at)
                        {
                            facing = (at.FaceX, at.FaceY);
                            faceTo = null;
                        }
                    }
                    else
                    {
                        _shift = (0, 0);
                    }
                }
            }

            // The anaesthetist works from the head of the table, by their machine.
            if (act == Act.Bag && _agent is StaffMember gas && Activity3D.AnaesthetistSpot(gas, sim, _owner._tasks) is { } head)
            {
                (x, y) = head.At;
                facing = head.Facing;
                faceTo = null;
            }

            // A plaster technician works right beside the couch, at the limb.
            if (act == Act.Wrap && _agent is StaffMember tech && Activity3D.PlasterSpot(tech, world, sim, _owner._tasks) is { } spot)
            {
                (x, y) = spot.At;
                facing = spot.Facing;
                faceTo = null;
            }

            // Physio on the ward: the patient walks up and down beside their bed with the physio alongside.
            if (physio is not null)
            {
                (x, y) = physio.At;
                (act, faceTo) = (Act.None, null);
                facing = physio.Facing;
                pose = physio.Walking ? Pose.Walking : Pose.Standing;
                seated = physio.Seated;
            }
            else if (_agent is StaffMember helper && Activity3D.PhysioHelping(helper, world, sim, _owner.AnimTime) is { } step)
            {
                (x, y) = step.Helper;
                (act, faceTo) = (step.Walking ? Act.Support : Act.Talk, null);
                facing = step.Walking ? step.Facing : (step.At.X - step.Helper.X, step.At.Y - step.Helper.Y);
                pose = step.Walking ? Pose.Walking : Pose.Standing;
            }

            Wheelchair(inChair, look.Scale);
            TurnWheels(x, y);
            if (seated)
            {
                pose = Pose.Seated;
                _person.Seat = inChair ? Person3D.SeatShape.Wheelchair(look.Scale) : physio is not null ? Person3D.SeatShape.BedSide : Person3D.SeatShape.Chair;
                facing = world.IsSeated(_agent) && physio is null ? Facing(world.SeatFacing(_agent)) : facing;
            }

            Node.Position = Kit3D.At(x, y);

            // Turn smoothly towards where they're heading, rather than snapping round.
            var want = Kit3D.Yaw(facing.X, facing.Y);
            if (float.IsNaN(_yaw) || pose != Pose.Walking)
            {
                _yaw = pose == Pose.Walking || pose == Pose.Cpr || seated || float.IsNaN(_yaw) ? want
                    : act != Act.None || faceTo is not null ? _yaw + (Mathf.Wrap(want - _yaw, -Mathf.Pi, Mathf.Pi) * (1f - Mathf.Exp(-6f * (float)delta)))
                    : _yaw;
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
            if (walking && !_agent.IsMoving)
            {
                // Walking where only the drawing moves (a round to the shelves, physio): step in time with the clock.
                stride = (t * 6.5) + (_agent.Id * 7);
            }

            _person.SetPose(pose, stride);
            _person.SetAct(act, pose, t);

            // A cleaner with their kit pushes the bucket along, or has it beside them while they mop or sweep.
            var kit = CleanKit.None;
            if (_agent is StaffMember cleaner && cleaner.IsOnDuty && !cleaner.IsOnBreak && sim.CleanerJobOf(cleaner) is { HasKit: true })
            {
                kit = act == Act.Mop ? CleanKit.Mopping : act == Act.Sweep ? CleanKit.Sweeping : CleanKit.Pushing;
            }

            _person.Clean(kit, t);
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

        private Node3D[] _wheels = Array.Empty<Node3D>();
        private (double X, double Y)? _lastAt;
        private float _wheelTurn;

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
                _wheels = Array.Empty<Node3D>();
                return;
            }

            _chair = new Node3D { Name = "Wheelchair" };
            var b = new MeshBatch();
            Carriers3D.Wheelchair(b, Mathf.Clamp(scale, 0.6f, 1f), wheels: false);
            b.AddTo(_chair);
            _wheels = new[] { -1f, 1f }.Select(side =>
            {
                var wheel = new Node3D { Name = side < 0 ? "WheelL" : "WheelR", Position = Carriers3D.WheelchairHub(side) };
                var wb = new MeshBatch();
                Carriers3D.WheelchairWheel(wb, side);
                wb.AddTo(wheel);
                _chair.AddChild(wheel);
                return wheel;
            }).ToArray();
            Node.AddChild(_chair);
        }

        /// <summary>Rolls the wheelchair's wheels round by however far it has gone since the last frame.</summary>
        private void TurnWheels(double x, double y)
        {
            if (_lastAt is { } last && _wheels.Length > 0)
            {
                var moved = (float)Math.Sqrt(Math.Pow(x - last.X, 2) + Math.Pow(y - last.Y, 2));
                if (moved < 1f)
                {
                    _wheelTurn += moved / Person3D.V(9.2f);
                }

                foreach (var wheel in _wheels)
                {
                    wheel.Rotation = new Vector3(_wheelTurn, 0, 0);
                }
            }

            _lastAt = (x, y);
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
