using BlueprintHospital.Simulation.Data;
using BlueprintHospital.Simulation.Hospital;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>How many of each build bar item the hospital has, for the count badge on each tile.</summary>
public sealed partial class HospitalSimulation
{
    /// <summary>
    /// How many of a build bar item are built: rooms of that type (a lift or staircase counts once,
    /// not once per floor), waiting chairs, resus trolleys, reception desks, waiting room doors,
    /// building extensions or floors added above the ground floor.
    /// </summary>
    public int BuiltCount(string item)
    {
        switch (item)
        {
            case BuildMenu.Helipad:
                return Helipad is null ? 0 : 1;
            case BuildMenu.ExtendBuilding:
                return MainBuilding.Extensions.Count;
            case BuildMenu.AddFloor:
                return FloorCount - 1;
            case BuildMenu.WaitingChairs:
                return _chairs.Count;
            case BuildMenu.ResusTrolleys:
                return _trolleys.Count;
            case BuildMenu.WaitingDesk:
                return SpacesOfKind(CareSpaceKind.Waiting).Count(r => FittingsOf(r).Desk is not null);
            case BuildMenu.WaitingDoors:
                return SpacesOfKind(CareSpaceKind.Waiting).Sum(r => FittingsOf(r).Doors.Count);
        }

        if (item.StartsWith(DecorItems.TilePrefix, StringComparison.Ordinal))
        {
            var id = item[DecorItems.TilePrefix.Length..];
            return _decorations.Count(d => d.ItemId == id);
        }

        var rooms = _spaces.Values.Where(s => s.TemplateId == item);
        return rooms.Any(r => r.IsShaft)
            ? VerticalTransport.Shafts(Site, rooms).Count()
            : rooms.Count();
    }
}
