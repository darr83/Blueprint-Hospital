using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>What the tutorial needs to know about the screen: which mode, menus and panels are showing.</summary>
public interface ITutorialScreen
{
    /// <summary>Build mode is on (the build bar is showing).</summary>
    bool BuildMode { get; }

    /// <summary>The build bar icon whose pop-up is open, e.g. "emergency" or "building".</summary>
    string? OpenBuildIcon { get; }

    /// <summary>The build bar item being placed, e.g. "corridor" or "waiting_desk".</summary>
    string? BuildItem { get; }

    /// <summary>The Recruitment panel is open.</summary>
    bool RecruitmentOpen { get; }

    /// <summary>A patient's record is open.</summary>
    bool PatientRecordOpen { get; }

    /// <summary>The view has been moved, zoomed and turned since the tutorial began.</summary>
    bool CameraMoved { get; }

    bool CameraZoomed { get; }

    bool CameraTurned { get; }

    /// <summary>The game clock is running.</summary>
    bool ClockRunning { get; }

    /// <summary>The game clock is running at 2x or faster.</summary>
    bool ClockFast { get; }
}

/// <summary>One thing to do within a tutorial step, ticked off when <see cref="Done"/> is true.</summary>
public sealed record TutorialTask(string Text, Func<HospitalSimulation, ITutorialScreen, bool> Done);

/// <summary>
/// One step of the tutorial: a title, a few words of explanation, the tasks that finish it, where on screen
/// to point next, and (for building steps) a suggested place on the map.
/// </summary>
public sealed class TutorialStepDefinition
{
    public required string Id { get; init; }

    /// <summary>The part of the tutorial, e.g. "The front door".</summary>
    public required string Part { get; init; }

    public required string Title { get; init; }

    public required string Text { get; init; }

    /// <summary>What to do; the step is finished when every task is done. No tasks: the player presses Next.</summary>
    public IReadOnlyList<TutorialTask> Tasks { get; init; } = Array.Empty<TutorialTask>();

    /// <summary>
    /// What to click next, as an id the screen understands ("nav:Build", "bar:emergency", "tile:triage_room",
    /// "mode:Play", "speed:Normal", "button:hire_team", "map"), or null for nothing in particular.
    /// </summary>
    public Func<HospitalSimulation, ITutorialScreen, string?> Pointer { get; init; } = (_, _) => null;

    /// <summary>Where on the map the step suggests building (site tiles), if anywhere.</summary>
    public GridRect? Suggested { get; init; }

    /// <summary>The label shown on the suggested area.</summary>
    public string? SuggestedLabel { get; init; }

    /// <summary>A tip shown under the tasks, in a lighter colour.</summary>
    public string? Tip { get; init; }
}

/// <summary>
/// The guided tutorial. Starting from an empty building, the player builds a waiting room over the main entrance,
/// lays a corridor, fits out the waiting room, adds a triage room and cubicles, hires a team and opens A&amp;E, then follows the first patient through.
/// Steps tick themselves off as the hospital changes; the step reached is saved with the game. Engine-free, so the
/// whole tutorial can be played through in tests.
/// </summary>
public sealed class TutorialGuide
{
    /// <summary>Where the tutorial suggests building, on the standard site (the tutorial always uses it).</summary>
    public static class Spots
    {
        private static readonly GridRect B = StarterSite.MainBuildingFootprint;

        /// <summary>A corridor across the building, along the back of the waiting room.</summary>
        public static readonly GridRect Corridor = new(B.X + 4, B.Y + 18, 52, 8);

        /// <summary>The waiting room, over the front path and the main doors.</summary>
        public static readonly GridRect WaitingRoom = new(B.X + 15, B.Y + 26, 22, 14);

        /// <summary>The reception desk's tile, in the waiting room.</summary>
        public static readonly GridCoord Desk = new(B.X + 30, B.Y + 32);

        /// <summary>The waiting room's entrance door (on the front path) and its door onto the corridor.</summary>
        public static readonly GridCoord EntranceDoor = new(B.X + 26, B.Y + 39);

        public static readonly GridCoord InnerDoor = new(B.X + 26, B.Y + 26);

        /// <summary>A row of chairs facing the desk.</summary>
        public static readonly GridRect Chairs = new(B.X + 19, B.Y + 29, 1, 8);

        /// <summary>The triage room, beside the waiting room, off the corridor.</summary>
        public static readonly GridRect Triage = new(B.X + 5, B.Y + 26, 9, 8);

        /// <summary>Two minors cubicles and a majors cubicle across the corridor.</summary>
        public static readonly GridRect Minors1 = new(B.X + 14, B.Y + 9, 8, 9);

        public static readonly GridRect Minors2 = new(B.X + 22, B.Y + 9, 8, 9);

        public static readonly GridRect Majors = new(B.X + 32, B.Y + 9, 8, 9);
    }

    /// <summary>The saved step of a tutorial ended by older builds: the guide no longer shows. (Ending a tutorial now turns it into a Sandbox game.)</summary>
    public const int Ended = -1;

    /// <summary>How many chairs the waiting room needs for the tutorial.</summary>
    public const int ChairsWanted = 6;

    private readonly HospitalSimulation _sim;

    public TutorialGuide(HospitalSimulation sim)
    {
        _sim = sim;
        Steps = BuildSteps();
    }

    public IReadOnlyList<TutorialStepDefinition> Steps { get; }

    /// <summary>The step the player is on (kept in the simulation so it is saved).</summary>
    public int Index
    {
        get => Math.Clamp(_sim.TutorialStep, 0, Steps.Count - 1);
        private set => _sim.TutorialStep = Math.Clamp(value, 0, Steps.Count - 1);
    }

    public TutorialStepDefinition Current => Steps[Index];

    /// <summary>False once the player has ended the tutorial (it then stays ended, also after loading).</summary>
    public bool Active => _sim.IsTutorial && _sim.TutorialStep != Ended;

    /// <summary>True on the last step: the tutorial is finished.</summary>
    public bool Finished => Index == Steps.Count - 1;

    /// <summary>The patient the tutorial follows: the first to come in once A&amp;E is open.</summary>
    public Patient? FirstPatient => _sim.TutorialFirstPatient;

    /// <summary>Each task of the current step, and whether it is done.</summary>
    public IReadOnlyList<(TutorialTask Task, bool Done)> TaskStates(ITutorialScreen screen) =>
        Current.Tasks.Select(t => (t, t.Done(_sim, screen))).ToList();

    /// <summary>Whether the current step is finished (a step with no tasks waits for <see cref="Next"/>).</summary>
    public bool StepDone(ITutorialScreen screen) => Current.Tasks.Count > 0 && Current.Tasks.All(t => t.Done(_sim, screen));

    /// <summary>
    /// Moves on past every finished step (so building ahead of the tutorial is fine) and notes the first patient.
    /// Returns true when the step changed.
    /// </summary>
    public bool Update(ITutorialScreen screen)
    {
        if (_sim.TutorialFirstPatientId is null && _sim.IsEmergencyOpen)
        {
            _sim.TutorialFirstPatientId = _sim.Patients.Where(p => p.IsInEmergency).OrderBy(p => p.Id).FirstOrDefault()?.Id;
        }

        var before = Index;
        while (!Finished && StepDone(screen))
        {
            Index++;
        }

        return Index != before;
    }

    /// <summary>Goes on to the next step (the Next button, for steps that only explain).</summary>
    public void Next()
    {
        if (!Finished)
        {
            Index++;
        }
    }

    /// <summary>Goes back a step, to read it again.</summary>
    public void Back()
    {
        if (Index > 0)
        {
            Index--;
        }
    }

    /// <summary>Skips the rest of the current step.</summary>
    public void Skip() => Next();

    // ---------------------------------------------------------------- what counts as done

    private static IEnumerable<CareSpace> Built(HospitalSimulation sim, CareSpaceKind kind) =>
        sim.Spaces.Where(s => s.Kind == kind);

    private static CareSpace? WaitingRoom(HospitalSimulation sim) =>
        Built(sim, CareSpaceKind.Waiting).OrderBy(r => sim.WaitingRoomProblems(r).Count).FirstOrDefault();

    private static bool HasCorridor(HospitalSimulation sim) => Built(sim, CareSpaceKind.Corridor).Any();

    private static bool HasDesk(HospitalSimulation sim) => WaitingRoom(sim) is { } room && sim.DeskOf(room) is not null;

    private static bool HasEntranceDoor(HospitalSimulation sim) =>
        WaitingRoom(sim) is { } room && (sim.HasEntranceDoor(room) || sim.ReachedFromStreet(room));

    private static bool HasInnerDoor(HospitalSimulation sim) => WaitingRoom(sim) is { } room && sim.HasInnerDoor(room);

    private static int ChairsInWaitingRoom(HospitalSimulation sim) =>
        WaitingRoom(sim) is { } room ? sim.Chairs.Count(c => room.Rect.Contains(c.Tile)) : 0;

    private static int InUse(HospitalSimulation sim, CareSpaceKind kind) =>
        sim.Spaces.Where(s => s.Kind == kind && s.InUse).Sum(s => s.Places);

    private static int OnStaff(HospitalSimulation sim, StaffRole role) =>
        sim.Staff.Count(s => s.Role == role && s.BaseRoomId is null);

    private static bool DayAndNight(HospitalSimulation sim, StaffRole role) =>
        new[] { StaffShift.Day, StaffShift.Night }.All(shift => sim.Staff.Any(s => s.Role == role && s.Shift == shift));

    /// <summary>The next thing to click to place <paramref name="item"/> from the build bar icon <paramref name="icon"/>.</summary>
    private static string? BuildPointer(ITutorialScreen screen, string icon, string item) =>
        !screen.BuildMode ? "nav:Build"
        : screen.BuildItem == item ? "map"
        : screen.OpenBuildIcon != icon ? $"bar:{icon}"
        : $"tile:{item}";

    // ---------------------------------------------------------------- the steps

    private List<TutorialStepDefinition> BuildSteps()
    {
        const string Start = "Getting started", Front = "The front door", Treat = "Places to treat patients", Team = "Your team", Open = "Opening A&E";
        return new List<TutorialStepDefinition>
        {
            new()
            {
                Id = "welcome", Part = Start, Title = "Welcome to Blueprint Hospital",
                Text = "This hospital is an empty building. In this tutorial you will build a small A&E, hire a team to run it, and see your first patients through, one step at a time.\n\nPatients only come once A&E is open, so take your time. Your steps tick themselves off as you go, and you can go back or skip a step at any time.",
            },
            new()
            {
                Id = "camera", Part = Start, Title = "Look around",
                Text = "First, get used to moving the view.",
                Tasks = new TutorialTask[]
                {
                    new("Move the view: W A S D, the arrow keys, or drag with the right mouse button", (_, s) => s.CameraMoved),
                    new("Zoom in and out with the mouse wheel", (_, s) => s.CameraZoomed),
                    new("Turn the view with Q and E", (_, s) => s.CameraTurned),
                },
                Tip = "The buttons at the bottom right zoom and turn the view too.",
            },
            new()
            {
                Id = "build_mode", Part = Front, Title = "Switch to Build mode",
                Text = "Everything you build is in Build mode. Click Build on the left, or press B. The build bar opens along the bottom of the screen, with an icon for the building and one for each department.",
                Tasks = new TutorialTask[] { new("Switch to Build mode", (_, s) => s.BuildMode) },
                Pointer = (_, s) => s.BuildMode ? null : "nav:Build",
            },
            new()
            {
                Id = "waiting_room", Part = Front, Title = "Build a waiting room",
                Text = "Patients walk in through the main entrance, book in at reception and wait to be seen. Open A&E in the build bar, pick Waiting room, and drag it out over the main entrance. The blue outline shows a good place.",
                Tasks = new TutorialTask[] { new("Build a waiting room", (sim, _) => Built(sim, CareSpaceKind.Waiting).Any()) },
                Pointer = (_, s) => BuildPointer(s, "emergency", "waiting_room"),
                Suggested = Spots.WaitingRoom, SuggestedLabel = "Waiting room",
                Tip = "Drag from one corner to the other. Green means it can be built; red explains what is wrong. Right-click to stop placing.",
            },
            new()
            {
                Id = "corridor", Part = Front, Title = "Lay a corridor",
                Text = "Corridors join everything up: every room's door must open onto one, and each corridor must join the entrance or another corridor. Open the Building icon, pick Corridor, and drag a strip across the building along the back wall of the waiting room.",
                Tasks = new TutorialTask[] { new("Build a corridor", (sim, _) => HasCorridor(sim)) },
                Pointer = (_, s) => BuildPointer(s, "building", "corridor"),
                Suggested = Spots.Corridor, SuggestedLabel = "Corridor",
            },
            new()
            {
                Id = "desk", Part = Front, Title = "Add a reception desk",
                Text = "A receptionist books each patient in at the desk. Pick Reception desk from the A&E icon and click inside the waiting room. Press Z to turn it before you click.",
                Tasks = new TutorialTask[] { new("Place a reception desk in the waiting room", (sim, _) => HasDesk(sim)) },
                Pointer = (_, s) => BuildPointer(s, "emergency", "waiting_desk"),
                Suggested = new GridRect(Spots.Desk.X - 1, Spots.Desk.Y - 1, 4, 3), SuggestedLabel = "Desk",
            },
            new()
            {
                Id = "doors", Part = Front, Title = "Put in the doors",
                Text = "The waiting room needs two doors: an entrance on the front path, where people walk in from outside, and a door through to the corridor. Pick Waiting room doors and click on a wall for each.",
                Tasks = new TutorialTask[]
                {
                    new("An entrance door on the front path", (sim, _) => HasEntranceDoor(sim)),
                    new("A door through to the corridor", (sim, _) => HasInnerDoor(sim)),
                },
                Pointer = (_, s) => BuildPointer(s, "emergency", "waiting_doors"),
                Suggested = new GridRect(Spots.InnerDoor.X - 1, Spots.InnerDoor.Y - 1, 4, 2), SuggestedLabel = "Door",
            },
            new()
            {
                Id = "chairs", Part = Front, Title = "Add some chairs",
                Text = $"Nobody likes to stand. Pick Waiting chairs and drag a row of at least {ChairsWanted} in the waiting room. Z turns them; the colour swatches change their colour.",
                Tasks = new TutorialTask[] { new($"Place {ChairsWanted} chairs in the waiting room", (sim, _) => ChairsInWaitingRoom(sim) >= ChairsWanted) },
                Pointer = (_, s) => BuildPointer(s, "emergency", "waiting_chairs"),
                Suggested = Spots.Chairs, SuggestedLabel = "Chairs",
            },
            new()
            {
                Id = "triage", Part = Treat, Title = "Build a triage room",
                Text = "After booking in, a nurse checks each patient's observations and gives them a colour: Green for minor problems, Amber for patients who need a closer eye, Red for the sickest. Build a triage room with its door on the corridor.",
                Tasks = new TutorialTask[] { new("Build a triage room off the corridor", (sim, _) => InUse(sim, CareSpaceKind.Triage) > 0) },
                Pointer = (_, s) => BuildPointer(s, "emergency", "triage_room"),
                Suggested = Spots.Triage, SuggestedLabel = "Triage",
                Tip = "A room's door goes on the corridor by itself. A room whose door doesn't reach a corridor shows a red Not in use sign.",
            },
            new()
            {
                Id = "minors", Part = Treat, Title = "Build two minors cubicles",
                Text = "Green patients are seen in minors: a doctor examines them, nurses give the treatment, and most go home. Build two minors cubicles along the corridor.",
                Tasks = new TutorialTask[] { new("Two minors beds off the corridor", (sim, _) => InUse(sim, CareSpaceKind.Minors) >= 2) },
                Pointer = (_, s) => BuildPointer(s, "emergency", "minors_cubicle"),
                Suggested = new GridRect(Spots.Minors1.X, Spots.Minors1.Y, Spots.Minors2.Right - Spots.Minors1.X, Spots.Minors1.Length), SuggestedLabel = "Minors",
            },
            new()
            {
                Id = "majors", Part = Treat, Title = "Build a majors cubicle",
                Text = "Amber patients are more unwell: they go to a majors cubicle, with a monitor and a drip stand. Build one next to the minors cubicles.",
                Tasks = new TutorialTask[] { new("A majors cubicle off the corridor", (sim, _) => InUse(sim, CareSpaceKind.Majors) > 0) },
                Pointer = (_, s) => BuildPointer(s, "emergency", "majors_cubicle"),
                Suggested = Spots.Majors, SuggestedLabel = "Majors",
                Tip = "Red patients need a resus bay. The tutorial only sends Green and Amber patients; once it ends, Red patients come too.",
            },
            new()
            {
                Id = "recruitment", Part = Team, Title = "Open Recruitment",
                Text = "Rooms come empty: you hire the staff. Click Hire on the left, or press H. New candidates come every few hours, each with a grade, a skill and a salary.",
                Tasks = new TutorialTask[] { new("Open the Recruitment panel", (_, s) => s.RecruitmentOpen) },
                Pointer = (_, s) => s.RecruitmentOpen ? null : "nav:Hire",
            },
            new()
            {
                Id = "hire", Part = Team, Title = "Hire a day and a night team",
                Text = "A&E runs day and night, so each shift needs a receptionist, a nurse and a doctor. Hire them one by one, or press Hire minimum team to fill every post the department needs.",
                Tasks = new TutorialTask[]
                {
                    new("Receptionists for days and nights", (sim, _) => DayAndNight(sim, StaffRole.Receptionist)),
                    new("Nurses for days and nights", (sim, _) => DayAndNight(sim, StaffRole.Nurse)),
                    new("Doctors for days and nights", (sim, _) => DayAndNight(sim, StaffRole.Doctor)),
                },
                Pointer = (_, s) => s.RecruitmentOpen ? "button:hire_team" : "nav:Hire",
                Tip = "The Staff rota (T) shows who works which shift, and where there are gaps.",
            },
            new()
            {
                Id = "open", Part = Open, Title = "Open the doors",
                Text = "A&E opens once it has everything it needs. Switch back to Play mode and set the clock going: patients will start to walk in.",
                Tasks = new TutorialTask[]
                {
                    new("Switch to Play mode", (_, s) => !s.BuildMode),
                    new("Set the clock going", (_, s) => s.ClockRunning),
                    new("A&E is open", (sim, _) => sim.IsEmergencyOpen),
                },
                Pointer = (sim, s) => s.BuildMode ? "mode:Play" : !s.ClockRunning ? "speed:Normal" : null,
            },
            new()
            {
                Id = "arrive", Part = Open, Title = "Your first patient",
                Text = "Patients walk in through the main entrance, queue at reception, and sit in the waiting room until triage. The faster speeds help if you don't want to wait.",
                Tasks = new TutorialTask[]
                {
                    new("A patient walks in", (sim, _) => FirstPatientOf(sim) is not null),
                    new("They book in at reception", (sim, _) => FirstPatientOf(sim) is { Stage: not (PatientStage.WalkingIn or PatientStage.WaitingToRegister) }),
                    new("A nurse triages them", (sim, _) => FirstPatientOf(sim) is { Triage: not null }),
                },
                Pointer = (_, s) => s.ClockFast ? null : "speed:Fast",
            },
            new()
            {
                Id = "record", Part = Open, Title = "Look at their record",
                Text = "Click on a patient to open their record: their observations, triage colour, what they are waiting for, and their journey so far.",
                Tasks = new TutorialTask[] { new("Click a patient", (_, s) => s.PatientRecordOpen) },
                Pointer = (_, _) => "patient",
            },
            new()
            {
                Id = "treat", Part = Open, Title = "Seen, treated and home",
                Text = "The patient goes to a cubicle, a doctor sees them and decides what is wrong, the nurses give the treatment, and they go home. Keep an eye on the four-hour clock: everyone should leave A&E within four hours.",
                Tasks = new TutorialTask[]
                {
                    new("A doctor sees them", (sim, _) => FirstPatientOf(sim) is { } p && (p.FirstSeenByDoctorAt is not null || !p.IsInEmergency)),
                    new("They leave A&E", (sim, _) => FirstPatientOf(sim) is { IsInEmergency: false }),
                },
                Pointer = (_, s) => s.ClockFast ? null : "speed:Fast",
            },
            new()
            {
                Id = "done", Part = Open, Title = "Your A&E is running",
                Text = "Well done: you built an A&E from nothing and treated your first patient. From here, try:\n• a resus bay for Red patients\n• a staff room, so staff can take their breaks\n• toilets and a vending machine for the waiting room\n• an X-ray room, so A&E can scan broken bones\n\nPress Keep playing to carry on as a normal hospital: from then on every kind of patient comes, including Red patients by ambulance.",
            },
        };
    }

    private static Patient? FirstPatientOf(HospitalSimulation sim) => sim.TutorialFirstPatient;
}
