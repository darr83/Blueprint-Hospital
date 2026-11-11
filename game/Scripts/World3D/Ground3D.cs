using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.World;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>
/// The grounds: grass, paths, roads, parking and the ambulance bay, as one vertex-coloured mesh.
/// When an upper floor is in view the grounds fade under a pale wash, with the building's slab on top.
/// </summary>
public partial class Ground3D : Node3D
{
    private HospitalSimulation _sim = null!;

    public void Initialise(HospitalSimulation simulation) => _sim = simulation;

    public void Rebuild()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }

        var site = _sim.Site;
        var ground = new MeshBatch();
        foreach (var tile in site.Bounds.Tiles())
        {
            ground.Floor(tile.X, tile.Y, 1, 1, 0, SiteRenderer.TileColour(site, tile));
        }

        ground.AddTo(this, shadow: false);

        var marks = new MeshBatch();
        if (IsoDraw.ViewFloor == 0)
        {
            // Entrance mats: blue at the front doors, red at the ambulance doors.
            foreach (var entrance in site.Buildings.SelectMany(b => b.Entrances))
            {
                var colour = entrance.IsAmbulanceEntrance ? new Color("#E53935") : new Color("#005EB8");
                var (x, y, w, l) = entrance.Side switch
                {
                    WallSide.North or WallSide.South => (entrance.Tile.X, entrance.Tile.Y, entrance.WidthTiles, 1),
                    _ => (entrance.Tile.X, entrance.Tile.Y, 1, entrance.WidthTiles),
                };
                marks.Floor(x, y, w, l, 0.006f, colour.Lerp(SiteRenderer.TileColour(site, entrance.Tile), 0.45f));
            }
        }
        else
        {
            // Upstairs: the grounds below fade, and the floor slab is the building's outline.
            var wash = new MeshBatch();
            wash.Floor(0, 0, site.Width, site.Length, 0.01f, SiteRenderer.UpperFloorGround);
            wash.AddTo(this, alpha: true, shadow: false);
            foreach (var r in site.Buildings.SelectMany(b => b.Areas))
            {
                marks.Floor(r.X, r.Y, r.Width, r.Length, 0.02f, SiteRenderer.UpperFloorSlab);
            }
        }

        marks.AddTo(this, shadow: false);
    }
}
