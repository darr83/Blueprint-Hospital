using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>The plaster room, the physio gym, the endoscopy recovery bay and the ambulatory care unit.</summary>
public class RehabTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A west wing with a corridor and the given rooms south of it, left to right, with the recommended staff hired.</summary>
    private static SimulationHarness Wing(int seed, (string Template, int W, int D)[] south, params string[] unlock)
    {
        var h = new SimulationHarness(seed);
        h.Sim.AdjustBalance(3_000_000);
        foreach (var department in unlock)
        {
            Build(h.Sim.UnlockDepartment(department), department);
        }

        Build(h.Sim.ExtendBuilding(new GridRect(B.X - 40, B.Y, 40, 34)), "extension");
        Build(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 40, B.Y + 10, 44, 6), WallSide.South)), "corridor");
        var x = B.X - 40;
        foreach (var (template, w, d) in south)
        {
            Build(h.Sim.BuildRoom(h.Sim.AutoPlan(template, new GridRect(x, B.Y + 16, w, d))), template);
            x += w;
        }

        h.StaffToRecommended();
        return h;
    }

    private static void Build(BuildCheck result, string what) => Assert.True(result.CanBuild, $"{what}: {string.Join("; ", result.Problems)}");

    [Fact]
    public void New_rooms_ask_for_their_own_specialists()
    {
        var h = Wing(1, new[] { ("plaster_room", 9, 8), ("physio_gym", 12, 10), ("endoscopy_recovery", 8, 8) }, "trauma_orthopaedics", "gastroenterology");
        var plaster = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.PlasterRoom);
        var gym = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.PhysioGym);
        Assert.Contains(h.Sim.Staff, s => s.Role == StaffRole.PlasterTechnician && s.BaseRoomId == plaster.Id);
        Assert.Contains(h.Sim.Staff, s => s.Role == StaffRole.Physiotherapist && s.BaseRoomId == gym.Id);
        Assert.DoesNotContain(h.Sim.RecommendedStaffing(), n => n.RoomId == h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.EndoscopyRecovery).Id && n.Short > 0);
    }

    [Fact]
    public void Broken_ankles_are_plastered_in_the_plaster_room_by_the_technician()
    {
        var h = Wing(3, new[] { ("xray_room", 10, 9), ("plaster_room", 9, 8) }, "trauma_orthopaedics");
        var plaster = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.PlasterRoom);
        var tech = h.Sim.Staff.Single(s => s.Role == StaffRole.PlasterTechnician && s.IsOnDuty);
        var plastered = 0;
        for (var i = 0; i < 3; i++)
        {
            var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "ankle_fracture");
            var inRoom = false;
            h.RunUntil(() =>
            {
                inRoom |= !patient.IsMoving && patient.Tile == plaster.PatientSpot;
                return patient.Stage == PatientStage.Gone;
            }, TimeSpan.FromHours(12), "ankle fracture to go home");
            var journey = h.Journey(patient);
            if (journey.Contains("Below-knee plaster backslab done by"))
            {
                plastered++;
                Assert.True(inRoom, journey);
                Assert.Contains($"Below-knee plaster backslab done by {tech.Name} in {plaster.Name}", journey);
            }
        }

        Assert.True(plastered > 0, "No broken ankle was plastered in the plaster room.");
    }

    [Fact]
    public void Without_a_plaster_technician_AE_staff_plaster_in_the_cubicle()
    {
        var h = Wing(3, new[] { ("xray_room", 10, 9), ("plaster_room", 9, 8) }, "trauma_orthopaedics");
        foreach (var tech in h.Sim.Staff.Where(s => s.Role == StaffRole.PlasterTechnician).ToList())
        {
            h.Sim.Fire(tech.Id);
        }

        var plaster = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.PlasterRoom);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "ankle_fracture");
        var inRoom = false;
        h.RunUntil(() =>
        {
            inRoom |= patient.Tile == plaster.PatientSpot;
            return patient.Stage == PatientStage.Gone;
        }, TimeSpan.FromHours(12), "ankle fracture to go home");
        Assert.False(inRoom, h.Journey(patient));
    }

    [Fact]
    public void Physiotherapy_gets_stroke_patients_home_sooner()
    {
        var h = Wing(5, new[] { ("stroke_unit", 16, 10), ("physio_gym", 12, 10) }, "neurology", "trauma_orthopaedics");
        var physio = h.Sim.Staff.Single(s => s.Role == StaffRole.Physiotherapist && s.IsOnDuty);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "ischaemic_stroke");
        h.RunUntil(() => patient.Stage is PatientStage.OnWard or PatientStage.Gone or PatientStage.AwaitingTransfer, TimeSpan.FromHours(12), "stroke patient to reach a ward");
        if (patient.Stage != PatientStage.OnWard || patient.WorkingDiagnosis?.ConditionId != "ischaemic_stroke")
        {
            return; // Misdiagnosed or transferred: covered elsewhere.
        }

        var before = patient.DischargeDueAt!.Value;
        h.RunUntil(() => patient.PhysioSessions > 0 || patient.Stage != PatientStage.OnWard, TimeSpan.FromDays(3), "a physiotherapy session");
        Assert.Equal(1, patient.PhysioSessions);
        Assert.True(patient.DischargeDueAt < before - TimeSpan.FromHours(9), h.Journey(patient));
        Assert.Contains($"Physiotherapy with {physio.Name}", h.Journey(patient));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Endoscopy_patients_come_round_in_the_recovery_bay_or_else_on_the_trolley(bool withBay)
    {
        var rooms = withBay
            ? new[] { ("endoscopy_room", 10, 9), ("endoscopy_recovery", 8, 8), ("gastro_ward", 16, 10) }
            : new[] { ("endoscopy_room", 10, 9), ("gastro_ward", 16, 10) };
        var h = Wing(8, rooms, "gastroenterology");
        var scope = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.Endoscopy);
        var scoped = 0;
        for (var i = 0; i < 3 && scoped == 0; i++)
        {
            var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "upper_gi_bleed");
            h.RunUntil(() => patient.Stage is PatientStage.OnWard or PatientStage.Gone or PatientStage.AwaitingTransfer, TimeSpan.FromHours(16), "GI bleed to leave A&E");
            var journey = h.Journey(patient);
            if (!journey.Contains("OGD (endoscopy) done by"))
            {
                continue;
            }

            scoped++;
            Assert.Contains(withBay ? "to come round from sedation" : "on the endoscopy trolley", journey);
            Assert.Contains("Awake and recovered from sedation", journey);
        }

        Assert.True(scoped > 0, "No GI bleed had an OGD.");
        Assert.Empty(h.Sim.Recovering);
        Assert.Null(scope.OccupantId);
    }

    [Fact]
    public void Patients_well_enough_have_same_day_care_in_the_ambulatory_unit_and_go_home()
    {
        var h = Wing(11, new[] { ("ambulatory_care_unit", 16, 10), ("medical_ward", 16, 10) });
        var unit = h.Sim.Spaces.Single(s => s.TemplateId == "ambulatory_care_unit");
        Assert.True(h.Sim.IsSameDayUnit(unit));
        var sameDay = 0;
        for (var i = 0; i < 4; i++)
        {
            var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "pyelonephritis");
            CareSpace? ward = null;
            h.RunUntil(() =>
            {
                if (patient.Stage == PatientStage.OnWard && patient.CareSpaceId is { } id)
                {
                    ward ??= h.Sim.FindSpace(id);
                }

                return patient.Stage == PatientStage.Gone;
            }, TimeSpan.FromDays(6), "kidney infection to go home");

            var journey = h.Journey(patient);
            if (journey.Contains("Well enough for same-day emergency care"))
            {
                Assert.Equal(unit.Id, ward?.Id);
                if (patient.SameDay)
                {
                    // Up to 10 hours on the unit, then up to 4 hours waiting for medicines from an outside pharmacy.
                    sameDay++;
                    Assert.Contains("finish treatment at home", journey);
                    Assert.True(h.Sim.Now - patient.AdmittedAt <= TimeSpan.FromHours(15), journey);
                }
            }
            else if (ward is not null)
            {
                Assert.NotEqual(unit.Id, ward.Id);
            }
        }

        Assert.True(sameDay > 0, "No patient had same-day care.");
    }
}
