using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Maternity: midwives, the delivery room, mother and baby wards, babies, the neonatal unit, the C-section theatre and antenatal clinics.</summary>
public class MaternityTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>
    /// A maternity wing west of A&amp;E: a corridor with rooms north and south of it. Rooms are given as
    /// (template, width, depth) in tiles; the recommended staff are hired unless <paramref name="staff"/> is false.
    /// </summary>
    private static SimulationHarness Wing(int seed, (string Template, int W, int D)[] north, (string Template, int W, int D)[] south, bool staff = true)
    {
        var h = new SimulationHarness(seed);
        h.Sim.AdjustBalance(5_000_000);
        Build(h.Sim.UnlockDepartment("maternity"), "maternity");
        Build(h.Sim.ExtendBuilding(new GridRect(B.X - 36, B.Y, 36, 36)), "extension");
        Build(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 36, B.Y + 12, 40, 6), WallSide.South)), "corridor");
        var x = B.X - 36;
        foreach (var (template, w, d) in north)
        {
            Build(h.Sim.BuildRoom(h.Sim.AutoPlan(template, new GridRect(x, B.Y + 12 - d, w, d))), template);
            x += w;
        }

        x = B.X - 36;
        foreach (var (template, w, d) in south)
        {
            Build(h.Sim.BuildRoom(h.Sim.AutoPlan(template, new GridRect(x, B.Y + 18, w, d))), template);
            x += w;
        }

        if (staff)
        {
            h.StaffToRecommended();
        }

        return h;
    }

    private static readonly (string, int, int)[] Labour = { ("delivery_room", 10, 10), ("mother_baby_ward", 25, 11) };

    private static void Build(BuildCheck result, string what) => Assert.True(result.CanBuild, $"{what}: {string.Join("; ", result.Problems)}");

    private static CareSpace Room(SimulationHarness h, string template) => h.Sim.Spaces.First(s => s.TemplateId == template);

    private static string Notes(SimulationHarness h, Patient p) =>
        h.Journey(p) + Environment.NewLine + string.Join(Environment.NewLine, h.Sim.BabiesOf(p).SelectMany(b => b.Timeline).Select(t => $"{t.Time:HH:mm} {t.Text}"));

    // ---------------------------------------------------------------- data

    [Fact]
    public void Maternity_data_loads_and_labour_never_comes_through_AandE()
    {
        var h = new SimulationHarness();
        Assert.Equal("maternity", h.Database.Conditions["established_labour"].Department);
        Assert.Equal(CareSpaceKind.DeliveryRoom, h.Database.Conditions["established_labour"].Operation!.Room);
        Assert.Equal(CareSpaceKind.Theatre, h.Database.Conditions["placental_abruption"].Operation!.Room);
        Assert.False(h.Database.Conditions["failure_to_progress"].Maternity!.Arrives);
        Assert.Equal(WardUnit.Neonatal, h.Database.RoomTemplates["neonatal_unit"].Unit);
        Assert.Equal(new[] { "maternity" }, h.Database.RoomTemplates["caesarean_theatre"].TheatreDepartments);
        Assert.Equal(StaffRole.Midwife, h.Database.Clinics["antenatal_clinic"].Clinician);
        Assert.Equal(StaffRole.Sonographer, h.Database.Clinics["antenatal_scan_room"].Clinician);
        Assert.Contains(StaffRole.Midwife, HospitalSimulation.RecruitableRoles);
        Assert.Equal(4, StaffGrades.For(StaffRole.Midwife).Count());

        var generator = new PatientGenerator(h.Database, new GameRandom(7));
        for (var i = 0; i < 3000; i++)
        {
            Assert.False(generator.Generate(h.Clock.Now).Condition.IsMaternity);
        }

        var mother = generator.GenerateMaternity(h.Clock.Now);
        Assert.True(mother.Maternity);
        Assert.Equal(Sex.Female, mother.Sex);
        Assert.InRange(mother.GestationWeeks, 26, 42);
    }

    [Fact]
    public void Wards_have_a_cot_by_every_bed_and_the_neonatal_unit_has_incubators()
    {
        var h = Wing(1, new[] { ("mother_baby_ward", 25, 11) }, new[] { ("neonatal_unit", 18, 11) }, staff: false);
        var ward = Room(h, "mother_baby_ward");
        Assert.True(ward.Beds.Count >= 3, $"only {ward.Beds.Count} beds");
        Assert.Equal(ward.Beds.Count, ward.Furniture.Count(f => f.ItemId == "cot"));
        var unit = Room(h, "neonatal_unit");
        Assert.True(unit.Beds.Count >= 3, $"only {unit.Beds.Count} incubators");
        Assert.Equal(unit.Beds.Count, unit.Furniture.Count(f => f.ItemId == "incubator"));
        Assert.DoesNotContain(unit.Furniture, f => f.ItemId == "hospital_bed");
    }

    // ---------------------------------------------------------------- opening

    [Fact]
    public void Maternity_opens_with_a_delivery_room_a_midwife_and_a_mother_and_baby_ward()
    {
        var h = new SimulationHarness();
        Assert.False(h.Sim.IsMaternityOpen);
        Assert.Null(h.Sim.SpawnMaternityPatient("established_labour"));
        Assert.Equal(1, h.Sim.MaternityStats.Diverted);

        h = Wing(2, Labour, Array.Empty<(string, int, int)>(), staff: false);
        Assert.Contains("a midwife posted to a delivery room", h.Sim.MaternityClosedReasons);
        var room = Room(h, "delivery_room");
        Assert.Contains(h.Sim.PostingsFor(StaffRole.Midwife), p => p.RoomId == room.Id);
        Assert.Contains(h.Sim.PostingsFor(StaffRole.Midwife), p => p.RoomId == Room(h, "mother_baby_ward").Id);
        h.Sim.HireStaff(StaffRole.Midwife, room.Id, skill: 0.7);
        Assert.True(h.Sim.IsMaternityOpen, string.Join(", ", h.Sim.MaternityClosedReasons));
    }

    [Fact]
    public void Recommended_staffing_asks_for_midwives_on_the_ward_and_neonatal_nurses()
    {
        var h = Wing(3, Labour, new[] { ("neonatal_unit", 18, 11) }, staff: false);
        var needs = h.Sim.RecommendedStaffing().Where(n => n.Shift == StaffShift.Day).ToList();
        var ward = Room(h, "mother_baby_ward");
        var unit = Room(h, "neonatal_unit");
        Assert.Contains(needs, n => n.RoomId == Room(h, "delivery_room").Id && n.Role == StaffRole.Midwife);
        Assert.Contains(needs, n => n.RoomId == ward.Id && n.Role == StaffRole.Midwife);
        Assert.DoesNotContain(needs, n => n.RoomId == ward.Id && n.Role == StaffRole.Nurse);
        Assert.Contains(needs, n => n.RoomId == unit.Id && n.Role == StaffRole.Nurse && n.Recommended == (unit.Beds.Count + 1) / 2);
    }

    // ---------------------------------------------------------------- births

    [Fact]
    public void A_woman_in_labour_has_her_baby_and_goes_home_with_it()
    {
        var h = Wing(4, Labour, Array.Empty<(string, int, int)>());
        var mother = h.Sim.SpawnMaternityPatient("established_labour");
        Assert.NotNull(mother);
        Assert.Equal(PatientStage.OnWard, mother!.Stage);
        Assert.False(mother.IsInEmergency);
        Assert.Equal(Room(h, "mother_baby_ward").Id, mother.CareSpaceId);

        h.RunUntil(() => h.Sim.BabiesOf(mother).Any(), TimeSpan.FromHours(10), "the baby to be born");
        var baby = h.Sim.BabiesOf(mother).First();
        Assert.Equal(mother.Surname, baby.Surname);
        Assert.InRange(baby.WeightKg, 0.5, 5.2);
        Assert.True(mother.Delivered);

        if (h.Sim.MaternityStats.Caesareans == 0 && baby.Place == BabyPlace.WithMother)
        {
            // Back on the ward after skin-to-skin, baby in the cot beside her.
            h.RunUntil(() => !mother.IsMoving && h.Sim.BedOf(mother) is { } bed && mother.Tile == bed.PatientSpot, TimeSpan.FromHours(2), "mum back in her bed");
        }

        h.RunUntil(() => mother.Outcome == PatientOutcome.Discharged, TimeSpan.FromDays(3), "mum to go home");
        h.RunFor(TimeSpan.FromMinutes(2));
        Assert.All(h.Sim.BabiesOf(mother).Where(b => !b.NeedsNeonatalUnit && b.AdmittedToNeonatalAt is null), b => Assert.Equal(BabyPlace.Home, b.Place));
        Assert.True(h.Sim.Accounts.Sum(d => d[MoneyCategory.Maternity]) > 0, Notes(h, mother));
        Assert.Equal(0, h.Sim.Stats.ArrivalsTotal);
    }

    [Fact]
    public void A_breech_baby_is_born_by_caesarean_in_the_C_section_theatre()
    {
        var h = Wing(5, Labour, new[] { ("caesarean_theatre", 12, 12) });
        var theatre = Room(h, "caesarean_theatre");
        Assert.Equal(new[] { "maternity" }, theatre.AllowedDepartments);
        Assert.True(h.Sim.HasTheatreFor("maternity"));

        var mother = h.Sim.SpawnMaternityPatient("breech_presentation");
        Assert.NotNull(mother);
        h.RunUntil(() => mother!.Delivered, TimeSpan.FromHours(4), "the caesarean section");
        Assert.Equal(1, h.Sim.MaternityStats.Caesareans);
        Assert.Contains(mother!.Timeline, t => t.Text.Contains("Caesarean section by", StringComparison.Ordinal));
        h.RunUntil(() => !mother.IsMoving && h.Sim.BedOf(mother) is { } bed && mother.Tile == bed.PatientSpot, TimeSpan.FromHours(1), "mum back on the ward");
    }

    [Fact]
    public void Women_needing_a_caesarean_are_sent_elsewhere_without_a_C_section_theatre()
    {
        var h = Wing(6, Labour, Array.Empty<(string, int, int)>());
        Assert.Null(h.Sim.SpawnMaternityPatient("placental_abruption"));
        Assert.Null(h.Sim.SpawnMaternityPatient("preterm_labour"));
        Assert.Equal(2, h.Sim.MaternityStats.Diverted);
    }

    [Fact]
    public void The_C_section_theatre_is_only_for_maternity_and_ordinary_theatres_cant_take_maternity()
    {
        var h = Wing(7, Labour, new[] { ("caesarean_theatre", 12, 12), ("operating_theatre", 12, 12) }, staff: false);
        var section = Room(h, "caesarean_theatre");
        h.Sim.SetTheatreAccess(section.Id, "general_surgery", true);
        h.Sim.SetTheatreAccess(section.Id, "maternity", false);
        Assert.Equal(new[] { "maternity" }, section.AllowedDepartments);
        Assert.DoesNotContain(h.Sim.SurgicalDepartments, d => d.Id == "maternity");
        Assert.Empty(Room(h, "operating_theatre").AllowedDepartments);
    }

    // ---------------------------------------------------------------- the neonatal unit

    [Fact]
    public void A_premature_baby_is_carried_to_the_neonatal_unit_and_gets_better()
    {
        var h = Wing(8, Labour, new[] { ("neonatal_unit", 18, 11) });
        var unit = Room(h, "neonatal_unit");
        var mother = h.Sim.SpawnMaternityPatient("preterm_labour");
        Assert.NotNull(mother);
        h.RunUntil(() => h.Sim.BabiesOf(mother!).Any(), TimeSpan.FromHours(10), "the baby to be born");
        var baby = h.Sim.BabiesOf(mother!).First();
        Assert.True(baby.NeedsNeonatalUnit);
        Assert.Equal(unit.Id, baby.UnitId);

        h.RunUntil(() => baby.Place == BabyPlace.Carried, TimeSpan.FromHours(2), "a nurse to pick the baby up");
        var nurse = h.Sim.FindStaff(baby.CarriedByStaffId!.Value)!;
        Assert.Equal(unit.Id, nurse.BaseRoomId);
        h.RunUntil(() => baby.Place == BabyPlace.NeonatalUnit, TimeSpan.FromHours(1), "the baby to reach the neonatal unit");
        Assert.Equal(1, h.Sim.MaternityStats.NeonatalAdmissions);
        var before = baby.Severity;
        h.RunFor(TimeSpan.FromHours(12));
        Assert.True(baby.Severity < before, Notes(h, mother!));
    }

    [Fact]
    public void Without_a_neonatal_unit_a_poorly_baby_is_transferred()
    {
        var h = Wing(9, Labour, Array.Empty<(string, int, int)>());
        var mother = h.Sim.SpawnMaternityPatient("established_labour")!;
        h.RunUntil(() => h.Sim.BabiesOf(mother).Any(), TimeSpan.FromHours(10), "the baby to be born");

        // Every baby that needed neonatal care went to another hospital's neonatal unit.
        foreach (var baby in h.Sim.BabiesOf(mother).Where(b => b.NeonatalReason is not null))
        {
            Assert.Equal(BabyPlace.Transferred, baby.Place);
        }
    }

    // ---------------------------------------------------------------- clinics

    [Fact]
    public void Antenatal_clinics_are_run_by_midwives_and_scans_by_sonographers()
    {
        var h = Wing(10, new[] { ("antenatal_clinic", 8, 7), ("antenatal_scan_room", 9, 8) }, Array.Empty<(string, int, int)>(), staff: false);
        var clinic = Room(h, "antenatal_clinic");
        var scans = Room(h, "antenatal_scan_room");
        Assert.Contains("a midwife posted to it", h.Sim.ClinicProblems(clinic));
        Assert.Contains("a sonographer posted to it", h.Sim.ClinicProblems(scans));
        Assert.Contains(h.Sim.PostingsFor(StaffRole.Midwife), p => p.RoomId == clinic.Id);
        Assert.Contains(h.Sim.PostingsFor(StaffRole.Sonographer), p => p.RoomId == scans.Id);
        Assert.DoesNotContain(h.Sim.PostingsFor(StaffRole.Doctor), p => p.RoomId == clinic.Id);
        h.Sim.HireStaff(StaffRole.Midwife, clinic.Id, skill: 0.7);
        Assert.DoesNotContain("a midwife posted to it", h.Sim.ClinicProblems(clinic));
    }

    // ---------------------------------------------------------------- saving

    [Fact]
    public void Babies_are_saved_and_loaded()
    {
        var h = Wing(11, Labour, Array.Empty<(string, int, int)>());
        var mother = h.Sim.SpawnMaternityPatient("established_labour")!;
        h.RunUntil(() => h.Sim.BabiesOf(mother).Any(), TimeSpan.FromHours(10), "the baby to be born");
        var baby = h.Sim.BabiesOf(mother).First();
        h.Sim.RenameBaby(baby.Id, "Rosie May " + baby.Surname);

        var people = h.Sim.CapturePeople();
        var json = System.Text.Json.JsonSerializer.Serialize(people);
        Assert.Contains("Rosie May", json, StringComparison.Ordinal);
        Assert.Single(people.Babies, b => b.MotherId == mother.Id && b.FirstName == "Rosie May");
        Assert.True(people.Patients.Single(p => p.Id == mother.Id).Maternity);
        Assert.Equal(h.Sim.MaternityStats.Births, people.Maternity!.Births);
    }
}
