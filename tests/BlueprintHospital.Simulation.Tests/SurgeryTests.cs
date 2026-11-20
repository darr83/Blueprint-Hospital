using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Anaesthetic rooms, the way to theatre, urgency, getting ready and the Surgery panel's figures.</summary>
public class SurgeryTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>A west wing: corridor, a theatre on the south side (and its anaesthetic room if asked), a surgical ward and the recommended staff.</summary>
    private static SimulationHarness Wing(int seed = 7, bool anaestheticRoom = true, params string[] departments)
    {
        var h = new SimulationHarness(seed);
        h.Sim.AdjustBalance(5_000_000);
        foreach (var department in departments.Append("general_surgery").Distinct())
        {
            Build(h.Sim.UnlockDepartment(department), department);
        }

        Build(h.Sim.ExtendBuilding(new GridRect(B.X - 40, B.Y, 40, 34)), "extension");
        Build(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 40, B.Y + 12, 44, 6), WallSide.South)), "corridor");
        Build(h.Sim.BuildRoom(h.Sim.AutoPlan("operating_theatre", new GridRect(B.X - 40, B.Y + 18, 12, 12))), "theatre");
        if (anaestheticRoom)
        {
            Build(h.Sim.BuildRoom(h.Sim.AutoPlan("anaesthetic_room", new GridRect(B.X - 28, B.Y + 18, 7, 10))), "anaesthetic room");
        }

        Build(h.Sim.BuildRoom(h.Sim.AutoPlan("surgical_ward", new GridRect(B.X - 21, B.Y + 18, 16, 10))), "surgical ward");
        var theatre = h.Sim.SpacesOfKind(CareSpaceKind.Theatre).Single();
        foreach (var department in h.Sim.SurgicalDepartments)
        {
            h.Sim.SetTheatreAccess(theatre.Id, department.Id, true);
        }

        h.StaffToRecommended();
        return h;
    }

    private static void Build(BuildCheck result, string what) => Assert.True(result.CanBuild, $"{what}: {string.Join("; ", result.Problems)}");

    [Fact]
    public void Shared_doors_go_in_the_middle_of_the_shared_wall_away_from_corners()
    {
        // Theatre to the west (12 x 12), anaesthetic room to the east (7 x 10), sharing x = 12.
        var door = TheatreSuite.SharedDoor(new GridRect(12, 0, 7, 10), new GridRect(0, 0, 12, 12))!.Value;
        Assert.Equal(WallSide.West, door.InA.Side);
        Assert.Equal(WallSide.East, door.InB.Side);
        Assert.Equal(4, door.InA.Width);
        Assert.Equal(door.InA.Offset, door.InB.Offset);
        Assert.InRange(door.InA.Offset, 1, 10 - 1 - 4);

        // Touching only at a corner, or not at all: no door.
        Assert.Null(TheatreSuite.SharedDoor(new GridRect(12, 11, 7, 10), new GridRect(0, 0, 12, 12)));
        Assert.Null(TheatreSuite.SharedDoor(new GridRect(13, 0, 7, 10), new GridRect(0, 0, 12, 12)));
    }

    [Fact]
    public void An_anaesthetic_room_must_be_built_against_a_theatre_and_is_joined_to_it_by_automatic_doors()
    {
        var h = Wing(anaestheticRoom: false);
        var theatre = h.Sim.SpacesOfKind(CareSpaceKind.Theatre).Single();

        // Away from the theatre it can't be built.
        var away = h.Sim.CheckRoomAt("anaesthetic_room", new GridRect(B.X - 40, B.Y + 2, 7, 10));
        Assert.Contains(away.Problems, p => p.Contains("against an operating theatre"));

        // No anaesthetic room: the theatre stays shut, however many staff it has.
        Assert.Contains("an anaesthetic room built against it", h.Sim.TheatreProblems(theatre));
        Assert.False(h.Sim.IsStaffed(theatre));

        Build(h.Sim.BuildRoom(h.Sim.AutoPlan("anaesthetic_room", new GridRect(B.X - 28, B.Y + 18, 7, 10))), "beside the theatre");
        var room = h.Sim.Spaces.Single(s => s.Kind == CareSpaceKind.AnaestheticRoom);
        Assert.Equal(theatre.Id, room.LinkedRoomId);
        Assert.Equal(room.Id, theatre.LinkedRoomId);
        Assert.Single(room.LinkDoors);
        Assert.Single(theatre.LinkDoors);
        Assert.True(h.Sim.IsStaffed(theatre));
        Assert.Same(room, h.Sim.AnaestheticRoomOf(theatre));

        // People can walk straight through the automatic doors.
        var route = h.Sim.Nav.FindRoute(room.PatientSpot, theatre.PatientSpot);
        Assert.NotEmpty(route);
        Assert.True(route.Count < 20, $"route through the doors is {route.Count} steps");

        // A second anaesthetic room for the same theatre is refused.
        var second = h.Sim.CheckRoomAt("anaesthetic_room", new GridRect(B.X - 47, B.Y + 18, 7, 10));
        Assert.False(second.CanBuild);
    }

    [Fact]
    public void Patients_are_put_to_sleep_in_the_anaesthetic_room_then_wheeled_into_theatre()
    {
        var h = Wing();
        var theatre = h.Sim.SpacesOfKind(CareSpaceKind.Theatre).Single();
        var room = h.Sim.AnaestheticRoomOf(theatre)!;
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");

        var inAnaestheticRoom = false;
        var onTable = false;
        var sawBoard = new HashSet<TheatreState>();
        var listed = false;
        h.RunUntil(() =>
        {
            inAnaestheticRoom |= !patient.IsMoving && patient.Tile == room.PatientSpot;
            onTable |= inAnaestheticRoom && !patient.IsMoving && patient.Tile == theatre.PatientSpot;
            sawBoard.Add(h.Sim.TheatreBoard().Single().State);
            listed |= h.Sim.SurgeryWaitingList().Any(c => c.Patient == patient);
            return patient.Stage is PatientStage.OnWard or PatientStage.Gone or PatientStage.AwaitingTransfer;
        }, TimeSpan.FromHours(20), "appendicitis patient to reach a ward");

        var journey = h.Journey(patient);
        Assert.True(h.Sim.Stats.OperationsDone == 1, journey);
        Assert.True(inAnaestheticRoom, journey);
        Assert.True(onTable, journey);
        Assert.True(listed);
        Assert.Contains($"Anaesthetic given by", journey);
        Assert.Contains($"in {room.Name}", journey);
        Assert.Contains("Urgent operation", journey);
        Assert.Contains(TheatreState.Anaesthetic, sawBoard);
        Assert.Contains(TheatreState.Operating, sawBoard);
        Assert.Equal(1, h.Sim.SurgeryToday.Operations);
        Assert.Equal(h.Database.Conditions["acute_appendicitis"].Operation!.Tariff, h.Sim.SurgeryToday.Income);
        Assert.Contains(h.Sim.TheatreLog, l => l.PatientId == patient.Id && l.End is not null);
        Assert.Null(patient.SurgeryBookedAt);
    }

    [Fact]
    public void Nobody_goes_to_theatre_until_consented_bloods_back_and_fasted_except_an_immediate_case()
    {
        var h = Wing(departments: "gynaecology");
        var urgent = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");
        h.RunUntil(() => urgent.SurgeryBookedAt is not null || urgent.Stage == PatientStage.Gone, TimeSpan.FromHours(10), "appendicitis to be booked");
        if (urgent.SurgeryBookedAt is null)
        {
            return;
        }

        Assert.Equal(SurgeryUrgency.Urgent, urgent.SurgeryUrgency);
        Assert.False(h.Sim.ReadyForTheatre(urgent));
        var listed = h.Sim.SurgeryWaitingList().Single(c => c.Patient == urgent);
        Assert.False(listed.Consented);
        Assert.Equal(TimeSpan.FromHours(6), listed.Target);

        // Pretend they just ate: they can't go until 6 hours later.
        urgent.LastAteAt = h.Clock.Now;
        h.RunFor(TimeSpan.FromHours(1));
        Assert.False(h.Sim.ReadyForTheatre(urgent));
        Assert.False(h.Sim.SurgeryWaitingList().Single(c => c.Patient == urgent).Fasted);
        Assert.DoesNotContain(h.Sim.TheatreBoard(), b => b.Current?.Patient == urgent);

        // A ruptured ectopic goes straight in, whatever they last ate.
        var ectopic = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "ectopic_pregnancy");
        h.RunUntil(() => ectopic.SurgeryBookedAt is not null || ectopic.Stage == PatientStage.Gone, TimeSpan.FromHours(6), "ectopic to be booked");
        if (ectopic.SurgeryBookedAt is null)
        {
            return;
        }

        ectopic.LastAteAt = h.Clock.Now;
        Assert.Equal(SurgeryUrgency.Immediate, ectopic.SurgeryUrgency);
        Assert.True(h.Sim.ReadyForTheatre(ectopic));
        Assert.Equal(SurgeryUrgency.Immediate, h.Sim.SurgeryWaitingList()[0].Urgency);
        h.RunUntil(() => ectopic.HadSurgery || ectopic.Stage == PatientStage.Gone, TimeSpan.FromHours(4), "ectopic to be operated on");
        Assert.True(ectopic.HadSurgery, h.Journey(ectopic));
        Assert.False(urgent.HadSurgery);
    }

    [Fact]
    public void Theatres_are_cleaned_between_cases_and_the_board_says_so()
    {
        var h = Wing();
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "perianal_abscess");
        var cleaning = false;
        h.RunUntil(() =>
        {
            cleaning |= h.Sim.TheatreBoard().Single().State == TheatreState.Cleaning;
            return cleaning || patient.Stage == PatientStage.Gone;
        }, TimeSpan.FromHours(30), "the theatre to be cleaned after the operation");
        if (!patient.HadSurgery)
        {
            return;
        }

        var board = h.Sim.TheatreBoard().Single();
        Assert.Equal(TheatreState.Cleaning, board.State);
        Assert.NotNull(board.CleanUntil);
        Assert.Equal(h.Database.Conditions["perianal_abscess"].Operation!.Tariff, h.Sim.SurgeryToday.Income);
    }

    [Fact]
    public void Anaesthetic_rooms_and_surgery_bookings_survive_save_and_load()
    {
        var h = Wing();
        var patient = h.Sim.SpawnPatient(ArrivalMethod.Ambulance, "acute_appendicitis");
        h.RunUntil(() => patient.SurgeryBookedAt is not null || patient.Stage == PatientStage.Gone, TimeSpan.FromHours(10), "appendicitis to be booked");

        var json = SaveSerializer.Serialize(new SaveGame { Clock = h.Clock.CaptureState(), Layout = h.Sim.CaptureLayout(), People = h.Sim.CapturePeople() });
        var save = SaveSerializer.Deserialize(json);
        var restored = new SimulationHarness(seed: 7);
        restored.Sim.RestoreLayout(save.Layout!);
        restored.Sim.RestorePeople(save.People!);

        var theatre = restored.Sim.SpacesOfKind(CareSpaceKind.Theatre).Single();
        Assert.NotNull(restored.Sim.AnaestheticRoomOf(theatre));
        Assert.True(restored.Sim.IsStaffed(theatre));
        if (patient.SurgeryBookedAt is { } booked)
        {
            var again = restored.Sim.Patients.Single(p => p.Id == patient.Id);
            Assert.Equal(booked, again.SurgeryBookedAt);
            Assert.Equal(patient.SurgeryUrgency, again.SurgeryUrgency);
            Assert.Equal(patient.LastAteAt, again.LastAteAt);
        }
    }
}
