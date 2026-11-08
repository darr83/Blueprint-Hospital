using System;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>Shared wording and colours for clinical information in the interface.</summary>
public static class ClinicalText
{
    public const string RedHex = "#E53935";
    public const string AmberHex = "#F9A825";
    public const string GreenHex = "#43A047";

    public static Color TriageColour(TriageCategory? category) => category switch
    {
        TriageCategory.Red => new Color(RedHex),
        TriageCategory.Amber => new Color(AmberHex),
        TriageCategory.Green => new Color(GreenHex),
        _ => new Color("#AEB7BD"),
    };

    public static string TriageHex(TriageCategory? category) => category switch
    {
        TriageCategory.Red => RedHex,
        TriageCategory.Amber => AmberHex,
        TriageCategory.Green => GreenHex,
        _ => "#AEB7BD",
    };

    public static string TriageName(TriageCategory? category) => category?.ToString().ToUpperInvariant() ?? "NOT TRIAGED";

    public static string News2Hex(int total) => total >= 7 ? RedHex : total >= 5 ? "#E07B00" : total >= 1 ? "#B58A00" : GreenHex;

    /// <summary>Where the patient is and what they are waiting for, in plain words.</summary>
    public static string Whereabouts(HospitalSimulation sim, Patient patient)
    {
        var room = patient.CareSpaceId is { } id ? sim.FindSpace(id)?.Name : null;
        return patient.Stage switch
        {
            PatientStage.WalkingIn => "Walking in to A&E",
            PatientStage.WaitingToRegister => "Queueing at reception",
            PatientStage.WaitingForTriage => "Waiting room: waiting for triage",
            PatientStage.InAmbulanceQueue => patient.TriagedAt is null ? "On an ambulance trolley: waiting for handover" : "On an ambulance trolley: waiting for a bed",
            PatientStage.WaitingForSpace => "Waiting room: waiting for a cubicle",
            PatientStage.InTreatmentSpace => room ?? "In a treatment space",
            PatientStage.AwaitingTransfer => $"{room ?? "A&E"}: waiting for transfer",
            PatientStage.AwaitingBed when patient.ReservedWardId is { } ward => $"{room ?? "A&E"}: bed found on {sim.FindSpace(ward)?.Name ?? "a ward"}, waiting to go up",
            PatientStage.AwaitingBed => $"{room ?? "A&E"}: waiting for a ward bed",
            PatientStage.OnWard => $"{room ?? "Ward"}, bed {(patient.BedIndex ?? 0) + 1}",
            PatientStage.Leaving => "Leaving A&E",
            PatientStage.Gone => patient.Outcome switch
            {
                PatientOutcome.Discharged => "Discharged",
                PatientOutcome.Transferred => "Transferred",
                PatientOutcome.Admitted => "Admitted",
                PatientOutcome.LeftWithoutBeingSeen => "Left without being seen",
                PatientOutcome.Died => "Died",
                _ => "Left A&E",
            },
            _ => patient.Stage.ToString(),
        };
    }

    public static string Duration(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes:00} m" : $"{Math.Max(0, (int)span.TotalMinutes)} m";

    /// <summary>Escapes text for a RichTextLabel with BBCode enabled.</summary>
    public static string Escape(string text) => text.Replace("[", "[lb]");
}
