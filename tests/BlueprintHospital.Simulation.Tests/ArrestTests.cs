using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Cardiac arrests, the crash team and resus trolleys.</summary>
public class ArrestTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    private static (SimulationHarness H, CareSpace Ward) WithWard(int seed = 5)
    {
        var h = new SimulationHarness(seed);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_ward", new GridRect(B.X - 30, B.Y + 17, 26, 15))).CanBuild);
        h.StaffToRecommended();
        return (h, h.Sim.Wards.Single());
    }

    private static Patient OnWard(SimulationHarness h)
    {
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "community_acquired_pneumonia");
        h.RunUntil(() => patient.Stage is PatientStage.OnWard or PatientStage.Gone, TimeSpan.FromHours(12), "the patient to reach the ward");
        Assert.True(patient.Stage == PatientStage.OnWard, h.Journey(patient));
        return patient;
    }

    [Fact]
    public void Every_resus_bay_and_ward_gets_a_resus_trolley()
    {
        var (h, ward) = WithWard();
        h.RunFor(TimeSpan.FromSeconds(20));
        Assert.Contains(h.Sim.ResusTrolleys, t => t.HomeRoomId == ward.Id && ward.Rect.Contains(t.Tile));
        foreach (var bay in h.Sim.SpacesOfKind(CareSpaceKind.Resus))
        {
            Assert.Contains(h.Sim.ResusTrolleys, t => t.HomeRoomId == bay.Id && bay.Rect.Contains(t.Tile));
        }
    }

    [Fact]
    public void A_trolley_can_be_moved_and_extra_ones_bought_but_not_on_a_bed()
    {
        var (h, ward) = WithWard();
        h.RunFor(TimeSpan.FromSeconds(20));
        var trolley = h.Sim.ResusTrolleys.First(t => t.HomeRoomId == ward.Id);

        Assert.False(h.Sim.MoveResusTrolley(trolley.Id, ward.Beds[0].PatientSpot).CanBuild);

        // Out into the corridor.
        var corridorTile = new GridCoord(B.X - 20, B.Y + 12);
        var moved = h.Sim.MoveResusTrolley(trolley.Id, corridorTile);
        Assert.True(moved.CanBuild, string.Join("; ", moved.Problems));
        Assert.Equal(0, moved.Cost);
        h.RunFor(TimeSpan.FromSeconds(20));
        Assert.Equal(corridorTile, trolley.Tile);
        Assert.True(trolley.Moved);

        var before = h.Sim.ResusTrolleys.Count;
        var bought = h.Sim.BuyResusTrolley(new GridCoord(B.X - 10, B.Y + 12));
        Assert.True(bought.CanBuild, string.Join("; ", bought.Problems));
        Assert.True(bought.Cost > 0);
        Assert.Equal(before + 1, h.Sim.ResusTrolleys.Count);
    }

    [Fact]
    public void A_ward_arrest_calls_the_crash_team_who_bring_the_trolley_and_it_ends_in_rosc_or_death()
    {
        var (h, _) = WithWard();
        var patient = OnWard(h);
        var announcements = new List<Announcement>();
        h.Events.Subscribe<Announcement>(a => announcements.Add(a));

        var arrest = h.Sim.StartArrest(patient, "test");
        h.RunUntil(() => arrest.CrashCalled, TimeSpan.FromMinutes(10), "the crash call");
        Assert.Contains(announcements, a => a.Kind == AnnouncementKind.CardiacArrest && a.Clips.Contains("arrest_medical_ward"));
        Assert.Contains(arrest.Team, r => r.Role == CrashRole.Compressions);
        Assert.Contains(arrest.Team, r => r.Role == CrashRole.Leader);
        Assert.Contains(arrest.Team, r => r.Role == CrashRole.Trolley);

        h.RunUntil(() => arrest.CprStartedAt is not null, TimeSpan.FromMinutes(10), "CPR to start");
        h.RunUntil(() => !arrest.IsActive || arrest.TrolleyArrivedAt is not null, TimeSpan.FromMinutes(15), "the resus trolley");
        h.RunUntil(() => !arrest.IsActive, TimeSpan.FromMinutes(45), "the arrest to end");

        var journey = h.Journey(patient);
        Assert.Contains("Cardiac arrest", journey);
        Assert.Contains("CPR started by", journey);
        if (arrest.Outcome == ArrestOutcome.Rosc)
        {
            Assert.Contains("Return of circulation", journey);
            Assert.True(patient.IsAlive);
            Assert.Equal(3, patient.CareLevel);
        }
        else
        {
            Assert.False(patient.IsAlive, journey);
        }

        // Everyone goes back to work, and the trolley goes home.
        h.RunFor(TimeSpan.FromMinutes(15));
        Assert.DoesNotContain(h.Sim.Staff, s => s.CrashPatientId == patient.Id);
        Assert.All(h.Sim.ResusTrolleys, t => Assert.True(t.IsHome, $"trolley {t.Id} at {t.Tile}, home {t.Home}"));
        Assert.Equal(1, h.Sim.Stats.CardiacArrests);
    }

    [Fact]
    public void Patients_in_bed_arrest_instead_of_dying_on_the_spot()
    {
        var h = new SimulationHarness(seed: 5);
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "septic_shock");
        h.RunUntil(() => patient.CareSpaceId is not null && !patient.IsMoving, TimeSpan.FromHours(1), "a cubicle");
        patient.Severity = 0.9999;
        h.RunUntil(() => patient.InArrest || !patient.IsAlive, TimeSpan.FromMinutes(30), "the heart to stop");
        Assert.True(patient.InArrest, h.Journey(patient));
        Assert.NotNull(h.Sim.ArrestOf(patient));
    }

    [Fact]
    public void Nobody_to_respond_means_no_cpr_and_the_patient_dies()
    {
        var (h, _) = WithWard();
        var patient = OnWard(h);
        foreach (var member in h.Sim.Staff.Where(s => s.Role is StaffRole.Nurse or StaffRole.HealthcareAssistant or StaffRole.Doctor or StaffRole.Midwife or StaffRole.Anaesthetist or StaffRole.OutreachNurse).ToList())
        {
            h.Sim.Fire(member.Id);
        }

        var arrest = h.Sim.StartArrest(patient);
        h.RunUntil(() => !arrest.IsActive, TimeSpan.FromMinutes(30), "the arrest to end");
        Assert.Null(arrest.CprStartedAt);
        Assert.Equal(ArrestOutcome.Died, arrest.Outcome);
        Assert.False(patient.IsAlive);
    }

    [Fact]
    public void Survival_odds_are_realistic_about_half_rosc_and_a_quarter_survive()
    {
        // A mix like the game's: most on wards (found after a few minutes), the rest in resus or ICU.
        var random = new Core.GameRandom(42);
        int rosc = 0, survived = 0;
        const int n = 20_000;
        for (var i = 0; i < n; i++)
        {
            var ward = random.Chance(0.6);
            var age = ResusOdds.AgeFactor(random.Pick(new[] { 55, 65, 75, 78, 85 }));
            var rhythm = random.Chance(0.3) ? ArrestRhythm.Shockable : ArrestRhythm.NonShockable;
            var found = ward ? random.Range(0.0, 4.0) : 0;
            var cpr = found + (ward ? random.Range(0.3, 1.0) : 0.2);
            var trolley = found + (ward ? random.Range(1.5, 3.5) : 0.4);
            var lead = found + (ward ? random.Range(2.0, 5.0) : random.Range(0.5, 2.0));
            var airway = random.Chance(ward ? 0.6 : 0.7) ? found + random.Range(1.0, 6.0) : 999;
            var q = ResusOdds.Quality(cpr);
            var restarted = false;
            double? lastShock = null;
            for (var t = cpr; t - cpr < ResusOdds.StopAfterMinutesOfCpr && !restarted; t += 1.0 / 6)
            {
                var team = ResusOdds.TeamFactor(t >= trolley ? 2 : 1, t >= lead ? 0.65 : null, 0.2, t >= airway);
                if (rhythm == ArrestRhythm.Shockable && t >= ResusOdds.ShockableDecaysAfterMinutes && lastShock is null)
                {
                    rhythm = ArrestRhythm.NonShockable;
                }

                if (rhythm == ArrestRhythm.Shockable && t >= trolley)
                {
                    if (lastShock is null || t - lastShock >= ResusOdds.MinutesBetweenShocks)
                    {
                        lastShock = t;
                        restarted = random.Chance(ResusOdds.ShockSuccess(q, team, age));
                    }
                }
                else
                {
                    restarted = random.Chance(1 - Math.Pow(1 - ResusOdds.NonShockablePerMinute(q, team, age, t >= trolley), 1.0 / 6));
                }
            }

            if (restarted)
            {
                rosc++;
                survived += random.Chance(ResusOdds.SurvivalAfterRosc(q, age, rhythm)) ? 1 : 0;
            }
        }

        var roscRate = rosc / (double)n;
        var survivalRate = survived / (double)n;
        Assert.InRange(roscRate, 0.4, 0.65);
        Assert.InRange(survivalRate, 0.15, 0.32);
    }

    [Fact]
    public void Fast_cpr_and_a_defibrillator_give_better_odds_than_slow()
    {
        var fast = ResusOdds.EstimateSurvival(ArrestRhythm.Shockable, 0, 0.3, true, true, ResusOdds.TeamFactor(2, 0.8, 0, true), 1.0);
        var slow = ResusOdds.EstimateSurvival(ArrestRhythm.Shockable, 0, 5, true, false, ResusOdds.TeamFactor(1, null, 0, false), 1.0);
        Assert.True(fast > slow * 2, $"fast {fast:0.00}, slow {slow:0.00}");
    }

    [Fact]
    public void An_arrest_in_progress_survives_save_and_load()
    {
        var (h, _) = WithWard();
        var patient = OnWard(h);
        var arrest = h.Sim.StartArrest(patient);
        h.RunUntil(() => arrest.CrashCalled, TimeSpan.FromMinutes(10), "the crash call");

        var layout = h.Sim.CaptureLayout();
        var people = h.Sim.CapturePeople();
        var json = System.Text.Json.JsonSerializer.Serialize(people);
        var back = System.Text.Json.JsonSerializer.Deserialize<PeopleState>(json)!;
        Assert.Single(back.Arrests!);
        Assert.NotEmpty(back.ResusTrolleys!);
        Assert.Equal(people.ResusTrolleys!.Count, back.ResusTrolleys!.Count);
        Assert.NotNull(layout);
    }
}
