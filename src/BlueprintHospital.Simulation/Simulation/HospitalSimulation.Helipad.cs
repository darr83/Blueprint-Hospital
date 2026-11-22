using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.Transport;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// The helipad and the air ambulance. The player builds one square pad on open ground. An air ambulance casualty
/// then lands on it: the helicopter hovers down, its paramedic wheels the patient round to the ambulance entrance
/// and hands over to A&amp;E, then walks back and it lifts off. Without a helipad it lands in a field nearby and a
/// road ambulance brings the patient (and the paramedic) the last part, which takes longer.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>The helipad is 12 m square, the H inside a touchdown circle.</summary>
    public const int HelipadSize = 24;

    /// <summary>Open ground kept clear round the pad, so the rotors are well away from the walls.</summary>
    public const int HelipadClearance = 4;

    public const int HelipadCost = 150_000;

    /// <summary>Without a helipad the air ambulance lands this far away and a road ambulance brings the patient in.</summary>
    public static readonly TimeSpan NoHelipadDelay = TimeSpan.FromMinutes(8);

    /// <summary>The helicopter leaves after this long on the pad even if its crew are still inside.</summary>
    private static readonly TimeSpan LongestOnPad = TimeSpan.FromMinutes(45);

    private readonly List<HelicopterVehicle> _helicopters = new();

    private int _nextHelicopterId;

    private static readonly string[] AirCallSigns = { "Helimed 08", "Helimed 22", "Helimed 51", "Helimed 63", "Helimed 71" };

    /// <summary>The helipad, if one has been built.</summary>
    public GridRect? Helipad { get; private set; }

    /// <summary>Air ambulances over (or on) the site.</summary>
    public IReadOnlyList<HelicopterVehicle> Helicopters => _helicopters;

    internal HelicopterVehicle? HelicopterFor(int patientId) => _helicopters.FirstOrDefault(h => h.PatientId == patientId && h.State != HelicopterState.Gone);

    /// <summary>Still on board the air ambulance, flying in or landing.</summary>
    public bool InHelicopter(Patient patient) => HelicopterFor(patient.Id) is { State: HelicopterState.Inbound or HelicopterState.Landing };

    /// <summary>The middle of the pad, where the helicopter sits.</summary>
    public static GridCoord HelipadCentre(GridRect pad) => new(pad.X + (pad.Width / 2), pad.Y + (pad.Length / 2));

    /// <summary>The tile on the edge of the pad nearest the ambulance entrance, where the stretcher comes off.</summary>
    public GridCoord HelipadDoorTile(GridRect pad)
    {
        var target = AirHandoverSpot;
        var x = Math.Clamp(target.X, pad.X, pad.Right - 1);
        var y = Math.Clamp(target.Y, pad.Y, pad.Bottom - 1);
        return new GridCoord(x, y);
    }

    /// <summary>Just outside the ambulance entrance: where the air crew wait with the patient for the handover.</summary>
    public GridCoord AirHandoverSpot
    {
        get
        {
            var e = MainBuilding.Entrances.First(x => x.IsAmbulanceEntrance);
            var along = e.Side is WallSide.North or WallSide.South;
            var door = new GridCoord(e.Tile.X + (along ? (e.WidthTiles / 2) - 1 : 0), e.Tile.Y + (along ? 0 : (e.WidthTiles / 2) - 1));
            return Step(door, e.Side);
        }
    }

    // ---------------------------------------------------------------- building

    /// <summary>The square a helipad with its top-left corner at <paramref name="corner"/> would cover.</summary>
    public static GridRect HelipadAt(GridCoord corner) => new(corner.X, corner.Y, HelipadSize, HelipadSize);

    /// <summary>Whether a helipad can go with its top-left corner at <paramref name="corner"/> (on the ground floor).</summary>
    public BuildCheck CheckHelipad(GridCoord corner)
    {
        var pad = HelipadAt(corner);
        var problems = new List<string>();
        if (Helipad is not null)
        {
            problems.Add("The hospital already has a helipad. Click it to sell it first.");
            return new BuildCheck(HelipadCost, problems);
        }

        if (!IsRoomUnlocked(BuildMenu.Helipad))
        {
            problems.Add($"The helipad unlocks at level {LevelOfRoom(BuildMenu.Helipad)}.");
        }

        var around = new GridRect(pad.X - HelipadClearance, pad.Y - HelipadClearance, pad.Width + (2 * HelipadClearance), pad.Length + (2 * HelipadClearance));
        if (pad.X < 0 || pad.Y < 0 || pad.Right > Site.Width || pad.Bottom > Site.Length)
        {
            problems.Add("Must be inside the hospital site.");
            return new BuildCheck(HelipadCost, problems);
        }

        var kinds = pad.Tiles().Select(t => Site[t]).Where(k => k is not (TileKind.Grass or TileKind.Paving)).Distinct().ToList();
        foreach (var kind in kinds)
        {
            problems.Add(kind switch
            {
                TileKind.AmbulanceBay => "Can't land on the ambulance bays.",
                TileKind.Parking => "Can't land on the car park.",
                TileKind.Road or TileKind.TaxiRank => "Can't land on the road.",
                _ => "Can't land on the building.",
            });
        }

        if (kinds.Count == 0 && around.Tiles().Where(Site.InBounds).Any(t => Site[t] == TileKind.BuildingFloor))
        {
            problems.Add($"Too close to the building: keep {Units.TilesToMetres(HelipadClearance):0} m of open ground round the pad.");
        }

        if (MainBuilding.Entrances.Any(e => ApproachOf(e).Intersects(pad)))
        {
            problems.Add("Would block the way into an entrance.");
        }

        if (AmbulanceBayPatientTiles.Concat(AmbulanceParkingTiles).Append(WalkInSpawn).Any(pad.Contains))
        {
            problems.Add("Would block the ambulance bay.");
        }

        if (AgentsOn(pad, null).Any(a => !MainBuilding.Contains(a.Tile)))
        {
            problems.Add("Someone is standing there.");
        }

        if (!CanAfford(HelipadCost))
        {
            problems.Add($"Not enough money: costs £{HelipadCost:N0}, you have £{Balance:N0}.");
        }

        return new BuildCheck(HelipadCost, problems.Distinct().ToList());
    }

    public BuildCheck BuildHelipad(GridCoord corner)
    {
        var check = CheckHelipad(corner);
        if (!check.CanBuild)
        {
            return check;
        }

        var pad = HelipadAt(corner);
        Site.Fill(pad, TileKind.Helipad);
        Helipad = pad;
        Book(MoneyCategory.Construction, -check.Cost);
        Notifications.Add(Now, NotificationLevel.Info, $"Built a helipad for £{check.Cost:N0}. The air ambulance can now land at the hospital.");
        AfterLayoutChange();
        return check;
    }

    /// <summary>Whether the helipad can be taken away (not while the air ambulance is on it or coming in).</summary>
    public BuildCheck CheckRemoveHelipad()
    {
        if (Helipad is null)
        {
            return new BuildCheck(0, new[] { "There is no helipad." });
        }

        var problems = new List<string>();
        if (_helicopters.Any(h => h.State != HelicopterState.Gone))
        {
            problems.Add("The air ambulance is using it.");
        }

        return new BuildCheck(-(int)Math.Round(HelipadCost * DemolitionRefund), problems);
    }

    public BuildCheck RemoveHelipad()
    {
        var check = CheckRemoveHelipad();
        if (!check.CanBuild || Helipad is not { } pad)
        {
            return check;
        }

        Site.Fill(pad, TileKind.Grass);
        Helipad = null;
        Book(MoneyCategory.Construction, -check.Cost);
        Notifications.Add(Now, NotificationLevel.Info, $"Took the helipad out (refund £{-check.Cost:N0}).");
        AfterLayoutChange();
        return check;
    }

    // ---------------------------------------------------------------- flying

    /// <summary>Sends the air ambulance in with <paramref name="patient"/> on board, to land on the helipad.</summary>
    private HelicopterVehicle FlyIn(Patient patient, GridRect pad)
    {
        var centre = HelipadCentre(pad);

        // In over the back of the site (away from the road along the front), from well beyond the edge.
        var from = new GridCoord(centre.X < Site.Width / 2 ? -60 : Site.Width + 60, -50);
        var helicopter = new HelicopterVehicle
        {
            Id = HelicopterVehicle.IdOffset + _nextHelicopterId,
            CallSign = AirCallSigns[_nextHelicopterId % AirCallSigns.Length],
            PatientId = patient.Id,
            From = from,
        };
        _nextHelicopterId++;
        helicopter.PlaceAt(from);
        helicopter.FollowPath(new[] { centre });
        _helicopters.Add(helicopter);
        return helicopter;
    }

    private void UpdateHelicopters(double dt)
    {
        foreach (var helicopter in _helicopters)
        {
            helicopter.Move(dt);
            var patient = _patients.GetValueOrDefault(helicopter.PatientId);
            switch (helicopter.State)
            {
                case HelicopterState.Inbound when !helicopter.IsMoving:
                    helicopter.State = HelicopterState.Landing;
                    break;

                case HelicopterState.Landing:
                    helicopter.Altitude = Math.Max(0, helicopter.Altitude - (HelicopterVehicle.CruiseMetres / HelicopterVehicle.HoverSeconds * dt));
                    if (helicopter.Altitude <= 0)
                    {
                        helicopter.State = HelicopterState.Landed;
                        helicopter.LandedAt = Now;
                        Unload(helicopter, patient);
                    }

                    break;

                case HelicopterState.Landed:
                    helicopter.Rotor = Math.Max(0, helicopter.Rotor - (dt / HelicopterVehicle.SpinSeconds));
                    if (patient is not null && patient.Stage == PatientStage.InAmbulanceQueue && patient.TriagedAt is null && !patient.IsMoving
                        && patient.Tile == AirHandoverSpot && !HasTask(patient, TaskKind.AmbulanceHandover))
                    {
                        patient.QueuedSince = Now;
                        patient.Log(Now, "At the ambulance entrance with the air ambulance crew. Waiting for handover to A&E staff.");
                        AddTask(TaskKind.AmbulanceHandover, StaffRole.Nurse, patient, 10, "Air ambulance handover and triage");
                    }

                    var crewWithThem = Parents.Any(p => p.ChildId == helicopter.PatientId && p.Escort == EscortKind.AirCrew);
                    if ((helicopter.CrewAboard && !crewWithThem) || (patient is null && !crewWithThem) || Now - (helicopter.LandedAt ?? Now) > LongestOnPad)
                    {
                        helicopter.State = HelicopterState.TakingOff;
                    }

                    break;

                case HelicopterState.TakingOff:
                    helicopter.Rotor = Math.Min(1, helicopter.Rotor + (dt / HelicopterVehicle.SpinSeconds));
                    if (helicopter.Rotor >= 1)
                    {
                        helicopter.Altitude = Math.Min(HelicopterVehicle.CruiseMetres, helicopter.Altitude + (HelicopterVehicle.CruiseMetres / HelicopterVehicle.HoverSeconds * dt));
                        if (helicopter.Altitude >= HelicopterVehicle.CruiseMetres)
                        {
                            helicopter.State = HelicopterState.Outbound;
                            helicopter.FollowPath(new[] { helicopter.From });
                        }
                    }

                    break;

                case HelicopterState.Outbound when !helicopter.IsMoving:
                    helicopter.State = HelicopterState.Gone;
                    break;
            }
        }

        _helicopters.RemoveAll(h => h.State == HelicopterState.Gone);
    }

    /// <summary>On the pad: the crew lift the patient out onto their stretcher and wheel them round to the ambulance entrance.</summary>
    private void Unload(HelicopterVehicle helicopter, Patient? patient)
    {
        if (patient is null || patient.Stage != PatientStage.InAmbulanceQueue || Helipad is not { } pad)
        {
            helicopter.CrewAboard = true;
            return;
        }

        patient.PlaceAt(HelipadDoorTile(pad));
        UpdateMobility(patient);
        Stats.AmbulanceArrived();
        patient.Log(Now, $"Air ambulance {helicopter.CallSign} landed on the helipad.");
        Go(patient, AirHandoverSpot);
    }

    /// <summary>
    /// Brings an air ambulance casualty in: by helicopter onto the helipad if there is one, otherwise (after
    /// <see cref="NoHelipadDelay"/>, see the incidents file) by road from where it landed nearby.
    /// </summary>
    private void ArriveByAir(Patient patient)
    {
        patient.AirAmbulance = true;
        _patients[patient.Id] = patient;
        Stats.RecordArrival(patient);
        patient.Stage = PatientStage.InAmbulanceQueue;
        patient.AmbulanceHandover = BuildHandoverNote(patient);
        var helicopter = FlyIn(patient, Helipad!.Value);
        patient.Log(Now, $"Air ambulance {helicopter.CallSign} on its way to the helipad.");
        Notifications.Add(Now, NotificationLevel.Info, $"Air ambulance {helicopter.CallSign} landing: {Describe(patient)} with {Database.Symptoms[patient.PresentingComplaint].Name.ToLowerInvariant()}.", patient.Id);
        Events.Publish(new PatientArrived(patient));
    }

    // ---------------------------------------------------------------- saving

    internal List<HelicopterSave> SaveHelicopters() => _helicopters.Select(h => new HelicopterSave
    {
        Id = h.Id, CallSign = h.CallSign, PatientId = h.PatientId, State = h.State, Altitude = h.Altitude, Rotor = h.Rotor,
        LandedAt = h.LandedAt, CrewAboard = h.CrewAboard, FromX = h.From.X, FromY = h.From.Y, X = h.X, Y = h.Y,
    }).ToList();

    internal void RestoreHelicopters(IEnumerable<HelicopterSave>? saved, int nextId)
    {
        _helicopters.Clear();
        _nextHelicopterId = nextId;
        foreach (var s in saved ?? Enumerable.Empty<HelicopterSave>())
        {
            var helicopter = new HelicopterVehicle
            {
                Id = s.Id, CallSign = s.CallSign, PatientId = s.PatientId, From = new GridCoord(s.FromX, s.FromY),
                State = s.State, Altitude = s.Altitude, Rotor = s.Rotor, LandedAt = s.LandedAt, CrewAboard = s.CrewAboard,
            };
            helicopter.PlaceAt(new GridCoord((int)Math.Floor(s.X), (int)Math.Floor(s.Y)));
            if (Helipad is not { } pad && s.State is HelicopterState.Inbound or HelicopterState.Landing or HelicopterState.Landed)
            {
                // The pad has gone: it turns away.
                helicopter.State = HelicopterState.Outbound;
                helicopter.Altitude = HelicopterVehicle.CruiseMetres;
                helicopter.Rotor = 1;
                helicopter.FollowPath(new[] { helicopter.From });
            }
            else if (s.State == HelicopterState.Inbound)
            {
                helicopter.FollowPath(new[] { HelipadCentre(Helipad!.Value) });
            }
            else if (s.State == HelicopterState.Outbound)
            {
                helicopter.FollowPath(new[] { helicopter.From });
            }

            _helicopters.Add(helicopter);
            if (helicopter.State == HelicopterState.Outbound && _patients.TryGetValue(s.PatientId, out var aboard) && aboard.Stage == PatientStage.InAmbulanceQueue && s.State is HelicopterState.Inbound or HelicopterState.Landing)
            {
                // Its patient comes the rest of the way by road.
                DispatchAmbulance(aboard, AmbulancePurpose.Arrival);
            }
        }
    }
}

/// <summary>An air ambulance on the site, as saved.</summary>
public sealed class HelicopterSave
{
    public int Id { get; init; }

    public string CallSign { get; init; } = string.Empty;

    public int PatientId { get; init; }

    public HelicopterState State { get; init; }

    public double Altitude { get; init; }

    public double Rotor { get; init; }

    public DateTime? LandedAt { get; init; }

    public bool CrewAboard { get; init; }

    public int FromX { get; init; }

    public int FromY { get; init; }

    public double X { get; init; }

    public double Y { get; init; }
}
