using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Tests;

/// <summary>Waiting rooms the player builds and fits out, waiting chairs, and where patients wait.</summary>
public class WaitingRoomTests
{
    private static readonly GridRect B = StarterSite.MainBuildingFootprint;

    /// <summary>The waiting room's spot: in front of A&amp;E, over the front path, joined to the A&amp;E corridor.</summary>
    private static readonly GridRect Spot = new(B.X + 15, B.Y + 26, 22, 14);

    /// <summary>Middle of the front wall, where the path comes in.</summary>
    private static readonly GridCoord EntranceTile = new(B.X + 26, B.Bottom - 1);

    /// <summary>Middle of the north wall, onto the corridor coming down from A&amp;E.</summary>
    private static readonly GridCoord InnerDoorTile = new(B.X + 26, B.Y + 26);

    private static readonly GridCoord DeskTile = new(B.X + 30, B.Y + 32);

    /// <summary>A new game's A&amp;E with an empty waiting room built in the usual place.</summary>
    private static (SimulationHarness H, CareSpace Room) EmptyWaitingRoom(int seed = 1234)
    {
        var h = new SimulationHarness(seed, waitingRoom: false);
        Assert.Empty(h.Sim.SpacesOfKind(CareSpaceKind.Waiting));
        var plan = h.Sim.AutoPlan("waiting_room", Spot);
        var check = h.Sim.BuildRoom(plan);
        Assert.True(check.CanBuild, string.Join("; ", check.Problems));
        return (h, h.Sim.SpacesOfKind(CareSpaceKind.Waiting).Single());
    }

    /// <summary>Puts the desk and both doors into a waiting room in the usual place.</summary>
    internal static void FitOut(SimulationHarness h, CareSpace room)
    {
        var desk = h.Sim.PlaceReceptionDesk(room.Id, DeskTile, WallSide.South);
        Assert.True(desk.CanBuild, string.Join("; ", desk.Problems));
        var entrance = h.Sim.PlaceWaitingDoor(room.Id, EntranceTile);
        Assert.True(entrance.CanBuild, string.Join("; ", entrance.Problems));
        var inner = h.Sim.PlaceWaitingDoor(room.Id, InnerDoorTile);
        Assert.True(inner.CanBuild, string.Join("; ", inner.Problems));
    }

    private static CareSpace Room(SimulationHarness h) => h.Sim.SpacesOfKind(CareSpaceKind.Waiting).Single();

    [Fact]
    public void A_new_game_has_no_waiting_room_and_A_and_E_waits_for_one()
    {
        var h = new SimulationHarness(waitingRoom: false);
        Assert.Empty(h.Sim.SpacesOfKind(CareSpaceKind.Waiting));
        Assert.Null(h.Sim.MainWaitingRoom);
        Assert.False(h.Sim.IsEmergencyOpen);
        Assert.Equal(new[] { "a waiting room" }, h.Sim.ClosedReasons);
    }

    [Fact]
    public void A_waiting_room_is_built_empty_then_fitted_out_and_A_and_E_opens()
    {
        var (h, room) = EmptyWaitingRoom();
        Assert.Empty(room.Doors);
        Assert.DoesNotContain(room.Furniture, f => f.ItemId == "reception_desk");
        Assert.False(h.Sim.IsEmergencyOpen);

        var before = h.Sim.Balance;
        FitOut(h, Room(h));
        room = Room(h);
        Assert.Equal(before - h.Sim.ReceptionDeskCost - HospitalSimulation.EntranceDoorCost - HospitalSimulation.InnerDoorCost, h.Sim.Balance);
        Assert.Contains(room.Furniture, f => f.ItemId == "reception_desk");
        Assert.True(h.Sim.HasEntranceDoor(room));
        Assert.True(h.Sim.HasInnerDoor(room));
        Assert.Same(room, h.Sim.MainWaitingRoom);
        Assert.True(h.Sim.IsEmergencyOpen, string.Join(", ", h.Sim.ClosedReasons));

        // The receptionist works behind the desk; the queue runs out in front of it.
        Assert.Equal(new GridCoord(DeskTile.X, DeskTile.Y - 1), room.StaffSpot);
        Assert.Equal(new GridCoord(DeskTile.X, DeskTile.Y + 1), room.QueueTiles[0]);
        h.RunFor(TimeSpan.FromMinutes(5));
        Assert.Contains(h.Sim.Staff, s => s.Role == StaffRole.Receptionist && s.Tile == room.StaffSpot);
    }

    [Fact]
    public void The_entrance_door_must_be_on_the_front_path()
    {
        var (h, room) = EmptyWaitingRoom();

        // The west wall looks onto empty floor, not a corridor or the path.
        var west = h.Sim.CheckWaitingDoor(room.Id, new GridCoord(Spot.X, Spot.Y + 7));
        Assert.Contains(west.Problems, p => p.Contains("front path"));

        // Along the front wall but off the path, where there is no way out.
        var offPath = h.Sim.CheckWaitingDoor(room.Id, new GridCoord(Spot.X + 3, Spot.Bottom - 1));
        Assert.Contains(offPath.Problems, p => p.Contains("front path"));

        Assert.True(h.Sim.CheckWaitingDoor(room.Id, EntranceTile).CanBuild);
        Assert.True(h.Sim.PlaceWaitingDoor(room.Id, EntranceTile).CanBuild);
        Assert.Contains(h.Sim.CheckWaitingDoor(room.Id, EntranceTile).Problems, p => p.Contains("already a door"));

        // Taking a door out refunds half of it.
        var before = h.Sim.Balance;
        Assert.True(h.Sim.RemoveWaitingDoor(room.Id, EntranceTile).CanBuild);
        Assert.Equal(before + (HospitalSimulation.EntranceDoorCost / 2), h.Sim.Balance);
        Assert.Empty(Room(h).Doors);
    }

    [Fact]
    public void The_desk_must_fit_with_room_for_the_receptionist_and_a_queue()
    {
        var (h, room) = EmptyWaitingRoom();

        // Against the back wall facing out: the receptionist would be outside the room.
        var check = h.Sim.CheckReceptionDesk(room.Id, new GridCoord(Spot.X + 10, Spot.Y), WallSide.South);
        Assert.Contains(check.Problems, p => p.Contains("inside the room"));

        Assert.True(h.Sim.PlaceReceptionDesk(room.Id, DeskTile, WallSide.South).CanBuild);

        // Moving it again is free.
        var before = h.Sim.Balance;
        Assert.True(h.Sim.PlaceReceptionDesk(room.Id, new GridCoord(Spot.X + 6, Spot.Y + 6), WallSide.East).CanBuild);
        Assert.Equal(before, h.Sim.Balance);
        Assert.Equal(new GridCoord(Spot.X + 7, Spot.Y + 6), Room(h).PatientSpot);
    }

    [Fact]
    public void Chairs_go_in_waiting_rooms_and_corridors_but_not_doorways_queues_or_other_rooms()
    {
        var (h, room) = EmptyWaitingRoom();
        FitOut(h, room);
        room = Room(h);
        var colour = ChairColours.Default;

        Assert.True(h.Sim.CheckChair(new GridCoord(Spot.X + 2, Spot.Y + 4), WallSide.South, colour).CanBuild);
        Assert.True(h.Sim.CheckChair(new GridCoord(B.X + 10, B.Y + 12), WallSide.South, colour).CanBuild);

        Assert.Contains(h.Sim.CheckChair(room.QueueTiles[1], WallSide.South, colour).Problems, p => p.Contains("reception desk"));
        Assert.Contains(h.Sim.CheckChair(new GridCoord(EntranceTile.X, EntranceTile.Y), WallSide.South, colour).Problems, p => p.Contains("doorway"));
        Assert.Contains(h.Sim.CheckChair(new GridCoord(InnerDoorTile.X, InnerDoorTile.Y - 1), WallSide.South, colour).Problems, p => p.Contains("doorway"));
        var cubicle = h.Sim.Space("minors_1");
        Assert.Contains(h.Sim.CheckChair(new GridCoord(cubicle.Rect.X + 2, cubicle.Rect.Y + 2), WallSide.South, colour).Problems, p => p.Contains("waiting room or along a corridor"));

        // A new chair lines up with the one beside it; next to a wall it has its back to the wall.
        Assert.True(h.Sim.PlaceChair(new GridCoord(Spot.X + 2, Spot.Y + 4), WallSide.East, colour).CanBuild);
        Assert.Equal(WallSide.East, h.Sim.SuggestChairFacing(new GridCoord(Spot.X + 2, Spot.Y + 5)));
        Assert.Equal(WallSide.East, h.Sim.SuggestChairFacing(new GridCoord(Spot.X, Spot.Y + 9)));

        // Against a wall the back goes to the wall, even beside a chair facing along the wall.
        Assert.True(h.Sim.PlaceChair(new GridCoord(Spot.X, Spot.Y + 10), WallSide.South, colour).CanBuild);
        Assert.Equal(WallSide.East, h.Sim.SuggestChairFacing(new GridCoord(Spot.X, Spot.Y + 9)));
        Assert.Equal(WallSide.East, h.Sim.SuggestChairFacing(new GridCoord(Spot.X, Spot.Y + 11)));

        // In a corner it follows the chairs beside it, as long as that keeps its back to a wall.
        Assert.True(h.Sim.PlaceChair(new GridCoord(Spot.X + 1, Spot.Y), WallSide.South, colour).CanBuild);
        Assert.Equal(WallSide.South, h.Sim.SuggestChairFacing(new GridCoord(Spot.X, Spot.Y)));

        // Chairs stand clear of the walls beside them; out in the open they stand in the middle of their tile.
        Assert.Equal((HospitalSimulation.ChairWallGap, 0.0), h.Sim.ChairShift(new GridCoord(Spot.X, Spot.Y + 9)));
        Assert.Equal((HospitalSimulation.ChairWallGap, HospitalSimulation.ChairWallGap), h.Sim.ChairShift(new GridCoord(Spot.X, Spot.Y)));
        Assert.Equal((0.0, 0.0), h.Sim.ChairShift(new GridCoord(Spot.X + 2, Spot.Y + 4)));
    }

    [Fact]
    public void Without_chairs_patients_waiting_for_triage_stand_in_the_waiting_room()
    {
        var (h, room) = EmptyWaitingRoom();
        FitOut(h, room);
        room = Room(h);

        // Keep triage busy so they have to wait.
        foreach (var triage in h.Sim.SpacesOfKind(CareSpaceKind.Triage))
        {
            triage.ReservedByStaffId = 999;
        }

        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn);
        h.RunUntil(() => patient.Stage == PatientStage.WaitingForTriage && !patient.IsMoving, TimeSpan.FromMinutes(30), "the patient registering and finding somewhere to wait");
        Assert.Null(patient.Seat);
        Assert.True(room.Rect.Contains(patient.Tile), $"{patient.Tile} is not in the waiting room");
        Assert.DoesNotContain(patient.Tile, room.QueueTiles);
    }

    [Fact]
    public void Before_triage_patients_sit_in_the_waiting_room_and_afterwards_near_where_they_are_going()
    {
        var h = new SimulationHarness();
        var room = Room(h);
        foreach (var triage in h.Sim.SpacesOfKind(CareSpaceKind.Triage))
        {
            triage.ReservedByStaffId = 999;
        }

        // A corridor chair right outside triage must not be used before triage: that waiting happens in the waiting room.
        var outsideTriage = new GridCoord(B.X + 23, B.Y + 19);
        Assert.True(h.Sim.PlaceChair(outsideTriage, WallSide.West, ChairColours.Default).CanBuild, string.Join("; ", h.Sim.CheckChair(outsideTriage, WallSide.West, ChairColours.Default).Problems));

        var patient = h.Sim.SpawnPatient(ArrivalMethod.WalkIn);
        h.RunUntil(() => patient.Stage == PatientStage.WaitingForTriage && !patient.IsMoving, TimeSpan.FromMinutes(30), "the patient to sit down for triage");
        Assert.NotNull(patient.Seat);
        Assert.True(room.Rect.Contains(patient.Seat!.Value));

        // Every cubicle is full, so after triage they wait: on a chair beside the cubicles, not back in the waiting room.
        foreach (var space in h.Sim.Spaces.Where(s => s.IsTreatmentSpace))
        {
            space.OccupantId = 999;
        }

        var nearCubicles = new[] { new GridCoord(B.X + 6, B.Y + 10), new GridCoord(B.X + 14, B.Y + 10), new GridCoord(B.X + 30, B.Y + 10), new GridCoord(B.X + 38, B.Y + 10), new GridCoord(B.X + 48, B.Y + 10) };
        foreach (var tile in nearCubicles)
        {
            Assert.True(h.Sim.PlaceChair(tile, WallSide.North, ChairColours.Default).CanBuild, string.Join("; ", h.Sim.CheckChair(tile, WallSide.North, ChairColours.Default).Problems));
        }

        foreach (var triage in h.Sim.SpacesOfKind(CareSpaceKind.Triage))
        {
            triage.ReservedByStaffId = null;
        }

        h.RunUntil(() => patient.Stage == PatientStage.WaitingForSpace && patient.BusyWithTaskId is null && !patient.IsMoving, TimeSpan.FromHours(1), "the patient to be triaged and sit down again");
        Assert.NotNull(patient.Seat);
        Assert.Contains(patient.Seat!.Value, nearCubicles);
        Assert.Equal(patient.Seat, patient.Tile);
    }

    [Fact]
    public void Clinic_patients_sit_on_the_free_corridor_chair_nearest_their_clinic()
    {
        var h = new SimulationHarness(seed: 3);
        h.Sim.AdjustBalance(2_000_000);
        Assert.True(h.Sim.ExtendBuilding(new GridRect(B.X - 30, B.Y, 30, 34)).CanBuild);
        Assert.True(h.Sim.BuildRoom(new RoomPlan("corridor", new GridRect(B.X - 30, B.Y + 9, 34, 8), WallSide.South)).CanBuild);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("outpatient_reception", new GridRect(B.X - 30, B.Y + 17, 7, 6))).CanBuild);
        var reception = h.Sim.SpacesOfKind(CareSpaceKind.OutpatientReception).Single();
        Assert.Empty(reception.Seats);
        Assert.True(h.Sim.BuildRoom(h.Sim.AutoPlan("medical_clinic", new GridRect(B.X - 14, B.Y + 17, 8, 7))).CanBuild);
        var clinic = h.Sim.Spaces.Single(s => s.TemplateId == "medical_clinic");
        h.Sim.HireStaff(StaffRole.Receptionist, reception.Id, skill: 0.7);
        h.Sim.HireStaff(StaffRole.Doctor, clinic.Id, skill: 0.7);

        var near = new GridCoord(B.X - 6, B.Y + 15);
        var far = new GridCoord(B.X - 28, B.Y + 10);
        Assert.True(h.Sim.PlaceChair(near, WallSide.North, ChairColours.Default).CanBuild);
        Assert.True(h.Sim.PlaceChair(far, WallSide.South, ChairColours.Default).CanBuild);

        h.RunUntil(() => h.Sim.Outpatients.Any(o => o.Stage == OutpatientStage.Waiting && o.Seat is not null), TimeSpan.FromHours(3), "a clinic patient sitting down");
        Assert.Equal(near, h.Sim.Outpatients.First(o => o.Stage == OutpatientStage.Waiting && o.Seat is not null).Seat);
    }

    [Fact]
    public void Desk_doors_and_chairs_are_saved_and_loaded()
    {
        var (h, room) = EmptyWaitingRoom();
        FitOut(h, room);
        var plum = ChairColours.All[3].Hex;
        h.Sim.PlaceChair(new GridCoord(Spot.X + 2, Spot.Y + 4), WallSide.East, plum);
        h.Sim.PlaceChair(new GridCoord(B.X + 10, B.Y + 12), WallSide.North, ChairColours.Default);

        var save = SaveSerializer.Deserialize(SaveSerializer.Serialize(new SaveGame { Layout = h.Sim.CaptureLayout() }));
        var to = new SimulationHarness(waitingRoom: false);
        to.Sim.RestoreLayout(save.Layout!);

        var loaded = Room(to);
        Assert.Equal(Room(h).Doors, loaded.Doors);
        Assert.Equal(Room(h).StaffSpot, loaded.StaffSpot);
        Assert.Equal(Room(h).QueueTiles, loaded.QueueTiles);
        Assert.Equal(h.Sim.Chairs.OrderBy(c => c.Tile.X).ThenBy(c => c.Tile.Y), to.Sim.Chairs.OrderBy(c => c.Tile.X).ThenBy(c => c.Tile.Y));
        Assert.True(to.Sim.IsEmergencyOpen, string.Join(", ", to.Sim.ClosedReasons));
    }

    [Fact]
    public void An_old_save_keeps_its_waiting_room_desk_doors_and_seats()
    {
        // As written before waiting rooms were fitted by hand: no desk or doors, and corridor chairs as bare tiles.
        var h = new SimulationHarness(waitingRoom: false);
        var layout = h.Sim.CaptureLayout();
        var (w, d) = RoomLayoutEngine.LocalSize(Spot, WallSide.North);
        var old = new LayoutState
        {
            Balance = layout.Balance,
            Rooms = layout.Rooms.Append(new RoomState("waiting_room", "Waiting room", "waiting_room", Spot.X, Spot.Y, w, d, WallSide.North)).ToList(),
            CorridorChairs = new[] { new GridCoord(B.X + 10, B.Y + 10) },
        };

        var to = new SimulationHarness(waitingRoom: false);
        to.Sim.RestoreLayout(old);
        var room = Room(to);
        Assert.Same(room, to.Sim.MainWaitingRoom);
        Assert.Contains(room.Furniture, f => f.ItemId == "reception_desk");
        Assert.True(to.Sim.HasEntranceDoor(room));
        Assert.True(to.Sim.HasInnerDoor(room));
        Assert.True(to.Sim.Chairs.Count(c => room.Rect.Contains(c.Tile)) > 20);
        Assert.Equal(WallSide.South, to.Sim.ChairAt(new GridCoord(B.X + 10, B.Y + 10))!.Facing);
        Assert.True(to.Sim.IsEmergencyOpen, string.Join(", ", to.Sim.ClosedReasons));
    }
}
