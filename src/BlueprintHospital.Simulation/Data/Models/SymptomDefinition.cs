namespace BlueprintHospital.Simulation.Data.Models;

/// <summary>Body system a symptom mainly relates to. Used for grouping in the UI and for patient generation.</summary>
public enum BodySystem
{
    General,
    Cardiovascular,
    Respiratory,
    Gastrointestinal,
    Neurological,
    Gynaecological,
    Musculoskeletal,
    Skin,
}

/// <summary>A presenting symptom. Symptoms are independent records that conditions refer to by id. Loaded from Data/Symptoms.</summary>
public sealed class SymptomDefinition : IDataRecord
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>How the patient might describe it at reception, in plain English.</summary>
    public string PatientDescription { get; init; } = string.Empty;

    public BodySystem BodySystem { get; init; } = BodySystem.General;

    /// <summary>True if this symptom on its own should make triage nurses consider at least Amber.</summary>
    public bool IsRedFlag { get; init; }

    /// <summary>Tests the triage nurse starts straight away under protocol, e.g. an ECG for chest pain.</summary>
    public IReadOnlyList<string> TriageInvestigations { get; init; } = Array.Empty<string>();

    public void Validate(ValidationContext context)
    {
        context.RequireId("id", Id);
        context.RequireText("name", Name);
        context.RequireText("patient_description", PatientDescription);
        context.RequireIds("triage_investigations", TriageInvestigations);
    }
}
