using System;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>What is being done to a patient lying down, when it changes how they lie and what is attached to them.</summary>
public enum Care
{
    None,

    /// <summary>A 12-lead ECG: gown open, ten electrodes on the chest and limbs, wires to the machine on its cart.</summary>
    Ecg,

    /// <summary>Giving birth: sat well up, knees raised and apart in the leg supports, pushing with each contraction.</summary>
    Birth,

    /// <summary>Lying on the X-ray table for a chest film.</summary>
    XrayChest,

    /// <summary>X-ray of the tummy.</summary>
    XrayAbdomen,

    /// <summary>X-ray of an arm: the arm out to the side on the detector plate.</summary>
    XrayArm,

    /// <summary>X-ray of a leg: the detector plate under the lower leg.</summary>
    XrayLeg,

    /// <summary>CT of the head: arms by their sides, head in the cradle.</summary>
    CtHead,

    /// <summary>CT of the chest or tummy: arms up above the head.</summary>
    CtBody,

    /// <summary>An echocardiogram: gown open, gel and the probe on the chest.</summary>
    Echo,

    /// <summary>An ultrasound scan of the tummy: gown lifted, gel and the probe on the tummy.</summary>
    Ultrasound,

    /// <summary>An EEG: a cap of electrodes on the head, its bundle of wires going to the recorder.</summary>
    Eeg,

    /// <summary>An endoscopy: turned on their left side, a mouth guard in, the scope in the doctor's hands.</summary>
    Endoscopy,
}

/// <summary>Works out, from the simulation alone, which procedure a lying patient is in the middle of. Changes nothing.</summary>
public static class Procedure3D
{
    /// <summary>The care under way for <paramref name="patient"/> and the task it comes from.</summary>
    public static (Care Care, ClinicalTask? Task) For(Patient patient, HospitalSimulation sim)
    {
        foreach (var task in sim.Tasks)
        {
            if (task.PatientId != patient.Id)
            {
                continue;
            }

            switch (task.Kind)
            {
                case TaskKind.BedsideTest when task.Started && task.Detail == "ecg":
                    return (Care.Ecg, task);
                case TaskKind.Surgery when task.Started && task.SpaceId is { } id && sim.FindSpace(id) is { } place && place.PatientSpot == patient.Tile:
                    if (place.Kind == CareSpaceKind.DeliveryRoom)
                    {
                        return (Care.Birth, task);
                    }

                    if (place.Kind == CareSpaceKind.Endoscopy)
                    {
                        return (Care.Endoscopy, task);
                    }

                    break;
                case TaskKind.Imaging when task.SpaceId is { } id && sim.FindSpace(id) is { } room && room.PatientSpot == patient.Tile:
                    var what = (task.Detail ?? task.Label).ToLowerInvariant();
                    switch (room.Kind)
                    {
                        case CareSpaceKind.CT:
                            return (what.Contains("head") ? Care.CtHead : Care.CtBody, task);
                        case CareSpaceKind.XRay:
                            return (what.Contains("chest") ? Care.XrayChest
                                : what.Contains("abdom") ? Care.XrayAbdomen
                                : ArmInjury(patient.Condition.Id) ? Care.XrayArm : Care.XrayLeg, task);
                        case CareSpaceKind.Echo when task.Started:
                            return (Care.Echo, task);
                        case CareSpaceKind.Ultrasound when task.Started:
                            return (Care.Ultrasound, task);
                        case CareSpaceKind.Eeg when task.Started:
                            return (Care.Eeg, task);
                        case CareSpaceKind.Endoscopy when task.Started:
                            return (Care.Endoscopy, task);
                    }

                    break;
            }
        }

        return (Care.None, null);
    }

    private static bool ArmInjury(string condition) =>
        condition.Contains("wrist") || condition.Contains("shoulder") || condition.Contains("elbow") || condition.Contains("hand") || condition.Contains("finger");

    /// <summary>Whether <paramref name="care"/> is an X-ray or a CT scan (no covers, lined up with lights).</summary>
    public static bool OnScanner(Care care) => care is Care.XrayChest or Care.XrayAbdomen or Care.XrayArm or Care.XrayLeg or Care.CtHead or Care.CtBody;

    /// <summary>Where on the table (across, along; the table's own frame, +z to the feet) the picture is taken.</summary>
    public static Vector2 Target(Care care) => care switch
    {
        Care.XrayAbdomen => new Vector2(0, -0.1f),
        Care.XrayArm => new Vector2(0.59f, -0.37f),
        Care.XrayLeg => new Vector2(0.17f, 1.0f),
        Care.CtHead => new Vector2(0, -1.45f),
        Care.CtBody => new Vector2(0, -0.6f),
        _ => new Vector2(0, -0.75f),
    };

    /// <summary>+1 unless the patient's bed has its head to the south, when the bed's own frame is turned right round.</summary>
    public static double Turn(Patient patient, HospitalSimulation sim) =>
        sim.BedOf(patient) is { } bed && bed.PatientSpot == patient.Tile && bed.HeadSouth ? -1.0 : 1.0;

    /// <summary>A point in the frame of the patient's bed (across, along towards the feet) as a place on the grid.</summary>
    public static (double X, double Y) BedPoint(Patient patient, HospitalSimulation sim, double across, double along)
    {
        var f = Turn(patient, sim);
        return (patient.X + 0.5 + (f * across), patient.Y + 0.5 + (f * along));
    }

    /// <summary>
    /// Which side of the bed (-1 or +1 across it) the work is done from: the side whoever is doing it came to,
    /// unless a wall, another bed or furniture is in the way there and the other side is clear.
    /// </summary>
    public static float Side(Patient patient, StaffMember? member, HospitalSimulation sim)
    {
        var f = Turn(patient, sim);
        var preferred = member is not null && f * (member.X - patient.X) < 0 ? -1f : 1f;
        return Clear(patient, sim, preferred) || !Clear(patient, sim, -preferred) ? preferred : -preferred;
    }

    /// <summary>Whether there is room beside the bed on that side for someone to stand and a machine to be wheeled in.</summary>
    private static bool Clear(Patient patient, HospitalSimulation sim, float side)
    {
        var rooms = sim.Spaces.Where(s => s.Rect.Contains(patient.Tile)).ToList();
        foreach (var (across, along) in new[] { (1.5, -0.6), (1.7, 0.55) })
        {
            var (x, y) = BedPoint(patient, sim, side * across, along);
            if (!rooms.Any(r => x > r.Rect.X + 0.3 && x < r.Rect.Right - 0.3 && y > r.Rect.Y + 0.3 && y < r.Rect.Bottom - 0.3))
            {
                return false;
            }

            foreach (var item in rooms.SelectMany(r => r.Furniture))
            {
                var mine = patient.X + 0.5 > item.X && patient.X + 0.5 < item.X + item.Width && patient.Y + 0.5 > item.Y && patient.Y + 0.5 < item.Y + item.Length;
                if (!mine && x > item.X - 0.25 && x < item.X + item.Width + 0.25 && y > item.Y - 0.25 && y < item.Y + item.Length + 0.25)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>The scanning machine in the patient's room (ultrasound machine, EEG recorder, endoscopy stack), if it has one.</summary>
    public static PlacedFurniture? Machine(Patient patient, HospitalSimulation sim) =>
        sim.Spaces.Where(s => s.Rect.Contains(patient.Tile))
            .SelectMany(s => s.Furniture)
            .Where(f => f.ItemId is "ultrasound_machine" or "eeg_machine" or "endoscopy_stack")
            .OrderBy(f => Math.Abs(f.CentreX - (patient.X + 0.5)) + Math.Abs(f.CentreY - (patient.Y + 0.5)))
            .FirstOrDefault();

    /// <summary>The side of the couch the scanning machine stands on; +1 when there is none.</summary>
    public static float MachineSide(Patient patient, HospitalSimulation sim) =>
        Machine(patient, sim) is { } machine && Turn(patient, sim) * (machine.CentreX - (patient.X + 0.5)) < 0 ? -1f : 1f;

    /// <summary>The length of one contraction and the rest after it, in seconds on screen.</summary>
    public const double ContractionCycle = 7;

    /// <summary>How hard she is pushing right now, 0 (resting) to 1.</summary>
    public static float Push(double time, int taskId)
    {
        var phase = (time + (taskId * 1.9)) % ContractionCycle;
        return phase < 3.6 ? Mathf.Sin((float)(phase / 3.6 * Math.PI)) : 0f;
    }

    /// <summary>
    /// The last minutes of a birth: 0 until the baby is born, rising to 1 as the midwife lifts the baby clear,
    /// and on to 2 as she lays the baby on its mother's chest. At 2 the mother is cuddling her baby.
    /// </summary>
    public static float Born(ClinicalTask task, HospitalSimulation sim)
    {
        var left = Remaining(task, sim);
        return (float)Math.Clamp((240 - left) / 45, 0, 2);
    }

    /// <summary>How many seconds of the hospital's clock <paramref name="task"/> still has to run (a lot, if nobody has started it).</summary>
    public static double Remaining(ClinicalTask task, HospitalSimulation sim) =>
        sim.Staff.FirstOrDefault(s => s.CurrentTaskId == task.Id) is { } member && task.Started ? member.WorkRemainingSeconds : double.MaxValue;

    /// <summary>Whoever is doing <paramref name="task"/>, if they have got there.</summary>
    public static StaffMember? Doing(ClinicalTask task, HospitalSimulation sim) =>
        sim.Staff.FirstOrDefault(s => s.CurrentTaskId == task.Id && !s.IsMoving);
}
