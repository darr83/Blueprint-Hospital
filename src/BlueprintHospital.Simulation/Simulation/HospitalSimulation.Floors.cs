using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// Upper floors, lifts and stairs. The player pays to add a floor; it has the ground floor's outline.
/// Lifts and stairs are built through every floor at once (one room per floor, the same footprint),
/// and people change floor by them: wheelchairs and trolleys by lift only.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>Building the first floor up, per square metre of the building's outline.</summary>
    public const int FloorCostPerSquareMetre = 500;

    /// <summary>Each floor higher costs this much more per square metre.</summary>
    public const int FloorCostRisePerSquareMetre = 100;

    /// <summary>Floors in the building, counting the ground floor.</summary>
    public int FloorCount => Site.FloorCount;

    /// <summary>Which floor a tile is on (0 = ground).</summary>
    public int FloorOf(GridCoord tile) => Site.FloorOf(tile);

    public int FloorOf(CareSpace space) => Site.FloorOf(space.Rect);

    public int FloorOf(Agent agent) => Site.FloorOf(agent.Y);

    /// <summary>Every lift and staircase, each as its rooms from the lowest floor up.</summary>
    public IEnumerable<IReadOnlyList<CareSpace>> Shafts => VerticalTransport.Shafts(Site, _spaces.Values);

    /// <summary>All the rooms of the lift or staircase <paramref name="space"/> belongs to (just the room itself otherwise).</summary>
    public IReadOnlyList<CareSpace> ShaftOf(CareSpace space) =>
        space.IsShaft
            ? _spaces.Values.Where(s => s.TemplateId == space.TemplateId && Site.Local(s.Rect) == Site.Local(space.Rect)).OrderBy(FloorOf).ToList()
            : new[] { space };

    /// <summary>Patients are brought to this room in wheelchairs and on trolleys, so upstairs it needs a lift.</summary>
    private static bool NeedsLift(CareSpace space) => space.Kind is not (CareSpaceKind.Corridor or CareSpaceKind.Stairs
        or CareSpaceKind.NursesStation or CareSpaceKind.DoctorsOffice or CareSpaceKind.StaffRoom or CareSpaceKind.OutreachOffice or CareSpaceKind.Pharmacy);

    /// <summary>A walking route for anyone; wheelchairs and trolleys go by lift (falling back to any route if there is none).</summary>
    public IReadOnlyList<GridCoord> PathFor(Agent agent, GridCoord destination)
    {
        var wheeled = agent is Patient { Mobility: not Mobility.Walking } || (agent is Staff.StaffMember { PushingPatientId: not null });
        if (wheeled && Site.FloorCount > 1)
        {
            var stepFree = Nav.FindPath(agent.Tile, destination, stepFreeOnly: true);
            if (stepFree.Count > 0)
            {
                return stepFree;
            }
        }

        return Nav.FindPath(agent.Tile, destination);
    }

    // ------------------------------------------------------------------ floors

    /// <summary>What adding the next floor costs: its floor, plus every lift and staircase carried up through it.</summary>
    public int NextFloorCost()
    {
        var perSquareMetre = FloorCostPerSquareMetre + (FloorCostRisePerSquareMetre * (Site.FloorCount - 1));
        var shafts = Shafts.Sum(s => s[0].BuildCost);
        return (int)Math.Round(MainBuilding.AreaSquareMetres * perSquareMetre) + shafts;
    }

    public BuildCheck CheckAddFloor()
    {
        var problems = new List<string>();
        var cost = NextFloorCost();
        if (Site.FloorCount > SiteMap.MaxUpperFloors)
        {
            problems.Add($"The building can be at most {SiteMap.MaxUpperFloors + 1} floors high.");
        }

        if (!CanAfford(cost))
        {
            problems.Add($"Not enough money: costs £{cost:N0}, you have £{Balance:N0}.");
        }

        return new BuildCheck(cost, problems);
    }

    /// <summary>Builds a new top floor with the ground floor's outline, and carries every lift and staircase up to it.</summary>
    public BuildCheck AddFloor()
    {
        var check = CheckAddFloor();
        if (!check.CanBuild)
        {
            return check;
        }

        var shafts = Shafts.ToList();
        Site.AddFloor();
        var floor = Site.FloorCount - 1;
        foreach (var shaft in shafts)
        {
            var below = shaft[^1];
            var template = Database.RoomTemplates[below.TemplateId];
            var door = below.Doors.FirstOrDefault();
            var plan = ShaftPlan(below, door, floor);
            var room = Furnish(plan, NewRoomId(template), below.Name);
            _spaces[room.Id] = room;
        }

        Book(MoneyCategory.Construction, -check.Cost);
        Notifications.Add(Now, NotificationLevel.Info, $"Built the {SiteMap.FloorName(floor).ToLowerInvariant()} for £{check.Cost:N0}.");
        AfterLayoutChange();
        return check;
    }

    /// <summary>The plan for a lift or staircase room on <paramref name="floor"/>, matching <paramref name="room"/> on another floor.</summary>
    private RoomPlan ShaftPlan(CareSpace room, Doorway? door, int floor)
    {
        var rect = Site.OnFloor(Site.Local(room.Rect), floor);
        if (door is null)
        {
            return new RoomPlan(room.TemplateId, rect, room.DoorSide);
        }

        // Centre the door on its own doorway, so it lands in the same place as on the floor below.
        var plan = new RoomPlan(room.TemplateId, rect, room.DoorSide, door.Offset, door.Width);
        if (Furnish(plan).Doors.FirstOrDefault() == door)
        {
            return plan;
        }

        var (w, _) = RoomLayoutEngine.LocalSize(room.Rect, room.DoorSide);
        return plan with { DoorStart = w - door.Offset - door.Width };
    }

    // ------------------------------------------------------------------ lifts and stairs

    /// <summary>A lift or staircase goes through every floor: each floor must have room for it, and its door must open onto a corridor somewhere.</summary>
    private BuildCheck CheckShaft(RoomTemplate template, RoomPlan plan, string? replacingId)
    {
        var problems = new List<string>();
        if (replacingId is not null)
        {
            problems.Add($"{template.Name}s can't be moved. Demolish it and build a new one.");
            return new BuildCheck(0, problems);
        }

        var rooms = Enumerable.Range(0, Site.FloorCount)
            .Select(f => Furnish(plan with { Rect = Site.OnFloor(Site.Local(plan.Rect), f) }))
            .ToList();
        var cost = rooms.Sum(r => r.BuildCost);

        CheckSize(template, plan, problems);
        if (!rooms.All(r => r.Rect.Tiles().All(Site.IsIndoors)))
        {
            problems.Add("Must be inside the building. Extend the building first.");
        }

        foreach (var room in rooms)
        {
            if (_spaces.Values.FirstOrDefault(s => s.Rect.Intersects(room.Rect)) is { } overlap)
            {
                var where = Site.FloorCount > 1 ? $" on the {SiteMap.FloorName(FloorOf(room)).ToLowerInvariant()}" : string.Empty;
                problems.Add($"Overlaps {overlap.Name}{where}. A {template.Name.ToLowerInvariant()} needs the same space clear on every floor.");
                break;
            }
        }

        if (rooms.Any(r => AgentsOn(r.Rect, null).Any()))
        {
            problems.Add("Someone is standing there.");
        }

        // The door must open onto a corridor on at least one floor; on the others it waits for one to be laid.
        var doorProblems = rooms.Select(r =>
        {
            var list = new List<string>();
            CheckDoors(r, null, list);
            return list;
        }).ToList();
        if (doorProblems.All(p => p.Count > 0))
        {
            problems.Add("Must touch a corridor so it can have a door.");
        }

        if (problems.Count == 0)
        {
            problems.AddRange(CheckReachable(_spaces.Values.Concat(rooms).ToList()));
        }

        if (!CanAfford(cost))
        {
            problems.Add($"Not enough money: costs £{cost:N0}, you have £{Balance:N0}.");
        }

        var here = rooms.FirstOrDefault(r => FloorOf(r) == Site.FloorOf(plan.Rect)) ?? rooms[0];
        return new BuildCheck(cost, problems, here);
    }

    private void BuildShaft(RoomTemplate template, RoomPlan plan, string name, int cost)
    {
        for (var floor = 0; floor < Site.FloorCount; floor++)
        {
            var room = Furnish(plan with { Rect = Site.OnFloor(Site.Local(plan.Rect), floor) }, NewRoomId(template), name);
            _spaces[room.Id] = room;
        }

        Book(MoneyCategory.Construction, -cost);
        var floors = Site.FloorCount > 1 ? $" through all {Site.FloorCount} floors" : string.Empty;
        Notifications.Add(Now, NotificationLevel.Info, $"Built {name}{floors} for £{cost:N0}.");
        AfterLayoutChange();
    }
}
