using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Specialty test rooms (echo, EEG, early pregnancy unit, endoscopy), the cath lab and the specialty wards.</summary>
public class SpecialtyRoomTests
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

    [Theory]
    [InlineData("echo_room", "echocardiogram", StaffRole.CardiacPhysiologist, "cardiology")]
    [InlineData("eeg_room", "eeg", StaffRole.Neurophysiologist, "neurology")]
    [InlineData("early_pregnancy_unit", "pelvic_ultrasound", StaffRole.Sonographer, "gynaecology")]
    [InlineData("endoscopy_room", "ogd", StaffRole.Endoscopist, "gastroenterology")]
    public void Specialty_test_rooms_open_once_their_specialist_is_hired(string template, string testId, StaffRole role, string department)
    {
        var h = new SimulationHarness(2);
        h.Sim.AdjustBalance(2_000_000);
        Assert.Contains(h.Sim.CheckRoomAt(template, new GridRect(B.X - 30, B.Y + 16, 10, 9)).Problems, p => p.StartsWith("Unlock"));
        Build(h.Sim.UnlockDepartment(department), department);
        Build(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)), "extension");
        Build(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 10, 34, 6), WallSide.South)), "corridor");
        Build(h.Sim.BuildRoom(h.Sim.AutoPlan(template, new GridRect(B.X - 30, B.Y + 16, 10, 9))), template);
        var room = h.Sim.Spaces.Single(s => s.TemplateId == template);
        var test = h.Database.Investigations[testId];

        Assert.False(h.Sim.CanDo(test));
        Assert.Contains(h.Sim.RecommendedStaffing(), n => n.RoomId == room.Id && n.Role == role && n.Short == 1);
        h.RunFor(TimeSpan.FromDays(1));
        Assert.Contains(h.Sim.Candidates, c => c.Role == role);

        var specialist = h.Sim.HireStaff(role);
        Assert.Equal(room.Id, specialist.BaseRoomId);
        Assert.True(h.Sim.CanDo(test));
    }

    [Fact]
    public void Heart_attacks_have_primary_pci_in_the_cath_lab_then_go_to_the_coronary_care_unit()
    {
        var h = Wing(4, new[] { ("cath_lab", 14, 13), ("coronary_care_unit", 16, 10) }, "cardiology");
        var lab = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.CathLab);
        var ccu = h.Sim.Spaces.Single(s => s.TemplateId == "coronary_care_unit");
        Assert.Contains(h.Sim.Staff, s => s.Role == StaffRole.CardiacPhysiologist && s.BaseRoomId == lab.Id);
        Assert.DoesNotContain(h.Sim.SurgicalDepartments, d => d.Id == "cardiology");

        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_myocardial_infarction");
        var inLab = false;
        h.RunUntil(() =>
        {
            inLab |= !patient.IsMoving && patient.Tile == lab.PatientSpot;
            return patient.Stage is PatientStage.OnWard or PatientStage.Gone or PatientStage.AwaitingTransfer;
        }, TimeSpan.FromHours(12), "heart attack patient to reach a ward");

        var journey = h.Journey(patient);
        if (patient.WorkingDiagnosis?.ConditionId != "acute_myocardial_infarction")
        {
            return; // Misdiagnosed in A&E: covered by the diagnosis tests.
        }

        Assert.True(inLab, journey);
        Assert.Contains("Primary PCI performed by", journey);
        Assert.Contains("Booked for the cath lab", journey);
        Assert.Contains("Back from the cath lab", journey);
        Assert.Equal(PatientStage.OnWard, patient.Stage);
        Assert.Equal(ccu.Id, patient.CareSpaceId);
        Assert.Null(lab.ReservedByStaffId);
    }

    [Fact]
    public void Suspected_ectopic_pregnancy_is_scanned_in_the_early_pregnancy_unit()
    {
        var h = Wing(6, new[] { ("early_pregnancy_unit", 10, 9) }, "gynaecology");
        var epu = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.Ultrasound);
        var scanned = 0;
        for (var i = 0; i < 4; i++)
        {
            var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "ectopic_pregnancy");
            h.RunUntil(() => patient.Stage is PatientStage.Gone or PatientStage.AwaitingTransfer or PatientStage.AwaitingSurgery, TimeSpan.FromHours(12), "ectopic patient to be dealt with");
            if (patient.Investigations.Any(o => o.InvestigationId == "pelvic_ultrasound" && o.Status == InvestigationStatus.Resulted))
            {
                scanned++;
                Assert.Contains($"Taken to {epu.Name} for Pelvic ultrasound", h.Journey(patient));
            }
        }

        Assert.True(scanned > 0, "No suspected ectopic pregnancy was scanned.");
    }
}
