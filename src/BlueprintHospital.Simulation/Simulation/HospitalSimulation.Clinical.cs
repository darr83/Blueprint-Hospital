using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>What happens when each kind of clinical task is finished.</summary>
public sealed partial class HospitalSimulation
{
    private readonly List<(DateTime Due, Patient Patient)> _pendingOutcomeReviews = new();

    private void CompleteTask(StaffMember member, ClinicalTask task, Patient patient)
    {
        // Any doctor's work at the bedside of a red-monitor patient counts as their review.
        if (task.Role == StaffRole.Doctor && task.Kind != TaskKind.RedReview && patient.MonitorIsRed && !patient.RedReviewed)
        {
            MarkRedReviewed(member, patient);
        }

        switch (task.Kind)
        {
            case TaskKind.Register:
                CompleteRegistration(member, patient);
                break;
            case TaskKind.Triage:
                CompleteTriage(member, patient, handover: false);
                break;
            case TaskKind.AmbulanceHandover:
                if (!TryHandOverToCorridor(member, patient))
                {
                    CompleteTriage(member, patient, handover: true);
                }
                break;
            case TaskKind.Observations:
                TakeObservations(patient);
                break;
            case TaskKind.DoctorAssessment:
                CompleteAssessment(member, patient);
                break;
            case TaskKind.TakeBloods:
                CompleteBloods(member, patient);
                break;
            case TaskKind.BedsideTest:
                CompleteBedsideTest(member, patient, task.Detail!);
                break;
            case TaskKind.DoctorReview:
                CompleteReview(member, patient);
                break;
            case TaskKind.GiveMedication:
                CompleteTreatmentStep(member, patient, int.Parse(task.Detail!, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case TaskKind.Procedure:
                CompleteTreatmentStep(member, patient, int.Parse(task.Detail!, System.Globalization.CultureInfo.InvariantCulture));
                if (task.SpaceId is { } procedureRoom && FindSpace(procedureRoom) is { IsServiceRoom: true } specialistRoom)
                {
                    patient.Log(Now, $"{task.Label} done by {member.Name} in {specialistRoom.Name}.");
                    ReturnFromServiceRoom(patient);
                }

                break;
            case TaskKind.Physio:
                CompletePhysio(member, patient);
                break;
            case TaskKind.OutreachReview:
                CompleteOutreachReview(member, patient);
                break;
            case TaskKind.RedReview:
                CompleteRedReview(member, patient);
                break;
            case TaskKind.Discharge:
                CompleteDischarge(member, patient);
                break;
            case TaskKind.EscortToTransfer:
                CompleteTransfer(member, patient);
                break;
            case TaskKind.EscortToWard:
                CompleteEscortToWard(member, patient);
                break;
            case TaskKind.WardClerking:
                CompleteClerking(member, patient);
                break;
            case TaskKind.WardRound:
                CompleteWardRound(member, patient);
                break;
            case TaskKind.WardDischarge:
                CompleteWardDischarge(member, patient);
                break;
            case TaskKind.Imaging:
                CompleteImaging(member, patient, task.Detail!);
                break;
            case TaskKind.Surgery:
                CompleteSurgery(member, patient);
                break;
            case TaskKind.DispenseMedicines:
                CompleteDispensing(member, patient);
                break;
        }
    }

    private void CompleteRegistration(StaffMember member, Patient patient)
    {
        _receptionQueue.Remove(patient.Id);
        patient.Log(Now, $"Registered by {member.Name}. Hospital number {patient.HospitalNumber}.");
        patient.Stage = PatientStage.WaitingForTriage;
        patient.QueuedSince = Now;
        SendToWaitingRoom(patient);
        AddTask(TaskKind.Triage, StaffRole.Nurse, patient, 6, "Triage");
    }

    private void CompleteTriage(StaffMember nurse, Patient patient, bool handover)
    {
        foreach (var room in SpacesOfKind(CareSpaceKind.Triage).Where(t => t.ReservedByStaffId == nurse.Id))
        {
            room.ReservedByStaffId = null;
        }

        var obs = patient.CurrentObservations();
        patient.RecordObservations(Now, obs);
        var decision = TriageSystem.Assess(obs, patient.KnownSymptoms.Concat(handover ? patient.Symptoms : Array.Empty<string>()).Distinct(), Database);

        if (handover)
        {
            // The crew's history tells staff about all the symptoms they found.
            foreach (var s in patient.Symptoms)
            {
                patient.KnownSymptoms.Add(s);
            }

            patient.Log(Now, $"Ambulance handover to {nurse.Name}: {patient.AmbulanceHandover}");
        }

        patient.Triage = decision.Category;
        patient.TriageReason = decision.Reason;
        patient.TriagedAt = Now;
        patient.QueuedSince = Now;
        patient.Log(Now, $"Triaged {decision.Category} by {nurse.Name}: {decision.Reason} (obs: {obs})", important: decision.Category == TriageCategory.Red);
        Stats.RecordTriage(decision.Category);
        Events.Publish(new PatientTriaged(patient, decision.Category, IsUpgrade: false));

        if (decision.Category == TriageCategory.Red)
        {
            Notifications.Add(Now, NotificationLevel.Critical, $"Red patient: {Describe(patient)}. {decision.Reason}", patient.Id, null, decision.Category);
        }

        // Nurse-initiated tests under triage protocol, e.g. an ECG within 10 minutes for chest pain.
        // Only tests this hospital can do: an X-ray isn't requested when there is no staffed X-ray room.
        var protocol = patient.KnownSymptoms
            .SelectMany(s => Database.Symptoms[s].TriageInvestigations)
            .Distinct()
            .Where(id => patient.Investigations.All(o => o.InvestigationId != id) && CanDo(Database.Investigations[id]))
            .ToList();
        if (protocol.Count > 0)
        {
            OrderInvestigations(patient, protocol);
            patient.Log(Now, $"Triage protocol: {string.Join(", ", protocol.Select(i => Database.Investigations[i].Name))} requested by {nurse.Name}.");
        }

        if (!handover && patient.CorridorSpot is not null)
        {
            // Corridor care: rated on their trolley where they are, then they wait there for a cubicle.
            patient.Stage = PatientStage.WaitingForSpace;
            patient.Log(Now, $"Observations done in the corridor by {nurse.Name}. Waiting on the trolley for a cubicle.");
        }
        else if (!handover)
        {
            patient.Stage = PatientStage.WaitingForSpace;
            SendToWaitingRoom(patient);
        }
    }

    private void CompleteAssessment(StaffMember doctor, Patient patient)
    {
        patient.HistoryTaken = true;
        patient.FirstSeenByDoctorAt ??= Now;
        foreach (var s in patient.Symptoms)
        {
            patient.KnownSymptoms.Add(s);
        }

        Stats.RecordTimeToDoctor(Now - patient.ArrivedAt);
        var symptoms = string.Join(", ", patient.Symptoms.Select(s => Database.Symptoms[s].Name.ToLowerInvariant()));
        var history = patient.PastHistory.Count == 0 ? "No significant past medical history" : "Past history: " + string.Join(", ", patient.PastHistory);
        var allergies = patient.Allergies.Count == 0 ? "no known drug allergies" : "allergies: " + string.Join(", ", patient.Allergies);
        patient.Log(Now, $"Assessed by {doctor.Name}. History and examination: {symptoms}. {history}; {allergies}.");
        TakeObservations(patient);
        ApplyDecision(doctor, patient);
    }

    private void CompleteReview(StaffMember doctor, Patient patient)
    {
        foreach (var order in patient.Investigations.Where(o => o.Status == InvestigationStatus.Resulted))
        {
            order.Reviewed = true;
        }

        patient.Log(Now, $"Results reviewed by {doctor.Name}.");
        ApplyDecision(doctor, patient);
    }

    private void ApplyDecision(StaffMember doctor, Patient patient)
    {
        if (patient.WorkingDiagnosis is not null)
        {
            return;
        }

        var decision = _diagnosis.Decide(patient, doctor.Skill, doctor.Fatigue, doctor.Caseload.Count, Random);
        IReadOnlyList<DifferentialEntry> entries = decision switch
        {
            DiagnosticDecision.OrderTests o => o.Differential,
            DiagnosticDecision.AwaitResults a => a.Differential,
            DiagnosticDecision.Diagnose d => d.Differential,
            _ => Array.Empty<DifferentialEntry>(),
        };
        var differential = string.Join(", ", entries.Take(3).Select(d => $"{d.Condition.Name.ToLowerInvariant()} ({d.Probability:P0})"));

        switch (decision)
        {
            case DiagnosticDecision.OrderTests order:
                OrderInvestigations(patient, order.InvestigationIds);
                patient.Log(Now, $"Differential diagnosis: {differential}. Requested: {string.Join(", ", order.InvestigationIds.Select(i => Database.Investigations[i].Name))}.");
                break;

            case DiagnosticDecision.AwaitResults:
                patient.Log(Now, $"Differential diagnosis: {differential}. Awaiting results.");
                break;

            case DiagnosticDecision.Diagnose diagnose:
                patient.WorkingDiagnosis = new Diagnosis(diagnose.Condition.Id, diagnose.Confidence, Now, doctor.Id);
                Stats.RecordDiagnosis(diagnose.IsCorrect);
                patient.Log(Now, $"Diagnosis by {doctor.Name}: {diagnose.Condition.Name} ({diagnose.Confidence:P0} confident). Differential considered: {differential}.", important: true);
                TakeObservations(patient, allowDowngrade: true);
                PlanTreatment(patient, diagnose.Condition);
                break;
        }
    }

    private void OrderInvestigations(Patient patient, IReadOnlyList<string> ids)
    {
        var addedBlood = false;
        foreach (var id in ids)
        {
            var test = Database.Investigations[id];
            patient.Investigations.Add(new InvestigationOrder { InvestigationId = id, RequestedAt = Now });
            switch (test.Category)
            {
                case InvestigationCategory.Blood:
                    addedBlood = true;
                    break;
                case InvestigationCategory.Imaging:
                    AddTask(TaskKind.Imaging, RoleForRoom(test.Room!.Value), patient, test.ProcedureMinutes, test.Name, id);
                    break;
                default:
                    AddTask(TaskKind.BedsideTest, test.PerformedBy == ClinicalRole.Doctor ? StaffRole.Doctor : StaffRole.Nurse, patient, test.ProcedureMinutes, test.Name, id);
                    break;
            }
        }

        if (addedBlood && !HasTask(patient, TaskKind.TakeBloods))
        {
            AddTask(TaskKind.TakeBloods, StaffRole.Nurse, patient, 6, "Take blood samples");
        }
    }

    private void CompleteBloods(StaffMember nurse, Patient patient)
    {
        var samples = patient.Investigations
            .Where(o => o.Status == InvestigationStatus.Requested && Database.Investigations[o.InvestigationId].Category == InvestigationCategory.Blood)
            .ToList();
        foreach (var order in samples)
        {
            order.Status = InvestigationStatus.SampleTaken;
            order.SampleTakenAt = Now;
            order.ResultDueAt = Now.AddMinutes(Database.Investigations[order.InvestigationId].TurnaroundMinutes);
        }

        if (samples.Count > 0)
        {
            patient.Log(Now, $"Blood samples taken by {nurse.Name} and sent to the laboratory ({string.Join(", ", samples.Select(s => Database.Investigations[s.InvestigationId].Name))}).");
        }
    }

    private void CompleteBedsideTest(StaffMember member, Patient patient, string investigationId)
    {
        var order = patient.Investigations.FirstOrDefault(o => o.InvestigationId == investigationId && o.Status == InvestigationStatus.Requested);
        if (order is null)
        {
            return;
        }

        order.SampleTakenAt = Now;
        ResultInvestigation(patient, order);
        RequestReviewIfResultsComplete(patient);
    }

    // ---------------------------------------------------------------- treatment and disposition

    private void PlanTreatment(Patient patient, ConditionDefinition diagnosed)
    {
        patient.TreatmentPlan.Clear();
        for (var i = 0; i < diagnosed.Treatment.Count; i++)
        {
            var step = diagnosed.Treatment[i];
            patient.TreatmentPlan.Add(new TreatmentRecord { Step = step });
            if (step.Type == TreatmentStepType.Medication)
            {
                var med = Database.Medications[step.Medication!];
                AddTask(TaskKind.GiveMedication, StaffRole.Nurse, patient, 5, $"Give {med.Name.ToLowerInvariant()}", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (step.Room is { } room && SpacesOfKind(room).Any(IsStaffed))
            {
                // A specialist room does it (a plaster technician puts on a proper cast).
                AddTask(TaskKind.Procedure, RoleForRoom(room), patient, step.Minutes, step.Name!, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                AddTask(TaskKind.Procedure, step.PerformedBy == ClinicalRole.Doctor ? StaffRole.Doctor : StaffRole.Nurse, patient, step.Minutes, step.Name!, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        if (patient.TreatmentPlan.Count == 0)
        {
            BeginDisposition(patient);
        }
    }

    private void CompleteTreatmentStep(StaffMember member, Patient patient, int index)
    {
        if (index < 0 || index >= patient.TreatmentPlan.Count)
        {
            return;
        }

        var record = patient.TreatmentPlan[index];
        var step = record.Step;

        if (step.Type == TreatmentStepType.Medication)
        {
            var med = Database.Medications[step.Medication!];
            var clash = med.AllergyGroups.FirstOrDefault(g => patient.AllergyGroups.Contains(g));
            if (clash is not null)
            {
                record.Withheld = true;
                record.Note = $"Not given: patient allergic ({clash}). Alternative considered.";
                patient.Log(Now, $"{med.Name} withheld by {member.Name}: allergy check found {clash} allergy.", important: true);
            }
            else
            {
                record.Note = $"{med.TypicalDose}, {med.Route}";
                patient.Log(Now, $"{med.Name} {med.TypicalDose} ({med.Route}) given by {member.Name}.");
                var cost = MedicineCost(med.CostPerDose);
                Stats.MedicationCost += cost;
                Book(MoneyCategory.Medicines, -cost);
                patient.Analgesia = Math.Min(0.7, patient.Analgesia + med.DrugClass switch
                {
                    "Opioid analgesic" => 0.5,
                    "Analgesic" or "NSAID" => 0.25,
                    "Nitrate" => 0.15,
                    _ => 0,
                });
            }
        }
        else
        {
            record.Note = $"Performed by {member.Name}";
            patient.Log(Now, $"{step.Name} performed by {member.Name}.");
        }

        record.Done = true;

        // Recovery starts only when the right treatment for the real condition has been given.
        if (patient.WorkingDiagnosis?.ConditionId == patient.Condition.Id
            && patient.TreatmentPlan.Where(r => r.Step.Essential).All(r => r.Done)
            && !patient.EffectiveTreatmentGiven)
        {
            patient.EffectiveTreatmentGiven = true;
            patient.Log(Now, "Responding to treatment.");
        }

        if (patient.TreatmentPlan.All(r => r.Done))
        {
            BeginDisposition(patient);
        }
    }

    private void BeginDisposition(Patient patient)
    {
        var diagnosed = Database.Conditions[patient.WorkingDiagnosis!.ConditionId];
        switch (diagnosed.Disposition)
        {
            case Disposition.Discharge when diagnosed.ObserveHours is not null && StartObservation(patient, diagnosed):
                break;

            case Disposition.Discharge:
                AddTask(TaskKind.Discharge, StaffRole.Nurse, patient, 5, "Discharge with advice");
                break;

            case Disposition.Admit when diagnosed.AmbulatoryHours is not null && patient.Triage != TriageCategory.Red && StartSameDayCare(patient, diagnosed):
                break;

            case Disposition.Admit:
                RequestWardBed(patient, diagnosed);
                break;

            case Disposition.Surgery:
                RequestSurgery(patient, diagnosed);
                break;

            default:
                ArrangeTransfer(patient, diagnosed);
                break;
        }
    }

    private void CompleteDischarge(StaffMember nurse, Patient patient)
    {
        var diagnosed = Database.Conditions[patient.WorkingDiagnosis!.ConditionId];
        var correct = diagnosed.Id == patient.Condition.Id;
        patient.OutcomeNote = correct
            ? diagnosed.DispositionNote
            : $"{diagnosed.DispositionNote} (Missed diagnosis: actually {patient.Condition.Name.ToLowerInvariant()}.)";
        patient.Log(Now, $"Discharged by {nurse.Name}. {diagnosed.DispositionNote}");
        EndVisit(patient, PatientOutcome.Discharged);
        WalkTo(patient, WalkInSpawn);

        if (!correct)
        {
            Stats.MissedDiagnoses++;
            if (patient.Condition.CanBeFatal || patient.Condition.DeteriorationPerHour > 0.05)
            {
                _pendingOutcomeReviews.Add((Now.AddHours(Random.Range(2.0, 8.0)), patient));
            }
        }
    }

    private void CompleteTransfer(StaffMember nurse, Patient patient)
    {
        var diagnosed = Database.Conditions[patient.WorkingDiagnosis!.ConditionId];
        var ambulance = AmbulanceFor(patient);
        patient.OutcomeNote = diagnosed.Id == patient.Condition.Id
            ? diagnosed.DispositionNote
            : $"{diagnosed.DispositionNote} (Working diagnosis was wrong: actually {patient.Condition.Name.ToLowerInvariant()}; the receiving team will reassess.)";
        var where = ambulance is { BaySlot: null } ? " in their cubicle (the ambulance bays were full)" : string.Empty;
        patient.Log(Now, $"Handed over to the transfer crew by {nurse.Name}{where}. Left A&E at {Now:HH:mm}.");
        Notifications.Add(Now, NotificationLevel.Info, $"{patient.FullName} transferred out.", patient.Id, null, patient.Triage);
        EndVisit(patient, PatientOutcome.Transferred);
        patient.Stage = PatientStage.Gone;
        if (ambulance is not null)
        {
            SendAmbulanceAway(ambulance);
        }
    }

    /// <summary>Patients discharged with a missed serious diagnosis come back to haunt the department.</summary>
    internal void CheckOutcomeReviews()
    {
        foreach (var review in _pendingOutcomeReviews.Where(r => r.Due <= Now).ToList())
        {
            _pendingOutcomeReviews.Remove(review);
            var p = review.Patient;
            var serious = p.Condition.CanBeFatal;
            p.Log(Now, serious
                ? $"Re-attended another hospital with {p.Condition.Name.ToLowerInvariant()} after being discharged. Serious incident review opened."
                : $"Re-attended their GP with worsening {p.Condition.Name.ToLowerInvariant()} after being discharged.", important: true);
            Stats.ReputationPenalties += serious ? 5 : 1;
            Notifications.Add(Now, serious ? NotificationLevel.Critical : NotificationLevel.Warning,
                $"Missed diagnosis: {p.FullName}, discharged at {p.LeftAt:HH:mm}, actually had {p.Condition.Name.ToLowerInvariant()}.", p.Id);
        }
    }
}
