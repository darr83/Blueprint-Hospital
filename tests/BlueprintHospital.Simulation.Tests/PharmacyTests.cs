using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Collecting prescriptions at the pharmacy, and porters carrying ward medicines.</summary>
public class PharmacyTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A west wing with a corridor, a pharmacy north of it and (optionally) more rooms south of it.</summary>
    private static (SimulationHarness H, CareSpace? Pharmacy) Wing(int seed, bool pharmacy, params (string Template, int W, int D)[] south)
    {
        var h = new SimulationHarness(seed);
        h.Sim.AdjustBalance(2_000_000);
        Build(h, h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)), "extension");
        Build(h, h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 10, 34, 6), WallSide.South)), "corridor");
        if (pharmacy)
        {
            Build(h, h.Sim.BuildRoom(h.Sim.AutoPlan("pharmacy", new GridRect(B.X - 30, B.Y + 2, 10, 8))), "pharmacy");
        }

        var x = B.X - 30;
        foreach (var (template, w, d) in south)
        {
            Build(h, h.Sim.BuildRoom(h.Sim.AutoPlan(template, new GridRect(x, B.Y + 16, w, d))), template);
            x += w;
        }

        h.StaffToRecommended();
        return (h, h.Sim.SpacesOfKind(CareSpaceKind.Pharmacy).FirstOrDefault());
    }

    private static void Build(SimulationHarness h, BuildCheck result, string what) => Assert.True(result.CanBuild, $"{what}: {string.Join("; ", result.Problems)}");

    [Fact]
    public void A_pharmacy_has_a_counter_seat_across_from_the_pharmacist_and_room_to_queue_inside_the_door()
    {
        var h = new SimulationHarness();
        var template = h.Database.RoomTemplates["pharmacy"];
        foreach (var (w, l) in new[] { (3.0, 3.0), (4.0, 3.5), (5.0, 4.5), (8.0, 8.0) })
        {
            foreach (var side in Enum.GetValues<WallSide>())
            {
                var room = RoomLayoutEngine.Build(template, h.Database, "p", "P",
                    RoomLayoutEngine.RectFor(new GridCoord(0, 0), Units.MetresToTiles(w), Units.MetresToTiles(l), side), side);
                var what = $"{w}x{l} facing {side}";
                Assert.NotEqual(room.StaffSpot, room.PatientSpot);
                Assert.True(room.Rect.Contains(room.PatientSpot) && !room.Rect.IsOnEdge(room.PatientSpot), what);
                Assert.NotEmpty(room.QueueTiles);
                Assert.All(room.QueueTiles, t =>
                {
                    Assert.True(room.Rect.Contains(t) && !room.Rect.IsOnEdge(t), what);
                    Assert.DoesNotContain(room.Furniture, f => f.Overlaps(t.X, t.Y, 1, 1));
                });
            }
        }
    }

    [Fact]
    public void An_AE_patient_sent_home_with_tablets_collects_them_from_the_pharmacy_on_the_way_out()
    {
        var (h, pharmacy) = Wing(3, pharmacy: true);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "cellulitis");

        var atCounter = false;
        h.RunUntil(() =>
        {
            atCounter |= !patient.IsMoving && patient.Tile == pharmacy!.PatientSpot;
            return patient.Stage == PatientStage.Gone;
        }, TimeSpan.FromHours(12), "cellulitis patient to go home");

        var journey = h.Journey(patient);
        if (patient.Outcome != PatientOutcome.Discharged || !patient.TreatmentPlan.Any(r => r.Step.Medication == "flucloxacillin" && !r.Withheld))
        {
            return; // Misdiagnosed or allergic: covered elsewhere.
        }

        Assert.True(atCounter, journey);
        Assert.Contains("Collected flucloxacillin", journey);
        Assert.Empty(h.Sim.PharmacyTrips);
        var paid = patient.Age is >= HospitalSimulation.PrescriptionChargeFromAge and < HospitalSimulation.PrescriptionChargeUntilAge;
        Assert.Equal(paid ? HospitalSimulation.PrescriptionCharge : 0, h.Sim.Today[MoneyCategory.Prescriptions]);
    }

    [Fact]
    public void Without_a_pharmacy_the_prescription_goes_to_a_chemist_in_town()
    {
        var (h, _) = Wing(3, pharmacy: false);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "cellulitis");
        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromHours(12), "cellulitis patient to go home");
        if (patient.Outcome == PatientOutcome.Discharged && patient.TreatmentPlan.Any(r => r.Step.Medication == "flucloxacillin" && !r.Withheld))
        {
            Assert.Contains("chemist in town", h.Journey(patient));
        }

        Assert.Empty(h.Sim.PharmacyTrips);
    }

    [Fact]
    public void Patients_queue_inside_the_pharmacy_and_are_served_one_at_a_time()
    {
        var (h, _) = Wing(4, pharmacy: true);
        var patients = Enumerable.Range(0, 3).Select(_ => h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "cellulitis")).ToList();
        var mostAtCounter = 0;
        h.RunUntil(() =>
        {
            var trips = h.Sim.PharmacyTrips.ToList();
            mostAtCounter = Math.Max(mostAtCounter, trips.Count(t => t.Stage is PharmacyTripStage.AtCounter or PharmacyTripStage.BeingServed));
            return patients.All(p => p.Stage == PatientStage.Gone);
        }, TimeSpan.FromHours(16), "all three patients to go home");

        Assert.True(mostAtCounter <= 1);
        var collected = patients.Count(p => h.Journey(p).Contains("Collected"));
        Assert.True(collected >= 1, string.Join("\n\n", patients.Select(h.Journey)));
        Assert.Empty(h.Sim.PharmacyTrips);
    }

    [Fact]
    public void A_porter_carries_ward_medicines_from_the_pharmacy_to_the_bed()
    {
        var (h, pharmacy) = Wing(9, pharmacy: true, ("medical_ward", 16, 10));
        Assert.Contains(h.Sim.Staff, s => s.Role == StaffRole.Porter);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        h.RunUntil(() => patient.Stage is PatientStage.OnWard or PatientStage.Gone, TimeSpan.FromHours(12), "pneumonia patient to reach the ward");
        Assert.Equal(PatientStage.OnWard, patient.Stage);

        var porterAtPharmacy = false;
        var carried = false;
        h.RunUntil(() =>
        {
            foreach (var porter in h.Sim.Staff.Where(s => s.Role == StaffRole.Porter))
            {
                porterAtPharmacy |= porter.Tile == pharmacy!.PatientSpot;
                carried |= h.Sim.IsCarryingMedicines(porter);
            }

            return patient.MedicinesReadyAt is not null || patient.Stage == PatientStage.Gone;
        }, TimeSpan.FromDays(6), "medicines to reach the ward");

        var journey = h.Journey(patient);
        Assert.True(porterAtPharmacy, journey);
        Assert.True(carried, journey);
        Assert.Contains("dispensed by", journey);
        Assert.Contains("brought to the ward by", journey);
        Assert.Contains("(porter)", journey);

        h.RunUntil(() => patient.Stage == PatientStage.Gone, TimeSpan.FromDays(1), "patient to go home");
        Assert.Equal(PatientOutcome.Discharged, patient.Outcome);
    }

    [Fact]
    public void Some_clinic_patients_collect_a_prescription_before_going_home()
    {
        var (h, _) = Wing(3, pharmacy: true, ("outpatient_reception", 14, 10), ("medical_clinic", 8, 7));
        var reception = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.OutpatientReception);
        var clinic = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.Clinic);
        if (!h.Sim.Staff.Any(s => s.BaseRoomId == reception.Id))
        {
            h.Sim.HireStaff(StaffRole.Receptionist, reception.Id, skill: 0.7);
        }

        if (!h.Sim.Staff.Any(s => s.BaseRoomId == clinic.Id))
        {
            h.Sim.HireStaff(StaffRole.Doctor, clinic.Id, skill: 0.7);
        }

        var collecting = false;
        h.RunUntil(() =>
        {
            collecting |= h.Sim.PharmacyTrips.Any(t => t.Person is Outpatient);
            return collecting && h.Sim.Today[MoneyCategory.Clinics] > 0 && h.Sim.PharmacyTrips.All(t => t.Person is not Outpatient);
        }, TimeSpan.FromDays(2), "a clinic patient to collect a prescription");

        Assert.True(collecting);
    }
}
