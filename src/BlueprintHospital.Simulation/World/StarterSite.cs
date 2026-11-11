namespace BlueprintHospital.Simulation.World;

/// <summary>Builds the site layout a new game starts with: one hospital building, roads, parking and an ambulance bay.</summary>
public static class StarterSite
{
    public const int SiteWidth = 160;   // 80 m
    public const int SiteLength = 120;  // 60 m

    /// <summary>The main hospital building: 30 m x 20 m.</summary>
    public static readonly GridRect MainBuildingFootprint = new(40, 30, 60, 40);

    /// <summary>How many ambulances can park at the ambulance entrance at once.</summary>
    public const int AmbulanceBays = 4;

    /// <summary>Each bay is 3.5 m wide (the ambulance is 2.7 m across)...</summary>
    public const int BayWidth = 7;

    /// <summary>...and 6.5 m deep (the ambulance is 5.8 m long).</summary>
    public const int BayLength = 13;

    /// <summary>The bays start this far out from the building wall, leaving a path for the stretchers.</summary>
    public const int BayOffset = 3;

    /// <summary>The side road starts this far out from the building wall, just past the bays.</summary>
    public const int RoadOffset = BayOffset + BayLength;

    public static SiteMap Create()
    {
        var site = new SiteMap(SiteWidth, SiteLength);
        var b = MainBuildingFootprint;

        // Access road along the south side of the site, and the east side road the ambulances use.
        site.Fill(new GridRect(0, b.Bottom + 14, SiteWidth, 10), TileKind.Road);
        site.Fill(new GridRect(b.Right + RoadOffset, 0, 10, SiteLength), TileKind.Road);

        // Paved apron around the building.
        site.Fill(new GridRect(b.X - 4, b.Y - 4, b.Width + 8, b.Length + 8), TileKind.Paving);

        // Front path from the main entrance down to the road.
        site.Fill(new GridRect(b.X + 22, b.Bottom + 4, 8, 10), TileKind.Paving);

        // Car park to the south-west.
        site.Fill(new GridRect(b.X - 4, b.Bottom + 4, 24, 10), TileKind.Parking);

        // Ambulance bays to the east, beside the ambulance entrance, opening onto the side road.
        var door = b.Y + 9 + 4;
        site.Fill(new GridRect(b.Right + BayOffset, door - (AmbulanceBays * BayWidth / 2), BayLength, AmbulanceBays * BayWidth), TileKind.AmbulanceBay);

        var building = new Building("main", "Main Hospital Building", b);
        building.AddEntrance(new Entrance("main_entrance", "Main entrance", new GridCoord(b.X + 22, b.Bottom - 1), WallSide.South, 8, false));
        building.AddEntrance(new Entrance("ambulance_entrance", "Ambulance entrance", new GridCoord(b.Right - 1, b.Y + 9), WallSide.East, 8, true));
        site.AddBuilding(building);

        return site;
    }
}
