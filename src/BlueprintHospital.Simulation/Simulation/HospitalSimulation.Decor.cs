using BlueprintHospital.Simulation.Hospital;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>Floor and wall finishes: what each room is decorated with, what redecorating costs, and the look new rooms start with.</summary>
public sealed partial class HospitalSimulation
{
    /// <summary>Each room's finish, by room id. Rooms not listed have the standard finish.</summary>
    private readonly Dictionary<string, RoomDecor> _decor = new();

    /// <summary>The look the player chose for new rooms of a template, by template id.</summary>
    private readonly Dictionary<string, RoomDecor> _decorDefaults = new();

    /// <summary>A room's floor and wall finish.</summary>
    public RoomDecor DecorOf(CareSpace space) => _decor.GetValueOrDefault(space.Id, RoomDecor.Standard);

    /// <summary>The look a new room built from this template gets: the player's choice for that kind of room, or its usual one.</summary>
    public RoomDecor NewRoomDecor(string templateId) =>
        _decorDefaults.TryGetValue(templateId, out var chosen) ? chosen
        : Database.RoomTemplates.TryGetValue(templateId, out var template) ? DecorCatalogue.StartingLook(template.Kind)
        : RoomDecor.Standard;

    /// <summary>True if the player has chosen the look for new rooms of this template.</summary>
    public bool HasDecorDefault(string templateId) => _decorDefaults.ContainsKey(templateId);

    /// <summary>What it costs to change a room to this finish: each part that changes is charged by the room's floor area.</summary>
    public int DecorateCost(CareSpace space, RoomDecor decor)
    {
        var now = DecorOf(space);
        var area = space.AreaSquareMetres;
        var cost = 0.0;
        if (now.Floor != decor.Floor || now.FloorColour != decor.FloorColour)
        {
            cost += area * DecorCatalogue.PricePerSquareMetre(decor.Floor);
        }

        if (space.HasWalls && (now.Walls != decor.Walls || now.WallColour != decor.WallColour))
        {
            cost += area * DecorCatalogue.PricePerSquareMetre(decor.Walls);
        }

        return (int)Math.Round(cost);
    }

    /// <summary>
    /// Redecorates a room, paying for it from the budget. With <paramref name="forNewRooms"/> the same
    /// look is also used for every room of this type built from now on.
    /// </summary>
    public BuildCheck Decorate(string roomId, RoomDecor decor, bool forNewRooms = false)
    {
        if (FindSpace(roomId) is not { } space)
        {
            return new BuildCheck(0, new[] { "That room no longer exists." });
        }

        if (!DecorCatalogue.CanDecorate(space))
        {
            return new BuildCheck(0, new[] { "Lifts and stairs can't be decorated." });
        }

        if (!space.HasWalls)
        {
            decor = decor with { Walls = WallFinish.Standard, WallColour = null };
        }

        var cost = DecorateCost(space, decor);
        if (!CanAfford(cost))
        {
            return new BuildCheck(cost, new[] { $"Not enough money: redecorating costs £{cost:N0}." });
        }

        if (decor.IsStandard)
        {
            _decor.Remove(space.Id);
        }
        else
        {
            _decor[space.Id] = decor;
        }

        if (forNewRooms)
        {
            _decorDefaults[space.TemplateId] = decor;
        }

        if (cost > 0)
        {
            Book(MoneyCategory.Construction, -cost);
            Notifications.Add(Now, NotificationLevel.Info, $"Redecorated {space.Name} for £{cost:N0}.");
        }

        Events.Publish(new LayoutChanged());
        return new BuildCheck(cost, Array.Empty<string>(), space);
    }

    /// <summary>Gives a newly built room the look for its type, free with the build.</summary>
    private void DecorateNewRoom(CareSpace room)
    {
        var decor = DecorCatalogue.CanDecorate(room) ? NewRoomDecor(room.TemplateId) : RoomDecor.Standard;
        if (!room.HasWalls)
        {
            decor = decor with { Walls = WallFinish.Standard, WallColour = null };
        }

        if (decor.IsStandard)
        {
            _decor.Remove(room.Id);
        }
        else
        {
            _decor[room.Id] = decor;
        }
    }

    private Dictionary<string, RoomDecor> CaptureDecor() =>
        _decor.Where(d => _spaces.ContainsKey(d.Key)).OrderBy(d => d.Key, StringComparer.Ordinal).ToDictionary(d => d.Key, d => d.Value);

    private void RestoreDecor(Dictionary<string, RoomDecor>? decor, Dictionary<string, RoomDecor>? defaults)
    {
        // Older saves have no finishes: every room keeps the standard look it had.
        _decor.Clear();
        foreach (var (id, look) in decor ?? new Dictionary<string, RoomDecor>())
        {
            if (_spaces.ContainsKey(id) && !look.IsStandard)
            {
                _decor[id] = look;
            }
        }

        _decorDefaults.Clear();
        foreach (var (template, look) in defaults ?? new Dictionary<string, RoomDecor>())
        {
            if (Database.RoomTemplates.ContainsKey(template))
            {
                _decorDefaults[template] = look;
            }
        }
    }
}
