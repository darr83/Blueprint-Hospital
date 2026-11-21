using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.World3D;

/// <summary>What someone is doing with their hands, on top of how they stand or sit.</summary>
public enum Act
{
    None,

    /// <summary>A doctor at the bedside: leans in and listens with the stethoscope, then writes a note.</summary>
    Examine,

    /// <summary>A nurse doing observations: BP cuff hand on the patient, clipboard in the other.</summary>
    Observations,

    /// <summary>Bending over the arm to take blood or do a bedside test.</summary>
    Bloods,

    /// <summary>Handing over medicine: a pot in an outstretched hand.</summary>
    Medicine,

    /// <summary>At a keyboard: reception, the radiographer's console, the pharmacy.</summary>
    Typing,

    /// <summary>At the operating table: leaning in, both hands working.</summary>
    Operate,

    /// <summary>Taking blood, or a needle test (blood gas, lumbar puncture, a tap): bent over with a syringe.</summary>
    Needle,

    /// <summary>A finger-prick test: the meter in one hand, the patient's finger in the other.</summary>
    Prick,

    /// <summary>A urine test: the sample pot in one hand, dipping the strip and reading it.</summary>
    Dip,

    /// <summary>Taking a swab from the nose and throat.</summary>
    Swab,

    /// <summary>Observations: the thermometer in the patient's ear.</summary>
    Thermometer,

    /// <summary>Observations: writing the numbers on the chart.</summary>
    Write,

    /// <summary>Both hands up at the patient's face: fitting a mask, passing a tube, packing a nose.</summary>
    Fit,

    /// <summary>Talking someone through something: hands gesturing.</summary>
    Talk,

    /// <summary>A physio showing an exercise: arms up and down.</summary>
    Exercise,

    /// <summary>At a vending machine: pressing the buttons.</summary>
    VendPress,

    /// <summary>At a vending machine: bending down to take it out of the tray.</summary>
    VendCollect,

    /// <summary>At a café table: a cup to the mouth now and then.</summary>
    Eat,

    /// <summary>Sitting looking at their phone.</summary>
    Phone,

    /// <summary>Sitting reading a magazine.</summary>
    Read,

    /// <summary>Glancing at their watch.</summary>
    Watch,

    /// <summary>A child swinging their legs on a chair.</summary>
    Fidget,

    /// <summary>Washing hands at a basin.</summary>
    WashHands,

    /// <summary>On a break with a mug of tea.</summary>
    Tea,

    /// <summary>Hanging a new bag of fluid on the drip stand: both hands up at the hook.</summary>
    HangDrip,

    /// <summary>On the phone: the handset held to the ear, the other hand making notes.</summary>
    PhoneCall,

    /// <summary>Tapping their details into the check-in screen on the counter.</summary>
    CheckIn,

    /// <summary>Reaching up to take a box down from the shelves.</summary>
    Reach,

    /// <summary>Counting tablets out on a tray at the counter.</summary>
    Count,

    /// <summary>The anaesthetist at the head of the table, squeezing the breathing bag.</summary>
    Bag,

    /// <summary>Wrapping a plaster cast round and round.</summary>
    Wrap,

    /// <summary>A physio walking alongside a patient, an arm out ready to steady them.</summary>
    Support,

    /// <summary>Scanning: the ultrasound probe held out on the patient, moved about in small circles.</summary>
    Scan,

    /// <summary>An endoscopy: the scope's control held up in one hand, the other feeding the tube.</summary>
    Scope,

    /// <summary>Carrying a newborn: both arms out in front, cradling.</summary>
    Carry,

    /// <summary>A cleaner mopping the floor, the mop swinging side to side, with a wet floor sign out.</summary>
    Mop,

    /// <summary>A cleaner sweeping with a wide brush, pushing it out and drawing it back.</summary>
    Sweep,
}

/// <summary>
/// Works out, from the simulation alone, what a person is doing at this moment, so the figure can be
/// animated to match: a doctor examining the patient they are with, a radiographer at the console, a
/// patient pressing the vending machine buttons. Nothing here changes the simulation.
/// </summary>
public static class Activity3D
{
    /// <summary>
    /// The act and, when it has one, the point (in grid units) they should face while doing it.
    /// <paramref name="time"/> is the animation clock in real seconds, used to vary idle habits.
    /// </summary>
    public static (Act Act, (double X, double Y)? FaceTo) For(Agent agent, WorldLayer world, HospitalSimulation sim, IReadOnlyDictionary<int, ClinicalTask> tasks, double time)
    {
        if (agent.IsMoving)
        {
            return (Act.None, null);
        }

        if (sim.ToiletTripOf(agent) is { Stage: ToiletStage.Washing })
        {
            return (Act.WashHands, null);
        }

        switch (agent)
        {
            case StaffMember member:
                return ForStaff(member, world, sim, tasks, time);
            case Patient or Outpatient:
                return ForVisitor(agent, world, sim, time);
            default:
                return (Act.None, null);
        }
    }

    private static (Act, (double, double)?) ForStaff(StaffMember member, WorldLayer world, HospitalSimulation sim, IReadOnlyDictionary<int, ClinicalTask> tasks, double time)
    {
        if (member.Activity == StaffActivity.OnBreak)
        {
            // Tea, and a chat with whoever else is on their break.
            var mate = world.IsSeated(member) && sim.Staff.Any(o => o != member && o.Activity == StaffActivity.OnBreak && world.IsSeated(o) && Math.Abs(o.X - member.X) + Math.Abs(o.Y - member.Y) < 4);
            var habit = Habit(member.Id, time, 8);
            return (!world.IsSeated(member) ? Act.None : mate && habit < 0.45 ? Act.Talk : habit < 0.85 ? Act.Tea : Act.None, null);
        }

        if (member.Role == StaffRole.Cleaner && sim.CleanerJobOf(member) is { Stage: CleanerStage.Cleaning, HasKit: true } job)
        {
            return (job.Mopping ? Act.Mop : Act.Sweep, null);
        }

        if (member.Activity == StaffActivity.Idle)
        {
            return Idle(member, world, sim, time);
        }

        var taskId = member.CurrentTaskId ?? member.AssistingTaskId;
        if (member is { Role: StaffRole.Anaesthetist, IsMoving: false } && taskId is { } gasId && sim.Tasks.FirstOrDefault(t => t.Id == gasId) is { Kind: TaskKind.Surgery, Phase: SurgeryPhase.Anaesthetic } induction
            && sim.FindPatient(induction.PatientId) is { IsMoving: false } sleepy && Procedure3D.GoingUnder(sleepy, sim) is { } under)
        {
            // In the anaesthetic room: the cannula in the hand, then the mask held on as they drift off.
            return (under < 0.4f ? Act.Needle : Act.Fit, (sleepy.X + 0.5, sleepy.Y + 0.5));
        }

        if (member.Activity != StaffActivity.Working || taskId is not { } id || !tasks.TryGetValue(id, out var task) || !task.Started)
        {
            return (Act.None, null);
        }

        var patient = sim.FindPatient(task.PatientId);
        (double, double)? face = patient is null ? null : Centre(patient, world);
        var act = task.Kind switch
        {
            TaskKind.DoctorAssessment or TaskKind.DoctorReview or TaskKind.WardRound or TaskKind.WardClerking
                or TaskKind.RedReview or TaskKind.EmergencyReview or TaskKind.OutreachReview
                => Habit(member.Id, time, 9) < 0.7 ? Act.Examine : Act.Talk,
            TaskKind.Observations or TaskKind.Triage or TaskKind.AmbulanceHandover => ObsAct(member.Id, time),
            TaskKind.TakeBloods or TaskKind.BedsideTest => TestAct(task, member.Id, time),
            TaskKind.GiveMedication or TaskKind.EmergencyTreatment => TreatAct(task, member, sim, time) ?? Act.Medicine,
            TaskKind.Register or TaskKind.Imaging or TaskKind.DispenseMedicines => Act.Typing,
            TaskKind.Surgery when member.Role == StaffRole.Anaesthetist => Act.Bag,
            TaskKind.Surgery => Act.Operate,
            TaskKind.Procedure when member.Role == StaffRole.Anaesthetist => Act.Bag,
            TaskKind.Procedure when member.Role == StaffRole.PlasterTechnician => Act.Wrap,
            TaskKind.Procedure => TreatAct(task, member, sim, time) ?? Act.Operate,
            TaskKind.Physio => Act.Exercise,
            TaskKind.Discharge or TaskKind.WardDischarge => Act.Talk,
            TaskKind.StayWithPatient => Act.Talk,
            _ => Act.None,
        };

        if (act == Act.Medicine && task.Kind == TaskKind.GiveMedication && patient is not null && world.InBed(patient)
            && OnDrip(task, sim) && (DripStandBy(patient, sim) is not null || sim.Spaces.Any(r => r.IsWard && r.Rect.Contains(patient.Tile))))
        {
            // Some medicines go up as a drip: hang the new bag on the stand by the bed, or on the
            // bed's own pole (drawn by Beds3D while the drip goes up) when there is no stand.
            return (Act.HangDrip, DripStandBy(patient, sim) ?? (patient.X + 0.5, patient.Y + 0.5));
        }

        if (task.Kind is TaskKind.Imaging or TaskKind.DispenseMedicines or TaskKind.Register)
        {
            // At the console or the counter: face the desk they are at. A radiographer with no console
            // to hand lines the patient up on the table instead.
            if (DeskAhead(member, world, sim) is { } desk)
            {
                face = desk;
            }
            else if (task.Kind == TaskKind.Imaging)
            {
                act = Habit(member.Id, time, 5) < 0.6 ? Act.Bloods : Act.Talk;
            }
        }

        return (act, face);
    }

    /// <summary>
    /// Between jobs: a chat with a colleague standing nearby, or catching up on notes at the desk or nurses'
    /// station they're beside; otherwise just standing ready.
    /// </summary>
    private static (Act, (double, double)?) Idle(StaffMember member, WorldLayer world, HospitalSimulation sim, double time)
    {
        if (world.IsSeated(member))
        {
            var h = Habit(member.Id, time, 15);
            return (h < 0.5 ? Act.Typing : h < 0.75 && member.Role == StaffRole.Receptionist ? Act.PhoneCall : Act.None, null);
        }

        var colleague = sim.Staff
            .Where(o => o != member && o.Activity == StaffActivity.Idle && !o.IsMoving && o.IsInBuilding)
            .Select(o => (Other: o, D: Math.Abs(o.X - member.X) + Math.Abs(o.Y - member.Y)))
            .Where(o => o.D is > 0.5 and < 4.5)
            .OrderBy(o => o.D)
            .Select(o => o.Other)
            .FirstOrDefault();
        if (colleague is not null)
        {
            // Take turns talking: one gestures while the other listens.
            var talking = (int)Math.Floor((time + (Math.Min(member.Id, colleague.Id) * 1.7)) / 3) % 2 == (member.Id < colleague.Id ? 0 : 1);
            var (cx, cy) = world.Interpolated(colleague);
            return (talking ? Act.Talk : Act.None, (cx, cy));
        }

        if (DeskAhead(member, world, sim) is { } desk)
        {
            var (x, y) = world.Interpolated(member);
            if (Math.Abs(desk.Item1 - x) + Math.Abs(desk.Item2 - y) < 2)
            {
                // Receptionists answer the phone between patients.
                var h = Habit(member.Id, time, 12);
                return (member.Role == StaffRole.Receptionist && h >= 0.45 && h < 0.8 ? Act.PhoneCall : h < 0.65 ? Act.Typing : Act.None, desk);
            }
        }

        return (Act.None, null);
    }

    private static (Act, (double, double)?) ForVisitor(Agent person, WorldLayer world, HospitalSimulation sim, double time)
    {
        if (sim.TripOf(person) is { } trip && person.Tile == trip.BuyAt)
        {
            var room = sim.FindSpace(trip.RoomId);
            var machine = room is null ? null : Nearest(room, person, "vending_machine", "food_counter", "water_cooler");
            (double, double)? face = machine is null ? null : (machine.CentreX, machine.CentreY);
            switch (trip.Stage)
            {
                case RefreshmentStage.Buying when room?.VendingSpots.Contains(trip.BuyAt) == true:
                    var left = trip.Until is { } until ? (until - sim.Now).TotalSeconds : 0;
                    return (left < 35 ? Act.VendCollect : Act.VendPress, face);
                case RefreshmentStage.Buying:
                    return (Act.Talk, face);
            }
        }

        if (sim.TripOf(person) is { Stage: RefreshmentStage.Eating })
        {
            return (Act.Eat, null);
        }

        if (CheckingIn(person, sim) && CounterAhead(person, world, sim) is { } counter)
        {
            // At the reception counter: tap their details into the check-in screen, then a word with the receptionist.
            return (Habit(person.Id, time, 6) < 0.6 ? Act.CheckIn : Act.Talk, counter);
        }

        if (!world.IsSeated(person) || person is Patient { Stage: PatientStage.InTreatmentSpace })
        {
            return (Act.None, null);
        }

        // Waiting: everyone has their own habits, and changes what they are doing every so often.
        var look = person switch
        {
            Patient p => Looks.Patient(p),
            Outpatient o => Looks.Outpatient(o, sim.FindAppointment(o.AppointmentId)),
            _ => null,
        };
        var child = look is { Scale: < 0.8f };
        var spell = (int)Math.Floor((time + (person.Id * 3.7)) / 14);
        var roll = Roll(person.Id, spell);
        if (child)
        {
            return (roll < 0.55 ? Act.Fidget : roll < 0.75 ? Act.Phone : Act.None, null);
        }

        var elderly = look?.Stoop == true || look?.Stick == true || look?.Frame == true;
        return roll switch
        {
            < 0.4 when !elderly => (Act.Phone, null),
            < 0.62 => (Act.Read, null),
            < 0.72 => (Act.Watch, null),
            _ => (Act.None, null),
        };
    }

    /// <summary>How long one round of a scan takes on screen (seconds): position, step behind the screen, expose, come back.</summary>
    public const double ScanCycle = 12;

    /// <summary>Where a scan is in its round, 0 to <see cref="ScanCycle"/>; each scan runs from its own start.</summary>
    public static double ScanPhase(double time, int taskId) => (time + (taskId * 2.3)) % ScanCycle;

    /// <summary>During this part of the round the X-ray is taken, or the CT table is in the scanner.</summary>
    public static bool Exposing(double phase, bool ct) => ct ? phase is >= 6.6 and < 8.6 : phase is >= 6.8 and < 8.0;

    /// <summary>
    /// A radiographer's round during an X-ray or CT: line the patient up at the table, walk round behind the
    /// lead screen and take the picture at the console, then walk back. Returns null when they aren't scanning
    /// (or the room has no lead screen). Only where they are drawn changes; the simulation keeps them at the table.
    /// </summary>
    public static (Act Act, (double X, double Y) At, (double X, double Y) Facing, bool Walking)? ScanVisit(StaffMember member, WorldLayer world, HospitalSimulation sim, IReadOnlyDictionary<int, ClinicalTask> tasks, double time)
    {
        if (member.IsMoving || member.CurrentTaskId is not { } id || !tasks.TryGetValue(id, out var task) || task.Kind != TaskKind.Imaging
            || task.SpaceId is not { } roomId || sim.FindSpace(roomId) is not { } room
            || sim.FindPatient(task.PatientId) is not { IsMoving: false } patient || patient.Tile != room.PatientSpot
            || room.Furniture.FirstOrDefault(f => f.ItemId == "lead_screen") is not { } screen)
        {
            return null;
        }

        var table = (X: room.PatientSpot.X + 0.5, Y: room.PatientSpot.Y + 0.5);
        var chest = (X: table.X, Y: table.Y - 0.75);
        var start = world.Interpolated(member);

        // Behind the screen: on its far side from the table, kept inside the room.
        var (sx, sy) = (screen.CentreX - table.X, screen.CentreY - table.Y);
        var len = Math.Max(0.01, Math.Sqrt((sx * sx) + (sy * sy)));
        var r = room.Rect;
        var behind = (
            X: Math.Clamp(screen.CentreX + (sx / len * 1.1), r.X + 0.6, r.Right - 0.6),
            Y: Math.Clamp(screen.CentreY + (sy / len * 1.1), r.Y + 0.6, r.Bottom - 0.6));

        var phase = ScanPhase(time, task.Id);
        return phase switch
        {
            < 3.5 => (Act.Bloods, start, Towards(start, chest), false),
            < 5.5 => (Act.None, Lerp(start, behind, (phase - 3.5) / 2), Towards(start, behind), true),
            < 9 => (Act.Typing, behind, Towards(behind, (screen.CentreX, screen.CentreY)), false),
            < 11 => (Act.None, Lerp(behind, start, (phase - 9) / 2), Towards(behind, start), true),
            _ => (Act.Talk, start, Towards(start, chest), false),
        };
    }

    /// <summary>A patient at the front of the A&amp;E reception queue being booked in, or a clinic visitor checking in.</summary>
    private static bool CheckingIn(Agent person, HospitalSimulation sim) => person switch
    {
        Outpatient { Stage: OutpatientStage.CheckingIn } => true,
        Patient p => p.Stage == PatientStage.WaitingToRegister && sim.Tasks.Any(t => t.Started && t.Kind == TaskKind.Register && t.PatientId == p.Id),
        _ => false,
    };

    /// <summary>The reception desk right in front of a visitor, if they are at one.</summary>
    private static (double, double)? CounterAhead(Agent person, WorldLayer world, HospitalSimulation sim)
    {
        var (x, y) = world.Interpolated(person);
        var desk = sim.Spaces
            .SelectMany(s => s.Furniture)
            .Where(f => f.ItemId == "reception_desk")
            .Select(f => (F: f, D: Math.Abs(Math.Clamp(x, f.X, f.X + f.Width) - x) + Math.Abs(Math.Clamp(y, f.Y, f.Y + f.Length) - y)))
            .Where(f => f.D < 2.5)
            .OrderBy(f => f.D)
            .Select(f => f.F)
            .FirstOrDefault();
        if (desk is null)
        {
            return null;
        }

        // Face the nearest point of the desk, not its middle (it may be long).
        return (Math.Clamp(x, desk.X, desk.X + desk.Width), Math.Clamp(y, desk.Y, desk.Y + desk.Length));
    }

    /// <summary>The drip stand beside a patient's bed, if there is one.</summary>
    private static HashSet<string>? _ivLabels;

    /// <summary>Whether this medicine goes in through a drip (an intravenous one).</summary>
    public static bool OnDrip(ClinicalTask task, HospitalSimulation sim)
    {
        _ivLabels ??= sim.Database.Medications.Values
            .Where(m => m.Route == "intravenous")
            .Select(m => $"Give {m.Name.ToLowerInvariant()}")
            .ToHashSet();
        return task.Kind == TaskKind.GiveMedication && _ivLabels.Contains(task.Label);
    }

    /// <summary>The drip stand a nurse putting up a drip turns to, when there is one by the bed.</summary>
    public static (double X, double Y)? DripStandFor(StaffMember member, HospitalSimulation sim, IReadOnlyDictionary<int, ClinicalTask> tasks) =>
        member.CurrentTaskId is { } id && tasks.TryGetValue(id, out var task) && OnDrip(task, sim) && sim.FindPatient(task.PatientId) is { } patient
            ? DripStandBy(patient, sim)
            : null;

    private static (double, double)? DripStandBy(Patient patient, HospitalSimulation sim)
    {
        // Corridors can overlap rooms, so look in every space the bed is in.
        var stand = sim.Spaces.Where(s => s.Rect.Contains(patient.Tile))
            .Select(s => Nearest(s, patient, "drip_stand"))
            .FirstOrDefault(f => f is not null);
        return stand is null || Math.Abs(stand.CentreX - (patient.X + 0.5)) + Math.Abs(stand.CentreY - (patient.Y + 0.5)) > 7
            ? null
            : (stand.CentreX, stand.CentreY);
    }

    /// <summary>
    /// A pharmacist making up a prescription: a word at the counter, round to the shelves to take the box
    /// down, back to the counter to count the tablets out, then hand it over. Null when not dispensing or there
    /// are no shelves in their room. Only where they are drawn changes.
    /// </summary>
    public static (Act Act, (double X, double Y) At, (double X, double Y) Facing, bool Walking)? PharmacyVisit(StaffMember member, WorldLayer world, HospitalSimulation sim, IReadOnlyDictionary<int, ClinicalTask> tasks, double time)
    {
        if (member.IsMoving || member.CurrentTaskId is not { } id || !tasks.TryGetValue(id, out var task) || task.Kind != TaskKind.DispenseMedicines)
        {
            return null;
        }

        var room = sim.Spaces.FirstOrDefault(s => s.Rect.Contains(member.Tile));
        var start = world.Interpolated(member);
        var shelf = room is null ? null : room.Furniture
            .Where(f => f.ItemId is "medicine_shelves" or "drug_fridge")
            .OrderBy(f => Math.Abs(f.CentreX - start.X) + Math.Abs(f.CentreY - start.Y))
            .FirstOrDefault();
        if (room is null || shelf is null)
        {
            return null;
        }

        // Stand just in front of the shelves, on the side facing the counter.
        var (sx, sy) = (start.X - shelf.CentreX, start.Y - shelf.CentreY);
        var len = Math.Max(0.01, Math.Sqrt((sx * sx) + (sy * sy)));
        var half = Math.Abs(sx) > Math.Abs(sy) ? shelf.Width / 2 : shelf.Length / 2;
        var r = room.Rect;
        var front = (
            X: Math.Clamp(shelf.CentreX + (sx / len * (half + 0.55)), r.X + 0.5, r.Right - 0.5),
            Y: Math.Clamp(shelf.CentreY + (sy / len * (half + 0.55)), r.Y + 0.5, r.Bottom - 0.5));
        var desk = DeskAhead(member, world, sim) ?? (start.X, start.Y + 1);
        var shelfAt = (shelf.CentreX, shelf.CentreY);

        var phase = (time + (task.Id * 1.7)) % 13;
        return phase switch
        {
            < 1.5 => (Act.Typing, start, Towards(start, desk), false),
            < 3.5 => (Act.None, Lerp(start, front, (phase - 1.5) / 2), Towards(start, front), true),
            < 6 => (Act.Reach, front, Towards(front, shelfAt), false),
            < 8 => (Act.None, Lerp(front, start, (phase - 6) / 2), Towards(front, start), true),
            < 11.5 => (Act.Count, start, Towards(start, desk), false),
            _ => (Act.Talk, start, Towards(start, desk), false),
        };
    }

    /// <summary>
    /// Physiotherapy on the ward: the patient gets out of bed and, as they get stronger, walks up and down
    /// beside it (with a frame at first) while the physio walks alongside. Returns where the patient is and
    /// where the physio is, or null when no session is under way for this patient. Only the drawing changes.
    /// </summary>
    public static PhysioStep? PhysioWalk(Patient patient, WorldLayer world, HospitalSimulation sim, double time)
    {
        if (patient.IsMoving || !world.InBed(patient)
            || sim.Tasks.FirstOrDefault(t => t.PatientId == patient.Id && t.Kind == TaskKind.Physio && t.Started) is not { } task
            || sim.BedOf(patient) is not { } bed || bed.PatientSpot != patient.Tile
            || sim.Staff.FirstOrDefault(s => s.CurrentTaskId == task.Id) is not { IsMoving: false } physio)
        {
            return null;
        }

        // Out along the side of the bed towards its foot, and back.
        var side = (X: bed.StaffSpot.X - bed.PatientSpot.X, Y: bed.StaffSpot.Y - bed.PatientSpot.Y);
        var sideLen = Math.Max(0.01, Math.Sqrt((side.X * side.X) + (side.Y * side.Y)));
        var outward = (X: side.X / sideLen, Y: side.Y / sideLen);
        var foot = bed.HeadSouth ? -1.0 : 1.0;
        var start = (X: bed.StaffSpot.X + 0.5, Y: bed.StaffSpot.Y + 0.5 - (foot * 0.6));
        var far = (X: start.X, Y: start.Y + (foot * 2.2));
        var sessions = patient.PhysioSessions;
        var frame = sessions is 1 or 2;
        var phase = (time + (task.Id * 2.9)) % 14;
        (double X, double Y) at;
        (double X, double Y) facing;
        bool walking;
        var seated = false;
        if (sessions == 0)
        {
            // First session: sitting on the side of the bed, then standing up with help.
            (at, facing, walking, seated) = (start, outward, false, phase < 6);
        }
        else
        {
            (at, facing, walking) = phase switch
            {
                < 2.5 => (start, Towards(start, far), false),
                < 6.5 => (Lerp(start, far, (phase - 2.5) / 4), Towards(start, far), true),
                < 7.5 => (far, Towards(far, start), false),
                < 11.5 => (Lerp(far, start, (phase - 7.5) / 4), Towards(far, start), true),
                _ => (start, outward, false),
            };
        }

        var helper = (X: at.X + (outward.X * 0.6), Y: at.Y + (outward.Y * 0.6));
        return new PhysioStep(at, facing, walking, seated, frame, helper, physio);
    }

    /// <summary>One moment of a physio session on the ward (see <see cref="PhysioWalk"/>).</summary>
    public sealed record PhysioStep((double X, double Y) At, (double X, double Y) Facing, bool Walking, bool Seated, bool Frame, (double X, double Y) Helper, StaffMember Physio);

    /// <summary>The physio's side of <see cref="PhysioWalk"/>: where to draw them while they walk a patient.</summary>
    public static PhysioStep? PhysioHelping(StaffMember member, WorldLayer world, HospitalSimulation sim, double time) =>
        member.Role == StaffRole.Physiotherapist && !member.IsMoving && member.CurrentTaskId is { } id
        && sim.Tasks.FirstOrDefault(t => t.Id == id) is { Kind: TaskKind.Physio, Started: true } task
        && sim.FindPatient(task.PatientId) is { } patient
            ? PhysioWalk(patient, world, sim, time)
            : null;

    private static (double, double) Towards((double X, double Y) from, (double X, double Y) to)
    {
        var (dx, dy) = (to.X - from.X, to.Y - from.Y);
        var d = Math.Sqrt((dx * dx) + (dy * dy));
        return d < 1e-6 ? (0, 1) : (dx / d, dy / d);
    }

    private static (double, double) Lerp((double X, double Y) a, (double X, double Y) b, double u)
    {
        u = Math.Clamp(u, 0, 1);
        u = u * u * (3 - (2 * u));
        return (a.X + ((b.X - a.X) * u), a.Y + ((b.Y - a.Y) * u));
    }

    /// <summary>Whether a plaster job is on the leg (a backslab or cast below the knee) rather than the arm.</summary>
    public static bool LegCast(ClinicalTask task) =>
        task.Label.Contains("knee", StringComparison.OrdinalIgnoreCase) || task.Label.Contains("ankle", StringComparison.OrdinalIgnoreCase)
        || task.Label.Contains("leg", StringComparison.OrdinalIgnoreCase) || task.Label.Contains("foot", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where a plaster technician stands while putting a cast on: right beside the couch at the limb, on
    /// whichever side they came in. Null unless they are doing one. Only the drawing moves.
    /// </summary>
    public static ((double X, double Y) At, (double X, double Y) Facing)? PlasterSpot(StaffMember member, WorldLayer world, HospitalSimulation sim, IReadOnlyDictionary<int, ClinicalTask> tasks)
    {
        if (member.Role != StaffRole.PlasterTechnician || member.IsMoving || member.CurrentTaskId is not { } id || !tasks.TryGetValue(id, out var task)
            || task.Kind != TaskKind.Procedure || sim.FindPatient(task.PatientId) is not { IsMoving: false } patient)
        {
            return null;
        }

        var (px, py) = (patient.X + 0.5, patient.Y + 0.5);
        var (mx, _) = world.Interpolated(member);
        var side = mx < px ? -1 : 1;
        var limb = (X: px + (side * 0.25), Y: py + (LegCast(task) ? 1.2 : -0.35));
        var at = (px + (side * 1.45), limb.Y);
        return (at, (-side, 0));
    }

    /// <summary>
    /// Where staff stand, and what they do there, for a procedure that has its own choreography: a nurse at the
    /// chest putting ECG leads on and then at the machine while it prints, a midwife beside the mother's knees,
    /// whoever is scanning beside the couch with the probe, the endoscopist at the patient's head. Null otherwise.
    /// Only the drawing moves; they walk the step or two there (see Agents3D).
    /// </summary>
    public static (Act Act, (double X, double Y) At, (double X, double Y) Facing)? CareSpot(StaffMember member, WorldLayer world, HospitalSimulation sim, IReadOnlyDictionary<int, ClinicalTask> tasks, double time)
    {
        if (member.IsMoving || (member.CurrentTaskId ?? (member.Role == StaffRole.ScrubNurse ? member.AssistingTaskId : null)) is not { } id || !tasks.TryGetValue(id, out var task)
            || sim.FindPatient(task.PatientId) is not { IsMoving: false } patient || !world.InBed(patient))
        {
            return null;
        }

        var (care, careTask) = Procedure3D.For(patient, sim);
        var f = Procedure3D.Turn(patient, sim);
        (double, double) Bed(double across, double along) => Procedure3D.BedPoint(patient, sim, across, along);
        if (care is Care.Surgery or Care.Cath && careTask == task && member.Role != StaffRole.Anaesthetist)
        {
            // Round the table: the surgeon at the tummy on one side, the scrub nurse across from them passing
            // instruments (or further down the same side if the far side is blocked).
            var lead = Procedure3D.Doing(task, sim);
            double side = care == Care.Cath ? Procedure3D.Side(patient, lead, sim) : Procedure3D.Side(patient, lead, sim);
            if (member.Role == StaffRole.ScrubNurse)
            {
                var hands = Habit(member.Id, time, 5) < 0.55 ? Act.Operate : Act.Carry;
                return Procedure3D.Free(patient, sim, (float)-side)
                    ? (hands, Bed(-side * 1.35, 0.1), (f * side, 0))
                    : (hands, Bed(side * 1.35, 0.8), (-f * side, -f * 0.2));
            }

            return member.Id == lead?.Id ? (Act.Operate, Bed(side * 1.35, care == Care.Cath ? 0.05 : -0.2), (-f * side, 0)) : null;
        }

        if (care == Care.None && task.Kind is TaskKind.Procedure or TaskKind.GiveMedication or TaskKind.EmergencyTreatment
            && member.Role != StaffRole.PlasterTechnician && member.Role != StaffRole.Anaesthetist && TreatAct(task, member, sim, time) is { } treat)
        {
            // A treatment at the bedside: at the head for a mask or a tube, at the arm for a needle or a bandage.
            double side = Procedure3D.Side(patient, member, sim);
            var label = task.Label.ToLowerInvariant();
            if (label.Contains("dress") || label.Contains("wound"))
            {
                // The dressing goes on the left forearm.
                side = -1;
            }

            switch (treat)
            {
                case Act.HangDrip when label.Contains("transfusion"):
                    // At the pole at the head of the bed, on the patient's right, hanging the bag of blood.
                    return (treat, Bed(1.4, -1.5), (-f, -f * 0.55));
                case Act.Fit:
                    return (treat, Bed(side * 1.3, -1.1), (-f * side, -f * 0.4));
                case Act.Needle or Act.Wrap or Act.Bloods:
                    return (treat, Bed(side * 1.38, -0.35), (-f * side, 0));
                default:
                    return null;
            }
        }

        if (careTask != task || care == Care.None || Procedure3D.OnScanner(care))
        {
            return null;
        }

        switch (care)
        {
            case Care.Ecg:
            {
                double side = Procedure3D.Side(patient, member, sim);
                return Progress(task, sim) > 0.45
                    ? (Habit(member.Id, time, 4) < 0.7 ? Act.Typing : Act.None, Bed(side * 1.5, -0.45), (f * 0.25 * side, f * 1.0))
                    : (Act.Bloods, Bed(side * 1.4, -0.7), (-f * side, 0));
            }

            case Care.Birth:
            {
                double side = Procedure3D.Side(patient, member, sim);
                var born = Procedure3D.Born(task, sim);
                var pushing = Procedure3D.Push(time, task.Id) > 0.08;
                var act = born >= 2 ? Act.Talk : born > 0.9 ? Act.Carry : born > 0 || pushing ? Act.Operate : Act.Talk;
                return (act, Bed(side * 1.4, born > 1.2 ? -0.1 : 0.45), (-f * side, f * (born > 1 ? -0.45 : 0.1)));
            }

            case Care.Echo or Care.Ultrasound:
            {
                // Beside the couch on the machine's side, the probe on the chest or the tummy, eyes on the screen now and then.
                double side = Procedure3D.MachineSide(patient, sim);
                var along = care == Care.Echo ? -0.75 : -0.25;
                return (Act.Scan, Bed(side * 1.35, along), (-f * side, 0));
            }

            case Care.Eeg:
            {
                // Fits the cap at the patient's head, then watches the recording at the machine.
                if (Progress(task, sim) < 0.3 || Procedure3D.Machine(patient, sim) is not { } machine)
                {
                    double side = Procedure3D.MachineSide(patient, sim);
                    return (Act.Bloods, Bed(side * 1.3, -1.35), (-f * side, 0));
                }

                var head = Bed(0, -1.4);
                var (dx, dy) = (head.Item1 - machine.CentreX, head.Item2 - machine.CentreY);
                var d = Math.Max(0.01, Math.Sqrt((dx * dx) + (dy * dy)));
                return (Habit(member.Id, time, 5) < 0.7 ? Act.Typing : Act.None, (machine.CentreX + (dx / d * 1.0), machine.CentreY + (dy / d * 1.0)), (-dx, -dy));
            }

            case Care.Endoscopy:
            {
                double side = Procedure3D.Side(patient, member, sim);
                return (Act.Scope, Bed(side * 1.35, -1.25), (-f * side, f * 0.15));
            }

            case Care.Obs:
            {
                // Beside the patient's shoulder: cuff, then the thermometer in their ear, then the chart.
                double side = Procedure3D.Side(patient, member, sim);
                var act = ObsAct(member.Id, time);
                return (act, Bed(side * 1.4, act == Act.Thermometer ? -1.0 : -0.75), (-f * side, act == Act.Thermometer ? -f * 0.45 : 0));
            }

            case Care.Bloods or Care.FingerPrick or Care.PeakFlow or Care.AsciticTap or Care.LumbarPuncture or Care.JointAspiration or Care.Urine or Care.Swab:
            {
                double side = Procedure3D.Side(patient, member, sim);
                var along = care switch
                {
                    Care.Swab => -1.1,
                    Care.PeakFlow => -0.8,
                    Care.JointAspiration => 0.45,
                    Care.LumbarPuncture => -0.35,
                    Care.Urine => 0.1,
                    _ => -0.3,
                };
                var out_ = care == Care.Urine ? 1.6 : 1.38;
                return (TestAct(task, member.Id, time), Bed(side * out_, along), (-f * side, care == Care.Swab ? -f * 0.4 : 0));
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// An anaesthetist bagging the patient stands at the head of the table, by the anaesthetic machine, facing
    /// down the table. Null when there is no machine in the room.
    /// </summary>
    public static ((double X, double Y) At, (double X, double Y) Facing)? AnaesthetistSpot(StaffMember member, HospitalSimulation sim, IReadOnlyDictionary<int, ClinicalTask> tasks)
    {
        if (member.Role != StaffRole.Anaesthetist || member.IsMoving || (member.CurrentTaskId ?? member.AssistingTaskId) is not { } id || !tasks.TryGetValue(id, out var task)
            || sim.FindPatient(task.PatientId) is not { IsMoving: false } patient
            || sim.Spaces.FirstOrDefault(r => r.Rect.Contains(patient.Tile)) is not { } room
            || Nearest(room, patient, "anaesthetic_machine") is not { } machine)
        {
            return null;
        }

        var (px, py) = (patient.X + 0.5, patient.Y + 0.5);
        var (dx, dy) = (px - machine.CentreX, py - machine.CentreY);
        var d = Math.Max(0.01, Math.Sqrt((dx * dx) + (dy * dy)));
        var at = (machine.CentreX + (dx / d * 0.8), machine.CentreY + (dy / d * 0.8));
        return (at, (dx, dy));
    }

    /// <summary>
    /// Putting a patient to sleep on their trolley in the anaesthetic room: the anaesthetist stands at their
    /// shoulder, on whichever side of the trolley has room, rather than hidden behind its raised back.
    /// </summary>
    public static ((double X, double Y) At, (double X, double Y) Facing)? InductionSpot(StaffMember member, HospitalSimulation sim)
    {
        if (member is not { Role: StaffRole.Anaesthetist, IsMoving: false } || (member.CurrentTaskId ?? member.AssistingTaskId) is not { } id
            || sim.Tasks.FirstOrDefault(t => t.Id == id) is not { Kind: TaskKind.Surgery, Phase: SurgeryPhase.Anaesthetic } task
            || sim.FindPatient(task.PatientId) is not { IsMoving: false } patient || Procedure3D.GoingUnder(patient, sim) is null
            || sim.Spaces.FirstOrDefault(r => r.Rect.Contains(patient.Tile)) is not { } room)
        {
            return null;
        }

        var (px, py) = (patient.X + 0.5, patient.Y + 0.5);
        var (fx, fy) = (patient.Facing.X, patient.Facing.Y);
        var len = Math.Sqrt((fx * fx) + (fy * fy));
        (fx, fy) = len < 1e-6 ? (0, 1) : (fx / len, fy / len);
        (double X, double Y) Spot(double s) => (px + (-fy * s * 1.2) - (fx * 0.45), py + (fx * s * 1.2) - (fy * 0.45));
        bool Free((double X, double Y) at) => at.X > room.Rect.X + 0.4 && at.X < room.Rect.Right - 0.4 && at.Y > room.Rect.Y + 0.4 && at.Y < room.Rect.Bottom - 0.4
            && !room.Furniture.Any(f => at.X > f.X - 0.2 && at.X < f.X + f.Width + 0.2 && at.Y > f.Y - 0.2 && at.Y < f.Y + f.Length + 0.2);
        var side = Free(Spot(1)) || !Free(Spot(-1)) ? 1.0 : -1.0;
        var at = Spot(side);
        return (at, (px - (fx * 0.45) - at.X, py - (fy * 0.45) - at.Y));
    }

    private static Dictionary<string, string>? _routes;

    /// <summary>
    /// What the hands do for a treatment, from its name (or the way the medicine goes in): fitting a mask,
    /// an injection, bandaging, hanging a bag, or just talking it through. Null when nothing special fits.
    /// </summary>
    public static Act? TreatAct(ClinicalTask task, StaffMember member, HospitalSimulation sim, double time)
    {
        var what = task.Label.ToLowerInvariant();
        if (task.Kind == TaskKind.GiveMedication)
        {
            _routes ??= sim.Database.Medications.Values.GroupBy(m => $"give {m.Name.ToLowerInvariant()}").ToDictionary(g => g.Key, g => g.First().Route);
            return _routes.TryGetValue(what, out var route) ? route switch
            {
                "nebulised" or "inhaled" => Act.Fit,
                "subcutaneous" or "intramuscular" or "local injection" => Act.Needle,
                _ => null,
            } : null;
        }

        bool Has(params string[] words) => words.Any(what.Contains);
        return Has("oxygen", "nebulis", "nasogastric", "nasal") ? Act.Fit
            : Has("transfusion", "iv ", "fluids") ? Act.HangDrip
            : Has("im adrenaline", "nerve block", "drain", "wound") ? Act.Needle
            : Has("backslab", "plaster", "dress", "ankle support", "tape") ? Act.Wrap
            : Has("sling", "pads", "redness") ? Act.Bloods
            : Has("observations", "blood pressure", "monitoring") ? ObsAct(member.Id, time)
            : Has("swallow", "feeding", "glucose") ? Act.Medicine
            : Has("advice", "assessment", "referral", "check ", "score", "stop medicines", "look for", "reassurance", "care (") ? (Habit(member.Id, time, 6) < 0.65 ? Act.Talk : Act.Write)
            : null;
    }

    /// <summary>A round of observations: blood pressure, then temperature, then writing it all down.</summary>
    public static Act ObsAct(int who, double time)
    {
        var phase = (time + (who * 1.3)) % 13;
        return phase < 5.5 ? Act.Observations : phase < 8.5 ? Act.Thermometer : Act.Write;
    }

    /// <summary>What the hands do for taking blood or a bedside test, from which test it is.</summary>
    public static Act TestAct(ClinicalTask task, int who, double time) => task.Kind == TaskKind.TakeBloods ? Act.Needle : task.Detail switch
    {
        "urine_dipstick" or "pregnancy_test" => Act.Dip,
        "capillary_glucose" or "blood_ketones" => Act.Prick,
        "viral_swab" => Act.Swab,
        "venous_blood_gas" or "lumbar_puncture" or "ascitic_tap" or "joint_aspiration" => Act.Needle,
        "peak_flow" => Habit(who, time, 6) < 0.7 ? Act.Talk : Act.Write,
        _ => Act.Bloods,
    };

    /// <summary>How far through their job (0 to 1) whoever is doing <paramref name="task"/> is.</summary>
    public static double Progress(ClinicalTask task, HospitalSimulation sim)
    {
        var member = sim.Staff.FirstOrDefault(s => s.CurrentTaskId == task.Id);
        if (member is null || task.Minutes <= 0)
        {
            return task.Started ? 1 : 0;
        }

        var total = task.Minutes * 60 * member.DurationFactor;
        return Math.Clamp(1 - (member.WorkRemainingSeconds / Math.Max(1, total)), 0, 1);
    }

    /// <summary>The middle of where a patient is: their bed, the table they are on, or where they stand.</summary>
    private static (double, double) Centre(Patient patient, WorldLayer world)
    {
        var (x, y) = world.Interpolated(patient);
        if (world.InBed(patient))
        {
            // Lying down: turn to their chest, a little towards the head of the bed from its middle.
            var headSouth = world.Simulation.BedOf(patient) is { } bed && bed.PatientSpot == patient.Tile && bed.HeadSouth;
            return (x, y + (headSouth ? 0.75 : -0.75));
        }

        return (x, y);
    }

    /// <summary>The desk or console in front of a member of staff: the nearest desk-like piece of furniture in their room.</summary>
    private static (double, double)? DeskAhead(StaffMember member, WorldLayer world, HospitalSimulation sim)
    {
        var room = sim.Spaces.FirstOrDefault(s => s.Rect.Contains(member.Tile));
        var desk = room is null ? null : Nearest(room, member, "lead_screen", "reception_desk", "triage_desk", "pharmacy_counter", "office_desk", "station_counter");
        if (desk is null)
        {
            return null;
        }

        var (x, y) = world.Interpolated(member);
        var (dx, dy) = (desk.CentreX - x, desk.CentreY - y);
        return Math.Abs(dx) + Math.Abs(dy) > 4 ? null : (desk.CentreX, desk.CentreY);
    }

    private static PlacedFurniture? Nearest(CareSpace room, Agent person, params string[] ids) =>
        room.Furniture
            .Where(f => ids.Contains(f.ItemId))
            .OrderBy(f => Math.Abs(f.CentreX - (person.X + 0.5)) + Math.Abs(f.CentreY - (person.Y + 0.5)))
            .FirstOrDefault();

    /// <summary>A steady 0-1 number that changes once every <paramref name="period"/> seconds, different for everyone.</summary>
    private static double Habit(int id, double time, double period) => Roll(id, (int)Math.Floor((time + (id * 1.3)) / period));

    /// <summary>A steady 0-1 number for <paramref name="id"/> in time slot <paramref name="spell"/>.</summary>
    public static double Chance(int id, int spell) => Roll(id, spell);

    private static double Roll(int id, int spell)
    {
        unchecked
        {
            var h = (uint)((id * 73856093) ^ (spell * 19349663));
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h % 10000) / 10000.0;
        }
    }
}
