using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Transport;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// Walk-in patients who come by car or taxi: some park in the car park and walk in, some are dropped at
/// the taxi rank by a relative or a taxi. Everyone else arrives on foot as before.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>Of walk-in patients: how many drive and park, are dropped off, or take a taxi.</summary>
    public const double ParksShare = 0.30;

    public const double DropOffShare = 0.12;

    public const double TaxiShare = 0.15;

    /// <summary>A car or taxi at the rank lets its passenger out, then waits this long before pulling away.</summary>
    private static readonly TimeSpan RankDwell = TimeSpan.FromMinutes(2);

    /// <summary>A parked car whose owner has been admitted to a ward is fetched by a relative after this long.</summary>
    private static readonly TimeSpan CollectedAfterAdmission = TimeSpan.FromMinutes(30);

    /// <summary>Patients aged this or over use the blue badge spaces.</summary>
    private const int BlueBadgeAge = 70;

    private readonly List<CarVehicle> _cars = new();
    private GameRandom _carRandom = new(0);
    private int _nextCarId = 1;

    /// <summary>Visitors' cars and taxis on the site.</summary>
    public IReadOnlyList<CarVehicle> Cars => _cars;

    /// <summary>The marked car park spaces, nearest the front path first.</summary>
    public IReadOnlyList<GridRect> ParkingBays => Site.Layout?.ParkingBays ?? Array.Empty<GridRect>();

    /// <summary>Where taxis (and cars dropping someone off) stop.</summary>
    public IReadOnlyList<GridCoord> TaxiStands => Site.Layout?.TaxiStands ?? Array.Empty<GridCoord>();

    private AmbulanceLane? CarLane => Site.Layout?.CarLane;

    /// <summary>
    /// Decides how a new walk-in patient gets here. Returns true when they are coming by car or taxi: the
    /// vehicle is on its way and they join the hospital when it stops. False: they walk in now.
    /// </summary>
    private bool TryArriveByCar(Patient patient)
    {
        if (CarLane is null || !_options.CarArrivals)
        {
            return false;
        }

        var roll = _carRandom.NextDouble();
        var trip = roll < ParksShare ? CarTrip.Parks
            : roll < ParksShare + DropOffShare ? CarTrip.DropOff
            : roll < ParksShare + DropOffShare + TaxiShare ? CarTrip.Taxi
            : (CarTrip?)null;
        if (trip is null)
        {
            return false;
        }

        var slot = trip == CarTrip.Parks ? FreeParkingBay(patient) : -1;
        if (trip == CarTrip.Parks && slot < 0)
        {
            // Car park full: someone drops them at the door instead.
            trip = CarTrip.DropOff;
        }

        if (trip != CarTrip.Parks)
        {
            slot = FreeTaxiStand();
            if (slot < 0)
            {
                return false;
            }
        }

        SendCar(patient, trip.Value, slot);
        return true;
    }

    /// <summary>Brings <paramref name="patient"/> in by car or taxi (for scenarios and tests). False when there is nowhere for it to stop.</summary>
    public bool SpawnCarArrival(Patient patient, CarTrip trip)
    {
        if (CarLane is null)
        {
            return false;
        }

        var slot = trip == CarTrip.Parks ? FreeParkingBay(patient) : FreeTaxiStand();
        if (slot < 0)
        {
            return false;
        }

        SendCar(patient, trip, slot);
        return true;
    }

    /// <summary>A new walk-in patient who has not arrived yet, for <see cref="SpawnCarArrival"/>.</summary>
    public Patient GenerateWalkIn(string? conditionId = null) => _generator.Generate(Now, ArrivalMethod.WalkIn, conditionId);

    private void SendCar(Patient patient, CarTrip trip, int slot)
    {
        var car = new CarVehicle { Id = _nextCarId++, Trip = trip, PatientId = patient.Id, Passenger = patient, Slot = slot };
        car.PlaceAt(CarLane!.Entry);
        _cars.Add(car);
        DriveIn(car);
    }

    /// <summary>A space is out of use if it has a car in it or heading for it, or the ground is no longer car park.</summary>
    private bool BayInUse(int slot) => !ParkingBays[slot].Tiles().All(t => Site[t] == TileKind.Parking) || _cars.Any(c => c.Trip == CarTrip.Parks && c.Slot == slot && c.State != CarState.Gone && c.State != CarState.Leaving);

    private bool StandInUse(int slot) => _cars.Any(c => c.Trip != CarTrip.Parks && c.Slot == slot && c.State is CarState.Arriving or CarState.Parked);

    /// <summary>A free space: a blue badge one for older patients if there is one, otherwise an ordinary one; -1 when full.</summary>
    private int FreeParkingBay(Patient patient)
    {
        var bays = ParkingBays;
        var badge = Site.Layout?.DisabledBays ?? 0;
        var free = Enumerable.Range(0, bays.Count).Where(i => !BayInUse(i)).ToList();
        if (patient.AgeAt(Now) >= BlueBadgeAge && free.Any(i => i < badge))
        {
            return free.First(i => i < badge);
        }

        var ordinary = free.Where(i => i >= badge).ToList();
        return ordinary.Count > 0 ? ordinary[_carRandom.Range(0, Math.Min(ordinary.Count, 3) - 1)] : -1;
    }

    private int FreeTaxiStand() => Enumerable.Range(0, TaxiStands.Count).FirstOrDefault(i => !StandInUse(i), -1);

    /// <summary>Where a car stops: the middle of its space, or its stand in the rank.</summary>
    private GridCoord StoppingTile(CarVehicle car)
    {
        if (car.Trip != CarTrip.Parks)
        {
            return TaxiStands[car.Slot];
        }

        var bay = ParkingBays[car.Slot];
        return new GridCoord(bay.X + (bay.Width / 2), bay.Y + (bay.Length / 2));
    }

    /// <summary>One step from the road towards the hospital, on the entrance side.</summary>
    private (int X, int Y) TowardsHospital => Site.Layout!.Plan.EntranceSide switch
    {
        WallSide.South => (0, -1),
        WallSide.North => (0, 1),
        WallSide.West => (1, 0),
        _ => (-1, 0),
    };

    /// <summary>Where the passenger gets out (and gets back in): beside the car, on the hospital side of a taxi.</summary>
    public GridCoord CarDoorTile(CarVehicle car)
    {
        var stop = StoppingTile(car);
        var (tx, ty) = TowardsHospital;
        if (car.Trip != CarTrip.Parks)
        {
            // Onto the pavement behind the rank.
            return new GridCoord(stop.X + (tx * 3), stop.Y + (ty * 3));
        }

        // Beside the car, at the edge of the space, towards its road end.
        var (dx, dy) = StarterSite.CarDirection(Site.Layout!.Plan.EntranceSide);
        var road = CarLane!.Beside(stop);
        return new GridCoord(stop.X + (dx * 2) + Math.Sign(road.X - stop.X), stop.Y + (dy * 2) + Math.Sign(road.Y - stop.Y));
    }

    /// <summary>
    /// Along the road to level with its space and straight in, nose first; or along the road and across
    /// into the rank a little before its stand, so it is straight by the time it stops.
    /// </summary>
    private void DriveIn(CarVehicle car)
    {
        var lane = CarLane!;
        var stop = StoppingTile(car);
        car.State = CarState.Arriving;
        car.Reversing = false;
        if (car.Trip == CarTrip.Parks)
        {
            var level = lane.Beside(stop);
            car.FollowPath(lane.Beside(car.Tile) == level && car.Tile != level
                ? StraightRoute(car.Tile, new[] { stop })
                : StraightRoute(car.Tile, new[] { lane.Beside(car.Tile), level, stop }));
            return;
        }

        var (dx, dy) = StarterSite.CarDirection(Site.Layout!.Plan.EntranceSide);
        var across = Math.Abs(lane.Beside(stop).X - stop.X) + Math.Abs(lane.Beside(stop).Y - stop.Y);
        var turnIn = lane.Beside(new GridCoord(stop.X - (dx * (across + 3)), stop.Y - (dy * (across + 3))));
        var ahead = lane.Along(car.Tile) < lane.Along(turnIn);
        car.FollowPath(ahead
            ? StraightRoute(car.Tile, new[] { lane.Beside(car.Tile), turnIn, new GridCoord(stop.X - (dx * 3), stop.Y - (dy * 3)), stop })
            : StraightRoute(car.Tile, new[] { stop }));
    }

    private void DriveAway(CarVehicle car)
    {
        var lane = CarLane!;
        car.StoppedAt = null;
        if (car.Trip == CarTrip.Parks && car.Tile != lane.Beside(car.Tile))
        {
            // Back out onto the road first; UpdateCars sends it on once it is there.
            car.State = CarState.BackingOut;
            car.Reversing = true;
            car.FollowPath(StraightRoute(car.Tile, new[] { lane.Beside(car.Tile) }));
            return;
        }

        car.State = CarState.Leaving;
        car.Reversing = false;
        var (dx, dy) = StarterSite.CarDirection(Site.Layout!.Plan.EntranceSide);
        var across = Math.Abs(lane.Beside(car.Tile).X - car.Tile.X) + Math.Abs(lane.Beside(car.Tile).Y - car.Tile.Y);
        var rejoin = lane.Beside(new GridCoord(car.Tile.X + (dx * (across + 3)), car.Tile.Y + (dy * (across + 3))));
        car.FollowPath(across == 0
            ? StraightRoute(car.Tile, new[] { lane.Exit })
            : StraightRoute(car.Tile, new[] { new GridCoord(car.Tile.X + (dx * 3), car.Tile.Y + (dy * 3)), rejoin, lane.Exit }));
    }

    private void UpdateCars()
    {
        foreach (var car in _cars)
        {
            switch (car.State)
            {
                case CarState.Arriving when !car.IsMoving:
                    car.State = CarState.Parked;
                    car.StoppedAt = Now;
                    if (car.Passenger is { } passenger)
                    {
                        car.Passenger = null;
                        AdmitNewPatient(passenger, CarDoorTile(car));
                        passenger.Log(Now, car.Trip switch
                        {
                            CarTrip.Taxi => "Came by taxi.",
                            CarTrip.DropOff => "Dropped off at the front by a relative.",
                            _ => "Drove here and parked in the car park.",
                        });
                    }

                    break;

                case CarState.Parked when car.Trip != CarTrip.Parks && Now - car.StoppedAt >= RankDwell:
                    DriveAway(car);
                    break;

                case CarState.Parked when car.Trip == CarTrip.Parks && OwnerFinished(car):
                    DriveAway(car);
                    break;

                case CarState.BackingOut when !car.IsMoving:
                    DriveAway(car);
                    break;

                case CarState.Leaving when !car.IsMoving:
                    car.State = CarState.Gone;
                    break;
            }
        }

        _cars.RemoveAll(c => c.State == CarState.Gone);
    }

    /// <summary>
    /// True when a parked car can go: its owner has got back to it and left, or has left some other way,
    /// or was admitted to a ward a while ago (a relative takes the car home).
    /// </summary>
    private bool OwnerFinished(CarVehicle car)
    {
        if (!_patients.TryGetValue(car.PatientId, out var owner) || owner.Stage == PatientStage.Gone)
        {
            return true;
        }

        return owner.Stage != PatientStage.Leaving && owner.LeftEmergencyAt is { } admitted && Now - admitted >= CollectedAfterAdmission;
    }

    /// <summary>The car a patient left in the car park, if it is still there.</summary>
    public CarVehicle? ParkedCarOf(Patient patient) =>
        _cars.FirstOrDefault(c => c.PatientId == patient.Id && c.Trip == CarTrip.Parks && c.State == CarState.Parked);

    /// <summary>Where a patient going home heads for: back to their car if they drove, otherwise out to the road.</summary>
    private GridCoord WayOut(Patient patient) =>
        ParkedCarOf(patient) is { } car && Nav.FindPath(patient.Tile, CarDoorTile(car)) is { Count: > 0 } ? CarDoorTile(car) : WalkInSpawn;
}
