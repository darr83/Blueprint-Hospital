using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Hospital;

/// <summary>
/// How lifts and stairs join the floors. A lift or staircase is one room per floor, all with the same
/// footprint; together they make a shaft. Costs are in tiles walked (a tile is 0.5 m, walked in about
/// 0.4 s), so A* weighs a ride against the walk it saves.
/// </summary>
public static class VerticalTransport
{
    /// <summary>Waiting for the lift to come and the doors to open and close.</summary>
    public const int LiftWait = 30;

    /// <summary>The lift travelling one floor.</summary>
    public const int LiftPerFloor = 6;

    /// <summary>Climbing (or going down) one floor of stairs.</summary>
    public const int StairsPerFloor = 24;

    /// <summary>The rooms of every lift and staircase, grouped into shafts (lowest floor first).</summary>
    public static IEnumerable<IReadOnlyList<CareSpace>> Shafts(SiteMap site, IEnumerable<CareSpace> spaces) =>
        spaces.Where(s => s.IsShaft)
            .GroupBy(s => (s.TemplateId, site.Local(s.Rect)))
            .Select(g => (IReadOnlyList<CareSpace>)g.OrderBy(s => site.FloorOf(s.Rect)).ToList());

    /// <summary>Links each shaft's floors in the walking grid: a lift goes straight to any floor, stairs one floor at a time.</summary>
    public static void AddLinks(NavGrid grid, SiteMap site, IEnumerable<CareSpace> spaces)
    {
        grid.MinLinkCostPerFloor = Math.Min(LiftPerFloor, StairsPerFloor);
        foreach (var shaft in Shafts(site, spaces))
        {
            var lift = shaft[0].Kind == CareSpaceKind.Lift;
            for (var i = 0; i < shaft.Count; i++)
            {
                for (var j = i + 1; j < shaft.Count; j++)
                {
                    var floors = site.FloorOf(shaft[j].Rect) - site.FloorOf(shaft[i].Rect);
                    if (lift)
                    {
                        grid.AddLink(shaft[i].PatientSpot, shaft[j].PatientSpot, LiftWait + (LiftPerFloor * floors), stepFree: true);
                    }
                    else if (floors == 1)
                    {
                        grid.AddLink(shaft[i].PatientSpot, shaft[j].PatientSpot, StairsPerFloor, stepFree: false);
                    }
                }
            }
        }
    }
}
