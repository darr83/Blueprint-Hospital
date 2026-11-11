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

        if (member.Activity == StaffActivity.Idle)
        {
            return Idle(member, world, sim, time);
        }

        var taskId = member.CurrentTaskId ?? member.AssistingTaskId;
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
            TaskKind.Observations or TaskKind.Triage or TaskKind.AmbulanceHandover => Act.Observations,
            TaskKind.TakeBloods or TaskKind.BedsideTest => Act.Bloods,
            TaskKind.GiveMedication or TaskKind.EmergencyTreatment => Act.Medicine,
            TaskKind.Register or TaskKind.Imaging or TaskKind.DispenseMedicines => Act.Typing,
            TaskKind.Surgery => Act.Operate,
            TaskKind.Procedure => Act.Operate,
            TaskKind.Physio => Act.Exercise,
            TaskKind.Discharge or TaskKind.WardDischarge => Act.Talk,
            TaskKind.StayWithPatient => Act.Talk,
            _ => Act.None,
        };

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
            return (Habit(member.Id, time, 15) < 0.5 ? Act.Typing : Act.None, null);
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
                return (Habit(member.Id, time, 12) < 0.65 ? Act.Typing : Act.None, desk);
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

        static (double, double) Towards((double X, double Y) from, (double X, double Y) to)
        {
            var (dx, dy) = (to.X - from.X, to.Y - from.Y);
            var d = Math.Sqrt((dx * dx) + (dy * dy));
            return d < 1e-6 ? (0, 1) : (dx / d, dy / d);
        }

        static (double, double) Lerp((double X, double Y) a, (double X, double Y) b, double u)
        {
            u = u * u * (3 - (2 * u));
            return (a.X + ((b.X - a.X) * u), a.Y + ((b.Y - a.Y) * u));
        }

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
