using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>Where someone is on their way to collect a prescription.</summary>
public enum PharmacyTripStage
{
    /// <summary>Walking to the pharmacy counter, or to a place in its queue.</summary>
    Going,

    /// <summary>Standing in the queue inside the pharmacy door.</summary>
    Queueing,

    /// <summary>Sitting at the counter, waiting for the pharmacist.</summary>
    AtCounter,

    /// <summary>The pharmacist is checking the prescription and handing it over.</summary>
    BeingServed,
}

/// <summary>A patient going home from A&amp;E, or from a clinic, who stops at the pharmacy on the way out.</summary>
public sealed class PharmacyTrip
{
    public required Agent Person { get; init; }

    public required string RoomId { get; init; }

    /// <summary>What they are collecting, as it reads in their notes ("flucloxacillin", "their new medicine").</summary>
    public required string Medicine { get; init; }

    /// <summary>The counter seat or queue tile they are heading to or standing on.</summary>
    public required GridCoord Target { get; set; }

    public PharmacyTripStage Stage { get; set; } = PharmacyTripStage.Going;

    /// <summary>When they joined the pharmacy queue, so they are served in turn and give up after a while.</summary>
    public required DateTime QueuedAt { get; init; }

    /// <summary>The pharmacist serving them, once one has come to the counter.</summary>
    public int? ServedByStaffId { get; set; }

    /// <summary>When the pharmacist will have finished with them.</summary>
    public DateTime? Until { get; set; }
}

/// <summary>
/// Collecting prescriptions. A&amp;E patients sent home with a course of tablets, and some clinic patients,
/// walk to the pharmacy on their way out, queue inside the door, sit at the counter while the pharmacist
/// checks and hands over their medicines, then go home. Adults of working age pay the prescription charge.
/// Without a staffed pharmacy they take the prescription to a chemist in town instead. Ward patients don't
/// queue: the pharmacist makes up their medicines to take home and a porter (or, with no porters, a ward
/// nurse) carries them up to the ward.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>The prescription charge, paid by patients aged <see cref="PrescriptionChargeFromAge"/> to <see cref="PrescriptionChargeUntilAge"/>.</summary>
    public const int PrescriptionCharge = 10;

    public const int PrescriptionChargeFromAge = 18;

    /// <summary>Patients this age and over get their prescriptions free.</summary>
    public const int PrescriptionChargeUntilAge = 60;

    /// <summary>The share of clinic patients who leave with a new prescription.</summary>
    public const double ClinicPrescriptionChance = 0.4;

    /// <summary>How long the pharmacist takes to check a prescription and hand it over.</summary>
    public const double CounterMinutes = 4;

    /// <summary>After waiting this long at the pharmacy, people give up and use a chemist in town.</summary>
    public static readonly TimeSpan PharmacyGiveUpAfter = TimeSpan.FromMinutes(45);

    private readonly Dictionary<Agent, PharmacyTrip> _pharmacyTrips = new();
    private GameRandom? _pharmacyRandom;

    /// <summary>A separate random stream, so adding prescriptions doesn't change anything else that happens.</summary>
    private GameRandom PharmacyRandom => _pharmacyRandom ??= new GameRandom(_options.Seed + 130_363);

    /// <summary>People on their way to (or waiting at) a pharmacy right now.</summary>
    public IReadOnlyCollection<PharmacyTrip> PharmacyTrips => _pharmacyTrips.Values;

    /// <summary>The prescription a person is collecting, if they are.</summary>
    public PharmacyTrip? PharmacyTripOf(Agent person) => _pharmacyTrips.GetValueOrDefault(person);

    /// <summary>Whether this porter or nurse is carrying a bag of medicines up to a ward.</summary>
    public bool IsCarryingMedicines(StaffMember member) =>
        member.CurrentTaskId is { } id && _tasks.TryGetValue(id, out var task) && task.Kind == TaskKind.DeliverMedicines && task.Started;

    private bool IsCollecting(Agent person) => _pharmacyTrips.ContainsKey(person);

    /// <summary>Tablets (or anything else taken by mouth) from the A&amp;E treatment that the patient carries on with at home.</summary>
    private string? TakeHomeMedicine(Patient patient)
    {
        var given = patient.TreatmentPlan
            .Where(r => r.Step.Type == TreatmentStepType.Medication && !r.Withheld
                        && r.Step.Medication is { } id && Database.Medications.TryGetValue(id, out var med) && med.Route == "oral")
            .Select(r => Database.Medications[r.Step.Medication!].Name.ToLowerInvariant())
            .Distinct()
            .ToList();
        return given.Count == 0 ? null : string.Join(" and ", given);
    }

    /// <summary>An A&amp;E patient going home with tablets to take collects them on the way out.</summary>
    private void OfferPrescription(Patient patient)
    {
        if (TakeHomeMedicine(patient) is not { } medicine)
        {
            return;
        }

        if (!StartPharmacyTrip(patient, medicine))
        {
            patient.Log(Now, $"Given a prescription for {medicine} to take to a chemist in town (no pharmacy open here).");
        }
    }

    /// <summary>Some clinic patients leave with a new prescription to collect.</summary>
    private void OfferClinicPrescription(Outpatient outpatient)
    {
        if (PharmacyRandom.Chance(ClinicPrescriptionChance))
        {
            StartPharmacyTrip(outpatient, "their new medicine");
        }
    }

    /// <summary>Sends someone to the nearest staffed pharmacy's counter, or to the back of its queue.</summary>
    private bool StartPharmacyTrip(Agent person, string medicine)
    {
        var room = SpacesOfKind(CareSpaceKind.Pharmacy).Where(IsStaffed).OrderBy(r => Distance(person.Tile, r.PatientSpot)).FirstOrDefault();
        if (room is null)
        {
            return false;
        }

        var trip = new PharmacyTrip { Person = person, RoomId = room.Id, Medicine = medicine, Target = QueuePlace(room), QueuedAt = Now };
        _pharmacyTrips[person] = trip;
        Send(person, trip.Target);
        switch (person)
        {
            case Patient p:
                p.Log(Now, $"Prescription for {medicine}: going to {room.Name} to collect it.");
                break;
            case Outpatient o:
                o.StatusText = $"Collecting a prescription from {room.Name}";
                break;
        }

        return true;
    }

    /// <summary>The counter if nobody is there or waiting for it, otherwise the next free place in the queue.</summary>
    private GridCoord QueuePlace(CareSpace room)
    {
        var here = _pharmacyTrips.Values.Where(t => t.RoomId == room.Id).ToList();
        if (here.All(t => t.Target != room.PatientSpot) && here.All(t => t.Stage != PharmacyTripStage.Queueing))
        {
            return room.PatientSpot;
        }

        var taken = here.Select(t => t.Target).ToHashSet();
        return room.QueueTiles.FirstOrDefault(t => !taken.Contains(t), room.QueueTiles.Count > 0 ? room.QueueTiles[^1] : room.PatientSpot);
    }

    private void UpdatePharmacyTrips()
    {
        foreach (var trip in _pharmacyTrips.Values.OrderBy(t => t.QueuedAt).ToList())
        {
            var person = trip.Person;
            var gone = person switch
            {
                Patient p => p.Stage == PatientStage.Gone || !p.IsAlive,
                Outpatient o => !_outpatients.Contains(o),
                _ => true,
            };
            if (gone)
            {
                EndPharmacyTrip(trip);
                continue;
            }

            if (FindSpace(trip.RoomId) is not { } room || !IsStaffed(room))
            {
                GiveUpCollecting(trip, "the pharmacy has closed");
                continue;
            }

            // The pharmacist serving them was called away (or their shift ended).
            if (trip.ServedByStaffId is { } staffId && (FindStaff(staffId) is not { } pharmacist || pharmacist.Activity != StaffActivity.Working))
            {
                trip.ServedByStaffId = null;
                trip.Until = null;
                trip.Stage = PharmacyTripStage.AtCounter;
            }

            switch (trip.Stage)
            {
                case PharmacyTripStage.Going when !person.IsMoving && person.Tile == trip.Target:
                    trip.Stage = trip.Target == room.PatientSpot ? PharmacyTripStage.AtCounter : PharmacyTripStage.Queueing;
                    break;

                case PharmacyTripStage.Going when !person.IsMoving:
                    // Got stuck on the way (the room was rebuilt): try again.
                    Send(person, trip.Target);
                    break;

                case PharmacyTripStage.Queueing when CounterFree(room) && FirstInQueue(room) == trip:
                    trip.Target = room.PatientSpot;
                    trip.Stage = PharmacyTripStage.Going;
                    Send(person, trip.Target);
                    break;
            }

            if (trip.ServedByStaffId is null && Now - trip.QueuedAt >= PharmacyGiveUpAfter)
            {
                GiveUpCollecting(trip, "the queue at the pharmacy was too slow");
            }
        }
    }

    private bool CounterFree(CareSpace room) => !_pharmacyTrips.Values.Any(t => t.RoomId == room.Id && t.Target == room.PatientSpot);

    private PharmacyTrip? FirstInQueue(CareSpace room) =>
        _pharmacyTrips.Values.Where(t => t.RoomId == room.Id && t.Stage == PharmacyTripStage.Queueing).OrderBy(t => t.QueuedAt).FirstOrDefault();

    /// <summary>
    /// A pharmacist with nothing else on serves whoever is sitting at their counter. Ward medicines and
    /// counter customers are taken in the order they came in. Returns true while the pharmacist is busy with
    /// a customer, so they don't pick up other work.
    /// </summary>
    private bool ServePharmacyCounter(StaffMember member)
    {
        if (member.Role != StaffRole.Pharmacist)
        {
            return false;
        }

        if (_pharmacyTrips.Values.FirstOrDefault(t => t.ServedByStaffId == member.Id) is { } serving)
        {
            ProgressCounter(member, serving);
            return true;
        }

        if (member.CurrentTaskId is not null || member.Activity != StaffActivity.Idle || member.BaseRoomId is not { } roomId || FindSpace(roomId) is not { } room)
        {
            return false;
        }

        var customer = _pharmacyTrips.Values
            .Where(t => t.RoomId == roomId && t.Stage == PharmacyTripStage.AtCounter && t.ServedByStaffId is null)
            .OrderBy(t => t.QueuedAt)
            .FirstOrDefault();
        if (customer is null)
        {
            return false;
        }

        var wardJob = _tasks.Values
            .Where(t => t.Kind == TaskKind.DispenseMedicines && t.AssignedStaffId is null && IsForMember(t, member))
            .OrderBy(t => t.CreatedAt)
            .FirstOrDefault();
        if (wardJob is not null && wardJob.CreatedAt < customer.QueuedAt)
        {
            return false;
        }

        customer.ServedByStaffId = member.Id;
        member.Activity = StaffActivity.Working;
        member.StatusText = $"Prescription: {NameOf(customer.Person)}";
        if (member.Tile != room.StaffSpot)
        {
            WalkTo(member, room.StaffSpot);
        }

        return true;
    }

    private void ProgressCounter(StaffMember member, PharmacyTrip trip)
    {
        if (FindSpace(trip.RoomId) is not { } room)
        {
            return;
        }

        if (member.Tile != room.StaffSpot)
        {
            if (!member.IsMoving)
            {
                WalkTo(member, room.StaffSpot);
            }

            return;
        }

        if (member.IsMoving)
        {
            return;
        }

        if (trip.Until is null)
        {
            trip.Stage = PharmacyTripStage.BeingServed;
            trip.Until = Now.AddMinutes(CounterMinutes * member.DurationFactor * PharmacyRandom.Range(0.75, 1.25));
            if (trip.Person is Outpatient o)
            {
                o.StatusText = "At the pharmacy counter";
            }

            return;
        }

        if (Now < trip.Until)
        {
            return;
        }

        var person = trip.Person;
        var age = person switch
        {
            Patient p => p.Age,
            Outpatient o => FindAppointment(o.AppointmentId)?.Age ?? 0,
            _ => 0,
        };
        var pays = age is >= PrescriptionChargeFromAge and < PrescriptionChargeUntilAge;
        if (pays)
        {
            Book(MoneyCategory.Prescriptions, PrescriptionCharge);
        }

        Tally(StatKeys.PrescriptionsCollected);
        if (person is Patient patient)
        {
            patient.Log(Now, $"Collected {trip.Medicine} from {member.Name} at {room.Name}{(pays ? $" (paid the £{PrescriptionCharge} prescription charge)" : " (free prescription)")}.");
        }

        member.Activity = StaffActivity.Idle;
        member.StatusText = "Available";
        member.TasksCompleted++;
        EndPharmacyTrip(trip);
    }

    private void GiveUpCollecting(PharmacyTrip trip, string why)
    {
        if (trip.Person is Patient p)
        {
            p.Log(Now, $"Left without collecting {trip.Medicine}: {why}. Will take the prescription to a chemist in town.");
        }

        EndPharmacyTrip(trip);
    }

    /// <summary>The trip is over: they head home.</summary>
    private void EndPharmacyTrip(PharmacyTrip trip)
    {
        _pharmacyTrips.Remove(trip.Person);
        if (trip.ServedByStaffId is { } id && FindStaff(id) is { Activity: StaffActivity.Working } pharmacist && pharmacist.CurrentTaskId is null)
        {
            pharmacist.Activity = StaffActivity.Idle;
            pharmacist.StatusText = "Available";
        }

        switch (trip.Person)
        {
            case Patient p when p.Stage == PatientStage.Leaving:
                WalkTo(p, WayOut(p));
                break;
            case Outpatient o when _outpatients.Contains(o):
                o.StatusText = "Going home";
                Walk(o, WalkInSpawn);
                break;
        }
    }

    private string NameOf(Agent person) => person switch
    {
        Patient p => p.FullName,
        Outpatient o => FindAppointment(o.AppointmentId)?.FullName ?? "a clinic patient",
        _ => "a patient",
    };

    // ---------------------------------------------------------------- ward medicines by porter

    /// <summary>
    /// The porter collects the bag of medicines from the pharmacy counter and carries it to the patient's
    /// bed; the patient can then be discharged. If the pharmacy has gone, they go straight to the ward.
    /// </summary>
    private void ProgressDeliverMedicines(StaffMember member, ClinicalTask task, Patient patient)
    {
        if (patient.Stage != PatientStage.OnWard || !patient.IsAlive)
        {
            FinishCarry(member, task);
            return;
        }

        if (!task.Started)
        {
            if (task.Detail is { } roomId && FindSpace(roomId) is { } pharmacy)
            {
                var pickUp = pharmacy.PatientSpot;
                if (!member.IsMoving && member.Tile != pickUp)
                {
                    WalkTo(member, pickUp);
                }

                if (member.IsMoving || member.Tile != pickUp)
                {
                    return;
                }
            }

            task.Started = true;
            member.Activity = StaffActivity.WalkingToTask;
            member.StatusText = $"Taking medicines to {patient.FullName}";
        }

        var dropOff = BedOf(patient)?.StaffSpot ?? BesideTile(patient.Tile);
        if (!member.IsMoving && member.Tile != dropOff)
        {
            WalkTo(member, dropOff);
        }

        if (member.IsMoving || member.Tile != dropOff)
        {
            return;
        }

        patient.MedicinesReadyAt = Now;
        patient.Log(Now, $"Medicines to take home brought to the ward by {member.Name}{(member.Role == StaffRole.Porter ? " (porter)" : string.Empty)}.");
        FinishCarry(member, task);
    }
}
