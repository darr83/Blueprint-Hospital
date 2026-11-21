using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Visitors on the wards, visiting hours, and people walking about to make the hospital feel lived in.</summary>
public class VisitorTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A west wing with a corridor, a medical ward and a café, staffed, with the clock at 10:00.</summary>
    private static (SimulationHarness H, CareSpace Ward) WithWard(int seed)
    {
        var h = new SimulationHarness(seed, start: new DateTime(2026, 11, 2, 10, 0, 0));
        h.Sim.AdjustBalance(3_000_000);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 40, B.Y, 40, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 40, B.Y + 9, 44, 8), WallSide.South)).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_ward", new GridRect(B.X - 40, B.Y + 17, 26, 15))).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("cafeteria", new GridRect(B.X - 12, B.Y + 17, 12, 10))).CanBuild);
        h.StaffToRecommended();
        return (h, h.Sim.Wards.Single());
    }

    private static Patient Admit(SimulationHarness h, string condition = "community_acquired_pneumonia")
    {
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, condition, age: 70);
        h.RunUntil(() => patient.Stage is PatientStage.OnWard or PatientStage.Gone, TimeSpan.FromHours(12), "the patient to reach the ward");
        Assert.True(patient.Stage == PatientStage.OnWard, h.Journey(patient));
        return patient;
    }

    [Fact]
    public void Every_ward_bed_has_a_visitors_chair_beside_it_and_so_does_every_ICU_bed()
    {
        var h = new SimulationHarness();
        foreach (var template in new[] { "medical_ward", "intensive_care_unit", "high_dependency_unit", "mother_baby_ward" })
        {
            foreach (var side in Enum.GetValues<WallSide>())
            {
                var t = h.Database.RoomTemplates[template];
                var ward = RoomLayoutEngine.Build(t, h.Database, "w", "W", RoomLayoutEngine.RectFor(new GridCoord(0, 0), 26, 15, side), side);
                Assert.NotEmpty(ward.Beds);
                var beds = ward.Furniture.Where(f => f.ItemId == "hospital_bed").ToList();
                var chairs = ward.Furniture.Where(f => f.ItemId == "visitor_chair").ToList();
                var withChair = ward.Beds.Count(b => h.Sim.BedsideChairs(ward, b).Any());
                Assert.True(withChair >= ward.Beds.Count - 1, $"{template} {side}: only {withChair} of {ward.Beds.Count} beds have a chair");

                // Chairs sit on the floor beside the beds, never on a bed or on another item.
                foreach (var chair in chairs)
                {
                    Assert.True(ward.Rect.Contains(chair.Tile), $"{template} {side}: chair outside the room");
                    Assert.DoesNotContain(ward.Furniture, f => f != chair && f.Overlaps(chair.X, chair.Y, chair.Width, chair.Length));
                }
            }
        }
    }

    [Fact]
    public void Critical_care_and_the_neonatal_unit_allow_two_visitors_at_a_bed_and_wards_have_afternoon_and_evening_hours()
    {
        var h = new SimulationHarness();
        h.Sim.AdjustBalance(5_000_000);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 40, B.Y, 40, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 40, B.Y + 9, 44, 8), WallSide.South)).CanBuild);
        Assert.True(h.Sim.UnlockDepartment("critical_care").CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("intensive_care_unit", new GridRect(B.X - 40, B.Y + 17, 24, 11))).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_ward", new GridRect(B.X - 14, B.Y + 17, 14, 11))).CanBuild);

        var icu = h.Sim.Wards.Single(w => w.TemplateId == "intensive_care_unit");
        var ward = h.Sim.Wards.Single(w => w.TemplateId == "medical_ward");
        Assert.Equal(2, h.Sim.VisitingHoursOf(icu).AtOnce);
        Assert.True(h.Sim.VisitingHoursOf(ward).AtOnce > 2);
        Assert.True(h.Sim.VisitingHoursOf(ward).IsOpenAt(new DateTime(2026, 11, 2, 15, 0, 0)));
        Assert.False(h.Sim.VisitingHoursOf(ward).IsOpenAt(new DateTime(2026, 11, 2, 3, 0, 0)));

        // The player can change them, within sensible limits.
        h.Sim.SetVisitingHours(icu.Id, 14, 16, 9);
        Assert.Equal(new VisitingHours(14, 16, 4), h.Sim.VisitingHoursOf(icu));
        h.Sim.SetVisitingHours(icu.Id, 18, 12, 0);
        Assert.Equal(new VisitingHours(18, 19, 1), h.Sim.VisitingHoursOf(icu));
    }

    [Fact]
    public void Admitted_patients_get_visitors_who_check_in_sit_by_the_bed_cheer_them_up_and_go_home()
    {
        var (h, ward) = WithWard(31);
        var patients = new[] { Admit(h), Admit(h), Admit(h) };

        var maxHere = new Dictionary<int, int>();
        var sawCheckIn = false;
        var sawSeated = false;
        var seats = h.Sim.SeatTiles;
        h.RunUntil(
            () =>
            {
                foreach (var v in h.Sim.Visitors)
                {
                    sawCheckIn |= v.Activity == VisitorActivity.CheckingIn;
                    sawSeated |= v.Activity == VisitorActivity.Visiting && seats.Contains(v.Tile) && ward.Rect.Contains(v.Tile);
                }

                foreach (var p in patients)
                {
                    var here = h.Sim.VisitorsOf(p).Count();
                    maxHere[p.Id] = Math.Max(maxHere.GetValueOrDefault(p.Id), here);
                }

                return patients.Any(p => p.VisitsReceived > 0) && sawSeated && sawCheckIn;
            },
            TimeSpan.FromHours(30),
            "a visitor to come, check in and sit by a bed");

        var visited = patients.First(p => p.VisitsReceived > 0);
        Assert.Contains(visited.Timeline, e => e.Text.Contains("came to visit"));
        Assert.True(visited.Happiness > HospitalSimulation.UsualHappiness - 0.05, $"happiness {visited.Happiness}");
        Assert.All(maxHere.Values, n => Assert.True(n <= h.Sim.VisitingHoursOf(ward).AtOnce));

        // Visitors don't stay the night: by 23:00 everyone has gone home.
        var night = h.Clock.Now.Date.AddHours(23) > h.Clock.Now ? h.Clock.Now.Date.AddHours(23) : h.Clock.Now.Date.AddDays(1).AddHours(23);
        h.RunUntil(() => h.Clock.Now >= night, TimeSpan.FromDays(2), "late evening");
        Assert.DoesNotContain(h.Sim.Visitors, v => !v.Partner && v.Activity == VisitorActivity.Visiting);
    }

    [Fact]
    public void Nobody_visits_outside_the_hours_the_ward_sets_and_only_one_at_a_bed_when_that_is_the_limit()
    {
        var (h, ward) = WithWard(32);
        var patients = new[] { Admit(h), Admit(h) };
        h.Sim.SetVisitingHours(ward.Id, 15, 17, 1);

        var outside = new List<string>();
        var crowded = false;
        h.RunUntil(
            () =>
            {
                var hour = h.Clock.Now.Hour;
                if ((hour < 15 || hour >= 17) && h.Sim.Visitors.Any(v => v.Activity == VisitorActivity.Visiting))
                {
                    outside.Add($"{h.Clock.Now:HH:mm}");
                }

                crowded |= patients.Any(p => h.Sim.VisitorsOf(p).Count() > 1);
                return h.Clock.Now >= new DateTime(2026, 11, 3, 23, 0, 0);
            },
            TimeSpan.FromDays(3),
            "a day and a half");

        Assert.Empty(outside.Distinct());
        Assert.False(crowded);
    }

    [Fact]
    public void Visiting_hours_are_saved()
    {
        var (h, ward) = WithWard(33);
        h.Sim.SetVisitingHours(ward.Id, 13, 21, 2);
        var json = SaveSerializer.Serialize(new SaveGame { Clock = h.Clock.CaptureState(), Layout = h.Sim.CaptureLayout(), People = h.Sim.CapturePeople() });
        var save = SaveSerializer.Deserialize(json);
        var to = new SimulationHarness(seed: 99);
        to.Clock.RestoreState(save.Clock);
        to.Sim.RestoreLayout(save.Layout!);
        to.Sim.RestorePeople(save.People!);
        Assert.Equal(new VisitingHours(13, 21, 2), to.Sim.VisitingHoursOf(to.Sim.FindSpace(ward.Id)!));
    }

    [Fact]
    public void Patients_who_have_waited_a_while_get_up_for_a_walk_and_come_back_to_their_chair()
    {
        var h = new SimulationHarness(34);
        var patients = Enumerable.Range(0, 16).Select(_ => h.Sim.SpawnPatient(ArrivalMethod.WalkIn, "simple_laceration")).ToList();
        h.RunUntil(() => h.Sim.Strolls.Any(s => s.Person is Patient), TimeSpan.FromHours(8), "a waiting patient to take a walk");
        var stroll = h.Sim.Strolls.First(s => s.Person is Patient);
        var walker = (Patient)stroll.Person;
        var chair = stroll.Home;
        Assert.Equal(walker.Seat, chair);
        Assert.NotEqual(chair, stroll.To);

        h.RunUntil(() => h.Sim.StrollOf(walker) is null, TimeSpan.FromMinutes(30), "the walk to end");
        h.RunFor(TimeSpan.FromSeconds(30));
        if (walker.Stage is PatientStage.WaitingForTriage or PatientStage.WaitingForSpace && !walker.IsMoving)
        {
            Assert.Equal(walker.Seat, walker.Tile);
        }
    }

    [Fact]
    public void Idle_ward_nurses_walk_round_their_patients_instead_of_standing_still()
    {
        var (h, ward) = WithWard(35);
        Admit(h);
        Admit(h);
        var nurses = h.Sim.Staff.Where(s => s.BaseRoomId == ward.Id && s.Role == StaffRole.Nurse).ToList();
        Assert.NotEmpty(nurses);
        h.RunUntil(() => nurses.Any(n => h.Sim.WalkaboutOf(n) is not null), TimeSpan.FromHours(6), "a ward nurse to walk round");
        var what = nurses.Select(h.Sim.WalkaboutOf).First(w => w is not null)!;
        Assert.True(what.StartsWith("Checking on", StringComparison.Ordinal) || what.Contains("station"), what);
    }

    [Fact]
    public void Turning_lively_people_off_means_no_visitors_walks_or_rounds()
    {
        var h = new SimulationHarness(36, lively: false);
        h.Sim.AdjustBalance(3_000_000);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 40, B.Y, 40, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 40, B.Y + 9, 44, 8), WallSide.South)).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_ward", new GridRect(B.X - 40, B.Y + 17, 26, 15))).CanBuild);
        h.StaffToRecommended();
        Admit(h);
        h.RunFor(TimeSpan.FromHours(20));
        Assert.Empty(h.Sim.Visitors);
        Assert.Empty(h.Sim.Strolls);
    }
}
