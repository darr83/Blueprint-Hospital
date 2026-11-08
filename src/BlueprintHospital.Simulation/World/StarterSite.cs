namespace BlueprintHospital.Simulation.World;

/// <summary>Builds the site layout a new game starts with: one hospital building, roads, parking and an ambulance bay.</summary>
public static class StarterSite
{
    public const int SiteWidth = 160;   // 80 m
    public const int SiteLength = 120;  // 60 m

    /// <summary>The main hospital building: 30 m x 20 m.</summary>
    public static readonly GridRect MainBuildingFootprint = new(40, 30, 60, 40);

    public static SiteMap Create()
    {
        var site = new SiteMap(SiteWidth, SiteLength);
        var b = MainBuildingFootprint;

        // Access road along the south side of the site and up the east side.
        site.Fill(new GridRect(0, b.Bottom + 14, SiteWidth, 10), TileKind.Road);
        site.Fill(new GridRect(b.Right + 14, 0, 10, b.Bottom + 14), TileKind.Road);

        // Paved apron around the building.
        site.Fill(new GridRect(b.X - 4, b.Y - 4, b.Width + 8, b.Length + 8), TileKind.Paving);

        // Front path from the main entrance down to the road.
        site.Fill(new GridRect(b.X + 22, b.Bottom + 4, 8, 10), TileKind.Paving);

        // Car park to the south-west.
        site.Fill(new GridRect(b.X - 4, b.Bottom + 4, 24, 10), TileKind.Parking);

        // Ambulance bay to the east, joined to the side road.
        site.Fill(new GridRect(b.Right + 4, b.Y + 6, 10, 14), TileKind.AmbulanceBay);

        var building = new Building("main", "Main Hospital Building", b);
        building.AddEntrance(new Entrance("main_entrance", "Main entrance", new GridCoord(b.X + 22, b.Bottom - 1), WallSide.South, 8, false));
        building.AddEntrance(new Entrance("ambulance_entrance", "Ambulance entrance", new GridCoord(b.Right - 1, b.Y + 9), WallSide.East, 8, true));
        site.AddBuilding(building);

        return site;
    }
}
