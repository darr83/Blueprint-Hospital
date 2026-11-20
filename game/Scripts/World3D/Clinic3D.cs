using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// What a clinic appointment looks like at this moment. Where the patient is (on their chair, or lying on the
/// couch), what is being done to them, and where the clinician stands and what their hands do.
/// </summary>
/// <param name="OnCouch">The patient is lying down (on the examination couch, or the scan room's bed).</param>
/// <param name="CouchAt">The middle of what they lie on, on the grid.</param>
/// <param name="CouchFeet">The way their feet point.</param>
/// <param name="Height">How high the top of it is.</param>
/// <param name="Care">What changes how they lie (a scan of the tummy).</param>
/// <param name="Side">Which side of the couch (in its own frame) the clinician works from.</param>
/// <param name="StaffAct">What the clinician's hands do.</param>
/// <param name="StaffAt">Where the clinician stands; null when they stay in their chair at the desk.</param>
/// <param name="StaffFacing">Which way the clinician faces when standing.</param>
/// <param name="PatientAct">What the patient does while sitting.</param>
public sealed record ClinicScene(bool OnCouch, (double X, double Y) CouchAt, (double X, double Y) CouchFeet, float Height, Care Care, float Side,
    Act StaffAct, (double X, double Y)? StaffAt, (double X, double Y) StaffFacing, Act PatientAct);

/// <summary>
/// Works out, from the simulation alone, how a clinic appointment plays out on screen: a few minutes talking at
/// the desk, then the examination or the procedure, then a few words before they go. Changes nothing.
/// </summary>
public static class Clinic3D
{
    private static readonly Dictionary<int, (DateTime Start, DateTime Until)> Started = new();
    private static float? _bedTop;

    /// <summary>The appointment <paramref name="patient"/> is in the middle of, as it should be drawn.</summary>
    public static ClinicScene? For(Outpatient patient, HospitalSimulation sim, double time) =>
        patient.Stage == OutpatientStage.BeingSeen && !patient.IsMoving && sim.Consults.FirstOrDefault(c => c.OutpatientId == patient.Id) is { } consult
            ? Scene(consult, patient, sim, time)
            : null;

    /// <summary>The appointment <paramref name="member"/> is running, as it should be drawn.</summary>
    public static ClinicScene? For(StaffMember member, HospitalSimulation sim, double time) =>
        !member.IsMoving && member.Activity == StaffActivity.Working && sim.Consults.FirstOrDefault(c => c.StaffId == member.Id) is { } consult
        && sim.FindOutpatient(consult.OutpatientId) is { Stage: OutpatientStage.BeingSeen, IsMoving: false } patient
            ? Scene(consult, patient, sim, time)
            : null;

    private static ClinicScene? Scene(ClinicConsult consult, Outpatient patient, HospitalSimulation sim, double time)
    {
        if (sim.FindSpace(consult.ClinicId) is not { } room || sim.FindAppointment(patient.AppointmentId) is not { } appointment || patient.BusyUntil is not { } until)
        {
            return null;
        }

        if (!Started.TryGetValue(patient.Id, out var seen) || seen.Until != until || seen.Start > sim.Now)
        {
            if (Started.Count > 300)
            {
                Started.Clear();
            }

            seen = (sim.Now, until);
            Started[patient.Id] = seen;
        }

        var total = Math.Max(1, (until - seen.Start).TotalMinutes);
        var elapsed = (sim.Now - seen.Start).TotalMinutes;
        var left = (until - sim.Now).TotalMinutes;
        var why = appointment.Reason.ToLowerInvariant();
        var (px, py) = (patient.X + 0.5, patient.Y + 0.5);

        // Take turns talking: one speaks while the other listens (the clinician types a note now and then).
        var turn = (int)Math.Floor((time + (patient.Id * 1.7)) / 3.5) % 2 == 0;
        var note = (int)Math.Floor((time + (patient.Id * 0.9)) / 9) % 3 == 2;
        var talk = new ClinicScene(false, (px, py), (0, 1), 0, Care.None, 1, note ? Act.Typing : turn ? Act.Talk : Act.None, null, (0, 1), turn || note ? Act.None : Act.Talk);

        // The scan room: on the bed for the whole visit, the probe on the tummy.
        if (room.Furniture.FirstOrDefault(f => f.ItemId == "ultrasound_machine") is { } machine && room.PatientSpot == patient.Tile)
        {
            _bedTop ??= Carriers3D.Bed(new MeshBatch(), made: false, back: null);
            var side = machine.CentreX < px ? -1f : 1f;
            return new ClinicScene(true, (px, py), (0, 1), _bedTop.Value, Care.Ultrasound, side, Act.Scan, (px + (side * 1.35), py - 0.25), (-side, 0), Act.None);
        }

        var talking = elapsed < Math.Min(4, total * 0.25) || left < Math.Min(3, total * 0.2);
        if (talking)
        {
            return talk;
        }

        bool Has(params string[] words) => words.Any(why.Contains);
        var template = room.TemplateId;
        var seatedCheck = template == "fracture_clinic" || Has("blood pressure", "booking", "blood tests", "diabetes review", "botox", "wound check", "finger");
        if (seatedCheck || room.Furniture.FirstOrDefault(f => f.ItemId == "examination_couch") is not { } couch)
        {
            // Looked at where they sit: the clinician comes round the desk and stands beside them.
            var (sx, sy) = (room.StaffSpot.X + 0.5, room.StaffSpot.Y + 0.5);
            var (dx, dy) = (sx - px, sy - py);
            var d = Math.Max(0.01, Math.Sqrt((dx * dx) + (dy * dy)));
            var hands = Has("cast", "wound") ? Act.Wrap
                : Has("injection", "botox", "blood tests") ? Act.Needle
                : Has("blood pressure", "booking", "diabetes") ? Activity3D.ObsAct(patient.Id, time)
                : Act.Bloods;
            return talk with { StaffAct = hands, StaffAt = (px + (dx / d * 1.2), py + (dy / d * 1.2)), StaffFacing = (-dx, -dy), PatientAct = Act.None };
        }

        // On the couch: head to the north or the west end, the clinician on whichever side has room.
        var alongY = couch.Length >= couch.Width;
        var feet = alongY ? (X: 0.0, Y: 1.0) : (X: 1.0, Y: 0.0);
        var across = alongY ? (X: 1.0, Y: 0.0) : (X: 0.0, Y: -1.0);
        var centre = (X: couch.CentreX, Y: couch.CentreY);
        var gynae = Has("smear", "colposcopy", "coil", "pessary", "polyp", "biopsy");
        var scan = Has("scan", "fibroscan") || template == "antenatal_clinic";
        var care = scan ? Care.Ultrasound : Care.None;
        var act = scan ? Act.Scan
            : Has("injection", "biopsy") ? Act.Needle
            : appointment.IsProcedure ? Act.Operate
            : Activity3D.Chance(patient.Id, (int)Math.Floor(time / 7)) < 0.7 ? Act.Examine : Act.Talk;
        var along = gynae ? 0.0 : scan ? -0.1 : Has("toenail", "ankle", "hip", "tibia") ? 0.5 : -0.35;
        float Free(float s)
        {
            var (x, y) = (centre.X + (across.X * s * 1.3) + (feet.X * along), centre.Y + (across.Y * s * 1.3) + (feet.Y * along));
            var inside = x > room.Rect.X + 0.3 && x < room.Rect.Right - 0.3 && y > room.Rect.Y + 0.3 && y < room.Rect.Bottom - 0.3;
            return inside && !room.Furniture.Any(f => f != couch && x > f.X - 0.2 && x < f.X + f.Width + 0.2 && y > f.Y - 0.2 && y < f.Y + f.Length + 0.2) ? 1f : 0f;
        }

        var where = Free(1) >= Free(-1) ? 1f : -1f;
        var half = (alongY ? couch.Length : couch.Width) / 2;
        var at = gynae && appointment.IsProcedure
            ? (centre.X + (feet.X * (half + 0.55)), centre.Y + (feet.Y * (half + 0.55)))
            : (centre.X + (across.X * where * 1.3) + (feet.X * along), centre.Y + (across.Y * where * 1.3) + (feet.Y * along));
        var facing = gynae && appointment.IsProcedure ? (-feet.X, -feet.Y) : (-across.X * where, -across.Y * where);
        return new ClinicScene(true, centre, feet, Kit3D.H(21f), care, where, act, at, facing, Act.None);
    }
}
