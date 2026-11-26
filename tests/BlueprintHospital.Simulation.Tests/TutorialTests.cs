using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;
using Xunit;

namespace BlueprintHospital.Simulation.Tests;

public sealed class TutorialTests
{
    /// <summary>A stand-in for the screen, set by the test the way the player's clicks would.</summary>
    private sealed class Screen : ITutorialScreen
    {
        public bool BuildMode { get; set; }

        public string? OpenBuildIcon { get; set; }

        public string? BuildItem { get; set; }

        public bool RecruitmentOpen { get; set; }

        public bool PatientRecordOpen { get; set; }

        public bool CameraMoved { get; set; }

        public bool CameraZoomed { get; set; }

        public bool CameraTurned { get; set; }

        public bool ClockRunning { get; set; }
    }

    private static SimulationHarness NewTutorial() =>
        new(arrivals: true, startWith: StartingHospital.EmptyBuilding, type: GameType.Tutorial);

    private static void Build(SimulationHarness h, string template, GridRect rect)
    {
        var result = h.Sim.BuildRoom(h.Sim.AutoPlan(template, rect));
        Assert.True(result.CanBuild, $"{template}: " + string.Join("; ", result.Problems));
    }

    [Fact]
    public void The_tutorial_starts_on_an_empty_building_with_no_patients()
    {
        var h = NewTutorial();
        var guide = new TutorialGuide(h.Sim);

        Assert.True(h.Sim.IsTutorial);
        Assert.False(h.Sim.IsCareer);
        Assert.Empty(h.Sim.Spaces);
        Assert.Empty(h.Sim.Staff);
        Assert.Equal("welcome", guide.Current.Id);
        h.RunFor(TimeSpan.FromHours(2));
        Assert.Equal(0, h.Sim.Stats.ArrivalsTotal);
    }

    [Fact]
    public void Each_step_ticks_off_as_the_player_builds_and_hires_and_the_first_patient_goes_home()
    {
        var h = NewTutorial();
        var sim = h.Sim;
        var guide = new TutorialGuide(sim);
        var screen = new Screen();
        void Expect(string id)
        {
            guide.Update(screen);
            Assert.Equal(id, guide.Current.Id);
        }

        Expect("welcome");
        guide.Next();
        Expect("camera");
        screen.CameraMoved = screen.CameraZoomed = true;
        Expect("camera");
        screen.CameraTurned = true;
        Expect("build_mode");
        Assert.Equal("nav:Build", guide.Current.Pointer(sim, screen));
        screen.BuildMode = true;
        Expect("waiting_room");
        Assert.Equal("bar:emergency", guide.Current.Pointer(sim, screen));
        screen.OpenBuildIcon = "emergency";
        Assert.Equal("tile:waiting_room", guide.Current.Pointer(sim, screen));
        screen.BuildItem = "waiting_room";
        Assert.Equal("map", guide.Current.Pointer(sim, screen));

        // Build each room in the place the tutorial suggests.
        Build(h, "waiting_room", TutorialGuide.Spots.WaitingRoom);
        Expect("corridor");
        var corridor = sim.BuildRoom(new RoomPlan("corridor", TutorialGuide.Spots.Corridor, WallSide.South));
        Assert.True(corridor.CanBuild, string.Join("; ", corridor.Problems));
        Expect("desk");
        var room = sim.SpacesOfKind(CareSpaceKind.Waiting).Single();
        var desk = sim.PlaceReceptionDesk(room.Id, TutorialGuide.Spots.Desk, WallSide.South);
        Assert.True(desk.CanBuild, string.Join("; ", desk.Problems));
        Expect("doors");
        Assert.True(sim.PlaceWaitingDoor(room.Id, TutorialGuide.Spots.EntranceDoor).CanBuild);
        Expect("doors");
        var inner = sim.PlaceWaitingDoor(room.Id, TutorialGuide.Spots.InnerDoor);
        Assert.True(inner.CanBuild, string.Join("; ", inner.Problems));
        Expect("chairs");
        var chairs = TutorialGuide.Spots.Chairs;
        for (var y = chairs.Y; y < chairs.Bottom; y++)
        {
            var chair = sim.PlaceChair(new GridCoord(chairs.X, y), WallSide.East, ChairColours.All[0].Hex);
            Assert.True(chair.CanBuild, string.Join("; ", chair.Problems));
        }

        Expect("triage");
        Build(h, "triage_room", TutorialGuide.Spots.Triage);
        Expect("minors");
        Build(h, "minors_cubicle", TutorialGuide.Spots.Minors1);
        Expect("minors");
        Build(h, "minors_cubicle", TutorialGuide.Spots.Minors2);
        Expect("majors");
        Build(h, "majors_cubicle", TutorialGuide.Spots.Majors);
        Expect("recruitment");
        Assert.Equal("nav:Hire", guide.Current.Pointer(sim, screen));
        screen.RecruitmentOpen = true;
        Expect("hire");
        Assert.Equal("button:hire_team", guide.Current.Pointer(sim, screen));

        // "Hire minimum team" fills both shifts.
        var hired = sim.HireForShortages();
        Assert.Contains(hired, m => m.Role == StaffRole.Receptionist);
        guide.Update(screen);
        Assert.True(guide.Current.Id == "open", string.Join(", ", sim.Staff.Select(m => $"{m.Role} {m.Shift}")) + " | needs " + string.Join(", ", sim.RecommendedStaffing().Select(n => $"{n.Role} {n.Shift} {n.Recommended}/{n.Short}")) + " | candidates " + string.Join(", ", sim.Candidates.Select(c => c.Role)));
        Assert.True(sim.IsEmergencyOpen, string.Join(", ", sim.ClosedReasons));
        Assert.Equal("mode:Play", guide.Current.Pointer(sim, screen));
        screen.BuildMode = false;
        screen.RecruitmentOpen = false;
        screen.ClockRunning = true;
        Expect("arrive");

        h.RunUntil(() => { guide.Update(screen); return guide.Current.Id == "record"; }, TimeSpan.FromHours(6), "the first patient to be triaged");
        var first = guide.FirstPatient!;
        Assert.NotNull(first.Triage);
        Assert.True(first.Triage != Medical.TriageCategory.Red, "The tutorial only sends green and amber patients.");
        screen.PatientRecordOpen = true;
        Expect("treat");
        h.RunUntil(() => { guide.Update(screen); return guide.Finished; }, TimeSpan.FromHours(10), "the first patient to leave A&E");
        Assert.Equal("done", guide.Current.Id);
        Assert.True(first.Outcome is PatientOutcome.Discharged or PatientOutcome.Admitted or PatientOutcome.Transferred, $"{first.FullName} left as {first.Outcome}.");
    }

    [Fact]
    public void Tutorial_patients_are_adults_who_walk_in_green_or_amber()
    {
        var h = NewTutorial();
        var sim = h.Sim;
        Build(h, "waiting_room", TutorialGuide.Spots.WaitingRoom);
        var corridor = sim.BuildRoom(new RoomPlan("corridor", TutorialGuide.Spots.Corridor, WallSide.South));
        Assert.True(corridor.CanBuild, string.Join("; ", corridor.Problems));
        var room = sim.SpacesOfKind(CareSpaceKind.Waiting).Single();
        Assert.True(sim.PlaceReceptionDesk(room.Id, TutorialGuide.Spots.Desk, WallSide.South).CanBuild);
        Assert.True(sim.PlaceWaitingDoor(room.Id, TutorialGuide.Spots.EntranceDoor).CanBuild);
        Assert.True(sim.PlaceWaitingDoor(room.Id, TutorialGuide.Spots.InnerDoor).CanBuild);
        Build(h, "triage_room", TutorialGuide.Spots.Triage);
        Build(h, "minors_cubicle", TutorialGuide.Spots.Minors1);
        Build(h, "minors_cubicle", TutorialGuide.Spots.Minors2);
        Build(h, "majors_cubicle", TutorialGuide.Spots.Majors);
        h.StaffToRecommended();
        Assert.True(sim.IsEmergencyOpen, string.Join(", ", sim.ClosedReasons));

        h.RunFor(TimeSpan.FromHours(24));
        var everyone = sim.Patients.Concat(sim.DepartedPatients).ToList();
        Assert.True(everyone.Count >= 5, $"Only {everyone.Count} patients came in a day.");
        Assert.All(everyone, p => Assert.True(p.Age >= HospitalSimulation.AdultAge, $"{p.FullName} is {p.Age}."));
        Assert.Equal(0, sim.Stats.AmbulanceArrivalsTotal);
        Assert.All(everyone.Where(p => p.Triage is not null), p => Assert.True(p.Triage != Medical.TriageCategory.Red, $"{p.FullName} was triaged {p.Triage}."));
    }

    [Fact]
    public void The_step_reached_is_saved_with_the_game()
    {
        var h = NewTutorial();
        var guide = new TutorialGuide(h.Sim);
        guide.Next();
        guide.Next();
        var save = new BlueprintHospital.Simulation.Core.SaveGame
        {
            HospitalName = "Tutorial",
            Clock = h.Clock.CaptureState(),
            Layout = h.Sim.CaptureLayout(),
            People = h.Sim.CapturePeople(),
        };
        var json = BlueprintHospital.Simulation.Core.SaveSerializer.Serialize(save);
        var loaded = new SimulationHarness(startWith: StartingHospital.EmptyBuilding);
        var back = BlueprintHospital.Simulation.Core.SaveSerializer.Deserialize(json);
        loaded.Sim.RestoreLayout(back.Layout!);
        loaded.Sim.RestorePeople(back.People!);

        Assert.True(loaded.Sim.IsTutorial);
        Assert.Equal("build_mode", new TutorialGuide(loaded.Sim).Current.Id);
    }
}
