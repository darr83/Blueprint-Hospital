using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.Transport;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.TopDown;

/// <summary>
/// Everything that moves, redrawn every frame from above: cars, taxis and ambulances, spills on the
/// floor, crash trolleys, the live screens of the obs monitors, whoever lies in the beds and on the
/// tables, babies in their cots, and everyone on their feet, in a chair, in a wheelchair or on a
/// trolley. It only reads the game.
/// </summary>
public partial class ActorsLayer : Node2D
{
    private static readonly Color StretcherBlanket = new("#B5413B");
    private static readonly Color PlayerRing = new("#2BB3A3");

    private HospitalSimulation _sim = null!;
    private WorldLayer _world = null!;
    private RoomsLayer _rooms = null!;
    private readonly ConditionalWeakTable<Agent, Turn> _turns = new();
    private readonly HashSet<int> _inBeds = new();
    private double _lastTime = double.NaN;
    private double _delta;

    private sealed class Turn
    {
        public double Angle = double.NaN;
    }

    public bool Shadows { get; set; } = true;

    /// <summary>The doctor the player is walking about as in Be A Doctor, marked with a ring of their own.</summary>
    public StaffMember? Player { get; set; }

    /// <summary>True while the player's keys are walking their doctor, so the legs step.</summary>
    public bool PlayerWalking { get; set; }

    /// <summary>How many people were drawn last frame: on their feet, sitting, lying or on a trolley.</summary>
    public int PeopleDrawn { get; private set; }

    /// <summary>How many of them were lying in a bed or on a table.</summary>
    public int InBedDrawn { get; private set; }

    /// <summary>How many cars and ambulances were drawn last frame.</summary>
    public int VehiclesDrawn { get; private set; }

    public void Initialise(HospitalSimulation simulation, WorldLayer world, RoomsLayer rooms)
    {
        _sim = simulation;
        _world = world;
        _rooms = rooms;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_sim is null)
        {
            return;
        }

        var p = new Pen(this, Shadows);
        _delta = double.IsNaN(_lastTime) ? 0 : Math.Clamp(_world.Time - _lastTime, 0, 0.25);
        _lastTime = _world.Time;
        PeopleDrawn = 0;
        InBedDrawn = 0;
        VehiclesDrawn = 0;

        foreach (var spill in _sim.FloorAccidents.Where(a => IsoDraw.OnViewFloor(a.Tile.Y)))
        {
            FurnitureKit.Puddle(p, spill.Tile.X + 0.5, spill.Tile.Y + 0.5, (spill.Tile.X * 31) + (spill.Tile.Y * 17));
        }

        Vehicles(p);
        Monitors(p);
        Beds(p);
        CotBabies(p);
        ResusTrolleys(p);
        People(p);
    }

    // ------------------------------------------------------------------ vehicles

    private void Vehicles(Pen p)
    {
        if (IsoDraw.ViewFloor != 0)
        {
            return;
        }

        foreach (var car in _sim.Cars.Where(c => c.State != CarState.Gone))
        {
            var (x, y) = _world.Interpolated(car);
            var a = Turned(car, Pen.Facing(car.Heading.X, car.Heading.Y), 3.2, always: true);
            VehicleArt.Car(p, x, y, Math.Sin(a), -Math.Cos(a), VehicleArt.CarColours[Math.Abs(car.Id) % VehicleArt.CarColours.Length], car.IsTaxi);
            VehiclesDrawn++;
        }

        foreach (var ambulance in _sim.Ambulances.Where(a => a.State != AmbulanceState.Gone))
        {
            var (x, y) = _world.Interpolated(ambulance);
            var (dx, dy) = ambulance.Heading;
            var a = Turned(ambulance, Math.Abs(dx) + Math.Abs(dy) < 1e-6 ? Math.PI / 2 : Pen.Facing(dx, dy), 2.6, always: true);
            var flashing = ambulance.State is AmbulanceState.Driving or AmbulanceState.WaitingForBay
                           && _sim.FindPatient(ambulance.PatientId) is { Triage: TriageCategory.Red or null };
            VehicleArt.Ambulance(p, x, y, Math.Sin(a), -Math.Cos(a), ambulance.CallSign, flashing ? _world.Time : null);
            VehiclesDrawn++;

            // The crew stand at either end of their stretcher until a nurse takes handover.
            var patient = ambulance is { State: AmbulanceState.AtBay, Purpose: AmbulancePurpose.Arrival } ? _sim.FindPatient(ambulance.PatientId) : null;
            if (patient is not null && _world.OnAmbulanceStretcher(patient))
            {
                var (sx, sy) = _world.Interpolated(patient);
                for (var i = 0; i < 2; i++)
                {
                    var end = i == 0 ? -1 : 1;
                    PeopleArt.Person(p, sx + 0.35, sy + (end * 2.75), 0, -end, Looks.Paramedic((ambulance.Id * 2) + i));
                }
            }
        }
    }

    // ------------------------------------------------------------------ monitors and beds

    private void Monitors(Pen p)
    {
        foreach (var monitor in _world.Monitors)
        {
            if (_world.ConnectedPatient(monitor) is not { } patient)
            {
                continue;
            }

            if (patient.MonitorIsRed)
            {
                var pulse = 0.5f + (0.5f * (float)Math.Sin(_world.Time * 8));
                p.Circle(monitor.X, monitor.Y, 0.75, new Color(Looks.Red, 0.12f + (0.2f * pulse)));
            }

            p.Push(monitor.X, monitor.Y);
            FurnitureKit.MonitorStand(p, true, patient.MonitorColour is { } colour ? Looks.TriageRing(colour) : null);
            p.Pop();
        }
    }

    private void Beds(Pen p)
    {
        _inBeds.Clear();
        var calling = _sim.Tasks
            .Where(t => !t.Started && t.Role is StaffRole.Nurse or StaffRole.HealthcareAssistant && t.Kind is TaskKind.Observations or TaskKind.GiveMedication)
            .Select(t => t.PatientId)
            .ToHashSet();
        foreach (var bed in _rooms.Beds)
        {
            var isBed = bed.TableId is null or "birthing_bed";
            var half = (isBed ? FurnitureKit.BedWidth : FurnitureKit.TableWidth(bed.TableId!) + 0.5) / 2;

            // The theatre lights come on over the table while a case is under way.
            if (bed.Theatre && _sim.Tasks.Any(t => t.Started && t.SpaceId == bed.Space.Id && t.Kind is TaskKind.Surgery or TaskKind.Procedure))
            {
                p.Circle(bed.X, bed.Y, 2.6, Pen.A("#FFF6C9", 0.16f));
                p.Circle(bed.X, bed.Y - 0.3, 1.5, Pen.A("#FFFBE3", 0.22f));
            }

            if (_world.BedOccupant(bed.Spot, bed.OccupantId?.Invoke()) is not { } patient || !_inBeds.Add(patient.Id))
            {
                continue;
            }

            var look = Looks.Patient(patient);
            var selected = ReferenceEquals(_world.Selected, patient);
            p.Push(bed.X, bed.Y, bed.Angle);
            if (selected)
            {
                p.Round(-half - 0.1, -2.6, (half * 2) + 0.2, 5.2, 0.3, Pen.A("#FFEB3B", 0.2f), PeopleArt.SelectedRing, 0.1);
            }
            else if (ReferenceEquals(_world.Hovered, patient))
            {
                p.Round(-half - 0.1, -2.6, (half * 2) + 0.2, 5.2, 0.3, Pen.A("#FFFFFF", 0.3f), PeopleArt.HoverRing, 0.08);
            }

            Alarm(p, patient, half + 0.22, 2.72);
            var (width, length) = isBed ? (FurnitureKit.MattressWidth, FurnitureKit.BedLength - 0.42) : (FurnitureKit.TableWidth(bed.TableId!), FurnitureKit.BedLength - 0.6);
            p.Push(0, isBed ? 0.03 : 0);
            PeopleArt.LyingFigure(p, look, bed.Blanket, patient.Maternity ? null : Looks.Wristband(patient.Triage), _world.OnChest(patient), width, length, !patient.IsAlive);
            CareArt(p, patient, look, width, length);
            p.Pop();
            if (isBed)
            {
                FurnitureKit.BedRails(p);
            }

            // The call light on the bed head, flashing while the patient waits for a nurse.
            if (bed.OnWard && calling.Contains(patient.Id) && Mathf.PosMod((float)_world.Time, 1.2f) < 0.8f)
            {
                p.Circle(0.9, -2.36, 0.3, Pen.A("#FFB300", 0.3f));
                p.Circle(0.9, -2.36, 0.13, new Color("#FFB300"), new Color("#8A5A00"), 0.03);
            }

            p.Pop();
            PeopleDrawn++;
            InBedDrawn++;
        }
    }

    /// <summary>The pulsing outline round someone who needs help now: red for an arrest, amber for an emergency nobody has got to yet.</summary>
    private void Alarm(Pen p, Patient patient, double halfWidth, double halfLength)
    {
        Color? colour = patient.InArrest ? Looks.Red : patient is { Emergency: not null, EmergencyTreated: false } || patient.OnFloorAfterFall ? Looks.Amber : null;
        if (colour is not { } c)
        {
            return;
        }

        var pulse = 0.5f + (0.5f * (float)Math.Sin(_world.Time * (patient.InArrest ? 8 : 5)));
        var grow = 0.12 * pulse;
        p.Round(-halfWidth - grow, -halfLength - grow, (halfWidth + grow) * 2, (halfLength + grow) * 2, 0.4, new Color(c, 0.08f + (0.1f * pulse)), new Color(c, 0.45f + (0.55f * pulse)), 0.11);
    }

    private void CotBabies(Pen p)
    {
        foreach (var cot in _rooms.Cots)
        {
            BabyLook? look = null;
            if (cot.Incubator)
            {
                var baby = _sim.Babies.FirstOrDefault(b => b.Place == BabyPlace.NeonatalUnit && b.UnitId == cot.Space.Id && b.CotIndex == cot.Index);
                look = baby is null ? null : Looks.Baby(baby, _sim.FindPatient(baby.MotherId));
            }
            else if (cot.Bed?.OccupantId is { } id && _sim.IsMotherAndBabyWard(cot.Space) && _sim.FindPatient(id) is { } mother
                     && !mother.IsMoving && mother.Tile == cot.Bed.PatientSpot && _world.InBed(mother) && _world.OnChest(mother) is null)
            {
                look = _world.BabiesWith(mother).FirstOrDefault();
            }

            if (look is { } b)
            {
                PeopleArt.Baby(p, cot.X, cot.Y, cot.Angle, b, 1.05);
                if (cot.Incubator)
                {
                    p.In(cot.X, cot.Y, cot.Angle, () => p.Round(-0.4, -0.6, 0.8, 1.2, 0.14, Pen.A("#BFE3F5", 0.22f)));
                }
            }
        }
    }

    private void ResusTrolleys(Pen p)
    {
        var colour = _sim.Database.Furniture.TryGetValue(HospitalSimulation.ResusTrolleyItem, out var def) ? new Color(def.Colour) : new Color("#D8483B");
        foreach (var trolley in _sim.ResusTrolleys.Where(t => IsoDraw.OnViewFloor(t.Y)))
        {
            var (x, y) = _world.Interpolated(trolley);
            p.Push(x, y, Pen.Facing(trolley.Facing.X, trolley.Facing.Y) + Math.PI);
            FurnitureKit.ResusTrolley(p, 1.0, 1.5, colour);
            p.Pop();
        }
    }

    // ------------------------------------------------------------------ people

    private void People(Pen p)
    {
        var tasks = _sim.Tasks.Where(t => t.Started).ToDictionary(t => t.Id);
        var agents = _world.AgentsInView().Where(a => a is Patient or StaffMember or Outpatient).ToList();

        // Whoever is lying down goes underneath whoever is standing over them.
        foreach (var patient in agents.OfType<Patient>())
        {
            if (!_inBeds.Contains(patient.Id) && Lying(p, patient))
            {
                _inBeds.Add(patient.Id);
                PeopleDrawn++;
            }
        }

        foreach (var agent in agents.OrderBy(a => _world.IsSeated(a) ? 0 : 1))
        {
            if (agent is Patient { } done && _inBeds.Contains(done.Id))
            {
                continue;
            }

            Upright(p, agent, tasks);
            PeopleDrawn++;
        }
    }

    /// <summary>A patient on a trolley, on the ambulance crew's stretcher or on the floor. False if they are on their feet.</summary>
    /// <summary>
    /// What a procedure adds to a patient in bed, seen from above: the ECG leads and the machine on its cart,
    /// knees up for a birth, gel for a scan, the cap of electrodes for an EEG. The 3D view's Beds3D does the same.
    /// </summary>
    private void CareArt(Pen p, Patient patient, PersonLook look, double width, double length)
    {
        var (care, task) = World3D.Procedure3D.For(patient, _sim);
        if (care == World3D.Care.None || task is not { Started: true })
        {
            return;
        }

        var k = Math.Min(1.0, look.Scale + 0.08);
        var headY = (-length / 2) + 0.56;
        var chestY = headY + (1.0 * k);
        var worker = World3D.Procedure3D.Doing(task, _sim);
        switch (care)
        {
            case World3D.Care.Ecg when worker is not null:
            {
                double side = World3D.Procedure3D.Side(patient, worker, _sim);
                var cart = (X: side * ((width / 2) + 1.0), Y: headY + (2.3 * k));
                var colours = new[] { "#E53935", "#FDD835", "#43A047", "#8D6E63", "#263238", "#8E24AA" };
                p.Ellipse(0, chestY, 0.42 * k, 0.32 * k, look.Skin);
                for (var i = 0; i < colours.Length; i++)
                {
                    var (x, y) = ((0.3 - (i * 0.12)) * k, chestY - 0.08 + (0.04 * i));
                    p.Line(x, y, side * 0.2, chestY + 0.3, new Color(colours[i]), 0.025);
                    p.Circle(x, y, 0.055, Colors.White, new Color(colours[i]), 0.025);
                }

                p.Path(new Color("#546E7A"), 0.05, (side * 0.2, chestY + 0.3), (side * (width / 2), chestY + 0.6), (cart.X - (side * 0.3), cart.Y));
                p.Box(cart.X - 0.42, cart.Y - 0.42, 0.84, 0.84, 0.1, new Color("#F5F1E6"));
                p.Round(cart.X - 0.3, cart.Y - 0.3, 0.6, 0.4, 0.05, new Color("#0B1F17"));
                var beat = Mathf.PosMod((float)_world.Time * 0.9f, 1f);
                p.Path(new Color("#5CFF8A"), 0.035, (cart.X - 0.26, cart.Y - 0.1), (cart.X - 0.08, cart.Y - 0.1), (cart.X - 0.03, cart.Y - 0.24), (cart.X + 0.03, cart.Y - 0.02), (cart.X + 0.08, cart.Y - 0.1), (cart.X + 0.26, cart.Y - 0.1));
                p.Circle(cart.X - 0.26 + (0.52 * beat), cart.Y - 0.1, 0.035, new Color("#D8FFE4"));
                break;
            }

            case World3D.Care.Birth when worker is not null && World3D.Procedure3D.Born(task, _sim) < 2:
            {
                // Knees up and apart under a drape, feet in the supports.
                var kneeY = headY + (2.15 * k);
                p.Ellipse(0, kneeY - 0.15, 0.62, 0.5, new Color("#F7F9FA"), new Color("#C9D3DA"), 0.03);
                foreach (var s in new[] { -1.0, 1.0 })
                {
                    p.Line(s * 0.42, kneeY, s * 0.6, kneeY + 0.62, look.Skin, 0.2);
                    p.Circle(s * 0.42, kneeY, 0.17, new Color("#EEF2F5"), new Color("#C9D3DA"), 0.03);
                    p.Round((s * 0.6) - 0.16, kneeY + 0.45, 0.32, 0.36, 0.08, new Color("#37474F"));
                    p.Circle(s * 0.6, kneeY + 0.66, 0.12, Colors.White, new Color("#C9D3DA"), 0.03);
                }

                break;
            }

            case World3D.Care.Echo:
                p.Ellipse(0, chestY, 0.42 * k, 0.32 * k, look.Skin);
                p.Ellipse(-0.1, chestY, 0.14, 0.1, Pen.A("#9AD8F2", 0.9f));
                break;
            case World3D.Care.Ultrasound:
                p.Ellipse(0, chestY + (0.55 * k), 0.42 * k, 0.3 * k, look.Skin);
                p.Ellipse(0.04, chestY + (0.55 * k), 0.16, 0.11, Pen.A("#9AD8F2", 0.9f));
                break;
            case World3D.Care.Eeg:
            {
                var colours = new[] { "#E53935", "#FDD835", "#43A047", "#1E88E5", "#8E24AA" };
                for (var i = 0; i < colours.Length; i++)
                {
                    var a = (i / 4.0 * 2.4) - 1.2;
                    p.Circle(Math.Sin(a) * 0.24 * k, headY - (Math.Cos(a) * 0.2 * k), 0.05, new Color(colours[i]));
                }

                break;
            }

            case World3D.Care.Obs when worker is not null:
            {
                // The cuff on the arm, its tube to the little machine on its stand, a heart beating on the screen.
                double side = World3D.Procedure3D.Side(patient, worker, _sim);
                var arm = (X: side * 0.5 * k, Y: chestY + 0.05);
                var stand = (X: side * ((width / 2) + 0.55), Y: headY + (1.9 * k));
                p.Path(new Color("#607D8B"), 0.04, (arm.X, arm.Y), (side * (width / 2), arm.Y + 0.3), (stand.X, stand.Y));
                p.Round(arm.X - 0.13, arm.Y - 0.16, 0.26, 0.32, 0.08, new Color("#1F3A5F"));
                p.Box(stand.X - 0.26, stand.Y - 0.22, 0.52, 0.44, 0.08, new Color("#F2F4F5"));
                p.Round(stand.X - 0.2, stand.Y - 0.16, 0.4, 0.3, 0.04, new Color("#0B1F17"));
                var beat = Mathf.PosMod((float)_world.Time * 1.2f, 1f) < 0.25f ? 0.085 : 0.06;
                p.Circle(stand.X - 0.1, stand.Y - 0.05, beat, new Color("#FF5252"));
                p.Line(stand.X, stand.Y - 0.05, stand.X + 0.14, stand.Y - 0.05, new Color("#5CFF8A"), 0.05);
                break;
            }

            case World3D.Care.Bloods or World3D.Care.FingerPrick when worker is not null:
            {
                // A tourniquet on the arm and the tray of tubes on the bed.
                double side = World3D.Procedure3D.Side(patient, worker, _sim);
                if (care == World3D.Care.Bloods)
                {
                    p.Round((side * 0.5 * k) - 0.13, chestY - 0.04, 0.26, 0.12, 0.05, new Color("#1E88E5"));
                }

                var tray = (X: side * 0.3, Y: headY + (2.5 * k));
                p.Round(tray.X - 0.2, tray.Y - 0.13, 0.4, 0.26, 0.08, new Color("#D9D2C3"), new Color("#9C9484"), 0.03);
                var caps = new[] { "#8E24AA", "#FDD835", "#1E88E5" };
                for (var i = 0; i < caps.Length; i++)
                {
                    p.Circle(tray.X - 0.1 + (i * 0.1), tray.Y, 0.04, new Color(caps[i]));
                }

                break;
            }

            case World3D.Care.PeakFlow:
                // The meter held up to the mouth.
                p.Round(-0.07, headY + (0.2 * k), 0.14, 0.46, 0.05, Colors.White, new Color("#90A4AE"), 0.03);
                p.Circle(0, headY + (0.24 * k), 0.07, new Color("#4FC3F7"));
                break;
            case World3D.Care.AsciticTap or World3D.Care.JointAspiration or World3D.Care.LumbarPuncture when worker is not null:
            {
                // The sterile drape with its window: on the tummy, the knee, or low on the back.
                double side = World3D.Procedure3D.Side(patient, worker, _sim);
                var at = care == World3D.Care.JointAspiration ? (X: side * 0.2, Y: headY + (2.6 * k)) : (X: side * 0.22 * k, Y: chestY + (0.6 * k));
                p.Round(at.X - 0.24, at.Y - 0.24, 0.48, 0.48, 0.06, new Color("#3F8FD6"), new Color("#2A6CB0"), 0.03);
                p.Circle(at.X, at.Y, 0.09, look.Skin);
                break;
            }
        }
    }

    private bool Lying(Pen p, Patient patient)
    {
        var look = Looks.Patient(patient);
        var pose = new PersonPose { Selected = ReferenceEquals(_world.Selected, patient), Hovered = ReferenceEquals(_world.Hovered, patient), Grey = !patient.IsAlive };
        var band = patient.Maternity ? (Color?)null : Looks.Wristband(patient.Triage);
        var (x, y) = _world.Interpolated(patient);
        if (_world.OnAmbulanceStretcher(patient))
        {
            // Head to the north, by the crew member at that end.
            Alert(p, patient, x, y, 2.2);
            PeopleArt.Trolley(p, x, y, 0, -1, look, StretcherBlanket, band, null, true, pose);
            return true;
        }

        if (_world.OnTrolley(patient))
        {
            // Feet first: the head is at the back, by whoever is pushing.
            var blanket = patient.Maternity ? Looks.MaternityBlanket : patient.Stage == PatientStage.OnWard ? Looks.WardBlanket : Looks.EdBlanket;
            var a = Turned(patient, Pen.Facing(-patient.Facing.X, -patient.Facing.Y), 9, always: true);
            Alert(p, patient, x, y, 2.2);
            PeopleArt.Trolley(p, x, y, Math.Sin(a), -Math.Cos(a), look, blanket, band, _world.BabiesWith(patient), false, pose);
            return true;
        }

        if (patient.OnFloorAfterFall && _sim.BedOf(patient) is { } fallenFrom)
        {
            (x, y) = (fallenFrom.StaffSpot.X + 0.5, fallenFrom.StaffSpot.Y + 0.5);
            Alert(p, patient, x, y, 1.7);
            PeopleArt.OnFloor(p, x, y, 1, 0, look, pose);
            return true;
        }

        if (patient.InArrest && !_world.InBed(patient))
        {
            Alert(p, patient, x, y, 1.7);
            PeopleArt.OnFloor(p, x, y, patient.Facing.X, patient.Facing.Y, look, pose);
            return true;
        }

        return false;
    }

    /// <summary>The pulsing ring on the floor round a patient who needs help now and is not in a bed.</summary>
    private void Alert(Pen p, Patient patient, double x, double y, double radius)
    {
        Color? colour = patient.InArrest ? Looks.Red : patient.OnFloorAfterFall || patient is { Emergency: not null, EmergencyTreated: false } ? Looks.Amber : null;
        if (colour is { } c)
        {
            var pulse = 0.5f + (0.5f * (float)Math.Sin(_world.Time * (patient.InArrest ? 8 : 5)));
            p.Circle(x, y, radius + (0.15 * pulse), new Color(c, 0.08f + (0.1f * pulse)), new Color(c, 0.45f + (0.55f * pulse)), 0.11);
        }
    }

    private void Upright(Pen p, Agent agent, Dictionary<int, ClinicalTask> tasks)
    {
        var look = agent switch
        {
            Patient patient => Looks.Patient(patient),
            StaffMember member => Looks.Staff(member),
            Outpatient outpatient => Looks.Outpatient(outpatient, _sim.FindAppointment(outpatient.AppointmentId)),
            _ => null,
        };
        if (look is null)
        {
            return;
        }

        var (x, y) = _world.FootGrid(agent);
        var facing = agent.Facing;
        var seated = _world.IsSeated(agent);
        var walking = agent.IsMoving || (PlayerWalking && ReferenceEquals(agent, Player));
        var pushing = false;
        double? cpr = null;
        BabyLook? baby = null;
        var bag = false;
        var wheelchair = false;
        var bucket = false;
        Color? ring = null;
        var snap = false;

        switch (agent)
        {
            case StaffMember member when _world.CprSpot(member) is { } spot:
                (x, y) = (spot.FootX, spot.FootY);
                facing = (spot.ChestX - spot.FootX, spot.ChestY - spot.FootY);
                cpr = _world.Time;
                snap = true;
                break;
            case StaffMember member:
                pushing = member.PushingPatientId is { } pid && _sim.FindPatient(pid) is { IsMoving: true };
                pushing |= member.CrashPatientId is not null && member.IsMoving && _sim.ResusTrolleys.Any(t => t.PushedByStaffId == member.Id);
                baby = _world.CarriedBy(member);
                bag = _sim.IsCarryingMedicines(member);
                bucket = member.IsOnDuty && !member.IsOnBreak && _sim.CleanerJobOf(member) is { HasKit: true };
                pushing |= bucket && member.IsMoving;

                // Busy with a patient: turn to them.
                if (!walking && !seated && member.CurrentTaskId is { } taskId && tasks.TryGetValue(taskId, out var task)
                    && _sim.FindPatient(task.PatientId) is { } with && IsoDraw.OnViewFloor(with.Y))
                {
                    var (dx, dy) = (with.X - x, with.Y - y);
                    if (Math.Abs(dx) + Math.Abs(dy) is > 0.3 and < 8)
                    {
                        facing = (dx, dy);
                    }
                }

                break;
            case Patient patient:
                baby = _world.InArms(patient);
                wheelchair = _world.InWheelchair(patient);
                ring = Looks.TriageRing(patient.Triage);
                break;
            case Outpatient:
                ring = Looks.OutpatientRing;
                break;
        }

        if (seated)
        {
            facing = ChairArt.Direction(_world.SeatFacing(agent));
            snap = true;
        }

        var want = Math.Abs(facing.X) + Math.Abs(facing.Y) < 1e-6 ? 0 : Pen.Facing(facing.X, facing.Y);
        var angle = snap ? Turned(agent, want, 1000, always: true) : Turned(agent, want, walking ? 12 : 6, always: true);
        var (fx, fy) = (Math.Sin(angle), -Math.Cos(angle));
        if (ReferenceEquals(agent, Player))
        {
            p.Circle(x, y, 0.78, new Color(PlayerRing, 0.18f), PlayerRing, 0.1);
        }

        if (bucket && !seated)
        {
            PeopleArt.MopBucket(p, x, y, angle);
        }

        PeopleArt.Person(p, x, y, fx, fy, look, new PersonPose
        {
            Seated = seated,
            Walking = walking,
            Stride = (_world.StrideTime(agent, walking) * 9) + (agent.Id * 7),
            Pushing = pushing,
            Cpr = cpr,
            Ring = ring,
            Selected = ReferenceEquals(_world.Selected, agent),
            Hovered = ReferenceEquals(_world.Hovered, agent),
            Baby = baby,
            Bag = bag,
            Grey = agent is Patient { IsAlive: false },
            Wheelchair = wheelchair,
        });
    }

    /// <summary>Turns someone smoothly towards <paramref name="want"/> (radians) rather than snapping round, and gives the angle to draw them at.</summary>
    private double Turned(Agent agent, double want, double rate, bool always)
    {
        var turn = _turns.GetOrCreateValue(agent);
        if (double.IsNaN(turn.Angle) || rate >= 1000)
        {
            turn.Angle = want;
            return want;
        }

        var diff = Mathf.Wrap(want - turn.Angle, -Math.PI, Math.PI);
        turn.Angle += diff * (1 - Math.Exp(-rate * _delta));
        return turn.Angle;
    }

    // ------------------------------------------------------------------ picking

    /// <summary>The patient, visitor or member of staff under a grid point: the nearest person within reach, or whoever lies in the bed there.</summary>
    public Agent? HitTest(double gx, double gy)
    {
        Agent? best = null;
        var bestGap = 0.85;
        foreach (var agent in _world.AgentsInView())
        {
            if (agent is not (Patient or StaffMember or Outpatient))
            {
                continue;
            }

            var (x, y) = _world.FootGrid(agent);
            if (agent is StaffMember member && _world.CprSpot(member) is { } spot)
            {
                (x, y) = (spot.FootX, spot.FootY);
            }

            var gap = Math.Sqrt(Math.Pow(gx - x, 2) + Math.Pow(gy - y, 2));
            if (agent is Patient patient && (_world.IsLying(patient) || patient.OnFloorAfterFall))
            {
                // Anywhere on the bed or trolley picks its patient, but someone standing beside it wins.
                if (_world.InBed(patient))
                {
                    (x, y) = (patient.Tile.X + 0.5, patient.Tile.Y + 0.5);
                }

                gap = Math.Abs(gx - x) < 1.2 && Math.Abs(gy - y) < 2.4 ? Math.Min(gap, 0.8) : 99;
            }

            if (gap < bestGap)
            {
                (best, bestGap) = (agent, gap);
            }
        }

        return best;
    }
}
